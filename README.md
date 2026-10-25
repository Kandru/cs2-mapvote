# CS2 MapVote

Fullscreen Panorama end-match map vote for [CounterStrikeSharp](https://docs.cssharp.dev/).

Players see a fullscreen CS2 end-of-match map vote: Valve portrait cards (110×175%, ~10 across), a top row of maps that already have votes, then a wrapping catalog that scrolls when needed. Vote counts are orange (count only). Unused slots stay collapsed. The game clock is left alone. On lock, the winning tile shows a green check, then the plugin changes the map.

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
# Override binary: make images WORKSHOP_DIR=... SOURCE2VIEWER_CLI=/path/to/Source2Viewer-CLI
```

The script opens each VPK directory, picks the largest suitable screenshot (`map_icons/screenshots/1080p` → `720p` → `360p`, else radar), decompiles `vtex_c` to PNG, center-crops to 16:9, resizes to **350×197**, writes a companion `.vtex`, and updates the `mv-map-*` CSS palette. Fallback art is `default.png` (`mv-map-default`). Requires Pillow. Republish the workshop addon after extracting new maps.

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

Copy `workshop/content/` into your `csgo_addons/<addon>/` tree. Rename `vote.xml` → `vote_v1.vxml` to match `PanoramaVersion` in `Version.cs`, and point the stylesheet include at the versioned name if you version styles as well.

## Commands

- `!rtv` / `css_rtv` — rock the vote (forces the match to end so this overlay can run)
