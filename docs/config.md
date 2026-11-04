# Config

Path: `addons/counterstrikesharp/configs/plugins/MapVote/MapVote.json` (created on first run). Source of truth: [`PluginConfig.cs`](../src/MapVote/Config/PluginConfig.cs). Disable the plugin in CounterStrikeSharp to turn MapVote off entirely.

| Key | Default | Meaning |
| --- | --- | --- |
| `debug` | `false` | Extra console logging |
| `endmatch.exclude_current_map` | `true` | Hide the map just played |
| `endmatch.vote_seconds` | `null` | When set, written to `mp_endmatch_votenextleveltime` on the win-panel event (shared maximum for native and custom). `null` leaves the server cvar alone |
| `endmatch.win_panel_display_time` | `null` | When set, written to `mp_win_panel_display_time` on the win-panel event. `null` leaves the server cvar alone |
| `endmatch.winner_reveal_seconds` | `5` | Locked-winner hold after our vote (custom menu only); taken off the end of the post-panel phase |
| `rtv.enabled` | `true` | `!rtv` / `css_rtv` / `rtv` |
| `rtv.ratio` | `0.6` | Fraction of humans required (clamped 0.01–1) |
| `rtv.duration_seconds` | `60` | Seconds the RTV window stays open after the first vote. `0` = no limit |
| `rtv.announce_seconds` | `15` | Seconds between chat reminders while RTV is open. `0` = no reminders |
| `rtv.success_command` | `mp_halftime 0; mp_maxrounds 1` | Server command after RTV passes (forces match toward end) |
| `filters.enabled` | `true` | Type / sort chip bar |
| `filters.types` | bomb/hostage/… | Chip groups: `id` + `prefixes` (map-name prefixes; empty → “other”) |
| `filters.sorts` | most/least/favorites | `id`, `by` (`plays` / `name` / `favorite`), `order` (`asc` / `desc`), optional `only` (favorites-only list) |
| `maps_votable` | (auto) | Per-map show flags. Keys `map_{name}` (local) / `ws_{name}` (workshop). `true` shows, `false` hides. New maps default `true`; keys for removed maps stay in the file |

Server cvars (not required in this file): `mp_win_panel_display_time` is the delay before the custom menu opens and the RCON catalog budget (`max(2, panel − 1)`). `mp_endmatch_votenextleveltime` is the end-match deadline from the win-panel epoch; the plugin `changelevel`s one second before it. Optional config overrides above write those cvars on the win-panel event. The plugin does not write `mp_match_restart_delay`.

Default type chips: `de_` bomb, `cs_` hostage, `ar_` arms_race, `dz_` danger_zone, empty-prefix `other`. Default sorts: `most_played` / `least_played` by `plays`, `favorites` by `favorite` with `only: true`.
