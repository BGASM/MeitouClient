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

## Validation

Main, reflection and shadow stages repeat object work; inspect all three. **Observed** swamp
probes: hidden fragment shading can dominate despite large triangle counts. Generated LOD,
early depth and sorting change different costs. Validate doors/interiors, town stand-in joins,
LOD fades, mirrored/reflected geometry and texture/memory pressure in still and moving views.
[bench.md](bench.md), [render-shadows.md](render-shadows.md), [render-water.md](render-water.md).
