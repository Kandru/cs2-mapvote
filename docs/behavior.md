# Behavior

## Catalog

[`MapCatalog`](../src/MapVote/Maps/MapCatalog.cs) loads maps over localhost RCON (`127.0.0.1`, server port + `rcon_password`). Empty password → no load (logged once). Commands: `maps *` (local) and `print_mapgroup_sv` (workshop); merge capped at `MaxMaps` (100). Keys in config use `map_` / `ws_` from [`MapOrigin`](../src/MapVote/Maps/MapEntry.cs).

Refresh runs on intermission (not on plugin load, map start, or the win panel). The worker retries until both replies look ready ([`MapListReadiness`](../src/MapVote/Maps/MapListReadiness.cs)) or the 1s budget elapses. The path is chosen when that refresh settles, or at 2s if it has not. A later settle does not flip the path. A blank or banner-only body is not published over a previous good list. Worker thread → `Server.NextFrame` for `maps_votable` sync. A generation counter invalidates late callbacks across map changes.

## End-match vote

[`EventCsIntermission`](../src/MapVote/MapVote.cs) closes RTV and starts the path decision ([`MapVote.VoteFlow.cs`](../src/MapVote/MapVote.VoteFlow.cs), [`EndMatchFlow`](../src/MapVote/Voting/EndMatchFlow.cs)). The win panel is about 3s later and only opens our HUD. Map start and RTV do not arm or disable the Valve vote.

1. Intermission — catalog refresh, 1s budget. As soon as it settles, or at 2s at the latest, decide from the list already in hand.
2. Candidates — `mp_endmatch_votenextmap 0` and `mp_match_end_restart 0`. The option list is left alone until the win panel.
3. No candidates — `mp_endmatch_votenextmap 1` and `mp_match_end_restart 0`. Our HUD stays closed. The Valve panel is the only vote. Its option list is not cleared.
4. Win panel — custom path only. Writes `-1` into `EndMatchMapGroupVoteOptions` and `EndMatchMapGroupVoteTypes` so the Valve panel has an empty list. Optionally write `endmatch.vote_seconds` to `mp_endmatch_votenextleveltime`. Does not query RCON. If the win panel arrives before the 2s decision, the decision runs first with the current list. After the fixed 6s match-results hold, the Panorama HUD opens. Reveal is reserved at the end of the remaining window. `nextlevel` is set when the winner locks. `changelevel` (or `ds_workshop_changelevel`) runs at `mp_endmatch_votenextleveltime − 1`, measured from the win panel. A second change is ignored. Map end does not issue one.

Master clock for the menu: win-panel epoch via [`VoteClock.ResolveCustom`](../src/MapVote/Voting/VoteClock.cs). Example with vote time 20 and reveal 5: menu at 6s, reveal at 14s, `changelevel` at 19s. Session state: [`VoteSession`](../src/MapVote/Voting/VoteSession.cs).

## RTV

[`MapVote.Commands.cs`](../src/MapVote/MapVote.Commands.cs): `css_rtv` / `rtv` (chat `!rtv`). Humans only; one vote per SteamID. Need `ceil(humans * ratio)`. On success: chat, then `rtv.success_command`. The native vote is left for intermission to decide. Blocked while an end-match phase is already running.

The first vote opens a window: `rtv.duration_seconds` (default 60; `0` = no limit) cancels and clears votes when it elapses; `rtv.announce_seconds` (default 15; `0` = off) posts the tally in chat on that interval. Later votes do not reset the deadline. Timers stop without an expiry line when RTV passes, the last voter leaves, intermission fires, the map starts, or the plugin unloads.

`EventCsIntermission` closes RTV (clears voters, stops timers, rejects further `!rtv` with `rtv.ended`). It stays closed while our vote is on screen. It opens again on `OnMapStart`, or when the same map starts another match: `EventRoundAnnounceWarmup`, `EventRoundAnnounceMatchStart`, or `EventBeginNewMatch`. A finished native vote returns to idle at that point so the next intermission can decide again.

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
| `MapVote.cs` | Load/unload, catalog/archive, config, intermission, win panel, disconnect, HUD clicks |
| `MapVote.VoteFlow.cs` | Intermission decision, win-panel HUD, reveal, changelevel |
| `MapVote.Commands.cs` | RTV |
| `Huds/VoteHud.cs` | Panorama paint |
| `Maps/MapCatalog.cs` | RCON list |
| `Voting/VoteSession.cs` | Votes / winner |
