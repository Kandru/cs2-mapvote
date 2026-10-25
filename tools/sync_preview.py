#!/usr/bin/env python3
"""Generate workshop/preview from vote.xml + hud.vcss so the browser mock stays in sync."""
from __future__ import annotations

import html
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "workshop" / "content" / "panorama"
VOTE_XML = CONTENT / "layout" / "custom_game" / "mapvote" / "vote.xml"
HUD_VCSS = CONTENT / "styles" / "custom_game" / "mapvote" / "hud.vcss"
PREVIEW = ROOT / "workshop" / "preview"
OUT_HTML = PREVIEW / "vote.html"
OUT_CSS = PREVIEW / "hud.preview.css"
OUT_JS = PREVIEW / "vote.js"

TAGS = {"Panel": "div", "Label": "div", "Button": "button", "Image": "div"}

MAP_IMAGE_RE = re.compile(
    r'url\(\s*"s2r://panorama/images/custom_game/mapvote/maps/([A-Za-z0-9_]+)\.vtex"\s*\)'
)
OTHER_S2R_RE = re.compile(r'url\(\s*"s2r://[^"]+"\s*\)')
GRADIENT_RE = re.compile(r"gradient\(\s*(?:linear|radial)\s*,([^;]*?)\)\s*;", re.I)
BOX_SHADOW_RE = re.compile(
    r"box-shadow:\s*(#[0-9a-fA-F]+)((?:\s+-?[\d.]+px){2,4})\s*;"
)
TEXT_SHADOW_RE = re.compile(
    r"text-shadow:\s*((?:#[0-9a-fA-F]+\s+-?[\d.]+px\s+-?[\d.]+px(?:\s+-?[\d.]+px)?\s*,?\s*)+);"
)
FONT_SIZE_RE = re.compile(r"font-size:\s*([0-9.]+)\s*;")
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


def translate_gradient(body: str) -> str:
    stops = re.findall(r"(?:from|to)\(\s*(#[0-9a-fA-F]+)\s*\)", body)
    mid = re.findall(r"color-stop\(\s*[\d.]+\s*,\s*(#[0-9a-fA-F]+)\s*\)", body)
    cols = ([stops[0]] + mid + stops[1:]) if len(stops) >= 2 else stops
    horiz = "100% 0%" in body or "100%  0%" in body
    return f"linear-gradient(to {'right' if horiz else 'bottom'}, {', '.join(cols) or '#444'});"


def translate_text_shadow(raw: str) -> str:
    parts = []
    for m in re.finditer(
        r"(#[0-9a-fA-F]+)\s+(-?[\d.]+px)\s+(-?[\d.]+px)(?:\s+(-?[\d.]+px))?",
        raw,
    ):
        color, x, y, blur = m.group(1), m.group(2), m.group(3), m.group(4) or "0px"
        parts.append(f"{x} {y} {blur} {color}")
    return f"text-shadow: {', '.join(parts)};" if parts else "text-shadow: none;"


def translate_css(text: str) -> str:
    t = text
    t = GRADIENT_RE.sub(lambda m: translate_gradient(m.group(1)), t)
    t = BOX_SHADOW_RE.sub(lambda m: f"box-shadow:{m.group(2)} {m.group(1)};", t)
    t = TEXT_SHADOW_RE.sub(lambda m: translate_text_shadow(m.group(1)), t)
    t = FONT_SIZE_RE.sub(r"font-size: \1px;", t)
    t = WORLD_BLUR_RE.sub("backdrop-filter: blur(10px);", t)
    t = BLUR_RE.sub(r"filter: blur(\1px);", t)
    t = WASH_RE.sub("", t)
    t = SOUND_RE.sub("", t)
    t = IMG_SHADOW_RE.sub("", t)
    t = PRE_SCALE_RE.sub(r"transform: scale(\1, \2);", t)
    t = IGNORE_FLOW_RE.sub("position: absolute; inset: 0;", t)
    t = BG_IMG_OPACITY_RE.sub(
        lambda m: f"opacity: {m.group(1)}; /* background-img-opacity */", t
    )

    # Compose filter from saturation / brightness when blur is absent on same rule.
    # Simple pass: rewrite as filter pieces (may stack with blur — ok for preview).
    t = SAT_RE.sub(r"filter: saturate(\1); /* saturation */", t)
    t = BRIGHT_RE.sub(r"filter: brightness(\1); /* brightness */", t)

    t = t.replace("flow-children: down;", "display: flex; flex-direction: column;")
    t = t.replace("flow-children: right-wrap;", "display: flex; flex-direction: row; flex-wrap: wrap;")
    t = t.replace("flow-children: right;", "display: flex; flex-direction: row; flex-wrap: nowrap;")
    t = t.replace("visibility: collapse;", "display: none;")
    t = WIDTH_PERCENTAGE_RE.sub(
        lambda m: f"aspect-ratio: 100 / {m.group(1)}; height: auto;",
        t,
    )
    t = FIT_CHILDREN_RE.sub(r"\1: auto;", t)
    t = FILL_FLOW_RE.sub(
        lambda m: "flex: 1 1 auto; min-height: 0;" if m.group(1) == "height" else "flex: 1 1 auto;",
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
    t = VERT_MIDDLE_RE.sub("align-self: center;", t)
    t = VERT_BOTTOM_RE.sub("align-self: flex-end;", t)
    t = VERT_TOP_RE.sub("align-self: flex-start;", t)
    t = HORIZ_CENTER_RE.sub("margin-left: auto; margin-right: auto;", t)
    t = HORIZ_RIGHT_RE.sub("margin-left: auto;", t)
    t = HORIZ_LEFT_RE.sub("margin-right: auto;", t)

    # Real map images from content; other s2r → soft placeholder.
    t = MAP_IMAGE_RE.sub(
        r'url("../content/panorama/images/custom_game/mapvote/maps/\1.png")', t
    )
    t = OTHER_S2R_RE.sub(
        'url("data:image/svg+xml,%3Csvg xmlns=\'http://www.w3.org/2000/svg\' width=\'40\' height=\'40\'%3E'
        '%3Ccircle cx=\'2\' cy=\'2\' r=\'1\' fill=\'%23ffffff11\'/%3E%3C/svg%3E")',
        t,
    )
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
* { box-sizing: border-box; margin: 0; padding: 0; border: 0 solid transparent; }
html, body { height: 100%; }
body {
  min-height: 100vh;
  background: #0a0c10;
  font-family: "Stratum2 Condensed", "Barlow Condensed", "Arial Narrow", system-ui, sans-serif;
  color: #f2f5ff;
  overflow: hidden;
}
button {
  background: none;
  font: inherit;
  color: inherit;
  cursor: pointer;
  text-align: inherit;
  position: relative;
}
/* Plate: true fullscreen — fills the viewport at any aspect (no letterbox bars). */
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
.VoteRoot {
  position: relative;
  height: 100%;
}
.VoteRoot.is-off {
  /* Preview forces the card visible; game starts closed. */
  display: block !important;
  opacity: 1 !important;
}
.VoteColumn {
  position: relative;
  z-index: 1;
  height: 100%;
}
.VoteTopBar {
  position: relative;
  flex: 0 0 auto;
  width: 100%;
  height: 48px;
}
.VoteTitle {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  text-align: center;
  font-family: "Stratum2 Condensed", "Barlow Condensed", "Arial Narrow", sans-serif;
  font-weight: 400;
  font-size: 20px;
  letter-spacing: 1.6px;
  text-transform: uppercase;
  color: #ffffff;
  pointer-events: none;
}
.MinimizeBtn {
  position: absolute;
  top: 50%;
  right: 16px;
  transform: translateY(-50%);
  width: 108px;
  height: 32px;
  background: #00000099;
  border: 1px solid #ffffff33 !important;
  border-radius: 3px;
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 6;
}
.MinimizeBtn:hover { background: #ffffff22; }
.MinimizeLabel {
  width: 100%;
  line-height: 32px;
  text-align: center;
  font-family: "Stratum2 Bold Condensed", "Barlow Condensed", "Arial Narrow", sans-serif;
  font-weight: 700;
  font-size: 14px;
  letter-spacing: 1.2px;
  text-transform: uppercase;
  color: #fff;
  pointer-events: none;
  white-space: nowrap;
}
.VoteRoot.is-minimized .mv-glass,
.VoteRoot.is-minimized .mv-scrim-top,
.VoteRoot.is-minimized .mv-scrim-bottom,
.VoteRoot.is-minimized .VoteBody,
.VoteRoot.is-minimized .VoteTitle {
  display: none !important;
}
.VoteRoot.is-minimized .MinimizeBtn {
  background: #000000cc;
  border-color: #ff9a1a !important;
}
.VoteRoot.is-minimized .MinimizeLabel {
  color: #ff9a1a;
}
.VoteBody {
  position: relative;
  z-index: 1;
  width: 100%;
  flex: 1 1 auto;
  min-height: 0;
}
.VotedSection {
  flex: 0 0 auto;
  width: 100%;
  background: linear-gradient(to bottom, #000000b3, #00000066);
  border-bottom: 1px solid #ffffff22;
  padding: 8px 0 12px;
}
.VotedSection.has-votes { border-bottom: 2px solid #ff9a1a; }
.VotedRow {
  position: relative;
  overflow-x: auto;
  overflow-y: hidden;
  flex: 0 0 auto;
  height: 213px;
  scrollbar-width: thin;
}
.VotedHint {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  text-align: center;
  text-transform: uppercase;
  letter-spacing: 1px;
  color: #a3acb6;
  z-index: 1;
  pointer-events: none;
}
.VotedStrip {
  display: flex;
  flex-direction: row;
  flex-wrap: nowrap;
  margin-left: auto;
  margin-right: auto;
  height: 213px;
}
.MapGridHost {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
  width: 100%;
}
.MapGrid {
  overflow-y: auto;
  overflow-x: hidden;
  height: 100%;
}
.MapGridFadeTop,
.MapGridFadeBottom {
  position: absolute;
  left: 0;
  right: 0;
  pointer-events: none;
  z-index: 5;
}
.MapGridFadeTop {
  top: 0;
  height: 64px;
  background: linear-gradient(to bottom, #000000f2, #00000000);
}
.MapGridFadeBottom {
  bottom: 0;
  height: 48px;
  background: linear-gradient(to top, #000000e6, #00000000);
}
.MapCatalog {
  width: 100%;
  display: flex;
  flex-direction: column;
  margin-left: auto;
  margin-right: auto;
  padding-left: 12px;
  padding-right: 12px;
  box-sizing: border-box;
}
.CatalogRow {
  display: flex;
  flex-direction: row;
  flex-wrap: nowrap;
  justify-content: stretch;
  width: 100%;
}
.MapTile {
  position: relative;
  flex: 0 0 auto;
  box-sizing: border-box;
  width: 110px;
  height: 193px;
  overflow: hidden;
}
.CatalogRow .MapTile {
  flex: 1 1 0;
  width: auto;
  min-width: 0;
}
.MapShot,
.MapFade {
  position: absolute;
  inset: 0;
  display: block;
}
.MapShot {
  z-index: 0;
  transform-origin: center center;
  background-size: auto 100% !important;
  background-position: 50% 50% !important;
  background-repeat: no-repeat !important;
}
.MapFade { z-index: 1; }
.mv-scrim-top,
.mv-scrim-bottom {
  position: absolute;
  inset: 0;
  pointer-events: none;
}
.MapVotes,
.MapName {
  position: absolute;
  left: 0;
  right: 0;
  z-index: 2;
  pointer-events: none;
}
.MapVotes {
  bottom: 40px;
  color: #ff9a1a !important;
  font-family: Stratum2, "Barlow Condensed", "Arial Narrow", sans-serif !important;
  font-weight: 700 !important;
  font-size: 26px !important;
}
.MapFade {
  background: linear-gradient(to bottom, #00000000 0%, #00000040 35%, #000000cc 65%, #000000ff 100%) !important;
}
.MapName {
  bottom: 6px;
  left: 5%;
  right: 5%;
  width: auto;
  height: 36px;
  line-height: 36px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: "Stratum2 Medium Condensed", "Barlow Condensed", "Arial Narrow", sans-serif !important;
  font-weight: 500 !important;
  font-size: 16px !important;
}
/* Preview stand-in for Valve vote_check.vsvg — match #id-map-selection-btn__winner. */
.MapWinnerCheck {
  position: absolute;
  left: 50%;
  top: 50%;
  width: 50px;
  height: 50px;
  margin: -25px 0 0 -25px;
  z-index: 3;
  pointer-events: none;
  opacity: 0 !important;
  transform: scale(2) !important;
  transform-origin: center center;
  background: center / contain no-repeat url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E%3Cpath fill='%232bff00' d='M9.2 17.2 4.8 12.8l1.4-1.4 3 3 8-8 1.4 1.4z'/%3E%3C/svg%3E");
  transition: transform 0.1s ease-in-out 0.35s, opacity 0.1s ease-in-out 0.35s;
}
.MapTile.is-done .MapWinnerCheck {
  opacity: 1 !important;
  transform: scale(1) !important;
}
.MapTile.is-lost {
  filter: brightness(0.45);
  border-color: transparent !important;
  box-shadow: none !important;
  transition: filter 0.5s ease-in-out;
}
.MapTile.is-done {
  border: 2px solid #fff !important;
  box-shadow: #ffffff 0px 0px 6px 0px !important;
  animation: pv-winner-pulse 0.5s ease-in-out 1;
}
@keyframes pv-winner-pulse {
  0% { border-color: #ffffff00; }
  50% { border-width: 5px; border-color: #ffff00; }
  100% { border-width: 2px; border-color: #ffffff; }
}
.pv-var { min-width: 0.5ch; }
.pv-bar {
  position: fixed;
  left: 12px;
  bottom: 10px;
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
.is-off { display: none !important; }
.VoteRoot.is-off { display: block !important; }
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
    el.className = "MapShot";
    el.style.backgroundImage = `url("maps/${s}.webp")`;
    el.style.backgroundSize = "100% 100%";
    el.style.backgroundPosition = "50% 50%";
    el.style.backgroundRepeat = "no-repeat";
    // Fallback if the webp 404s
    const probe = new Image();
    probe.onerror = () => {
      el.style.backgroundImage = 'url("maps/default.webp")';
    };
    probe.src = `maps/${s}.webp`;
  };

  const state = {
    maps: MAPS.slice(),
    votes: Object.fromEntries(MAPS.map((m) => [m, 0])),
    firstVote: [],
    mine: null,
    mineSet: new Set(),
    mode: "single", // single = CS2 one-vote; multi = toggle many maps
    reveal: false,
    winner: null,
  };

  const isMine = (map) =>
    state.mode === "multi" ? state.mineSet.has(map) : state.mine === map;

  const setVar = (name, value) => {
    document.querySelectorAll(`[data-var="${name}"]`).forEach((el) => {
      el.textContent = value;
    });
  };

  const COLS = 10;
  const ROW_COUNT = Math.ceil(SLOT / COLS);

  const clearTile = (tileId, shotId, voteVar, nameVar) => {
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    if (!tile || !shot) return;
    tile.classList.add("is-off");
    tile.classList.remove("is-self", "is-done", "is-lost");
    shot.className = "MapShot";
    shot.style.backgroundImage = "";
    setVar(voteVar, "");
    setVar(nameVar, "");
  };

  const paintTile = (tileId, shotId, voteVar, nameVar, map) => {
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    if (!tile || !shot) return;
    const count = state.votes[map] || 0;
    const isWinner = state.winner === map;
    const isLost = state.reveal && state.winner && !isWinner;
    tile.classList.remove("is-off");
    tile.classList.toggle("is-self", !state.reveal && isMine(map));
    tile.classList.toggle("is-done", isWinner);
    tile.classList.toggle("is-lost", !!isLost);
    applyShot(shot, map);
    setVar(voteVar, String(count));
    setVar(nameVar, map);
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
    setLadder(document.getElementById("mv-voted-strip"), "vw", SLOT, voted.length);

    const rowCount = state.maps.length === 0 ? 0 : Math.ceil(state.maps.length / COLS);
    setLadder(document.getElementById("mv-catalog"), "rows", ROW_COUNT, rowCount);
    for (let r = 0; r < ROW_COUNT; r++) {
      const crow = document.getElementById(`mv-crow-${r}`);
      if (crow) crow.classList.toggle("is-off", r >= rowCount);
    }

    for (let i = 0; i < SLOT; i++) {
      if (i < voted.length) {
        paintTile(`mv-voted-${i}`, `mv-voted-shot-${i}`, `vv${i}`, `vn${i}`, voted[i]);
      } else {
        clearTile(`mv-voted-${i}`, `mv-voted-shot-${i}`, `vv${i}`, `vn${i}`);
      }

      if (i < state.maps.length) {
        paintTile(`mv-${i}`, `mv-shot-${i}`, `v${i}`, `n${i}`, state.maps[i]);
      } else {
        clearTile(`mv-${i}`, `mv-shot-${i}`, `v${i}`, `n${i}`);
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
      if (!(Number.isInteger(i) && i >= 0 && i < state.maps.length)) return null;
      return { map: state.maps[i], fromVoted: false };
    }
    return null;
  };

  const toggleMinimized = () => {
    document.getElementById("Vote")?.classList.toggle("is-minimized");
  };

  document.getElementById("Vote")?.addEventListener("click", (ev) => {
    const btn = ev.target.closest("button[id]");
    if (!btn) return;
    if (btn.id === "mv-minimize") {
      toggleMinimized();
      return;
    }
    const hit = parseButton(btn.id);
    if (hit) cast(hit.map, hit.fromVoted);
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
        return [f"vote.xml: parse error: {ex}"]

    root = tree.getroot()
    body_parts: list[str] = []
    for child in root:
        if child.tag != "styles":
            render_node(child, body_parts)

    css = translate_css(HUD_VCSS.read_text(encoding="utf-8"))
    OUT_CSS.write_text(
        "/* AUTO-GENERATED from hud.vcss — do not edit. Run: make panorama */\n"
        + SHELL_CSS
        + "\n/* ---- hud.vcss (translated) ---- */\n"
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
