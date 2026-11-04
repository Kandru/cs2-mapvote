# Config

Path: `addons/counterstrikesharp/configs/plugins/MapVote/MapVote.json` (created on first run). Source of truth: [`PluginConfig.cs`](../src/MapVote/Config/PluginConfig.cs). Disable the plugin in CounterStrikeSharp to turn MapVote off entirely.

| Key | Default | Meaning |
| --- | --- | --- |
| `debug` | `false` | Extra console logging |
| `endmatch.exclude_current_map` | `true` | Hide the map just played |
| `endmatch.vote_seconds` | `15` | Length of the Valve end-match vote if we fall back (sets `mp_endmatch_votenextleveltime` only). The custom menu open window is derived from `mp_win_panel_display_time` minus query time and the winner reveal |
| `endmatch.winner_reveal_seconds` | `5` | Locked-winner hold after our vote (custom menu only) |
| `endmatch.catalog_wait_seconds` | `8` | Max seconds to retry RCON map-list queries on the win panel. Further capped so the reveal and a 1s spare still fit. `0` = one attempt |
| `rtv.enabled` | `true` | `!rtv` / `css_rtv` / `rtv` |
| `rtv.ratio` | `0.6` | Fraction of humans required (clamped 0.01–1) |
| `rtv.duration_seconds` | `60` | Seconds the RTV window stays open after the first vote. `0` = no limit |
| `rtv.announce_seconds` | `15` | Seconds between chat reminders while RTV is open. `0` = no reminders |
| `rtv.success_command` | `mp_halftime 0; mp_maxrounds 1` | Server command after RTV passes (forces match toward end) |
| `filters.enabled` | `true` | Type / sort chip bar |
| `filters.types` | bomb/hostage/… | Chip groups: `id` + `prefixes` (map-name prefixes; empty → “other”) |
| `filters.sorts` | most/least/favorites | `id`, `by` (`plays` / `name` / `favorite`), `order` (`asc` / `desc`), optional `only` (favorites-only list) |
| `maps_votable` | (auto) | Per-map show flags. Keys `map_{name}` (local) / `ws_{name}` (workshop). `true` shows, `false` hides. New maps default `true`; keys for removed maps stay in the file |

Server cvar (not in this file): `mp_win_panel_display_time` is the master end-match clock. The plugin reads it and `changelevel`s one second before it ends. It does not write that cvar, and it does not write `mp_match_restart_delay` (same-map restart countdown when `mp_match_end_restart` is 1).

Default type chips: `de_` bomb, `cs_` hostage, `ar_` arms_race, `dz_` danger_zone, empty-prefix `other`. Default sorts: `most_played` / `least_played` by `plays`, `favorites` by `favorite` with `only: true`.
