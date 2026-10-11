# Ray-traced global illumination (Meitou)

Status: **milestone 2** (2026-10-09, branch `global-illumination`): the traced scene, a debug view and probe GI (`--gi`).
This is a Meitou-only feature with no counterpart in the game. Kenshi's indirect light is one flat hemisphere ambient per biome
(`ambient light`, `KenshiLighting` / `AmbientMap`, [formats/lighting.md](formats/lighting.md)) plus SSAO, which the game ships switched off.
It is engine work, so the claims carry where they come from: *from the code*, *measured* (how), or *open*.

GI is an **optional quality mode** (DECISIONS 24): it needs a GPU with ray queries, it does not count against the frame-time targets of
the default picture, and `--faithful` keeps the flat ambient. The owner's target with it on: about 144 fps at 1080p with DLSS at a 0.67
render scale.

## Plan

1. **Milestone 1 (done)**: device support, acceleration structures for the terrain and the objects, a debug view that traces from the
   depth buffer. The question it answers: can the acceleration structures for Kenshi's world be kept up to date cheaply? Yes (see
   "Measured").
2. **Probe GI (done)** (DDGI-like, see "Probes"): a camera-centred grid of irradiance probes traced against the same structures, sampled by the material
   shaders in place of the hemisphere ambient (`mix(uAmbientGround, uAmbientSky, …)`, e.g. `TerrainShaders`). This fits the forward
   renderer as it is: no new targets. It became a runtime branch in `kenshiLight`, not a variant (see "Probes").
3. **Per-pixel GI** (ReSTIR GI, DLSS Ray Reconstruction): needs normal and albedo targets and a composite pass. These are changes to every
   renderer's fragment outputs, so this step comes after the probes.
4. **Fallback** for GPUs without ray queries (the integrated-GPU laptop): sky irradiance in spherical harmonics plus terrain horizon
   occlusion.

## Device (`VulkanDeviceOptions.RayTracing`)

*From the code.* Asked for unless `--no-gi` (GI is on by default since 2026-10-09; `--gi` still parses), or by `--gi-debug`, through `VulkanDisplay.RayTracing`, in the viewer and the game alike. A device without ray queries runs with GI off and prints `gi off`. When the device has `VK_KHR_acceleration_structure`,
`VK_KHR_ray_query` and `VK_KHR_deferred_host_operations` with buffer device address, `VulkanDevice.HasRayQuery` is set and they are
enabled. Without the option the device is created exactly as before. With it:

- Every buffer block of `GpuAllocator` is allocated with `VK_MEMORY_ALLOCATE_DEVICE_ADDRESS_BIT`, so any buffer can be given a device
  address.
- `BufferUse.RayInput` adds the shader-device-address and acceleration-structure-build-input usages. The object meshes' vertex and index
  buffers get it, so the bottom levels are built from the very buffers that are drawn. There are no copies.
- Buffer device address is the core 1.2 feature, never the EXT/KHR extensions, the same as with Streamline (DECISIONS 17), so DLSS and GI
  coexist.

A device without ray queries prints a warning and runs without the view. Shaders are GLSL 460 with `GL_EXT_ray_query`, compiled by the
same shaderc for Vulkan 1.3 (`GlslProgramCompiler`). `ShaderLibrary.Compute(glsl, name, bindings)` takes set 0 as given, because the
reflection reads neither storage images nor acceleration structures, and pushes it.

## The traced scene (`Gi/GiScene.cs`, `Gi/AccelerationStructure.cs`)

*From the code.*

- **Origin.** Positions in the structures are relative to `GiScene.Origin`, the eye rounded to 64 units, which follows the eye only once it is more than 1024 units away (2026-10-11, see "Cost of the GI stage"). Each frame's instance transforms
  are offset by it, so floats keep their precision anywhere in the world, which is 1.47 M units across.
- **Terrain.** Two height-field grids around the eye: 256 × 256 quads at 16 units (4096 across) and at 160 units (40960 across). Each is
  sampled from the CPU `HeightSnapshot` on a worker, uploaded and built into a bottom level. A grid is rebuilt when the eye has moved an
  eighth of its side, and the old one is freed after the frames that traced it.
- **Terrain facing.** The grids are coarser than the drawn terrain, so in a hollow the drawn surface lies under them. A bounce ray from
  there would hit the grid from below at once, which showed as large flat patches. The terrain instance therefore keeps facing culling,
  and the rays use `gl_RayFlagsCullBackFacingTrianglesEXT`, so they pass up through it.
  - **Verified** (`RayQueryTests`): a triangle counter-clockwise seen from the ray is front-facing (Vulkan's default, no `FLIP_FACING`).
    The grid's triangles are counter-clockwise from above.
  - Objects set `TRIANGLE_FACING_CULL_DISABLE`, because their meshes are not closed.
- **Reshaped meshes.** `BuildingLodMesh` can remake a resident mesh with another finest level and put the new parts into the same
  `GpuObjectMesh`, freeing the old buffers. An object structure therefore remembers the source and the vertex buffers it was built from,
  and is rebuilt when they differ. Before this fix, the geometry records pointed at freed memory: hits read it and the device was lost.
  **Verified**: `--bench-motion fly` with validation in the Hub (it crashed before the fix) and the swamp.
- **Objects.** `WorldObjectRenderer.RayInstances`: the resolved real instances (zones and landmarks, not distant stand-ins) whose bounds
  come within `--gi-range` (default 6000) of the eye.
  - Each mesh has one bottom level, built the first time an instance of it is in range, at most 32 per frame. It is built from the finest
    level the mesh holds (or that level's manual LOD mesh), one geometry per part.
  - A structure unused for 600 frames is dropped. Evicting the mesh itself does not invalidate it: a built structure no longer reads its
    inputs.
- **Top level.** One per frame slot, sized for 16384 instances and rebuilt in place (built to trace fast) when its instances are not those of its last build (2026-10-11: with the eye still or walking that is rare, see "Cost of the GI stage"; it was every frame, built fast). Instances and
  geometry records are written into host-visible buffers of the slot.
- **Geometry records.** 32 bytes per geometry: vertex address, index address, stride, kind. A hit's record is the instance's custom index
  plus the geometry index. The shader fetches the triangle's indices and positions with `GL_EXT_buffer_reference` (`uvec2` addresses,
  because `shaderInt64` is not enabled).
- **Foliage** (`FoliageRenderer.RayInstances`, `--gi-foliage <radius>`, default 150, 0 for none): trees, bushes and rocks whose bounding
  radius is at least the threshold and whose bounds come within 4000 units, nearest first, at most 14336 instances. One structure per resident
  mesh (main and leaves parts), keyed by its `GpuMesh` (a reload makes a new one; `RayGeneration` counts deletions). The instance list walks
  every zone, so it is cached: rebuilt every 120 frames, when the eye has moved 250 units, when a foliage mesh was deleted, or 2 frames
  after a rebuild that had structures left to build. Each frame only moves the cached instances to the frame's origin and refreshes the
  records' texture indices.
  - Leaves (and cut-out main meshes) are not opaque in their structures. The probe trace tests each candidate hit against the alpha of the
    part's normal map (the draws' `AlphaSource` 2) at its threshold (leaves without one: 0.5), at mip 2.
  - Plants are double-sided, so a back-face hit on foliage is a surface, not "inside". TERRAIN-mode rocks take the ground colour map, as the
    terrain does.
  - **Measured** (2026-10-09, swamp, DLSS 0.67, `--ab gi-scene`):
    - threshold 60: 5374 foliage instances of 5821, 142.8 fps with GI against 184 without (+1.57 ms GPU);
    - threshold 150 (the default): 375 foliage instances, 146.5 fps (+1.44 ms).
- **Textures at hits.** A geometry record (48 bytes) carries the bindless index of its diffuse map, written each frame per instance
  (`WorldObjectRenderer.RayTexture`, the foliage's `RefreshRayTextures`, at the draws' LOD bias so the entries are shared; 0 when not
  resident). It also carries the cut-out map and threshold. The trace reads the hit's texture coordinates (vertex byte 24) and samples
  mip 4, so a bounce takes the surface's colour. **Verified** by tinting textured hits red in a debug build: building interiors lit red.
- **Not traced yet:** grass, characters, water.
- **Lamps.** The probe trace adds the lamps' diffuse light at each hit ([render-lights.md](render-lights.md)), so lamp light bounces.

## Debug views (`--gi-debug <n>`, `Gi/GiDebugPass.cs`, `GiShaders.Debug`)

*From the code.* A compute pass after the post-processing reads the near slice's depth. It rebuilds positions with the inverse of the
rotation-only view and the near slice's projection, the same way `ShadowShaders.DebugFragment` does, and takes a normal from the depth's
slope on the side of the smaller step. It traces, then scales its picture (render size) over the finished frame.

| Mode | Shows |
| --- | --- |
| 1 | One cosine-weighted bounce ray per pixel: yellow to red by hit distance (rays of 4000 units), blue where it reaches the sky |
| 2 | A grey "clay" picture (albedo 0.5): the sun through a shadow ray, plus `--gi-samples` (default 4) bounce rays. Where a bounce ray hits, it gives the grey hit surface lit by the sun (its own shadow ray) and the flat ambient. Where it misses, it gives the hemisphere ambient in its direction. One bounce, no denoiser, exposure 0.35 |
| 3 | Rays from the eye through each pixel, coloured by the hit's geometric normal: the traced scene itself, with no raster involved (terrain dimmer) |

The F11 statistics and the screenshot log have a `gi` line: update time, instances, object structures and their memory, structures built
this frame, and the terrain grids. The bench registers `gi-scene` (structures and view) and `gi-debug` (the view only) as A/B switches when
the run has `--gi-debug`.

## Measured

**Observed** (2026-10-09, RTX 4070, `--view hub --size 1920x1080 --gi-debug 2 --ab gi-scene --ab-period 16 --bench-frames 512`, TAA):

- GPU total 4.15 against 3.29 ms (+0.86 ±0.01). Of that, the `post` stage, which holds the debug view (9 rays per pixel at 1080p: 4
  bounce, 4 shadow from their hits, 1 shadow from the pixel), is +0.84. The structures, recorded in the frame's `reflection` stage, are
  +0.05.
- Render thread +0.07 ms (`cpu:total`). The `gi` line: 0.07 ms per update, 265 instances and 99 object structures (11 MB) in a close view
  of the Hub.

So keeping the structures up to date costs well under a tenth of a millisecond per frame, and the tracing cost scales with the rays.

## Probes (`--gi`, `Gi/GiProbes.cs`, `GiShaders.ProbeTrace` / `ProbeBlend` / `ProbeSampling`)

*From the code.* DDGI-style irradiance probes, traced against the scene above. The scene and the probes are made by default (`--no-gi` leaves them out). The `gi` switch
(Tab panel and `--faithful gi`) chooses between them and the flat ambient: off, the shaders take the flat ambient exactly as before.

- **Grids.** There are two cascades of 32 × 32 probe columns centred on the camera, each column 8 probes high:
  - cascade 0: 128 units apart, 96 up, rays of 4000 units;
  - cascade 1: 512 apart, 384 up, rays of 16000 units.
  A column stands on the lowest ground under its cell (5 samples of `HeightSnapshot`, recomputed only when the grid scrolls or the fine
  height window changes). The grids scroll in whole cells, and a column's tile is its world column modulo 32 (toroidal), so nothing is
  copied. A probe whose position changed starts over: no hysteresis on its next update.
- **Atlases.** The irradiance atlas holds 8 × 8 octahedral texels per probe, plus a 1-texel border (RGBA16F; alpha 1 marks a valid probe).
  The distance atlas holds 16 × 16 per probe, plus the border (RG16F). It stores the mean hit distance and the mean of its square, over
  twice the spacing. The borders copy the opposite edges, so bilinear filtering works across the octahedron's seams.
- **Trace.** Each probe traces 32 rays (64 with `MEITOU_GI_RAYS=64`; 64 until 2026-10-11, see "Cost of the GI stage"), in spherical Fibonacci directions turned by a random rotation each update.
  - A ray that misses sees the lighting function's ambient in its direction: the sky's irradiance × (1 − 0.04) × the ambient map × the
    environment factor, the same units as `kenshiLight`'s `envDiffuse`.
  - A front-face hit sees an albedo (the weather's ground colour map on the terrain, a constant 0.35 / 0.32 / 0.28 on objects) times the
    sun through a shadow ray, as `kenshiLight` gives it, plus the probes' own irradiance at the hit. That last term is the multi-bounce
    feedback.
  - A back-face hit is dark, with a negative distance. A probe whose rays see more than 25 % back faces is inside geometry and is marked
    invalid.
- **Blend.** The rays are blended into the irradiance tile (cosine weights) and the distance tile (cosine to the 50th power), borders
  included. The hysteresis is 0.97 per frame at 64 rays; with 32 rays it is scaled to 0.985 so that the noise stays the same (the memory of an exponential average of n-ray samples has a variance of (1 - h) / (1 + h) / n).
- **Round robin.** Each frame updates a quarter of the probes, every fourth probe by index, from a phase that advances by one per frame
  (`--gi-phases`, 1, 2, 4 or 8; default 4). The hysteresis per update is 0.97 raised to the number of phases, so a probe settles in about
  as many frames as with every probe updated each frame. When a grid scrolls (or its bases change), `GiShaders.ProbeInvalidate` first sets alpha 0 on every tile
  whose stored position is not its probe's current one (the column that wrapped round from the far side), so its old light is not read
  until its own update starts it over.
- **Sampling** (`giIrradiance` in `kenshiLight`). The 8 probes around the point are blended trilinearly, weighted by the wrap-around
  backface term and by Chebyshev visibility against the distance moments, the same way DDGI does.
  - Cascade 0 is used where it covers the point. Cascade 1 fills in where cascade 0 does not, and the sky's flat ambient fills in where
    neither does.
  - The result replaces `envDiffuse`, by `--gi-strength` (default 1; `uGiParams.y` scales the probes' weight, so 0.75 keeps a quarter
    of the sky's flat ambient).
  - The specular environment term is scaled by the ratio of the probes' luminance to the sky's, clamped to 0..1, as a cheap specular
    occlusion.
- **Shader model.** A runtime branch on `uGiParams.x`, not a compile-time variant: the legacy and native world programs' SPIR-V changes,
  but with the switch off the picture does not.
  - **Measured**, `image-diff`, 1280 × 720, Hub and swamp: with GI off, and with `--gi --faithful gi`, mean 0.0000 against the commit
    before.

**Observed** (2026-10-09, RTX 4070, swamp, 1920 × 1080, `--gi --ab gi-scene --ab-period 16 --bench-frames 512`):

| Setting | GPU with GI | GPU without | Difference | Of it, probe update | Of it, shading | fps with / without |
| --- | --- | --- | --- | --- | --- | --- |
| TAA, every probe each frame | 8.37 | 5.81 | +2.56 | +1.57 | +1.01 | |
| TAA, `--gi-phases 2` | | | | +0.97 | | |
| TAA, `--gi-phases 4` | 7.41 | 5.80 | +1.62 | +0.67 | +1.0 | 134 / 172 |
| DLSS at 0.67, `--gi-phases 4` | 6.34 | 5.26 | +1.09 | +0.59 | +0.55 | 156 / 189 |

- The shading cost is the 8 probes' visibility and irradiance fetches, in terrain, objects and foliage. It scales with the render
  resolution.
- The update does not scale linearly with the phases. About 0.3 ms is fixed: the full barriers between trace and blend, and the base
  upload.
- Render thread: +0.16 ms with the base heights cached; it was +0.42 ms recomputing them every frame.
- **Cheaper updates** (2026-10-09): bounce hits in the trace read the probes without the visibility test (`GI_NO_VISIBILITY`); the distance
  blend skips rays under cos 0.6 (their weight cos^50 is below 1e-11); the barriers between the probe passes and before the shading are
  compute-to-compute/shading ones instead of full ones. Measured: probe update 0.59 to 0.55 ms (DLSS 0.67, swamp). About 0.37 ms of the update
  does not scale with the probes updated (from the 1, 2 and 4 phase runs): the full barriers that start the scene and probe segments.

**Cost breakdown** (2026-10-09, with foliage, object textures and lamps; RTX 4070, `--view swamp --gi --upscaler dlss --render-scale 0.67
--size 1920x1080`). The frame clock has a `gi` stage (the scene and the probe passes, plus the lamps' binning), and the bench has a
`gi-shade` switch: side B updates the probes but the shading does not read them, which isolates the reads.

| Part | GPU ms | How measured |
| --- | --- | --- |
| Whole GI | +1.47 (±0.09) | `--ab gi-scene`, 512 paired frames; 138.6 against 173.6 fps |
| Probe update (`gi` stage) | 0.71 | the same run |
| of it, scene build (bottom and top levels) | ~0.20 | `MEITOU_PASS_STATS=1`, `--fly-benchmark 300 --fly-speed 0` (serialised) |
| of it, trace | ~0.55 | the same; the trace and blend are separate native segments for this |
| of it, blend | ~0.28 | the same |
| Shading reads | +0.74 (±0.10) | `--ab gi-shade` |
| of it, foliage | +0.27 to +0.32 | both runs |
| of it, reflection pass | +0.16 to +0.21 | both runs |
| of it, objects | +0.12 to +0.17 | both runs |
| of it, terrain | +0.15 | both runs |
| Render thread | +0.23 | `--ab gi-scene` |

- The serialised pass meter's parts sum to about 1.0 ms, more than the 0.71 ms of the pipelined `gi` stage; read them as proportions
  (scene ~20 %, trace ~53 %, blend ~27 %).
- **Half the cost is reading the probes, not updating them.** Every shaded pixel blends 8 probes with the visibility test, in every pass
  that runs `kenshiLight`. The foliage pays for its overdraw, and the water reflection pays for a second view of the scene.
- The blend is large for what it does (64 rays into 8 × 8 and 16 × 16 tiles); the distance tile is most of it.

**Cheaper reads and updates** (2026-10-09). **Measured** (idle GPU, same swamp command, 512 paired frames each):

| | Before | After |
| --- | --- | --- |
| Whole GI (`gi-scene`), frame | +1.47 ms, 138.6 / 173.6 fps | +0.94 ms (±0.11), 158.7 / 186.6 fps |
| Probe update (`gi` stage) | 0.71 | 0.47 |
| of it, sleeping probes (`gi-sleep`) | | −0.16 |
| Shading reads (`gi-shade`) | +0.74 | +0.37 (±0.09) |
| of it, water reflection (`gi-reflection`) | +0.16 to +0.21 | 0 (the switch: −0.14) |
| of it, foliage | +0.27 to +0.32 | +0.13 |
| of it, terrain / objects | +0.15 / +0.15 | +0.15 / +0.17 |

- The rest of the update's saving (about 0.08 ms) is the inner-texel blend.
- With GI the p99 frame is still worse than without (104 against 127 fps in the `gi-scene` run).

**Terrain and object reads** (2026-10-09, `--ab gi-shade`, same command; pictures against the commit before at the swamp, the Hub at
7.5, Squin, Mongrel at 16 and Heft at 8):

| Change | Terrain | Objects | Picture |
| --- | --- | --- | --- |
| Before | +0.15 | +0.17 | |
| No visibility test at all (tried, not kept) | +0.08 | +0.05 | Mongrel mean 1.19, 3.5 % of pixels over 12, max 58: light leaks onto the shaded street between buildings |
| Probes under trilinear weight 0.02 skipped (`GI_MIN_WEIGHT`) | +0.14 | +0.12 | max 14, means ≤ 0.006 |
| and the normal's octahedral position once per point, not per probe | +0.14 | +0.11 | the same |

- **Observed:** the reads are bound by the fetches, not the arithmetic. Hoisting the per-probe encode changed nothing measurable, and the
  skipped corners save little because neighbouring pixels in a wave still take the others. Each probe's fetches wait on its column's base
  height (`uGiBase`), a dependent chain.
- **Open:** one `textureGather` for the four base heights (a bases texture per cascade, repeating); or the probes resolved once per pixel
  at a lower resolution in a pass of their own, which the shading then reads with one fetch.

*From the code:*
- **No probes in the water reflection.** The reflection's draws keep the sky's flat ambient: `GiProbes.InReflection` is set around the
  reflection pass, and the frame block is re-read per native segment, so `uGiParams.x` is 0 there. The bench's `gi-reflection` switch
  (side B) reads the probes in the reflection again.
- **Foliage without the visibility test.** The foliage mesh and grass fragments define `GI_NO_VISIBILITY`: 8 fetches per cascade instead
  of 16, over the foliage's overdraw. Leaking light through walls matters little on leaves.
- **Sleeping probes.** The blend marks a probe whose rays found no surface within reach (the cell's diagonal plus the lookup's offset,
  0.3 of the smaller spacing; back faces count as near) as asleep: state w 2 instead of 1. A sleeping probe at the same place is traced
  and blended only every 8th of its updates (`SleepPeriod`; `rayLength.z` in the trace and blend parameters), with a new random rotation,
  to find out whether something came near; on the other rounds its workgroup returns at once and its tiles keep their values. No pixel
  and no bounce weights such a probe, by construction. The bench's `gi-sleep` switch (side B) traces every probe. **Open:** a thin
  surface between the rays can be missed until the next check.
- **Blend of the inner texels only.** The blend computes the 8 × 8 irradiance and 16 × 16 distance texels (one and four per invocation,
  instead of two and six rounds over the bordered 10 × 10 and 18 × 18 tiles), keeps them in shared memory, and then writes every texel,
  the borders copying their inner texel. cos^50 is five multiplications instead of `pow`.
- **Picture** (Squin at 13:00, the canyon street, 1920 × 1080, against the shot before these changes): mean difference 0.08, max 9.
  Vulkan validation: 0 errors.

**Observed**, in pictures:
- Hub at midday: the change is subtle on open ground. On objects, the blue sky ambient turns into warmer bounce light (mean difference 1.8).
- Low sun (`7.5`) in an alley: the shade is much darker and lit by warm bounce light (mean difference 21).
- **Open:** the overall level may be a little dark (energy lost through the constant object albedo, and no foliage in the scene).

## Probes in the fog (2026-10-11, `Gi/GiFogHide.cs`)

Meitou's own optimisation; the picture is meant not to change. A probe only matters through the surfaces that read it, and where the fog block the
eye is in hides everything near it, no such surface is visible. `GiProbes.Update` takes the frame's `FogVolumes` and works out, per probe column,
how many of its lowest probes are hidden; those are traced and blended on one of every `HiddenPeriod` (16) of their own rounds instead of every
round. `--ab gi-fog` (side B: every probe at its normal rate), `MEITOU_GI_HIDDEN_PERIOD=<n>` sets the default (0 off).

- **Rule** (*from the code*). A lookup (`giCascade`) moves its point off the surface by at most 0.3 of the smaller spacing, then reads the cells
  around it, so a probe is read from points within one spacing across (plus that offset) and, up and down, from `base + (l - 0.5) sy` to
  `base + (l + 1.5) sy` (the bottom layer from below too, the top layer from above too: the layer index is clamped). `GiFogHide.ReachBox` is that
  box for a column's lowest L layers, from the cull block's floor up. A column's hidden layers are the largest L < 8 for which `FogVolumes.Covers`
  holds for the box: wholly inside the block's box and seven planes and no nearer to the eye than the hide distance, or past the weather fog's
  distance. That is the test the terrain, object and foliage culls use (docs/formats/fogfeatures.md "In Meitou"), so the rule is no looser than
  those. The top layer is never hidden. Bounces read the same lookup, so a hit surface in a hidden box reads hidden probes too.
- **Solved rarely.** The hidden layers are worked out again when the grid scrolls, the block changes, the eye has moved 96 units, or a hide
  distance has grown by more than the margin left of the boxes' widening (the boxes are widened by twice 96, so the cached answer holds for an eye that
  moved up to 96 and a distance that grew by 192 less the move). A solve is 0.08 ms on the render thread (the swamp, 2048 columns, most rejected by
  a distance test in x and z); orbiting at Shark it runs every third frame.
- **The list.** The CPU writes the frame's probe indices into a per-slot buffer (`GiFogHide.List`, bit 31 marks a slow update) and the trace and
  blend dispatch one workgroup per entry (`probe = list[gid]` instead of `gid * phases + phase`). With nothing hidden the list is exactly the old
  enumeration (`GiFogHideTests`), so clear views are unchanged by construction. The blend gives a slow update the hysteresis of 16 updates in one
  (`pow(h, 16)`); a probe that moved starts over as before.
- **Hidden probes are not frozen and not invalid**: they keep their last irradiance, valid, and are refreshed every 16th round (64 frames at the
  default four phases). The round is keyed on the probe's tile, as the sleep rule is (`giAsleep`, every 8th round), so with a period that is a multiple
  of 8 a hidden sleeper is listed only on rounds where it is awake. (The first version keyed it on the window index: a probe both hidden and asleep was
  then mostly listed on rounds where the trace returned at once, and its tile went stale; that was the max 22 in the first fly-through picture check.)
  The price is that the updates come in bursts, a quarter of the hidden probes on four rounds of the sixteen (about 220 extra probes on a
  burst round of a list of 3400). Bounces from visible probes that land on a hidden surface read them. A probe that comes out of the hidden zone has
  at most that much stale light until its next normal update; a probe whose position changed (a column that scrolled in) is invalid until its first
  update, which comes within 16 rounds, long before the eye is near enough (the zone's inner edge is at least the hide distance away).
- **Hysteresis of a slow update**: `pow(0.97^4, 16)` = 0.14, so a slow update is mostly its own 64 rays (the sleepers keep the normal 0.885 per update).
  Reason: a hidden probe's light is 16 times older than a normal one's, so it should track the light 16 times as fast per update to reach the same time
  constant in frames. The cost is that it is noisier (about a single sample, 64 rays), and bounces off hidden surfaces read that noise. Not
  compared against the normal hysteresis in a picture (**Unknown**: the pictures below show no difference at all with this one).
- **The water reflection** reads the probes only with the `gi-reflection` switch's side B; its mirrored camera is not the main view's, so the
  hidden set (the main camera's) does not hold for it. Off by default.

**Where the GI time goes** (**Observed**, 2026-10-11, RTX 4070, Shark in the swamp rain, `--world --camera-code
018C6C25C7309B75C4723B5747D78075406DF4113E00A00C46 --radius 2 --size 1920x1080 --upscaler dlss --render-scale native --fly-benchmark 400 --fly-speed 0` with
`MEITOU_PASS_STATS=1 MEITOU_PASS_STATS_SKIP=80`; the eye is a little lower than the camera code's, and another viewer shared the GPU, so +-0.03 ms; GPU ms): whole GI
0.54 = scene 0.16 + trace 0.27 + blend 0.11, the resolve and apply 0.10 more.

| Experiment (same command, one run each) | scene | trace | blend |
| --- | --- | --- | --- |
| as it is (`MEITOU_GI_HIDDEN_PERIOD=0`) | 0.16 | 0.27 | 0.11 |
| no objects or foliage in the traced scene (`--gi-range 100 --gi-foliage 0`) | 0.04 | 0.07 | 0.09 |
| `--gi-range 3000`, 6000 (default) | 0.16, 0.16 | 0.28, 0.27 | 0.11, 0.11 |
| cascade 0 only (cascade 1 not listed) | 0.20 | 0.19 | 0.07 |
| cascade 1 only | 0.16 | 0.13 | 0.05 |
| rays of 1000 and 2000 (cascade 0, 1; picture changes) | 0.16 | 0.21 | 0.11 |
| rays of 4000 and 4500 | 0.15 | 0.26 | 0.10 |
| hidden probes never updated (`MEITOU_GI_HIDDEN_PERIOD=100000`) | 0.15 | 0.25 | 0.08 |

(The ray-length and cascade rows came from throw-away switches, not kept.)

- **What the fog makes invisible here is a small part of it.** At this camera the hide distance is 4455 and the block hides 3574 of the 16384
  probes (all in cascade 1: cascade 0 reaches 2896 from the eye at most); the other 12.8 k are read by pixels or bounces within the fog's reach.
  The scene's 765 instances (384 foliage) all lie within 4000 units (objects within 3000), so the fog hides none of the structure either. The trace's
  cost is the objects and foliage in the structure (0.2 of its 0.27 ms: the rays traverse many instances), which are all near and visible.
- **Rays: not shortened.** Cutting the rays to what the fog leaves visible barely changes the trace (0.27 to 0.26 at 4000 / 4500, 0.21 at 1000 /
  2000: the cost is the traversal of nearby instances and the hit shading, not the length), and it is not conservative: a hit on a hidden wall is
  light that reaches a visible probe, which would then see the sky's ambient instead. Not built.
- **The traced scene: not cut.** For the same reason the instances beyond the hide distance are not dropped (there are none here to drop).
- **The GI resolve: not built.** Its pixels beyond the hide distance would be skipped by a per-point test (the position it already rebuilds, inside
  the block's planes, past the hide distance). Measured by painting the flagged pixels past 4455 in the resolve's apply: none at this camera (the
  farthest hills are at 1500 to 4000; a test at 1500 painted the hills and the horizon band). The resolve and apply cost 0.10 ms and would save a few
  percent of that where fog hides far surfaces; not worth the plumbing of the block's planes into the legacy post programs yet.

**Measured** (**Observed**, 2026-10-11, RTX 4070, `--ab gi-fog --ab-period 4 --bench-frames 3072`, other options as above; delta = A - B; a second viewer was
on the GPU at times, so read the deltas, not the absolute times):

| View | GI stage (GPU) | GPU total | Frame | Render thread, `gi` |
| --- | --- | --- | --- | --- |
| Shark camera code, still | -0.06 (+-0.00) | -0.13 (+-0.01) | -0.12 (+-0.03) | +0.01 (+-0.00) |
| the same, `--bench-motion orbit` (3 px a frame at distance 9000: the eye moves 135 units a frame, so the layers are solved every frame, and it leaves the block's reach for part of the circle) | -0.02 (+-0.01) | -0.09 (+-0.04) | +0.02 (+-0.08) | +0.05 (+-0.01) |
| `--view swamp-rain` (orbiting Shark at 2000), two runs | -0.04, -0.04 (+-0.01) | -0.10, -0.10 (+-0.03) | -0.17, -0.11 (+-0.09, +-0.04) | +0.03, +0.02 (+-0.01) |
| `--view hub` (no fog block, nothing hidden, the lists are equal: the harness's own bias) | -0.02 (+-0.01) | -0.06 | -0.05 | 0.00 |

So the GI stage gets about 0.04 to 0.06 ms cheaper (of 0.5 to 0.65) where the swamp fog is on and the eye is inside its reach, 8 to 12 %; the Hub row shows the method reads -0.02 with identical lists, so the true figure may be nearer -0.04. The
listing costs +0.01 ms of render thread with the eye still and 0.02 to 0.03 at walking speed (a solve every third frame); at 135 units a frame it is +0.05.

**Pictures** (**Observed**, `--screenshot --upscaler off --no-particles` at 1920 x 1080 so that nothing else moves; two shots of one build are identical, mean
0.0000): hidden probes at 16 against off: mean 0.0000, max 1 of 255 at the Shark camera. After flying through the zone (`--fly-to` three arrival points, 3000 to 6000
units away, one inside the fog and two where the block does not hide anything any more): max 1, 0 and 0. The Hub and the desert (`--world --town Heft --radius 2 --distance 3000 --pitch 10 --yaw 300
--time 12`): 0 and 1 against off, and 0 and 1 for two shots of the same build. The harness's own A/B stills cannot show this: two stills of one build differ in 650 k pixels (rain and
lamps), the same as A against B. **Popping**: none seen in those pictures; the stale light is the light of up to 64 frames ago, and the sun and the lamps hardly change in that
time. A fast time of day (`--time-speed`) or a flight so fast that the zone's inner edge reaches a probe before its next update would show it first. **Unknown**: lamp flicker
in the hidden zone (not looked at).

## Cost of the GI stage (2026-10-11, `perf-gi-cost`)

Meitou's own optimisations; the picture is meant not to change. **Observed**, RTX 4070, Shark in the swamp rain (`--world --camera-code
018C6C25C7309B75C4723B5747D78075406DF4113E00A00C46 --radius 2 --size 1920x1080 --upscaler dlss --render-scale native`), master fa5174a. Sizes at the
start: 2 cascades of 32 x 8 x 32 probes = 16384 probes (128 and 512 apart), a quarter of them each frame (4096, fewer with the sleepers and the fog-hidden
ones), 64 rays each (at most 262144 rays a frame), a traced scene of about 765 instances (375 objects, 384 foliage, the terrain grids).

**Where the time went** (serialised pass meter, `MEITOU_PASS_STATS=1 MEITOU_PASS_STATS_SKIP=80 ... --fly-benchmark 300 --fly-speed 0`, three runs after a warm-up
run: the first run after a pause shows the card at idle clocks, trace 0.38 and resolve 0.7, so never read a first run; the rows are the `gi` stage's parts, GPU ms):

| Part | Before | After |
| --- | --- | --- |
| `gi scene barrier` (the full barrier before the builds, with the frame's uploads) | 0.01 | (no segment is recorded when nothing is built) |
| `gi scene blas` (terrain grids and new object structures: none once settled) | 0.01 | 0 |
| `gi scene` = the top level's rebuild, 765 instances | 0.13 to 0.14 | 0.01 (the barrier after it) |
| `gi probes barrier` (the full barrier that starts the probe passes) | 0.01 | 0.01 |
| `gi trace` | 0.22 to 0.23 | 0.14 |
| `gi blend` | 0.07 to 0.08 | 0.04 to 0.05 |
| **`gi` stage** | **0.44 to 0.46** | **0.20 to 0.21** |
| `gi resolve` (the resolve and the apply, the `post` section `gi`; not changed) | 0.09 | 0.09 to 0.10 |

The pass meter splits the scene segment in three (`GiScene`: after the barrier, before the top level) and the probes' one after its barrier, only while the
meter is on (`StageClock.OnClose`); without it the native lists are as they were.

**What the trace's time is** (`--ab` of throw-away switches that skipped one part of the trace shader at a time, still, 3072 frames, period 4, GPU `gi` stage A - B,
so the cost of the part; none kept; at 64 rays unless noted):

| Part skipped | Cost |
| --- | --- |
| the leaf alpha test (`solid()`: the indices, three vertex uvs and the map's alpha, for every candidate hit on a leaf card; the candidates are still stepped through) | +0.08 to +0.09 (+0.05 at 32 rays) |
| the sun's shadow ray from each hit | +0.03 (+0.02) |
| all the shading of the hits (the rays still traced) | +0.07 (+0.03) |
| the probes' bounce lookup (`giIrradiance` at the hits) | 0.00 |
| the lamps at the hits | -0.01 to 0.00 |
| the object's diffuse map at the hit / the terrain's ground colour map | 0.00 / -0.02 (nothing) |
| foliage out of the primary rays: cascade 0 / cascade 1 | +0.01 to +0.02 / +0.01 |
| foliage out of the shadow rays | +0.02 to +0.03 |
| objects out of the primary rays | 0.00 |
| the alpha tested at mip 4 instead of 2 | -0.02 (nothing: the fetch is not what costs) |
| leaves taken as solid past 800 units without the test | 0.00 (the cost is in the candidates near the probe, in the canopies) |
| every other lane of a probe's workgroup returns at once (half the rays, the same warps) | +0.07 (a warp costs what it cost, so fewer lanes save little) |

- **Observed:** the traversal itself is two thirds of the trace (0.15 of 0.22 at 64 rays), the hit shading a third. The foliage's cut-out leaves are the largest
  part that can be told apart (about a fifth of the stage after the changes below), and it is not the texture: the candidate steps and their dependent loads are.
- The alpha test can only be made cheaper by changing the scene (fewer or trimmed leaf cards, `--gi-foliage` above 150, a coverage per part instead of the test),
  none of which keeps the picture; **Open**.

**Changes** (each a Meitou switch, default on; `--ab <name>`, side A the change; the deltas are each against the state with the ones before it on, GPU `gi` stage / GPU total,
`--ab-period 4 --bench-frames 3072`, the Shark camera unless noted):

| Switch (env to turn it off) | Still | Orbit (135 units a frame) | Hub, still | Walking, 4 and 16 units a frame |
| --- | --- | --- | --- | --- |
| `gi-top-reuse` (`MEITOU_GI_TOP_REUSE=0`) | -0.12 / -0.14 | -0.01 / -0.06 | -0.08 / -0.09 | -0.10 / -0.16 and -0.07 / -0.15 |
| `gi-rays` (`MEITOU_GI_RAYS=64`) | -0.10 / -0.13 | -0.10 / -0.08 | -0.06 / -0.08 | |
| `gi-far` (`MEITOU_GI_FAR_PERIOD=1`) | -0.03 / -0.08 | -0.04 / -0.05 | -0.02 / -0.06 | |
| `gi-top-trace` (`MEITOU_GI_TOP_TRACE=0`) | -0.02 / -0.07 | -0.01 / -0.04 | -0.01 / -0.03 | |

- **`gi-top-reuse`** (`GiScene`): the top level is rebuilt only when the frame's instances are not, byte for byte, those it was last built over (a copy per frame slot,
  49 KB, compared with `SequenceEqual`; an equal array is an equal structure, `InstanceRecord` has no padding). Three things make them equal for longer:
  the origin follows the eye only once it is 1024 units away (it was rounded to 64 every frame, so any walk changed every transform); the objects are
  selected around the eye snapped to a 128 grid with the range widened by 111 (the set changed whenever an object crossed the 6000 range, about every third frame
  at walking speed; every object within the range of the true eye is still in); and a frame that builds nothing no longer records the scene's native list at all
  (it is opened on first use: `GiScene.Open`). The foliage block was cached already. Costs: floats at up to 1024 + 6000 units from the origin, as before at 6000 (their precision at
  8192 is 0.001); an object the eye is up to 111 units past the range edge of is kept. Side B of the switch is the old behaviour exactly.
  In the orbit the eye moves 135 units a frame and the structure is rebuilt as before (no gain); the Hub gains less because it has fewer instances to build.
- **`gi-rays`**: 32 rays per probe instead of 64, so the trace's workgroup is one warp (the shaders are made per ray count, `GiShaders.ProbeTrace(rays)`, `GiProbes.Variant`),
  and the hysteresis is scaled so that the noise stays that of 64 rays: an exponential average of independent n-ray samples has variance (1 - h) / (1 + h) / n of one ray's, so
  (1 - h) / (1 + h) is multiplied by 32 / 64 (0.97 a frame becomes 0.985, 0.885 an update becomes 0.941). The cost is a probe that takes twice as many frames to settle (about 65 frames
  to 95 % at the default four phases instead of 33, so the probes follow the sun and the lamps' flicker half as fast) and a probe that starts over (a column that scrolled in,
  at the grid's far edge) is one 32-ray sample at first. Trace 0.23 to 0.15, blend 0.08 to 0.05 (the pass meter), the stage -0.10 in paired runs.
- **`gi-far`**: the probes of cascade 1 are listed on every second one of their rounds (every 8th frame), `GiFogHide.FarBit` in the list entry, the blend giving them the hysteresis of two updates
  in one (`pow(h, 2)`; the same slow update as the fog-hidden probes have at 16). The sleep rule is keyed on the tile as before, so a far sleeper is still checked every
  8th of its updates, on a round where it is listed. A fog-hidden probe keeps its own rule. The far probes light only the pixels and bounces beyond cascade 0's reach, about 2000 units out.
- **`gi-top-trace`**: the top level is built to trace fast instead of to build fast (`AccelerationStructure.RebuildTop(fastTrace)`; the storage is sized for both). With the reuse it is built rarely,
  and in the orbit, where it is built every frame, the stage is unchanged (the build costs what the trace gains).

**Together** (**Observed**, the same binary with the four env switches off against on, two runs each interleaved, 3072 frames, GPU ms, mean):

| View | `gi` stage before | after | GPU total | Frame |
| --- | --- | --- | --- | --- |
| Shark, still | 0.50 | 0.26 | -0.22, -0.24 | -0.24, -0.33 |
| Shark, orbit (`--bench-motion orbit`) | 0.64 to 0.65 | 0.54 to 0.56 | -0.09, +0.11 (a run with a stall) | -0.10, +0.37 (the same) |
| Hub, still (`--view hub`) | 0.34 | 0.20 | -0.14, -0.13 | -0.14 (one run had a stall) |
| Shark, walking 8 units a frame (`--bench-motion fly --fly-speed 8`) | 0.56 | 0.32 | -0.23, -0.23 | -0.24, -0.23 |

Render thread, Shark still: the `gi` stage 0.22 to 0.16 ms (the objects are no longer walked into a new list and a native list recorded every frame; the other rows of
the render-thread table differ by a tenth of a ms between the separate processes, so read the GPU rows).
The whole GI at Shark, still, is 0.55 ms before (stage 0.45 + resolve 0.09) and 0.30 after. The GPU total moves less than the stage in the orbit because the other stages overlap it.

**Pictures** (`--upscaler off --no-particles --bench-motion still`, `--ab-period 16 --bench-frames 768`; the floor, a switch that does not change the picture, is about 340 to 480 k pixels
at >= 1/255, 2 to 7 k at >= 4, 150 to 900 at >= 12, max 28 to 45): `gi-rays` Shark 476 k / 7.9 k / 892, max 33 and Hub 378 k / 3.2 k / 351, max 45; at 22:00 Hub 431 k / 1.9 k / 155, max 31
and Shark 516 k / 6.9 k / 899, max 30; `gi-far` Shark 385 k / 5.7 k / 518, max 28, Hub 398 k / 3.4 k / 385, max 45, at 22:00 431 k / 2.0 k / 174; `gi-top-reuse` Shark 384 k / 5.6 k / 497, Hub
341 k / 3.2 k / 351; `gi-top-trace` Shark 411 k / 5.7 k / 536. A control that does change the picture (the sun's shadow ray left out of the bounces) shows 790 k / 84 k / 2651. **The floor of a switch that changes nothing is not a floor for `gi-rays`**: the two stills of an A/B run are 70 frames
of different random ray rotations apart, so they differ by the probes' own temporal noise, which a no-op switch (`gi-top-trace`) measures for each build: at Shark with 64 rays (`MEITOU_GI_RAYS=64`) 494 k / 7.9 k / 851 >= 12, max 33,
with the default 32 rays 411 k / 5.7 k / 536, max 28; at the Hub 413 k / 4.3 k / 396, max 42 against 422 k / 3.7 k / 375, max 45. So two stills of the new build differ **less** than two of the old (the longer memory), and the `gi-rays`
A against B at Shark (476 k / 7.9 k / 892, max 33) is exactly the old build's own frame-to-frame difference. The diff of `gi-rays` at Shark is the rain's dither, the lamps' flicker and that noise.
**Not converged** (`--screenshot --upscaler off --no-particles` of a cold start, 70 frames after the first probe update, Shark, `MEITOU_GI_RAYS=64` against the default; each pair of shots of one build is identical, mean 0.0000, max 0 and 7): the two builds differ by mean 0.379, 0.158 % of the pixels
over 12, max 38, because 32 rays settle in about twice the frames: after a teleport or a load the GI is not at its final value for about a second longer. **Unknown:** the probes' noise in motion (flicker from frame to frame at 32 rays) was measured only as the stills' difference above.
Vulkan validation (`MEITOU_VK_VALIDATION=sync`, a flight at 20 units a frame with the reuse): 0 errors.

**Open:** the alpha test of the leaves (about a fifth of what is left); the resolve (0.09); a fast camera, where the structure is built every frame (the instances change faster than the origin
can lag; a refit does not help, the count changes); fewer rays for cascade 1 (16000 long) than for cascade 0.

## Resolve (`--gi-resolve <n>`, `Gi/GiResolveShaders.cs`, `PostProcess.RunGiResolve`)

*From the code.* The terrain and the buildings no longer read the probes per fragment; the probes are read once per pixel of a target at
1/n of the render size after the scene (default 2; 1 full size; 0 the old per-fragment reads).

- **Surface target.** While the resolve runs, the scene's passes carry a second colour attachment (location 1, RGBA16UI, cleared to 0):
  `kenshiLight` in the terrain and building fragments keeps the sky's ambient and writes the shading normal's octahedral position (16 bits
  each), sqrt(albedo) (8 bits each) and a flag. The foliage, grass, characters and impostors keep reading the probes themselves and write
  0 (`GI_IN_SHADER`), as does the water; the sky leaves the clear. Every lit fragment shader is wrapped (`WithAlbedo`): its `main` renamed
  and a new one that writes the output after it. A pipeline writes the attachment only when its fragment shader declares location 1
  (SPIR-V reflection), never blended. The GPU layer's `AttachmentFormats`, `RenderingDesc`, `PassTargets` and secondary inheritance take
  the optional second attachment.
- **Resolve.** Per low texel: the first pixel of its block with the flag, its position from the near or far slice's depth, the stored
  normal, then `giIrradiance` there and the sky's ambient as the fragment had it. Stored: (probes − sky) × the GI share × the strength × the
  haze's transmittance (the haze is linear in the colour, so `atmoApply(1) − atmoApply(0)`), and the view depth.
- **Apply.** Per flagged pixel: the four nearest low texels, bilinear weights divided by the relative depth difference, times the albedo,
  added to the scene colour (red, green and blue). It runs after the slices and before the fog volumes, SSAO and the upscaler.
- **Lost against the per-fragment reads:** the probes' specular occlusion on these surfaces (the sky's reflection is not dimmed in
  occluded corners), and the light of the probes under water in the Meitou water's refraction (the refraction copies the scene first).

**Measured** (2026-10-09, RTX 4070, swamp, DLSS 0.67, 1920 × 1080, 512 paired frames):

| | Frame | Terrain | Objects | Resolve + apply |
| --- | --- | --- | --- | --- |
| `--ab gi-resolve` (half against per-fragment) | −0.19 ms (163.6 / 158.5 fps) | −0.10 | −0.10 | 0.07 |
| `--ab gi-scene` (whole GI, with the resolve) | +0.62 ms (168.5 / 188.3 fps; it was +0.94) | +0.03 | 0.00 | 0.06 |

- **Pictures** against the per-fragment reads (the five views of "Terrain and object reads"): full size means 0.1–1.4, max 31; half size
  means 0.3–1.7, max 65 (Mongrel; the block's one normal at edges). The first version took its normal from the depth: means up to 2.5 and
  the terrain's and rocks' facets showed in the shade; hence the stored normal.
- **Parity:** with GI off, and with `--gi --faithful gi`, mean 0.0000 against `7114e08` (Hub and swamp, 1280 × 720). Vulkan validation:
  0 errors.

## Open

- **Resolve:** specular occlusion for the resolved surfaces (a spare 7 bits of the surface target could carry the fragment's
  environment specular); foliage in the resolve (its pixels' normals are noisy; it reads without the visibility test now).

- **Terrain resolution.** The fine grid matches the drawn terrain only roughly (16 against the fine window's spacing). Building it from
  the streamed fine window directly would remove the culling workaround.
- **Compaction** of the object structures, if the memory grows with longer ranges.
- **Probe update cost.** The fixed part (full barriers; the update could run on an async compute queue beside the shadow pass) and
  a cheaper bounce lookup in the trace (no visibility test at hits).
- **Probe relocation.** DDGI moves probes out of geometry instead of only marking them invalid. In towns many probes sit inside walls.
