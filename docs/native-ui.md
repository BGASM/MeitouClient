# Native UI and extensions

UI mode (--ui/Tab) is independent of graphics: Faithful adapts MyGUI assets, Meitou native trees;
Vulkan paints both, choice persisted. `Meitou.Ui` has no window/GPU/simulation dependency.
Host supplies snapshot text/availability and actions that enqueue commands. Extensions receive
read-only binding copies, never mutable world. [MyGUI](formats/mygui.md), [parity](faithful-ui-parity.md).

## Extension API

Provisional ApiVersion1: IUiExtension registered explicitly with UiHost.Register, returning
IDisposable. No automatic mod-DLL scanning/loading; incompatible versions/duplicate module IDs
or keys rejected. Keys namespace extension-id:local-name. Failed registration rolls back;
disposal removes factories/styles/overrides/actions/contributions; re-register supported.

RegisterControl builds UiNodes; RegisterStyle supplies state colours/alignment/skin/font/scale/
TextPadding UiInsets; OverrideStyle scoped replacement. RegisterAction receives read-only frame
context/string argument; disabled controls never call it. Contribute supplies ordered slot tree
with optional UI-mode restriction. OverrideControl can extend context.CreateBaseControl(id).
Source/test interfaces are authoritative for signatures; samples in Ui extension tests.

Builtin meitou toolbar/selection/squad factories, panel/label/button styles. UiSlots:
hud.toolbar,hud.selection,hud.squad,hud.overlay; Faithful first3 map original regions, overlay
viewport. Equal-order later contribution draws above earlier; shared slot does not automatically
insert into builtin layout. Override factory or choose explicit child placement.

## Layout, input and lifetime

Panels/labels/buttons/compound children: pixel/percentage/offset/right-bottom placement,
absolute/row/column flow, padding/gaps. Parent disabled propagates; clip drawing/hits to parent/
slot. Labels pass input through; panels block world in region. F7 suppresses all UI mouse regions.
Registration/build/action on UI thread; fresh tree each UiFrameContext, no blocking callbacks.
Painter only receives UiControls, never calls module code. Contribution/action exceptions raise
UiHost.Diagnostic; failed contribution omitted atomically, others continue. Modules own resources
outside registration. Native HUD uses actual snapshot aggregates. Both UI modes open the original
equipment, backpack, limb and trader windows; the Meitou stats popup remains native. Inventory/stat interactions
and current gaps are tracked in parity ledger. [Host](engine.md), [saves](simulation.md#quicksave-and-quickload).
