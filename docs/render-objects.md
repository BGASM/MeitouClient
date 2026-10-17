# Object rendering

`WorldObjects`, `WorldObjectLayout`, `WorldObjectRenderer`, `ObjectStreamer`, `ObjectMeshCache`,
`BuildingLodMesh`, `BuildingLodShaders`, `ObjectRanges`. Placement rules: [zones](formats/zones.md);
materials: [runtime-materials](formats/runtime-materials.md); geometry: [Ogre mesh](formats/ogre-mesh.md).

## Assembly, LOD and streaming

Resolve building/part/feature instances from merged records, seeded part choice and placement
transforms. Preserve interior/exterior, materials, destroyed states, doors, offsets and scale.
Workers prepare resident mesh/texture content; render thread publishes GPU objects. Shared meshes
and per-instance state have separate lifetimes. A resident mesh can change finest LOD: refresh
all draw/ray-geometry references before retiring replaced buffers.

Faithful uses researched object/part distances; Meitou `reach` extends size-aware ranges and
distant landmarks/towns. Distant town stand-ins fade separately from real parts. Source/manual
and generated levels use projected demand with stable transitions. Do not treat a shorter part
range as an accidental renderer limit: it can be original fidelity. Visibility/depth/shadow/
reflection and streaming residency need consistent bounds.

## Batching and depth

Batch compatible mesh/material/state instances; sort colour batches near-first when enabled.
Open building meshes are double-sided. Fade uses per-instance dither lo/hi windows; solid,
fading and distant-town batches have different coverage needs. `EarlyDepth.Enabled` selects
opaque, discard-free colour only for no-cutout/non-fading parts. Draw solids first; retain
cutout/dither shaders for other batches. Cache native state per program so solid/fading variants
do not evict one another. Depth/caster coverage must remain compatible with colour.

## Player buildings and the placement ghost

Sites and finished buildings of `WorldSnapshot.Buildings` are runtime placements (`WorldObjects.AddRuntime/RemoveRuntime`,
`BuildingPlacement` with `WorldY` absolute), merged into their zone's layout. Meitou adds one to an already loaded zone
incrementally (`ObjectStreamer.AddRuntime`, no zone reload, so nothing flickers; a pending zone job falls back to a
reload). Host diff: [simulation.md](simulation.md#buildings).

**Scaffold.** The original clips each part at `1.2 f` of its mesh height and draws the scaffold grid (`construction.dds`)
above it ([construction look](game/buildings-production.md#construction-look)). Meitou's approximation: the part's
instance matrix carries `M44` = `ScaffoldBase` (10000) + the world-space clip height (`f` and the mesh top from its bounds);
`BuildingLodShaders` keeps pixels below it as the building and above it only a procedural world-space grid
(cell 0.12, line 0.06), discarding the rest. The shadow pass clips the same. Differences: no `construction.dds`
texture, no fade of a grid over the built part, and the clip is a world height, so a tilted site clips level.
Sites always use the fading (discard-capable) batch.

**Ghost.** `WorldObjectRenderer.SetGhosts(GhostDraw[])` draws a building's parts (laid out once at the origin, exterior
only) at a transform, from the `ObjectStreamer.Loose` pseudo-zone (never streamed out, scanned each frame like the
landmarks), with 65 % dither coverage and a tint through the push `FlatColour` (`uTint` only reaches grey-channel
materials): green valid, red invalid, blue queued. Not drawn in shadow or landmarks-only passes.

## Doors

Door parts carry `PlacedMesh.DoorIndex` (their entry in the building's `doors` list). The host hands the renderer the snapshot's
open amounts (`WorldObjectRenderer.Doors`, by placement id and index); `DrawReal` poses a door part from its closed placement by
`DoorLeaf.LocalOffset` ([motion](game/locks.md#doors)) along the part's own axes, in world units, when its amount changed; the
step-out axis comes from the mesh file's bounds. Colour and shadow passes see the moved leaf; the GI acceleration structures take
the instance transform only when they gather instances (not checked for leaves in motion).

## Validation

Main, reflection and shadow stages repeat object work; inspect all three. **Observed** swamp
probes: hidden fragment shading can dominate despite large triangle counts. Generated LOD,
early depth and sorting change different costs. Validate doors/interiors, town stand-in joins,
LOD fades, mirrored/reflected geometry and texture/memory pressure in still and moving views.
[bench.md](bench.md), [render-shadows.md](render-shadows.md), [render-water.md](render-water.md).
