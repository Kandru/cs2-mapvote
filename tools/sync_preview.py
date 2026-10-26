#!/usr/bin/env python3
"""Generate workshop/preview from versioned vote XML + hud VCSS so the browser mock stays in sync."""
from __future__ import annotations

import html
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from mapvote_layout import HUD_VCSS_NAME, VOTE_XML_NAME

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "workshop" / "content" / "panorama"
VOTE_XML = CONTENT / "layout" / "custom_game" / "mapvote" / VOTE_XML_NAME
HUD_VCSS = CONTENT / "styles" / "custom_game" / "mapvote" / HUD_VCSS_NAME
PREVIEW = ROOT / "workshop" / "preview"
OUT_HTML = PREVIEW / "vote.html"
OUT_CSS = PREVIEW / "hud.preview.css"
OUT_JS = PREVIEW / "vote.js"

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
    # Face weight is in the Panorama family name — browsers need an explicit weight.
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


def render_node(node: ET.Element, out: list[str]) -> None:
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
        render_node(child, out)
    out.append(f"</{tag}>")


SHELL_CSS = """
/* Browser host only. Visual layout comes from the translated hud.vcss below. */
@import url("https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@500;600;700&display=swap");

* { box-sizing: border-box; }
html, body { height: 100%; margin: 0; }
body {
  min-height: 100vh;
  background: #0a0c10;
  font-family: "Barlow Condensed", "Arial Narrow", system-ui, sans-serif;
  color: #f2f5ff;
  overflow: hidden;
}
button {
  background: none;
  border: 0 solid transparent;
  font: inherit;
  color: inherit;
  cursor: pointer;
  text-align: inherit;
  padding: 0;
  appearance: none;
}

.pv-plate {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: 100vh;
  background: #0a0c10 url("endmatch.jpg") center center / cover no-repeat;
  overflow: hidden;
}
.HudScreen {
  position: absolute;
  inset: 0;
}

/* Preview forces the card open; game starts with is-off. */
.VoteRoot.is-off {
  display: grid !important;
  opacity: 1 !important;
}

/* Panorama flow-children: none → CSS grid stack: every direct child shares the cell.
 * VoteTopBar / FilterChip use flex centering instead — do not force grid-area there. */
.VoteRoot > *,
.VotedRow > *,
.MapSlot > *,
.MapTile > *,
.MapGridHost > *,
.FavBtn > *,
.CreditBar > * {
  grid-area: 1 / 1;
  min-width: 0;
  min-height: 0;
}

.VoteColumn {
  z-index: 1;
  width: 100%;
  height: 100%;
}

/*
 * Vertical centering in the browser preview.
 * Panorama uses vertical-align:middle; Chromium does not. Match CreditLabel:
 * single-line labels use line-height == box height (not flex + em-box math).
 */
.VoteTopBar {
  position: relative;
  flex-shrink: 0;
  height: 50px !important;
}
.VoteTopBar > .VoteTitle {
  position: absolute !important;
  left: 0 !important;
  right: 0 !important;
  top: 50% !important;
  bottom: auto !important;
  width: 100% !important;
  height: auto !important;
  margin: 0 !important;
  transform: translateY(-50%) !important;
  display: block !important;
  text-align: center !important;
  line-height: normal !important;
}
.VoteTopBar > .MinimizeBtn {
  position: absolute !important;
  top: 50% !important;
  right: 16px !important;
  left: auto !important;
  bottom: auto !important;
  transform: translateY(-50%) !important;
  margin: 0 !important;
  display: block !important;
  justify-self: auto !important;
  align-self: auto !important;
  line-height: 0 !important; /* label owns the line-height */
}
.FilterBar,
.FilterTypeRow,
.FilterSortRow {
  display: flex !important;
  flex-direction: row !important;
  align-items: center !important;
}
.FilterBar {
  width: max-content !important;
  max-width: 100%;
  margin-left: auto !important;
  margin-right: auto !important;
}
.FilterChip {
  display: block !important;
  line-height: 0 !important;
}
.FilterChip.is-off {
  display: none !important;
}
/* 28px chip/btn − 1px border × 2 → 26px content box */
.MinimizeLabel,
.FilterChipLabel {
  width: fit-content;
  height: 26px !important;
  display: block !important;
  text-align: center !important;
  line-height: 26px !important;
  margin: 0 auto !important;
  transform: none;
}
.VoteTitle .pv-var,
.MinimizeLabel .pv-var,
.FilterChipLabel .pv-var {
  display: inline !important;
  line-height: inherit !important;
  height: auto !important;
  width: auto;
  margin: 0 !important;
  transform: none !important;
}
.mv-glass,
.mv-scrim {
  width: 100%;
  height: 100%;
  pointer-events: none;
}

/* Labels / tile chrome never capture clicks (Panorama: hittest=false). */
.MapName,
.MapVotes,
.VotedHint,
.FilterChipLabel,
.MinimizeLabel,
.CreditLabel,
.VoteTitle,
.MapEmpty,
.MapWinnerCheck,
.MapFade,
.MapShot,
.FavIcon {
  pointer-events: none;
}

.MapSlot > .FavBtn {
  position: relative;
  z-index: 4;
}

/*
 * Panorama stacks flow-children:none via z-index. Chromium grid + transform on
 * .MapShot can paint the shot above .MapFade — pin shot/fade absolutely.
 */
.MapTile {
  position: relative;
}
.MapTile > .MapShot,
.MapTile > .MapFade,
.MapTile > .MapWinnerCheck {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
}
.MapTile > .MapShot { z-index: 0; }
.MapTile > .MapFade { z-index: 1; }
.MapTile > .MapName,
.MapTile > .MapVotes {
  position: absolute;
  left: 0;
  right: 0;
  z-index: 2;
  /* vertical-align:bottom → bottom edge (margin-bottom comes from hud.vcss). */
  top: auto;
  bottom: 0;
  height: auto;
  align-self: auto;
}
.MapTile > .MapWinnerCheck { z-index: 3; }

/* Map screenshots in the preview come from JS (preview/maps/*.webp). */
.MapShot {
  background-size: 343px 193px;
  background-position: 50% 50%;
  background-repeat: no-repeat;
  transform-origin: center center;
}

.pv-var { min-width: 0.5ch; display: inline-block; }
.pv-bar {
  position: fixed;
  left: 12px;
  bottom: 40px;
  z-index: 20;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  font: 12px/1.4 ui-monospace, monospace;
  color: #8a93a0;
  max-width: 70ch;
}
.pv-bar button {
  border: 1px solid #ffffff22;
  background: #ffffff10;
  padding: 4px 10px;
  border-radius: 3px;
  color: #d7deea;
  cursor: pointer;
}
.pv-bar button:hover { background: #ffffff1c; }
.pv-bar button.is-active {
  border-color: #ff9a1a88;
  background: #ff9a1a22;
  color: #ffd27a;
}
.pv-bar .pv-note { opacity: 0.85; }
.pv-bar .pv-sep {
  width: 1px;
  height: 18px;
  background: #ffffff22;
  margin: 0 2px;
}
"""

VOTE_JS = r"""(() => {
  const SLOT = (() => {
    let n = 0;
    document.querySelectorAll('[id^="mv-"]').forEach((el) => {
      const m = /^mv-(\d+)$/.exec(el.id);
      if (m) n = Math.max(n, Number(m[1]) + 1);
    });
    return n || 50;
  })();

  // Prefer maps that have screenshots under preview/maps/*.webp
  const MAPS = [
    "de_dust2","de_mirage","de_inferno","de_nuke","de_overpass","de_ancient","de_anubis","de_vertigo",
    "cs_office","cs_italy","ar_baggage","ar_shoots","de_cache","de_train","cs_agency","cs_assault",
    "cs_militia","de_basalt","de_brewery","de_edin","de_palacio","de_whistle","de_dogtown",
    "de_jura","de_golden","de_palais","de_thera","de_mills","de_assembly","de_boulder",
    "de_canals","de_lake","de_safehouse","de_shortdust","de_tuscan","de_cbble","cs_alpine",
    "cs_shelter","ar_pool_day","de_debris","de_eldorado","de_fachwerk","de_grail","de_memento",
    "de_poseidon","de_rooftop","de_sanctum","de_stronghold","de_sugarcane","de_transit"
  ].slice(0, SLOT);

  const stem = (map) => map.replace(/[^a-z0-9_]/gi, "_").toLowerCase();

  const applyShot = (el, map) => {
    const s = stem(map);
    el.classList.add("MapShot");
    el.style.backgroundImage = `url("maps/${s}.webp")`;
    el.style.backgroundSize = "350px 197px";
    el.style.backgroundPosition = "50% 50%";
    el.style.backgroundRepeat = "no-repeat";
    const probe = new Image();
    // Clear overlay on miss so the default MapShot sibling shows through.
    probe.onerror = () => {
      el.style.backgroundImage = "";
    };
    probe.src = `maps/${s}.webp`;
  };

  const TYPES = [
    { id: "all", label: "All", prefixes: null },
    { id: "bomb", label: "Bomb", prefixes: ["de_"] },
    { id: "hostage", label: "Hostage", prefixes: ["cs_"] },
    { id: "arms_race", label: "Arms Race", prefixes: ["ar_"] },
    { id: "danger_zone", label: "Danger Zone", prefixes: ["dz_"] },
    { id: "other", label: "Other", prefixes: [] },
  ];
  const SORTS = [
    { id: "most_played", label: "Most played" },
    { id: "least_played", label: "Least played" },
    { id: "favorites", label: "Favorites" },
  ];

  const state = {
    maps: MAPS.slice(),
    votes: Object.fromEntries(MAPS.map((m) => [m, 0])),
    plays: Object.fromEntries(MAPS.map((m, i) => [m, (MAPS.length - i) % 17])),
    favorites: new Set(),
    firstVote: [],
    mine: null,
    mineSet: new Set(),
    mode: "single", // single = CS2 one-vote; multi = toggle many maps
    reveal: false,
    winner: null,
    type: 0,
    sort: -1,
  };

  const isMine = (map) =>
    state.mode === "multi" ? state.mineSet.has(map) : state.mine === map;

  const setVar = (name, value) => {
    document.querySelectorAll(`[data-var="${name}"]`).forEach((el) => {
      el.textContent = value;
    });
  };

  const COLS = 10;
  const COLS_WIDTH_MAX = 6;
  const ROW_COUNT = Math.ceil(SLOT / COLS);

  const clearTile = (slotId, tileId, shotId, favId, voteVar, nameVar) => {
    const slot = document.getElementById(slotId);
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    const fav = document.getElementById(favId);
    if (!slot || !tile || !shot) return;
    slot.classList.add("is-off");
    tile.classList.remove("is-self", "is-done", "is-lost");
    fav?.classList.remove("is-on");
    shot.className = "MapShot";
    shot.style.backgroundImage = "";
    setVar(voteVar, "");
    setVar(nameVar, "");
  };

  const paintTile = (slotId, tileId, shotId, favId, voteVar, nameVar, map) => {
    const slot = document.getElementById(slotId);
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    const fav = document.getElementById(favId);
    if (!slot || !tile || !shot) return;
    const count = state.votes[map] || 0;
    const isWinner = state.winner === map;
    const isLost = state.reveal && state.winner && !isWinner;
    slot.classList.remove("is-off");
    tile.classList.toggle("is-self", !state.reveal && isMine(map));
    tile.classList.toggle("is-done", isWinner);
    tile.classList.toggle("is-lost", !!isLost);
    fav?.classList.toggle("is-on", state.favorites.has(map));
    applyShot(shot, map);
    setVar(voteVar, String(count));
    setVar(nameVar, map);
  };

  const matchesType = (map, type) => {
    if (!type || type.prefixes === null) return true;
    if (type.prefixes.length === 0) {
      return !TYPES.some(
        (t) => t.prefixes && t.prefixes.length > 0 && t.prefixes.some((p) => map.startsWith(p))
      );
    }
    return type.prefixes.some((p) => map.startsWith(p));
  };

  const filteredMaps = () => {
    const type = TYPES[state.type] || TYPES[0];
    let maps = state.maps.filter((m) => matchesType(m, type));
    if (state.sort === 0) {
      maps = maps
        .slice()
        .sort((a, b) => (state.plays[b] || 0) - (state.plays[a] || 0) || state.maps.indexOf(a) - state.maps.indexOf(b));
    } else if (state.sort === 1) {
      maps = maps
        .slice()
        .sort((a, b) => (state.plays[a] || 0) - (state.plays[b] || 0) || state.maps.indexOf(a) - state.maps.indexOf(b));
    } else if (state.sort === 2) {
      maps = maps.filter((m) => state.favorites.has(m));
    }
    return maps;
  };

  const typeHasMaps = (type) =>
    state.maps.some((m) => matchesType(m, type));

  const sortEnabled = (sort) =>
    sort.id !== "favorites" || state.favorites.size > 0;

  const paintFilters = () => {
    const bar = document.getElementById("mv-filters");
    if (bar) bar.classList.remove("is-off");

    // Drop a type selection that no longer has maps (mirrors C# PaintFilterBar).
    if (state.type > 0 && state.type < TYPES.length && !typeHasMaps(TYPES[state.type])) {
      state.type = 0;
    }
    if (state.sort >= 0 && state.sort < SORTS.length && !sortEnabled(SORTS[state.sort])) {
      state.sort = -1;
    }

    for (let i = 0; i < 8; i++) {
      const chip = document.getElementById(`mv-ft-${i}`);
      if (!chip) continue;
      if (i < TYPES.length) {
        const disabled = i > 0 && !typeHasMaps(TYPES[i]);
        chip.classList.remove("is-off");
        chip.classList.toggle("is-disabled", disabled);
        chip.classList.toggle("is-active", state.type === i);
        setVar(`ft${i}`, TYPES[i].label);
      } else {
        chip.classList.add("is-off");
        chip.classList.remove("is-disabled", "is-active");
      }
    }

    const favSortIndex = SORTS.findIndex((s) => s.id === "favorites");
    const favChip = document.getElementById("mv-sort-fav");
    if (favChip) {
      if (favSortIndex >= 0) {
        const disabled = !sortEnabled(SORTS[favSortIndex]);
        favChip.classList.remove("is-off");
        favChip.classList.toggle("is-disabled", disabled);
        favChip.classList.toggle("is-active", state.sort === favSortIndex);
        setVar("sfav", SORTS[favSortIndex].label);
      } else {
        favChip.classList.add("is-off");
        favChip.classList.remove("is-disabled", "is-active");
      }
    }

    // mv-fs-* chips paint non-favorites sorts only (favorites lives next to All).
    const otherSorts = SORTS.map((s, i) => ({ s, i })).filter(({ s }) => s.id !== "favorites");
    for (let i = 0; i < 4; i++) {
      const chip = document.getElementById(`mv-fs-${i}`);
      if (!chip) continue;
      if (i < otherSorts.length) {
        const { s, i: sortIndex } = otherSorts[i];
        const disabled = !sortEnabled(s);
        chip.classList.remove("is-off");
        chip.classList.toggle("is-disabled", disabled);
        chip.classList.toggle("is-active", state.sort === sortIndex);
        setVar(`fs${i}`, s.label);
      } else {
        chip.classList.add("is-off");
        chip.classList.remove("is-disabled", "is-active");
      }
    }
  };

  const setLadder = (el, prefix, max, value) => {
    if (!el) return;
    for (let n = 0; n <= max; n++) el.classList.toggle(`${prefix}-${n}`, n === value);
  };

  const votedOrder = () => {
    const alive = state.firstVote.filter((m) => (state.votes[m] || 0) > 0);
    return alive
      .map((m, i) => ({ m, c: state.votes[m] || 0, i }))
      .sort((a, b) => b.c - a.c || a.i - b.i)
      .map((x) => x.m);
  };

  const paint = () => {
    const voted = votedOrder();
    const section = document.getElementById("mv-voted-section");
    const hint = document.getElementById("mv-voted-hint");
    if (section) section.classList.toggle("has-votes", voted.length > 0);
    if (hint) hint.classList.toggle("is-off", voted.length > 0 || state.reveal);
    setVar("mv_title", state.reveal ? "Winning map" : "Vote for the next map");
    setVar("mv_hint", "Cast the first vote");
    setVar("mv_chip", "Map Vote");
    setVar("mv_empty", "No maps match this filter");
    setLadder(document.getElementById("mv-voted-strip"), "vw", SLOT, voted.length);
    paintFilters();

    const grid = filteredMaps();
    const empty = document.getElementById("mv-empty");
    if (empty) empty.classList.toggle("is-off", grid.length > 0);
    const rowCount = grid.length === 0 ? 0 : Math.ceil(grid.length / COLS);
    setLadder(document.getElementById("mv-catalog"), "rows", ROW_COUNT, rowCount);
    for (let r = 0; r < ROW_COUNT; r++) {
      const crow = document.getElementById(`mv-crow-${r}`);
      if (!crow) continue;
      const visible = r < rowCount ? Math.min(COLS, grid.length - r * COLS) : 0;
      crow.classList.toggle("is-off", visible <= 0);
      setLadder(crow, "cols", COLS_WIDTH_MAX, Math.min(visible, COLS_WIDTH_MAX));
    }

    for (let i = 0; i < SLOT; i++) {
      if (i < voted.length) {
        paintTile(
          `mv-voted-slot-${i}`,
          `mv-voted-${i}`,
          `mv-voted-shot-${i}`,
          `mv-voted-fav-${i}`,
          `vv${i}`,
          `vn${i}`,
          voted[i]
        );
      } else {
        clearTile(
          `mv-voted-slot-${i}`,
          `mv-voted-${i}`,
          `mv-voted-shot-${i}`,
          `mv-voted-fav-${i}`,
          `vv${i}`,
          `vn${i}`
        );
      }

      if (i < grid.length) {
        paintTile(`mv-slot-${i}`, `mv-${i}`, `mv-shot-${i}`, `mv-fav-${i}`, `v${i}`, `n${i}`, grid[i]);
      } else {
        clearTile(`mv-slot-${i}`, `mv-${i}`, `mv-shot-${i}`, `mv-fav-${i}`, `v${i}`, `n${i}`);
      }
    }
  };

  const bump = (map, delta) => {
    const next = Math.max(0, (state.votes[map] || 0) + delta);
    state.votes[map] = next;
    if (delta > 0 && next === delta && !state.firstVote.includes(map)) {
      state.firstVote.push(map);
    }
    if (next === 0) {
      state.firstVote = state.firstVote.filter((m) => m !== map);
    }
  };

  const castSingle = (map) => {
    const prev = state.mine;
    if (prev === map) return;
    if (prev) bump(prev, -1);
    state.mine = map;
    state.mineSet = new Set([map]);
    bump(map, 1);
  };

  const castMulti = (map, fromVoted) => {
    bump(map, fromVoted ? -1 : 1);
    if ((state.votes[map] || 0) > 0) state.mineSet.add(map);
    else state.mineSet.delete(map);
    state.mine = state.mineSet.size ? [...state.mineSet][0] : null;
  };

  const cast = (map, fromVoted) => {
    if (state.reveal || !map) return;
    if (state.mode === "multi") castMulti(map, fromVoted);
    else castSingle(map);
    paint();
  };

  const parseButton = (id) => {
    if (!id) return null;
    if (id.startsWith("mv-voted-fav-")) {
      const i = Number(id.slice("mv-voted-fav-".length));
      const order = votedOrder();
      if (!(Number.isInteger(i) && i >= 0 && i < order.length)) return null;
      return { fav: order[i] };
    }
    if (id.startsWith("mv-fav-")) {
      const i = Number(id.slice("mv-fav-".length));
      const grid = filteredMaps();
      if (!(Number.isInteger(i) && i >= 0 && i < grid.length)) return null;
      return { fav: grid[i] };
    }
    if (id.startsWith("mv-ft-")) {
      const i = Number(id.slice(6));
      if (Number.isInteger(i) && i >= 0 && i < TYPES.length) return { type: i };
      return null;
    }
    if (id === "mv-sort-fav") {
      const i = SORTS.findIndex((s) => s.id === "favorites");
      return i >= 0 ? { sort: i } : null;
    }
    if (id.startsWith("mv-fs-")) {
      const i = Number(id.slice(6));
      const otherSorts = SORTS.map((s, idx) => ({ s, idx })).filter(({ s }) => s.id !== "favorites");
      if (Number.isInteger(i) && i >= 0 && i < otherSorts.length) return { sort: otherSorts[i].idx };
      return null;
    }
    if (id.startsWith("mv-voted-")) {
      const rest = id.slice("mv-voted-".length);
      if (!/^\d+$/.test(rest)) return null;
      const i = Number(rest);
      const order = votedOrder();
      if (!(Number.isInteger(i) && i >= 0 && i < order.length)) return null;
      return { map: order[i], fromVoted: true };
    }
    if (/^mv-\d+$/.test(id)) {
      const i = Number(id.slice(3));
      const grid = filteredMaps();
      if (!(Number.isInteger(i) && i >= 0 && i < grid.length)) return null;
      return { map: grid[i], fromVoted: false };
    }
    return null;
  };

  const toggleMinimized = () => {
    document.getElementById("Vote")?.classList.toggle("is-minimized");
  };

  const toggleFavorite = (map) => {
    if (!map) return;
    if (state.favorites.has(map)) state.favorites.delete(map);
    else state.favorites.add(map);
    paint();
  };

  document.getElementById("Vote")?.addEventListener("click", (ev) => {
    const btn = ev.target.closest("button[id]");
    if (!btn) return;
    if (btn.id === "mv-minimize") {
      toggleMinimized();
      return;
    }
    const hit = parseButton(btn.id);
    if (!hit) return;
    if (hit.fav) {
      toggleFavorite(hit.fav);
      return;
    }
    if (hit.type !== undefined) {
      if (hit.type > 0 && !typeHasMaps(TYPES[hit.type])) return;
      state.type = hit.type;
      paint();
      return;
    }
    if (hit.sort !== undefined) {
      if (!sortEnabled(SORTS[hit.sort])) return;
      state.sort = state.sort === hit.sort ? -1 : hit.sort;
      paint();
      return;
    }
    if (hit.map) cast(hit.map, hit.fromVoted);
  });

  const clearVotes = () => {
    state.votes = Object.fromEntries(state.maps.map((m) => [m, 0]));
    state.firstVote = [];
    state.mine = null;
    state.mineSet = new Set();
    state.reveal = false;
    state.winner = null;
    paint();
  };

  const seedVotes = () => {
    clearVotes();
    ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "cs_office"].forEach((m, idx) => {
      if (!state.maps.includes(m)) return;
      state.votes[m] = idx === 0 ? 3 : idx === 1 ? 2 : 1;
      state.firstVote.push(m);
    });
    state.mine = "de_dust2";
    state.mineSet = new Set(["de_dust2"]);
    paint();
  };

  const randomize = () => {
    clearVotes();
    const picks = state.maps.slice().sort(() => Math.random() - 0.5);
    const n = 4 + Math.floor(Math.random() * 8);
    for (let i = 0; i < n && i < picks.length; i++) {
      const m = picks[i];
      const c = 1 + Math.floor(Math.random() * 6);
      state.votes[m] = c;
      state.firstVote.push(m);
    }
    if (state.mode === "multi") {
      const mineN = 1 + Math.floor(Math.random() * 3);
      state.mineSet = new Set(votedOrder().slice(0, mineN));
      state.mine = [...state.mineSet][0] || null;
    } else {
      state.mine = votedOrder()[0] || null;
      state.mineSet = state.mine ? new Set([state.mine]) : new Set();
    }
    paint();
  };

  const topMap = () => votedOrder()[0] || state.maps[0];

  const reveal = () => {
    state.reveal = true;
    state.winner = topMap();
    paint();
  };

  const setMode = (mode) => {
    if (mode !== "single" && mode !== "multi") return;
    state.mode = mode;
    state.reveal = false;
    state.winner = null;
    if (mode === "single" && state.mineSet.size > 1) {
      // Keep highest-voted selected map as the single vote; drop the rest from mineSet only.
      const keep = votedOrder().find((m) => state.mineSet.has(m)) || [...state.mineSet][0];
      for (const m of [...state.mineSet]) {
        if (m !== keep) {
          bump(m, -1);
          state.mineSet.delete(m);
        }
      }
      state.mine = keep || null;
    }
    syncModeButtons();
    paint();
  };

  const syncModeButtons = () => {
    bar.querySelectorAll("[data-mode]").forEach((btn) => {
      btn.classList.toggle("is-active", btn.getAttribute("data-mode") === state.mode);
    });
    const note = bar.querySelector(".pv-note");
    if (note) {
      note.textContent =
        state.mode === "multi"
          ? "Multi: catalog +1 · voted strip −1"
          : "Single: one personal vote (CS2 default)";
    }
  };

  const bar = document.createElement("div");
  bar.className = "pv-bar";
  bar.innerHTML = `
    <button type="button" data-act="clear">Clear votes</button>
    <button type="button" data-act="randomize">Randomize</button>
    <button type="button" data-act="seed">Seed demo</button>
    <button type="button" data-act="reveal">Reveal winner</button>
    <span class="pv-sep" aria-hidden="true"></span>
    <button type="button" data-mode="single">Single vote</button>
    <button type="button" data-mode="multi">Multi select</button>
    <span class="pv-note"></span>
  `;
  document.body.appendChild(bar);
  bar.addEventListener("click", (ev) => {
    const t = ev.target;
    if (!(t instanceof HTMLElement)) return;
    const act = t.getAttribute("data-act");
    const mode = t.getAttribute("data-mode");
    if (act === "clear") clearVotes();
    if (act === "randomize") randomize();
    if (act === "seed") seedVotes();
    if (act === "reveal") reveal();
    if (mode) setMode(mode);
  });

  seedVotes();
  syncModeButtons();
})();
"""


def write_js() -> None:
    # Keep interactive demo logic generated so SlotCount stays derived from XML ids.
    OUT_JS.write_text(VOTE_JS, encoding="utf-8")


def sync() -> list[str]:
    errors: list[str] = []
    if not VOTE_XML.is_file():
        return [f"missing {VOTE_XML.relative_to(ROOT)}"]
    if not HUD_VCSS.is_file():
        return [f"missing {HUD_VCSS.relative_to(ROOT)}"]

    PREVIEW.mkdir(parents=True, exist_ok=True)

    try:
        tree = ET.parse(VOTE_XML)
    except ET.ParseError as ex:
        return [f"{VOTE_XML_NAME}: parse error: {ex}"]

    root = tree.getroot()
    body_parts: list[str] = []
    for child in root:
        if child.tag != "styles":
            render_node(child, body_parts)

    css = translate_css(HUD_VCSS.read_text(encoding="utf-8"))
    OUT_CSS.write_text(
        f"/* AUTO-GENERATED from {HUD_VCSS_NAME} — do not edit. Run: make panorama */\n"
        + SHELL_CSS
        + f"\n/* ---- {HUD_VCSS_NAME} (translated) ---- */\n"
        + css
        + "\n",
        encoding="utf-8",
    )

    OUT_HTML.write_text(
        "<!DOCTYPE html>\n"
        '<html lang="en">\n'
        "<head>\n"
        '<meta charset="utf-8" />\n'
        "<title>MapVote preview</title>\n"
        '<link rel="stylesheet" href="hud.preview.css" />\n'
        "</head>\n"
        "<body>\n"
        '<div class="pv-plate">\n'
        f"{''.join(body_parts)}\n"
        "</div>\n"
        '<script src="vote.js"></script>\n'
        "</body>\n"
        "</html>\n",
        encoding="utf-8",
    )
    write_js()
    return errors


def main() -> int:
    errors = sync()
    if errors:
        print("Preview sync failed:")
        for err in errors:
            print(f"  - {err}")
        return 1
    print(f"Preview synced → {OUT_HTML.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
