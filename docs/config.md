# Config

Path: `addons/counterstrikesharp/configs/plugins/MapVote/MapVote.json` (created on first run). Source of truth: [`PluginConfig.cs`](../src/MapVote/Config/PluginConfig.cs). Disable the plugin in CounterStrikeSharp to turn MapVote off entirely.

| Key | Default | Meaning |
| --- | --- | --- |
| `debug` | `false` | Extra console logging |
| `endmatch.exclude_current_map` | `true` | Hide the map just played |
| `endmatch.vote_seconds` | `null` | When set, written to `mp_endmatch_votenextleveltime` on the win-panel event (shared maximum for native and custom). `null` leaves the server cvar alone |
| `endmatch.winner_reveal_seconds` | `5` | Locked-winner hold after our vote (custom menu only); taken off the end of the time after the 5s match-results hold |
| `rtv.enabled` | `true` | `!rtv` / `css_rtv` / `rtv` |
| `rtv.ratio` | `0.6` | Fraction of humans required (clamped 0.01–1) |
| `rtv.duration_seconds` | `60` | Seconds the RTV window stays open after the first vote. `0` = no limit |
| `rtv.announce_seconds` | `15` | Seconds between chat reminders while RTV is open. `0` = no reminders |
| `rtv.success_command` | `mp_halftime 0; mp_maxrounds 1` | Server command after RTV passes (forces match toward end) |
| `filters.enabled` | `true` | Type / sort chip bar |
| `filters.types` | bomb/hostage/… | Chip groups: `id` + `prefixes` (map-name prefixes; empty → “other”) |
| `filters.sorts` | most/least/favorites | `id`, `by` (`plays` / `name` / `favorite`), `order` (`asc` / `desc`), optional `only` (favorites-only list) |
| `maps_votable` | (auto) | Per-map show flags. Keys `map_{name}` (local) / `ws_{name}` (workshop). `true` shows, `false` hides. New maps default `true`; keys for removed maps stay in the file |

Server cvars (not required in this file): `mp_endmatch_votenextleveltime` is the whole window from the win-panel event. The first 5 seconds are match results. The plugin opens its menu at 5s, asks RCON for at most 4s, and `changelevel`s one second before the cvar elapses. `endmatch.vote_seconds`, when set, is written to that cvar on the win-panel event. The plugin does not write `mp_match_restart_delay` or `mp_win_panel_display_time`. A stale `win_panel_display_time` key in an existing `MapVote.json` is ignored.

Default type chips: `de_` bomb, `cs_` hostage, `ar_` arms_race, `dz_` danger_zone, empty-prefix `other`. Default sorts: `most_played` / `least_played` by `plays`, `favorites` by `favorite` with `only: true`.
