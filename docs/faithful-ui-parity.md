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
| HUD | Clock, money, speed/pause, selection, live squad name buttons, medic/rescue, inventory/stats/map/tech/squad shortcuts; live HP/blood/hunger rows | Portraits, original medical row order and injury-layer correspondence, town/biome/floor/build information, jobs list, original context menu |
| Inventory | Original equipment/pocket, bag and limb windows; live names/quantities/footprints; pocket/bag drag and Arrange commands | Icons, tooltip details, stacking/splitting, equipment changes and constraints; original bag resizing |
| Stats | Original window/panels; all record stats; selecting a stat shows its current level | Original descriptions, XP/race modifiers, all derived stats, exact stat grouping and row skins |
| Overview | All original tabs; live messages and squad names; installed map image, pan/zoom/centre, player/camera markers and right-click move orders | Original map projection/zoom equivalence, settlement discovery/markers, faction data, squad editing, research/crafting/AI data bindings |
| Escape menu | Original layout, pause/resume, Options via Tab settings, exit confirmation, new game, original named save/load dialogs | Original options window content; menu and dialog keyboard shortcuts beyond defaults |
| Frontend | Original title image/buttons and credits; start selection and Begin using existing spawn backend; save metadata/slot selection/name editing and overwrite confirmation; load confirmation | Splash, advanced option bindings, character creation, loading/progress, continue-slot persistence (currently newest listed slot, disabled if unreadable) |
| Other screens | XML is readable; confirmation message boxes used by menu flows | Dialogue, trade, building, prospecting, tutorials, import, file dialogs |

## Gameplay backends tracked separately

| Backend | State / dependency |
| --- | --- |
| Trading and stealing | No complete trade transaction/shop/stealing backend; UI must not fake transactions |
| Dialogue | No complete conversation/choice/effect backend |
| Research and crafting | No complete research queue, benches or production queue backend |
| Construction/prospecting | No complete placement/construction or prospecting backend |
| Squad editing/faction identity | Selection exists; splitting/merging/dismissal and faction naming commands need implementation |
| Orders/AI policy | Medic/rescue/stop exist; stance, chase/hold/passive/ranged/taunt/sneak/run mode and editable jobs need commands |
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
