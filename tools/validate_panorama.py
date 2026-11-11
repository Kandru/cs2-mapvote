#!/usr/bin/env python3
"""Validate the MapVote Panorama addon (workshop/content/panorama): XML + versioned hud.vcss + images."""
from __future__ import annotations

import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from map_names import is_playable_map_id
from mapvote_layout import (
    HUD_V2_VCSS_NAME,
    V2_FILTER_SORT_SLOTS,
    V2_FILTER_TYPE_SLOTS,
    V2_GRID_SLOTS,
    V2_RANK_SLOTS,
    V2_SHARE_STEP,
    V2_SPAN_MAX,
    V2_SPAN_MIN,
    VOTE_V2_XML_NAME,
    HUD_CSS_NAME,
    HUD_VCSS_NAME,
    MAPS_CSS_NAME,
    MAPS_VCSS_NAME,
    PANORAMA_VERSION,
    VOTE_XML_NAME,
    VOTED_SLOT_COUNT,
)

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "workshop" / "content" / "panorama"
LAYOUTS = CONTENT / "layout" / "custom_game" / "mapvote"
STYLES = CONTENT / "styles" / "custom_game" / "mapvote"
IMAGES = CONTENT / "images" / "custom_game" / "mapvote" / "maps"
HUD_VCSS = STYLES / HUD_VCSS_NAME
HUD_CSS = STYLES / HUD_CSS_NAME
MAPS_VCSS = STYLES / MAPS_VCSS_NAME
MAPS_CSS = STYLES / MAPS_CSS_NAME
VOTE_XML = LAYOUTS / VOTE_XML_NAME
VOTE_HUD_CS = ROOT / "src" / "MapVote" / "Huds" / "VoteHud.cs"
VOTE_IDS_CS = ROOT / "src" / "MapVote" / "Huds" / "VoteIds.cs"
MAP_CATALOG_CS = ROOT / "src" / "MapVote" / "Maps" / "MapCatalog.cs"
VERSION_CS = ROOT / "src" / "MapVote" / "Version.cs"
PREVIEW_HTML = ROOT / "workshop" / "preview" / "vote.html"
PREVIEW_BROWSE_HTML = ROOT / "workshop" / "preview" / "maps.html"
GENERATE_XML = ROOT / "tools" / "generate_vote_xml.py"
CREDIT_TEXT = "made by kandru.net - visit github.com/kandru/cs2-mapvote"

# Engine name tables on one CCSCustomHudLayout. Every id/variable the server
# writes is interned per layout entity; hitting the cap aborts PackEntity and
# segfaults the server. Keep the pool well below it (see SLOT_COUNT).
PANEL_ID_ENGINE_CAP = 1024
DIALOG_VAR_ENGINE_CAP = 1244
# XML pool caps. The grid is written globally; per-player state is only
# visibility/favorites, so the pool can match the catalog cap.
MAX_SAFE_SLOTS = 100

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
    # Map PNG/.vtex tiles are published from another repository. CSS must still
    # point at the expected path; only check a .vtex when one is checked in here.
    vtex = IMAGES / f"{file_stem}.vtex"
    if not vtex.is_file():
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
    voted_slot_count = VOTED_SLOT_COUNT  # v1-only pool; VoteIds dropped it with v2
    filter_types = csharp_const_int(VOTE_IDS_CS, "FilterTypeSlots")
    filter_sorts = csharp_const_int(VOTE_IDS_CS, "FilterSortSlots")
    root_panel = csharp_const_string(VOTE_IDS_CS, "Root")

    if slot_count > MAX_SAFE_SLOTS:
        errors.append(
            f"VoteIds.SlotCount ({slot_count}) exceeds the XML pool cap "
            f"{MAX_SAFE_SLOTS}"
        )

    if MAP_CATALOG_CS.is_file():
        max_maps = csharp_const_int(MAP_CATALOG_CS, "MaxMaps")
        if max_maps < slot_count:
            errors.append(
                f"MapCatalog.MaxMaps ({max_maps}) must be at least "
                f"VoteIds.SlotCount ({slot_count})"
            )

    expected_ids = {
        root_panel,
        "mv-minimize",
        "mv-current",
        "mv-current-image",
        "mv-current-shot",
        "mv-current-rating",
        "mv-current-fav",
        "mv-current-like",
        "mv-current-dislike",
        "mv-voted-section",
        "mv-voted-hint",
        "mv-voted-strip",
        "mv-filters",
        "mv-sort-fav",
        "mv-empty",
        "mv-catalog",
    }
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
                f"mv-rate-{i}",
            }
        )
    for i in range(voted_slot_count):
        expected_ids.update(
            {
                f"mv-voted-slot-{i}",
                f"mv-voted-{i}",
                f"mv-voted-shot-{i}",
                f"mv-voted-fav-{i}",
                f"mv-voted-rate-{i}",
            }
        )

    expected_vars = (
        {f"v{i}" for i in range(slot_count)}
        | {f"n{i}" for i in range(slot_count)}
        | {f"r{i}" for i in range(slot_count)}
        | {f"vv{i}" for i in range(voted_slot_count)}
        | {f"vn{i}" for i in range(voted_slot_count)}
        | {f"vr{i}" for i in range(voted_slot_count)}
        | {f"ft{i}" for i in range(filter_types)}
        | {f"fs{i}" for i in range(filter_sorts)}
        | {
            "mv_title",
            "mv_hint",
            "mv_chip",
            "mv_empty",
            "mv_current",
            "mv_cur_name",
            "mv_cur_rating",
            "sfav",
        }
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

    hud_raw = HUD_VCSS.read_text(encoding="utf-8") if HUD_VCSS.is_file() else ""
    maps_raw = MAPS_VCSS.read_text(encoding="utf-8") if MAPS_VCSS.is_file() else ""

    for path, name, raw in (
        (HUD_VCSS, HUD_VCSS_NAME, hud_raw),
        (MAPS_VCSS, MAPS_VCSS_NAME, maps_raw),
    ):
        if not path.is_file():
            errors.append(f"missing {path.relative_to(ROOT)}")
            continue

        css = re.sub(r"/\*.*?\*/", "", raw, flags=re.S)
        if HIDDEN_CLASS.search(css):
            errors.append(f"{name} uses forbidden class name hidden/Hidden")
        if BAD_CSS.search(css):
            for m in BAD_CSS.finditer(css):
                line = css.count("\n", 0, m.start()) + 1
                errors.append(f"{name}:~{line}: forbidden CSS near {m.group(0)!r}")
        if css.count("{") != css.count("}"):
            errors.append(f"{name}: unbalanced braces")
        for prop in PROPERTY_RE.findall(css):
            if prop not in KNOWN_PROPS:
                errors.append(f"{name}: unknown/unsupported property {prop!r}")
        for m in KEYFRAMES_RE.finditer(css):
            keyframe = m.group(1)
            if not (keyframe.startswith("'") and keyframe.endswith("'")) and not (
                keyframe.startswith('"') and keyframe.endswith('"')
            ):
                line = css.count("\n", 0, m.start()) + 1
                errors.append(f"{name}:~{line}: @keyframes name must be quoted ({keyframe!r})")

    if HUD_VCSS.is_file():
        for req in (
            ".is-off",
            ".is-fading",
            ".mv-glass",
            ".mv-scrim",
            ".CurrentMap",
            ".CurrentImage",
            ".CurrentRating",
            ".CurrentAction",
            ".CurrentActionIcon",
            ".LikeBtn",
            ".DislikeBtn",
            ".MapSlot",
            ".MapTile",
            ".MapTile.is-voted",
            ".MapTile.is-fav",
            ".MapTile.is-lost",
            ".MapRating",
            ".MapRating.is-positive",
            ".MapRating.is-negative",
            ".MapShot",
            ".MapFade",
            ".MapGrid",
            ".VotedSection",
            ".VotedRow",
            ".VotedStrip",
            ".VoteHeadRow",
            ".FilterBar",
            ".FilterChip",
            ".FavBtn",
            ".FavIcon",
            ".CreditBar",
            ".CreditLabel",
            ".MapGridHost",
            ".MapCatalog",
            ".is-self",
            ".is-done",
            ".vw-0",
        ):
            if req not in hud_raw:
                errors.append(f"{HUD_VCSS_NAME}: missing required selector/class {req}")

        if "flow-children: right-wrap;" not in hud_raw:
            errors.append(f"{HUD_VCSS_NAME}: .MapCatalog must use flow-children: right-wrap")

        hud_clean = re.sub(r"/\*.*?\*/", "", hud_raw, flags=re.S)
        if re.search(r"\.mv-map-[a-z0-9_]+", hud_clean):
            errors.append(
                f"{HUD_VCSS_NAME}: map image classes belong in {MAPS_VCSS_NAME}, not the UI sheet"
            )
        if STOCK_BLOCK_RE.search(hud_raw) or PALETTE_BLOCK_RE.search(hud_raw):
            errors.append(f"{HUD_VCSS_NAME}: map image blocks belong in {MAPS_VCSS_NAME}")

    for css_path, css_name, target_path, target_name in (
        (HUD_CSS, HUD_CSS_NAME, HUD_VCSS, HUD_VCSS_NAME),
        (MAPS_CSS, MAPS_CSS_NAME, MAPS_VCSS, MAPS_VCSS_NAME),
    ):
        if not css_path.is_file():
            errors.append(f"{css_name} missing (must be symlink to {target_name})")
        elif not css_path.is_symlink() or css_path.resolve() != target_path.resolve():
            errors.append(f"{css_name} must be a symlink to {target_name}")

    if MAPS_VCSS.is_file():
        stock_block = STOCK_BLOCK_RE.search(maps_raw)
        palette_block = PALETTE_BLOCK_RE.search(maps_raw)
        if stock_block is None:
            errors.append(f"{MAPS_VCSS_NAME}: missing /* BEGIN mv-map-stock */ … /* END mv-map-stock */ block")
        if palette_block is None:
            errors.append(f"{MAPS_VCSS_NAME}: missing /* BEGIN mv-map-palette */ … /* END mv-map-palette */ block")
        if ".mv-map-default" not in maps_raw:
            errors.append(f"{MAPS_VCSS_NAME}: missing required selector/class .mv-map-default")

        for stem, body in MV_MAP_STOCK_CLASS_RE.findall(stock_block.group(1) if stock_block else ""):
            if stem == "random" or stem.startswith("lobby_"):
                errors.append(f".mv-map-map_{stem}: random and lobby_* are not shipped")
            if not is_playable_map_id(stem):
                errors.append(f".mv-map-map_{stem}: not a playable map id")
            check_local_map_image(errors, f"mv-map-map_{stem}", f"map_{stem}", body)

        for stem, body in MV_MAP_CLASS_RE.findall(palette_block.group(1) if palette_block else ""):
            if stem.startswith("map_"):
                errors.append(f".mv-map-{stem}: official maps belong in the mv-map-stock block")
                continue
            if stem != "default" and not stem.startswith("ws_"):
                errors.append(f".mv-map-{stem}: workshop images must use a ws_ stem")
                continue
            map_id = stem if stem == "default" else stem[len("ws_") :]
            if not is_playable_map_id(map_id):
                errors.append(f".mv-map-{stem}: not a playable map id")
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
    if MAPS_VCSS_NAME not in includes:
        errors.append(f"{VOTE_XML_NAME}: must include mapvote/{MAPS_VCSS_NAME}")

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

    if len(found_ids) > PANEL_ID_ENGINE_CAP:
        errors.append(
            f"{VOTE_XML_NAME}: {len(found_ids)} panel ids exceed "
            f"m_vecPanelIds cap {PANEL_ID_ENGINE_CAP} (lower GRID_SLOT_COUNT)"
        )
    if len(found_vars) > DIALOG_VAR_ENGINE_CAP:
        errors.append(
            f"{VOTE_XML_NAME}: {len(found_vars)} dialog vars exceed "
            f"m_vecDialogVariableNames cap {DIALOG_VAR_ENGINE_CAP} (lower GRID_SLOT_COUNT)"
        )

    if not (IMAGES / "default.png").is_file() or not (IMAGES / "default.vtex").is_file():
        errors.append("default.png / default.vtex required under images/.../maps/")

    return errors


# ---- Layout v2 (T6): stricter than v1 - ruleset §5.1 subset only. ----
V2_XML = LAYOUTS / VOTE_V2_XML_NAME
V2_VCSS = STYLES / HUD_V2_VCSS_NAME
GENERATE_XML_V2 = ROOT / "tools" / "generate_vote_xml_v2.py"
V2_PROPS = frozenset(
    {
        "width", "height", "flow-children", "horizontal-align", "vertical-align",
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "background-color", "background-image", "background-size", "background-repeat", "background-img-opacity",
        "border", "border-top", "border-right", "border-bottom", "border-left", "border-color", "border-radius",
        "box-shadow", "font-size", "font-family", "color", "letter-spacing", "text-transform", "text-align",
        "text-overflow", "white-space", "visibility", "opacity", "overflow", "clip", "transform", "brightness",
        "transition-property", "transition-duration", "transition-timing-function",
        "animation-name", "animation-duration", "animation-delay", "animation-timing-function",
        "animation-iteration-count", "animation-fill-mode", "world-blur", "wash-color", "cursor", "sound", "z-index",
    }
)
V2_TRANSITION_OK = frozenset({"opacity", "transform", "clip", "background-color", "brightness"})
V2_HIT_PANELS = frozenset({"MapCatalog", "MapSlot", "FilterBar"})
# U15: text that must stay readable at 720p (1080p units).
V2_MIN_IMPORTANT = {
    "TimerText": 18, "StatusText": 18, "RankName": 18, "CardName": 18, "CurrentName": 18,
    "ChipLabel": 18, "HideLabel": 18, "RateLabel": 18, "RevealName": 18,
}
CSS_BLOCK_RE = re.compile(r"([^{}]+)\{([^{}]*)\}")


def v2_expected() -> tuple[set[str], set[str]]:
    """Ids and dialog variables the v2 contract promises (docstring of generate_vote_xml_v2.py)."""
    ids = {
        "Vote", "mv-timer", "mv-timer-fill", "mv-minimize", "mv-status", "mv-current", "mv-current-shot",
        "mv-current-fav", "mv-rate", "mv-like", "mv-dislike", "mv-rank", "mv-rank-empty", "mv-filters",
        "mv-fav-only", "mv-catalog", "mv-empty", "mv-win-shot",
    }
    ids |= {f"mv-ft-{i}" for i in range(V2_FILTER_TYPE_SLOTS)} | {f"mv-fs-{i}" for i in range(V2_FILTER_SORT_SLOTS)}
    for i in range(V2_RANK_SLOTS):
        ids |= {f"mv-rank-{i}", f"mv-rank-self-{i}", f"mv-rank-shot-{i}", f"mv-rank-fill-{i}"}
    for i in range(V2_GRID_SLOTS):
        ids |= {f"mv-slot-{i}", f"mv-{i}", f"mv-shot-{i}", f"mv-fav-{i}"}
    variables = {
        "mv_title", "mv_time", "mv_hide", "mv_status", "mv_cur_tag", "mv_cur_name", "mv_rate_q", "mv_like",
        "mv_dislike", "mv_like_n", "mv_dislike_n", "mv_rank_title", "mv_rank_empty", "mv_self", "mv_favonly",
        "mv_empty", "mv_win_tag", "mv_win_name", "mv_win_votes",
    }
    variables |= {f"ft{i}" for i in range(V2_FILTER_TYPE_SLOTS)} | {f"fs{i}" for i in range(V2_FILTER_SORT_SLOTS)}
    variables |= {f"rn{i}" for i in range(V2_RANK_SLOTS)} | {f"rv{i}" for i in range(V2_RANK_SLOTS)}
    variables |= {f"v{i}" for i in range(V2_GRID_SLOTS)} | {f"n{i}" for i in range(V2_GRID_SLOTS)}
    return ids, variables


def check_v2_css(raw: str) -> list[str]:
    name = HUD_V2_VCSS_NAME
    errors: list[str] = []
    css = re.sub(r"/\*.*?\*/", "", raw, flags=re.S)
    line_of = lambda pos: css.count("\n", 0, pos) + 1  # noqa: E731
    for m in BAD_CSS.finditer(css):
        errors.append(f"{name}:~{line_of(m.start())}: forbidden CSS near {m.group(0)!r}")
    for m in re.finditer(r":not\(|:selected|:disabled|:focus|\btransition\s*:", css):
        errors.append(f"{name}:~{line_of(m.start())}: forbidden selector/shorthand {m.group(0)!r}")
    if HIDDEN_CLASS.search(css):
        errors.append(f"{name}: forbidden class name hidden/Hidden")
    if css.count("{") != css.count("}"):
        errors.append(f"{name}: unbalanced braces")
    for prop in sorted(set(PROPERTY_RE.findall(css)) - V2_PROPS):
        errors.append(f"{name}: property {prop!r} is not in the ruleset §5.1 subset")
    for m in re.finditer(r"transition-property:\s*([^;]+);", css):
        for prop in (p.strip() for p in m.group(1).split(",")):
            if prop not in V2_TRANSITION_OK:
                errors.append(f"{name}:~{line_of(m.start())}: transition on {prop!r} (allowed: {sorted(V2_TRANSITION_OK)})")
    for m in KEYFRAMES_RE.finditer(css):
        if m.group(1)[0] not in "'\"":
            errors.append(f"{name}:~{line_of(m.start())}: @keyframes name must be quoted")
    if len(re.findall(r"animation-iteration-count:\s*infinite", css)) > 1:
        errors.append(f"{name}: more than one infinite animation (only is-urgent may pulse)")
    for m in re.finditer(r"font-size:\s*([0-9.]+)\s*;", css):
        if float(m.group(1)) < 14:
            errors.append(f"{name}:~{line_of(m.start())}: font-size {m.group(1)} below the minimum 14")
    for sel, body in CSS_BLOCK_RE.findall(css):
        size = re.search(r"font-size:\s*([0-9.]+)", body)
        for part in sel.split(","):
            cls = part.strip().lstrip(".")
            if size and cls in V2_MIN_IMPORTANT and float(size.group(1)) < V2_MIN_IMPORTANT[cls]:
                errors.append(f"{name}: .{cls} font-size {size.group(1)} < {V2_MIN_IMPORTANT[cls]} (U15)")
    for s in range(V2_SPAN_MIN, V2_SPAN_MAX + 1):
        if f".TimerFill.span-{s} " not in css:
            errors.append(f"{name}: missing countdown class span-{s}")
    for p in range(0, 101, V2_SHARE_STEP):
        if f".RankFill.p{p} " not in css:
            errors.append(f"{name}: missing share ladder class p{p}")
    for req in (".is-off", ".is-fading", ".is-minimized", ".view-reveal", ".view-browse", ".is-running",
                ".is-urgent", ".is-active", ".is-disabled", ".is-self", ".votes-some", ".ico-like", ".ico-dislike"):
        if req not in css:
            errors.append(f"{name}: missing required selector {req}")
    if "thumbs_up.vsvg" not in css or "rotateZ( 180deg )" not in css:
        errors.append(f"{name}: like/dislike must use the stock thumbs_up.vsvg (dislike rotated 180deg)")
    return errors


def collect_v2_errors() -> list[str]:
    errors: list[str] = []
    check = subprocess.run([sys.executable, str(GENERATE_XML_V2), "--check"], cwd=ROOT, capture_output=True, text=True)
    if check.returncode != 0:
        errors.append((check.stderr or check.stdout or f"{VOTE_V2_XML_NAME} out of date").strip())

    # Pool sizes = C# constants (grid and filter pools are shared with v1).
    for const, value in (("SlotCount", V2_GRID_SLOTS), ("RankSlots", V2_RANK_SLOTS),
                         ("FilterTypeSlots", V2_FILTER_TYPE_SLOTS), ("FilterSortSlots", V2_FILTER_SORT_SLOTS)):
        cs = csharp_const_int(VOTE_IDS_CS, const)
        if cs != value:
            errors.append(f"v2 pool {const}: generator {value} != VoteIds.{const} {cs}")

    if not V2_VCSS.is_file():
        return errors + [f"missing {V2_VCSS.relative_to(ROOT)}"]
    errors += check_v2_css(V2_VCSS.read_text(encoding="utf-8"))

    name = VOTE_V2_XML_NAME
    try:
        root = ET.parse(V2_XML).getroot()
    except (ET.ParseError, FileNotFoundError) as ex:
        return errors + [f"{name}: {ex}"]
    found_ids: list[str] = []
    found_vars: set[str] = set()
    includes: list[str] = []
    for el in root.iter():
        tag, attrs = el.tag, el.attrib
        if tag not in ALLOWED_TAGS:
            errors.append(f"{name}: forbidden tag <{tag}>")
        for attr in attrs:
            if attr not in ALLOWED_ATTRS:
                errors.append(f"{name}: rejected attr {attr!r} on <{tag}>")
        classes = set(attrs.get("class", "").split())
        if tag == "Button" and "hittest" in attrs:
            errors.append(f"{name}: do not set hittest on Button ({attrs.get('id')})")
        if tag == "Label" and attrs.get("hittest") != "false":
            errors.append(f"{name}: Label must have hittest=\"false\" ({attrs.get('id')})")
        if tag == "Panel" and attrs.get("hittest") != "false" and not classes & V2_HIT_PANELS:
            errors.append(f"{name}: Panel must have hittest=\"false\" ({attrs.get('id') or attrs.get('class')})")
        if tag == "include":
            m = INCLUDE_RE.search(attrs.get("src", ""))
            if m:
                includes.append(m.group(1))
        if "id" in attrs:
            found_ids.append(attrs["id"])
        text = attrs.get("text", "")
        found_vars.update(VAR_RE.findall(text))
        if tag == "Label" and text and not VAR_RE.fullmatch(text) and text != CREDIT_TEXT and not text.isdigit():
            errors.append(f"{name}: Label text must be {{s:…}}, a rank digit or the credit ({text!r})")

    if len(found_ids) != len(set(found_ids)):
        errors.append(f"{name}: duplicate panel ids")
    exp_ids, exp_vars = v2_expected()
    if set(found_ids) != exp_ids:
        errors.append(f"{name}: ids differ from contract: missing {sorted(exp_ids - set(found_ids))[:10]}, "
                      f"extra {sorted(set(found_ids) - exp_ids)[:10]}")
    if found_vars != exp_vars:
        errors.append(f"{name}: dialog vars differ from contract: missing {sorted(exp_vars - found_vars)[:10]}, "
                      f"extra {sorted(found_vars - exp_vars)[:10]}")
    if len(found_ids) >= PANEL_ID_ENGINE_CAP:
        errors.append(f"{name}: {len(found_ids)} panel ids, engine cap {PANEL_ID_ENGINE_CAP}")
    if len(found_vars) >= DIALOG_VAR_ENGINE_CAP:
        errors.append(f"{name}: {len(found_vars)} dialog vars, engine cap {DIALOG_VAR_ENGINE_CAP}")
    slots = sum(1 for el in root.iter("Panel") if "MapSlot" in el.attrib.get("class", "").split())
    ranks = sum(1 for el in root.iter("Button") if "RankRow" in el.attrib.get("class", "").split())
    if slots != V2_GRID_SLOTS or ranks != V2_RANK_SLOTS:
        errors.append(f"{name}: pools grid={slots} rank={ranks}, expected {V2_GRID_SLOTS}/{V2_RANK_SLOTS}")
    for el in root.iter():
        if el.attrib.get("id") == "Vote" and not {"is-off", "view-vote"} <= set(el.attrib.get("class", "").split()):
            errors.append(f"{name}: #Vote must start with is-off view-vote")
        if "MapSlot" in el.attrib.get("class", "") and "is-off" not in el.attrib.get("class", ""):
            errors.append(f"{name}: {el.attrib.get('id')} must start is-off")
    if includes != [HUD_V2_VCSS_NAME, MAPS_VCSS_NAME]:
        errors.append(f"{name}: must include {HUD_V2_VCSS_NAME} then {MAPS_VCSS_NAME} (got {includes})")
    if CREDIT_TEXT not in V2_XML.read_text(encoding="utf-8"):
        errors.append(f"{name}: missing credit footer")
    print(f"v2: {len(found_ids)}/{PANEL_ID_ENGINE_CAP} panel ids, {len(found_vars)}/{DIALOG_VAR_ENGINE_CAP} dialog vars")
    return errors


def main() -> int:
    errors = collect_errors() + collect_v2_errors()
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
        f"Panorama validation OK ({VOTE_XML_NAME} + {HUD_VCSS_NAME} + {MAPS_VCSS_NAME}); "
        f"preview synced → {PREVIEW_HTML.relative_to(ROOT)} + "
        f"{PREVIEW_BROWSE_HTML.relative_to(ROOT)}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
