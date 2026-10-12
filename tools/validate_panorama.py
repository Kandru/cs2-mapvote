#!/usr/bin/env python3
"""Validate the MapVote Panorama addon (workshop/content/panorama): XML + hud.vcss + images."""
from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "workshop" / "content" / "panorama"
LAYOUTS = CONTENT / "layout" / "custom_game" / "mapvote"
STYLES = CONTENT / "styles" / "custom_game" / "mapvote"
IMAGES = CONTENT / "images" / "custom_game" / "mapvote" / "maps"
HUD_VCSS = STYLES / "hud.vcss"
HUD_CSS = STYLES / "hud.css"
VOTE_XML = LAYOUTS / "vote.xml"
VOTE_HUD_CS = ROOT / "src" / "MapVote" / "Huds" / "VoteHud.cs"
MAP_CATALOG_CS = ROOT / "src" / "MapVote" / "Maps" / "MapCatalog.cs"
PREVIEW_HTML = ROOT / "workshop" / "preview" / "vote.html"

INCLUDE_RE = re.compile(r"s2r://panorama/styles/custom_game/mapvote/([A-Za-z0-9_]+\.vcss)")
VAR_RE = re.compile(r"\{s:([A-Za-z0-9_]+)\}")
HIDDEN_CLASS = re.compile(r"(?<![\w-])(?:hidden|Hidden)(?![\w-])")
PROPERTY_RE = re.compile(r"(?:^|[{;])\s*([a-z][a-z0-9-]*)\s*:", re.M)
MV_MAP_CLASS_RE = re.compile(r"\.mv-map-([a-z0-9_]+)\s*\{([^}]*)\}", re.S)
VTEX_FILE_RE = re.compile(r'"m_fileName"\s+"string"\s+"([^"]+)"')

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
    r")",
    re.I | re.M,
)
REJECTED_ATTRS = frozenset({"texturewidth", "textureheight", "style", "onactivate", "hittestchildren"})
ALLOWED_TAGS = frozenset({"root", "styles", "include", "snippets", "Panel", "Label", "Image", "Button"})
ALLOWED_ATTRS = frozenset({"id", "class", "hittest", "text", "src"})

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
        "text-shadow",
        "white-space",
        "line-height",
        "opacity",
        "brightness",
        "saturation",
        "contrast",
        "wash-color",
        "blur",
        "world-blur",
        "visibility",
        "overflow",
        "z-index",
        "transform",
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
        "tooltip-position",
        "sound",
        "sound-optical",
        "sound-pulse",
        "img-shadow",
        "ui-scale",
        "pre-transform-scale2d",
        "clip",
        "hue-rotation",
        "opacity-brush",
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


def collect_errors() -> list[str]:
    errors: list[str] = []

    if not VOTE_HUD_CS.is_file():
        return [f"missing {VOTE_HUD_CS.relative_to(ROOT)}"]

    slot_count = csharp_const_int(VOTE_HUD_CS, "SlotCount")
    root_panel = csharp_const_string(VOTE_HUD_CS, "Panel")

    if MAP_CATALOG_CS.is_file():
        max_maps = csharp_const_int(MAP_CATALOG_CS, "MaxMaps")
        if max_maps != slot_count:
            errors.append(
                f"MapCatalog.MaxMaps ({max_maps}) must equal VoteHud.SlotCount ({slot_count})"
            )

    expected_ids = {
        root_panel,
        "mv-grid",
        "mv-voted-row",
        "mv-winner",
        "mv-winner-shot",
        "mv-winner-name",
        "mv-winner-votes",
    }
    for i in range(slot_count):
        expected_ids.update(
            {
                f"mv-{i}",
                f"mv-shot-{i}",
                f"mv-votes-{i}",
                f"mv-voted-{i}",
                f"mv-voted-shot-{i}",
                f"mv-voted-votes-{i}",
            }
        )

    expected_vars = (
        {"winner_name", "winner_votes"}
        | {f"v{i}" for i in range(slot_count)}
        | {f"vv{i}" for i in range(slot_count)}
        | {f"n{i}" for i in range(slot_count)}
        | {f"vn{i}" for i in range(slot_count)}
    )

    if not HUD_VCSS.is_file():
        errors.append(f"missing {HUD_VCSS.relative_to(ROOT)}")
    else:
        css_raw = HUD_VCSS.read_text(encoding="utf-8")
        css = re.sub(r"/\*.*?\*/", "", css_raw, flags=re.S)
        if HIDDEN_CLASS.search(css):
            errors.append("hud.vcss uses forbidden class name hidden/Hidden")
        if BAD_CSS.search(css):
            for m in BAD_CSS.finditer(css):
                line = css.count("\n", 0, m.start()) + 1
                errors.append(f"hud.vcss:~{line}: forbidden CSS near {m.group(0)!r}")
        if css.count("{") != css.count("}"):
            errors.append("hud.vcss: unbalanced braces")
        for prop in PROPERTY_RE.findall(css):
            if prop not in KNOWN_PROPS:
                errors.append(f"hud.vcss: unknown/unsupported property {prop!r}")
        for req in (
            ".ph-off",
            ".ph-card",
            ".mv-bg",
            ".MapTile",
            ".MapShot",
            ".VotedRow",
            ".MapGrid",
            ".is-empty",
            ".is-voted",
            ".is-mine",
            ".mv-map-default",
        ):
            if req not in css_raw:
                errors.append(f"hud.vcss: missing required selector/class {req}")

        if not HUD_CSS.is_file():
            errors.append("hud.css missing (must be symlink to hud.vcss)")
        elif not HUD_CSS.is_symlink() or HUD_CSS.resolve() != HUD_VCSS.resolve():
            errors.append("hud.css must be a symlink to hud.vcss")

        for stem, body in MV_MAP_CLASS_RE.findall(css):
            png = IMAGES / f"{stem}.png"
            vtex = IMAGES / f"{stem}.vtex"
            if not png.is_file():
                errors.append(f"missing image for .mv-map-{stem}: {png.relative_to(ROOT)}")
            if not vtex.is_file():
                errors.append(f"missing vtex for .mv-map-{stem}: {vtex.relative_to(ROOT)}")
            else:
                vtext = vtex.read_text(encoding="utf-8")
                m = VTEX_FILE_RE.search(vtext)
                expected = f"panorama/images/custom_game/mapvote/maps/{stem}.png"
                if not m or m.group(1) != expected:
                    errors.append(f"{vtex.relative_to(ROOT)}: m_fileName must be {expected!r}")
            if "background-image" not in body:
                errors.append(f".mv-map-{stem}: missing background-image")

    if not VOTE_XML.is_file():
        errors.append(f"missing {VOTE_XML.relative_to(ROOT)}")
        return errors

    try:
        tree = ET.parse(VOTE_XML)
    except ET.ParseError as ex:
        errors.append(f"vote.xml: parse error: {ex}")
        return errors

    root = tree.getroot()
    found_ids: set[str] = set()
    found_vars: set[str] = set()
    includes: list[str] = []

    for el in root.iter():
        tag = el.tag
        if tag not in ALLOWED_TAGS:
            errors.append(f"vote.xml: forbidden tag <{tag}>")
        for attr, value in el.attrib.items():
            if attr not in ALLOWED_ATTRS:
                errors.append(f"vote.xml: rejected attr {attr!r} on <{tag}>")
            if attr in REJECTED_ATTRS:
                errors.append(f"vote.xml: rejected attr {attr!r}")
        if tag == "Button" and "hittest" in el.attrib:
            errors.append("vote.xml: do not set hittest on Button")
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

    for child in list(root):
        if child.tag == "Panel" and "id" in child.attrib:
            errors.append("vote.xml: outermost Panel must have no id")

    card = None
    for el in root.iter("Panel"):
        if el.attrib.get("id") == root_panel:
            card = el
            break
    if card is None:
        errors.append(f"vote.xml: missing root card id={root_panel!r}")
    else:
        classes = card.attrib.get("class", "").split()
        if "ph-off" not in classes:
            errors.append(f"vote.xml: #{root_panel} must start with ph-off")

    if "hud.vcss" not in includes:
        errors.append("vote.xml: must include mapvote/hud.vcss")

    missing_ids = expected_ids - found_ids
    extra_ids = found_ids - expected_ids
    if missing_ids:
        errors.append(f"vote.xml: missing ids: {sorted(missing_ids)}")
    if extra_ids:
        errors.append(f"vote.xml: unexpected ids: {sorted(extra_ids)}")

    missing_vars = expected_vars - found_vars
    extra_vars = found_vars - expected_vars
    if missing_vars:
        errors.append(f"vote.xml: missing dialog vars: {sorted(missing_vars)}")
    if extra_vars:
        errors.append(f"vote.xml: unexpected dialog vars: {sorted(extra_vars)}")

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
        "Panorama validation OK (vote.xml + hud.vcss + map images); "
        f"preview synced → {PREVIEW_HTML.relative_to(ROOT)}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
