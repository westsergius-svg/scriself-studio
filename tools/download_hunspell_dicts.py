#!/usr/bin/env python3
"""Скачать Hunspell-словари (wooorm/dictionaries) в resources/hunspell/."""
from __future__ import annotations

import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src" / "scriborium" / "resources" / "hunspell"
BASE = "https://raw.githubusercontent.com/wooorm/dictionaries/main/dictionaries"

LOCALES = ("en", "ru", "de", "es", "fr", "uk")


def main() -> None:
    for loc in LOCALES:
        dest = OUT / loc
        dest.mkdir(parents=True, exist_ok=True)
        for ext in ("aff", "dic"):
            url = f"{BASE}/{loc}/index.{ext}"
            target = dest / f"index.{ext}"
            print(f"GET {url}")
            urllib.request.urlretrieve(url, target)
            print(f"  -> {target} ({target.stat().st_size} bytes)")
    print("Done.")


if __name__ == "__main__":
    main()
