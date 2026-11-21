#!/usr/bin/env python3
"""Generate workshop/preview from versioned vote XML + hud/maps VCSS so the browser mock stays in sync."""
from __future__ import annotations

import html
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from map_names import DEFAULT_STEM, is_playable_map_id
from mapvote_layout import HUD_VCSS_NAME, MAPS_VCSS_NAME, VOTE_XML_NAME

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "workshop" / "content" / "panorama"
VOTE_XML = CONTENT / "layout" / "custom_game" / "mapvote" / VOTE_XML_NAME
HUD_VCSS = CONTENT / "styles" / "custom_game" / "mapvote" / HUD_VCSS_NAME
MAPS_VCSS = CONTENT / "styles" / "custom_game" / "mapvote" / MAPS_VCSS_NAME
PREVIEW = ROOT / "workshop" / "preview"
OUT_HTML = PREVIEW / "vote_v2.html"
OUT_BROWSE_HTML = PREVIEW / "maps_v2.html"
OUT_CSS = PREVIEW / "hud_v2.preview.css"
CONFIG_DEFAULTS = ROOT / "src" / "MapVote" / "Config" / "ConfigDefaults.cs"
PLUGIN_CONFIG = ROOT / "src" / "MapVote" / "Config" / "PluginConfig.cs"
LANG = ROOT / "src" / "MapVote" / "lang" / "de.json"

TYPE_DEFAULT_RE = re.compile(
    r'new\(\)\s*\{\s*Id\s*=\s*"([^"]*)"\s*,\s*Prefixes\s*=\s*\[([^\]]*)\]\s*\}'
)
SORT_DEFAULT_RE = re.compile(
    r'new\(\)\s*\{\s*Id\s*=\s*"([^"]*)"\s*,\s*By\s*=\s*"([^"]*)"\s*,\s*Order\s*=\s*"([^"]*)"\s*\}'
)
PREFIX_RE = re.compile(r'"([^"]*)"')
NOMINATIONS_RE = re.compile(
    r"class NominationsConfig.*?Enabled\s*\{\s*get;\s*set;\s*\}\s*=\s*(true|false);",
    re.S,
)

TAGS = {"Panel": "div", "Label": "div", "Button": "button", "Image": "div"}

MAP_IMAGE_RE = re.compile(
    r'url\(\s*"s2r://panorama/images/custom_game/mapvote/maps/([A-Za-z0-9_]+)\.vtex"\s*\)'
)
S2R_URL_RE = re.compile(r'url\(\s*"s2r://([^"]+)"\s*\)')
GRADIENT_RE = re.compile(r"gradient\(\s*(?:linear|radial)\s*,([^;]*?)\)\s*;", re.I)
BOX_SHADOW_RE = re.compile(
    r"box-shadow:\s*(#[0-9a-fA-F]+)((?:\s+-?[\d.]+px){2,4})\s*;"
)
TEXT_SHADOW_RE = re.compile(
    r"text-shadow:\s*((?:#[0-9a-fA-F]+\s+-?[\d.]+px\s+-?[\d.]+px(?:\s+-?[\d.]+px)?\s*,?\s*)+);"
)
FONT_SIZE_RE = re.compile(r"font-size:\s*([0-9.]+)\s*;")
FONT_FAMILY_RE = re.compile(r"font-family:\s*([^;]+);")
BLUR_RE = re.compile(r"(?<![-\w])blur:\s*gaussian\(\s*([0-9.]+)\s*\)\s*;")
WORLD_BLUR_RE = re.compile(r"world-blur:\s*[^;]+;")
WASH_RE = re.compile(r"wash-color:\s*[^;]+;")
SAT_RE = re.compile(r"saturation:\s*([0-9.]+)\s*;")
BRIGHT_RE = re.compile(r"brightness:\s*([0-9.]+)\s*;")
SOUND_RE = re.compile(r"sound(?:-optical)?:\s*[^;]+;")
IGNORE_FLOW_RE = re.compile(r"ignore-parent-flow:\s*[^;]+;")
IMG_SHADOW_RE = re.compile(r"img-shadow:\s*[^;]+;")
PRE_SCALE_RE = re.compile(
    r"pre-transform-scale2d:\s*([0-9.]+)\s*,\s*([0-9.]+)\s*;"
)
SCALE3D_RE = re.compile(
    r"transform:\s*scale3d\(\s*([0-9.]+)\s*,\s*([0-9.]+)\s*,\s*[0-9.]+\s*\)\s*;"
)
OVERFLOW_RE = re.compile(r"overflow:\s*([^;]+);")
FIT_CHILDREN_RE = re.compile(r"(width|height):\s*fit-children\s*;")
FILL_FLOW_RE = re.compile(r"(width|height):\s*fill-parent-flow\([^)]*\)\s*;")
WIDTH_PERCENTAGE_RE = re.compile(
    r"height:\s*width-percentage\(\s*([0-9.]+)\s*%\s*\)\s*;"
)
VERT_MIDDLE_RE = re.compile(r"vertical-align:\s*middle\s*;")
VERT_BOTTOM_RE = re.compile(r"vertical-align:\s*bottom\s*;")
VERT_TOP_RE = re.compile(r"vertical-align:\s*top\s*;")
HORIZ_CENTER_RE = re.compile(r"horizontal-align:\s*center\s*;")
HORIZ_RIGHT_RE = re.compile(r"horizontal-align:\s*right\s*;")
HORIZ_LEFT_RE = re.compile(r"horizontal-align:\s*left\s*;")
BG_IMG_OPACITY_RE = re.compile(r"background-img-opacity:\s*([0-9.]+)\s*;")
CLIP_RECT_RE = re.compile(
    r"(?<![-\w])clip:\s*rect\(\s*([\d.]+)%\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*\)\s*;"
)
TRANSITION_PROP_RE = re.compile(r"transition-property:([^;]*);")
QUOTED_ANIM_RE = re.compile(r"""(@keyframes\s+|animation-name:\s*)['"]([^'"]+)['"]""")

# Stand-ins for stock Valve icons referenced by the HUD (browser cannot load s2r://).
S2R_ICON_STANDINS = {
    "panorama/images/icons/ui/favorite_no.vsvg": (
        "data:image/svg+xml,"
        "%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E"
        "%3Cpath fill='none' stroke='%23ffffff' stroke-width='2' "
        "d='M12 17.3 5.8 21l1.6-7.1L2 9.2l7.2-.6L12 2l2.8 6.6 7.2.6-5.4 4.7L18.2 21z'/%3E"
        "%3C/svg%3E"
    ),
    "panorama/images/icons/ui/favorite_yes.vsvg": (
        "data:image/svg+xml,"
        "%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E"
        "%3Cpath fill='%23ff9a1a' "
        "d='M12 17.3 5.8 21l1.6-7.1L2 9.2l7.2-.6L12 2l2.8 6.6 7.2.6-5.4 4.7L18.2 21z'/%3E"
        "%3C/svg%3E"
    ),
    "panorama/images/icons/ui/vote_check.vsvg": (
        "data:image/svg+xml,"
        "%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E"
        "%3Cpath fill='%232bff00' d='M9.2 17.2 4.8 12.8l1.4-1.4 3 3 8-8 1.4 1.4z'/%3E"
        "%3C/svg%3E"
    ),
    "panorama/images/icons/ui/check.svg": (
        "data:image/svg+xml,"
        "%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E"
        "%3Cpath fill='none' stroke='%23ffffff' stroke-width='3' "
        "d='M4 12.5 9.5 18 20 6'/%3E"
        "%3C/svg%3E"
    ),
    # Stock CS2 thumb; the dislike is the same icon turned by 180deg in CSS.
    "panorama/images/icons/ui/thumbs_up.vsvg": (
        "data:image/svg+xml,"
        "%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E"
        "%3Cpath fill='%23ffffff' d='M2 21h4V9H2v12zm20-11a2 2 0 0 0-2-2h-6.3l1-4.6v-.3a1.5 1.5 0 0 0-.4-1"
        "L13.2 1 6.6 7.6A2 2 0 0 0 6 9v10a2 2 0 0 0 2 2h9a2 2 0 0 0 1.8-1.2l3-7.1c.1-.2.2-.5.2-.7v-2z'/%3E"
        "%3C/svg%3E"
    ),
    "panorama/images/icons/ui/cancel.svg": (
        "data:image/svg+xml,"
        "%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E"
        "%3Cpath fill='none' stroke='%23ffffff' stroke-width='3' "
        "d='M6 6l12 12M18 6 6 18'/%3E"
        "%3C/svg%3E"
    ),
}


def translate_gradient(body: str) -> str:
    """Panorama `gradient(linear, start, end, …)` → CSS `linear-gradient(...)`."""
    pts = re.findall(r"(\d+(?:\.\d+)?)\s*%\s+(\d+(?:\.\d+)?)\s*%", body)
    direction = "to bottom"
    if len(pts) >= 2:
        x0, y0 = float(pts[0][0]), float(pts[0][1])
        x1, y1 = float(pts[1][0]), float(pts[1][1])
        dx, dy = x1 - x0, y1 - y0
        if abs(dx) >= abs(dy):
            direction = "to right" if dx > 0 else "to left"
        else:
            direction = "to bottom" if dy > 0 else "to top"

    stops: list[str] = []
    from_m = re.search(r"from\(\s*(#[0-9a-fA-F]+)\s*\)", body)
    if from_m:
        stops.append(from_m.group(1))
    for m in re.finditer(
        r"color-stop\(\s*([\d.]+)\s*,\s*(#[0-9a-fA-F]+)\s*\)", body
    ):
        pct = float(m.group(1)) * 100
        stops.append(f"{m.group(2)} {pct:g}%")
    to_m = re.search(r"to\(\s*(#[0-9a-fA-F]+)\s*\)", body)
    if to_m:
        stops.append(to_m.group(1))
    return f"linear-gradient({direction}, {', '.join(stops) or '#444'});"


def translate_text_shadow(raw: str) -> str:
    parts = []
    for m in re.finditer(
        r"(#[0-9a-fA-F]+)\s+(-?[\d.]+px)\s+(-?[\d.]+px)(?:\s+(-?[\d.]+px))?",
        raw,
    ):
        color, x, y, blur = m.group(1), m.group(2), m.group(3), m.group(4) or "0px"
        parts.append(f"{x} {y} {blur} {color}")
    return f"text-shadow: {', '.join(parts)};" if parts else "text-shadow: none;"


def quote_font_family(raw: str) -> str:
    """Return a full `font-family` (+ weight) declaration for a Panorama face name."""
    name = raw.strip().strip("\"'")
    if not name:
        return "font-family: sans-serif;"
    # Stratum2 is rarely installed; Barlow Condensed matches the condensed metric.
    # Face weight is in the Panorama family name - browsers need an explicit weight.
    stack = f'"{name}", "Barlow Condensed", "Arial Narrow", sans-serif'
    # Panorama bakes weight into the face name; browsers need an explicit weight.
    if "Bold" in name or name == "Stratum2":
        return f"font-family: {stack}; font-weight: 700;"
    if "Medium" in name:
        return f"font-family: {stack}; font-weight: 500;"
    return f"font-family: {stack}; font-weight: 400;"


def translate_s2r(path: str) -> str:
    path = path.strip()
    if path in S2R_ICON_STANDINS:
        return f'url("{S2R_ICON_STANDINS[path]}")'
    m = re.fullmatch(
        r"panorama/images/custom_game/mapvote/maps/([A-Za-z0-9_]+)\.vtex", path
    )
    if m:
        return f'url("../content/panorama/images/custom_game/mapvote/maps/{m.group(1)}.png")'
    return (
        'url("data:image/svg+xml,%3Csvg xmlns=\'http://www.w3.org/2000/svg\' '
        'width=\'8\' height=\'8\'%3E%3C/svg%3E")'
    )


def translate_css(text: str) -> str:
    """Map Panorama UI CSS → browser CSS. Keep selectors/values as close as possible."""
    t = text
    t = GRADIENT_RE.sub(lambda m: translate_gradient(m.group(1)), t)
    # Panorama puts gradients on background-color; browsers need background-image.
    t = re.sub(
        r"background-color:\s*(linear-gradient\([^;]+\)\s*);",
        r"background-image: \1;",
        t,
    )
    t = BOX_SHADOW_RE.sub(lambda m: f"box-shadow:{m.group(2)} {m.group(1)};", t)
    t = TEXT_SHADOW_RE.sub(lambda m: translate_text_shadow(m.group(1)), t)
    t = FONT_SIZE_RE.sub(r"font-size: \1px;", t)
    t = FONT_FAMILY_RE.sub(lambda m: quote_font_family(m.group(1)), t)
    t = WORLD_BLUR_RE.sub("backdrop-filter: blur(10px);", t)
    t = BLUR_RE.sub(r"filter: blur(\1px);", t)
    t = WASH_RE.sub("", t)
    t = SOUND_RE.sub("", t)
    t = IMG_SHADOW_RE.sub("", t)
    t = PRE_SCALE_RE.sub(r"transform: scale(\1, \2);", t)
    t = SCALE3D_RE.sub(r"transform: scale(\1, \2);", t)
    t = IGNORE_FLOW_RE.sub("position: absolute; inset: 0;", t)
    t = BG_IMG_OPACITY_RE.sub(
        lambda m: f"opacity: {m.group(1)}; /* background-img-opacity */", t
    )

    # Panorama clip rect (t, r, b, l in %) -> clip-path inset; keyframe names unquoted for browsers.
    t = CLIP_RECT_RE.sub(
        lambda m: (
            f"clip-path: inset({m.group(1)}% {100 - float(m.group(2)):g}% "
            f"{100 - float(m.group(3)):g}% {m.group(4)}%);"
        ),
        t,
    )
    t = TRANSITION_PROP_RE.sub(
        lambda m: "transition-property:" + re.sub(r"\bclip\b", "clip-path", m.group(1)) + ";", t
    )
    t = QUOTED_ANIM_RE.sub(r"\1\2", t)
    t = t.replace("background-size: contains;", "background-size: contain;")
    # A later fixed height replaces fill-parent-flow in Panorama; flex-grow would keep it in Chromium.
    t = t.replace("height: 0px;", "height: 0px; flex: none;")
    # Panorama labels clip their own text; browsers need overflow for the ellipsis.
    t = t.replace("text-overflow: ellipsis;", "text-overflow: ellipsis; overflow: hidden;")

    t = SAT_RE.sub(r"filter: saturate(\1); /* saturation */", t)
    t = BRIGHT_RE.sub(r"filter: brightness(\1); /* brightness */", t)

    # Panorama flow → CSS flex / grid stack.
    t = t.replace(
        "flow-children: none;",
        "display: grid; grid-template: minmax(0, 1fr) / minmax(0, 1fr);",
    )
    t = t.replace("flow-children: down;", "display: flex; flex-direction: column;")
    t = t.replace(
        "flow-children: right-wrap;",
        "display: flex; flex-direction: row; flex-wrap: wrap;",
    )
    t = t.replace(
        "flow-children: right;",
        "display: flex; flex-direction: row; flex-wrap: nowrap;",
    )
    t = t.replace("visibility: collapse;", "display: none;")
    # Undo of a collapse; a rule that needs a column/stack also states its flow-children after this.
    t = t.replace("visibility: visible;", "display: flex;")
    t = WIDTH_PERCENTAGE_RE.sub(
        lambda m: f"aspect-ratio: 100 / {m.group(1)}; height: auto;",
        t,
    )
    t = FIT_CHILDREN_RE.sub(r"\1: auto;", t)
    t = FILL_FLOW_RE.sub(
        lambda m: (
            "flex: 1 1 auto; min-height: 0;"
            if m.group(1) == "height"
            else "flex: 1 1 0; min-width: 0;"
        ),
        t,
    )

    def _overflow_css(raw: str) -> str:
        parts = raw.strip().split()

        def axis(token: str) -> str:
            if token == "scroll":
                return "auto"
            if token == "noclip":
                return "visible"
            if token in ("clip", "squish"):
                return "hidden"
            return token

        if len(parts) == 2:
            return f"overflow-x: {axis(parts[0])}; overflow-y: {axis(parts[1])};"
        if len(parts) == 1 and parts[0] == "scroll":
            return "overflow: auto;"
        if len(parts) == 1 and parts[0] == "noclip":
            return "overflow: visible;"
        if len(parts) == 1 and parts[0] in ("clip", "squish"):
            return "overflow: hidden;"
        return f"overflow: {raw};"

    t = OVERFLOW_RE.sub(lambda m: _overflow_css(m.group(1)), t)
    # Align works for both flex (self) and grid (self) children of flow-none stacks.
    t = VERT_MIDDLE_RE.sub("align-self: center;", t)
    t = VERT_BOTTOM_RE.sub("align-self: end;", t)
    t = VERT_TOP_RE.sub("align-self: start;", t)
    t = HORIZ_CENTER_RE.sub("justify-self: center; margin-left: auto; margin-right: auto;", t)
    t = HORIZ_RIGHT_RE.sub("justify-self: end; margin-left: auto;", t)
    t = HORIZ_LEFT_RE.sub("justify-self: start; margin-right: auto;", t)

    t = MAP_IMAGE_RE.sub(
        r'url("../content/panorama/images/custom_game/mapvote/maps/\1.png")', t
    )
    t = S2R_URL_RE.sub(lambda m: translate_s2r(m.group(1)), t)
    return t


def render_node(node: ET.Element, out: list[str], mark_hit: bool = False) -> None:
    tag = TAGS.get(node.tag)
    if tag is None:
        return

    cls = node.get("class", "")
    ident = node.get("id", "")
    attrs = []
    if cls:
        attrs.append(f'class="{html.escape(cls)}"')
    if ident:
        attrs.append(f'id="{html.escape(ident)}"')
        attrs.append(f'data-id="{html.escape(ident)}"')
    if mark_hit and node.get("hittest") == "false":
        attrs.append("data-nohit")

    text = node.get("text", "")
    body = ""
    if text:
        m = re.fullmatch(r"\{s:([^}]+)\}", text.strip())
        if m:
            body = f'<span class="pv-var" data-var="{html.escape(m.group(1))}"></span>'
        else:
            body = html.escape(text)

    attr_s = (" " + " ".join(attrs)) if attrs else ""
    out.append(f"<{tag}{attr_s}>{body}")
    for child in node:
        render_node(child, out, mark_hit)
    out.append(f"</{tag}>")


PREVIEW_MAP_ORDER = [
    "de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_overpass", "de_ancient",
    "de_anubis", "de_vertigo", "cs_office", "cs_italy", "ar_baggage", "ar_shoots",
    "de_cache", "de_train", "cs_agency", "cs_assault", "cs_militia", "de_basalt",
    "de_brewery", "de_edin", "de_palacio", "de_whistle", "de_dogtown", "de_jura",
    "de_golden", "de_palais", "de_thera", "de_mills", "de_assembly", "de_boulder",
    "de_canals", "de_lake", "de_safehouse", "de_shortdust", "de_tuscan", "de_cbble",
    "cs_alpine", "cs_shelter", "ar_pool_day", "de_debris", "de_eldorado",
    "de_fachwerk", "de_grail", "de_memento", "de_poseidon", "de_rooftop",
    "de_sanctum", "de_stronghold", "de_sugarcane", "de_transit",
]


def preview_config() -> dict[str, object]:
    """Read runtime defaults, nominations default and German strings so the mock cannot drift."""
    if not CONFIG_DEFAULTS.is_file() or not PLUGIN_CONFIG.is_file() or not LANG.is_file():
        raise ValueError(f"missing {CONFIG_DEFAULTS}, {PLUGIN_CONFIG} or {LANG}")
    defaults = CONFIG_DEFAULTS.read_text(encoding="utf-8")
    config_source = PLUGIN_CONFIG.read_text(encoding="utf-8")
    lang = json.loads(LANG.read_text(encoding="utf-8"))

    types = [
        {
            "id": type_id,
            "label": lang.get(f"filter.{type_id}", type_id),
            "prefixes": PREFIX_RE.findall(prefixes),
        }
        for type_id, prefixes in TYPE_DEFAULT_RE.findall(defaults)
    ]
    sorts = [
        {
            "id": sort_id,
            "label": lang.get(f"sort.{sort_id}", sort_id),
            "by": by,
            "order": order,
        }
        for sort_id, by, order in SORT_DEFAULT_RE.findall(defaults)
    ]
    if not types or not sorts:
        raise ValueError("could not read default filter types/sorts from ConfigDefaults.cs")
    nominations = NOMINATIONS_RE.search(config_source)
    if nominations is None:
        raise ValueError("could not read the nominations default from PluginConfig.cs")

    types.insert(0, {"id": "all", "label": lang.get("filter.all", "All"), "prefixes": []})
    return {
        "types": types,
        "sorts": sorts,
        "text": lang,
        "nominationsEnabled": nominations.group(1) == "true",
    }


def preview_maps() -> list[str]:
    """Preview maps with a bundled .webp, preferring the curated order."""
    image_dir = PREVIEW / "maps"
    on_disk = {
        path.stem
        for path in image_dir.glob("*.webp")
        if path.is_file() and path.stem != DEFAULT_STEM and is_playable_map_id(path.stem)
    }
    ordered = [name for name in PREVIEW_MAP_ORDER if name in on_disk]
    return ordered + sorted(on_disk - set(ordered))


SHELL_CSS = """
/* Browser host only. Visual layout comes from the translated hud_v2.vcss below. */
@import url("https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@500;600;700&display=swap");

* { box-sizing: border-box; }
html, body { height: 100%; margin: 0; }
body { background: #0a0c10; color: #f5f7fa; overflow: hidden; font-family: "Barlow Condensed", sans-serif; }
div, button { min-width: 0; min-height: 0; flex-shrink: 0; line-height: 1.15; }
button { background: none; border: 0; font: inherit; color: inherit; cursor: pointer; text-align: inherit; padding: 0; appearance: none; }
.pv-plate { position: fixed; inset: 0; background: #0a0c10 url("endmatch.jpg") center center / cover no-repeat; overflow: hidden; }
.HudScreen { position: absolute; inset: 0; }
/* Panorama paints later siblings on top; in Chromium the blurred glass would form a higher stacking layer. */
.VoteColumn { position: relative; z-index: 1; }
/* hittest="false" */
[data-nohit] { pointer-events: none; }
button { pointer-events: auto; }
.pv-bar { position: fixed; left: 12px; bottom: 36px; z-index: 20; display: flex; gap: 6px; align-items: center;
  font: 12px/1.4 ui-monospace, monospace; }
.pv-bar a { border: 1px solid #ffffff22; background: #0b0f14e0; padding: 3px 9px; border-radius: 3px; color: #d7deea; text-decoration: none; }
.pv-bar a.is-active { border-color: #f0a531; color: #ffd27a; }
.pv-bar .pv-sep { width: 1px; height: 18px; background: #ffffff33; margin: 0 4px; }
"""

STACK_CLASS_RE = re.compile(r"^\.([A-Za-z][\w-]*)\s*\{[^}]*flow-children:\s*none", re.M)


def sync() -> list[str]:
    """Generate the v2 browser preview from the same XML/CSS the plugin ships."""
    for path in (VOTE_XML, HUD_VCSS, MAPS_VCSS):
        if not path.is_file():
            return [f"missing {path.relative_to(ROOT)}"]
    try:
        config = preview_config()
    except (OSError, ValueError, json.JSONDecodeError) as ex:
        return [f"preview config: {ex}"]
    try:
        root = ET.parse(VOTE_XML).getroot()
    except ET.ParseError as ex:
        return [f"{VOTE_XML_NAME}: parse error: {ex}"]
    body_parts: list[str] = []
    for child in root:
        if child.tag != "styles":
            render_node(child, body_parts, mark_hit=True)
    body = "".join(body_parts)

    hud = HUD_VCSS.read_text(encoding="utf-8")
    # flow-children: none stacks children; the browser grid needs every child in the one cell.
    # position: relative keeps DOM paint order when a child forms its own layer (filter, opacity).
    stacks = "\n".join(f".{c} > * {{ grid-area: 1 / 1; position: relative; }}" for c in STACK_CLASS_RE.findall(hud))
    css = [f"/* AUTO-GENERATED from {HUD_VCSS_NAME} + {MAPS_VCSS_NAME} by tools/sync_preview.py - do not edit. */\n", SHELL_CSS, stacks]
    for path in (HUD_VCSS, MAPS_VCSS):
        translated = "\n".join(line.rstrip() for line in translate_css(path.read_text(encoding="utf-8")).splitlines())
        css.append(f"\n/* ---- {path.name} (translated) ---- */\n{translated}\n")
    PREVIEW.mkdir(parents=True, exist_ok=True)
    OUT_CSS.write_text("\n".join(css), encoding="utf-8", newline="\n")

    maps_json = json.dumps(preview_maps(), separators=(",", ":"))
    config_json = json.dumps(config, ensure_ascii=False, separators=(",", ":"))
    for out, browse, title in ((OUT_HTML, "0", "MapVote preview"), (OUT_BROWSE_HTML, "1", "MapVote map browser preview")):
        out.write_text(
            "<!DOCTYPE html>\n<html lang=\"de\">\n<head>\n<meta charset=\"utf-8\" />\n"
            f"<title>{title}</title>\n<link rel=\"stylesheet\" href=\"hud_v2.preview.css\" />\n</head>\n"
            f"<body data-browse=\"{browse}\">\n<div class=\"pv-plate\">\n{body}\n</div>\n"
            f"<script>window.PV_MAPS = {maps_json}; window.PV_CONFIG = {config_json};</script>\n"
            "<script src=\"preview_v2.js\"></script>\n"
            "</body>\n</html>\n",
            encoding="utf-8",
            newline="\n",
        )
    return []


def main() -> int:
    errors = sync()
    if errors:
        print("Preview sync failed:")
        for err in errors:
            print(f"  - {err}")
        return 1
    print(
        f"Preview synced → {OUT_HTML.relative_to(ROOT)} + "
        f"{OUT_BROWSE_HTML.relative_to(ROOT)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
