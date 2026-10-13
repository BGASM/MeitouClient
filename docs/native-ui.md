# Native UI and code extensions

The UI has two presentation modes, independent of graphics mode: Faithful reads
the original MyGUI assets; Meitou builds a styled native control tree. Both are
painted into the Vulkan frame. The HTML prototype has been removed.
UI mode is selected in the Tab settings panel (Faithful / Meitou) or with `--ui`;
there is no mode-switch button on either HUD. The choice is saved on exit,
independently of the graphics switches.

`src/Meitou.Ui` is the public, platform-independent UI library. It references the
data types used by the MyGUI adapter, but does not reference Vulkan, windowing,
the executable or the simulation. The game supplies text/availability bindings
from its published snapshot and registers actions which enqueue game commands.
Extensions receive read-only copies of those bindings, not the mutable world.

## Extension API (version 1, provisional)

Code modules implement `Meitou.Ui.IUiExtension`. The host calls
`UiHost.Register(extension)` and keeps the returned `IDisposable`. Registration
is explicit: this change does **not** scan or load DLLs from mod folders. A future
managed mod loader will own these handles and expose the host to modules.

- `RegisterControl`: register a factory which composes native `UiNode` objects.
- `RegisterStyle`: register shared normal/hover/active/disabled colours, text colour
  and alignment, optional MyGUI atlas skin, font name, text scale and `TextPadding`
  (`UiInsets`: left/top/right/bottom pixels, applied inside a skin's text client).
  `OverrideStyle` temporarily changes an existing named style.
- `RegisterAction`: register an action callback with a read-only frame context and
  a string argument. A disabled control never invokes its handler.
- `Contribute`: add a control tree to a named slot, with a stable order and an
  optional restriction to Faithful or Meitou mode.
- `OverrideControl`: replace an existing registered factory. A replacement can
  call `context.CreateBaseControl(id)` and append children to extend the original.
- Disposing a registration removes its contributions, factories, styles, overrides
  and actions. A failed registration rolls back all of its changes. Re-registering
  the same module after disposal is supported.

Registration keys are automatically namespaced as `extension-id:local-name`.
Duplicate module IDs and duplicate keys within a module are rejected. Modules
with an incompatible `ApiVersion` are rejected before registration. This is an
initial contract, not a promise of permanent binary compatibility; package/version
negotiation belongs to the future mod loader.

The built-in module is `meitou`. Its reusable factories are `meitou:toolbar`,
`meitou:selection` and `meitou:squad`; its styles are `meitou:panel`,
`meitou:label` and `meitou:button`. Slots are `hud.toolbar`, `hud.selection`,
`hud.squad` and `hud.overlay` (`UiSlots` constants). In Faithful mode, the first
three refer to the corresponding original HUD regions. The overlay spans the
viewport. Contributions into a slot share that region; choose child placements or
override the built-in factory when inserting into its internal layout. A later
contribution with the same order draws above an earlier one.

## Example module

This module adds an independently styled panel and an action in both UI modes.
It only needs references to `Meitou.Ui` and the shared `Meitou.Data.Ui` types.

```csharp
using System.Numerics;
using Meitou.Ui;

public sealed class SupplyPanel : IUiExtension
{
    public string Id => "example.supplies";
    public int ApiVersion => 1;
    bool expanded;

    public void Register(UiRegistration r)
    {
        string panel = r.RegisterStyle("panel", new()
        {
            Background = new Vector4(0.12f, 0.18f, 0.16f, 0.95f),
        });
        string button = r.RegisterStyle("button", new()
        {
            Background = new Vector4(0.2f, 0.35f, 0.25f, 1),
            Hover = new Vector4(0.3f, 0.45f, 0.35f, 1),
            TextAlign = "Center",
        });
        string toggle = r.RegisterAction("toggle", _ => expanded = !expanded);
        string factory = r.RegisterControl("panel", c =>
        {
            var root = new UiNode(r.Key("root"))
            {
                Placement = new(260, 100, Right: 16, Top: 120),
                Style = panel, Padding = 12,
            };
            root.Children.Add(new(r.Key("toggle"), UiNodeKind.Button)
            {
                Placement = new(UiLength.Fill, 30), Style = button,
                Text = "Supplies", Action = toggle,
            });
            root.Children.Add(new(r.Key("funds"), UiNodeKind.Label)
            {
                Placement = new(UiLength.Fill, 24, Top: 44),
                Text = c.Text("MoneyAmountText"), Visible = expanded,
            });
            return root;
        });
        r.Contribute(UiSlots.Overlay, c => c.CreateControl(factory), order: 100);
    }
}
```

## Layout, input and lifetime

Native nodes support panels, labels, buttons and compound controls made from
children. Placement supports pixels, percentages, offsets and right/bottom
anchors; container flow supports absolute layout, rows and columns with padding
and gaps. A parent's disabled state applies to its children. Children are clipped
to the containing slot/parent for both drawing and hit testing. Labels pass input
through to underlying buttons; panels normally block world input in their region.
The game's F7 visibility switch suppresses the entire UI's mouse regions.

All registration, frame building and actions run on the UI thread. Build a fresh
tree from `UiFrameContext` each frame; avoid blocking work in factories or actions.
The Vulkan painter receives the resulting `UiControl` list and does not call
extension code. Exceptions during contribution construction or action execution
raise `UiHost.Diagnostic`; a failed contribution is omitted atomically and other
modules continue. Module authors own any other resources/subscriptions they create
outside the registration scope.

## Status and validation

The built-in Meitou theme reuses the install's weathered MyGUI skins and standard/
painted fonts. The HUD is arranged along the bottom: character condition and
inventory/stats shortcuts on the left, clickable squad members in the centre,
time/speed/orders on the right. Blood, worst-part health and hunger meters use the
published snapshot's actual values; these are aggregate indicators, not a guessed
mapping to the original game's individual body-part bars. Stats bars show 0..100
visually while retaining the exact numeric value. Inventory shows the published
item names and quantities with decorative slot frames; it remains read-only.

Capture actual HUD, inventory and stats states in one run:

```text
dotnet run --project src/Meitou.Game -- --new-game --ui meitou --ui-showcase --screenshot artifacts/ui/kenshi-native.png --size 1600x900 --ticks 2
```

This writes `kenshi-native-hud.png`, `kenshi-native-inventory.png` and
`kenshi-native-stats.png`. The showcase opens the inventory/stats through their
registered button actions, after selecting the player's leader through the normal
selection command and publishing at least one tick. It omits the animation sandbox's diagnostic panel when
used with `--sandbox`, and leaves ordinary single-frame screenshots unchanged.
Images remain in the ignored artifacts directory.

The 1600x900 showcase was rendered against the configured install's Wanderer
start after two simulation ticks: the HUD displays the selected character's real
condition/money, inventory lists the carried items, and stats shows the trained
values. The build and all 18 UI tests pass after the styling change.

Implemented: native HUD, style states, shared game actions, extension/control/style
registries, scoped unloading, rollback, diagnostics, read-only frame bindings and
mode-specific contributions. `NativeUiTests` checks these independently of a GPU;
the installed-layout tests retain the Faithful asset-reader check. Both modes keep
the current basic read-only inventory/stats popups.

Still to do: dynamic assembly discovery/loading, richer typed gameplay bindings,
keyboard focus/navigation, editable text/IME, scrolling, inventory drag/drop,
fonts/text shaping, accessibility, richer layout and custom drawing primitives.
Code extensions currently compose the supplied native primitives; there is no
public raw Vulkan callback or full custom painting API.
