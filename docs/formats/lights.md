# Placed lights

Original LIGHT schema, building assembly, deferred light shader and on-state binary research.
Record table/formulas below. Engine WorldLights/WorldLamps: [render-lights](../render-lights.md).

## Instances and lighting

LIGHT records instantiate through building/part/layout transforms with colour/radius/power/spot/
effect parameters and rolled power. Per-instance node/state/power ownership matters; mesh emissive
material is not the same as a lamp. Visible positive-power sphere/cone volumes add diffuse/GGX
in sun-light units; radius clips, NdotL<0.01 skips. Attenuation/spot equation block retained.
PULSE=0.5+0.5sin4t, SHIMMER=0.95+0.05sin30t*sin17t; clock t units unknown. No flicker case found
in the decoded pass, factor1 there; another writer may affect it.

**Verified decompiled** BF_LIGHT15 creates LightBuilding class11, night-only flag, hidden above
daylight0.1 and power requested at night. Other buildings start hidden then follow setLightsOn/
operating/power state, can show day/night. Ordinary town on-state and exact time-unit mapping
remain unknown; saved power-on is passed through. Do not assume every lamp is night-only.
Original pass has no discovered hard count cap; engine spatial selection/budgets are separate.

### The LIGHT record (Verified: fcs.def, the merged base game, the exe's light builder FUN_140815420)

| Field | List | Meaning |
|---|---|---|
| `type` | int | `LightType`: 0 POINT, 1 SPOT. The game makes an Ogre point light (0) or spotlight (2) of it |
| `diffuse` | int 0xRRGGBB | Light colour. Unpacked as bytes / 255 (Ogre `ColourValue::setAsARGB`), used as is in the linear HDR light pass (no sRGB decode) |
| `specular` | int 0xRRGGBB | Not in fcs.def, read by the exe as the specular colour; set only on `Streetlight-Droidtype-OmniLIGHT` (FFFFFF). The light shader declares `specularColour` but does not use it (it uses `diffuseColour` for both terms) |
| `brightness` | float | Base power (Ogre `setPowerScale`); 0.2 to 1 in the base game, 8 and 15 on two effect lights |
| `variance` | float | Each instance's power is `brightness + U(−variance/2, +variance/2)` (below) |
| `radius` | float | The light's reach, Ogre `setAttenuation(radius, 1, 0, 0)`; the shader uses only the range. 25 to 750 (3000 on lightning) |
| `inner`, `outer` | float, degrees | Spot cone full angles (`setSpotlightRange` after degree→radian; the light volume's cone uses tan(outer/2)). Set on point lights too, unused there |
| `falloff` | float | Spot falloff exponent between the cones ("1 linear, >1 soft, <1 hard", fcs.def) |
| `effect` | int | `LightEffect`: 0 NONE, 1 PULSE, 2 FLICKER, 3 SHIMMER (below) |
| `landscape`, `buildings`, `characters` | bool | "Cast shadows" switches. **Dead data**: the light builder always calls `setCastShadows(false)` and no code references the strings (FCS sets all three from the scene's `castShadows`) |

| LIGHT | Type | diffuse | brightness ± variance/2 | radius | inner/outer | effect | Used by |
|---|---|---|---|---|---|---|---|
| `torch_light` | point | FFCE5E | 0.4 | 70 | — | FLICKER | Torch Post, Large Torch Post |
| `HolyStreetTorch` | point | FFCE5E | 0.7 ± 0.05 | 150 | — | FLICKER | Holy Street / Sinner Torch |
| `campfire light` | point | FFCE5E | 0.6 | 140 | — | SHIMMER | Campfire, Cooking Pit |
| `strip_light_blue` | point | 9DEBF9 | 0.8 ± 0.2 | 60 | — | — | Electrical Torch Post |
| `street_light_for lampposts` | spot | A8FFE2 | 0.6 ± 0.125 | 170 | 75/120 | — | City Lamp Post, Light Post II |
| `LIGHT` (14380-Newwworld) | spot | CEFDF0 | 0.35 | 100 | 74.7/120 | — | Quad Streetlamp (4 per lamp) |
| `indoor_lamp_light` | spot | E9FF9B | 0.7 ± 0.1 | 120 | 90/100 | — | Ceiling Lamp, Ceiling Fan Lamp |
| `street_light_yellow` | spot | F1E8B4 | 0.6 ± 0.15 | 300 | 40/90 | — | Spotlight, Wall Spotlight |
| `spotlight powerful` | spot | F1FFC4 | 1 ± 0.1 | 750 | 60/80 | — | Searchlight |

### Shading (Verified: `deferred.hlsl` `light_fs`, `CalcPunctualLight`, the light pass FUN_1402d8540) — layout/example

```
x      = saturate(d / radius)
atten  = x < 0.649 ? 0.2 + 0.8 (1 − x)³
       : x < 0.8   ? 0.242 − 3 (x − 0.6)²
       :             3 (x − 1)²                       1 at the light, 0.30 at R/2, 0.235 at 0.649 R, 0.12 at 0.8 R, 0 at R
spot   = saturate(((L·dir − spot.y) / (spot.x − spot.y))^spot.z)    spot lights only
colour = (π · N·L · diffuse · Fresnel-diffuse · albedo + GGX specular(diffuse)) · atten · spot · power
power  = powerScale · global (1.0) · effect
```

## Expression reference

- Shading: ` x = saturate(d / radius) atten = x < 0.649 ? 0.2 + 0.8 (1 − x)³ : x < 0.8 ? 0.242 − 3 (x − 0.6)² : 3 (x − 1)² 1 at the light, 0.30 at R/2, 0.235 at 0.649 R, 0.12 at 0.8 R, 0 at R spot = saturate(((L·dir − spot.y) / (spot.x − spot.y))^spot.z) spot lights only colour = (π · N·L · diffuse · Fresnel-diffuse · albedo + GGX specular(diffuse)) · atten · spot · power power = powerScale · global (1.0) · effect `

## Evidence index

Evidence scopes below apply only to their named sources/checks.

- The LIGHT record (Verified: fcs.def, the merged base game, the exe's light builder FUN_140815420)
- Counts (Observed, `WorldLights.ForWorld`, 2026-10-09)
- Shading (Verified: `deferred.hlsl` `light_fs`, `CalcPunctualLight`, the light pass FUN_1402d8540)
- When a light is on (Verified (decompiled) unless marked)
