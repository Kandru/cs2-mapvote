# Behavior

## Catalog

[`MapCatalog`](../src/MapVote/Maps/MapCatalog.cs) loads maps over localhost RCON (`127.0.0.1`, server port + `rcon_password`). Empty password → no load (logged once). Commands: `maps *` (local) and `print_mapgroup_sv` (workshop); merge capped at `MaxMaps` (100). Keys in config use `map_` / `ws_` from [`MapOrigin`](../src/MapVote/Maps/MapEntry.cs).

Refresh runs on the match win panel (not on plugin load / map start). The worker retries until both replies look ready ([`MapListReadiness`](../src/MapVote/Maps/MapListReadiness.cs)) or `endmatch.catalog_wait_seconds` elapses (further capped by the win-panel budget). A blank or banner-only body is not published over a previous good list. Worker thread → `Server.NextFrame` for `maps_votable` sync and vote start. A generation counter invalidates late callbacks across map changes.

## End-match vote

Only [`EventCsWinPanelMatch`](../src/MapVote/MapVote.cs) starts the vote ([`MapVote.VoteFlow.cs`](../src/MapVote/MapVote.VoteFlow.cs)). `EventCsIntermission` is not used (it always fires first; the win panel is the menu trigger).

1. `ArmEngineVote` (map start / RTV) - if any votable map exists (or catalog still loading / empty): turn the Valve vote off (`EngineVotePrep.PrepareCustomVote`); else enable native CS2 end-match vote. Does not write `mp_match_restart_delay` or `mp_win_panel_display_time`.
2. Win panel - turn native vote off, start a catalog refresh inside the query budget, wait for that refresh (or a short watchdog).
3. Settled list with candidates - open the Panorama HUD. Vote length is whatever time remains before the winner reveal inside the owner's `mp_win_panel_display_time` (not `endmatch.vote_seconds`). Then reveal (`winner_reveal_seconds`), then `changelevel` one second before the panel ends.
4. Settled empty / no candidates - enable the Valve vote; `endmatch.vote_seconds` is how long that UI accepts clicks (`mp_endmatch_votenextleveltime` only).

Master clock: read `mp_win_panel_display_time` (example 60s). Schedule from the win-panel epoch via [`VoteClock.ResolveCustom`](../src/MapVote/Voting/VoteClock.cs): changelevel at panel−1s; reveal immediately before that; vote fills the rest after the query. Session state: [`VoteSession`](../src/MapVote/Voting/VoteSession.cs).

## RTV

[`MapVote.Commands.cs`](../src/MapVote/MapVote.Commands.cs): `css_rtv` / `rtv` (chat `!rtv`). Humans only; one vote per SteamID. Need `ceil(humans * ratio)`. On success: chat, `ArmEngineVote`, run `rtv.success_command`. Blocked while a vote is already busy.

## HUD

- Layout path: `panorama/layout/custom_game/mapvote/vote_{PanoramaVersion}.vxml_c` (`PanoramaVersion` = `v1` in [`Version.cs`](../src/MapVote/Version.cs)).
- Styles: `hud_v1` under workshop content; sources in `workshop/content/`.
- Paint / clicks: [`VoteHud`](../src/MapVote/Huds/VoteHud.cs) + [`CustomHud`](../src/MapVote/Utils/CustomHud.cs) (`CheckTransmit`, dialog vars, classes, input capture). Fixed slot pools - see [PANORAMA_RULESET](../stuff/PANORAMA_RULESET.md).
- Map tile classes: `mv-map-map_{name}` / `mv-map-ws_{name}` / `mv-map-default` ([images.md](images.md)).

## Localization

Chat + HUD strings: [`lang/en.json`](../src/MapVote/lang/en.json), [`lang/de.json`](../src/MapVote/lang/de.json). Filter/sort chip labels use `filter.{id}` / `sort.{id}` matching config ids.

## Entry points (partials)

| File | Role |
| --- | --- |
| `MapVote.cs` | Load/unload, catalog/archive, config, win panel, disconnect, HUD clicks |
| `MapVote.VoteFlow.cs` | Arm / win-panel start / reveal / native fallback |
| `MapVote.Commands.cs` | RTV |
| `Huds/VoteHud.cs` | Panorama paint |
| `Maps/MapCatalog.cs` | RCON list |
| `Voting/VoteSession.cs` | Votes / winner |
