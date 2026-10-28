#!/usr/bin/env python3
"""Validate the MapVote Panorama addon (workshop/content/panorama): XML + versioned hud.vcss + images."""
from __future__ import annotations

import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from mapvote_layout import (
    COLS_WIDTH_MAX,
    COL_WIDTH_PERCENT,
    HUD_CSS_NAME,
    HUD_VCSS_NAME,
    PANORAMA_VERSION,
    VOTE_XML_NAME,
)

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "workshop" / "content" / "panorama"
LAYOUTS = CONTENT / "layout" / "custom_game" / "mapvote"
STYLES = CONTENT / "styles" / "custom_game" / "mapvote"
IMAGES = CONTENT / "images" / "custom_game" / "mapvote" / "maps"
HUD_VCSS = STYLES / HUD_VCSS_NAME
HUD_CSS = STYLES / HUD_CSS_NAME
VOTE_XML = LAYOUTS / VOTE_XML_NAME
VOTE_HUD_CS = ROOT / "src" / "MapVote" / "Huds" / "VoteHud.cs"
VOTE_IDS_CS = ROOT / "src" / "MapVote" / "Huds" / "VoteIds.cs"
MAP_CATALOG_CS = ROOT / "src" / "MapVote" / "Maps" / "MapCatalog.cs"
VERSION_CS = ROOT / "src" / "MapVote" / "Version.cs"
PREVIEW_HTML = ROOT / "workshop" / "preview" / "vote.html"
GENERATE_XML = ROOT / "tools" / "generate_vote_xml.py"
CREDIT_TEXT = "made by kandru.net - visit github.com/kandru/cs2-mapvote"

INCLUDE_RE = re.compile(r"s2r://panorama/styles/custom_game/mapvote/([A-Za-z0-9_]+\.vcss)")
VAR_RE = re.compile(r"\{s:([A-Za-z0-9_]+)\}")
HIDDEN_CLASS = re.compile(r"(?<![\w-])(?:hidden|Hidden)(?![\w-])")
PROPERTY_RE = re.compile(r"(?:^|[{;])\s*([a-z][a-z0-9-]*)\s*:", re.M)
MV_MAP_CLASS_RE = re.compile(r"\.mv-map-([a-z0-9_]+)\s*\{([^}]*)\}", re.S)
MV_MAP_STOCK_CLASS_RE = re.compile(r"\.mv-map-map_([a-z0-9_]+)\s*\{([^}]*)\}", re.S)
STOCK_BLOCK_RE = re.compile(
    r"/\*\s*BEGIN mv-map-stock\s*\*/(.*?)/\*\s*END mv-map-stock\s*\*/",
    re.S,
)
PALETTE_BLOCK_RE = re.compile(
    r"/\*\s*BEGIN mv-map-palette\s*\*/(.*?)/\*\s*END mv-map-palette\s*\*/",
    re.S,
)
LOCAL_MAP_URL_RE = re.compile(
    r'url\(\s*"s2r://panorama/images/custom_game/mapvote/maps/([a-z0-9_]+)\.vtex"\s*\)'
)
VTEX_FILE_RE = re.compile(r'"m_fileName"\s+"string"\s+"([^"]+)"')
KEYFRAMES_RE = re.compile(r"@keyframes\s+([^{\s]+)")

BAD_CSS = re.compile(
    r"(?:"
    r"(?:[{;]|^)\s*display\s*:"
    r"|\b(?:rgba?|hsla?|calc|var)\s*\("
    r"|background-size\s*:\s*contain\b"
    r"|@media\b"
    r"|@define\b"
    r"|::(?:before|after)\b"
    r"|(?:[{;]|^)\s*visibility\s*:\s*hidden\b"
    r"|(?:[{;]|^)\s*overflow\s*:\s*[^;]*\bhidden\b"
    r"|(?:[{;]|^)\s*background-blur\s*:"
    r"|(?:[{;]|^)\s*vertical-align\s*:\s*center\b"
    r"|(?:[{;]|^)\s*font-size\s*:\s*[0-9.]+[a-z%]+\b"
    r"|flow-children\s*:\s*right-wrap\b"
    r"|height\s*:\s*width-percentage\b"
    r"|pre-transform-scale2d\s*:"
    r")",
    re.I | re.M,
)
REJECTED_ATTRS = frozenset({"texturewidth", "textureheight", "style", "onactivate", "hittestchildren"})
ALLOWED_TAGS = frozenset({"root", "styles", "include", "snippets", "Panel", "Label", "Button"})
ALLOWED_ATTRS = frozenset({"id", "class", "hittest", "text", "src"})
# Hit-enabled panels (omit hittest=false): scroll gutters for mouse wheel between
# tiles, plus FilterBar so scrolled MapGridHost tiles cannot steal chip clicks.
WHEEL_HIT_PANEL_CLASSES = frozenset(
    {
        "VotedRow",
        "VotedStrip",
        "MapGrid",
        "MapCatalog",
        "CatalogRow",
        "MapSlot",
        "FilterBar",
    }
)

# Ruleset §5.1 - custom_hud_layout subset (stuff/PANORAMA_RULESET.md).
KNOWN_PROPS = frozenset(
    {
        "width",
        "height",
        "min-width",
        "min-height",
        "max-width",
        "max-height",
        "flow-children",
        "horizontal-align",
        "vertical-align",
        "padding",
        "padding-top",
        "padding-right",
        "padding-bottom",
        "padding-left",
        "margin",
        "margin-top",
        "margin-right",
        "margin-bottom",
        "margin-left",
        "background-color",
        "background-image",
        "background-size",
        "background-position",
        "background-repeat",
        "background-img-opacity",
        "border",
        "border-top",
        "border-right",
        "border-bottom",
        "border-left",
        "border-color",
        "border-radius",
        "box-shadow",
        "color",
        "font-size",
        "font-weight",
        "font-family",
        "letter-spacing",
        "text-align",
        "text-transform",
        "text-overflow",
        "white-space",
        "opacity",
        "brightness",
        "wash-color",
        "world-blur",
        "visibility",
        "overflow",
        "z-index",
        "transform",
        "clip",
        "transition-property",
        "transition-duration",
        "transition-timing-function",
        "transition-delay",
        "animation-name",
        "animation-duration",
        "animation-timing-function",
        "animation-fill-mode",
        "animation-iteration-count",
        "animation-delay",
        "ignore-parent-flow",
        "cursor",
        "sound",
    }
)


def csharp_const_int(path: Path, name: str) -> int:
    text = path.read_text(encoding="utf-8")
    match = re.search(rf"public const int {name} = (\d+);", text)
    if not match:
        raise SystemExit(f"missing public const int {name} in {path.relative_to(ROOT)}")
    return int(match.group(1))


def csharp_const_string(path: Path, name: str) -> str:
    text = path.read_text(encoding="utf-8")
    match = re.search(rf'public const string {name} = "([^"]+)";', text)
    if not match:
        raise SystemExit(f'missing public const string {name} in {path.relative_to(ROOT)}')
    return match.group(1)


def check_local_map_image(errors: list[str], css_class: str, file_stem: str, body: str) -> None:
    if "background-image" not in body:
        errors.append(f".{css_class}: missing background-image")
        return
    m = LOCAL_MAP_URL_RE.search(body)
    if not m or m.group(1) != file_stem:
        errors.append(
            f".{css_class}: must use "
            f's2r://panorama/images/custom_game/mapvote/maps/{file_stem}.vtex'
        )
    png = IMAGES / f"{file_stem}.png"
    vtex = IMAGES / f"{file_stem}.vtex"
    if not png.is_file():
        errors.append(f"missing image for .{css_class}: {png.relative_to(ROOT)}")
    if not vtex.is_file():
        errors.append(f"missing vtex for .{css_class}: {vtex.relative_to(ROOT)}")
        return
    vtext = vtex.read_text(encoding="utf-8")
    found = VTEX_FILE_RE.search(vtext)
    expected = f"panorama/images/custom_game/mapvote/maps/{file_stem}.png"
    if not found or found.group(1) != expected:
        errors.append(f"{vtex.relative_to(ROOT)}: m_fileName must be {expected!r}")


def collect_errors() -> list[str]:
    errors: list[str] = []

    if not VOTE_IDS_CS.is_file():
        return [f"missing {VOTE_IDS_CS.relative_to(ROOT)}"]
    if not VOTE_HUD_CS.is_file():
        return [f"missing {VOTE_HUD_CS.relative_to(ROOT)}"]

    if VERSION_CS.is_file():
        try:
            cs_version = csharp_const_string(VERSION_CS, "PanoramaVersion")
            if cs_version != PANORAMA_VERSION:
                errors.append(
                    f"Version.cs PanoramaVersion ({cs_version!r}) must equal "
                    f"mapvote_layout.PANORAMA_VERSION ({PANORAMA_VERSION!r})"
                )
        except SystemExit as ex:
            errors.append(str(ex))

    slot_count = csharp_const_int(VOTE_IDS_CS, "SlotCount")
    catalog_cols = csharp_const_int(VOTE_IDS_CS, "CatalogCols")
    catalog_rows = csharp_const_int(VOTE_IDS_CS, "CatalogRowCount")
    catalog_cols_width_max = csharp_const_int(VOTE_IDS_CS, "CatalogColsWidthMax")
    filter_types = csharp_const_int(VOTE_IDS_CS, "FilterTypeSlots")
    filter_sorts = csharp_const_int(VOTE_IDS_CS, "FilterSortSlots")
    root_panel = csharp_const_string(VOTE_IDS_CS, "Root")

    if MAP_CATALOG_CS.is_file():
        max_maps = csharp_const_int(MAP_CATALOG_CS, "MaxMaps")
        if max_maps != slot_count:
            errors.append(
                f"MapCatalog.MaxMaps ({max_maps}) must equal VoteIds.SlotCount ({slot_count})"
            )

    expected_rows = (slot_count + catalog_cols - 1) // catalog_cols
    if catalog_rows != expected_rows:
        errors.append(
            f"VoteIds.CatalogRowCount ({catalog_rows}) must equal "
            f"ceil(SlotCount/CatalogCols) ({expected_rows})"
        )

    if catalog_cols_width_max != COLS_WIDTH_MAX:
        errors.append(
            f"VoteIds.CatalogColsWidthMax ({catalog_cols_width_max}) must equal "
            f"mapvote_layout.COLS_WIDTH_MAX ({COLS_WIDTH_MAX})"
        )

    expected_ids = {
        root_panel,
        "mv-minimize",
        "mv-voted-section",
        "mv-voted-hint",
        "mv-voted-strip",
        "mv-filters",
        "mv-sort-fav",
        "mv-empty",
        "mv-catalog",
    }
    for r in range(catalog_rows):
        expected_ids.add(f"mv-crow-{r}")
    for i in range(filter_types):
        expected_ids.add(f"mv-ft-{i}")
    for i in range(filter_sorts):
        expected_ids.add(f"mv-fs-{i}")
    for i in range(slot_count):
        expected_ids.update(
            {
                f"mv-slot-{i}",
                f"mv-{i}",
                f"mv-shot-{i}",
                f"mv-fav-{i}",
                f"mv-voted-slot-{i}",
                f"mv-voted-{i}",
                f"mv-voted-shot-{i}",
                f"mv-voted-fav-{i}",
            }
        )

    expected_vars = (
        {f"v{i}" for i in range(slot_count)}
        | {f"n{i}" for i in range(slot_count)}
        | {f"vv{i}" for i in range(slot_count)}
        | {f"vn{i}" for i in range(slot_count)}
        | {f"ft{i}" for i in range(filter_types)}
        | {f"fs{i}" for i in range(filter_sorts)}
        | {"mv_title", "mv_hint", "mv_chip", "mv_empty", "sfav"}
    )

    if GENERATE_XML.is_file():
        check = subprocess.run(
            [sys.executable, str(GENERATE_XML), "--check"],
            cwd=ROOT,
            capture_output=True,
            text=True,
        )
        if check.returncode != 0:
            msg = (check.stderr or check.stdout or f"{VOTE_XML_NAME} out of date").strip()
            errors.append(msg)

    if not HUD_VCSS.is_file():
        errors.append(f"missing {HUD_VCSS.relative_to(ROOT)}")
    else:
        css_raw = HUD_VCSS.read_text(encoding="utf-8")
        css = re.sub(r"/\*.*?\*/", "", css_raw, flags=re.S)
        if HIDDEN_CLASS.search(css):
            errors.append(f"{HUD_VCSS_NAME} uses forbidden class name hidden/Hidden")
        if BAD_CSS.search(css):
            for m in BAD_CSS.finditer(css):
                line = css.count("\n", 0, m.start()) + 1
                errors.append(f"{HUD_VCSS_NAME}:~{line}: forbidden CSS near {m.group(0)!r}")
        if css.count("{") != css.count("}"):
            errors.append(f"{HUD_VCSS_NAME}: unbalanced braces")
        for prop in PROPERTY_RE.findall(css):
            if prop not in KNOWN_PROPS:
                errors.append(f"{HUD_VCSS_NAME}: unknown/unsupported property {prop!r}")
        for m in KEYFRAMES_RE.finditer(css):
            name = m.group(1)
            if not (name.startswith("'") and name.endswith("'")) and not (
                name.startswith('"') and name.endswith('"')
            ):
                line = css.count("\n", 0, m.start()) + 1
                errors.append(f"{HUD_VCSS_NAME}:~{line}: @keyframes name must be quoted ({name!r})")

        for req in (
            ".is-off",
            ".is-fading",
            ".mv-glass",
            ".mv-scrim",
            ".MapSlot",
            ".MapTile",
            ".MapShot",
            ".MapFade",
            ".MapGrid",
            ".VotedSection",
            ".VotedRow",
            ".VotedStrip",
            ".FilterBar",
            ".FilterChip",
            ".FavBtn",
            ".FavIcon",
            ".CreditBar",
            ".CreditLabel",
            ".MapGridHost",
            ".MapCatalog",
            ".CatalogRow",
            ".is-self",
            ".is-done",
            ".mv-map-default",
            ".vw-0",
            ".rows-0",
            ".cols-0",
            f".CatalogRow.cols-3 {{ width: {3 * COL_WIDTH_PERCENT}%; }}",
            f".CatalogRow.cols-{COLS_WIDTH_MAX} {{ width: 100%; }}",
        ):
            if req not in css_raw:
                errors.append(f"{HUD_VCSS_NAME}: missing required selector/class {req}")

        if f".CatalogRow.cols-{COLS_WIDTH_MAX + 1}" in css_raw:
            errors.append(
                f"{HUD_VCSS_NAME}: cols ladder must stop at cols-{COLS_WIDTH_MAX} "
                f"(100% saturation)"
            )

        if not HUD_CSS.is_file():
            errors.append(f"{HUD_CSS_NAME} missing (must be symlink to {HUD_VCSS_NAME})")
        elif not HUD_CSS.is_symlink() or HUD_CSS.resolve() != HUD_VCSS.resolve():
            errors.append(f"{HUD_CSS_NAME} must be a symlink to {HUD_VCSS_NAME}")

        stock_block = STOCK_BLOCK_RE.search(css_raw)
        palette_block = PALETTE_BLOCK_RE.search(css_raw)
        if stock_block is None:
            errors.append(f"{HUD_VCSS_NAME}: missing /* BEGIN mv-map-stock */ … /* END mv-map-stock */ block")
        if palette_block is None:
            errors.append(f"{HUD_VCSS_NAME}: missing /* BEGIN mv-map-palette */ … /* END mv-map-palette */ block")

        for stem, body in MV_MAP_STOCK_CLASS_RE.findall(stock_block.group(1) if stock_block else ""):
            if stem == "random" or stem.startswith("lobby_"):
                errors.append(f".mv-map-map_{stem}: random and lobby_* are not shipped")
            check_local_map_image(errors, f"mv-map-map_{stem}", f"map_{stem}", body)

        for stem, body in MV_MAP_CLASS_RE.findall(palette_block.group(1) if palette_block else ""):
            if stem.startswith("map_"):
                errors.append(f".mv-map-{stem}: official maps belong in the mv-map-stock block")
                continue
            if stem != "default" and not stem.startswith("ws_"):
                errors.append(f".mv-map-{stem}: workshop images must use a ws_ stem")
            check_local_map_image(errors, f"mv-map-{stem}", stem, body)

    if not VOTE_XML.is_file():
        errors.append(f"missing {VOTE_XML.relative_to(ROOT)}")
        return errors

    try:
        tree = ET.parse(VOTE_XML)
    except ET.ParseError as ex:
        errors.append(f"{VOTE_XML_NAME}: parse error: {ex}")
        return errors

    root = tree.getroot()
    found_ids: set[str] = set()
    found_vars: set[str] = set()
    includes: list[str] = []

    for el in root.iter():
        tag = el.tag
        if tag not in ALLOWED_TAGS:
            errors.append(f"{VOTE_XML_NAME}: forbidden tag <{tag}>")
        for attr, value in el.attrib.items():
            if attr not in ALLOWED_ATTRS:
                errors.append(f"{VOTE_XML_NAME}: rejected attr {attr!r} on <{tag}>")
            if attr in REJECTED_ATTRS:
                errors.append(f"{VOTE_XML_NAME}: rejected attr {attr!r}")
        if tag == "Button" and "hittest" in el.attrib:
            errors.append(f"{VOTE_XML_NAME}: do not set hittest on Button")
        if tag == "Panel" and el.attrib.get("hittest") != "false":
            classes = set(el.attrib.get("class", "").split())
            if classes & WHEEL_HIT_PANEL_CLASSES:
                pass  # intentional: wheel target for scroll gutters
            elif "id" in el.attrib or "class" in el.attrib:
                errors.append(
                    f"{VOTE_XML_NAME}: Panel must have hittest=\"false\" "
                    f"(id={el.attrib.get('id')!r})"
                )
        if tag == "Label" and el.attrib.get("hittest") != "false":
            errors.append(
                f"{VOTE_XML_NAME}: Label must have hittest=\"false\" "
                f"(id={el.attrib.get('id')!r})"
            )
        if tag == "include":
            src = el.attrib.get("src", "")
            m = INCLUDE_RE.search(src)
            if m:
                includes.append(m.group(1))
        pid = el.attrib.get("id")
        if pid:
            found_ids.add(pid)
        text = el.attrib.get("text", "")
        found_vars.update(VAR_RE.findall(text))
        if tag == "Label" and text:
            if not VAR_RE.fullmatch(text.strip()) and text != CREDIT_TEXT:
                errors.append(
                    f"{VOTE_XML_NAME}: Label text must be {{s:…}} or the credit sentence "
                    f"(got {text!r})"
                )

    if CREDIT_TEXT not in VOTE_XML.read_text(encoding="utf-8"):
        errors.append(f"{VOTE_XML_NAME}: missing credit footer text {CREDIT_TEXT!r}")

    for child in list(root):
        if child.tag == "Panel" and "id" in child.attrib:
            errors.append(f"{VOTE_XML_NAME}: outermost Panel must have no id")

    card = None
    for el in root.iter("Panel"):
        if el.attrib.get("id") == root_panel:
            card = el
            break
    if card is None:
        errors.append(f"{VOTE_XML_NAME}: missing root card id={root_panel!r}")
    else:
        classes = card.attrib.get("class", "").split()
        if "is-off" not in classes:
            errors.append(f"{VOTE_XML_NAME}: #{root_panel} must start with is-off")

    if HUD_VCSS_NAME not in includes:
        errors.append(f"{VOTE_XML_NAME}: must include mapvote/{HUD_VCSS_NAME}")

    missing_ids = expected_ids - found_ids
    extra_ids = found_ids - expected_ids
    if missing_ids:
        errors.append(f"{VOTE_XML_NAME}: missing ids: {sorted(missing_ids)}")
    if extra_ids:
        errors.append(f"{VOTE_XML_NAME}: unexpected ids: {sorted(extra_ids)}")

    missing_vars = expected_vars - found_vars
    extra_vars = found_vars - expected_vars
    if missing_vars:
        errors.append(f"{VOTE_XML_NAME}: missing dialog vars: {sorted(missing_vars)}")
    if extra_vars:
        errors.append(f"{VOTE_XML_NAME}: unexpected dialog vars: {sorted(extra_vars)}")

    if not (IMAGES / "default.png").is_file() or not (IMAGES / "default.vtex").is_file():
        errors.append("default.png / default.vtex required under images/.../maps/")

    return errors


def main() -> int:
    errors = collect_errors()
    if errors:
        print("Panorama validation failed:")
        for err in errors:
            print(f"  - {err}")
        return 1

    # Keep browser preview in sync whenever XML/CSS validate.
    sys.path.insert(0, str(ROOT / "tools"))
    from sync_preview import sync

    preview_errors = sync()
    if preview_errors:
        print("Panorama validation OK, but preview sync failed:")
        for err in preview_errors:
            print(f"  - {err}")
        return 1

    print(
        f"Panorama validation OK ({VOTE_XML_NAME} + {HUD_VCSS_NAME} + map images); "
        f"preview synced → {PREVIEW_HTML.relative_to(ROOT)}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
