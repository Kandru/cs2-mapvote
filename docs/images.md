# Map images

Shipped Workshop addon (HUD + tiles): [3827322946](https://steamcommunity.com/sharedfiles/filedetails/?id=3827322946). Mount via MultiAddonManager. Map pictures cannot live in the plugin folder — only in a mounted Workshop addon. Missing tile → `default.png` / class `mv-map-default`.

Custom addons must ship the same Panorama image classes the HUD expects (`mv-map-map_{name}`, `mv-map-ws_{name}`). Layout/style sources: `workshop/content/` (`vote_v1.xml`, `hud_v1.vcss` ↔ `PanoramaVersion` in `Version.cs`). Copy into `csgo_addons/<addon>/`; publish compiles to `.vxml_c` / `.vcss_c`.

## Extract

One-time: download [VRF CLI 20](https://github.com/ValveResourceFormat/ValveResourceFormat/releases/download/20.0/cli-linux-x64.zip) into `tools/.cache/` as `Source2Viewer-CLI` (or pass `SOURCE2VIEWER_CLI=…`). Needs Pillow.

```bash
make images WORKSHOP_DIR=/path/to/workshop/content/730
make images GAME_DIR=/path/to/Steam/steamapps/common/Counter-Strike\ Global\ Offensive/game/csgo
make images WORKSHOP_DIR=… GAME_DIR=…/game/csgo
make images-check   # rewrite tiles that are not 350×197 PNG
make images-css     # rebuild VCSS (+ preview) from PNGs on disk
```

Republish the workshop addon after extracting images.

### Workshop (`WORKSHOP_DIR`)

Opens each VPK in path order; only packages with `maps/*.vmap_c`. Screenshots: `map_icons/screenshots/1080p` → `720p` → `360p` (largest file first). Skips radar/overhead paths; prefers shots that are not too dark (mean luma under 40) and not much taller than wide. Fallbacks: first too-dark screenshot → first portrait/tall (full width, vertically centered crop to 16:9) → `panorama/images/overheadmaps/{map}_radar_*` (plain, then lower/higher floors). Output: `ws_{name}.png` + `.vtex` under the panorama maps folder, **350×197**, class `.mv-map-ws_{name}`. Duplicate names: `ws_{name}_1`, `_2`, …. No usable shot → package skipped (HUD uses default).

### Official maps (`GAME_DIR`)

Reads `pak01_dir.vpk` (pass `game/csgo` or the VPK). Keeps maps with a 360p screenshot except `random` and `lobby_*`. Output: `map_{name}.png` + `.vtex`, class `.mv-map-map_{name}`. HUD picks `map_` vs `ws_` from map origin; same name can exist as both.
