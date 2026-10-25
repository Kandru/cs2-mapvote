# CounterstrikeSharp - MapVote

[![Discord Support](https://img.shields.io/discord/289448144335536138?label=Discord%20Support&color=darkgreen)](https://discord.gg/NtHCk5PWEt)
[![GitHub release](https://img.shields.io/github/release/Kandru/cs2-mapvote?include_prereleases=&sort=semver&color=blue)](https://github.com/Kandru/cs2-mapvote/releases/)
[![License](https://img.shields.io/badge/License-GPLv3-blue)](#license)
[![issues - cs2-mapvote](https://img.shields.io/github/issues/Kandru/cs2-mapvote?color=darkgreen)](https://github.com/Kandru/cs2-mapvote/issues)
[![](https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif)](https://www.paypal.com/donate/?hosted_button_id=C2AVYKGVP9TRG)

![Map vote](example.png)

Fullscreen Panorama end-of-match map vote for [CounterStrikeSharp](https://docs.cssharp.dev/). Players get Valve-style portrait cards, a top row of maps that already have votes, a scrollable catalog with type and sort filters, and `!rtv`. On lock the winning tile shows a green check, then the plugin changes the map. The game clock is left alone.

> [!TIP]
> Please consider a [donation](https://www.paypal.com/donate/?hosted_button_id=C2AVYKGVP9TRG) when you're using this plugin - it took a long time to build and improve - it only costs you a few dollars and would help me very much!

## Installation

1. Install [Metamod:Source](https://www.metamodsource.net/downloads.php?branch=dev).
2. Install [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager/releases) and configure it to load workshop item [3827322946](https://steamcommunity.com/sharedfiles/filedetails/?id=3827322946) (Panorama HUD), for example `mm_extra_addons "3827322946"` in `game/csgo/cfg/multiaddonmanager/multiaddonmanager.cfg`.
3. Install [CounterStrikeSharp](https://docs.cssharp.dev/).
4. Download the latest release from the [GitHub releases page](https://github.com/Kandru/cs2-mapvote/releases/) and copy the `MapVote` folder into `/game/csgo/addons/counterstrikesharp/plugins/`.
5. Set a non-empty `rcon_password` (map lists are loaded via localhost RCON).
6. Restart the server. Config is created at `addons/counterstrikesharp/configs/plugins/MapVote/MapVote.json`.

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

`addons/counterstrikesharp/configs/plugins/MapVote/MapVote.json` (generated on first run). Disable the plugin in CounterStrikeSharp if you do not want MapVote at all.

| Key | Default | Meaning |
| --- | --- | --- |
| `debug` | `false` | Extra console logging |
| `endmatch.exclude_current_map` | `true` | Hide the map just played |
| `endmatch.vote_seconds` | `15` | Open voting window (custom menu or native CS2 vote) |
| `endmatch.winner_reveal_seconds` | `5` | Locked-winner hold after our vote (custom menu only) |
| `rtv.enabled` | `true` | `!rtv` / `css_rtv` |
| `rtv.ratio` | `0.6` | Fraction of humans required |
| `filters.enabled` | `true` | Type / sort chip bar |
| `filters.types` | bomb/hostage/… | Prefix (or glob) groups for type chips |
| `filters.sorts` | most/least/favorites | Sort chips: `id`, `by` (`plays`/`name`/`favorite`), `order` (`asc`/`desc`), optional `only` |

On map start the plugin refreshes local (`maps *`) and workshop (`print_mapgroup_sv`) maps via RCON. At intermission it disables the native map vote and opens our overlay when the catalog has candidates; otherwise it enables the native CS2 vote for `endmatch.vote_seconds`. For the custom overlay, `mp_match_restart_delay` / `mp_endmatch_votenextleveltime` are set to `vote_seconds + winner_reveal_seconds + 1` so the plugin can changelevel before the engine ends the intermission. Player favorites and map play counts stay in memory during the map and are written on map end.

## Workshop layout

Copy `workshop/content/` into your `csgo_addons/<addon>/` tree. Layout/style sources are already versioned (`vote_v1.xml`, `hud_v1.vcss`) to match `PanoramaVersion` in `Version.cs`; publish compiles them to `vote_v1.vxml_c` / `hud_v1.vcss_c`. Panorama binding rules live in `stuff/PANORAMA_RULESET.md`.

## Commands

- `!rtv` / `css_rtv` — rock the vote (forces the match to end so this overlay can run)

## License

Released under [GPLv3](/LICENSE) by [@Kandru](https://github.com/Kandru).

## Authors

- [@derkalle4](https://www.github.com/derkalle4)
