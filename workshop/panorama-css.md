# Panorama CSS (MapVote HUD)

Web CSS is dropped with no log. Run `make panorama` (`tools/validate_panorama.py`) before calling a layout done — it rejects unknown properties, forbidden values, and the layout traps below.

**One stylesheet:** [`content/panorama/styles/custom_game/mapvote/hud.vcss`](content/panorama/styles/custom_game/mapvote/hud.vcss) (symlink `hud.css` for validators).

## Layout model

`flow-children: down|right|right-wrap|none`. Size with `width`/`height` (`px`, `%`, `fit-children`, `fill-parent-flow(n)`). Parent must be sized before a child `%` resolves.

**Padding does not inset `width/height: 100%` children.** Those measure the border box. Fullscreen shells that need a clear margin use spacer panels in the flow, or longhand padding when `ph-bg` / `.mv-bg` uses `ignore-parent-flow`.

**Centered text:** give the Label an **explicit px width** (or `%` under a sized parent) plus `text-align: center`. Prefer stock `horizontal-center` / `text-align-center` from `csgostyles` as well.

`horizontal-align` / `vertical-align` position the panel in its parent. Use `vertical-align: middle` (not `center`). `ignore-parent-flow` for overlays (`.mv-bg`).

## Values that work here

`background-color` `#rrggbb` / `#rrggbbaa` / `gradient(linear, …)` / `none`. **No `rgba()`/`rgb()`/`hsl()`.** `background-image` + `background-size: 100% 100%` (or `contains`) + `background-position` / `background-repeat` / `background-img-opacity`. The engine rejects web `contain` (falls back to `auto`).

`font-size` is **unitless** (`font-size: 48`, not `48px`). Beat stock Stratum with `Label.ClassName` selectors.

Colours are hex literals only. No `@define` / `var()`.

`visibility: collapse` (there is no `display`). `overflow: noclip|clip|scroll`. `z-index` (siblings only; HUD-above-crosshair = `99999` on `.HudScreen`).

Blur:

- `world-blur` — frosted glass over the world / backbuffer (use on `.mv-bg`)
- `blur` — this panel and its children (use on `.MapShot` when voted so the vote number stays sharp as a sibling)
- `background-blur` — registered and broken; do not use

Composition colour (inherits to children): `saturation` (0 = greyscale), `brightness`, `wash-color`, `opacity`.

`box-shadow`: **colour first** — `#000000aa 0px 4px 8px 0px`. Transitions: split `transition-property` / `transition-duration` / `transition-timing-function`.

## MapVote classes

`.mv-bg` fullscreen glass · `.VotedRow` horizontal voted strip · `.MapGrid` wrap flow · `.MapTile` (five-across via `width: 20%`) / `.MapShot` / `.MapVotes` / `.MapVotesBack` · `.is-empty` / `.is-voted` / `.is-mine` · `bump-a` / `bump-b` strip pulse · `.Winner` reveal card · `tick-a` / `tick-b` pulse · `.mv-map-*` image palette · `.ph-card` / `.ph-fading` / `.ph-off`. Browser preview: [`preview/vote.html`](preview/vote.html) (synced by `make panorama`).

Hide empty slots with `is-empty` → `visibility: collapse`. Do not name a class `hidden` / `Hidden`.

## Do not

`display` / flex / grid, `calc()`, `var()`, `@define`, `@media`, `::before` / `::after`, `background-size: contain` (use `contains` or `100% 100%`), `visibility: hidden`, `overflow: hidden`, class name `hidden`, `font-size` with a unit, `vertical-align: center`, bareword theme tokens, `background-blur`, `@keyframes`.

`:hover` / `:active` work only while input capture is on.
