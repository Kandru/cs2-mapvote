# Config

Path: `addons/counterstrikesharp/configs/plugins/MapVote/MapVote.json` (created on first run). Source of truth: [`PluginConfig.cs`](../src/MapVote/Config/PluginConfig.cs). Disable the plugin in CounterStrikeSharp to turn MapVote off entirely.

| Key | Default | Meaning |
| --- | --- | --- |
| `debug` | `false` | Extra console logging |
| `clean_map_names` | `true` | Show `de_dust2` as `Dust2` and `de_cache_night` as `Cache Night`: drop a leading type token of at most 4 characters (`de_`, `cs_`, `rush_`, `surf_`, ...), turn remaining underscores into spaces, and capitalize each word. A longer leading token is part of the name (`mymapname_1` stays `Mymapname 1`). Also the key for name sorting. `false` keeps raw map ids |
| `endmatch.exclude_current_map` | `true` | Hide the map just played |
| `endmatch.vote_seconds` | `null` | Menu length. `null` uses the server's `mp_endmatch_votenextleveltime` as first read. The engine timer is then written longer (vote length + 4s) so it does not change the map before the plugin |
| `endmatch.max_maps` | grid pool size (`VoteIds.SlotCount`, currently 200) | Most maps offered in the end-match vote, clamped to 1…`VoteIds.SlotCount`. Above that many votable maps, the maps at the end of the configured base order are left out each vote; nominations are always kept. The `!maps` browser is not capped by this value; it uses the full grid pool |
| `endmatch.winner_reveal_seconds` | `5` | Locked-winner hold after our vote (custom menu only); taken off the end of the time after the 6s match-results hold |
| `nominations.enabled` | `true` | Allow map picks through `!maps` / `!nom` / `!nominate` during the round. Nominated maps are guaranteed candidates in the end-match vote and the nominator's vote is prefilled there |
| `rtv.enabled` | `true` | `!rtv` / `css_rtv` / `rtv` |
| `rtv.ratio` | `0.6` | Fraction of humans required (clamped 0.01–1) |
| `rtv.duration_seconds` | `60` | Seconds the RTV window stays open after the first vote. `0` = no limit |
| `rtv.announce_seconds` | `15` | Seconds between chat reminders while RTV is open. `0` = no reminders |
| `rtv.success_command` | `mp_halftime 0; mp_maxrounds 1` | Server command after RTV passes (forces match toward end) |
| `catalog.query_local_maps` | `true` | Query `maps *` over RCON and treat all results as local/internal maps |
| `catalog.query_mapgroup` | `true` | Query `print_mapgroup_sv` over RCON. A `Map group: workshop` reply lists workshop maps by bare name; admin mapgroups use bare local names and `workshop/<id>/<name>` paths for workshop maps. With both switches `false` no catalog query runs and the previous list is kept |
| `filters.enabled` | `true` | Type / sort chip bar |
| `filters.types` | bomb/hostage/… | Chip groups: `id` + `prefixes` (map-name prefixes; empty → “other”) |
| `display.order` | `shuffle` | Base order of the map list: `shuffle` (assigned once per match, reused by browse and the vote) or `alphabetical` |
| `filters.sorts` | A-Z / newest / oldest / most positive / most negative | `id`, `by` (`newest` / `oldest` / `plays` / `rating` / `name` / `favorite`), `order` (`asc` / `desc`). At most `VoteIds.FilterSortSlots` (5) are shown. Sorts only reorder; they never hide maps. `newest`/`oldest` use `maps.*.added`; maps without a timestamp sort last. `rating` is net likes − dislikes |
| `maps` | (auto) | Per-map settings. Keys `map_{name}` (local) / `ws_{name}` (workshop). Each value is `{ "enabled": true, "added": <unix_seconds> }`. Missing key = shown; `added` is recorded on first discovery and drives the newest/oldest sorts. On a fully ready catalog refresh, keys for maps that no longer exist are removed. The old `maps_votable` key is not read |

Server cvars (not required in this file): on intermission the plugin loads the map list (1s) and by 2s either disables the Valve vote or arms it. The menu clock starts at the win panel, about 3s after intermission. The first 6 seconds are match results. The plugin opens its menu at 6s and `changelevel`s one second before the vote length elapses. On map start, and again when the custom path is chosen, `mp_endmatch_votenextleveltime` and `mp_match_restart_delay` are set to the vote length plus 4s so the engine timer expires after that `changelevel`. The plugin does not write `mp_win_panel_display_time`. A stale `win_panel_display_time` key in an existing `MapVote.json` is ignored.

Default type chips: `de_` bomb, `cs_` hostage, `ar_` arms_race, `dz_` danger_zone, empty-prefix `other`. Default sorts: `alphabetical` by name (selected by default), `newest` by first-seen descending, `oldest` ascending, `most_positive` by rating descending, `most_negative` by rating ascending. Every map stays in the list; the sort only changes the order. Existing configs with exactly the old default sort sets are migrated once. Likes and dislikes are stored per player as `likes` / `dislikes` in the player JSON and aggregated server-wide for the rating sorts, the card badges and the current-map counters.
