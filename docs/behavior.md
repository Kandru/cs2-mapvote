# Behavior

## Catalog

[`MapCatalog`](../src/MapVote/Maps/MapCatalog.cs) loads maps over localhost RCON (`127.0.0.1`, server port + `rcon_password`). Empty password → no load (logged once). Commands: `maps *` (local) and `print_mapgroup_sv` (workshop); merge capped at `MaxMaps` (100). Keys in config use `map_` / `ws_` from [`MapOrigin`](../src/MapVote/Maps/MapEntry.cs). Both replies use one playability filter ([`MapNameFilter`](../src/MapVote/Maps/MapNameFilter.cs)): paths, `workshop_preview_*`, `*_vanity`, `*_skybox`, `error`, `graphics_settings`, `lobby` and `lobby_mapveto` are skipped.

Refresh runs on intermission (not on plugin load, map start, or the win panel). The worker retries until both replies look ready ([`MapListReadiness`](../src/MapVote/Maps/MapListReadiness.cs)) or the 1s budget elapses. The path is chosen when that refresh settles, or at 2s if it has not. A later settle does not flip the path. A blank or banner-only body is not published over a previous good list. Worker thread → `Server.NextFrame` for `maps_votable` sync. A generation counter invalidates late callbacks across map changes.

## End-match vote

[`EventCsIntermission`](../src/MapVote/MapVote.cs) closes RTV and starts the path decision ([`MapVote.VoteFlow.cs`](../src/MapVote/MapVote.VoteFlow.cs), [`EndMatchFlow`](../src/MapVote/Voting/EndMatchFlow.cs)). The win panel is about 3s later and only opens our HUD. Map start writes the stretched engine timer. RTV does not arm or disable the Valve vote.

1. Intermission — catalog refresh, 1s budget. As soon as it settles, or at 2s at the latest, decide from the list already in hand.
2. Candidates — `mp_endmatch_votenextmap 0`, `mp_match_end_restart 0`, and the engine timer (`mp_endmatch_votenextleveltime` and `mp_match_restart_delay`) set to the vote length plus 4s so it expires after our `changelevel`. Blank `EndMatchMapGroupVoteOptions` and `EndMatchMapGroupVoteTypes` to `-1` now and once more next frame, then mark `m_pGameRules` changed so the client receives the empty list.
3. No candidates — `mp_endmatch_votenextmap 1` and `mp_match_end_restart 0`. Our HUD stays closed. The Valve panel is the only vote. Its option list is not cleared.
4. Win panel — custom path only. Blanks the native option list again and marks `m_pGameRules` changed so clients drop the maps. Optionally the menu length still follows `endmatch.vote_seconds`. Does not query RCON. If the win panel arrives before the 2s decision, the decision runs first with the current list. After the fixed 6s match-results hold, the Panorama HUD opens. Reveal is reserved at the end of the remaining window. `nextlevel` is set when the winner locks. `changelevel` (or `ds_workshop_changelevel`) runs one second before the vote length elapses, measured from the win panel — not from the stretched engine timer. A second change is ignored. Map end does not issue one. The same engine hold is also written on map start, before the next intermission reads it.

Master clock for the menu: win-panel epoch via [`VoteClock.ResolveCustom`](../src/MapVote/Voting/VoteClock.cs). Example with vote time 20 and reveal 5: menu at 6s, reveal at 14s, `changelevel` at 19s. Session state: [`VoteSession`](../src/MapVote/Voting/VoteSession.cs).

## RTV

[`MapVote.Commands.cs`](../src/MapVote/MapVote.Commands.cs): `css_rtv` / `rtv` (chat `!rtv`). Humans only; one vote per SteamID. Need `ceil(humans * ratio)`. On success: chat, then `rtv.success_command`. The native vote is left for intermission to decide. Blocked while an end-match phase is already running.

The first vote opens a window: `rtv.duration_seconds` (default 60; `0` = no limit) cancels and clears votes when it elapses; `rtv.announce_seconds` (default 15; `0` = off) posts the tally in chat on that interval. Later votes do not reset the deadline. Timers stop without an expiry line when RTV passes, the last voter leaves, intermission fires, the map starts, or the plugin unloads.

`EventCsIntermission` closes RTV (clears voters, stops timers, rejects further `!rtv` with `rtv.ended`). It stays closed while our vote is on screen. It opens again on `OnMapStart`, or when the same map starts another match: `EventRoundAnnounceWarmup`, `EventRoundAnnounceMatchStart`, or `EventBeginNewMatch`. A finished native vote returns to idle at that point so the next intermission can decide again.

## Map browser

`css_maps` / `maps` (chat `!maps`) opens the same Panorama overlay in browse mode with [`VoteHud`](../src/MapVote/Huds/VoteHud.cs): type and Favorites chips, sort chips, the current-map card with rating, and up to 100 shuffled map cards. It has no vote target and no Leading ranking; map tiles are not clickable. The Close button closes the overlay for that player.

The command triggers a short catalog refresh when the list is still empty after plugin load, then opens for everyone who was waiting. Intermission (`EventCsIntermission`) and map end close the browser and reject `!maps` until `OnMapStart`, warmup, match start, or a new match. An active end-match vote also rejects it (`maps.busy`).

## HUD

- Layout path: `panorama/layout/custom_game/mapvote/vote_{PanoramaVersion}.vxml_c` (`PanoramaVersion` = `v2` in [`Version.cs`](../src/MapVote/Version.cs)).
- Styles: `hud_v2` (UI) + `maps_v1` (generated map images, shared with v1) under workshop content; sources in `workshop/content/`. The v1 pair (`vote_v1.xml`, `hud_v1.vcss`) stays in the addon for servers still on the old plugin. Layout v2 is generated by `tools/generate_vote_xml_v2.py`; its docstring is the id contract that [`VoteIds`](../src/MapVote/Huds/VoteIds.cs) mirrors.
- Paint / clicks: [`VoteHud`](../src/MapVote/Huds/VoteHud.cs) + [`CustomHud`](../src/MapVote/Utils/CustomHud.cs) (`CheckTransmit`, dialog vars, classes, input capture). Fixed slot pools - see [PANORAMA_RULESET](../stuff/PANORAMA_RULESET.md). The XML pool is 100 grid cards + 6 ranking rows; the grid is written globally, so the map count no longer scales with player count. Only per-player state (`is-self`, favorites, filter visibility, minimize, own rating) is written per player. Type and favorites filters are per player; sort chips (browse only) reorder the shared grid. The catalog is shuffled at vote/browse start; the Leading ranking is global and shows the top 6 by vote count.
- The current-map card shows the current map with its favorite button, the question "How was this map?" and two labelled buttons, Like and Dislike, with server-wide counters. Each player's own rating is highlighted and can be changed. Rating is not possible during the reveal.
- The grid is one `flow-children: right-wrap` scroll panel of landscape cards, so the column count follows the viewport width; only the grid scrolls. `right-wrap` is the documented Panorama deviation and must be verified in game.
- Tiles: the vote badge is shown only when a map has votes, `is-self` marks the local vote and the favorite star sits top-right. The Leading ranking lists up to 6 maps with vote count and share bar, marks your own vote, and clicking a row votes for that map.
- Reveal: the grid is dimmed and a centered "Next map" card shows the winner with its vote count, or "random pick - no votes".
- Map tile classes: `mv-map-map_{name}` / `mv-map-ws_{name}` / `mv-map-default` ([images.md](images.md)).

## Localization

Chat + HUD strings: [`lang/en.json`](../src/MapVote/lang/en.json), [`lang/de.json`](../src/MapVote/lang/de.json). Filter/sort chip labels use `filter.{id}` / `sort.{id}` matching config ids. Shared texts (title, status, reveal) are written in the server language; players with another language get per-player overrides.

## Entry points (partials)

| File | Role |
| --- | --- |
| `MapVote.cs` | Load/unload, catalog/archive, config, intermission, win panel, disconnect, HUD clicks |
| `MapVote.VoteFlow.cs` | Intermission decision, win-panel HUD, reveal, changelevel |
| `MapVote.Commands.cs` | RTV |
| `MapVote.MapBrowser.cs` | `!maps` browser state, deferred catalog open |
| `Huds/VoteHud.cs` | Panorama paint (vote + browse) |
| `Huds/VoteView.cs` | Pure v2 decisions: countdown, share steps, grid visibility |
| `Huds/VoteIds.cs` | Layout v2 ids and dialog variables (mirrors the generator docstring) |
| `Maps/MapCatalog.cs` | RCON list |
| `Voting/VoteSession.cs` | Votes / winner |
