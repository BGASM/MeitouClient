# Buildings and production

Original building classes/function IDs, records, construction/producer/power/storage/farm/research
binary research; field/constants tables and expressions retained. Placement: [zones](../formats/zones.md),
prices/stock: [economy](economy.md). Meitou construction contract: [simulation](../simulation.md#buildings);
production/research gameplay is incomplete.

## Construction

### Construction state

`Building+0x160` (returned by Building slot 69) holds the construction state (**Verified**, offsets read in the
initializer `FUN_14055ba20`, the tick `FUN_1405595a0` and the save writer/reader `FUN_1405547f0` / `FUN_140555650`):

| Offset | Meaning | Save key |
|---|---|---|
| +0x160 byte | complete | `is complete` |
| +0x161 byte | paused (auto-jobs skip it) | `pause` |
| +0x162 byte | dismantled (set when dismantling reaches 0) | - |
| +0x168 float | dismantle work held while `FUN_14025c6d0` blocks it (**Unknown** what) | - |
| +0x164 float | progress, in material units | `construction progress` |
| +0x178 / +0x180 | material entries `{item, required float, delivered float}` | `<i>construction mats` = delivered of entry i |
| +0x188 float | target = sum of required amounts | - |
| +0x18c float | rate = `build speed mult` / (1200/11), i.e. per game hour converted to per second | - |
| +0x190 int | builder counter of the 0.75 falloff | - |
| +0x194 float | visual threshold = clamp(`build threshold` / 100, 0, 0.95) | - |
| +0x1a0 byte | is a door (set by the DoorStuff constructor `FUN_14029fb40`): build work x3, never completes itself, no refund | - |
| +0x1a1 byte | destroyed | `destroyed` |

Materials (**Verified**, `FUN_14055ba20`): one entry per BUILDING `construction` reference (item = the referenced
record, required = its first value); if that list is empty and int `build materials` > 0, one entry of item
`580-gamedata.base` (Building Materials) with required = `build materials`. Delivered starts at 0. Re-initialising keeps existing entries
(matched by item) and resets only their required amounts. A total of 0 completes at once. Data (**Verified**, probe of
the install): `gamedata.base` has `construction` lists on 2 buildings (House: Building Materials x15), the rest use
`build materials`; `rebirth.mod` adds lists to 174 buildings, all ITEM records (e.g. Harpoon Turret: Steel bars x3,
Iron Plates x7, Copper x3).

### Construction tick

**Verified** (`Task_Build` update `FUN_14034a440`, event dispatcher `FUN_1407b65b0` case 11, `FUN_1405595a0`):

1. Each update of a builder whose site is not stalled pushes a type-11 event `(building, amount)` with
   `amount = lerp(0.3, 2.0, clamp(engineering, 0, 100) / 100) * dt`, `dt` = the character's update time
   (`Character+0xc4`, game-speed-scaled seconds). The builder gains engineering XP (`FUN_1408c6250`, [stats](character-stats.md)).
2. The dispatcher calls Building slot 70 with that amount. Slot 70 scales it by the *Building speed* option
   (`142133584`, 0.5..2) and CONSTANTS `build speed` (`g_Constants+0x168`, 6 in the base game), then by `0.75^k`
   where `k` = calls since the counter was last reset (k capped at 15; a counter above 100 restarts at 0), and by 3
   for a door (+0x1a0).
3. `progress += rate * scaled amount`; a rate <= 0 sets progress to the target at once.
4. When +0x1a0 and complete are both clear and `progress >= target`, Building slot 72 (`FUN_140562340`) completes it
   (progress = target, complete set, functionality/lights/interior switched on) and, for a player-faction building (**Observed**),
   posts "Building '{1}' construction complete [at '{2}']." and the `Building_Complete` sound.

The counter is zeroed by Building slot 28 (`FUN_1405626a0`); how often that runs is **Unknown**, so whether the 0.75
falloff is per frame (several builders on one site) is **Observed** only.

Materials gate progress (**Verified**, `FUN_14054a3f0`): a site is stalled when it is not complete and
`sum(min(delivered, required)) <= progress`; `Task_Build` ends instead of pushing an event. One delivered unit thus
allows one unit of progress, and the last tick may overshoot by one event.

Resulting time for one builder (**Verified** algebra of the above): `hours = materials / (build speed mult x 6 x option
x lerp(0.3, 2, engineering/100))`. The build window's "Estimated build time" (`FUN_14055bf10`) is the same without the
engineering factor (it equals an engineering 41.2 builder). Meitou should run the tick above, not the UI hours.

Destroyed buildings (**Verified**, `FUN_1405595a0`): the first tick on a building with +0x1a1 set clears complete, sets
progress to 0.4 x target, sets each delivered amount to `floor(delivered x U[0.3, 0.5) + 0.5)` and calls slot 104
(`FUN_140557280`, destroyed off: navmesh/doors restored, **Observed**).

Save import (**Verified**, slot 18 `FUN_140552b70`): when importing, a building whose faction is not the player's has
`destroyed` cleared, every delivered set to required and is completed at once (slot 70 with FLT_MAX).

### Construction look

**Verified** (`FUN_140559110`, `FUN_1405583e0`, `data/materials/deferred/objects.hlsl`): the shader constant
`constructionState` of every part entity = `(f, f, 0)` (`(0, f, 0)` while +0x1a0 is set), with
`f = (min(progress/target, 1) - threshold) / (1 - threshold)`. The vertex shader gives each vertex
`y = height / mesh top`; pixels above `1.2 f` keep only the scaffold grid (`construction.dds`, alpha > 0.7), and the
grid overlay fades out above `1 - 5 (f - 0.8)`. The shadow caster clips the same way.

### Material delivery

**Verified** (`Task_AddMaterialsToBuilding` `FUN_140348870` / `FUN_140351e20` / `FUN_140336e60`): animation `medic`,
sound `Character_Building_Materials`; a bar fills at `dt x 0.2` (5 s). When full, for every entry with
required > delivered it looks for one item of that record in the character's inventory, then its backpack; if found
the item is removed and delivered += 1 (`FUN_14054a3a0`). Materials are therefore consumed on delivery, one unit per
missing material type per action; progress later spends delivered units (stall rule above). The task is done when
nothing is outstanding and fails to the planner when something is outstanding but nothing is carried. Whether the bar
restarts between actions is **Unknown**.

Fetching (**Verified** class, **Observed** meaning): `Task_EmptyMachine_BuildMats` (task 247 "Collecting resource")
shares the Task_EmptyMachine update `FUN_14033cd10`: it faces a building and moves its items into the inventory while
there is room, 2 per pickup (`FUN_1403411c0`). Which storages are chosen and the range are **Unknown**.

### Builders and dismantle

Task ids and AI conditions: [ai-tasks](ai-tasks.md#build-tasks). Auto-job (**Verified**, `FUN_1409a6900`,
`FUN_1409a6c10`, `BuildingFinderForEngineers`): candidates are buildings of the character's owner that are not
blacklisted, dismantled, holding dismantle work (+0x168 > 0) or paused; an incomplete building (or one needing repair,
slot 0x328) is a candidate, a complete one contributes its mounted children that need work. Score
`max(2 (1 - d^2 / 9000^2), 0.0001)`, x planner cost when the site is stalled and the character carries nothing, x0.5
when slot 89 is set, / (n + 1) with n = characters already working it. No cap on builders per site (**Observed**: the
score and the 0.75 falloff spread them). Work distance (`isAtLocation`) is **Unknown**.

Dismantle (**Verified**, `Task_Dismantle` `FUN_1403518c0`, Building slot 73 `FUN_1402a2860`): refused with "I have to
dismantle the furniture first." / "...unmount the buildings first." while those exist; a door redirects to its
building. Animation `stonemine`, engineering XP as for building. Per update `amount = dt x lerp(0.05, 4, eng/100) x 4`;
slot 73 clears complete (a complete building first drops to `target - 1`), then `progress -= amount x rate` (no option
or CONSTANTS factor). At progress <= 0: mounted turrets released, +0x162 set, sound `Thing_Complete`, refund, building
removed.

Refund (**Verified**, `FUN_14029dc10`, called by slot 73 and by `FUN_14054c810` for mounted buildings that cannot
move): skipped for doors; for each entry with delivered > 0, spawns `max(1, trunc(U[m, 1) x delivered))` items at
random points in the building's bounds, `m` = CONSTANTS `min dismantle materials percentage` / 100 (60 -> 0.6,
`g_Constants+0x134`). Cancelling an unbuilt site is a dismantle and returns 60-100 % of what was delivered.

### Placement

Build-mode object `DAT_14212de48`; the ghost (class `PreviewBuilding`, subclasses `_Wall`, `_Resource`, `_Ceiling`,
`_Snapping`) at +0x18 recomputes its flags every frame in slot 25 `FUN_1404dba40`; the click handler `FUN_1404e27e0`
shows the first failing message in this order (**Verified** unless marked):

| Message | Check |
|---|---|
| "Can't build too close to another town." / "Can't place in a building you don't own." | a non-player town is within its block radius (below); second text when the ghost is inside/on a building |
| "No resources at this location, mining would be pointless." / "This crop will not grow in this environment." | resource value or crop yield < 0.01 (below) |
| "Cannot build inside incomplete buildings." | the building under the ghost is incomplete |
| "Must be placed outside or on a roof." / "Must be placed inside a building." | indoor/outdoor rule; the first for `BF_RAIN_COLLECTOR` (26) |
| "Can only be placed on certain locations (marked by blue nodes)." | `PreviewBuilding_Snapping` not on a snap node |
| "Too close to another building." / "No valid usage nodes." | a footprint node box overlaps physics shapes (group mask 0xfffde40, up to 32) or another queued ghost; second text when every usage node is invalid |
| "There are characters in the way." | an object in a node box is rejected by `FUN_1407d1440` (**Observed**) |
| "The building blocks another building use nodes." | the footprint covers another building's usage node |
| "The building has one or more parts on invalid ground." | a BUILDING_PART with `ground type` LAND has its node centre below y = 98 (water level, **Observed**) |
| "Invalid placing floor number. ..." | inside a building above floor 0 a node overlaps floor collision; or a ceiling ghost finds no ceiling |
| "Ground needs to be more level." | vertical margins or slope (below) |

- **Towns** (`FUN_140926d50`, `FUN_140926eb0`): radius = `town radius mult` x base (town 2500, village 937.5, ruins
  875, point of interest 437.5, others 1250). A `creates player town` building is blocked within 2.5 x radius (1 x for
  ruins/POI) or 2000 of the centre; other buildings only within 1 x radius, so they may stand outside any town.
  Player towns never block.
- **Outposts** (`FUN_1404d6d90`, `FUN_1404db180`): a ghost joins the nearest player town within 5000, or the town of a
  player building within 2500; otherwise a `creates player town` building makes a town "Your Outpost" at its position
  (`FUN_140934e10`), and player buildings whose nearest town within 8000 is it move into it. On Confirm, queued ghosts
  needing a town get one at their mean position if no player town is within 16000.
- **Slope** (slot 31 `FUN_1404daa20`): ground normal from four rays at the rotated bounding-box corners (outdoors) or
  one ray (indoors); slope = angle from vertical, compared with `max slope` in degrees. With `match slope` (forced for
  interior furniture and nodes) the ghost tilts to the ground and a slope over the limit fails; without it the tilt is
  blended by the player tilt factor and clamped, never failing. Also fails on an object surface more than 5 deg off
  the hit normal, or on ground with normal.y < 0.2 (`FUN_1404d5420`).
- **Vertical margins** (`FUN_1404d9c00`, `FUN_1404d21e0`): ground height by a ray from 1000 above; nodes marked
  `above ground` must be at or above it, others at or below; both minimum margins must be >= 0. `vertical position
  tolerance` widens this for single-kind buildings (**Observed**).
- **Indoors** (`FUN_1404d69c0`): `is interior furniture` / `is node` need all node points in one player-owned
  building; `is exterior furniture` the opposite. `ceiling placement` uses the ceiling ghost (hangs 1 below the hit of an
  upward ray).
- **Resources** (`FUN_1404d9ea0`): `world resource mining` STONE/WATER/GROUND from a noise field; IRON/COPPER/CARBON
  need the biome's `resources` and a resource node within 1500, falling off from the node radius (>= 25) to 1500.
  Farms: fertility `1 - (1 - v) x fertility effect` (0 at or below `minimum fertility`), yield = biome
  dry/green/swamp weights x the farm's factors x fertility.
- **Controls** (bindings `FUN_1403636c0`, applied in `FUN_1404d5420` x frame time): rotate +-80 deg/s, move up/down
  +-60 units/s (6 with the slow modifier, capped by the margins; offset reset above 1000), tilt factor +-1/s in 0..1,
  default 0.2.
- **Commit** (`FUN_1404d5130`, `FUN_1404db5a0`, `FUN_1404d72a0`): a click queues the ghost (position relative to the
  parent building, or height above terrain, and floor) and spawns a new ghost; Confirm assigns towns, then the factory
  `FUN_14057cc70` creates each building with its town, the player faction, rotation, parent building and floor. No
  terrain is flattened; outdoor buildings with `path mode` and `build threshold` 0 trigger a navmesh rebuild of their
  area (`FUN_1404d2c80` -> `FUN_1403a66c0`, **Observed**). That a new building starts at progress 0 with nothing
  delivered is **Observed** (the factory's complete flag is false).

### Build menu

**Verified** (`FUN_1406bff30`, `FUN_1404e4480`, `FUN_1404e4210`, `FUN_14082ea60`, `FUN_140830b40`): every BUILDING is a
candidate; category = `building category` (default "misc"), group = `building group` or the building's own name.
A building is offered when it is in the research manager's unlocked set, filled from each completed RESEARCH's
`enable buildings` (or the window's debug flag +0xa8 is set); a category shows when one member passes. In the base game
88 buildings are unlocked by research; categories DOORS, sign, nodes, moor, CANNIBAL, EXTERIOR, Furniture and misc have
none and never show. A group's default pick is the member with the highest `build materials`. Sort order **Unknown**.
Stats panel (`FUN_1404df450`): "Materials cost:" (each `construction` item and amount, or Building Materials x
`build materials`, or "Nothing"), "Estimated build time:" (formula above, whole hours), then power output/consumption,
`power capacity`, Num People (`max operators`), Produces (`produces`, `output rate`), Efficiency, Damage, Training max
and Turrets bonus (their formulas **Unknown**).

## Production

Producer/crafter consumes input recipe quantities, worker/skill/race/research/environment and
functionality rate, then creates output with quality/manufacturer rules. Crafting queue,
storage/output capacity and machine operation are distinct. Keep stateful progress and
availability tests; do not infer all rates from the displayed output-rate tooltip. Farming
fertility, water, crop growth, harvest/yield and labour differ from a generic machine.

## Power, storage and research

**Observed** power-output is data plus additive research increase; capacity is data plus increase,
then health fraction. Positive generates, negative consumes. Wind/fuel and battery output/capacity
are separate. Town sums supply/reserve, fairly shares remaining power among consumers and uses
batteries for deficit/charges from surplus. Minimum powered share and wind/fuel units remain
unknown. Tested base generator/battery values remain in data/source checks, not universal defaults.

**Verified** inventory-building survey87/603; default inventory18x18, itemtype-limit4. Storage
filters and stack bonuses apply separately from character backpacks. Research owns tech levels,
prerequisites, costs/progress, unlock/improvement effects and blueprints; money prices are in
[economy](economy.md). Additive versus multiplicative improvement semantics must be decoded per
field. Building operational state, ownership and saved progress: [save](../formats/save.md). Cages, beds,
doors and lockable containers: [locks](locks.md).

### Production and crafting

Main functionalities in the base game (**Verified**, probe of the functionality table: `production mult` and `output
per day`; all those listed have `output per day` true except crafting benches, power, battery and the furnace):

| function | examples (production mult) |
|---|---|
| 1 MINE | Ore mine 64, Stone mine 128, copper mine 72, well 48 |
| 1 MINE (farm types) | farming wheat 64, veg 64, cactus / cotton / hemp / potatoes / rice 48 |
| 4 REFINERY | Campfire 192, grain silo 72, Iron / Steel / copper plates 48, Rum / Grog / Sake / bread / drugs 24, chainmail and plates 12, Textiles cotton 32 |
| 5 GENERATOR | Power 0.00125, wind power 1 |
| 10 CRAFTING | most benches 1, cooking general 2 |
| 14 ITEM_FURNACE | Furnace 0.25 |
| 23 BATTERY | battery 0.00125 |
| 26 RAIN_COLLECTOR | 48 |

## Unknowns

- How often Building slot 28 resets the builder counter, the work distance of builders, which storages
  `Task_EmptyMachine_BuildMats` fetches from, and what `FUN_14025c6d0` (holding dismantle work) tests.
- Event types of the queue at `DAT_1421346d8` other than 8 (research progress) and 11 (build work).
- The unit and exact use of generator fuel burn (`production mult` 0.00125) and of the wind fraction.
- The crafting quality curve (`FUN_1402b5900`), the unit conversion between `craft time hrs` and the per-tick
  crafting amount, and the minimum power share for "powered".
- Default values of the three option sliders and whether they multiply or divide in every use.
- Meaning of the percentage and flag of consumes / produces reference triples.
- Whether the 0.6^k research falloff counter is reset per frame (per call is what the code shows).
- The argument `p` of the farm efficiency `FUN_1400dfe10`, and the record field that carries `BuildingClassType`.
