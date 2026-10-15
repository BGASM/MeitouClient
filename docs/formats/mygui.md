# MyGUI UI resources

Sources: the configured Kenshi install's `data/gui/`, the existing
[screen research](../game/ui-screens.md), and [MyGUI's upstream skin/template examples](https://github.com/MyGUI/mygui/tree/master/Docs/Papers).

## Layouts and resources

- **Verified** against every installed `data/gui/layout/**/*.layout` by
  `MyGuiTests.AllInstalledLayoutsAndHudResourcesCanBeRead`: layouts are XML with a
  `MyGUI type="Layout"` root and nested `Widget` elements. Attributes include `type`,
  `skin`, `name`, `layer`, `align`, `position` and `position_real`. Coordinates have
  four whitespace-separated numbers: left, top, width, height.
- **Observed** in `Kenshi_MainPanel.layout`: `position_real` values describe a widget
  relative to its parent, including children of fractional-size panels. Values may
  extend outside 0..1. Meitou evaluates these against the current viewport; exact
  MyGUI integer rounding and resize alignment are still **Unknown**.
- **Verified** by the installed HUD test: named controls include `TimeSpeedButton1`
  through `TimeSpeedButton4`, `MoneyAmountText`, `DayText`, `TimeText` and
  `LifeBar1` through `LifeBar9`; there is no `LifeBar10`.
- **Observed** in the installed HUD: anonymous `TabItem` children have editor squad
  captions, `LifeBar*Value` widgets have example numeric captions, and
  `JobsPanelInfo` has a caption containing literal backslash-n sequences. They need
  runtime population rather than displaying the source's example values.
- **Verified** by the installed resource test: `kenshi_init.xml` is a `MyGUI
  type="List"` document; its `List file="..."` entries resolve through the game's
  resource index. This loads skin, image, pointer, template, colour and layer files.
  `core_settings.xml` has nested `MyGUI` settings elements rather than named resources.
- **Observed** in `common_skins.xml` / `kenshi_skins.xml`: `ResourceSkin` carries a
  reference `size` and a texture name; each `BasisSkin` carries `type`, `offset`,
  `align`, and named `State` elements. State offsets select atlas pixel rectangles.
  SubSkin corners and edges combine Left/Right/Top/Bottom with HStretch/VStretch/
  Stretch. SimpleText/EditText parts describe text rather than image slices.
- **Observed** in the install's `kenshi_skins.xml`: native UI styling can reuse
  `Kenshi_GenericWindowSkin`, `Kenshi_GenericWindowHeaderSkin`,
  `Kenshi_Button1Skin`, `Kenshi_SquadPanelSkin` and `Kenshi_InventorySlotSkin`.
  These all refer to the `Kenshi_UI.png` atlas; no textures are copied into Meitou.
- **Observed** in the install's `kenshi_fonts.xml`: standard font variants use
  `Exo2-SemiBold.ttf`; painted text variants use `Sentencia_1.ttf`. The native
  painter now bakes these installed fonts for UI text, with a system-font fallback.
  Original hinting, point-size/resolution conversion and Unicode ranges are not
  reproduced by this initial font renderer.
- **Verified** against all installed layouts by `AllInstalledLayoutsAndHudResourcesCanBeRead`:
  ResourceLayout expansion produces finite widget bounds. The source widgets are
  kept immutable; template Client regions receive application children, and
  template pieces resize against their original pixel reference dimensions.
- **Observed** in `kenshi_templates.xml`: window captions and Client regions are
  template children; window buttons carry `UserString Event` values `close` and
  `pin`. `Kenshi_ScrollBarH` uses its own name as its root skin. A template root's
  native skin must not be instantiated as another template with the same name.
- **Unknown**: exact native widget skin registration, integer rounding, text/font
  sizing, inherited pick flags, localisation, tab button sizing and clipping
  equivalence with the original. No full MyGUI compatibility claim is made yet.

## Implementation (2026-10-13)

`Meitou.Data.Ui` reads layouts, properties and user strings, decodes skin atlas
states, follows resource lists once per resolved path (including cyclic lists),
and retains each original XML document in memory, including unsupported resource
types. XML external entities and DTDs are disabled. No original assets are stored
in the repository. Resource lookup uses the existing `AssetLocator` and enabled
mod resource folders; whole-mod UI parity has not been established.

The game has independent `--ui faithful` / `--ui meitou` modes. Faithful reads
`Kenshi_MainPanel.layout` and renders the install's SubSkin/MainSkin atlas pieces.
Meitou builds a styled native control tree through the public `Meitou.Ui`
extension API. The HTML prototype was removed following the native-UI decision.
See [native UI](../native-ui.md) for code extension slots, registration, styles,
actions and the remaining mod-loader work.

Both modes use the same published simulation snapshot and command queue. The
initial HUD provides the game clock, money, selection, aggregate health/hunger,
pause/speeds, medic/rescue commands and read-only inventory/stats panels. Faithful
also adds live squad member selection to the original squad area. The Tab settings
panel's UI mode choice switches during play; it is saved in `meitou.user.json` on exit. F7 hides
the interface and its mouse hit regions. The graphics Faithful/Meitou switches do
not change UI mode.

Faithful now uses the original inventory, stats, overview and Escape menu layouts,
including their ResourceLayout window frames. The screen host handles modal input,
close/pin events, window dragging, selected tabs, text entry and scrolling/selecting
dynamic lists. Synthetic tests cover these interactions and clipping. Stats reads
all trained skills; inventory renders equipment names and pocket cell footprints.
Escape opens the pause menu; its Options action opens Tab settings. Save/Load now
open the original dialogs, show real slots and CAMERA metadata, and queue named
save/load requests for the host's tick boundary. Original slots are load sources;
named writes use Meitou's save root. Exit, replacing a save and replacing the live
game use the original confirmation layout. Title/new-game layouts show installed
artwork/credits and NEW_GAME_STARTOFF data; Begin uses the existing spawn backend.
The map shows the installed image and supports pan/zoom/centre, player/camera
markers and right-click movement. Original map placement/zoom equivalence remains
unverified. Other overview gameplay bindings are unfinished.

Limitations: exact MyGUI font sizing, Unicode shaping, original medical row order
and injury layer mapping, portraits, equipment/stack/split operations,
and advanced controls are unfinished. Unsupported original HUD buttons are disabled.
Pockets and backpacks support validated drag/move and arrangement commands;
equipment changes are not simulated by those commands. Live medical rows use the
snapshot's anatomy order, health fraction `(flesh - stun + rig) / maximum`, blood
and hunger; this is an implementation choice, not a verified original row mapping.
Meitou mode uses the same original inventory/trader windows as Faithful, while stats keeps its native popup. The installed HUD and
native frontend were rendered in the sandbox at 1280x720; the styled native HUD,
populated inventory and stats were also captured in a real Wanderer new game at
1600x900 using `--ui-showcase`. These are rendering smoke checks, not a
side-by-side fidelity measurement.

The original Faithful HUD, inventory and full-skill stats window were captured at
1600x900 in a real Wanderer start after template expansion. The renderer stores
targets using a lower-left convention, so top-left UI clip rectangles are converted
before issuing Vulkan scissors. **Verified** by `UiRenderingTests` with pixel
readback and Vulkan synchronization validation, and by inspecting these captures.

UI and gameplay gaps are tracked separately in [Faithful parity](../faithful-ui-parity.md).

## Additional resource observations (2026-10-14)

- **Observed** in the installed `Kenshi_InventorySlotSkin`: `TileRect` states carry
  `TileSize`, `TileH` and `TileV` properties. The renderer repeats the atlas cell and
  crops partial cells at the bounds. Inventory presentation uses square cells at 25 px times viewport height / 1080; bag bounds follow the container dimensions, and trader stock
  keeps its full canvas behind a clipped viewport with wheel/arrow scrolling. This scaling policy is Meitou's approximation; the screenshot observations are in [UI screens](../game/ui-screens.md#6-inventory-trade-dialogue-build).
- **Observed** in installed image sets: Group selects a texture and a two-component
  image size; Index selects a named frame sequence; Frame point gives the atlas
  origin. Meitou currently draws the first frame. `Kenshi_TitleScreen` points at
  `title_screen1.dds`; map marker groups point into `Kenshi_UI.png`.
- **Verified** against the installed `Kenshi_TextBoxEmptySkin` by the installed
  resource test: its SimpleText normal state declares colour `0 0 0` without an
  image offset. Text states therefore need independent property decoding even when
  they contribute no atlas rectangle. Synthetic tests verify state fallback and
  explicit widget colour overrides. Standard text templates use dark brown ink;
  runtime parchment paragraphs/list rows use that ink while dark panels can set
  light text explicitly. No original colour rules are inferred from the debug
  overlay's light text default.
- **Observed** in `Kenshi_GenericTextBoxSkin` and `Kenshi_Button1Skin`: SimpleText
  `offset` / `align` specify an inset text rectangle, separately from the atlas
  pieces. The painter now wraps, aligns and clips inside that rectangle, after any
  additional Meitou `TextPadding`. Padding never changes the widget's background
  or clickable bounds. `UiPaddingTests` checks resized skin clients, explicit
  insets and small-widget clipping. The extra paragraph/list insets are a Meitou
  presentation choice requested during review, not a newly verified original rule.
