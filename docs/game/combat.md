# Combat

Original research: CharStats/CombatClass/MedicalSystem/ranged controller, FCS records and binary
constants. Tables/formulas/evidence scopes retained below; [stats](character-stats.md) owns medical
state/XP, [animation](../animation.md) owns layers/root motion. Implementation: Gameplay.Combat,
Simulation.Combat and Animation; approximate engine scheduling is not original parity.

## Weapons and damage

**Observed** condition-adjusted weapon skill/strength/dexterity, stat scale m:
cut=lerp((skill+dex*m)*cutMult/200,cutBase,cut99);
blunt=lerp((0.5*skill+1.5*str*m)*bluntMult/200,bluntBase,blunt99), interpolation unclamped;
pierce=0.78*(skill+dex*m)*pierceMult+10*pierceMult. Stored cut/blunt endpoints13/52;
blunt-dominant cutBase=0.4*blunt1; other bluntBase not decoded. Packet cut/blunt/pierce/stun0,
bleed and penetration then receives target-kind/race/general multipliers. Manufacturer/quality,
weight/strength and skill speed chains are separate from this packet.

Unarmed category5: martial fraction lerp(skill/100,0.25,1); damage combines toughness/strength/+10,
min1; blunt fraction lerp(skill/100,1,0.4), remainder cut, no pierce. Exact term shapes uncertain.
**Verified** unarmed CONST0.8 binding. Stumble threshold toughness*m*0.52 plus state eligibility.

## Techniques, block and dodge

Technique direction/category/weapon/state/reach/animation flags gate selection; attack versus
defence/dodge skill includes injuries/equipment. **Observed** outcome: block5 if matching block
past50% animation; dodge0 within progress0.1..0.98; otherwise hit2. Skill roll r1*A<r2*B,
A=1+0.05*max(0,s2-s1), B=1+0.05*max(0,s1-s2). Derived chance with k=B/A is1-1/(2k) ifk>=1,
elsek/2. General skill blend0.01*(0.6*stat+0.4*dexterity*m) feeds dodge/reload, with stat/gear
and injury variants. Technique power/max-encumbrance and some length/selection details unknown.

## Armour and medical hit

Coverage rolls, slot order, armour class/material quality and cut/blunt resist/efficiency affect
packet before a weighted body-part hit. Armour absorption/penetration and cut-to-blunt conversion
are distinct; never collapse protection into one damage multiplier. Apply blunt/cut, wound,
stun, bleed and reaction through MedicalSystem, then XP. Health, bandage, stun, blood and
KO/coma/death thresholds use [character-stats](character-stats.md). Never tick medicine twice.

## Ranged and turrets

Controller phases reload → steady aim → fire; target motion/range, skill/injury, equipment and
line of sight determine timing/spread. Bolts retain launch/direction/velocity/impact/friendly-hit
state and consume ammo charges; do not model hitscan. Turret objects and mounted ammo/reload
branches differ from carried crossbows. Player/NPC targeting and friendly-fire training have
separate eligibility. The retained field/constant tables and expression list specify decoded
values; trajectory, aim timing and obstruction uncertainties remain labelled.

## Validation

Game record/enumeration/constant checks are scoped separately from hand-decoded runtime formulas.
Synthetic determinism tests verify our implementation/order, not original-game equivalence.
Use researched endpoint/coverage/KO/ranged cases and preserve unknown terms rather than assigning
unverified constants. [Simulation](../simulation.md#combat) describes current wiring.

### Class map

| Class (RTTI) | Role |
|---|---|
| `Gear` | item base; `+0x220` quality fraction (level·0.01), `+0x224` level int. **Verified** (ctor `1400d3090`, and `1400ce2d0` lerping by `+0x220`) |
| `Weapon`, `Sword`, `Crossbow` | weapon items; `Sword` is the melee class, `Crossbow` creates the gun data |
| `Armour` | worn armour; loader `140898e90` |
| `CharStats` / `CharStatsAnimal` | stats, skills, per-hit damage maths |
| `MedicalSystem` | body parts, blood, KO, death; per-part `HealthPartStatus` |
| `CombatClass`, `CombatClassAI`, `CombatState`, `AttackState` | per-character combat state |
| `GunClassPersonal`, `GunClassTurret`, `TurretBuilding` | ranged weapons |
| `Task_MeleeAttack`, `Task_FocusedMeleeAttack`, `Task_RangedAttack`, `Task_UseTurret`, `Task_EquipBestWeapon`, `Task_SheatheWeapon`, `Task_StripWeapons`, `Task_CuffTarget`, `Task_MakeTargetStandStill` | AI tasks ([ai-tasks.md](ai-tasks.md#tasktype-to-task-class), [ai.md](ai.md)) |

### Global tuning: the CONSTANTS record

Loaded by `14086b2b0` (from "Initialising GameData", `140870ae0`) into one struct at `142133dd0`, field n at
`+4n`. **Verified** (field names read by the loader against the CONSTANTS record dumped from `gamedata.base`
through a probe, every value in the table below re-read; the "stored" rescalings re-read in the loader; consumers
by data xrefs and by the offsets used in the consuming functions). The loader rescales some values; "stored"
below is what the engine uses.

| Field | File value | Stored / use |
|---|---|---|
| damage multiplier | 0.65 | multiplied into the stored blunt/cut 1/99, pierce multiplier and stumble max |
| blunt damage 1 / 99 | 20 / 80 | blunt base at skill 1 and 99 |
| cut damage 1 / 99 | 20 / 80 | cut base likewise |
| pierce damage multiplier | 1.2 | |
| bow damage 1 / 99 | 1.1 / 1.3 | stored unscaled at fields 8 and 9; `14043c180` scales a crossbow's level-0 pierce ends by `bow damage 1` and its level-1 ends by `bow damage 99` before the quality lerp ([Ranged and turrets](#ranged-and-turrets), **Observed** in the disassembly) |
| stumble damage max | 80 | stumble threshold |
| unarmed damage mult | 0.8 | |
| attack chance factor | 0.05 | hit roll skew |
| base block chance | 70 | |
| block chance increase / reduction per 10 levels | 12 / 15 | stored ×0.1, i.e. 1.2 and 1.5 per level |
| max num attack slots | 1 | enemies that may melee one target (+ RACE `extra attack slots`) |
| damage resistance min / max | -0.65 / 0.65 | toughness resistance range |
| damage resistance randomised | False | present in the file only: the loader never reads it (no such string in the executable) |
| knockout mult 1 / 99 | 3 / 0.75 | KO time scale by toughness |
| knockout time base | 20 | |
| min / max toughness ko point | 10 / 85 | |
| degeneration mult 1 / 99 | 1.7 / 0.03 | wound deterioration by toughness |
| bodypart degeneration rate | 1.5 | |
| immediate blood loss, extra blood loss from bodyparts | 0.2, 1 | |
| bleed rate, bleeding clot rate, blood recovery rate | 0.01, 0.0085, 0.4 | stored ×0.1 |
| stun recovery rate | 1 | |
| blunt permanent organ damage | 0 | fraction of blunt that becomes lasting flesh damage |
| exp gain multiplier | 3 | stored ×0.25 = 0.75 |
| skill diff xp 2x bonus / 0x penalty | 10 / 25 | |
| xp rate toughness, strength, from walking, athletics | 1.33, 0.5, 0.5, 1 | athletics stored ×0.5 |
| XP rate medic 1 / 99 | 15 / 150 | |
| weight strength diff 1x / max, min strength xp mult | 20 / 1, 0.1 | strength XP from carrying |

### Objects

| Object | What | Where |
|---|---|---|
| `GunClass` | the firing data of any gun (29 virtual slots); base of the next two | loader `FUN_14043baa0` |
| `GunClassPersonal` | a crossbow's gun, owned by the `Crossbow` item | loader `FUN_14043c180`, made by the item constructor `FUN_140898c90` |
| `GunClassTurret` | a turret building's gun (GUN_DATA; source file `rangedCombat.cpp`) | `FUN_14043cdb0` |
| ranged controller | one per character (`Character +0x2f0`): skill, target, aim point, reload timer, fire flags | tick `FUN_140438c70`, fire gate `FUN_14043aee0`, reload `FUN_140437ce0`, target choice `FUN_140437f40` / `FUN_1404374d0` |
| `Harpoon` | one bolt or harpoon in flight or stuck: a plain struct, pooled by `HarpoonManager` | spawn `FUN_14043a2f0`, per-frame update `FUN_14043afe0`, flight `FUN_1404380e0`, hit `FUN_140439c90`, landed `FUN_14043acf0`, recycle `FUN_140436c10` |

### Record fields

**Base-game data** (**Verified** by reading `gamedata.base`, `Newwworld.mod`, `Dialogue.mod` and `rebirth.mod`; the counts agree with "Data facts" below): 7 CROSSBOW,
6 GUN_DATA. Every crossbow has `num shots` 1, `aim speed` 1 and the three target multipliers 1.

| Crossbow | pierce min 0/1 | pierce max 0/1 | range 0/1 | reload min 0/1 | reload max 0/1 | deviation 0/1 | perfect skill | shot speed 0/1 | ammo (charges) |
|---|---|---|---|---|---|---|---|---|---|
| Ranger | 20/30 | 30/45 | 600/800 | 3/2 | 4.5/4 | 5/3 | 60 | 400/500 | Bolts [Regulars] (30) |
| Eagle's Cross | 50/60 | 70/100 | 1500/1900 | 5/4 | 10/8 | 2.5/1.5 | 80 | 700/800 | Bolts [Longs] (10) |
| Tooth Pick | 10/15 | 20/25 | 375/500 | 2/1.25 | 3/3 | 11/6 | 90 | 350/450 | Bolts [Toothpicks] (40) |
| Junkbow | 10/15 | 20/24 | 300/400 | 3/2 | 4/3 | 12/6 | 90 | 350/450 | Bolts [Toothpicks] (40) |
| Oldworld Bow MkI | 20/35 | 35/50 | 800/1100 | 3.5/2.5 | 4.5/4 | 4/2.5 | 70 | 500/600 | Bolts [Regulars] (30) |
| Oldworld Bow MkII | 35/45 | 45/60 | 800/1200 | 4/2.2 | 8/6.5 | 4/2.2 | 80 | 500/600 | Bolts [Regulars] (30) |
| Spring Bat | 30/50 | 40/80 | 400/550 | 4.5/3.5 | 6/8 | 8/4 | 70 | 500/600 | Bolts [Heavies] (20) |

### Unknowns of the ranged path

The ranged-or-melee switch; what sets the trigger flag (the time between the decision and the shot); the reload animation against the timer; a character's hit shape
(capsule or ragdoll) and whether downed bodies stop bolts; whether the crossbow's bleed and target multipliers and the armour's `ranged skill mult` do anything; turret
aiming and states; how NPC crossbowmen get their bolts.

## Unknowns

- KO wake-up, flags `+0x162`/`+0x163`, and the roles of hunger and "fed" in recovery; (the `+0x60`/`+0x64` pair is the hunger level and stomach buffer, [character-stats.md](character-stats.md#hunger-and-starvation)); where the KO point (`140643ae0`) is consumed.
- Attacker-side technique choice (which technique an attacker starts, using min/max skill, encumbrance, distance); whether `140887970` tests min/max skill. Attack-slot AI (`14060a9e0` and neighbours), aggression,
  stance and target selection. The bluntBase of non-blunt-dominant weapons; the stat that scales max blood; the part `+0x28` link.
- The role of the class-table columns 4-6 (athletics, combat speed, stealth is an inference) and of the slot-adjusted factor (armour item field index 0x5d) in `140898e90`.
- Turrets (aiming and states; see [Ranged and turrets](#ranged-and-turrets) for what is known and its own unknowns); the meaning of the weight/price-like armour table fields; the remaining difficulty floats beyond the dialog bindings listed above (their consumers).
- Whether the +20 guarding state of the block chance is the state the HUD's Block stance sets ([ui-screens.md](ui-screens.md#4-hud-mainbargui)).
- What the technique fields `power 1/2`, `limb 1/2`, `anim hesitate point`, `max encumbrance` and `max simultaneous hits` do; where `min cut damage mult` x the manufacturer's `min cut damage` is applied (the engine floors the cut at it); the term shapes of the fist damage (the engine takes half the stat each); how a "Heavy_Hit" works (the engine: 0.5 s helpless for a damage sum above the stumble threshold). The engine's answers are in [simulation.md](../simulation.md#combat).
- Whether the decompiled maths matches in-game numbers: nothing has been compared in play.

## Expression reference

- Combat: `lerp(t, a, b) = a + (b - a)·t`

## Evidence index

Evidence scopes below apply only to their named sources/checks.

- Turrets (secondary; **Observed** from `GunClassTurret` and the tooltip `FUN_14030d620`)
- AI (**Observed**)
