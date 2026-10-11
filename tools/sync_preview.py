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
BLUR_RE = re.compile(r"blur:\s*gaussian\(\s*([0-9.]+)\s*\)\s*;")
WORLD_BLUR_RE = re.compile(r"world-blur:\s*[^;]+;")
WASH_RE = re.compile(r"wash-color:\s*[^;]+;")
SAT_RE = re.compile(r"saturation:\s*([0-9.]+)\s*;")
BRIGHT_RE = re.compile(r"brightness:\s*([0-9.]+)\s*;")
SOUND_RE = re.compile(r"sound(?:-optical)?:\s*[^;]+;")
IGNORE_FLOW_RE = re.compile(r"ignore-parent-flow:\s*[^;]+;")
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
    t = BLUR_RE.sub(r"filter: blur(\1px);", t)
    t = WORLD_BLUR_RE.sub("backdrop-filter: blur(10px);", t)
    t = WASH_RE.sub("", t)
    t = SOUND_RE.sub("", t)
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
    t = OVERFLOW_RE.sub(
        lambda m: (
            "overflow: auto;"
            if "scroll" in m.group(1)
            else "overflow: visible;"
            if "noclip" in m.group(1)
            else f"overflow: {m.group(1)};"
        ),
        t,
    )
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
  background: #12161c url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='40' height='40'%3E%3Cpath d='M0 0h40v40H0z' fill='%23161b22'/%3E%3Cpath d='M0 0h20v20H0zM20 20h20v20H20z' fill='%23181e26'/%3E%3C/svg%3E");
  font-family: "Barlow Condensed", "Arial Narrow", system-ui, sans-serif;
  color: #f2f5ff;
}
button {
  background: none;
  font: inherit;
  color: inherit;
  cursor: pointer;
  text-align: inherit;
  position: relative;
}
.HudScreen {
  position: fixed;
  inset: 0;
}
.VoteRoot {
  position: relative;
}
.VoteRoot.ph-off {
  /* Preview forces the card visible; game starts closed. */
  opacity: 1 !important;
  transform: none !important;
}
.VoteBody {
  position: relative;
  z-index: 1;
  height: 100%;
}
.VotedRow {
  overflow-x: auto;
  overflow-y: visible;
}
.MapGrid {
  overflow-y: auto;
  overflow-x: hidden;
  align-content: flex-start;
}
.MapTile {
  position: relative;
  flex: 0 0 auto;
  box-sizing: border-box;
}
.WinnerFrame {
  position: relative;
  flex: 0 0 auto;
}
.MapShot {
  position: relative;
  display: block;
  width: 100%;
  height: 100%;
}
.WinnerShot {
  position: absolute;
  inset: 0;
}
.MapVotes, .WinnerVotes {
  position: absolute;
  right: 18px;
  bottom: 16px;
  z-index: 2;
  pointer-events: none;
}
.MapVotesBack, .WinnerVotes.MapVotesBack {
  z-index: 1;
}
.WinnerName {
  position: relative;
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
.pv-bar .pv-note { opacity: 0.85; }
.is-empty { display: none !important; }
.VoteRoot.is-reveal .VotedRow,
.VoteRoot.is-reveal .MapGrid { display: none !important; }
/* Compose filters the way Panorama stacks saturation + blur. */
.MapShot { filter: saturate(0); }
.MapTile:hover .MapShot,
.MapTile.is-voted .MapShot,
.MapTile.is-mine .MapShot { filter: saturate(1); }
.MapTile.is-voted .MapShot { filter: saturate(1) blur(2.5px); }
.Winner.tick-a .WinnerShot { filter: brightness(1.08); }
.Winner.tick-b .WinnerShot { filter: brightness(0.92); }
.mv-bg { opacity: 1; } /* ignore background-img-opacity→opacity translation */
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

  const MAPS = [
    "de_dust2","de_mirage","de_inferno","de_nuke","de_overpass","de_ancient","de_anubis","de_vertigo",
    "cs_office","cs_italy","ar_baggage","ar_shoots","de_cache","de_train","cs_agency","cs_assault",
    "cs_militia","de_basalt","de_boyard","de_edin","de_palacio","de_whistle","de_brewery","de_dogtown",
    "de_graffiti","de_jura","de_golden","de_seaside","gd_cbble",
    "de_dust2_x","de_mirage_x","de_inferno_x","de_nuke_x","de_overpass_x","de_ancient_x","de_anubis_x",
    "de_vertigo_x","cs_office_x","cs_italy_x","ar_baggage_x","ar_shoots_x","de_cache_x","de_train_x",
    "cs_agency_x","cs_assault_x","cs_militia_x","de_basalt_x","de_boyard_x","de_edin_x","de_palacio_x"
  ].slice(0, SLOT);

  const stem = (map) => map.replace(/[^a-z0-9_]/gi, "_").toLowerCase();

  const hasMapClass = (s) => {
    const probe = document.createElement("div");
    probe.className = `mv-map-${s}`;
    document.body.appendChild(probe);
    const bg = getComputedStyle(probe).backgroundImage;
    probe.remove();
    return bg && bg !== "none";
  };

  const mapClass = (map) => {
    const s = stem(map);
    return hasMapClass(s) ? `mv-map-${s}` : "mv-map-default";
  };

  const state = {
    maps: MAPS.slice(),
    votes: Object.fromEntries(MAPS.map((m) => [m, 0])),
    firstVote: [],
    mine: null,
    timer: 45,
    reveal: false,
    bumpSide: false,
    lastStrip: [],
  };

  const setVar = (name, value) => {
    document.querySelectorAll(`[data-var="${name}"]`).forEach((el) => {
      el.textContent = value;
    });
  };

  const clearTile = (tileId, shotId, varName) => {
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    if (!tile || !shot) return;
    tile.classList.add("is-empty");
    tile.classList.remove("is-voted", "is-mine", "bump-a", "bump-b");
    shot.className = "MapShot mv-map-default";
    setVar(varName, "");
  };

  const paintTile = (tileId, shotId, varName, map, mine, bump) => {
    const tile = document.getElementById(tileId);
    const shot = document.getElementById(shotId);
    if (!tile || !shot) return;
    const count = state.votes[map] || 0;
    tile.classList.remove("is-empty");
    tile.classList.toggle("is-voted", count > 0);
    tile.classList.toggle("is-mine", mine === map);
    tile.classList.toggle("bump-a", !!bump && state.bumpSide);
    tile.classList.toggle("bump-b", !!bump && !state.bumpSide);
    shot.className = `MapShot ${mapClass(map)}`;
    setVar(varName, count > 0 ? String(count) : "");
  };

  const votedOrder = () => {
    const alive = state.firstVote.filter((m) => (state.votes[m] || 0) > 0);
    return alive
      .map((m, i) => ({ m, c: state.votes[m] || 0, i }))
      .sort((a, b) => b.c - a.c || a.i - b.i)
      .map((x) => x.m);
  };
  const gridOrder = () => state.maps;

  const paint = () => {
    const voted = votedOrder();
    const grid = gridOrder();
    const row = document.getElementById("mv-voted-row");
    if (row) row.classList.toggle("is-empty", voted.length === 0);

    const bumps = voted.map((m, i) => {
      const prev = state.lastStrip[i];
      return !prev || prev.map !== m || prev.votes !== (state.votes[m] || 0);
    });
    if (bumps.some(Boolean)) state.bumpSide = !state.bumpSide;

    for (let i = 0; i < SLOT; i++) {
      if (i < voted.length) {
        paintTile(`mv-voted-${i}`, `mv-voted-shot-${i}`, `vv${i}`, voted[i], state.mine, bumps[i]);
      } else {
        clearTile(`mv-voted-${i}`, `mv-voted-shot-${i}`, `vv${i}`);
      }

      if (i < grid.length) paintTile(`mv-${i}`, `mv-shot-${i}`, `v${i}`, grid[i], state.mine, false);
      else clearTile(`mv-${i}`, `mv-shot-${i}`, `v${i}`);
    }

    state.lastStrip = voted.map((m) => ({ map: m, votes: state.votes[m] || 0 }));
    setVar("timer", state.reveal ? "" : String(state.timer));
  };

  const cast = (map) => {
    if (state.reveal || !map) return;
    const prev = state.mine;
    if (prev === map) return;
    if (prev) {
      state.votes[prev] = Math.max(0, (state.votes[prev] || 0) - 1);
      if (state.votes[prev] === 0) {
        state.firstVote = state.firstVote.filter((m) => m !== prev);
      }
    }
    state.mine = map;
    const next = (state.votes[map] || 0) + 1;
    state.votes[map] = next;
    if (next === 1) state.firstVote.push(map);
    paint();
  };

  const parseButton = (id) => {
    if (!id) return null;
    if (id.startsWith("mv-voted-")) {
      const i = Number(id.slice("mv-voted-".length));
      const order = votedOrder();
      return Number.isInteger(i) && i >= 0 && i < order.length ? order[i] : null;
    }
    if (/^mv-\d+$/.test(id)) {
      const i = Number(id.slice(3));
      const order = gridOrder();
      return Number.isInteger(i) && i >= 0 && i < order.length ? order[i] : null;
    }
    return null;
  };

  document.getElementById("Vote")?.addEventListener("click", (ev) => {
    const btn = ev.target.closest("button[id]");
    if (!btn) return;
    cast(parseButton(btn.id));
  });

  const seedVotes = () => {
    ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "cs_office"].forEach((m, idx) => {
      if (!state.maps.includes(m)) return;
      state.votes[m] = idx === 0 ? 3 : idx === 1 ? 2 : 1;
      state.firstVote.push(m);
    });
    state.mine = "de_dust2";
  };

  const reveal = () => {
    state.reveal = true;
    const root = document.getElementById("Vote");
    const winner = document.getElementById("mv-winner");
    const shot = document.getElementById("mv-winner-shot");
    root?.classList.add("is-reveal");
    winner?.classList.remove("is-empty");
    const top = votedOrder()[0] || state.maps[0];
    if (shot) shot.className = `WinnerShot ${mapClass(top)}`;
    setVar("winner_name", top);
    setVar("winner_votes", String(state.votes[top] || 0));
    setVar("timer", "");
    paint();
  };

  const reset = () => {
    state.votes = Object.fromEntries(state.maps.map((m) => [m, 0]));
    state.firstVote = [];
    state.lastStrip = [];
    state.mine = null;
    state.timer = 45;
    state.reveal = false;
    document.getElementById("Vote")?.classList.remove("is-reveal");
    document.getElementById("mv-winner")?.classList.add("is-empty");
    setVar("title", "Map Vote");
    setVar("winner_name", "");
    setVar("winner_votes", "");
    seedVotes();
    paint();
  };

  // Controls
  const bar = document.createElement("div");
  bar.className = "pv-bar";
  bar.innerHTML = `
    <button type="button" data-act="reset">Reset demo</button>
    <button type="button" data-act="reveal">Reveal winner</button>
    <span class="pv-note">Synced from vote.xml + hud.vcss. Strip = votes sorted; grid keeps all maps with the same vote overlay.</span>
  `;
  document.body.appendChild(bar);
  bar.addEventListener("click", (ev) => {
    const act = ev.target.getAttribute("data-act");
    if (act === "reset") reset();
    if (act === "reveal") reveal();
  });

  setVar("title", "Map Vote");
  seedVotes();
  paint();
  setInterval(() => {
    if (state.reveal || state.timer <= 0) return;
    state.timer -= 1;
    setVar("timer", String(state.timer));
  }, 1000);
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
        f"{''.join(body_parts)}\n"
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
