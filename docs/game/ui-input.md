# Input and controls

Original OIS/GUI/action dispatch research, controls.cfg and exe byte/name-table checks. Source
scopes/action tables below. Meitou `InputState`/`InputBindings` consume presses once per control
tick; host picking/UI issue commands, not direct simulation mutation. [Simulation](../simulation.md#player).

## Binding encoding

**Verified** code=scancode | Shift0x100 | Ctrl0x200 | Alt0x400. Low-byte OIS scancode; left/right
modifiers share bits. Low-byte0 mouse=(buttonIndex+1)*4096 (Mouse1/2/3=0x1000/2000/3000).
Name formatter prefixes Ctrl+Alt+Shift, unnamed Key_n, mouse Mouse_n. Key names are a256-entry
exe table;111 null. Preserve scancode semantics rather than substituting ASCII key codes.
Controls.cfg first line version1, repeated action=KeyName entries allow multiple bindings.
Missing/empty file writes defaults; version<1 treats changed camera/floor/build bindings specially
(exact application partly Observed). Default-unbound and UI-unrebindable actions remain in tables.

## Dispatch and selection

Window/OIS state, modifier composition, binding lookup, action event and GUI/pointer mode are
separate stages. UI consumption/pause/loading/editor state can block commands. Press/release,
held movement, click/drag, double-click, floor/camera and context verbs have separate handlers;
do not trigger game actions solely from a matching key. Selection ownership and inspection differ:
inspecting non-player characters updates panels without giving orders to them. Context menus
choose verbs based on target type/state, faction, carrying and available interaction.

Meitou selection commands retain the current selection when no valid player characters are
targeted (empty ground or an empty drag box). Selecting valid members replaces or adds to it;
inspecting another faction explicitly clears order selection (see [UI screens](ui-screens.md#4-hud-mainbargui)).

Original strategy camera actions/zoom flags are in [camera](../formats/camera.md). Engine defaults:
Space pause,F2/F3/F4=1/2/5,F5 save,F9 load,Tab settings; current complete keys in [viewer](../viewer.md)
and [engine](../engine.md). User bindings and viewer/cinematic bindings have separate lifetimes.
Settings-tab labels/type/ranges/restart semantics: [settings](../formats/settings.md).

### 1. Pipeline — layout/example

```
OIS keyboard/mouse -> MainListener (keyPressed/mouseMoved/mousePressed)
   -> inject into MyGUI (always)
   -> binding table (FUN_140360b30) unless a text field / key capture owns the keyboard
        -> sets held-state flags (polled by camera, selection code)
        -> appends event ids to a per-frame queue
   -> GameLoop_InputActions (FUN_140788100) drains the queue: MainBar hotkeys first, then game actions
   -> frameEnded (FUN_14082b6e0) clears the pressed flags of event actions and the queue (FUN_140360a80);
      held flags are cleared by the release handler (FUN_140360da0), which looks the released code up again
```

### 4. Action table

Binding maps: general map at Controls+0x28, build-mode map at +0x50 (checked first while mode field +0xd4 == 1,
**Observed**). Actions with `build_` prefix use context 1.

| Action | Default | Event id | Kind |
|---|---|---|---|
| mouse_select | Mouse1 | 0 | held |
| mouse_command | Mouse2 | 0 | held |
| mouse_rotate | Left Control or Mouse3 (two alternative bindings, not a chord) | 0 | held |
| camera_forward / back / left / right | Up+W / Down+S / Left+A / Right+D | 0 | held |
| camera_tilt+ / tilt- | `,` / `.` | 0 | held |
| camera_rotate_left / right | Q / E | 0 | held |
| camera_zoom_in / out | Home / End | 0 | held |
| highlight | Left Alt | 0 | held |
| toggle_fps_camera | `;` | 0x28 | event |
| toggle_camera_grid | Shift+`;` | 0x29 | event |
| floor_up / floor_down | PgUp / PgDn | 0x24 / 0x25 | event |
| speed_1 / 2 / 3 | F2 / F3 / F4 | 0x13 / 0x14 / 0x15 | event |
| stop_movement | R | 0x16 | event |
| pause | Space | 0x12 | event |
| toggle_block / hold / passive / ranged / sneak | NUM0 / NUM1 / NUM2 / NUM3 / NUM4 | 0x17 / 0x18 / 0x19 / 0x1b / 0x1c | event |
| toggle_taunt / cycle_run_speed | NUM5 / NUM6 | 0x1d / 0x1e | event |
| medic / rescue | NUM7 / NUM8 | 0x1f / 0x20 | event |
| toggle_jobs / prospect | unbound | 0x1a / 0x21 | event |
| screenshot | SYSRQ, F8 | 0x10 | event |
| toggle_bar | F7 | 8 | event |
| toggle_inventory / stats / map | I / C / M | 1 / 2 / 3 | event |
| toggle_faction / research / crafting | unbound / T / Y | 4 / 5 / 6 | event |
| toggle_help | F1 | 7 | event |
| quicksave / quickload | F5 / F9 | 0xe / 0xf | event |
| toggle_build | B | 10 | event |
| focus_char | F | 0x11 | event (accepts Shift) |
| select_0..8 / select_9 | keys 1..9 / key 0 | 100..109 | event (accepts Shift) |
| select_all | GRAVE | 0x22 | event (accepts Shift) |
| change_squad | Tab | 0x23 | event |
| character_next / prev | `]` / `[` | 0x26 / 0x27 | event |
| build_undo / build_apply | Backspace / Space | 0xb / 0xc | event, build map |
| build_rotate_left / right | `,` / `.` | - | build map |
| build_move_up / down | `=` / `-` (+ NUM+ / NUM- registered) | - | build map |
| build_tilt_increase / decrease | `]` / `[` (+ NUM+ / NUM-) | - | build map |
| editor_toggle | Shift+F12 | 9 | event |
| editor_delete | Delete | - | event |
| reload_biomes | Ctrl+F6 | 900 | event |
| rebuild_navmesh | Ctrl+Shift+F11 | 0x386 | event |
| gizmo_move / rotate / scale | H / J / K | 0x398 / 0x399 / 0x39a | event |

### 6. Event dispatch

GameLoop_InputActions (FUN_140788100; step 4 of [game-loop.md](game-loop.md#update-threads-and-budget)) drains the queue each frame. **Observed**: it does nothing while the loading
window is visible (FUN_1406e2100 is a visibility test on the LoadingWindow, constructor 14091a280); the Esc menu
being visible (EscMenu, constructor 1409180e0) ignores every event; two further gates suppress most game
events: a byte at PlayerInterface+0x2f0 and a visibility flag of the widget at GUI+0x20 (FUN_1406e1fd0, not
identified; pause 0x12 is only blocked by the second). While the MainBar object exists every event except the
100..120 and 0x22 group first goes to the MainBar hotkeys (FUN_1407262b0):

| Event | Effect (**Observed**) |
|---|---|
| 1 | open the inventory window of the selected character (FUN_140724980 -> FUN_1406e6f10; target window inferred from the action name `toggle_inventory`) |
| 2 | open or, when already open, close the per-character window for the selection (FUN_1406e5f10; stats window inferred from `toggle_stats`) |
| 3 / 4 / 5 / 6 | request Overview tab Map / Faction / Research / Crafting (HUD field +0xd0 = 0/1/2/3) |
| 7 | toggle the visibility of one GUI window (FUN_1406e1f20; taken to be the Tutorials window, **Unknown** which) |
| any other id | forwarded to the orders panel handler FUN_1407226e0, which acts on 0x17..0x21 (orders buttons) and ignores the rest |
| 8 | not in the hotkey function: handled in the action switch by FUN_1406e2a20, which flips the GUI-hidden flag at GUI+0x330 ("toggle whole GUI") |

Meitou's first aid and rest bindings (2026-10-13, `PlayerInterface`, engine choices; simulation side in [simulation.md](../simulation.md#bodies)). The original's own: `medic` (NUM7, event 0x1f) and `rescue` (NUM8, 0x20) are
orders-panel toggles forwarded to FUN_1407226e0 (the Medic and Rescue buttons of the panel, [ui-screens.md](ui-screens.md#4-hud-mainbargui)), i.e. job switches, not targeted orders (**Observed**); the targeted order is the context menu
verb 25 "First Aid" (pointer mode 1 `med`, [pointers](#mouse-pointers); Meitou's context menu also provides it). The numpad belongs to the cinema camera (`Cinema`, docs/viewer.md), so:

| Input | Effect |
|---|---|
| right click on one of the player's own characters with something to bandage | the selected characters treat it (`TreatOrder`); a lone selected character on itself treats itself |
| `H` | medic job on / off for the selection (the original's `medic` toggle: idle characters with a kit treat hurt squad mates by themselves) |
| `L` | the selection rests (sits down; **no binding in the original**); any move order or `R` ends it |

| Input | Effect |
|---|---|
| Ctrl + right click on a knocked-out or dead character | the nearest selected character picks it up (`PickUpOrder`); on a body a selected character carries, puts it down (`PutDownOrder`) |
| right click on one of the player's own downed characters with nothing to bandage | picks it up (as the original's quick right click; with something to bandage it is first aid, as above) |
| Alt + right click on a knocked-out or dead character | the nearest selected character takes all that fits from its inventory and backpack (`LootOrder`; no window) |
| `J` | rescue job on / off for the selection (the original's `rescue`, NUM8) |
| `K` | the selected carriers put their bodies down |

### Movement marker

**Observed** (decompile): a ground move order shows an entity of `position.mesh` (a downward cone over a flat ring, 0.29 × 1.56 units)
scaled 8 on its scene node, at the ray's ground point (the terrain-click handler FUN_140800250, show FUN_1407f2660). Built in FUN_1407f9530:
a clone (`movemarker`) of `debug_marker`, pass 0 set to alpha blending (`setSceneBlending(0)`, SBT_TRANSPARENT_ALPHA). Each frame FUN_1407f92d0 sets the fragment constant
`colour` of pass 0 to (0.612, 0.694, 0.529, t) and of pass 1 to (0.306, 0.347, 0.2645, 0.6 t), then lowers t (1 at the click) by the scaled
frame time `DAT_142133794` ([game-loop.md](game-loop.md#the-three-time-values): 1 s of game time, 0.01 a frame at speed 0) and hides it at
t ≤ 0. Shown only with `Movement marker` ([settings.md](../formats/settings.md); global `DAT_142133511`, `Rotation marker` is `DAT_142133510`).
**Verified** against `data/materials/debug.material` and `materials/forward/basic.program` / `basic.hlsl`: `debug_marker` pass 0 is
`depth_func less_equal`, pass 1 `depth_write off`, `depth_func greater`, `alpha_blend` (the part behind the scene); both `Basic_Lit_VP`
(`NORMALS`: `rgb *= saturate(N·sun) * contrast + 1 - contrast`, contrast 0.5) and `Basic_FP` (`colour × vertex colour`).
Meitou (`MoveMarkerRenderer`) draws both passes after the tone map, so the colours show as given whatever the exposure, sorting fragments by
the near slice's scene depth, multisampled (up to 8×) and resolved over the picture: the mesh's rough band at the cone's widest part
and the notch on the stem are in `position.mesh` itself; on every ground move, following the cursor each frame while the button steers, always on (settings.cfg is not read yet).

### Mouse pointers

**Verified** (read `data/gui/pointer/kenshi_pointers.xml`, `images/kenshi_images.xml`): 25 `ResourceImageSetPointer`s (default `arrow`, layer
`Pointer`, the last in `core_layer.xml`), each naming a `Size`, a hot spot `Point` (pixels from its top left) and an image set whose one frame
(group and index `Pointer`) is a crop of `Kenshi_UI.png`. An entry with an empty name and no values also exists. Some frames differ from the
pointer's `Size` (`lift` 34×30 in 35×35, `hand` 33×35 in 32×32, `pick_item` 32×32 in 35×35). **Verified** (MyGUI `ResourceImageSetPointer::setPosition`, read on GitHub): the image box is `Size` large at mouse − `Point`, so the frame is stretched to `Size`; screen pixels (no UI scale).

**Observed** (FUN_1406e3d60, which calls `PointerManager::setPointer`): pointer mode → name: 0 `arrow` (`invisible` with the free camera
and the Esc menu hidden), 1 `med`, 2 `search`, 3 `lift`, 4 `pick_item`, 5 `attk`, 7 `talk`, 8 and 10 `s_talk`, 9 `use`, 0xb `build`,
0xc `door`, 0xd `door_escape`, 0xe `lock`, 0xf `lockpick`, 0x10 `house`, 0x11 `green`, 0x12 `mine`, 0x13 `spanner`, 0x14 `light`,
0x15 `steal`, 0x16 `hand`, 0x17 `invalidmove`, 0x18 `scavenge`, 0x19 `knockout`; 6 has no case. Which hover state sets each mode is
**Unknown** (the function has no direct callers in the dump).

Meitou (`PlayerInterface.Pointer.cs`, engine choices): the window's own cursor is hidden and the pointer drawn last in the frame (not in
screenshots or cinema takes). It follows the quick right-click order, so it never disagrees with the click: with a selection, over a
trader `talk`, another living character `attk` (neutrals too: the quick order does not check hostility yet), an own hurt character `med`, an own downed one `lift`; Ctrl over a downed character
`lift`, Alt `search`; otherwise, over the interface, the settings panel or the loading screen, `arrow`. The free camera hides it (as mode 0's
`invisible`). Interface → Hardware mouse (`HardwareMouse` in meitou.user.json, default off as the game's `hardware_mouse`; settings.cfg is
not read) shows the Windows cursor instead, applied at once.

### Hover outline

Meitou only (Advanced → Hover outline, `HoverOutline`, default on; the game has none). The character under the cursor is drawn alone into a
mask (colour pass, live pose, picture levels, own depth planes round it) right after the portraits, then after the tone map an outline
(about 2.5 px at 1080p, fading outward) is blended round the silhouette, through walls, and its visible part tinted, visible meaning its
linear depth within 1 % + 1 unit of the near slice's scene depth (jittered, render size). Its colour is the pointer's order: attack red,
treat green, talk gold, carry/loot blue, else white (no selection or no order). Off in the free camera, with the interface hidden and in
screenshots and cinema takes. Not in the Shift+F1 switch list (a game setting, like Attackers per target).

## Unknowns

- Meaning of the listener's suspended field at +0x50 and of FUN_1409134c0 (possibly splash skip).
- What exactly the version-below-1 branch of the controls loader does with its action set.
- Identity of the widget behind FUN_1406e1fd0 (GUI+0x20) and of PlayerInterface+0x2f0, which gate most game events.
- Which window event 7 toggles (taken to be Tutorials); targets of events 1 and 2 inferred from the action names.
- Meaning of the target kinds 8 and 0x16 that shortcut the 0.2 s context-menu hold test.
- Press versus release timing of the command button, and the exact rotation marker condition.
- Whether a held, steering right click shows it again each order.
- Conditions inside FUN_140917120 (which windows count as closable) and the meaning of the held/edge flags.
- Box select handling of non-player squads and animals.
- Whether the controls loader accepts every composed name the formatter writes (`Alt+`, `Key_<n>`) when reading
  a hand-edited file.

## Expression reference

- Binding code encoding: `=`
- controls.cfg: `action=KeyName`
- Action table: `map[code] = action`

## Evidence index

Evidence scopes below apply only to their named sources/checks.

- Context menu verbs (FUN_14079b2d0)
