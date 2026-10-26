#!/usr/bin/env python3
"""Rewrite panorama map tiles that are not 350×197 PNG.

Center-crops to 16:9 (no stretch). A non-PNG next to a correct PNG is removed.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from extract_workshop_images import (
    IMAGES,
    TARGET_HEIGHT,
    TARGET_WIDTH,
    _pillow_image,
    center_crop_tile,
)

IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff"}


def is_tile(path: Path) -> bool:
    if path.suffix.lower() != ".png" or not path.is_file():
        return False
    with _pillow_image().open(path) as img:
        return img.format == "PNG" and img.size == (TARGET_WIDTH, TARGET_HEIGHT)


def rewrite_tile(source: Path, dest: Path) -> tuple[str, int, int]:
    with _pillow_image().open(source) as img:
        kind = img.format or source.suffix.lstrip(".").upper() or "unknown"
        width, height = img.size
        tile = center_crop_tile(img)
        tile.load()
    dest.parent.mkdir(parents=True, exist_ok=True)
    tile.save(dest, format="PNG", optimize=True)
    return kind, width, height


def check_dir(directory: Path) -> int:
    if not directory.is_dir():
        raise SystemExit(f"missing image directory: {directory}")

    files = sorted(
        p for p in directory.iterdir() if p.is_file() and p.suffix.lower() in IMAGE_SUFFIXES
    )
    ok = updated = 0
    for path in files:
        dest = path.with_suffix(".png")
        if is_tile(path):
            ok += 1
            continue
        if path != dest and is_tile(dest):
            path.unlink()
            print(f"[images-check] removed {path.name} ({dest.name} already {TARGET_WIDTH}x{TARGET_HEIGHT} PNG)")
            continue
        try:
            kind, width, height = rewrite_tile(path, dest)
        except Exception as ex:
            print(f"[images-check] {path.name}: failed ({ex})", file=sys.stderr)
            return 1
        if path != dest and path.exists():
            path.unlink()
        if not is_tile(dest):
            print(f"[images-check] {dest.name}: still not {TARGET_WIDTH}x{TARGET_HEIGHT} PNG", file=sys.stderr)
            return 1
        updated += 1
        print(
            f"[images-check] {path.name} ({kind} {width}x{height}) "
            f"→ {dest.name} ({TARGET_WIDTH}x{TARGET_HEIGHT})"
        )

    print(f"[images-check] {ok} ok, {updated} updated ({TARGET_WIDTH}x{TARGET_HEIGHT} PNG)")
    return 0


def main() -> int:
    directory = Path(sys.argv[1]) if len(sys.argv) > 1 else IMAGES
    return check_dir(directory)


if __name__ == "__main__":
    raise SystemExit(main())
