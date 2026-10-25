# CS2 Panorama Ruleset – server-driven HUDs

Binding reference for all work on Panorama layouts (`custom_hud_layout`) and the CounterStrikeSharp C# code that drives them.

## 0. How to read this

| Term | Meaning |
| --- | --- |
| **MUST** / **NEVER** | Mandatory. Deviation only with a written reason in the PR/commit. |
| **SHOULD** | Default. Deviation allowed with a reason in a code comment. |
| **MAY** | Option. |
| ✅ Proven | Used in production server code. |
| ⚠️ Unverified | Offered by the engine, never exercised in game. Test before relying on it. |

**Precedence on conflict:** (1) engine limits (§1) → (2) the contract of a layout that already shipped (ids, variables, classes) → (3) this ruleset → (4) taste.
Existing layouts keep their names. The naming rules in §6 apply to **new** layouts.

Nothing is "done" without an in-game test. If no test server is available, the PR MUST say so.

---

## 1. Runtime model and limits

`custom_hud_layout` is a server entity. The client loads the referenced layout from an addon the server mounts (Workshop addon). State lives in networked vectors on the entity: name tables `PanelIds`, `ClassNames`, `DialogVariableNames`; per player `PlayerLayoutStates[slot]` with `HasClasses`, `DialogVariableStrings`, `InputCaptureEnabled`; plus `GlobalLayoutState` for everyone.

### 1.1 What the server can do

| Channel | API (`CounterStrikeSharp.API.Modules.Extensions`) | Effect |
| --- | --- | --- |
| Text | `hud.SetDialogVariableStringForPlayer(player, panelId, name, value)` | fills `{s:name}` in labels below `panelId` |
| Text (all) | `hud.SetDialogVariableString(panelId, name, value)` | same for every viewer |
| Class | `hud.SetHasClassForPlayer(player, panelId, cls, on)` | adds/removes a class on the panel with that `id` |
| Class (all) | `hud.SetHasClass(panelId, cls, on)` | same for every viewer |
| Mouse | `hud.SetInputCaptureEnabled(player, on)` / `IsInputCaptureEnabled` | cursor and clicks go to the layout |
| Click | `Listeners.OnCustomHudClicked(player, layout, buttonId)` | `id` of the clicked `<Button>` |
| Visibility | `Listeners.CheckTransmit` → `info.TransmitEntities.Add/Remove(index)` | send the entity only to its viewers |
| Keys | `player.Buttons` (`PlayerButtons`) per tick | movement keys for keyboard menus |

### 1.2 What the server cannot do – and the replacement

| Impossible | Replacement (MUST) |
| --- | --- |
| create/delete panels | fixed **pool** of N slots in XML, shown/hidden by class |
| send a colour, size, position or style value | **class palette** (`tone-*`, `theme-*`, `pos-*`, `size-*`) |
| set a width or fill level | **class ladder** `p0…p100` with `clip: rect()` |
| set an image path | **icon class** per image (`ico-ak47` → `background-image`) |
| per-frame countdown | **client-side transition** (`span-N` + `is-running`), one write |
| text input, keyboard events in Panorama | chat command (search: `!cmd <text>`, §8); keys via `player.Buttons` |
| JavaScript, `onactivate`, Panorama events | none; clicks only via `<Button id>` + input capture |
| read/set scroll position | a client-scrolled list (§2) when the position does not matter, otherwise pagination |

### 1.3 Engine behaviour the code MUST handle

- **Round restart:** the client rebuilds the layout from XML (defaults); the server state stays. → The server MUST write the XML defaults back (§9, `RestoreDefaults`). XML default and C# default MUST be identical.
- **New entity** (map change, kill, loss) starts at the XML defaults for everyone → drop all server caches.
- **Transmit:** without a filter every client receives the entity; per-player state only overlays it. → Filter in `CheckTransmit`: send only to players whose root is visible or fading out.
- **Burst limit:** many writes in one tick reach the client as one packet; too many → overflow kick (`NETWORK_DISCONNECT_OVERFLOW`). ✅ Proven.
- **Not every overflow kick is a write burst:** the same `NETWORK_DISCONNECT_OVERFLOW` also follows `ProcessMessages has exhausted its CPU time budget` – the server spent too long on one client message (seen at `SIGNONSTATE_FULL`, ~400 ms, on the first connect after a plugin hot reload; the next connect took 24 ms). → Read the line before the disconnect in the server log before cutting writes. The CPU case was a one-off cold start; the cause inside the plugins is not known. Observed once.
- **Spawn/open inside a client command:** spawning the entity (~200 ms) or a large first write in the same tick as the chat command can drop the client. → Opening MUST be decoupled by a timer (≥ 0.2 s); pre-warm the entity at map start. ✅ Proven.
- **Name budget:** `DialogVariableNames` holds at most **1244** names per layout. Class and panel names are interned too → keep names few, ladders coarse (§10).
- **Draw order:** the layout always draws above the base HUD. The client sets root classes `HUD_BUYMENU_VISIBLE` and `HUD_SCOREBOARD_VISIBLE`. ⚠️ Unverified.
- **Spectators:** keyvalue `observable` (default `false`) controls whether spectators see the HUD of the spectated player. ⚠️ Unverified.
- **Threads:** all Panorama writes ONLY on the game thread. After `await` → `Server.NextFrame`.
- **Timers:** NEVER `Kill()` a one-shot timer from inside another one-shot timer's callback (the native list is erased by index → crash). Cancel by token instead. ✅ Proven.
- **Stale addon wins:** a mounted Workshop VPK that contains the same layout path takes precedence over loose or manually deployed files – on the server and on the client. The new `.vxml_c` is then present on disk but never loaded. Symptom: `[custom_hud] Unable to find panel with id '<id>'` for every new id while the old ids keep working. → Before testing a layout change, unmount/remove older VPKs of the addon on server **and** client, then restart both. ✅ Proven.
- **A running client keeps loaded layouts and stylesheets:** a loose-file deploy replaces the compiled `.vxml_c` / `.vcss_c`, but a client that already loaded that path keeps the old version in memory – `retry` / reconnect does not reload it. Symptom: new ids missing (see below) or old CSS on a new layout. → Restart the client, or load a new path (`_v<N>` bump, §3), which takes effect at the next connect. ✅ Proven. A layout path the running client has never loaded is picked up without a restart (files deployed while the game ran, menu opened afterwards). ✅ Proven.
- **The client localizes dialog variables that equal one of its tokens:** a value such as `CT`, `Counter-Terrorists` or `Terrorists` is shown translated into the *client* language (`AT-…`, `ANTITERROREINHEIT`, `TERRORISTEN` in a German client), not as sent. → Wrap such values in non-breaking spaces (`"\u00a0" + text + "\u00a0"`); it looks the same and no longer matches. ✅ Proven. Common UI words are tokens too: an English `Cancel` showed up as `ABBRECHEN`. Language-neutral codes (`T`, `CT`) MAY instead be literals in the XML (`text="CT"` is not localized) ✅ Proven; for words, pick a text that is no token (`Cancel ban`) or wrap it.
- **Reading "Unable to find panel with id":** the log lists only ids that actually received a write, so the pattern tells which writes ran (e.g. only rows whose class differs from the XML default). A missing root id together with working sibling roots means "old layout loaded", not "layout broken" – a broken layout loses every id. ✅ Proven.
- **Global vs per-player state:** `SetDialogVariableString` / `SetHasClass` without a player write `GlobalLayoutState` for everyone; per-player writes overlay it. Use global writes for content that is identical for all viewers (e.g. filler text) – one write instead of one per player. A variable is either global or per player, never both (§4.2). ✅ Proven (global text reaches every client); mixing global and per-player variables on one panel ⚠️ Unverified.
- **Same-tick changes coalesce:** writing a default and then a new value in the same tick sends only the net change. After a round restart, write the defaults in one tick and the new values in a later tick, otherwise a value equal to the previous round's never reaches the rebuilt client panel. ⚠️ Unverified (follows from the round-restart rule).

---

## 2. Proven building blocks

| Block | Implementation |
| --- | --- |
| Elements | `Panel`, `Label`, `Button` ✅; `Image` ⚠️ Unverified |
| Image | `Panel` + `background-image: url("s2r://panorama/images/…_png.vtex")` or `.vsvg` |
| Card show/hide | root classes `is-off` (collapse) + `is-fading` (opacity), two phases |
| Progress bar | track `overflow: clip` + fill `clip: rect(0%, N%, 100%, 0%)` per class |
| Time bar | duration class sets `transition-duration`, start class sets the target `clip` |
| List | fixed pool, pagination `‹ 1 / 3 ›` |
| Scroll list | panel with `flow-children: down` + `overflow: squish scroll` holding a fixed row pool; rows get a fixed width a bit below the panel width (room for the bar). The client scrolls, the server never knows the position. Scroll bar drag under input capture ✅ Proven; mouse wheel ⚠️ Unverified |
| Tabs/filters | buttons with exclusive `is-active` |
| View switch | all views `visibility: collapse` by default, one class group on the root (`view-a`, `view-b` …) and `.XxCard.view-a .XxViewA { visibility: visible; }` per view; the views share one `flow-children: none` area. ✅ Proven (5 views) |
| Sub-pages in a card | the same on a nested panel (`page-*` group on the card); a picker (`is-pick`) replaces tabs and pages by collapsing both. ✅ Proven |
| Switch | track panel + knob `transform: translateX()` under `is-on` |
| Dropdown | choice pool per row (≤ 6–12), `is-open` on the row |
| Stepper | `‹ value ›` with two buttons; each click applies at once, the buttons get `is-disabled` at the limits. ✅ Proven |
| Toast | pool per placement, slide/fade in, remaining-time bar |
| Close X | two 2px bars, `rotateZ(±45deg)` |
| Keyboard focus | `is-focus` on the entry, W/S/A/D via `player.Buttons` |
| Replay animation | two classes with the same keyframes, alternated |
| Themes | `theme-<name>` on the root, generated from one palette list |
| Hover/click sound | `sound: "UIPanorama.generic_button_rollover"` / `…_press` |
| Background blur | `world-blur: gaussian( 2 )` |

---

## 3. Files, paths, versions

```
<addon>/panorama/layout/custom_game/<plugin>/<name>_v<N>.xml
<addon>/panorama/styles/custom_game/<plugin>/<name>_v<N>.vcss
```

- Server path: `panorama/layout/custom_game/<plugin>/<name>_v<N>.vxml_c` (compiled). One constant in C#.
- Include stock `s2r://panorama/styles/csgostyles.vcss` first (font classes), then the own sheet. One path form per project (`.vcss` or `.vcss_c`; both work).
- `_v<N>` MUST be bumped when ids, variable names, classes or structure change incompatibly. Old versions stay in the addon until no server loads them.
- One layout per **visibility group**: read-only overlays (tracker, toast) and interactive menus are separate layouts (own transmit, own input capture, own name budget).
- Layout change = republish the addon. C# changes without layout changes do not need that.
- Repeated pool markup SHOULD be generated by a script, not copied by hand.

---

## 4. Layout XML

### 4.1 Skeleton

```xml
<root>
	<styles>
		<include src="s2r://panorama/styles/csgostyles.vcss" />
		<include src="s2r://panorama/styles/custom_game/<plugin>/<name>_v1.vcss" />
	</styles>

	<!-- <plugin>:<component> v1 rows=4 -->
	<Panel class="XxScreen" hittest="false">
		<Panel id="xx-root" class="XxCard is-off theme-gold" hittest="false">
			…
		</Panel>
	</Panel>
</root>
```

Full example: [`examples/panorama/layout/custom_game/example/quest_menu_v1.xml`](examples/panorama/layout/custom_game/example/quest_menu_v1.xml).

### 4.2 Rules

- Outermost panel: anonymous, `width/height: 100%`, `z-index: 99999`. The loader owns the root above it.
- Exactly one root panel with an `id` per layout. It carries visibility (`is-off`, `is-fading`), `theme-*`, and is the default scope for dialog variables.
- `hittest="false"` on **every** `Panel` and `Label`. `Button` without a `hittest` attribute. Read-only layouts contain no buttons.
- `id` ONLY on elements the server writes to or that deliver clicks. Everything else gets `class` only.
- Every pool slot is authored in its **default state** (`is-off`, `p0`, `tone-neutral` …). Default = most frequent state = C# default.
- Text ONLY via `text="{s:var}"`. Literals allowed for language-neutral glyphs (`‹ › ✓ ×`).
- Every visible text slot MUST be written before the first show (display of unset variables is unspecified).
- Contract comment below `<styles>`: component, version, pool sizes. Pool sizes = C# constants (validator checks).
- Variable scope: default = variable on the root with a unique name (`qm_r0_name`). MAY: short names on the row panel (`label` on `item-3`) when the name budget gets tight. Both ✅ Proven. A variable is either per player or global – NEVER both.

---

## 5. Panorama CSS: the allowed subset

Unknown or wrong CSS is **dropped without a log** (sometimes the whole block; a keyframe error drops the whole sheet). Use only the following.

### 5.1 Allowed

| Area | Syntax |
| --- | --- |
| Flow | `flow-children: down \| right \| none` (default `none` = stacked) |
| Size | `width/height: <px> \| <%> \| fit-children \| fill-parent-flow( 1.0 )` |
| Alignment | `horizontal-align: left \| center \| right`; `vertical-align: top \| middle \| bottom` |
| Spacing | `margin`, `padding` (single sides or 4-value shorthand in `px`) |
| Colour | hex only: `#rrggbb`, `#rrggbbaa` |
| Background | `background-color: #… \| gradient( linear, 0% 0%, 0% 100%, from( #… ), to( #… ) ) \| none`; `background-image: url("s2r://…")`; `background-size: contains \| <px> <px>`; `background-repeat`; `background-img-opacity` |
| Border | `border: 1px solid #…`; `border-color`; `border-radius: <px>`; `border-bottom` etc. |
| Shadow | `box-shadow: #rrggbbaa <x>px <y>px <blur>px <spread>px` (colour first) |
| Text | `font-size: 14` (**unitless**); `font-family: Stratum2 Bold` or classes `stratum-bold-tf` / `stratum-medium-tf`; `color`; `letter-spacing: <px>`; `text-transform: uppercase`; `text-align: left \| center \| right`; `text-overflow: ellipsis` + `white-space: nowrap` (needs a fixed width) |
| Visibility | `visibility: collapse \| visible`; `opacity` |
| Clipping | `overflow: noclip \| clip \| scroll` (per axis: `noclip noclip`); `overflow: squish scroll` for a scroll list (§2); `clip: rect( t, r, b, l )` |
| Transform | `transform: translateX/Y( px ) \| scale3d( x, y, z ) \| rotateZ( deg )`; `brightness` |
| Transition | `transition-property: a, b`; `transition-duration: 0.25s`; `transition-timing-function: ease-out \| ease-in \| linear \| ease-in-out` (separate properties, no shorthand) |
| Animation | `@keyframes 'name' { … }` (**name in quotes**); `animation-name/-duration/-delay/-timing-function/-iteration-count/-fill-mode` |
| Effects | `world-blur: gaussian( n )`; `wash-color: #… \| none` |
| Interaction | `cursor: pointer \| default`; `sound: "UIPanorama.generic_button_rollover"` |
| Pseudo | `:hover`, `:active` (only with input capture); `:last-child` |
| Layering | `z-index` (among siblings only) |

### 5.2 Forbidden (dropped or colliding)

`display`, flexbox, grid, `calc()`, `var()`, `@define`, `@media`, `rgb()/rgba()/hsl()`, `::before/::after`, `background-size: contain`, `visibility: hidden`, `overflow: hidden`, `font-size` with a unit, `vertical-align: center`, `background-blur`, `transition` shorthand, width transitions for bars, `:selected`, `:disabled`, `:focus`, class name `hidden`/`Hidden` (collides with `csgostyles`).

Avoid (not proven, may be dropped silently – use the replacement):

| Avoid | Instead |
| --- | --- |
| negative `margin` ⚠️ | position with alignment, or flip a box with `transform: rotateZ( 180deg )` |
| `box-shadow` (or other unlisted properties) in `transition-property` ⚠️ | transition only `opacity`, `transform`, `clip`, `background-color`, `brightness`; switch the shadow without a transition |
| relying on `animation-fill-mode: forwards` for the end state ⚠️ | make the resting style under the trigger class equal the last keyframe (§12.5) |

### 5.3 Layout traps

- `%` sizes need a sized parent.
- Padding does **not** shrink `100%` children (border box). Inset full-size children with spacer panels.
- Centered text: label with a fixed width + `text-align: center`, or `horizontal-align: center` + `fit-children`.
- Button label: button `flow-children: none` + fixed size; label `width: 100%`, `vertical-align: middle`, **no** `height: 100%`.
- `fill-parent-flow` for height only inside `down` parents, for width only inside `right` parents.
- Background panel (`width/height: 100%`) as the first child; content as a sibling on top (`flow-children: none` on the parent). Only for a parent with a fixed size: a card with `height: fit-children` gets `background-color` / `border` on itself instead (a 100% child of a `fit-children` parent has nothing to resolve against). ✅ Proven (cards on the card itself).
- `Button`: set `wash-color: none`, otherwise the default style tints it.
- Restarting an animation: alternate between two class names that use the same keyframes (§12.2). If the class sits on an ancestor (e.g. the root) and not on the animated panel, give the two classes two identically defined keyframes with **different names** (`xx-a` / `xx-b`) – a new `animation-name` always restarts.
- ⚠️ **Gap after a row of buttons:** a `Label` that follows a `flow-children: right` row of fixed-size `Button`s inside a `down` column was drawn far below the row in game – also with an explicit row height. Cause unknown. → Put the label above the row (or test the spacing in game). Counter-example ✅ Proven: rows with `height: fit-children` (buttons 36px + `margin-bottom: 6px`) in a `down` column with a fixed width, followed by a hint label, rendered without a gap (cs2-mini-admin server cards).
- **`fit-children` is capped by a smaller parent.** Content that is larger than its clipping window (scrolling strip, reel, marquee) MUST get an explicit size (e.g. `rows × row height`) and `overflow: noclip` on itself; only the window clips (`overflow: clip clip`). With `fit-children` the content is only as tall as the window, the rest is never laid out and scrolls in empty. ✅ Symptom proven in game; explicit size as the fix ⚠️ retest pending.

---

## 6. Naming (new layouts)

| What | Form | Example |
| --- | --- | --- |
| Prefix | 2–3 letters per layout | `qm` |
| Panel id | `<pfx>-<part>[-<n>[-<m>]]`, kebab-case | `qm-row-2`, `qm-fill-2` |
| Dialog variable | `<pfx>_<part>[<n>][_<field>]`, snake_case | `qm_r2_name`, `qm_page` |
| Part class | `<Pfx><Part>`, PascalCase | `QmRow`, `QmTabLabel` |
| State | `is-<state>` | see below |
| Variant (one of N) | `<group>-<value>` | `tone-warn`, `theme-gold`, `pos-right`, `kind-toggle`, `ico-ak47` |
| Ladder | `p<0…100>` | `p40` |
| Client time | `span-<seconds>` + `is-running` | `span-15 is-running` |
| Replay | `replay-0` / `replay-1` | |

State vocabulary (closed list; add new states here first):

| Class | Meaning | CSS effect |
| --- | --- | --- |
| `is-off` | not present | `visibility: collapse` |
| `is-fading` | fading in/out | `opacity: 0` (+ transform) |
| `is-active` | selected in a group (tab, filter) | accent border + tint |
| `is-disabled` | not usable | `opacity: 0.35`, no hover |
| `is-on` | switch on | knob right, accent track |
| `is-open` | dropdown/detail open | child panel visible |
| `is-focus` | keyboard cursor | 1px accent border |
| `is-done` | completed | check mark, muted text |
| `is-self` | own entry (leaderboard) | accent tint |
| `is-urgent` | last seconds | danger colour, pulse |
| `is-running` | client-timed transition runs | target `clip` |

---

## 7. Visual design

Goal: functional, calm, readable over a moving 3D scene. No decoration without function; no glitch, neon or 3D effects.

### 7.1 Colour tokens (paste hex; source of truth = the sheet's header comment)

| Token | Value | Use |
| --- | --- | --- |
| `surface` | gradient `#12171ee8` → `#0b0f14f2` | card background |
| `surface-raised` | `#ffffff0d` | rows, buttons at rest |
| `surface-hover` | `#ffffff1c` | hover |
| `surface-press` | `#ffffff2e` | active |
| `track` | `#ffffff12` | bar background |
| `border` | `#ffffff14` | borders, dividers |
| `ink` | `#f5f7fa` | titles, primary text |
| `ink-soft` | `#e8eef4` | labels |
| `muted` | `#a3acb6` | secondary text |
| `dim` | `#6b737c` | decoration/inactive only (contrast ≥ 3:1) |
| `accent` | theme, default `#f0a531` (light `#ffd27a`) | active state, bars, accent bar |
| `info` | `#5aa9e6` | hint |
| `success` | `#9ccf4f` | done, positive |
| `warn` | `#ff8a3d` | warning |
| `danger` | `#e05a5a` | error, destructive, `is-urgent` |
| `ct` / `t` | `#96c8fa` / `#eabe54` | team colours |

- Text contrast ≥ 4.5:1 against `surface`; every token except `dim` meets it. The background behind text MUST have alpha ≥ `e0`.
- Colour is NEVER the only carrier of meaning: add text, a check mark or a shape.
- Accent tint = accent + alpha: rest `22`, hover `44`, border solid. Hover border without active state: accent `66`.
- Themes (`theme-*`) override accent rules only; generated from one list (name, accent, gradient).

### 7.2 Typography

| Role | Size | Style |
| --- | --- | --- |
| Hero number (announcement, win) | 32–48 | `stratum-bold-tf` |
| Card title | 15–18 | `stratum-bold-tf`, `uppercase`, `letter-spacing: 1.6px` |
| Row title, buttons | 14–16 | `stratum-bold-tf`, `uppercase`, `letter-spacing: 1px` |
| Body, values | 14 | `stratum-medium-tf` |

Minimum **14**. Number columns right-aligned with a fixed width. Truncation: fixed width + `text-overflow: ellipsis`; cut very long strings on the server to the slot maximum.

### 7.3 Dimensions

- Spacing (`margin`, `padding`) on a 4px grid: `4 · 8 · 12 · 16 · 24 · 32`. Sizes, radii (3px) and lines (1–3px) are free.
- Click target ≥ 28×28px (mouse). Standard button 28–32px high, primary 40px. Row ≥ 36px.
- Screen inset 68px at the top (round status), ≥ 10px elsewhere.
- Read-only overlay ≤ 420px wide, at the right or left edge; do not permanently cover the kill feed (top right) or the radar (top left). NEVER permanently occupy the screen centre (crosshair).
- Modal menu 560–960px wide and centred, or full screen with a 68px inset.

### 7.4 Motion

| Token | Duration | Curve | Use |
| --- | --- | --- | --- |
| `fast` | 0.15s | ease-out | hover, press (`background-color`, `border`) |
| `enter` | 0.25s | ease-out | card/toast in (`opacity`, `transform` ≤ 8–16px) |
| `exit` | 0.18s | ease-in | out (≤ `enter`) |
| `fill` | 0.35s | ease-out | bar change (`clip`) |
| `slow` | 1.0s | ease-out | passive overlays in/out |
| `drain` | `span-N` | linear | time bars |

Server fade grace before `is-off` ≥ the longest exit duration. Animate only to express a state change. Infinite animation only for `is-urgent`, at most one element.

### 7.5 Interaction

- Every button: rest / `:hover` / `:active` / `is-disabled` visibly distinct; hover sound + press sound.
- `is-disabled`: `opacity: 0.35`, hover rule neutralised, `cursor: default`, `sound: ""`; ignore the click on the server.
- Feedback in the same tick: the click handler repaints immediately (diffs only).
- One primary action per view. Destructive: `danger`, spatially separated, confirmed (§8).
- Close always via the close button **and** the same chat command; menus close on round start, death and disconnect. Exception with a reason: a tool menu (admin) MAY stay open on death and come back after the round start (§8 "Persistent menu").
- Empty state instead of an empty area: one line of text (`qm-empty`).
- State survives reopening (tab); the page starts at 1.

---

## 8. Patterns per use case

| Case | Pattern | Rules |
| --- | --- | --- |
| **List** | pool of N rows + pager `‹ i / n ›` | N fixed (4–8); rows of equal height; unused slots `is-off`; pager buttons `is-disabled` at the ends; empty-state label |
| **Long list** (catalogue, all players) | one scroll list (§2) over a pool of up to ~64 single-line rows | pool ≥ the largest realistic count, rest `is-off`; paint under a write budget (§10); filters/sort narrow the same pool; the scroll position stays when the content changes – keep the list short enough to reread |
| **Search** | chat command `!cmd <text>`; the menu shows the active search as an accent chip with a clear button | search before filters (the filter counts follow it); empty result has its own empty text; clear resets only the search |
| **Two columns** (teams) | two scroll lists side by side, each its own row pool and head (name + count, team colour) | split on the server; team colour plus text, never colour alone; players without a column are left out deliberately |
| **Tabs / filters / button group** | `flow-children: right`, buttons with exclusive `is-active` | ≤ 5 entries; counts in the label (`OPEN 3`); switching resets to page 1 |
| **Switch** | row button + track/knob, `is-on` | label names the thing, not the state; knob `translateX`, `fast` |
| **Choice** | stepper `‹ value ›` | preferred; dropdown (choice pool, `is-open`) only for ≤ 6 values, otherwise a sub-menu |
| **Progress** | track + fill ladder | 10% steps for progress, 1% only for time bars; value also as text |
| **Countdown** | `span-N` + `is-running` on the fill; text at most once per second | set `span-N` first, `is-running` in the next write; reset: remove both, default `transition-duration: 0s` |
| **Toast** | pool per placement (depth 3–4), `tone-*` | 3–6 s; ≥ 0.1 s between two toasts to the same player; never input capture |
| **Modal menu** | full screen or centred, title bar + close X, input capture | open via timer (§1.3); capture off on close; one menu per player at a time |
| **Read-only overlay** | card at the edge, everything `hittest="false"` | never input capture; visible only when needed (transmit); `slow` fade |
| **Leaderboard/table** | row pool, fixed column widths, `is-self` | numbers right-aligned; own row always visible (own slot at top/bottom) |
| **Confirmation** | same button twice: label changes to "SURE?", `tone-danger`, 3 s timeout | no separate dialog needed; two labels in the button (normal + `{s:xx_sure}`), `is-confirm` swaps them; any other click disarms; expiry from the `OnTick` scheduler (one comparison while nothing is armed). ✅ Proven |
| **Picker as confirmation** (ban with duration + reason) | the action button opens a picker in place of the page; choosing the last part runs it | the picker is the confirmation, no "SURE?" on top; a cancel button; the picker closes on tab/target change. ✅ Proven |
| **Persistent menu** (admin tools) | stays open on death; on `EventRoundStart` close + `RestoreDefaults` as required, then reopen through the normal open timer | tab, sub-page and selection survive (per-slot state), pages start at 1; ✅ Proven |
| **Keyboard menu** | no capture; W/S moves `is-focus`, D/E selects, A goes back | rising-edge detection; poll only players with an open menu |
| **Icons** | `Panel` + `ico-<name>` class → `background-image` | stock images (`s2r://panorama/images/…`) or images compiled into the addon |
| **Player-chosen position** | `pos-left/center/right` on the root | class group, not coordinates |
| **Reel / scrolling strip** (slot machine, roulette) | fixed row pool in a clip window; the result always sits in the same row → one keyframe distance (§12.5) | explicit strip height; filler rows global, written once per round; per viewer only the result row + root classes; restart via `replay-0/1` with `-a/-b` keyframes; resting transform = end position; sounds on the server follow the same easing (OnTick scheduler). ✅ Proven in game (spin, result row, a new spin per roll); tick sound timing ⚠️ not measured |

---

## 9. Server architecture (C#)

Three layers; each knows only the one below:

| Layer | Job | Knows ids? |
| --- | --- | --- |
| **HudLayout** (generic) | entity spawn/adopt, diff caches, visibility, transmit, defaults | only the strings it is given |
| **View** (per layout) | model → slots, pagination, dirty flag | yes, through one `Ids` class |
| **Session/controller** | open/close, input capture, click routing, timers | only button id constants |

Lifecycle (MUST):

| Event | Action |
| --- | --- |
| `OnMapStart` | drop caches (`ResetForMap`), cache the tick rate, pre-warm the entity on a timer |
| chat command "open" | bump token, timer 0.2 s → `Open` → input capture on |
| click | check the layout by handle → close or view handler → repaint immediately |
| model change | only `MarkDirty(slot)`; the scheduler paints staggered |
| `EventRoundStart` | close menus, `RestoreDefaults` for all humans |
| `OnClientDisconnect` | `Forget(slot)` in layout, session and key cache |
| `Unload` | `Kill` the entity, clear all caches |

NEVER kill and respawn the layout entity mid-map to "reset" it.

### 9.1 Code rules

- Ids, variable names and classes are spelled in **exactly one** place (`static class <Layout>Ids`); indexed ids as prebuilt arrays (`RowIds[i]`), never string interpolation while painting.
- Bindings (text/flag/group/ladder) are registered at load; paint code works with handles.
- Early exit first: `player is not { IsValid: true }` → visibility → dirty → work.
- Per-slot state in arrays `[MaxSlots = 65]`, no `Dictionary<int,…>` on hot paths.
- No LINQ, `yield`, closures or string formatting in tick/transmit handlers; allowed in a dirty paint when it runs only there.
- Foreign callbacks (`OnButton`, `OnClosed`) in `try/catch`; log, never let them throw.
- Click routing for pools: while painting, store per viewer what each pool slot shows (`_rowUser[slot, row] = player.UserId`, map entry, SteamID) and resolve the click through that table – the click then hits what the viewer saw, even if the list changed since. Store a user id or SteamID, never the player slot (a slot is reused by the next player); re-check validity and permission on click. ✅ Proven.
- Per-viewer texts: CounterStrikeSharp's `Localizer.ForPlayer` / `GetLanguage` **throw for bots and unauthorized players (SteamID 0)**. Wrap them (`ForPlayerSafe`: SteamID 0 → server language). An exception in the middle of game logic aborts it halfway (e.g. a dice applied but never recorded). ✅ Proven.
- Timers: cancel through a token compared in the callback (§1.3).
- Formatting, `var` vs explicit types, namespace form: the host repository's style (`.editorconfig`, neighbouring code) wins over the style of the examples.
- A validator script (XML/CSS against C# constants, forbidden CSS, name budget) runs before "done". If there is none: create it.
- Generator `--check` and validators compare generated files **ignoring line endings** (`text.replace("\r\n", "\n")`): a Windows checkout with `core.autocrlf` turns the committed LF file into CRLF and every check would report "out of date". ✅ Proven. Strip CSS comments before scanning for forbidden CSS (a header comment that lists `@define` as forbidden matches otherwise).

### 9.2 XML and CSS format

- XML: tabs, one element per line, attribute order `id` → `class` → `hittest` → `text`.
- CSS: header comment (layout, contract file, palette). Per block: size/flow/alignment → box (margin, padding, border) → colour/text → effects → transition. Lower-case hex. One selector per block; short rules (`.theme-x .Y { … }`) may be single-line.
- Base rule before state rules, states before themes.

---

## 10. Performance and memory

**Cost model:** every `Set*` call = native call + change of the networked player state → delta to the client. Every new (panel, class) / (panel, variable) pair grows that state. A player who does not receive the entity gets the full state on the next transmit.

| Rule | Implementation |
| --- | --- |
| Send diffs only | per-slot diff cache that starts at the XML default → writing the default is free |
| Default = most frequent state | the first open sends only deviations |
| No idle writes | dirty flag per slot; the painter exits when invisible or clean |
| Spread the load | refresh budget ~128 players/s server-wide (= 2 per tick at 64 tick), at least 5 ticks between repaints of one player, roster once per second |
| Move time to the client | bars via `span-N` + `is-running` (1 write instead of N); countdown text ≤ once per second and only on change |
| Coarse ladders | 10% steps (11 classes) for progress; 1% (101) only for visible time bars |
| Same for everyone → global | `SetDialogVariableString`/`SetHasClass` once instead of per player |
| Do not send the invisible | `CheckTransmit`: entity only to players with a visible/fading root; never write to bots/HLTV |
| Do not clear hidden rows | set `is-off`, leave the old text |
| Reuse strings | number cache (`NumberText`), static labels once per open; format only on change |
| Avoid bursts | open via timer; space toasts; pre-warm the entity at map start |
| Write budget for big paints | count native writes (`HudLayout.Writes`); a paint stops after ~64–72 writes and stays dirty, the next ticks finish it – rows already sent cost nothing then. The round-start `RestoreDefaults` takes a budget per tick the same way. A 48-row list arrives over a few ticks without an overflow ✅ Proven |
| Name budget | ≤ 1244 variables per layout; pools as small as needed; when tight use row scope (§4.2) or a second layout |
| Free memory | `Forget(slot)` on disconnect; drop caches on map change/entity loss |
| Measure | timers around the `OnTick` and `CheckTransmit` handlers; target: idle ≈ one comparison |
| Many timed steps | one `OnTick` scheduler with per-slot timestamps (`Server.CurrentTime`) instead of one timer per step; exit with one comparison while nothing is active |
| Same content for everyone | global writes (§1.3) for filler/static text; per player only what differs |

---

## 11. Data transfer – reference implementation

All code lives in [`examples/`](examples/) and builds against CounterStrikeSharp.API 1.0.376 with warnings as errors. It is not tested in game. New layouts reuse [`HudLayout`](examples/src/Hud/HudLayout.cs) unchanged; only the view and the ids class are layout-specific.

| File | Content |
| --- | --- |
| [`Hud/HudLayout.cs`](examples/src/Hud/HudLayout.cs) | entity spawn/adopt, per-slot diff caches, flags/groups/ladders, shared text, show/hide with fade grace, round-restart defaults (optionally under a write budget), write counter, transmit filter |
| [`Hud/NumberText.cs`](examples/src/Hud/NumberText.cs) | allocation-free number strings |
| [`Hud/KeyNav.cs`](examples/src/Hud/KeyNav.cs) | keyboard navigation without input capture |
| [`QuestMenu/QuestMenuIds.cs`](examples/src/QuestMenu/QuestMenuIds.cs) | the layout contract in C# |
| [`QuestMenu/QuestMenuView.cs`](examples/src/QuestMenu/QuestMenuView.cs) | paginated list with tabs, dirty painting |
| [`QuestMenu/MenuSessions.cs`](examples/src/QuestMenu/MenuSessions.cs) | delayed open, input capture, click routing, token cancel |
| [`ExamplePlugin.cs`](examples/src/ExamplePlugin.cs) | lifecycle wiring, staggered refresh budget |

### 11.1 Contract registration (once, at load)

```csharp
_hud = new HudLayout(QuestMenuIds.Resource, QuestMenuIds.Root);   // root gets is-off / is-fading
_title = _hud.Text(QuestMenuIds.Root, "qm_title");
_prevDisabled = _hud.Flag(QuestMenuIds.Prev, "is-disabled", authoredOn: true);
_rowTone[r] = _hud.Group(QuestMenuIds.RowIds[r], QuestMenuIds.Tones, authored: 0);
_rowFill[r] = _hud.Ladder(QuestMenuIds.FillIds[r], "p", step: 10);
```

The `authored*` arguments MUST match the XML. They seed every slot's cache, so writing a default costs nothing.

### 11.2 Diff write (core of every class change)

```csharp
short prev = state[slot.Index];
if (prev == next)
{
    return;                                                  // unchanged: no native call, no network
}

ClassBinding b = _classes[slot.Index];
if (prev >= 0)
{
    hud.SetHasClassForPlayer(player, b.PanelId, b.Classes[prev], false);
}

if (next >= 0)
{
    hud.SetHasClassForPlayer(player, b.PanelId, b.Classes[next], true);
}

state[slot.Index] = next;
```

A flag is a group of one class, a ladder is a group of `p0…p100`: one code path for all three.

### 11.3 Paint (only when visible and dirty)

```csharp
public void PaintIfDirty(CCSPlayerController player)
{
    int slot = player.Slot;
    if (!_hud.IsVisible(slot) || !_dirty[slot])
    {
        return;
    }

    Paint(player);   // writes every slot; the caches drop everything unchanged
}
```

### 11.4 Show and hide in two phases

```csharp
// show: collapse off + faded in the same write, fade removed one frame later
SetFlag(player, _rootFading, true);
SetFlag(player, _rootOff, false);
Server.NextFrame(() => { /* still visible? */ SetFlag(live, _rootFading, false); });

// hide: fade now, collapse after FadeSeconds (Tick checks one timestamp while nothing is due)
SetFlag(player, _rootFading, true);
_closingUntil[slot] = Environment.TickCount64 + (long)(FadeSeconds * 1000);
```

### 11.5 Transmit filter

```csharp
(CCheckTransmitInfo info, CCSPlayerController? viewer) = infoList[v];
if (_visible[slot] || _closingUntil[slot] != 0)
{
    info.TransmitEntities.Add(_entityIndex);
}
else
{
    info.TransmitEntities.Remove(_entityIndex);
}
```

### 11.6 Staggered refresh

```csharp
int perTick = Math.Max(1, RefreshPerSecond / _ticksPerSecond);
int gap = Math.Max(1, (_roster.Count + perTick - 1) / perTick);
for (int i = (int)(tick % (uint)gap); i < _roster.Count; i += gap)
{
    _menu.PaintIfDirty(_roster[i]);
}
```

---

## 12. Snippets

Reference layout and stylesheet: [`quest_menu_v1.xml`](examples/panorama/layout/custom_game/example/quest_menu_v1.xml), [`quest_menu_v1.vcss`](examples/panorama/styles/custom_game/example/quest_menu_v1.vcss).

### 12.1 Client-timed bar (toast, countdown)

```css
.XxDrain
{
	width: 100%;
	height: 100%;
	clip: rect( 0%, 100%, 100%, 0% );
	transition-property: clip;
	transition-timing-function: linear;
	transition-duration: 0s;
}

.XxDrain.span-5       { transition-duration: 5.0s; }
.XxDrain.span-10      { transition-duration: 10.0s; }
.XxDrain.is-running   { clip: rect( 0%, 0%, 100%, 0% ); }
```

Write 1: `span-N`. Write 2 (next tick): `is-running`. The server writes nothing while it drains. Reset: remove both; the 0s default snaps the bar back instead of animating a refill.

### 12.2 Replayable entry animation

```css
@keyframes 'xx-slide-in'
{
	0%   { opacity: 0.0; transform: translateX( 32px ); }
	100% { opacity: 1.0; transform: translateX( 0px ); }
}

.XxEntry.replay-0,
.XxEntry.replay-1
{
	animation-name: xx-slide-in;
	animation-duration: 0.25s;
	animation-timing-function: ease-out;
}
```

Re-adding a class does not restart an animation. Register `replay-0`/`replay-1` as one group and switch to the other one for every new entry.

When the replay class is set on an ancestor (root) instead of the animated panel, use two identical keyframes with different names – a changed `animation-name` always restarts:

```css
.XxRoot.replay-0 .XxStrip { animation-name: xx-spin-a; }
.XxRoot.replay-1 .XxStrip { animation-name: xx-spin-b; }
```

### 12.3 Switch

```css
.XxSwitch      { width: 44px; height: 24px; flow-children: none; background-color: #ffffff1c; border-radius: 12px; transition-property: background-color; transition-duration: 0.15s; }
.XxKnob        { width: 16px; height: 16px; margin: 4px 4px 4px 4px; background-color: #f5f7fa; border-radius: 8px; transform: translateX( 0px ); transition-property: transform; transition-duration: 0.15s; }
.is-on .XxSwitch { background-color: #9ccf4f; }
.is-on .XxKnob   { transform: translateX( 20px ); }
```

### 12.4 Icon by class

```css
.XxIcon            { width: 48px; height: 24px; background-size: contains; background-repeat: no-repeat; }
.XxIcon.ico-ak47   { background-image: url("s2r://panorama/images/econ/weapons/base_weapons/weapon_ak47_png.vtex"); }
.XxIcon.ico-ssg08  { background-image: url("s2r://panorama/images/econ/weapons/base_weapons/weapon_ssg08_png.vtex"); }
```

The icon set is one class group; the server picks the index.

### 12.5 Reel / scrolling strip

A strip of N rows scrolls through a window; the result always lands in row W, so one keyframe distance serves every roll. The server writes the result text into row W, then swaps `replay-0/1` – nothing per frame.

```css
.XxWindow  { width: 328px; height: 132px; overflow: clip clip; }                 /* 3 rows visible */
.XxStrip   { width: 100%; height: 968px; flow-children: down; overflow: noclip noclip; transform: translateY( 0px ); }  /* 22 × 44px, explicit: §5.3 */
.XxRow     { width: 100%; height: 44px; flow-children: right; }

@keyframes 'xx-spin-a' { 0% { transform: translateY( 0px ); } 86% { transform: translateY( -850px ); } 100% { transform: translateY( -836px ); } }
@keyframes 'xx-spin-b' { 0% { transform: translateY( 0px ); } 86% { transform: translateY( -850px ); } 100% { transform: translateY( -836px ); } }

/* resting transform = end position: the result stays even if fill-mode is ignored */
.XxRoot.replay-0 .XxStrip,
.XxRoot.replay-1 .XxStrip { transform: translateY( -836px ); animation-duration: 2.0s; animation-timing-function: ease-out; animation-fill-mode: forwards; }
.XxRoot.replay-0 .XxStrip { animation-name: xx-spin-a; }
.XxRoot.replay-1 .XxStrip { animation-name: xx-spin-b; }
```

Distance = (W − 1) × row height (result in the middle of 3 visible rows), overshoot a few px and spring back. Filler rows are the same for everyone → global writes, once per round. Overlays (shade gradients top/bottom, win line) are later siblings of the strip inside the window. Tick sounds: the server computes when a row boundary passes the win line on the same `ease-out` curve and plays them from an `OnTick` scheduler. Reference: cs2-roll-the-dice [`Reel.cs`](https://github.com/Kandru/cs2-roll-the-dice/blob/main/src/Hud/Reel.cs) and [`hud.vcss`](https://github.com/Kandru/cs2-roll-the-dice/blob/main/workshop/content/panorama/styles/custom_game/rollthedice/hud.vcss). ⚠️ Unverified (in-game test pending).

---

## 13. Definition of done

- [ ] Only elements/CSS from §2/§5; validator green (ids/variables ↔ C#, forbidden CSS, ≤ 1244 variables).
- [ ] XML defaults = C# defaults (`authored*` values); `RestoreDefaults` wired to round start.
- [ ] Every pool slot starts `is-off`; every visible text is written before `Show`.
- [ ] All panels/labels `hittest="false"`; buttons have an `id`, their labels `hittest="false"`.
- [ ] Open decoupled by a timer; input capture off on close, round start and disconnect.
- [ ] Writes only through the diff cache; painters exit early (visible + dirty); refresh staggered.
- [ ] Transmit filter active; bots/HLTV never written to.
- [ ] Time-based displays client-side (`span-N` + `is-running`); ladders coarse.
- [ ] Rest/hover/active/disabled/selected states per button; sounds; contrast ≥ 4.5:1; meaning not by colour alone.
- [ ] Texts from language files; font size ≥ 14; screen centre free.
- [ ] Layout version bumped on contract change; old version kept in the addon.
- [ ] Content larger than its clipping parent has an explicit size (no `fit-children`); no negative margins; transitions only on listed properties (§5.2).
- [ ] Animations do not depend on `animation-fill-mode`; restarts use a changed `animation-name` when the class sits on an ancestor.
- [ ] Before the in-game test: no older Workshop VPK of the addon mounted on server or client; the loaded `.vxml_c` contains the new ids; the client restarted (or the layout path changed) since the last deploy.
- [ ] Values that may equal a client localization token (team names, short codes) are wrapped (§1.3).
- [ ] Per-viewer texts are safe for bots (§9.1); lists larger than ~20 rows paint under a write budget (§10).
- [ ] In-game test done, or "not tested in game" stated in the PR.

Testing tips (local dev server, client started with `-netconport`): send client commands through the netconsole as console commands (`rtd info`) rather than `say !rtd info` – the chat path was unreliable right after a reconnect. To test a chat trigger, quote the text like a real client does: `say "!fswitch Name"`. Unquoted, CounterStrikeSharp drops the first character of the text as if it were the quote, so `say !fswitch Name` ran `!switch` and `say !kick Name` reached nothing ✅ Proven. A command is reachable from the chat only when it is registered as `css_<name>` (`ConsoleCommand("kick")` alone gives no `!kick`; several `ConsoleCommand` attributes per method are allowed). A `retry` often leaves the pause menu open (`gameui_hide`). Before sending mouse input, make sure the game window is in the foreground – the user may be using the desktop.
