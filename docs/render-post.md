# Post-processing

`PostProcess*.cs`, `PostProcessShaders`, upscalers and `WorldFrame` own the HDR chain.
Original exposure/FXAA/haze rules: [formats/post-processing.md](formats/post-processing.md),
[sky](formats/sky.md), [weather](formats/weather.md). UI/CLI controls: [viewer.md](viewer.md).

## Frame contract

Scene shaders write unclamped HDR. Colour/depth/motion targets belong to PostProcess; scenes
must preserve requested attachments and near/far depth history. Scene rendering is at render
scale; reconstruction precedes display-sized composite/exposure/grading. Single sampling with
TAA/FSR/DLSS; jitter scene/sky, never shadow maps or reused reflections. Fade/dither/noise must
agree across colour/depth/velocity passes. Transparent layers do not overwrite opaque depth.

Faithful uses the game's FXAA, exposure, haze and flat ambient where selected. Meitou switches
independently select SSAO, TAA/upscalers, tone/colour/night grading, particle quality and GI.
Do not clamp before HDR lighting/composite. FSR/DLSS mip bias=log2(scale)-1; TAA=log2(scale)-0.5.
Motion uses previous camera/depth plus moving-object/grass paths; near/far depth reconstruction
must use the corresponding projection. `sun-motion` adds clock-driven sky motion near the sun.
Reset temporal histories after cuts, target resize or incompatible setting changes.

## SSAO, fog and layers

SSAO reads near depth; character mask controls character occlusion (default strength0.25).
`ssao-mask-skip` may omit a mask read only when no character was drawn. Sky/far-depth edges and
transparent water/particles require explicit exclusion/compositing rules.
`layer-merge`, `fog-half`, `shafts-half`, `layers-lean` combine shafts apply, placed fog and SSAO,
using reduced targets plus depth-aware reconstruction/mask fallback. A texel that does not fit
must use the full-resolution path. [Fog volumes](formats/fogfeatures.md), [shafts](render-shafts.md).
`post-merge` fuses velocity/exposure compute groups while preserving ordering and dependencies.

Fog shading rate (`FogShadingRate`): build tile rates from completed scene depth/fog; frame N's
R8_UINT rate image drives frame N+1 opaque scene shading. Requires device fragment-shading-rate
support; absent support uses normal shading. Excludes reflection, shadow, particles and post.
Coarse rates can alter fine cut-outs and temporal output; it is a quality/performance tradeoff.

## Colour, night and particles

Game HDR exposure is the reference; tone mapping/grading are Meitou controls, not original facts.
Night grading weights scotopic luminance (Jensen2001 night-sky model evaluated at sRGB primaries)
using measured scene luminance/adaptation; daylight contribution must fade to zero. Night red
terrain can be original ambient-map colour rather than lamp/emissive input. Haze follows weather
and sun; Meitou default strength0.5, game1, slider0..3.

Weather particle resolution: off/quarter/half/auto/full; density multiplies emission and changes
appearance/population, not just shading cost. Depth-aware reduced-resolution compositing must
preserve opaque intersections. Texture anisotropy caps mipmapped samplers, distinct from texture
resolution/LOD bias. [Particle simulation](formats/particle-universe.md).

## Validation

**Verified** merged-pass checks: Faithful/FXAA and TAA reference images byte-identical in the
named swamp screenshot test; DLSS/FSR outputs require baseline-noise comparison. Other layer,
mask, half-resolution and fog-rate changes are not blanket pixel-parity claims. Compare still
and moving views at matched time/weather/upscaler. [bench.md](bench.md).
**Limits:** near-only SSAO, screen-space information, temporal disocclusion, coarse fog shading,
and lost GI specular occlusion on resolved surfaces; see feature docs for uncertainty.
