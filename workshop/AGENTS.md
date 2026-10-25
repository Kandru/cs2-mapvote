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

No panel creation, no width/colour/image path from C#. Map images are a CSS class palette (`.mv-map-<name>`). C# swaps the class on `mv-shot-N` / `mv-voted-shot-N`. Layout edits need a VPK republish; C# does not.

**Round restart:** `custom_hud_layout` entities stay alive across rounds. `CustomHud.ResetRound()` restores XML defaults. Never Kill/respawn a live layout mid-match; `CustomHud.Shutdown` only runs on map end / unload.

**Fade:** `ShowPanel` mounts with `ph-fading` then clears it next frame. `HidePanel` / `BeginFadeOut` set `ph-fading` and keep transmit for 1s, then collapse with `ph-off`. Do not set `ph-off` in the same write as the fade-out start.

## Layout

| Layout | Root card id | Routed ids |
| --- | --- | --- |
| `vote.xml` | `Vote` | `Vote`, `mv-voted-row`, `mv-voted-0`…`49`, `mv-grid`, `mv-0`…`49`, shots/votes, `{s:vN}` / `{s:nN}` / `{s:vvN}` / `{s:vnN}` |

`tools/validate_panorama.py` (`make panorama`) checks that the ids and `{s:…}` variables match what C# writes, that every `.mv-map-*` class has a matching PNG + VTEX, and regenerates the browser preview under [`preview/`](preview/).

### Vote overlay ([`VoteHud`](../src/MapVote/Huds/VoteHud.cs))

Fullscreen CS2 end-of-match vote: top-dark scrim, **voted strip** (row 1) then catalog grid. Stage `max-width: 1420px` = **10** Valve cards across. Up to `SlotCount = 50`.

- Empty slots / empty voted strip: `is-empty` → `visibility: collapse`.
- Cards = Valve `map-selection-btn--vote` (110 × 175%, margins, fade, hover zoom, selected border).
- Vote count: orange `#ff9a1a`, count only (no `/max`).
- Voted strip: maps with votes, sorted by count; h-scroll if needed.
- Grid: full catalog, wrap + v-scroll; unused slots collapsed.
- Reveal: `.is-winner` green check on the winning tile; mouse capture drops.

Browser mock: [`preview/vote.html`](preview/vote.html). Preview shots load from [`preview/maps/*.webp`](preview/maps/) (not the addon VTEX palette). Plate: [`preview/endmatch.jpg`](preview/endmatch.jpg).

Mouse capture via `HudMenu`. Clicks → `Listeners.OnCustomHudClicked` → `HudMenu` → `VoteHud.OnButton`.

## Images

`make images WORKSHOP_DIR=…` extracts screenshots into the addon palette (`mv-map-*`). Preview-only art lives in `preview/maps/*.webp`. Always keep `default.png` / `default.vtex` (and `preview/maps/default.webp`).

## Check before inventing CSS

[panorama-css.md](panorama-css.md). `make panorama`. Ruleset: [`stuff/PANORAMA_RULESET.md`](../stuff/PANORAMA_RULESET.md).

Update this file when HUD behaviour changes.
