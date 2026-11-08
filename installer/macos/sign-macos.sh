#!/usr/bin/env bash
set -euo pipefail

app_path="${1:?Usage: bash sign-macos.sh /path/to/ScriSelf\ Studio.app [identity]}"
signing_identity="${2:--}"
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
[[ "$(uname -s)" == "Darwin" ]] || { echo "Code signing requires macOS" >&2; exit 1; }
[[ -f "$app_path/Contents/Info.plist" ]] || { echo "Invalid .app bundle" >&2; exit 1; }

signing_options=(--force --sign "$signing_identity")
if [[ "$signing_identity" == "-" ]]; then
    signing_options+=(--timestamp=none)
else
    signing_options+=(--timestamp --options runtime --entitlements "$script_directory/ScriSelfStudio.entitlements")
fi

# Sign each Mach-O binary first, then seal the bundle. No recursive --deep signing.
while IFS= read -r -d '' binary; do
    if [[ "$(/usr/bin/file -b "$binary")" == *Mach-O* ]]; then
        /usr/bin/codesign "${signing_options[@]}" "$binary"
    fi
done < <(find "$app_path/Contents/MacOS" -type f -print0)
/usr/bin/codesign "${signing_options[@]}" "$app_path"
/usr/bin/codesign --verify --strict --verbose=2 "$app_path"
