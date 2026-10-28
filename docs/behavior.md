# Behavior

## Catalog

[`MapCatalog`](../src/MapVote/Maps/MapCatalog.cs) loads maps over localhost RCON (`127.0.0.1`, server port + `rcon_password`). Empty password → no load (logged once). Commands: `maps *` (local) and `print_mapgroup_sv` (workshop); merge capped at `MaxMaps` (100). Keys in config use `map_` / `ws_` from [`MapOrigin`](../src/MapVote/Maps/MapEntry.cs). Refresh on load; worker thread → `Server.NextFrame` for `maps_votable` sync.

## End-match vote

Intermission / win-panel events arm and start the vote ([`MapVote.Events.cs`](../src/MapVote/MapVote.Events.cs), [`MapVote.VoteFlow.cs`](../src/MapVote/MapVote.VoteFlow.cs)).

1. `ArmEngineVote` — if any votable map exists (or catalog still loading): hold the Valve vote closed (`EngineVotePrep.PrepareCustomVote`); else enable native CS2 end-match vote.
2. `StartVote` — candidates from catalog + `maps_votable` (+ optional exclude current). Empty + not loading → native vote. Empty + loading → wait ≤2s, then native or custom.
3. Custom path: open Panorama HUD, tick vote clock (`VoteClock` / `endmatch.vote_seconds`), then winner reveal (`winner_reveal_seconds`), then change map.

Timing resolved in [`VoteClock`](../src/MapVote/Voting/VoteClock.cs). Session state: [`VoteSession`](../src/MapVote/Voting/VoteSession.cs).

## RTV

[`MapVote.Commands.cs`](../src/MapVote/MapVote.Commands.cs): `css_rtv` / `rtv` (chat `!rtv`). Humans only; one vote per SteamID. Need `ceil(humans * ratio)`. On success: chat, `ArmEngineVote`, run `rtv.success_command`. Blocked while a vote is already busy.

## HUD

- Layout path: `panorama/layout/custom_game/mapvote/vote_{PanoramaVersion}.vxml_c` (`PanoramaVersion` = `v1` in [`Version.cs`](../src/MapVote/Version.cs)).
- Styles: `hud_v1` under workshop content; sources in `workshop/content/`.
- Paint / clicks: [`VoteHud`](../src/MapVote/Huds/VoteHud.cs) + [`CustomHud`](../src/MapVote/Utils/CustomHud.cs) (`CheckTransmit`, dialog vars, classes, input capture). Fixed slot pools — see [PANORAMA_RULESET](../stuff/PANORAMA_RULESET.md).
- Map tile classes: `mv-map-map_{name}` / `mv-map-ws_{name}` / `mv-map-default` ([images.md](images.md)).

## Localization

Chat + HUD strings: [`lang/en.json`](../src/MapVote/lang/en.json), [`lang/de.json`](../src/MapVote/lang/de.json). Filter/sort chip labels use `filter.{id}` / `sort.{id}` matching config ids.

## Entry points (partials)

| File | Role |
| --- | --- |
| `MapVote.cs` | Load/unload, catalog/archive, config |
| `MapVote.Events.cs` | Intermission, disconnect, HUD clicks |
| `MapVote.VoteFlow.cs` | Arm / start / reveal / native fallback |
| `MapVote.Commands.cs` | RTV |
| `Huds/VoteHud.cs` | Panorama paint |
| `Maps/MapCatalog.cs` | RCON list |
| `Voting/VoteSession.cs` | Votes / winner |
