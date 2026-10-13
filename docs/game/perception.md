# Perception and fleeing

How `kenshi_x64.exe` lets a character see and hear others, what a squad shares, and when and how characters run away. Labels as in
[../README.md](../README.md): **Verified** (and how), **Observed**, **Unknown**.

Sources: the decompile dump of `kenshi_x64.exe` and Ghidra queries on a copy of the analysed project (disassembly of the hearing test, the
float constants read as bytes, functions Ghidra had not made decompiled after creating them in memory), a raw byte scan of the exe for
references (call, jump, `lea` and 8-byte pointers), and the install's merged game data (`gamedata.base` + `Newwworld.mod` + `Dialogue.mod` +
`rebirth.mod`) read with the repo's `GameDatabase`; all on 2026-10-13, outside the repository. No decompiled text is quoted; the rules are
restated. The game was not run. Related: [ai.md](ai.md) (the task system the senses feed), [ai-tasks.md](ai-tasks.md) (task and state
tables), [game-loop.md](game-loop.md) (the character update budget, the daylight factor), [character-stats.md](character-stats.md) (the
stealth stat), [factions-squads-towns.md](factions-squads-towns.md) (relations), [../formats/weather.md](../formats/weather.md) (weather
effects). What Meitou builds from this: [../simulation.md](../simulation.md#hostility-and-relations).

## Summary

| Figure | Value | Label | Where |
|---|---|---|---|
| Vision range outdoors | 1500 x RACE `vision range mult` (0.5 to 2 in the data; 1 for 41 of 53 races) | **Verified** (literal) | `FUN_140851bc0` |
| Vision range inside a building | 500 (no race multiplier) | **Verified** (literal) | `FUN_140851bc0`, `FUN_140852f70` |
| Night | range x lerp(daylight, 0.5, 1): half at night | **Verified** (literals), daylight factor in [game-loop.md](game-loop.md#the-clock) | `FUN_140851bc0` |
| Weather (dust storm / other affecting weather) | range down to x0.4 / x0.75 at full strength, less with protection | **Observed** | `FUN_140851bc0` |
| View cone | factor v = (cos a - 0.2) / 0.8; nothing beyond a = 78.5 degrees | **Verified** (literals) | `FUN_140852f70` |
| Seen when | v > 0.01 and d <= lerp(light, 0.2 or 0.4, 1) x contrast x R x v | **Observed** (control flow), constants **Verified** | `FUN_140852f70`, `FUN_140857b10` |
| Sneaking / crawling target | v x0.5 crawling; x0.75 or x0.5 sneaking; x lerp(stealth, 1, 0.6) | **Verified** (literals) | `FUN_140852f70` |
| Noise range of a mover | lerp(speed ratio, 350, 600); sneaking lerp(stealth, 450, 80); weather x0.25 / x0.75 | **Verified** (literals) | `FUN_140851d10` |
| Hearing chance per check | 0.75 x (1 - d^2/N^2); sneaking 0.1 (0.05) x (1 - stealth) x ...; x2 if recently alerted | **Verified** (disassembly and bytes) | `FUN_140857500` |
| Squad-shared sightings | a target a mate saw or heard counts as seen within 2 x R | **Observed** | `FUN_140857b10`, `GroupSense` |
| Gather per update | up to 40 objects within R; at 40, up to 20 more within 250 | **Verified** (literals) | `FUN_14085a8d0`, `FUN_1407866d0` |
| Update rate | with the AI, in the logic tick: 8 characters per frame in turn | **Observed** | [game-loop.md](game-loop.md#the-character-update-budget) |
| Run away (Task_Runaway) | 2000 + 400 r away from the threats' centre, then hops of 1000 + 400 r; until no enemy sensed and nobody attacking | **Verified** (literals), flow **Observed** | `FUN_14034b440`, `FUN_140336820` |
| Squad not intact (retreat package) | 4+ members: fewer than 20% (40% with the leader down) able; under 4: leader down | **Observed** | `FUN_14079acf0` |
| FACTION `run away ratio of squad size` / `relative to enemy` | loaded, no reader found | **Unknown** | `FUN_1407fe270` |
| Leash / give-up-chase distance | not found | **Unknown** | - |

## 1. The sense object and when it updates

- Each character's `AI` object holds a sense object at `AI+0x28` (its owner character at sense `+0x148`). `AI::vfunc_4` (`FUN_1405112b0`,
  the per-character AI update of [ai.md](ai.md#goal-loading)) calls the sense update `FUN_14085a8d0` before the task system thinks
  (**Verified**: the call with `AI+0x28` is the only caller of `FUN_14085a8d0`).
- `AI::vfunc_4` is reached from the character logic tick (Character slot 29, `FUN_1405ccd90`, through the object at character `+0x648`,
  its slot 4), which the character update budget runs for **8 characters per frame, round robin** (tier A of
  [game-loop.md](game-loop.md#the-character-update-budget)). So a character's senses refresh once every ceil(N / 8) frames for N
  characters in the world update (**Observed**: the call chain read; the budget itself is game-loop.md's **Observed**).
- The update is skipped once when the flag at sense `+0x1d0` ("sharing" in the debug panel) is set, and the flag is cleared. Only
  `FUN_14085a770` sets it: it copies a squad mate's gather list when the mate is within **200** units (**100** when the character's squad
  has a home town), in the same building and floor, awake. Nothing references `FUN_14085a770` or its thunk `14004bc8b` (byte scan of the exe
  for rel32 calls and jumps, rip-relative `lea` and 8-byte pointers: none), so the sharing never happens (**Observed**: dead code).
- **Gather** (`FUN_14085a8d0` -> `FUN_1407866d0`): a world-grid query round the character with the vision range R of section 2 (the
  "in a building" flag passed is Character slot 35, the floor index, being above 0), keeping at most **40** objects; when 40 were found, a
  second query of radius **250** adds up to **20** more not already listed (**Verified**: the literals 40, 20 and 250.0 in the call). A
  float argument 30.0 is passed too and not used by the decompiled body (**Unknown**).
- **Evaluate** (`FUN_14085a070`): every gathered character other than itself and every remembered entry is tested (`FUN_1408593d0`:
  can see, else can hear); the squad's `GroupSense` (`AI+0x68`) is told what was seen and heard. Per update it counts, in the sense object,
  the characters tagged ENEMY that are up (not down, dead or carried) at `+0x94`, those tagged NEUTRAL that are up at `+0x98`, and keeps the
  squared distance of the nearest enemy at `+0` (**Observed**). An animal-type character (slot 73 non-null) that had no enemy and now has
  one plays `VO_Creature_Alert`.
- A separate short-range list: characters whose 2D distance is below the two body radii + 40 and height difference below 20 are kept
  with their distance (`FUN_14085a070`, **Observed**; probably collision avoidance, not perception).

The debug panel (`FUN_1407981b0`) prints `sharing`, `nearest enemy`, `vision range`, `noise range`, `can hear`, `can see`, the tag
bits and `last seen` per entry.

## 2. Vision range

`FUN_140851bc0(sense, inside)` (**Verified**: literals; **Observed**: the meaning of the inputs):

1. R = **500** when `inside` is set, else **1500 x** the race's `vision range mult` (RACE `+0x74` of the character's race data).
2. R = R x lerp(daylight, **0.5**, **1**): the daylight factor of the sky controller (`FUN_14066cb50`, 0 at night, ramps over 2 game hours
   after sunrise and before sunset; [game-loop.md](game-loop.md#the-clock)). So the range halves at night.
3. Weather: when the character's current weather effect (character `+0x448` -> `+0x198`, a `WeatherAffecting` kind) is not 0 and the
   character is outdoors (or the slot 60 flag below), R = lerp(s x (1 - p), R, R x m) with m = **0.4** for kind 1 (dust storm) and **0.75**
   otherwise, s the effect's strength (`+0x1a0`) and p the character's protection against that kind (`FUN_140886d10`: 1 when the race is
   immune, `weather immunity`, else a per-kind value from its gear). **Observed** (the field meanings are inferred from the `WeatherAffecting`
   use and the immunity map of [character-stats.md](character-stats.md#races)).

Who passes `inside`: the sight test (section 3) passes it when the looker is in a building (Character slot 59 returns the building it is in, a
`hand` whose type is not `NULL_ITEM`) and Character slot 60 is false; slot 60 is true for a character in a building whose byte `+0xd5` is
set, and then the range is also doubled (**Observed**; what `+0xd5` means is **Unknown**). The gather (section 1) passes "floor index
above 0" (slot 35) instead. The can-see test of section 4 passes it for a looker on an upper floor whose squad has a home town, unless both
are the player's.

## 3. The sight test (NPC lookers)

`FUN_140852f70(sense, target)` returns a visibility factor; the caller (section 4) needs it above **0.01** (**Verified** literals,
**Observed** flow):

- Nothing is seen when the looker's state object (Character slot 14 -> `+0xb4`) is 1 (**Unknown** which state).
- **Cone.** v = (cos a - **0.2**) / (1 - 0.2), where a is the angle between the looker's facing (its movement object's direction) and the
  3D direction to the target. v <= 0 (a above 78.46 degrees) means unseen.
- **Target posture.** Character slot 109 (`+0xe0`: 1 = crawling, which forces speed 7 in [pathfinding.md](pathfinding.md); 2 another
  ground state; 3 and up down) at 1 or 2: v x **0.5**. When the target is sneaking (`+0xd4`, slot `FUN_1405c9750`) or in those states,
  and a byte of its body object (`+0x640` -> `+0x24`) is clear: v x **0.75** if the cone factor was at least 0.5, else x **0.5**; then
  v x lerp(stealth, **1**, **0.6**).
- **Stealth** (`FUN_140884850(stats, 1)`): the stealth skill (`STAT_STEALTH`, stats `+0xa4`) x **0.75** when carrying something (`+0x348`) x
  **2** when crawling x the encumbrance, armour, race and injury factors, x 0.01, clamped to [0, 1] (**Verified**: the factors match the
  stealth tooltip `FUN_14088f9a0`, "Carrying something", "Crawling: +100%", "Encumbrance", "Injuries", "Armour/equipment").
- **Range.** R from section 2 (`inside` as described there), x **2** when slot 60. The reach is R x v, capped at R x 0.5 when
  `FUN_140436610(looker, 1)` holds (**Unknown** condition).
- **Light.** Each character keeps a light level at `+0xd8`: the daylight factor, set in its logic tick, refined from nearby light sources
  (`FUN_140a0af10`) when another character asked for it (`+0xdc`) and the factor is at most 0.75 or it is the player's (**Observed**).
  With Lt the target's and Ll the looker's: contrast c = clamp(Lt - Ll + 1, 0, 2), ambient a = clamp((Lt + Ll) / 2, 0, 1), and the light
  term is lerp(a, k, 1) with k = **0.2**, or **0.4** when the looker's squad has no home town (character `+0x1a0` -> `+0x88`; the same
  object the run-home desire reads, section 7.2).
- **Seen** when d <= lerp(a, k, 1) x c x R x v.

Worked figures, both standing and unlit alike, a human looker: in full daylight 1500 straight ahead, 951 at 45 degrees, none beyond 78.5
degrees; at night (daylight 0, no lamps) R = 750 and the light term 0.2, so **150** straight ahead (300 without a home town).

## 4. Can see: the order of tests (`FUN_140857b10`)

**Observed** (control flow), constants **Verified**:

1. A looker whose `+0x2f8` is 1 sees nothing (**Unknown** state; `+0x2f8` is non-zero in other contexts read as "in a building").
2. **Squad-shared**: when the looker's `GroupSense` (`AI+0x68`, section 6) holds the target, it is seen if d < **2 R**, with no cone or
   light test.
3. **Player-faction lookers**: a target in a player list is seen; an enemy of the player's within R (distance only) is seen (no cone,
   no light).
4. A target not in the same place (building, see `FUN_1408569a0`) is handled by the building rules (**Unknown** in detail).
5. **NPC lookers**: the factor of section 3. When it is above 0.01 and the target is sneaking, not friendly, and the looker is free
   (`+0x2f8` 0), a **stealth detection** builds up (`FUN_140856cb0`): per check, if there is line of sight, a stored amount for the
   target grows by lerp(v, 0.5 + 5 s, 0.5) x lerp(alert, 4, 1) (alert 0.5 by default, else a value from the looker's AI object;
   s the target's stealth); the target is detected once the amount exceeds 1, and the amount is dropped when line of sight is lost
   (**Observed**; the store `FUN_140855e80` was not read).
6. Final **line-of-sight** ray (`FUN_1408533a0`) for some targets (the player's characters, the alert, those fighting or carrying
   others): a physics ray from 18 units above the looker to the target; blocked when it hits more than 2 units short of the target
   (**Observed**; the 40-unit test at its start is not understood).

## 5. Hearing

**Noise range** of the target (`FUN_140851d10`, **Verified** literals):

- 0 (silent) unless the byte at the target's body `+0x640` -> `+0x24` is set (or `FUN_140436610`): read as "moving" (**Observed**).
- Weather factor w: **0.25** in a dust storm, **0.75** in acid or rain (`WeatherAffecting` 2, 5), else 1; f = lerp(strength, 1, w).
- Not sneaking or crawling: N = lerp(t, **350** f, **600** f), t = the ratio of two speeds of its body (`+0xb8` / `+0xb4`, read as
  current over top speed).
- Sneaking or crawling: N = lerp(stealth, **450** f, **80**).

**Can hear** (`FUN_140857500`, from the disassembly; constants read as bytes, **Verified**):

- When both looker and target satisfy `FUN_140856bc0` (**Unknown** predicate): heard within **1000** units (d^2 < 1e6), nothing else.
- Otherwise a target in another building, or on a floor more than one away, is not heard; N is halved when the looker's squad
  (`+0x1a0` -> `+0xb4`) is 0, and halved (twice) between buildings.
- A target is re-rolled only after enough time since its last roll (a game-time test against a global factor, **Unknown**); until then
  the last answer stands.
- Chance: t = 1 - d^2 / N^2. For a target not sneaking, p = **0.75** x t. Sneaking: p = **0.1** (or **0.05** with the squad value above
  0) x (1 - stealth) x t. x **2** while the entry's alert timer (`+0x3c`) is running. Heard when a random number in [0, 1) is below p.
- A sneaking criminal heard by a looker of an alert squad raises crime event 0x2f (**Observed**).

## 6. Tags, memory and the squad's shared senses

- **Entry** (`FUN_1408580d0`, one per sensed character, map at sense `+8`): position, game time, seen/heard bits, faction, and tag bits at
  `+0x30` (**Verified** against the debug panel's names): 0 ALLY, 1 NEUTRAL, 2 ENEMY, 3 KO, 8 CANT_SEE (cleared when seen), 14 CRAWLING.
  More bits (3 dead or down, 5, 7, 9, 10 same faction, 11, 12, 13 player, 15 carried) are set there with no name in the panel.
- **ENEMY** comes from Character slot 125 (`FUN_14079c950`): not the same faction; personal relation tags (`FUN_140678960`) first; a
  disguise rule (a uniform worn by the target counts for its faction above 0.6); then the faction relation at -30 or below
  (`FUN_1406b25d0`). The faction `coexistence` flag is **not** consulted there (**Observed**). Slot 126 (`FUN_1407923d0`) is the
  friendliness test (same faction, or allied).
- **GroupSense** (RTTI class; `AI+0x68`): two maps, seen and heard, from the members' senses; an entry is set to 2 when a member senses
  the target, and every time all members of the squad have updated, each entry loses 1 and is dropped at 0 (slot 4, `FUN_140853c90`,
  **Observed**). So a target stays shared for about two rounds of the squad's updates.
- **"No enemies in the vicinity"** (state 6 `isNoEnemiesInVicinity`, `FUN_140597630`): `AI+0xbc` is 0, which is the sense object's enemy
  count `+0x94` (**Verified**: `AI+0x28` is the sense object, see section 1). It is the end condition of `ATTACK_ENEMIES` and of running away.

Which task makes a character attack a sensed enemy, and at what distance, is the planner's business ([ai.md](ai.md)) and was not traced
(**Unknown**).

## 7. Fleeing

### 7.1 Task_Runaway (RUN_AWAY 35, RUN_AWAY_FORCED 280)

- **Registration** ([ai-tasks.md](ai-tasks.md)): "Running away", flags 0x61, sticky with a commitment of 0.05 game hours + up to 0.1
  (**Verified**: the registration writes 0.05 and 0.1 to TaskData `+0x18`/`+0x1c`, and -0.5 and 1.5 to `+0x24`/`+0x28`, **Unknown**
  role). Done when not `isAggressionTowardsMe` and `isNoEnemiesInVicinity`; may run when `LOCATION_IS_ACCESSIBLE` and not
  `IS_CROUCHING_OR_LYING`.
- **Start** (`Task_Runaway::vfunc_1`, `FUN_14034b440`): the centre of the threats is the mean position of the sensed characters that
  pass a tag filter (`FUN_140853960` with 6 and mask 0x218) and whose combat object has a positive value at `+0xac` (plus `+0xa0` when it
  targets the same object); with none, its own position. The destination is **2000 + 400 r** (r uniform in [0, 1)) along the direction
  from that centre to the character, and the movement's speed limit (`+0xbc`) is set to **999** (**Verified** literals; whether the
  distance is measured from the centre or from the character is not certain from the decompile, **Observed**).
- **Update** (`Task_Runaway::vfunc_2`, `FUN_140336820`): when the movement is idle, or the destination is less than **200** away, a new
  destination **1000 + 400 r** away in a random direction (x and z uniform in [-1, 1], normalised) (**Verified** literals).
- **Desire** (`AI::vfunc_55`, `FUN_1405a34e0`, through the member pointer `FUN_140318520`): **1.0** when Character slot 108 (byte
  `+0x5b8`) is set; otherwise **0.001** when the character can get out of the building it is in (`FUN_1405a2710`; always outdoors), and 0
  in a building in state 3. For characters `FUN_1407916d0` accepts, 0 under two further conditions and unless all items of an inventory
  section are of types 3, 6 or 8 (**Observed**; meanings **Unknown**). No code was found that writes `+0x5b8`; animation and limb code read
  it next to the leg-health tests (**Unknown** meaning).
- **Data**: RUN_AWAY is only in the `Slave AI Goals` of four prison packages ("Police Station", "empire police station", "empire vault
  prison", "police station patrol"), so the escaped prisoners run (**Verified**: data probe).

### 7.2 Going home (RUN_AWAY_HOMETOWN 127 "Retreating fast", RETREAT_HOMETOWN 128 "Retreating")

`Task_GoHomeTownFast` / `Task_GoHomeTown`. Desire (`AI::vfunc_56`, `FUN_1405a3710`, member pointer `FUN_140318370`): 0 when the squad's
home town (`+0x1a0` -> `+0x88`) is the town it is in, else **0.5** when it can leave its building (**0.01** in state 1 and not
`FUN_140856bc0`) (**Observed**). Data: 127 in "captured NPC", "disband / refugee", "Slave only squad"; 128 in "Retreat and refresh", "ex
slave", "warrior guild", "fog squad return home", "fish resident", "at home, bar" and the southern drone packages (**Verified**: data probe).

### 7.3 Squad retreat: the "Retreat and refresh" package

- Signal 5 `SIG_SQUAD_NOT_INTACT` builds `RetreatAndRefresh` ([ai.md](ai.md#signal-functions-and-the-package-classes)); its `canActivate`
  (slot 12, `FUN_1402871c0`) is "the squad is not intact" and `isFinished` its opposite. Its goals send leader and squad to
  RETREAT_HOMETOWN.
- **Intact** (`FUN_1407ac7b0` -> `FUN_1407a5be0` -> `FUN_14079acf0`, **Observed**): always intact for the player's squads and when a
  platoon flag (`+0xf0`) is clear. Able members = not dead and (not down, or a value at `+0x504` below h), h = **0.2**, or **0.8** while
  the squad is "refreshing" (`+0x115`). With fewer than **4** members (the squad size at `+0xa4`): intact while the leader is not down
  (refreshing: while any is able). With 4 or more: intact while able >= size x **0.2** (**0.4** when the leader is down; 0 with the flag
  `+0x70`). A member carried by a non-enemy, or in state 2 at `+0x2f8`, keeps it intact. What `+0x504` holds is **Unknown** (no write to
  it was found by an offset scan).
- **Data**: the package is the `fallback AI package` of five SINGLE_DIPLOMATIC_ASSAULT records (raids) and the `retreat AI leader` of four
  FACTION_CAMPAIGN records, and is used by four dialogue lines; no SQUAD_TEMPLATE lists it (**Verified**: data probe). So beaten raiding
  parties go home; ordinary roaming squads have no retreat package.

### 7.4 The "Run Away!" squad order

`FUN_140269320` (a Blackboard method): sets the "skip packages" flag `+0x14c` and `+0x130`, fills the three role lists (`+0x198`,
`+0x1b0`, `+0x1c8`) with RUN_AWAY_HOMETOWN, weight 1, and has the first conscious member say "Run Away!". Nothing references it or its
thunk `14003a2b5` (byte scan as in section 1), so it is never called (**Observed**: dead code).

### 7.5 The FACTION run-away ratios

`run away ratio of squad size` and `run away ratio relative to enemy` are loaded into the faction object at `+0xc` and `+0x10`
(`FUN_1407fe270`, **Verified**). No reader was found (searched: the decompile for float reads of `+0xc`/`+0x10` in functions that also
fetch a faction; the SSE loads and compares of `[reg + 0xc]`/`[reg + 0x10]` between 0x140200000 and 0x140a00000; functions using the
squad's member and enemy counts), so their effect is **Unknown**. Data: 92 of 103 factions have both 0; Bloodraiders, Dust Bandits,
Outlaw, Grass Pirates and Yabuta Outlaws 0.3 / 0.2; Police, Cannibals and Fogmen 0.3 / 0.3; Starving Bandits, Vagrants and Bar Thugs
0.5 / 0.5 (**Verified**: data probe).

### 7.6 Leash

No give-up-chase distance was found in the senses, `Task_Runaway`, or the attack tasks read here (**Unknown**).

## Unknowns

- The attack decision after a sighting (the planner path from ENEMY to an attack task) and its range.
- Slot 14 `+0xb4`, `+0x2f8` = 1, `FUN_140436610`, `FUN_140856bc0`, slot 60's `+0xd5`, the hearing re-roll factor, the meaning of
  character `+0x5b8` and `+0x504`.
- Whether the run-away ratios are read at all; the leash.
- The full stealth detection store (`FUN_140855e80`) and the building rules of can-see and can-hear.
