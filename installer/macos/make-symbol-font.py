"""Regenerate the bundled MIT-licensed symbols. Requires fonttools (build-time only)."""

from pathlib import Path
import argparse
import io
import json
import re
import urllib.request

from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[2]
REVISION = "463d58b8186f874ab2f51cf0a08b4ff5097c912f"
UPSTREAM = f"https://raw.githubusercontent.com/microsoft/fluentui-system-icons/{REVISION}"
# Retain existing private-use IDs; the outlines come from Fluent System Icons,
# never from a Windows system font.
SYMBOLS = {
    0xE70D: "chevron_down", 0xE70E: "chevron_up", 0xE76C: "chevron_right",
    0xE7C1: "play", 0xE82D: "megaphone", 0xE945: "flash", 0xE7BA: "warning",
    0xE70B: "notepad", 0xE70F: "edit", 0xE710: "add", 0xE711: "dismiss",
    0xE712: "more_horizontal", 0xE713: "settings", 0xE721: "search",
    0xE72A: "arrow_right", 0xE72B: "arrow_left", 0xE72E: "share",
    0xE734: "star", 0xE736: "library", 0xE73E: "checkmark",
    0xE740: "arrow_maximize", 0xE74D: "delete", 0xE74E: "save",
    0xE753: "cloud", 0xE77B: "person", 0xE7C3: "document",
    0xE8A5: "folder_open", 0xE8B7: "folder_add", 0xE8BB: "text_bullet_list_ltr",
    0xE8BD: "chat", 0xE8D4: "book_open", 0xE8D7: "arrow_export",
    0xE8EC: "subtract_circle", 0xE8EF: "grid", 0xE8FD: "library",
    0xE902: "copy", 0xEDE1: "branch",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", type=Path, help="Offline cache of the upstream font, JSON and license")
    args = parser.parse_args()

    def read(name, remote):
        if args.source_dir:
            return (args.source_dir / name).read_bytes()
        with urllib.request.urlopen(f"{UPSTREAM}/{remote}", timeout=30) as response:
            return response.read()

    used = set()
    for path in (ROOT / "src").rglob("*"):
        if path.suffix not in (".cs", ".axaml") or "obj" in path.parts or "bin" in path.parts:
            continue
        text = path.read_text(encoding="utf-8-sig")
        used.update(int(code, 16) for code in re.findall(r"(?:&#x|\\u)(E[0-9a-fA-F]{3})", text))
    if used - SYMBOLS.keys():
        raise ValueError(f"Add mappings for new symbols: {sorted(used - SYMBOLS.keys())}")

    font = TTFont(io.BytesIO(read("FluentSystemIcons-Regular.ttf", "fonts/FluentSystemIcons-Regular.ttf")))
    codes = json.loads(read("FluentSystemIcons-Regular.json", "fonts/FluentSystemIcons-Regular.json"))
    cmap = font.getBestCmap()
    mapped = {code: cmap[codes[f"ic_fluent_{name}_20_regular"]] for code, name in SYMBOLS.items()}
    options = subset.Options()
    options.hinting = False
    options.name_IDs = ["*"]
    subsetter = subset.Subsetter(options)
    subsetter.populate(glyphs=set(mapped.values()))
    subsetter.subset(font)
    for table in font["cmap"].tables:
        if table.isUnicode():
            table.cmap = mapped.copy()
    names = {
        1: "ScriSelf Symbols", 2: "Regular", 3: "ScriSelfSymbols-Regular-1.0",
        4: "ScriSelf Symbols Regular", 6: "ScriSelfSymbols-Regular",
        16: "ScriSelf Symbols", 17: "Regular",
    }
    for record in font["name"].names:
        if record.nameID in names:
            record.string = names[record.nameID].encode(record.getEncoding())
    destination = ROOT / "src/ScriSelfStudio.Presentation/Assets/Fonts"
    destination.mkdir(parents=True, exist_ok=True)
    font.save(destination / "ScriSelfSymbols.ttf")
    (destination / "FluentSystemIcons-LICENSE.txt").write_bytes(
        read("FluentSystemIcons-LICENSE.txt", "LICENSE"))
    (destination / "README.md").write_text(
        f"# ScriSelf Symbols\n\nSubset of Microsoft Fluent System Icons, MIT license.\n"
        f"Upstream revision: `{REVISION}`. Font renamed and character IDs remapped for ScriSelf Studio.\n\n"
        f"Regenerate with `python installer/macos/make-symbol-font.py` (requires fonttools).\n",
        encoding="utf-8")
    print(f"Bundled {len(mapped)} symbols: {destination / 'ScriSelfSymbols.ttf'}")


if __name__ == "__main__":
    main()
