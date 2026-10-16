# Characters and appearances

Original race/loadout/appearance/body/attachment/material research from FCS, .bod2/.phs assets,
binary functions and shaders. Assembly: CharacterGenerator/CharacterAppearance/CharacterShape;
rendering: [character-renderer](character-renderer.md), animation: [animation](animation.md).

## Assembly and generation

Resolve race, sex, body/head/hair/beard, body sliders and clothing/weapon/backpack from record
references and .bod2 overrides. Keep generated appearance/loadout seed separate from per-frame
live stats. Record lists' weighted chances, forced equipment, quality/manufacturer rolls and
race restrictions have distinct semantics; missing references do not imply a universal item.
NPC generation order determines random consumption; original RNG sequence is not reproduced.

### Random appearance

**Observed** (`data/editor/editor_data_human.xml`): race/gender limits specify `min`, `max`, optional
`mid`, `random_variation` and `random_group`; male height, leg length and hands share group 0.
The generator rolls `round(mid + variation × (2r − 1))`, sharing `r` within each group/category.
Default variation uses the runtime deviation (raw CONSTANTS ×0.5; **Verified** constants-loader
evidence in [character-stats](game/character-stats.md#constants-used-by-this-subsystem)), not the raw field.
Base-game 0.4 therefore gives a ±20% range around the midpoint: human height 92–108, not 84–116.
Explicit XML `random_variation` remains a percentage of the full slider range. Rolls are not
clamped; **Unknown** whether the original applies a later clamp for extreme modded limits.

**Observed** (Kenshi x64 disassembly: template initialization `@ 140073fb2`, randomizers'
final copies `@ 14007acc2`, `@ 14007bc2d`, `@ 14007bfda`): randomization restores `Posture`
from the appearance template, whose default is 20, rather than retaining the randomized value.
Generated NPCs use 20; authored body files retain their posture.

Parts can have different meshes/skeleton links for male/female/race and slot. Clothing layers,
vests/backpacks, hair-hidden/part-map flags, stumps and missing limbs are not ordinary visibility
checkboxes. Weapon inventory choice, equipped/drawn location and sheath/back/hip attach bone
are separate; .phs attachment points are data. Part scales/transforms must follow skeletal axes.

## Shading, poses and LOD

**Observed HLSL** body/head texture layering uses head UV+(0,1), border-black and body normal.wy.
Skin tone, body normals/muscle/starvation, hair/beard overlays, dye/cloth/cutout and wet/dust inputs
have separate parameters. Material runtime facts: [runtime-materials](formats/runtime-materials.md).
Bone/positional sizes and face morph deltas apply before skinning; meshes with their own LOD or
manual linked LOD need compatible remaps/bone weights. Skin/head/hair colour-space assumptions
remain scoped to tested shader/screenshots, not every asset.

Body/race generation, mesh-per-item and clothing/weapon choice source addresses below retain
traceability. Tests check our factories/readers; screenshots cover named appearances only.

## Body files (`.bod2`)

CHARACTER `body` names a body file ("Leave blank for random body", fcs.def). **Verified** (FcsReader on the
files, `CharacterAppearanceTests`): a `.bod2` is an ordinary FCS file (no header in type 15) holding one
record of type 66, CHARACTER_APPEARANCE, typically named `default` with id `1--INGAME`. Of the 80
CHARACTER records (merged load order) that name a body file, 60 point at type 15 and 4 at type 16 files (read by `AppearanceFile`); 14 at
legacy layouts (types 5, 8, 9, 10, 13: the `.body` files and three `.bod2`), and 2 at files that don't ship
(`Crow Face.bod2`, `Cat.bod2`). Observed fields (Ruka.bod2, Beep.bod2):

| Kind | Fields |
| --- | --- |
| bool | `sex female` |
| float | body and face sliders (`Height`, `Waist`, `Head size`, `Posture`, `Neck position`, `Shoulder set`, `Hair Colour`, `Hair Brightness`, `Hair Saturation`, `Skin Tone`, `Age`...; mostly 0–200 with 100 neutral) and **pose weights keyed by the body mesh's pose names** (`bone_wide_jaw = 0.37`, `stick2_long_horns = -0.6`...) |
| int | `Age` |
| vec3 | `Skin Tone` (RGB, e.g. (0.99, 0.90, 0.90)) |
| string | `head` (HEAD id), `hair style`, `beard` (ATTACHMENT ids, may be empty), `idle stance` (an animation name, e.g. `idle_stand_relax`) |
| reference | `race` (RACE) |

CHARACTER `race` overrides the body file's race (fcs.def "will override race in .body file"; the viewer
follows this, not traced).

### Hidden body parts (Verified, `@ 140075600`, `@ 140071f90`, `@ 140531cc0`; HLSL `skin.hlsl`)

| Colour | RGB | Bit | PartMapColours |
  | --- | --- | --- | --- |
  | White | FFFFFF | 0x1 | White |
  | Red | FF0000 | 0x2 | Red |
  | Green | 00FF00 | 0x4 | Green |
  | Blue | 0000FF | 0x8 | Blue |
  | Yellow | FFFF00 | 0x10 | Yellow |
  | Magenta | FF00FF | 0x20 | Magenta |
  | Cyan | 00FFFF | 0x40 | Cyan |
  | Orange | FF8000 | 0x80 | Orange |
  | Purple | 8000FF | 0x100 | Purple |
  | Teal | 008080 | 0x200 | (not in the enum) |
  | Black | 000000 | none | |

### LOD of character meshes

Bodies, hair and armour use their meshes' own generated LOD levels (no separate LOD meshes); each entity picks
its level on its own with the `distance_sphere` rule ([formats/ogre-mesh.md](formats/ogre-mesh.md#stream-and-compatibility)). The rule now lives in `Meitou.Data.Ogre.MeshLod`; `CharacterLod` forwards to it.
**Verified** (scratch survey with `OgreMeshReader` of the meshes the base game's records name; `CharacterLodTests`
checks `human_male.mesh`):

| Records' meshes | LOD |
| --- | --- |
| RACE `male mesh` / `female mesh` | counted per record field: 2 levels at 200 for 18 male / 11 female (`human_male`, `bone_male`, `robot`, the three `Stick_person`), 2 at 400 for 32 (animals), none for the rest (`human_female`, `bone_female`, ...) |
| ATTACHMENT hair (`haircut_*`) | 2 levels at 200 (81 references); animal fur 400 or none |
| ARMOUR `mesh` / `mesh female` | 2 levels at 400 for 193 references (masks, hats, coats... meshes with no skeleton link), none for the 51 to meshes with a skeleton link (`samurai pants` ...) |
| CONTAINER backpacks | 2 levels at 400 |
| LIMB_REPLACEMENT, WEAPON (`mesh`, `bare sword`, `sheath`) | none |

## Open questions

- ~~Body-shape slider formulas~~, ~~`hide parts` bits~~, ~~`hide stump`~~, ~~`muscleBlend`~~: answered (animation.md
  "Body shape sliders", "Hidden body parts" above). Left: which body region each part-map colour marks, and the
  stats/nutrition fields listed in animation.md's open questions.
- Random appearance details: the untraced part of the morph-face name, the faction flag that re-rolls the skin tone
  (and a body file's gender); crossbow placement; extra hip copies; `RaceLimiter` rules.

## Expression reference

- Random appearance: `gender="Female"`
- Colour space: `hdr_to_gamma = false`

## Evidence index

Evidence scopes below apply only to their named sources/checks.

- Meshes per item (Verified, `@ 140537690`, `@ 1405358a0`)
- Order (Verified, `@ 140581770`)
- Body shading (Observed, HLSL `character.hlsl` main_fs; parameters Verified where noted)
- Hidden body parts (Verified, `@ 140075600`, `@ 140071f90`, `@ 140531cc0`; HLSL `skin.hlsl`)
- Hair meshes (Observed, HLSL; parameters Verified `@ 140532600`)
- Worn clothing (Observed, HLSL `skin.hlsl`; parameters in runtime-materials.md)
- Colour space (Observed)
