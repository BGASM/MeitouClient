# Simulation

Engine contracts for `Meitou.Simulation`; game facts live in [game/](README.md#gameplay-research).
Source authority: `World*.cs`, `StandardSystems.cs` and each subsystem. Implementation choices
below do not claim original-game parity. Retain Verified/Observed/Unknown scopes in linked research.

## Time model

Control: 30 Hz real time, input/UI/camera continue while paused. Simulation: 1/30 s game time;
accumulate `realDt*speed`, speeds 0/1/2/5. Budget `6*speed` ticks per frame; excess is dropped and
`AchievedSpeed` reports slowdown. Control ticks precede simulation ticks; a speed change applies
to the entire frame. `WorldSession` rejects unequal world/clock tick lengths, owns snapshots,
clocks, input and camera, and disposes systems/services.

At player speed 0 (without a modal/loading pause), queued selection, inventory move/arrange
and open-shop buy/sell/close commands apply between ticks and republish snapshots. Other
commands stay queued; neither world ticks, game time nor simulation phases advance.

**Verified** original clock: 1200/11 seconds per game hour ([game-loop.md](game/game-loop.md)).
`BodySystem.HoursPerTick=11/36000`; medical dt units remain **Unknown**. Navmesh unload uses
real time (30 s); population unload grace uses game time (towns 60 s, roaming 10 s).
`RequestedPause` is independent of remembered nonzero speed. Render previous/current states
with simulation alpha; camera uses control alpha. Positions are float world coordinates;
rendering is camera-relative.

## Threading

Tick order: Inputs → Schedule → Think → Move → Act → ordered Apply → SlowWorld → Publish.
Inputs/Schedule/Apply/SlowWorld are serial; each implemented parallel phase has a system barrier.
`CharacterHot.Previous` is read-only; a partition writes only its own `Next` slots. Spawn/remove
only in serial phases after consumers finish; generation IDs prevent stale slot references.
Combat has separate double buffers swapped at the end of its SlowWorld, not at global publish.

Effects sort by `(target,source,system,sequence)`; spatial-neighbour iteration order is fixed.
Commands apply by tick and queue order. In-tick orders use serial `World.Defer`; deferred orders
precede host orders for their due tick. No concurrent host enqueue may alter that ordering.
Randomness: stateless `Rng.Hash(seed,entityKey,purpose,counter)`; changing purpose/salt changes rolls.
Worker partitions are `threads*2`, minimum 16 slots (game 128); one worker means caller execution.
Async paths, navmesh and population results enter at tick boundaries; arrival timing is not
repeatable in play. Determinism tests use synchronous services.

`StateHash` is ordered, includes slot/generation/free-list and hashed subsystem state, normalizes
negative zero, and is a diagnostic rather than gameplay work. Golden scenarios must agree at
1/4/16 threads before pins change; pins are platform-specific. Snapshots expose living-character
identity, appearance, transform, animation, faction/squad/player/selection and body status;
selected characters also expose skills, inventory and path. Host reads double-buffered active
zones within the frame after ticks. Null appearances draw markers.

## World model

Dependencies: Core ← Data ← Simulation ← Engine ← Game; Navigation/Rendering are sibling
libraries. Characters use hot double buffers plus cold objects; squads/platoons use sorted
registries. `(slot,generation)` IDs and the free list participate in hashes. Static typed data
views are built over `GameRecord` once; missing fields use documented defaults. Population shares
factories/database/relations and lazy body/combat/item assembly. Research owns original formulas.

## Systems and their order

`StandardSystems.Build` is the single wiring authority. Ordered systems:
Population, Player, AI, Pursuit, Movement, FirstAid, Body, Carry, Construction, Combat, Animation, Feed,
Retaliation, Aggro, Flee, Crime, Trade. Tests selecting subsets retain this relative order.
Population spawns before later SlowWorld work; player selection precedes movement orders;
AI writes tasks before movement; pursuit schedules paths before movement answers them.
First aid reads patients in its own Act barrier and applies treatment during serial commit.
Body handles medical ticks before combat (`TickMedical=false`); carry sees knockouts before
combat and drops carried targets before attacking. Combat swaps before animation/retaliation;
crime sees deeds and final positions/carry links. Game combat uses `SelfApproach=false`.

## Population

`PopulationSystem`, `SquadFactory`, `CharacterAssembly`, `GeneratedAppearances`: activate zones
around focus, gate spawning on navmesh readiness, create residents/bar/roaming/wild/nest squads,
and assemble race appearance, gear, stats and bodies. Counts, roles, offsets, caps, spawn weights
and world-state gating follow [population research](game/factions-squads-towns.md).
Missing references become `SquadPlan.Problems`; world states initially false are an engine
choice, not a verified original default. Prisoners/cages, housemates and unique replacements
remain incomplete. `ActiveZones` is double-buffered. Zone loading/spawn planning stays off the
simulation thread; publish results at boundaries.
`World.PreparePopulation` queues nearby residents/wild squads, activates existing nearby roaming
platoons and drains completed builds during loading. It publishes matching snapshots without advancing
ticks, game hours, AI, movement or roaming timers; readiness includes navmesh-gated sites.

## Player

`PlayerSystem` / Orders: selection, stop, move/queued move, attack, treat/rest/medic, carry/rescue,
loot, stances and squad toggles are commands. Resolve empty-ID orders against current selection.
New-game start data seeds squad/appearance/gear/money; original start relation/research/force-race
semantics remain partly unknown. Picking/HUD are host responsibilities, never simulation input.

### Order toggles

Passive/Hold/Block/Taunt/Jobs/medic/rescue state belongs to the player/character and relevant
systems. Explicit player commands and automatic retaliation have different eligibility;
Idle players self-defend unless Passive, and Hold prevents pursuit beyond attack reach.
New player characters start with Jobs and Ranged enabled. Jobs gates automatic medic/rescue
work; direct orders still run. Saved toggles override defaults, including explicit false values.
Jobs uses the Meitou-only bool `meitou jobs enabled`; the original save key is **Unknown**.
Sneak: sneaking speed cap and clips, quieter, smaller view factor; a hostile looker sees the sneaker only once its
spot meter fills ([perception](game/perception.md#spotting-a-sneaker)); the exposure feeds stealth XP.
Ranged (tooltip, **Verified** in `main.pot`, mainbar.cpp:1279: "prioritise ranged attacks, melee will be a last resort"): a
crossbow carrier takes the crossbow at any distance and melees only when dry or a melee attacker is on it; off, the distance
rule of `ChooseMode` applies. Taunt (mainbar.cpp:1273: "draw more of the enemy to attack him"): a hostile looker picking a
new fight prefers a visible taunter over any non-taunter (nearest first); its squad joins as usual. Fights already under way
are not retargeted; a sneaking taunter does not count. Both rules are engine choices; the original's are **Unknown**.
New games select the squad's first member. The game host also selects the first member after
loading, before publishing the initial snapshot; the save reader itself retains saved selection.

## Movement and paths

`MovementSystem`, `PursuitSystem`, `IAgentWalkability`: schedule/query asynchronously, publish at
boundaries, follow ordered path points, steer/separate using previous state and project to ground.
New move orders preserve reusable paths; attack pursuit reschedules target paths. Turning can
hold translation while facing changes. Original speeds/encumbrance and research are in
[pathfinding.md](game/pathfinding.md); footprint clearance/water costs come from nav queries.
Heights use the character's (or clicked) Y to pick stacked floors; long paths have a provisional tail.

## Navmesh

Own DotRecast builder/cache (`Meitou.Navigation`), not Havok runtime. Default cells 2, height 1,
48-cell tiles, agent height 18/max climb 5; ground/water/door areas. A* checks portal clearance
`2*radius` (default radius 4), water factor and doors. `NavAdapter` exposes simulation interfaces;
`ZoneGate` holds population until ready. Collision and world placement rules:
[collision.md](formats/collision.md), [pathfinding.md](game/pathfinding.md).
Cache validation, asynchronous unload races and key omissions are listed under known gaps.

## Animation

`AnimationSystem` produces named layers/weights/times; `AnimationLibrary` supplies race clips,
lengths, masks and selection data. Movement, held care/lying clips, melee/ranged actions and hit
reactions use researched animation rules. Root motion advances with clip time; relocation keeps
separation by footprint radii and drops blocked components. Rendering consumes snapshot layers;
shape/skinning belongs to the character renderer. [animation.md](animation.md).

## Bodies

`BodySystem` / `FirstAidSystem`, `Meitou.Data.Gameplay.Bodies`: race/stat/part factories,
medical tick, blood/hunger/parts, knockout/death, healing, limb state, move speed/encumbrance and
moving XP. Combat hits use the same body state; avoid double medical ticks. Treat/rest/medic
orders find eligible still patients, walk to reach and apply aid serially. Medical time scale
is explicit and uncertain; [character-stats.md](game/character-stats.md) owns original rules.

## Items and eating

`Inventory`, `ItemFactory`, `FeedSystem`: grids, stack limits, backpacks, quantities/charges,
load and food provision. ITEM `stackable` is an integer, not boolean; gear stacks cap at one.
Transfer/split/top-up through inventory APIs. Food/auto-eat uncertainty and container bonuses:
[character-stats.md](game/character-stats.md), [economy.md](game/economy.md).

Inventory moves validate snapshot identity, bounds, occupancy, stack limits and equipment
type/armour slot. Weapon sections are 10×2 (back) and 7×1 (hip); other equipment slots hold
one compatible item. These are engine constraints, not a complete original-game rule set.
Backpacks move intact between pockets and their attachment slot; containers cannot be put
inside a backpack. Equipment moves refresh worn appearance and combat gear; pocket armour
does not grant worn bonuses. Race restrictions, swaps and prosthetic changes remain absent.

## Combat

`CombatSystem`, `CombatDatabase`, `Fighter`: attack scheduling, equipment/techniques, stance,
block/dodge, armour and per-part damage, stun/KO/death, combat XP. Read previous combat state;
ordered effects apply hits. `TickMedical=false` with BodySystem, `SelfApproach=false` with pursuit.
Original formulas, constants and unresolved technique terms: [combat.md](game/combat.md).
Hits, pauses and initiative follow combat.md "Hit reactions" / "Pacing"; the combat system picks the stumble
clip (through the animation system's chooser) and holds the fighter for its length. Engine choices: a miss waits
0.2 s; the first attack after an order waits 0.15–0.6 s; the initiative roll is drawn per pair and tick so both
sides agree, and the loser holds off for 1 s; a target that would not swing back (stumbling, defending, not fighting, Block or
Passive order) is simply attacked; one that lost the roll to a fighter becomes its target; a defence that starts late
joins its clip partway so the passed roll counts when "Late dodges and blocks" is on (Advanced setting, a balance
change: Faithful off, Meitou on);
attack slots go to the nearest fighters at the target (Advanced setting "Attackers per target", 1-5: Faithful 1,
the CONSTANTS value; Meitou 3), the rest wait instead of circling; the block break-out ignores the 10% lower bound; only a heavy bolt staggers.

### Ranged

Crossbow reload/steady aim, skill spread, bolt flight, ammo, obstruction and friendly hits are
simulation state; `Schedule` advances bolts. Targets without medical bodies do not stop bolts.
Ranged state is hashed when used. Weapon/turret research: [combat.md](game/combat.md).

### Animals

`ANIMAL_CHARACTER`, animal body/stats/size/fists and creature techniques come from typed data;
animals use faction hostility and carrying/damage rules where eligible. Unarmed humans use
martial-arts category 5. [animals.md](game/animals.md), [characters.md](characters.md).

### Hostility and relations

`AggroSystem`, `RetaliationSystem`, `FleeSystem`: previous-position sight/hearing, faction
relations, shared targets, idle-player self-defence, pursuit leash and return to routine.
Orders defer to the next tick. Relations are live state and deed/witness effects have distinct
paths; preserve current ordering. Original threshold/sense/retreat facts:
[perception.md](game/perception.md), [AI](game/ai.md), [factions](game/factions-squads-towns.md).

## Carrying, rescue and looting

`CarrySystem`: serial links, pickup/put-down, dropping on KO/death/attack, rescue jobs and inventory
looting. Carried bodies follow the carrier and affect load. Loot can raise witnessed theft and
stolen ownership marks. [carry-loot.md](game/carry-loot.md). It also runs the stealth knock-out
(`StealthKo` task, reusing `CarryTarget`/`LiftTime`): [combat](game/combat.md#stealth-knock-out).

## Crimes and bounties

`CrimeSystem`: pending deeds, witnesses, booking, police eligibility, faction ledgers and expiry.
Carry/combat report deeds; serial sweep observes current positions. Bounty expiry uses
`0.04 hours/cat`, permanent at >=10000. Original categories, witness rules, imprisonment and
remaining uncertainties: [economy.md](game/economy.md). Expiry is paused while imprisoned.

## Cages, beds and locks

`Locks/`: `World.Fixtures` (`FixtureRegistry`) holds every door, `has lock` container, cage (function 8, pole/peeler
`Restrains`) and bed of a town, registered once when the population first spawns the town (`RegisterSite`), kept on
unload, hashed only once non-empty. Facts: [locks](game/locks.md), [economy 11.5/11.7](game/economy.md#115-bail-and-sentence).
- **Ids** (engine): a building's own lock = its instance id; door i `<inst>#i`; interior item `<inst>/<layout index>`
  (`TownBuildings` collects them as `TownFixture`s); bed k `<inst>@bed<k>`. Owner: the placement's `owner faction ID`, else
  the town's faction. Lock levels roll from `Rng.Hash(seed, StableHash(id), Lock, 0)`: load order never changes them.
- **Doors**: `AiSystem` applies the template's `initial door state` when it assigns a home (first squad, not to restored
  doors). `IDoorGate` (the game's `NavAdapter` -> `NavDoors`, reset on `ReplaceWorld`) closes a building for every path while
  all its doors are effectively locked and none broken (engine: one nav door state per building; closed-unlocked doors stay passable).
- **Cages/beds**: `FindCage` (original weights), `Imprison` (state 2, `CharacterTable.Place` at the item's origin, cage
  locked, sentence), `Release` (unlock, clear, erase the releaser's and jail's bounty entries for a booking releaser),
  `GetOut` (refused while locked; Escaping Prison unless served), `EnterBed`/`LeaveBed` (a move order gets a sleeper up).
  `CharacterCold.InSomething`/`InWhat` name the fixture; an imprisoned character is skipped by AI, movement (held still),
  aggro, answering, `CombatSystem.Engage` as attacker (it can still be attacked) and carrying. Sentence: `SentenceStart`
  (`World.GameHours`) + `SentenceLength`; served when start + length <= now; no bounty with the cage owner: none (served).
- **Lockpicking** (`Lockpicking`, serial, for the next wave's order and AI tasks): `Step` per task update (pending crime,
  timer on the lock, seeded roll by the lock's roll counter, XP), `Allowed` (min chance; engine: conscious, an arm, not
  carried, not on a pole), `Break` (strength > 3 x hardness). Not built: shackles (item locks), cutting, lifting out of a
  cage, the owner's lock/unlock tasks, door motion.

## Buildings

Player construction contract (Faithful UI). Facts: [construction](game/buildings-production.md#construction),
[placement](game/buildings-production.md#placement), [build menu](game/buildings-production.md#build-menu),
[build tasks](game/ai-tasks.md#build-tasks), [save keys](formats/zones.md#placement-keys). Out of scope: walls and
snapping, outposts/town creation (`creates player town` only blocks), the door x3 rule, production/power/storage,
research progress, fetching from storage (task 247).

**Shared types** (stub commit f4cb78c; the simulation track owns and fills them):
- `Meitou.Data/Gameplay/Construction/BuildingBuildData.cs`: `BuildingBuildData.From/LoadAll` (build fields, material
  entries), `MaterialCost`.
- `.../PlacementRules.cs`: `PlacementFailure` (one value per original message), `PlacementTown`,
  `TownBlock(BuildingBuildData, Vector2 at, IReadOnlyList<PlacementTown>, string playerFactionId)`, `Message(PlacementFailure)`.
- `Meitou.Simulation/Construction/ConstructionCommands.cs`: `PlannedBuilding(Record, Position, Rotation, Floor, Parent)`;
  `PlaceBuildings(Plans)` (Confirm), `BuildOrder(Characters, Site, Repair)` (orders 2/95), `DismantleOrder(Characters, Site)`
  (96), `CancelSite(Site)` (engine: only a site with nothing delivered), `PauseSite(Site, Paused)`. Sites are named by instance id.
- `.../ConstructionSnapshot.cs` + `WorldSnapshot`: `Buildings` (`BuildingSnapshot`: `Id`, `Record`, `Position` absolute,
  `Rotation`, `Floor`, `Parent`, `Owner` faction index, `Progress`/`Target` in material units, `Materials` (item,
  required, delivered), `Complete`, `Paused`, `Dismantling`), `BuildingsRevision`, `UnlockedBuildings`.

**Simulation state.** `World.Buildings` (`Construction.BuildingRegistry`): player-built sites in creation order, by id,
holding the snapshot's fields plus rate, builder counter and destroyed. Base-game placements are not in it. Ids
`<n>-zone<X>.<Y>-INGAME`, n from a hashed registry counter started above every id in that zone's level file and the
loaded save. `World.Research` (`Construction.ResearchState`): completed RESEARCH ids from the start-off's `research`
(`PopulationSystem.Player`) and the save; unlocked = union of their `enable buildings`; static this milestone, not hashed.

**ConstructionSystem** (`StandardParts.Construction`, in `All`) runs after Carry, before Combat; all of it is serial.
- `Inputs`: commands. `PlaceBuildings` re-checks only the data half (record is a BUILDING, unlocked, `TownBlock`); a
  failing plan is dropped with `World.Post(Message)`. `PlaceBuildings`, `CancelSite` and `PauseSite` also apply while
  paused (`ApplyPausedInterfaceCommands`). Orders set the new `CharacterTask`s `Build`, `Dismantle`, `DeliverMaterials`
  and `CharacterCold.BuildSite`; movement walks the character to the site (work distance **Unknown**; engine:
  footprint edge + 20).
- AI `Think` gives the auto-job (researched scoring) to idle player characters with Jobs, as for medic work.
- `SlowWorld`: arrived characters in slot order deliver (5 s bar, one unit per missing type, inventory then backpack),
  build or dismantle by the researched formulas with dt = `TickSeconds`, `Building speed` option 1, CONSTANTS
  `build speed` and `min dismantle materials percentage` via `GameConstants`. The 0.75^k counter resets each tick
  (engine reading of the **Unknown** reset), so k = builders already applied to the site this tick. Completion sets
  `Complete` and posts the message for the player faction; dismantling to 0 refunds and removes the site.
- Refund count of entry i rolls `Rng.Hash(seed, StableHash(id), RngPurpose.Build, i)`. Engine (no ground items yet):
  the items go to the dismantler's inventory, then backpack; the rest is lost with a message.
- `BuildingsRevision` bumps on add, complete, destroyed and remove.

**Hash and determinism.** The registry hashes (counter, then each site's fields in creation order) only once non-empty,
like `Fixtures`; `BuildSite` hashes only when set; existing goldens stay. Decisions read only world state and command
order, never collision or navmesh readiness (paths stay the usual asynchronous input).

**Host seam: snapshot diff.** The host keeps the last `BuildingsRevision` it acted on (-1 after `ReplaceWorld`) and,
when it differs, diffs `Buildings` by id: added → render overlay (and navmesh if it qualifies), removed → out of both,
newly complete → navmesh. Progress does not bump the revision; the scaffold reads `Progress/Target` each frame (rule in
[construction look](game/buildings-production.md#construction-look)). An event queue would lose changes across dropped
ticks and loads; the diff does not. Navmesh membership (engine reading of the Observed rebuild on placing):
`PathMode != 0` and (`Complete` or `BuildThreshold == 0`). After each rebuilt zone publishes, the host enqueues
`RepathCommand`.
- Overlay type: the existing `BuildingPlacement` (`StateId` null, `WorldY` = absolute Y, zone of the position).
- Rendering: `WorldObjects.AddRuntime/RemoveRuntime(BuildingPlacement)` merged into `BuildZone`; the streamer reloads
  that zone; per-instance `constructionState` drives the scaffold.
- Navigation: one thread-safe overlay store shared by every `ZoneGeometryGatherer` (`NearBuildings` and `BuildingHash`
  see it, so the cache key changes), `NavSystem.AddRuntimeBuilding/RemoveRuntimeBuilding` and `RebuildZone(zone)` for
  each zone within `BuildingReach`.

**Placement validation.** Host: `Meitou.Game/Building/PlacementCheck.Evaluate(PlacementQuery) -> PlacementFailure`,
pure over the query (build data, transform, floor, queued ghosts, snapshot characters and sites, towns) and services
(heightmap, collision cache, water level). It calls `PlacementRules.TownBlock` first and returns the first failure in
the original's order. The geometric half depends on asynchronously loaded collision, so the simulation trusts it and
re-checks only the data half. Ghost controls, tilt and the unconfirmed ghost queue (Undo) are host UI state.

**Saves.** Simulation track: `Saving/BuildingSave.cs` writes each site as a GAMESTATE_BUILDING plus its building-list
entry in its zone file (keys per [zones](formats/zones.md#placement-keys)) and reads player-faction `-INGAME` buildings
absent from the level data back into the registry; `Saving/ResearchSave.cs` reads and writes the completed RESEARCH.
The first snapshot after a load carries every site, so the host seam needs nothing extra.

**Files.** Simulation track: `Meitou.Data/Gameplay/Construction/*`, `Gameplay/GameConstants.cs`, `Meitou.Data/Save/*`
if needed; `Meitou.Simulation/Construction/*`, `StandardSystems.cs`, `World.cs`, `World.Interface.cs`,
`WorldPublish.cs`, `WorldSnapshot.cs`, `Movement.cs`, `CharacterTable.cs` (cold fields), `Rng.cs`, `SeedSalts.cs`,
`PopulationSystem.Player.cs`, `Ai/AiSystem.cs`, `Ai/TaskBehaviours.cs`, `Saving/*`. Host track:
`Meitou.Game/GameHost.Building.cs` (diff, overlays, rebuilds, repath), `PlayerInterface.Build.cs` (Kenshi_BuildWindow,
ghost, commands), `Building/PlacementCheck.cs`, `Building/BuildMenu.cs` (categories and groups from
`BuildingBuildData` and `UnlockedBuildings`), `KeyMap.cs` (build map), `PlayerInterface.ContextMenu.cs` and
`PlayerInterface.Picking.cs` (site verbs), `GameHost.Interactive.cs` (hook), `Meitou.Engine` if needed,
`Meitou.Ui/*`; `Meitou.Rendering/WorldObjects.cs`, `ObjectStreamer.cs` and the object shader plumbing;
`Meitou.Navigation/NavSystem.cs`, `NavMeshService.cs`, `ZoneGeometryGatherer*.cs`, new `NavRuntimeBuildings.cs`.

## Trade

`TradeSystem`: trader-squad stock/purse/restock, open trade, buying/selling/fencing. Charge,
quality, culture, market, forbidden/stolen factors follow [economy.md](game/economy.md).
Shop state is hashed; open UI trade is not world state. Building shops/production/research remain
incomplete. Transfers and money changes must succeed together.

## AI

`AiSystem`, blackboards/minds, package/task data: slot/class/time/weight selection, homes/beds/nodes,
patrol/bar/shop/sleep/retreat/contracts, combat stances and off-screen roaming jobs. Data and
TaskData semantics: [ai.md](game/ai.md), [ai-tasks.md](game/ai-tasks.md). Desire scoring is an
engine approximation where original functions are undecoded; full planner, wars, prisoners and
resident town simulation off screen are incomplete. Hash blackboards/minds/jobs when present.

## Saves

`Meitou.Data.Save` parses/writes folders; `Meitou.Simulation.Saving` maps world state. Capture
serially at a boundary, write/read on workers; `ReplaceWorld` publishes a successfully loaded
world and preserves the running world on failure. [save.md](formats/save.md) owns file keys,
handles and original saved/derived/regenerated state.

### Quicksave and quickload

F5/F9 and named save menus share mapping. Restore clock, player money/faction, relation/prosperity,
player squads, character transforms/names/roles, stats/medical parts, appearance, inventories,
bounties, bed/cage state and sentence, and registered buildings' locks and doors (into existing zone records only; a
new save has none, [save.md](formats/save.md#locks-cages-and-sentences)) and selection. Town/roaming populations regenerate. Weather, orders/paths, animation,
combat engagement, wound-list/pain, pending crimes, base-game building state, ground items, camera/speed are not
fully restored. Loaded hashes need not match captured hashes: compare carried fields and agreement
between repeated loads. QuickSaveTests checks carried fields and repeatable loading.

## Sandbox

Headless simulation/debug harnesses use selected StandardParts and synchronous services for
repeatability. Sandbox omits Aggro when its scripted foe must remain idle; benchmarks must
separate simulation work, startup and rendering.

## Duel

The deterministic duel harness locates a scripted knockout, runs the pre-lap fight headless,
then replays the last 40 seconds and 4 after KO with cinematic keys. Replacing the world/clock
and resetting camera evaluation is required for repeatable recording.

## The game host

`Meitou.Game/GameHost*.cs` adapts input to commands, drives WorldSession, navmesh upkeep,
interpolated CharacterDrawList, HUD, loading/transitions and saves. It does not duplicate system
order. Renderer state lives in Rendering; snapshots are the simulation-to-host boundary.

## Open questions and known gaps

Original-game unknowns are maintained in each linked subsystem reference; do not infer parity from our determinism tests.

**Engine gaps:** remaining AI is listed in [AI](#ai); persistent town/world state, production, walls and outposts, full UI fidelity, dynamic doors and physics remain incomplete. Save and combat limitations are listed in their subsystem sections.

**Behaviour and hash constraints.**
- No end-of-tick hook: the combat system swaps its arrays at the end of its `SlowWorld`, so the result depends on the system order
  ([Systems and their order](#systems-and-their-order)). Moving the swap would change what retaliation reads.
- The two `MedicalContext` builders (`BodySystem` and `CombatSystem.MakeContext`) agree on toughness, strength, encumbrance (the body system writes it to
  `Fighter.EncumbranceFactor`, 2026-10-13) and seed, and differ in `BodyTimeScale` (combat's is 1) and `Resting` (combat's is false). So they are equivalent only at time scale 1 and for a
  character that is not resting; with the game's `TickMedical = false` the combat one only serves hits, where neither matters.
- Combat ticks are `int` (`CombatSlot.ReadyTick`, `StunUntil`) while the world's tick is `long`.
- `ItemInstance.StolenFrom` is not included in the inventory hash. Adding it requires updating the affected carry/crime pins; pins are platform-specific.
- `BodyHash` does not cover the private `starveGrace` and `starvationKo` of `MedicalState` (nor every flag such as `BloodKo`), so a divergence
  there shows in the hash only later.
- `CharacterHot.Animation`/`AnimationTime` (the old movement layers) and the name length are still hashed; removing them moves every hash.
- `HoursPerTick` is a constant of its own, not derived from `WorldSettings.TickSeconds`.
- Navmesh: unloading a zone whose build is queued or running does nothing, so the zone comes back when the build publishes (R3); the cache key
  misses foliage, record contents, `seeds.def`, collision files, the DotRecast version and the load order (R5).
