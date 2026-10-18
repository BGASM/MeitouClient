# Terrain rendering

`TerrainQuadtree`, `TerrainLod`, `TerrainRenderer`, `TerrainTextures`, `TerrainStreamer`.
Original height/blend/map/material facts: [formats/terrain.md](formats/terrain.md).
Engine LOD/streaming rules below; GPU lifetime: [renderer-native.md](renderer-native.md).

## Geometry and material

CDLOD chooses patches by error in rendered pixels, morphing between levels without cracks.
Whole-world coarse heights keep the horizon; a fine moving window follows the eye. Use fine
height directly at weight1, blend at edges. Height/normal sampling must agree between terrain,
objects, water/shore bakes and collision queries. Near/far slices retain consistent depth.
TERRAIN-mode rock geometry is foliage work, not terrain-patch stage timing.

Biome materials resolve blendinfo slots, mask/overlay/colour maps and layer colour/normal inputs.
Preserve source tile scales, layer order and weights; the engine's texture window does not alter
world coordinates. `terrain-layer-skip` can omit insignificant layer work; thresholds are quality
choices requiring boundary/material checks. `terrain-late-colour` separates depth from shading
where occluders justify it. A depth prepass is extra geometry, not automatically a speedup.

## Interior cut

Terrain inside a building's interior mask is not drawn, so a room dug into a hillside shows its floor.
- **Verified** (installed `interiormask.material`/`.hlsl`, `workspace.compositor`, `terrain.material`, `terrainfp4.hlsl`): node
  `InteriorMask` draws render queues 16-17 into `rt_depth_range` (RG16F, cleared 0, no depth buffer) twice: culling clockwise
  writing G, culling anticlockwise writing R, each value view depth + 1. Terrain (always), foliage (always) and CLIP_INTERIOR
  materials sample it as `rt_interiormask`: `if (w < G) clip(R - w)`, i.e. a fragment more than 1 unit beyond R and before G is
  cut. The water reflection binds a 1x1 dummy; the terrain shadow caster does not sample it.
- **Observed**: the queue-16 geometry is the `interior mask` parts' `phs or mesh` (`Storm_House_01_Interior_MASK.mesh` etc.;
  [settings](formats/settings.md) `skip stencil tricks`), placed with the building node. **Verified** (probe, merged
  records): 31 mask parts have such a mesh (gate triggers have none), none has an offset; the meshes are not the
  `xml collision` hulls (Outpost mesh Y 214..401, hull -155..748) and 30 of 31 are not convex.
- Meitou (`InteriorMaskRenderer`): masks of loaded buildings within 3000 of the eye, drawn before the scene with the near
  slice's camera into RGBA16F; every face writes depth + 1, min into R, max into A (the nearest and farthest face, the same as
  the game's facing split for a closed mesh seen from outside; not dependent on the meshes' winding). Patches overlapping a
  drawn mask mesh's X/Z box use cut variants of the colour and prepass programs; others keep discard-free programs. With the
  eye inside a building's trigger hull its masks write R = 0 (the game's value there: every face seen is a back face).
  Engine limits: near slice only; "inside" is decided by the hull, not per ray; foliage and CLIP_INTERIOR objects are not
  cut yet.

## Streaming and budgets

Worker jobs decode/sample; render thread uploads/publishes through BackgroundWork/UploadQueue.
Keep valid old windows until replacements are ready. Prioritize useful nearby work, limit each
frame's upload/allocation steps and retire resources only after in-flight references finish.
WorldTextureCache and mesh caches share memory pressure/range policy; avoid unloading resources
immediately needed by adjacent zones or a new mip level. Cold load/settle and steady-state frames
are separate performance measures. Height-window changes invalidate dependent bakes/probe bases.

## Validation

Use matched rendered resolution/error, not display resolution alone. Inspect seams/morph bands,
biome edges, fine/coarse window movement, horizon and teleport. **Observed** RTX4070 swamp/Hub
probes: near views can be material/shadow-bound; distant views can be geometry-bound. Changing
anisotropy, error or LOD need not change the limiting cost. [bench.md](bench.md).
Faithful material/placement claims remain in the format reference; keep output/noise evidence
with any deliberate Meitou quality change.
