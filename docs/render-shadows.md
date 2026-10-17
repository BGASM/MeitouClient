# Shadows

`ShadowPass`, `ShadowLod`, `ShadowFogCap`, `ShadowZoneBox`; original cascade/settings/shader
rules: [formats/shadows.md](formats/shadows.md). Engine API: [renderer-native](renderer-native.md).

## Modes and resources

Faithful reproduces researched cascade fits/splits/casters/receiver. Meitou fits useful camera
ranges, stable grids and soft filtering, plus terrain-horizon shadow beyond local caster range.
Shadow-quality default2 uses4096²; Tab supports1024/2048/4096. Cascade arrays include a fifth
Meitou cascade; consumers must use current layer count/splits, not assume four tiles everywhere.
Depth-array views, samplers, receiver blocks and frame-global publication must agree.

## Scheduling and casters

Near cascades refresh rapidly; far cascades cache/stagger according to camera/sun changes.
`shadow-spread` budgets one due far cascade per frame; invalidation must not reuse an incompatible
matrix. GPU culls and CPU zone/group work lists are per cascade. `shadow-zone-box` excludes
nonintersecting zone bounds before building lists. Record secondaries in stable caster order.

`shadow-coarse` / generated object levels select geometry against cascade texel size, not colour
pixel size. Preserve silhouette and alpha coverage; impostor casters have their own fade/plane.
`shadow-fogcap` can shorten reach only when placed fog conservatively hides every relevant
receiver. Shadow-map rendering and shadow receiving are distinct costs; omit receiver work only
where lighting cannot contribute. Minimum-radius impostor filter omits blocker search.

## Validation and limits

Check low sun, cutout trees, interiors, cascade boundaries, camera/clock cuts, reflection and
far terrain. Compare redraw spikes/p99 as well as mean; cached passes can hide costs in averages.
**Observed** moving swamp views: far-cascade redraws cause spikes; scheduling/coarser casters can
reduce tails without a significant mean gain. Fog-cap and extra-cascade rules must preserve
clear views and Faithful behaviour. Source formulas/unknowns remain in the format reference.
