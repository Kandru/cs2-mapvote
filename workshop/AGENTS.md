# MapVote HUD (AI)

Shared driver: [`src/MapVote/Utils/CustomHud.cs`](../src/MapVote/Utils/CustomHud.cs) (layout spawn, transmit, `SetText` / `SetHasClass` / image class swap, staggered `OnTick`, 1s opacity fade via `ph-fading` then `ph-off`). Painter: [`src/MapVote/Huds/VoteHud.cs`](../src/MapVote/Huds/VoteHud.cs). Menu input: [`src/MapVote/Utils/HudMenu.cs`](../src/MapVote/Utils/HudMenu.cs). **One stylesheet:** [`hud.vcss`](content/panorama/styles/custom_game/mapvote/hud.vcss) (validator-safe, see [panorama-css.md](panorama-css.md)).

`workshop/content` is the addon root: copy that folder into `csgo_addons/<addon>/` and publish. `AGENTS.md`, `panorama-css.md`, and `preview/` stay beside it and are not part of the paste. Layouts are loaded as `panorama/layout/custom_game/mapvote/vote_<PanoramaVersion>.vxml_c`.

## Panorama versions

Source names in this repo stay `vote.xml` and `hud.vcss`. Do **not** add `v1` / `v2` to those filenames here.

`PanoramaVersion` in [`src/MapVote/Version.cs`](../src/MapVote/Version.cs) is the generation this build spawns (`vote_vN.vxml_c` via `CustomHud`). On copy into the workshop addon, rename the layout to `vote_vN.vxml` and update the stylesheet include by hand.

Bump `PanoramaVersion` only when a layout or stylesheet change would break plugins still bound to the previous generation. Leave every older generation in the published addon.

## Server can only

1. Dialog variable on the layout's root card id → Label `text="{s:name}"` (text only, never a path or colour).
2. `SetHasClass` on a panel **id**.

No panel creation, no width/colour/image path from C#. Map images are a CSS class palette (`.mv-map-<name>`). C# swaps the class on `mv-shot-N`. Bars and accents are classes. Layout edits need a VPK republish; C# does not.

**Round restart:** `custom_hud_layout` entities stay alive across rounds. `CustomHud.ResetRound()` restores XML defaults. Never Kill/respawn a live layout mid-match; `CustomHud.Shutdown` only runs on map end / unload.

**Fade:** `ShowPanel` mounts with `ph-fading` then clears it next frame. `HidePanel` / `BeginFadeOut` set `ph-fading` and keep transmit for 1s, then collapse with `ph-off`. Do not set `ph-off` in the same write as the fade-out start.

## Layout

| Layout | Root card id | Routed ids |
| --- | --- | --- |
| `vote.xml` | `Vote` | `Vote`, `mv-voted-row`, `mv-voted-0`…`mv-voted-49`, `mv-grid`, `mv-0`…`mv-49`, shots/votes twins, `mv-winner`, `mv-title`, `mv-timer` |

`tools/validate_panorama.py` (`make panorama`) checks that the ids and `{s:…}` variables match what C# writes, that every `.mv-map-*` class has a matching PNG + VTEX, and regenerates the browser preview under [`preview/`](preview/).

### Vote overlay ([`VoteHud`](../src/MapVote/Huds/VoteHud.cs))

Fullscreen glass (`.mv-bg` + `world-blur`). Title + timer, then a horizontal voted strip (`VotedRow`, `{s:vvN}`) and a wrap grid of the full catalog (`MapGrid`, `{s:vN}`). Up to `SlotCount = 50` maps total (`MapCatalog.MaxMaps` must match).

- Empty slots / empty voted strip: `is-empty` → `visibility: collapse`.
- Map art: class swap on `mv-shot-N` / `mv-voted-shot-N` (`mv-map-<sanitized>` or `mv-map-default`).
- Greyscale: `saturation: 0` on `.MapShot`. Colour on `:hover` and `.is-voted`.
- Vote count: sibling Labels (white + black twin outline). Image gets `blur` when voted — same overlay on strip and grid tiles.
- Player's own choice: `is-mine` on the tile.
- Voted strip: maps with votes, sorted by count (first-vote order breaks ties); horizontal scroll past five. Strip slots pulse via `bump-a` / `bump-b` when count or order changes.
- Grid: full catalog order, five fluid columns (`width: 20%` + `height: width-percentage(56.25%)`), vertical scroll. Voted maps stay here too so players can find them.
- Reveal: strip + grid collapse, `#mv-winner` shows for the last 5 seconds. Clicks ignored; input capture off. Pulse via `tick-a` / `tick-b`.

Browser mock: open [`preview/vote.html`](preview/vote.html) (synced from `vote.xml` + `hud.vcss` on every `make panorama`).

Mouse capture via `HudMenu` so `:hover` works. Clicks arrive as `Listeners.OnCustomHudClicked` → `HudMenu` → `VoteHud.OnButton`.

## Images

`make images WORKSHOP_DIR=…` extracts the largest suitable screenshot/radar from each map VPK, center-crops to 16:9, resizes to **350×197**, writes `<map>.png` + `.vtex`, and rewrites the marked CSS block of `mv-map-*` classes. Clients only see images published in this addon. Always keep `default.png` / `default.vtex` as the fallback class (`mv-map-default`).

## Check before inventing CSS

[panorama-css.md](panorama-css.md) for the layout model. `make panorama` for the reject list. Engine skill pack: [`stuff/panorama-skills/`](../stuff/panorama-skills/).

Update this file when HUD behaviour changes.
