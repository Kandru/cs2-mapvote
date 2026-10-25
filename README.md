# CS2 MapVote

Fullscreen Panorama end-match map vote for [CounterStrikeSharp](https://docs.cssharp.dev/).

Players see a fullscreen CS2 end-of-match map vote: Valve portrait cards (110×193px, ~10 across), a top row of maps that already have votes (sorted by count), then a scrollable catalog in rows of 10. Unused slots stay collapsed. The overlay is transparent with a light frost. The game clock is left alone. On lock, the winning tile shows a green check, then the plugin changes the map.

## Requirements

- CounterStrikeSharp on a CS2 dedicated server (`.NET 10`)
- Non-empty `rcon_password` (map lists are loaded via localhost RCON)
- Workshop addon with the Panorama content under `workshop/content` published to clients

## Build

```bash
make debug      # dockerized dotnet build (runs panorama validation first)
make release    # dockerized dotnet publish
make test       # unit tests
make panorama   # validate XML/CSS/images only
```

Uses `mcr.microsoft.com/dotnet/sdk:10.0` — no project Dockerfile.

## Map images

```bash
# One-time: download VRF CLI 20 into tools/.cache/
# https://github.com/ValveResourceFormat/ValveResourceFormat/releases/download/20.0/cli-linux-x64.zip
make images WORKSHOP_DIR=/path/to/workshop/content/730
make images GAME_DIR=/path/to/Steam/steamapps/common/Counter-Strike\ Global\ Offensive/game/csgo
make images WORKSHOP_DIR=… GAME_DIR=…/game/csgo
# Override binary: make images … SOURCE2VIEWER_CLI=/path/to/Source2Viewer-CLI
```

**Workshop (`WORKSHOP_DIR`):** opens each VPK and tries map screenshots (`map_icons/screenshots/1080p` → `720p` → `360p`, largest file first). It skips radar/overhead paths, images that are too dark (mean luma under 40), and images that are much taller than they are wide, then takes the next candidate. The chosen PNG is center-cropped to 16:9, resized to **350×197**, written with a companion `.vtex`, and added as `.mv-map-{name}` in the workshop palette. Maps with no usable screenshot keep `default.png` (`mv-map-default`).

**Official maps (`GAME_DIR`):** reads `pak01_dir.vpk` (pass the `game/csgo` folder or the VPK itself), discovers `panorama/images/map_icons/screenshots/360p/{map}_png.vtex`, regenerates `tools/stock_maps.txt` and `.mv-map-stock-{name}` CSS (Valve `s2r://…/360p/…` URLs), and extracts preview WebPs into `workshop/preview/maps/`. Stock and workshop classes can share a map name; the HUD prefers the workshop image when both exist.

Requires Pillow. Republish the workshop addon after extracting new workshop maps (stock CSS only needs a republish when the stock class list changes).

## Config

`addons/counterstrikesharp/configs/plugins/MapVote/MapVote.json` (generated on first run):

| Key | Default | Meaning |
| --- | --- | --- |
| `enabled` | `true` | Master switch |
| `exclude_current_map` | `true` | Hide the map just played |
| `fallback_vote_seconds` | `15` | Used when end-match cvars are missing/too short |
| `reveal_seconds` | `5` | Locked-winner display window |
| `rtv.enabled` | `true` | `!rtv` / `css_rtv` |
| `rtv.ratio` | `0.6` | Fraction of humans required |

Vote duration is `floor(mp_endmatch_votenextleveltime) - reveal_seconds`, with fallbacks to `mp_match_restart_delay` then `fallback_vote_seconds`.

## Workshop layout

Copy `workshop/content/` into your `csgo_addons/<addon>/` tree. Rename `vote.xml` → `vote_v4.vxml` to match `PanoramaVersion` in `Version.cs`, and point the stylesheet include at the versioned name if you version styles as well. Panorama binding rules live in `stuff/PANORAMA_RULESET.md`.

## Commands

- `!rtv` / `css_rtv` — rock the vote (forces the match to end so this overlay can run)
