# Global illumination

Meitou DDGI implementation: `Gi/GiScene.cs`, `AccelerationStructure.cs`, `GiProbes.cs`,
`GiFogHide.cs`, `GiResolveShaders.cs`, `GiShaders`; PostProcess resolve/apply. Engine choices,
not original-game lighting rules. Original ambient: [formats/lighting.md](formats/lighting.md).

## Device and switch

Ray-query setup is requested by default unless `--no-gi`; unsupported hardware prints GI off.
The `gi` Enhancement/`--faithful gi` selects probe lighting versus unchanged flat ambient.
Require acceleration_structure/ray_query/deferred_host_operations and core buffer-device-address.
RayInput buffers carry address/build-input usage; no copied geometry required. Compile GLSL460
ray-query shaders with explicit descriptor bindings. The feature is optional for device support.

## Scene and lifetime

Fine/coarse terrain:256² quads, spacing16/160 (4096/40960 width), heights sampled on workers.
Object range6000; resident finest mesh parts build bottom levels, max32/frame; unused600 frames
can retire. Foliage radius>=150, range4000, max14336 instances, cached candidates invalidate by
movement/deletion/build progress. Do not trace grass, characters or water as geometry.
Origin tracks eye in64-unit steps with1024-unit hysteresis; lazy scene epochs coordinate commits.
Shared top level fits16384 instances, prefers fast trace, reuses identical instance data.
Terrain refits unchanged topology (periodic full rebuild); retire structures after GPU consumers.

Terrain rays cull back faces; objects/foliage are double-sided. **Verified** RayQueryTests winding.
Structure geometry records and hit-fetch addresses must refresh when mesh buffers are replaced;
source geometry lifetime and built acceleration-structure lifetime differ. Hit records carry
vertex/index addresses/stride/kind plus texture indices/cutout threshold. Alpha-tested foliage
uses candidate-hit coverage; null/unresident textures need valid fallback. Lamps add diffuse
light at hits, enabling bounce lighting.

## Probes

Two toroidal cascades,32x32 columns of8 probes: horizontal/vertical spacing128/96 and512/384,
ray lengths4000/16000. Ground bases use five height samples, updated incrementally on scroll.
Invalidate relocated tiles before sampling; reset history for changed positions.
Irradiance:8² octahedral texels plus border RGBA16F (alpha valid); distance moments:16² plus
border RG16F. Seam borders copy opposite edges.

32 spherical-Fibonacci rays/probe with rotated directions (64 for comparison). Misses sample
sky ambient; hits combine albedo, shadowed sun, lamp light and previous probe irradiance.
Negative backface distances mark interiors; >25% backface rays invalidates a probe.
Cosine-weight irradiance, cosine^50 distance moments; hysteresis adjusts with ray count/cadence.
Four-phase round robin; far probes every second round, fog-hidden probes slower. Preserve the
same effective history when updates slow; a newly invalid tile cannot contribute stale light.

Eight neighbouring probes use trilinear, facing and Chebyshev visibility weights. Near cascade
wins, far fills outside near, sky outside both. GI replaces envDiffuse; per-fragment path also
scales specular ambient by luminance ratio clamped0..1. Reflection probe use is a separate switch.

## Fog and resolve

`GiFogHide` chooses slower updates for probes hidden by placed fog without removing bounce
geometry or blindly shortening rays. Visible indirect light may depend on hidden surfaces.
Resolve divisor2: scene writes flagged normal/albedo into an extra RGBA16UI target; low-res
resolve reconstructs position from near/far depth; full-res apply depth-weights four neighbours.
Unflagged/background surfaces must remain unchanged. Resolved surfaces currently lose the
per-fragment specular-occlusion term. Strong depth/normal discontinuities need rejection.

## Performance and verification

`gi-top-reuse`, `gi-rays`, `gi-far`, `gi-top-trace`, `gi-terrain-refit`, `gi-lazy-scene`,
`gi-top-shared`, `gi-resolve` are A/B probes. Different schedules/ray counts may converge
differently; compare both settled and cold lighting. GPU GI cost includes scene builds,
trace/blend and barriers; resolve/apply may be accounted in post. Fixed camera results do not
predict moving-camera rebuilds. **Observed checks:** zero GI-off mean screenshot
change in Hub/swamp; moving-scene synchronization validation checks structure/address lifetime.

## Open

- **Resolve:** specular occlusion for the resolved surfaces (a spare 7 bits of the surface target could carry the fragment's
  environment specular); foliage in the resolve (its pixels' normals are noisy; it reads without the visibility test now).

- **Terrain resolution.** The fine grid matches the drawn terrain only roughly (16 against the fine window's spacing). Building it from
  the streamed fine window directly would remove the culling workaround.
- **Compaction** of the object structures, if the memory grows with longer ranges.
- **Probe update cost.** The fixed part (full barriers; the update could run on an async compute queue beside the shadow pass) and
  a cheaper bounce lookup in the trace (no visibility test at hits).
- **Probe relocation.** DDGI moves probes out of geometry instead of only marking them invalid. In towns many probes sit inside walls.
