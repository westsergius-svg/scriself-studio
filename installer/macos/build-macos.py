"""Publish self-contained macOS apps and ZIPs on Windows, macOS or Linux (Python 3.9+)."""

from datetime import datetime, timezone
from pathlib import Path
import argparse
import hashlib
import json
import os
import plistlib
import shutil
import stat
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
APP_NAME = "ScriSelf Studio"
EXECUTABLE = "ScriSelfStudio"
RUNTIMES = ("osx-arm64", "osx-x64")
CPU_TYPES = {0x0100000C: "osx-arm64", 0x01000007: "osx-x64"}
REQUIRED_NATIVE = (
    "libAvaloniaNative.dylib", "libSkiaSharp.dylib", "libHarfBuzzSharp.dylib",
    "libe_sqlite3.dylib", "libcoreclr.dylib", "libhostfxr.dylib", "libhostpolicy.dylib",
)


def macho_architectures(path):
    """Read thin and universal Mach-O headers without running the macOS executable."""
    with path.open("rb") as source:
        header = source.read(8)
        thin = {b"\xcf\xfa\xed\xfe": "<", b"\xfe\xed\xfa\xcf": ">"}
        fat = {
            b"\xca\xfe\xba\xbe": (">", 20), b"\xbe\xba\xfe\xca": ("<", 20),
            b"\xca\xfe\xba\xbf": (">", 32), b"\xbf\xba\xfe\xca": ("<", 32),
        }
        if header[:4] in thin:
            return {CPU_TYPES.get(struct.unpack(thin[header[:4]] + "I", header[4:])[0], "unknown")}
        if header[:4] in fat:
            endian, entry_size = fat[header[:4]]
            count = struct.unpack(endian + "I", header[4:])[0]
            if not 0 < count <= 16:
                raise ValueError(f"Invalid Mach-O architecture count: {path}")
            result = set()
            for _ in range(count):
                entry = source.read(entry_size)
                if len(entry) != entry_size:
                    raise ValueError(f"Truncated Mach-O header: {path}")
                result.add(CPU_TYPES.get(struct.unpack(endian + "I", entry[:4])[0], "unknown"))
            return result
    raise ValueError(f"Not a 64-bit macOS executable/library: {path}")


def validate_publish(directory, runtime):
    for name in (EXECUTABLE, EXECUTABLE + ".dll", EXECUTABLE + ".deps.json",
                 EXECUTABLE + ".runtimeconfig.json", *REQUIRED_NATIVE):
        if not (directory / name).is_file():
            raise ValueError(f"Missing published dependency: {directory / name}")
    for path in [directory / EXECUTABLE, *sorted(directory.rglob("*.dylib"))]:
        if runtime not in macho_architectures(path):
            raise ValueError(f"Wrong native architecture for {runtime}: {path}")
    configuration = json.loads((directory / (EXECUTABLE + ".runtimeconfig.json")).read_text())
    if "includedFrameworks" not in configuration["runtimeOptions"]:
        raise ValueError("The macOS app must include its .NET runtime")


def make_bundle(publish_directory, destination, version):
    contents = destination / "Contents"
    resources = contents / "Resources"
    resources.mkdir(parents=True, exist_ok=False)
    shutil.copytree(publish_directory, contents / "MacOS")
    icon = (ROOT / "src/ScriSelfStudio.App/Assets/scriself.png").read_bytes()
    if icon[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", icon[16:24]) != (256, 256):
        raise ValueError("The application icon must be a 256x256 PNG; update ICNS chunk type if its size changes")
    # Modern ICNS accepts PNG chunks: ic08 is the existing 256x256 window icon.
    (resources / "scriself.icns").write_bytes(
        b"icns" + struct.pack(">I", len(icon) + 16) + b"ic08" + struct.pack(">I", len(icon) + 8) + icon)
    shutil.copy2(ROOT / "src/ScriSelfStudio.Presentation/Assets/Fonts/FluentSystemIcons-LICENSE.txt", resources)
    metadata = {
        "CFBundleName": APP_NAME, "CFBundleDisplayName": APP_NAME,
        "CFBundleIdentifier": "ru.scriborium.scriselfstudio",
        "CFBundleExecutable": EXECUTABLE, "CFBundleIconFile": "scriself.icns",
        "CFBundleVersion": version, "CFBundleShortVersionString": version,
        "CFBundleInfoDictionaryVersion": "6.0", "CFBundlePackageType": "APPL",
        "LSMinimumSystemVersion": "15.0", "NSHighResolutionCapable": True,
        "CFBundleDocumentTypes": [{
            "CFBundleTypeName": "ScriSelf Studio Project", "CFBundleTypeRole": "Editor",
            "LSHandlerRank": "Owner", "LSItemContentTypes": ["ru.scriborium.scri-project"],
        }],
        "UTExportedTypeDeclarations": [{
            "UTTypeIdentifier": "ru.scriborium.scri-project",
            "UTTypeDescription": "ScriSelf Studio Project", "UTTypeConformsTo": ["public.data"],
            "UTTypeTagSpecification": {"public.filename-extension": ["scri"]},
        }],
    }
    with (contents / "Info.plist").open("wb") as output:
        plistlib.dump(metadata, output, sort_keys=False)
    if sys.platform != "win32":
        (contents / "MacOS" / EXECUTABLE).chmod(0o755)


def add_zip_entry(archive, path, name):
    info = zipfile.ZipInfo.from_file(path, name)
    info.create_system = 3  # UNIX: Finder must preserve the executable bit even for a Windows build.
    if path.is_dir():
        info.external_attr = ((stat.S_IFDIR | 0o755) << 16) | 0x10
        archive.writestr(info, b"")
        return
    executable = path.name == EXECUTABLE or path.suffix == ".dylib"
    info.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    with path.open("rb") as source, archive.open(info, "w", force_zip64=True) as target:
        shutil.copyfileobj(source, target, length=1024 * 1024)


def write_archive(bundle, readme, archive_path):
    with zipfile.ZipFile(archive_path, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in [bundle, *sorted(bundle.rglob("*"))]:
            add_zip_entry(archive, path, path.relative_to(bundle.parent).as_posix())
        add_zip_entry(archive, readme, "README-macOS.txt")
    with zipfile.ZipFile(archive_path) as archive:
        main = archive.getinfo(f"{bundle.name}/Contents/MacOS/{EXECUTABLE}")
        if main.create_system != 3 or (main.external_attr >> 16) & 0o111 != 0o111:
            raise ValueError("The ZIP did not preserve macOS executable permissions")
        corrupted = archive.testzip()
        if corrupted:
            raise ValueError(f"Corrupted ZIP entry: {corrupted}")


def write_update_manifest(archive, version, runtime, output, signing_key=None):
    with archive.open("rb") as source:
        hasher = hashlib.sha256()
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            hasher.update(chunk)
        digest = hasher.hexdigest()
    manifest = output / f"latest-{runtime}.json"
    notes = "Предварительная версия для macOS."
    if signing_key:
        subprocess.run([
            "dotnet", "run", "--project", str(ROOT / "installer/ScriSelfStudio.UpdateManifest/ScriSelfStudio.UpdateManifest.csproj"),
            "--configuration", "Release", "--no-launch-profile", "--",
            "--file", str(archive), "--output", str(manifest), "--version", version,
            "--runtime", runtime, "--notes", notes, "--signing-key", str(signing_key),
        ], cwd=ROOT, check=True)
        signed = json.loads(manifest.read_text(encoding="utf-8"))
        if signed["sha256"] != digest:
            raise ValueError("The archive changed while the update manifest was being signed")
    else:
        manifest.write_text(json.dumps({
            "version": version, "file": archive.name, "runtime": runtime,
            "notes": notes, "sha256": digest,
        }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return digest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", choices=RUNTIMES, nargs="+", default=list(RUNTIMES))
    parser.add_argument("--output", type=Path, help="A NEW output directory; previous builds are never deleted")
    parser.add_argument("--sign-identity", help="macOS only: '-' for ad-hoc signing, or a Developer ID identity")
    parser.add_argument("--update-signing-key", type=Path, help="Private RSA release key for the update manifest; separate from Apple code signing")
    args = parser.parse_args()
    if args.sign_identity and sys.platform != "darwin":
        parser.error("Code signing requires macOS; cross-published Windows builds remain unsigned")
    signing_key = args.update_signing_key
    if signing_key is None and os.environ.get("SCRISELF_UPDATE_SIGNING_KEY_PATH"):
        signing_key = Path(os.environ["SCRISELF_UPDATE_SIGNING_KEY_PATH"])
    if signing_key is not None and not signing_key.is_file():
        parser.error("The update signing key does not exist")
    if signing_key is None and os.environ.get("LOCALAPPDATA"):
        candidate = Path(os.environ["LOCALAPPDATA"]) / "ScriSelfStudioReleaseKeys/updates-rsa.pem"
        if candidate.is_file():
            signing_key = candidate
    if signing_key is not None:
        signing_key = signing_key.resolve()
    version = ET.parse(ROOT / "Directory.Build.props").findtext("./PropertyGroup/Version")
    if not version:
        raise ValueError("Missing application version in Directory.Build.props")
    output = (args.output or ROOT / "artifacts/macos" / version).resolve()
    output.mkdir(parents=True, exist_ok=False)
    timestamp = datetime.now(timezone.utc).isoformat()
    for runtime in dict.fromkeys(args.runtime):
        published = output / "publish" / runtime
        subprocess.run([
            "dotnet", "publish", str(ROOT / "src/ScriSelfStudio.App/ScriSelfStudio.App.csproj"),
            "--configuration", "Release", "--runtime", runtime, "--self-contained", "true",
            "-p:UseAppHost=true", "-p:PublishSingleFile=false", "-p:PublishTrimmed=false",
            "-p:PublishReadyToRun=false", "-p:IncludeNativeLibrariesForSelfExtract=false",
            "--output", str(published), "--nologo",
        ], cwd=ROOT, check=True)
        validate_publish(published, runtime)
        bundle = output / runtime / (APP_NAME + ".app")
        make_bundle(published, bundle, version)
        signing = "unsigned"
        if args.sign_identity:
            subprocess.run(["bash", str(ROOT / "installer/macos/sign-macos.sh"),
                            str(bundle), args.sign_identity], check=True)
            signing = "ad-hoc" if args.sign_identity == "-" else "developer-id"
        readme = bundle.parent / "README-macOS.txt"
        readme.write_text(
            f"ScriSelf Studio {version} — macOS, {runtime}\n\n"
            "Предварительная сборка. Требуется macOS 15 или новее. .NET уже включён.\n"
            "Распакуйте ZIP в Finder и перенесите ScriSelf Studio.app в папку «Программы».\n"
            "Проекты Windows .scri можно открыть через стартовый экран или Finder. Сохранение: Cmd+S.\n\n"
            f"Подпись: {signing}. Заверение Apple (notarization) не выполнено.\n"
            "Если macOS блокирует запуск: после попытки открытия выберите «Всё равно открыть»\n"
            "в «Системные настройки → Конфиденциальность и безопасность» для этой программы.\n\n"
            "Создание архива не подтверждает запуск на macOS. Доступ к Keychain, облаку,\n"
            "системным диалогам, буферу обмена и печати нужно проверить на Mac.\n",
            encoding="utf-8")
        archive = output / f"{EXECUTABLE}-{version}-{runtime}.zip"
        write_archive(bundle, readme, archive)
        digest = write_update_manifest(archive, version, runtime, output, signing_key)
        (bundle.parent / "build-info.json").write_text(json.dumps({
            "version": version, "runtime": runtime, "built_on": sys.platform,
            "built_at_utc": timestamp, "signing": signing, "notarized": False,
            "macos_launch_verified": False, "archive_sha256": digest,
            "update_manifest_signed": signing_key is not None,
        }, indent=2) + "\n", encoding="utf-8")
        print(f"macOS archive: {archive}", flush=True)


if __name__ == "__main__":
    main()
