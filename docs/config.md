# Config

Path: `addons/counterstrikesharp/configs/plugins/MapVote/MapVote.json` (created on first run). Source of truth: [`PluginConfig.cs`](../src/MapVote/Config/PluginConfig.cs). Disable the plugin in CounterStrikeSharp to turn MapVote off entirely.

| Key | Default | Meaning |
| --- | --- | --- |
| `debug` | `false` | Extra console logging |
| `endmatch.exclude_current_map` | `true` | Hide the map just played |
| `endmatch.vote_seconds` | `15` | Open voting window (custom menu or native CS2 vote) |
| `endmatch.winner_reveal_seconds` | `5` | Locked-winner hold after our vote (custom menu only) |
| `rtv.enabled` | `true` | `!rtv` / `css_rtv` / `rtv` |
| `rtv.ratio` | `0.6` | Fraction of humans required (clamped 0.01–1) |
| `rtv.success_command` | `mp_halftime 0; mp_maxrounds 1` | Server command after RTV passes (forces match toward end) |
| `filters.enabled` | `true` | Type / sort chip bar |
| `filters.types` | bomb/hostage/… | Chip groups: `id` + `prefixes` (map-name prefixes; empty → “other”) |
| `filters.sorts` | most/least/favorites | `id`, `by` (`plays` / `name` / `favorite`), `order` (`asc` / `desc`), optional `only` (favorites-only list) |
| `maps_votable` | (auto) | Per-map show flags. Keys `map_{name}` (local) / `ws_{name}` (workshop). `true` shows, `false` hides. New maps default `true`; keys for removed maps stay in the file |

Default type chips: `de_` bomb, `cs_` hostage, `ar_` arms_race, `dz_` danger_zone, empty-prefix `other`. Default sorts: `most_played` / `least_played` by `plays`, `favorites` by `favorite` with `only: true`.
