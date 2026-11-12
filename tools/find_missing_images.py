#!/usr/bin/env python3
"""Detect missing map images from server print_mapgroup_sv output, create placeholder tiles, and update CSS."""

from __future__ import annotations

import argparse
import re
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from generate_stock_map_css import merge_map_block, merge_maps_stock_css
from map_names import (
    DEFAULT_STEM,
    STOCK_IMAGE_PREFIX,
    WORKSHOP_IMAGE_PREFIX,
    ignored_stock_map,
)

ROOT = Path(__file__).resolve().parents[1]
IMAGES = ROOT / "workshop" / "content" / "panorama" / "images" / "custom_game" / "mapvote" / "maps"
CSS_BEGIN = "/* BEGIN mv-map-palette */"
CSS_END = "/* END mv-map-palette */"


def resolve_default_png() -> Path:
    candidates = [
        IMAGES / f"{DEFAULT_STEM}.png",
        IMAGES / "default.png",
        IMAGES / "map_default.png",
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise FileNotFoundError(f"Missing default placeholder image. Looked in: {candidates}")


def write_vtex(stem: str) -> None:
    path = IMAGES / f"{stem}.vtex"
    path.write_text(
        f"""<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
"CDmeVtex"
{{
    "m_inputTextureArray" "element_array"
    [
        "CDmeInputTexture"
        {{
            "m_name" "string" "0"
            "m_fileName" "string" "panorama/images/custom_game/mapvote/maps/{stem}.png"
            "m_colorSpace" "string" "srgb"
            "m_typeString" "string" "2D"
        }}
    ]

    "m_outputTypeString" "string" "2D"
    "m_outputFormat" "string" "RGBA8888"
    "m_bNoLod" "bool" "1"

    "m_textureOutputChannelArray" "element_array"
    [
        "CDmeTextureOutputChannel"
        {{
            "m_inputTextureArray" "string_array" [ "0" ]
            "m_srcChannels" "string" "rgba"
            "m_dstChannels" "string" "rgba"
            "m_outputColorSpace" "string" "srgb"
        }}
    ]
}}
""",
        encoding="utf-8",
    )


def save_default_tile(default_src: Path, stem: str) -> None:
    dest_png = IMAGES / f"{stem}.png"
    if not dest_png.is_file():
        shutil.copy2(default_src, dest_png)
    dest_vtex = IMAGES / f"{stem}.vtex"
    if not dest_vtex.is_file():
        write_vtex(stem)


def update_maps_vcss(stems: list[str]) -> None:
    """Append missing stock and workshop classes to maps_v1.vcss."""
    if not stems:
        return

    stock_names: list[str] = []
    workshop_stems: list[str] = []

    for stem in stems:
        if stem.startswith(STOCK_IMAGE_PREFIX):
            stock_names.append(stem[len(STOCK_IMAGE_PREFIX) :])
        elif stem.startswith(WORKSHOP_IMAGE_PREFIX):
            workshop_stems.append(stem)

    if stock_names:
        merge_maps_stock_css(stock_names)

    if workshop_stems:
        merge_map_block(CSS_BEGIN, CSS_END, workshop_stems)


def parse_mapgroup_output(text: str, force_type: str = "auto") -> list[tuple[str, str, bool]]:
    """Parse output from print_mapgroup_sv into (display_name, target_stem, is_missing) triplets."""
    entries: list[tuple[str, str, bool]] = []
    seen: set[str] = set()
    is_workshop_group = force_type == "workshop"

    for raw_line in text.splitlines():
        line = re.sub(r"[\s\xa0]+", " ", raw_line).strip()
        if not line:
            continue

        low = line.lower()

        if low.startswith("print_mapgroup_sv"):
            continue
        if low.startswith("map group:"):
            if force_type == "auto":
                group_name = low.split(":", 1)[1].strip()
                if "workshop" in group_name:
                    is_workshop_group = True
            continue

        # Match workshop path: workshop/<id>/<name> or <id>/<name>
        ws_match = re.search(r"(?:workshop/)?(\d+)/([a-zA-Z0-9_\-\.]+)", line)
        if ws_match:
            raw_id = ws_match.group(0)
            map_name = Path(ws_match.group(2)).stem.lower()

            if force_type == "stock":
                stem = f"{STOCK_IMAGE_PREFIX}{map_name}"
            else:
                stem = f"{WORKSHOP_IMAGE_PREFIX}{map_name}"

            if stem not in seen:
                seen.add(stem)
                is_missing = not (IMAGES / f"{stem}.png").is_file()
                entries.append((raw_id, stem, is_missing))
            continue

        # Match bare map name
        token = line.split()[0].strip('"\'')
        map_name = Path(token).stem.lower()

        if ignored_stock_map(map_name):
            continue

        ws_stem = f"{WORKSHOP_IMAGE_PREFIX}{map_name}"
        stock_stem = f"{STOCK_IMAGE_PREFIX}{map_name}"

        if force_type == "workshop":
            chosen_stem = ws_stem
            is_missing = not (IMAGES / f"{ws_stem}.png").is_file()
        elif force_type == "stock":
            chosen_stem = stock_stem
            is_missing = not (IMAGES / f"{stock_stem}.png").is_file()
        else:
            # Auto-detection
            ws_exists = (IMAGES / f"{ws_stem}.png").is_file()
            stock_exists = (IMAGES / f"{stock_stem}.png").is_file()
            if ws_exists:
                chosen_stem = ws_stem
                is_missing = False
            elif stock_exists:
                chosen_stem = stock_stem
                is_missing = False
            else:
                chosen_stem = ws_stem if is_workshop_group else stock_stem
                is_missing = True

        if map_name not in seen:
            seen.add(map_name)
            entries.append((map_name, chosen_stem, is_missing))

    return entries


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--type",
        choices=["auto", "workshop", "stock"],
        default="auto",
        help="Force destination tile prefix ('workshop' -> ws_, 'stock' -> map_)",
    )
    args = parser.parse_args()

    IMAGES.mkdir(parents=True, exist_ok=True)

    try:
        default_png = resolve_default_png()
    except FileNotFoundError as err:
        print(f"Error: {err}", file=sys.stderr)
        return 1

    input_text = sys.stdin.read()
    if not input_text.strip():
        return 0

    parsed = parse_mapgroup_output(input_text, force_type=args.type)
    missing = [(display_name, stem) for display_name, stem, is_missing in parsed if is_missing]

    added_stems: list[str] = []
    for display_name, stem in missing:
        print(display_name)
        save_default_tile(default_png, stem)
        added_stems.append(stem)

    if added_stems:
        update_maps_vcss(added_stems)

    return 0


if __name__ == "__main__":
    sys.exit(main())