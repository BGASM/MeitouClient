# Faithful UI parity

Scope: reproduce original screens and interactions using available game systems.
Missing gameplay systems are tracked independently. Loading a layout alone is
not feature parity. This ledger describes the implementation, not verified claims
about undocumented original behaviour. Original-file facts are in
[MyGUI](formats/mygui.md) and [UI screens](game/ui-screens.md).

## UI implementation and gaps

| Area | Implemented | Remaining UI work |
| --- | --- | --- |
| MyGUI resources | Layout/resource lists, skin atlas pieces including tiled slots, text colours and text client insets, ResourceLayout expansion, ImageTexture and first-frame image sets | Animated image sets, pointer resources, complete font sizing/Unicode, localisation, exact resize rounding |
| Window/input | Original frames, caption, close, pin, drag, modality, tab selection, editable caret/selection, Tab between edit fields, Enter default action, dynamic lists with scrolling and clipping | Resize, clipboard, button focus traversal, list columns, scrollbars, sliders, combo boxes, tooltips, sounds |
| HUD | Clock, money, speed/pause, selection, squad bar portraits (picture, name, selection frame; click selects, double click centres), medic/rescue, order toggles (Block, Hold, Chase, Passive, Ranged, Taunt, Sneak; checked state) and move speed arrows, inventory/stats/map/tech/squad shortcuts; live HP/blood/hunger rows | Jobs and Prospect buttons, the behaviour slider, a move speed caption, squad tabs and portrait scrolling, original medical row order and injury-layer correspondence, town/biome/floor/build information, jobs list; the context menu has only the verbs Meitou can give (see ui-input.md section 7) |
| Inventory | Original equipment/pocket, bag and limb windows; live names/quantities/footprints; pocket/bag drag and Arrange commands | Icons, tooltip details, stacking/splitting, equipment changes and constraints; original bag resizing |
| Stats | Original window/panels; all record stats; selecting a stat shows its current level | Original descriptions, XP/race modifiers, all derived stats, exact stat grouping and row skins |
| Overview | All original tabs; live messages and squad names; installed map image, pan/zoom/centre, player/camera markers and right-click move orders | Original map projection/zoom equivalence, settlement discovery/markers, faction data, squad editing, research/crafting/AI data bindings |
| Escape menu | Original layout, pause/resume, Options via Tab settings, exit confirmation, new game, original named save/load dialogs | Original options window content; menu and dialog keyboard shortcuts beyond defaults |
| Frontend | Original title image/buttons and credits; start selection and Begin using existing spawn backend; save metadata/slot selection/name editing and overwrite confirmation; load confirmation | Splash, advanced option bindings, character creation, loading/progress, continue-slot persistence (currently newest listed slot, disabled if unreadable) |
| Trade | Trader stock in `Kenshi_InventoryTraderWindow` beside the character's window, opened and closed with the trade; drag stock onto the pocket or bag grid to buy, own items onto the stock to sell; purses (and whether it buys stolen goods) in the data panel, prices in captions | Which layout the trader variant loads is **Observed** by name only (the file has the `datapanel`, `backpack_content` and `ArrangeButton` the window needs), price tooltips, stack splitting, the fencing prompt |
| Other screens | XML is readable; confirmation message boxes used by menu flows | Dialogue, building, prospecting, tutorials, import, file dialogs |

## Fidelity audit (2026-10-14)

Faithful mode was checked against the facts in [UI screens](game/ui-screens.md) and
[UI input](game/ui-input.md); no install was at hand, so nothing here is checked
against the running original.

Brought in line with a documented fact:

| What | Now | Source |
| --- | --- | --- |
| HUD money | `c.1,234`, no space after the point | format string `c.{1,num}` (ui-screens.md section 1) |
| Save/load info panel | the five original labels in order: File version, Faction name, Funds, Members size, Location; the extra "Day" line is gone | FUN_14047e390 / FUN_14047e780 (section 3) |
| Continue | hidden, not greyed, when no readable save exists | section 3 |
| Esc on a message box | presses its last button (Return already pressed the first) | ui-input.md section 5 |
| T / Y | open the overview on Research / Craft (M on Map); faction has no default key | events 3..6, ui-input.md sections 4 and 6 |

Engine choices that remain; the original is **Unknown** for each:

- Stats window: skills are grouped by the STATS record's field groups (combat, melee weapons; ranged weapons, athletics; thief; science, trades) across
  `Skills1..4Panel`; which group goes in which panel, the order inside, and the row labels are guesses until the installed layout's captions and the locale msgids are read.
- Message box captions (YES / CANCEL), window titles ("INVENTORY - name"), confirmation texts other than "Are you sure you want to exit?".
- Overview faction name shows "Player"; the original shows the player faction's name.
- The hover label is a plain text box; the original uses the HUD's `TooltipPanel`, whose children are not mapped.
- Medical rows: parts in anatomy order, then blood, then hunger, in `LifeBar1..9`; the colour-layer widget names (`LifeBarNRed`, `...Robot`) come from the panel code's colour words, not the layout.
- Save list rows carry a `[Meitou]` / `[Kenshi]` source tag; the title's version text reads "Meitou - Faithful UI" (the original's is "Kenshi 1.0.68 - x64 (Newland)"), on purpose.
- The sandbox keeps Z X C Y P G; with `--sandbox` the faithful C (stats) and Y (craft) shortcuts give way to it.

`FaithfulInteractionTests.EveryWidgetTheFaithfulModeFillsExistsInTheOriginalLayout` [Slow] binds the HUD and every faithful screen against the installed layouts. It
fails on any text, action, list or progress bound to a widget the layout lacks. It also prints the absent widgets that are only hidden or styled, and the named widgets
that keep their editor caption, which is the list of original text the mode does not fill yet. First run against the install on 2026-10-14: it
found the front end, inventory, bag and limb windows sharing one set of bindings (each now binds only its own widgets) and the trader in the generic window
(now `Kenshi_InventoryTraderWindow`); it passes, with about 50 named widgets still showing their editor caption (mostly button labels such as "ARRANGE").

## Gameplay backends tracked separately

| Backend | State / dependency |
| --- | --- |
| Trading and stealing | Shops, prices, buying, selling and fencing exist ([Trade](simulation.md#trade)); stealing from a conscious owner, building shops and smuggling need implementation |
| Dialogue | No complete conversation/choice/effect backend |
| Research and crafting | No complete research queue, benches or production queue backend |
| Construction/prospecting | No complete placement/construction or prospecting backend |
| Squad editing/faction identity | Selection exists; splitting/merging/dismissal and faction naming commands need implementation |
| Orders/AI policy | Medic/rescue/stop, Block, Passive, Hold, Sneak and the move speed act ([Order toggles](simulation.md#order-toggles)); Chase, Ranged and Taunt are stored only; editable jobs need commands |
| Inventory mutations | Storage and validated move/arrange commands exist; stack/split/equip/transfer commands and render appearance refresh remain |
| Advanced new-game options | Start records/spawning exist; per-session option overrides need integration |
| Character creation | Race/appearance data exist; player editor changes need validated commands |
| Import/reset squad positions | Save loading exists; import/reset rules need a separate backend |

## Validation

`MyGuiScreenTests` exercises template client placement, resize alignment,
immutability, drag capture, editing, close, modal interception, lists, clipping,
tab visibility and cycle rejection. The installed-layout test expands and arranges
all layouts using the configured install; it skips when no install is available.
Render captures are smoke checks. No side-by-side or complete interaction parity
gate has passed yet.
`UiRenderingTests` reads back GPU pixels to check top-left clipping against the
renderer's lower-left targets, with synchronization validation enabled.
`FaithfulInteractionTests` clicks installed title/confirmation/inventory controls:
frontend requests go to the host, and inventory drags only mutate at a simulation
tick. `InventoryCommandTests` checks stale drags, occupied cells, stack limits,
equipment rejection and failed-arrangement rollback. `SaveCatalogTests` checks
source identity, header metadata, and named-slot validation. `UiStateTests` covers
editing, map zoom/pan and runtime visibility overrides; `MyGuiColourTests` covers
skin state text colours and dark parchment list text.

The targeted UI/catalog/inventory tests pass. The installed save-schema suite
also exposes an existing `SaveWriteTests.New_records_have_the_key_sets_of_the_real_ones`
failure for a `PLATOON` `campaign` key in a sampled original save. The UI work
does not change `SaveBuilder` or that schema assertion; the failure is not suppressed.
