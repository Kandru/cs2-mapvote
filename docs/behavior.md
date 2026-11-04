# Behavior

## Catalog

[`MapCatalog`](../src/MapVote/Maps/MapCatalog.cs) loads maps over localhost RCON (`127.0.0.1`, server port + `rcon_password`). Empty password → no load (logged once). Commands: `maps *` (local) and `print_mapgroup_sv` (workshop); merge capped at `MaxMaps` (100). Keys in config use `map_` / `ws_` from [`MapOrigin`](../src/MapVote/Maps/MapEntry.cs).

Refresh runs on the match win panel (not on plugin load / map start). The worker retries until both replies look ready ([`MapListReadiness`](../src/MapVote/Maps/MapListReadiness.cs)) or the fixed 4s budget elapses. A settle after the 4s path decision is ignored. A blank or banner-only body is not published over a previous good list. Worker thread → `Server.NextFrame` for `maps_votable` sync. A generation counter invalidates late callbacks across map changes.

## End-match vote

Only [`EventCsWinPanelMatch`](../src/MapVote/MapVote.cs) starts the vote ([`MapVote.VoteFlow.cs`](../src/MapVote/MapVote.VoteFlow.cs)). `EventCsIntermission` fires earlier and only closes RTV until the next map; the win panel remains the menu trigger.

1. `ArmEngineVote` (map start / RTV) - if every catalog map is disabled: arm the Valve vote. Otherwise do nothing. Does not write `mp_match_restart_delay` or `mp_endmatch_votenextleveltime`.
2. Win-panel event (synchronous) - optionally write `endmatch.vote_seconds` to `mp_endmatch_votenextleveltime`; set `mp_match_end_restart 0`; start the catalog refresh. Do not arm the native vote and do not blank its slots.
3. At 4s - candidates ready: blank native vote slots once. No candidates, or the refresh missed the deadline: run `mp_endmatch_votenextmap 1` (native arm) and leave the slots alone. That second is the lead time before the engine shows a vote UI.
4. At 5s - custom path only: open the Panorama HUD. Clicks and the winner reveal share `mp_endmatch_votenextleveltime − 1 − 5`. Reveal is reserved at the end of that span. `changelevel` at `mp_endmatch_votenextleveltime − 1`.

Master clock: from the win-panel epoch via [`VoteClock.ResolveCustom`](../src/MapVote/Voting/VoteClock.cs). The first 5 seconds are the match-results hold (fixed, not a cvar). Example with vote time 20 and reveal 5: menu at 5s, reveal at 14s, `changelevel` at 19s. Session state: [`VoteSession`](../src/MapVote/Voting/VoteSession.cs).

## RTV

[`MapVote.Commands.cs`](../src/MapVote/MapVote.Commands.cs): `css_rtv` / `rtv` (chat `!rtv`). Humans only; one vote per SteamID. Need `ceil(humans * ratio)`. On success: chat, `ArmEngineVote`, run `rtv.success_command`. Blocked while a vote is already busy.

The first vote opens a window: `rtv.duration_seconds` (default 60; `0` = no limit) cancels and clears votes when it elapses; `rtv.announce_seconds` (default 15; `0` = off) posts the tally in chat on that interval. Later votes do not reset the deadline. Timers stop without an expiry line when RTV passes, the last voter leaves, the win panel starts, intermission fires, the map starts, or the plugin unloads.

`EventCsIntermission` closes RTV for the rest of the map (clears voters, stops timers, rejects further `!rtv` with `rtv.ended`). `OnMapStart` clears that lock.

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
