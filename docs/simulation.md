# Simulation

The simulation core of phase 5 of the [roadmap](../ROADMAP.md), with the pieces of phases 4, 6 and 7 it needed first (walkability,
characters on screen, saves of the world state). Designed 2026-10-07, built the same and the next day on branch `sim` in parallel tracks,
then refactored without changing behaviour (the golden hashes, [Threading](#threading)). This doc describes how it works now; the facts about
the original live in `docs/game/` and `docs/formats/` and are cited, not restated: [game-loop.md](game/game-loop.md) (frame, time, speeds,
clock, update order, budgets, zones), [ai.md](game/ai.md) / [ai-tasks.md](game/ai-tasks.md), [pathfinding.md](game/pathfinding.md),
[character-stats.md](game/character-stats.md), [combat.md](game/combat.md), [factions-squads-towns.md](game/factions-squads-towns.md),
[economy.md](game/economy.md), [buildings-production.md](game/buildings-production.md), [ui-input.md](game/ui-input.md),
[ui-screens.md](game/ui-screens.md), [formats/save.md](formats/save.md), [formats/collision.md](formats/collision.md).

Everything here is an **engine choice** unless it carries a label. Labels on claims about the original (**Verified**, **Observed**,
**Unknown**) are those of the research docs.

## Stages

Each stage ends with tests and something the owner can run.

| Stage | Content | Status |
|---|---|---|
| **0. Time facts** | `GameClock` at 1200/11 s per hour; speeds {0, 1, 2, 5}; the control tick in real time, the simulation tick in game time | Done ([Time model](#time-model)) |
| **1. Skeleton** | `Meitou.Simulation`, `World`, tick phases, worker pool, seeded randomness, state hash; typed views over CONSTANTS, FACTION, SQUAD_TEMPLATE, TOWN | Done ([Threading](#threading), [World model](#world-model)) |
| **2. Populate** | Squad factory, town residents, zone activation and unloading | Done ([Population](#population)); bar and roaming squads added after stage 6; the wild (homeless squads of the area sectors and nests) 2026-10-13 |
| **3. Move** | Movement, wander, go-to, follow | Done ([Movement and paths](#movement-and-paths)) |
| **4. Characters drawn** | Native character renderer | Done ([character-renderer.md](character-renderer.md)); animation layers from [Animation](#animation) |
| **5. Navmesh** | Collision reader, our own generator with the seed prune, path queries off the tick | Done in three passes plus the game wiring ([Navmesh](#navmesh)); gaps listed there |
| **6. Player** | New game from NEW_GAME_STARTOFF, selection, move orders, minimal HUD | Done ([Player](#player)); formation and queued orders included |
| **7. Bodies** | Stats and XP, hunger, blood, body parts, KO and death | Done and wired into `World` ([Bodies](#bodies)); first aid, resting and XP on the move are in play (2026-10-13, `FirstAidSystem`); beds and the time scale are open |
| **8. Combat** | Melee per [combat.md](game/combat.md); ranged later | Melee done and wired ([Combat](#combat)); fights start by faction hostility, the player's idle characters defend themselves, the player's assaults and killings change the relations (2026-10-13, `AggroSystem`, [Hostility](#hostility-and-relations)); crossbows done ([Ranged](#ranged)); animals fight with their natural weapons and bodies, drawn and animated with their own clips (2026-10-13, [Animals](#animals)); turrets and attack slots not built |
| **9. AI proper** | Packages, blackboard, scoring and planner ([ai.md](game/ai.md)), off-screen squads | Started 2026-10-14 ([AI](#ai)): the AI_PACKAGE and AI_TASK data, the blackboard's package choice by slot, the members' goal choice by tier, weight, hour window and continuity, 22 goal types acted out on the movement tasks; residents keep shop, patrol and go to bed by the clock. Not built: the package subclasses, the planner, the senses-driven goals (combat, rescue, prisoners), off-screen squads. `PursuitSystem`, `RetaliationSystem`, `AggroSystem` and the roaming squads are still engine stand-ins |
| **10. Saves** | Read and write the world state ([save.md](formats/save.md)) | API done ([Saves](#saves)); the game host quicksaves (F5) and quickloads (F9) the player's squad into its own slot ([Quicksave and quickload](#quicksave-and-quickload)); no save menu, no loading of the original game's saves in play |
| **11+** | Economy and trade, buildings and production, the MyGUI screens, audio | Crimes and bounties (2026-10-13, [Crimes and bounties](#crimes-and-bounties)); item prices, shops and trading (2026-10-14, [Trade](#trade)); the rest not started |

## Principles

1. **Headless and deterministic.** The simulation is a library with no GPU or window. Given the same data, seed, commands and tick
   count it reaches the same state with any number of worker threads. It never reads a wall clock and never sees the frame rate. A
   state hash makes this testable.
2. **Exact data, good-enough behaviour** (the owner's direction of 2026-10-06). File formats, merge rules and save keys are exact,
   because drop-in compatibility depends on them. Formulas from the research are used where they cost nothing extra. Scheduling
   details of the original (per-frame budgets, its AI thread, the races between them) are not copied.
3. **Parallel by construction.** Per-character work reads the previous tick's state and writes only its own; effects on other entities
   (a hit, an item handed over, a relation change, a spawn) go into ordered queues committed serially at the end of the tick.
4. **No stutter.** Loading zones, spawning squads, path queries and navmesh building run off the simulation thread; their results are
   taken in at tick boundaries.

## Time model

The original ([game-loop.md](game/game-loop.md#measuring-the-frame-time)) runs everything once per rendered frame with a variable `dt`
(capped at 0.05 s, smoothed over 20 frames), multiplies it by the speed, updates near characters every frame and far ones in per-frame
round robins. Its results depend on the frame rate, and at 5x every step is five times longer. Here (owner decisions 1 and 2):

- **Two clocks** (`Meitou.Engine.Time`, details in [engine.md](engine.md#the-game-loop-meitou-meitouenginetime)). The *control tick*
  (`FixedStepClock`, 30 Hz of real time) runs the input actions, the camera and the UI, so the camera moves while the game is paused. The
  *simulation tick* (`SimulationClock`) is 1/30 s of **game** time at every speed: its accumulator is fed `real dt x speed`, so speeds 1, 2
  and 5 run 30, 60 and 150 ticks per real second and pause runs none. Behaviour does not change with the speed or the frame rate; 5x
  costs five times the CPU per real second (the original keeps the cost and coarsens the step).
- **Falling behind slows the game.** A frame runs at most `6 x speed` ticks (`SimulationClock.MaxTicksPerFrame`: 0.2 s of game time per
  frame at every speed, so 5 fps is the floor at which the game still runs at the asked speed); the rest are dropped, as the original
  slows below 20 fps through its 0.05 s cap. `AchievedSpeed` is the asked speed times the share of the owed ticks that ran; the window
  title shows it when lower. The budget counts ticks, not milliseconds: a CPU-time budget would make the tick count depend on the
  machine, so it can only come later as a second limit, outside the determinism tests.
- **Frame order.** `WorldSession.Advance(real dt)` runs all control ticks the frame holds first (input, pause, speed, camera), then feeds
  `real dt` to the simulation clock at the speed they left. A speed change applies to the whole frame it is made in (at most one frame of
  error); the simulation never sees the input.
- **Session.** `WorldSession` (`src/Meitou.Engine/WorldSession.cs`) owns the two clocks, the `GameClock`, the input, the camera rig and the
  `World`. One simulation tick is `World.RunTick()` then `GameClock.Advance(tick seconds)`. The world's `WorldSettings.TickSeconds` (float
  `1f/30`) and the clock's `SimulationClock.TickSeconds` (double) are separate values; the session refuses settings where they differ.
  `Dispose` disposes the world and every `IDisposable` system (the movement system joins its path thread, the population system its build thread).
- **Cadences in game time.** The original's per-frame budgets become rates in game time ([game-loop.md](game/game-loop.md#the-character-update-budget)):
  every character moves every tick; slower work runs on its own cadence in ticks (zones every 15, retaliation every 15, an NPC's look for
  enemies every 15 staggered by slot, eating every 30, roaming pools every 60).
- **Clock.** `GameClock` advances with simulation ticks only: **Verified** 1200/11 game seconds per game hour, 24 hours a day, days per year
  from CONSTANTS; it keeps the day, the time of day, total game hours (double), the daylight factor with its 2-hour ramps, the day/night flag
  and the `HH:MM` / `Day n` texts ([game-loop.md](game/game-loop.md#the-clock)). Saves store day, hour and minute.
- **Speed and pause.** Speeds {0, 1, 2, 5}; pause remembers the last non-zero speed; `RequestedPause` stops the world for menus without
  touching the speed; the bindings are the game's (`pause` Space, `speed_1..3` F2 to F4, [ui-input.md](game/ui-input.md)).
- **Real-time things.** The original runs its autosave timer and zone unload timers in real time. Here the navmesh's zone unload runs on
  the host's real clock (30 s) and an autosave would too; the population's unload graces are in game time (60 s for towns, 10 s for roaming
  squads), an engine choice that keeps them deterministic. Physics: the original steps it in real time at every speed; here it would step
  with the simulation. That only shows in ragdolls at 2x and 5x and is decided when physics exists.
- **Body time.** One tick is `BodySystem.HoursPerTick` = 11/36000 game hours, which follows from the Verified clock (1/30 s x 11/1200 h per s).
  Whether the original's medical `dt` is game hours is **Unknown** ([Bodies](#bodies)).
- **Drawing.** The renderer reads the last two snapshots and interpolates by `WorldSession.SimulationAlpha`, as it does the camera by the
  control tick's alpha.
- **Coordinates.** Positions are world units (1 unit = 1 dm) in float, no floating origin: at the map edge (147456 units) a float step is
  0.016 units. The original rebases its origin every 10000 units for Havok and Ogre; the renderer here draws camera-relative.

## Threading

The simulation ticks on the main thread before the frame is drawn, with its parallel phases on a worker pool. The tick is written so that
moving it to its own thread (running ahead and publishing snapshots) is a host change only; it moves when the tick shows up in frame times.

**One tick** (`World.RunTick`, `src/Meitou.Simulation/World.cs`; `CharacterTable.Phase` records where it is, as a `TablePhase`):

| Step | Phase | What |
|---|---|---|
| 1 | `Inputs` (serial) | The commands due this tick (`CommandQueue.TakeDue`, by tick then queue order), handed to every system |
| 2 | `Schedule` (serial) | The `SpatialGrid` is built from `Previous`, the partitions are cut, then each system: path requests and answers, who is due |
| 3 | `Think` (parallel) | Per partition: AI intents (today the movement system's tasks) |
| 4 | `Move` (parallel) | Path following, steering, separation, integration on the ground |
| 5 | `Act` (parallel) | Medical tick, combat timers and blows, animation, the look for enemies; effects on others go to the partition's `EffectBuffer` |
| 6 | Commit (serial) | The effects of all partitions, sorted, each handed to `Apply` of the system that emitted it |
| 7 | `SlowWorld` (serial) | Zones, spawns and removals, roaming squads, eating, retaliation, the leash; each on its own cadence |
| 8 | Publish | Squads refreshed, dead characters pruned from the selection, the hot arrays swapped, the tick counted, the snapshot built |

Systems are `ITickSystem`s; every method has an empty default. In every phase systems run in the order they were added; each system's
parallel phase is a barrier. `SystemPhases` (built once per system when it is added) finds through the interface map which of `Think`,
`Move` and `Act` the system implements and keeps one delegate per implemented phase, so a phase left at the default costs no barrier and no
closure per tick. An effect carries the system's real index, so skipping a phase does not change the commit order.

**Double-buffered hot state.** `CharacterHot` structs live in two arrays: `Previous` (the last tick's result, read by every phase, other
characters included) and `Next` (a copy of `Previous` at the start of the tick; a parallel phase writes only the slots of its own partition).
They swap at the end of the tick. Spawns and removals happen only in serial phases: no spawn in a parallel phase, no removal before the
commit (a phase reads the character as alive); `CharacterTable.Spawn` and `Remove` throw otherwise. A slot freed this tick is therefore reused
no earlier than the commit. Cold state (`CharacterCold`) is changed by the character's own partition or in a serial phase. The combat system
keeps its per-character `CombatSlot`s in two arrays of its own with the same rule, swapped at the end of its `SlowWorld`.

**Partitioning** (`WorkerPool`, `World.MakePartitions`). `Threads x PartitionsPerThread` (2) partitions over `0..HighWater`, but none smaller
than `WorldSettings.MinPartitionSize`: 16 by default (the tests cut small worlds into many partitions), 128 in the game (a world under 256
slots runs on the calling thread; see [Measurements](#measurements) for why). Which worker runs which partition is not fixed. One thread
means no threads: the caller runs everything. The host chooses the thread count (`simThreads` in the user config; default
half the cores, 1 to 8).

**Effects.** An `Act` writes `Effect`s into its partition's `EffectBuffer`; the commit concatenates them and sorts by (target slot, source
slot, system, sequence), none of which depends on the partitioning. A source emits all its effects together. Sums over neighbours
(separation) visit the `SpatialGrid` (cell 100 units, built each tick from `Previous`) in a fixed order: float addition is not associative,
so the order must be a function of the state alone.

**Commands.** `SimCommand`s are stamped with the tick they apply at: `FocusCommand`, `MoveOrder`, `RepathCommand`, `SelectCommand`,
`StopCommand` (`Commands.cs`), `AttackOrder` (`Combat/CombatTypes.cs`) and `TreatOrder`, `RestOrder`, `MedicJobCommand` (`FirstAid/FirstAidTypes.cs`). The host enqueues them between ticks at `World.Tick`.
A system that decides on a command inside the tick (retaliation, the aggro's attacks and leash stops) hands it to `World.Defer` (serial phases only, for a later tick):
a list inside the world, never the host's `CommandQueue`, so a host enqueuing from another thread cannot interleave with it. At its tick the deferred commands come
first, in the order they were deferred, then the host's (2026-10-13; before, retaliation enqueued into the host's queue, where its commands sorted after any the host had
queued earlier for the same tick: no pinned scenario had such a command, and none of the nine old golden hashes moved). Deferred commands are not hashed (they apply
at the next tick and show there). `World.Post` / `TakeMessages` carry messages for the player (the relation thresholds) to the host, not hashed.

**Randomness.** Stateless: `Rng.Hash(world seed, entity key, RngPurpose, counter)` with SplitMix64 mixing; the entity key of a character is
`Rng.Key(id)` (slot and generation), the counter is usually a tick number. Nothing to store, hash or race on, and the same roll whichever
thread asks. `RandomStream` is the stateful convenience over the same hash. Helpers: `Rng.Float`, `Rng.Int`, `Rng.StableHash` (FNV-1a of a
string, for keys from record ids), `Rng.PointInDisc` (uniform in area; the player's start uses its divisor 3). Sub-streams: `BodyRolls`
(purpose `RngPurpose.Body`, an alias of value 0 so no body re-rolls), `CombatRolls` (seed, character key, `CombatRoll` kind, counter), and the
population's salts in `SeedSalts` (bar squads, the player's start, roaming choices, member looks); changing any of them re-rolls what it keys.
The original's own random sequence is not reproduced (open for the character generator too, [characters.md](characters.md)).

**State hash.** `World.StateHash()` is an order-sensitive mixing hash (`StateHasher`, written here, no packages) over the seed, the tick, every
slot in order (alive, generation, and for live ones every `CharacterHot` field, then faction, spawn tick, squad, role, formation offset, path
length and request, `IsPlayer`, the order queue, `DrawnWeapon`, stats and medical state through `BodyHash`, the inventory, provision and death
ticks, `InCombat`, the animation layers and the name's length; the bounty ledger and the pending crime only while set) and the free list, then the squad and platoon registries, the player state and every system that implements `IStateHashed` (the combat slots; the aggro system's leash slots, fight count and, once a deed has changed it, the whole relation table).
Floats go in by their bits (-0 as 0). The first aid state (`TreatTarget` and `ResumeTask` while the task is `Treat`, `MedicJob` when on) goes in only while it is set, so a world that never uses first aid hashes as it did before it existed. It is a test and debugging tool, not run in play.

**Determinism tests** (`tests/Meitou.Tests/Simulation`). A dummy workload (`WanderSystem` in `WanderWorkload.cs`: seeded wandering,
separation over the grid, a synthetic ground that dips under the water, blows on the nearest neighbour, births and deaths that reuse slots) and
the real systems on a synthetic town (`SyntheticTown`) and on The Hub of the install run at 1, 4 and 16 threads; the hashes at the checkpoints
agree, two runs agree, another seed differs. `GoldenHashTests` pins the hashes of twelve scripted scenarios (wander, player, roaming, bodies,
fight, duels, two worlds cut into many partitions with `MinPartitionSize` 4, a first aid and rest script on hand-made characters, and `aggro`: five squads of five
factions round a crossroads with a player character, fights started by hostility, self-defence and an assault that changes the relations, cut into partitions of one slot, and `ranged`: crossbowmen against katana fighters in one-slot partitions), so a refactor meant to change nothing is checked to change
nothing; a change of behaviour that is meant updates the numbers in the same commit. **Re-pinned 2026-10-13** (`bodies` from its sixth checkpoint, `fight` from its second, `town-parts` from its second): the XP on the move of `BodySystem` (athletics while running, strength under a load) raises stats a little, which feeds the speed chain; the first aid system, the rest flag and the fighter encumbrance moved nothing there (switching only the XP off made all eight old pins match again), and the new `first-aid` scenario agrees at 1, 4 and 16 threads before being pinned. **Pinned 2026-10-13**: `aggro` (agrees at 1, 4 and 16 threads first;
`AggroTests`); the hostility work moved none of the nine others (they do not run the aggro system, and the deferred list and self-defence change nothing in them).
**Pinned 2026-10-13**: `ranged` (agrees at 1, 4 and 16 threads first; `RangedTests`); the ranged code moved none of the ten others (their worlds have no crossbows, and the ranged state is hashed only once used, [Ranged](#ranged)).
**Re-pinned 2026-10-13**: `town-parts` from its first checkpoint, for the stack limits ([Items and eating](#items-and-eating)): its guards' record lists give two rations, which
were one item of quantity 2 and are now two cells (ITEM `stackable` 1); the world agrees at 1, 4 and 16 threads first (its test was folded into the `town-parts` pin of `GoldenHashTests` in the 2026-10-13 test cleanup, which also runs items),
and none of the eleven other pins moved.
**Pinned 2026-10-13**: `carry` (agrees at 1, 4 and 16 threads first; `CarryTests.Scripted`: an ordered lift, carry and put-down, a rescue by the job whose carrier is then knocked out, the dropped body lifted again, an NPC leader rescue, a witnessed loot and a loot of an emptied body); the carry code moved none of the eleven others (they do not build the carry system, and its fields are hashed only while set, [Carrying](#carrying-rescue-and-looting)).
**Pinned 2026-10-13**: `crime` (agrees at 1, 4 and 16 threads and with 16-slot partitions first; `CrimeTests.Scripted`: an assault on a townsman in view of two policemen, booked, the police
going after the wanted player character, and a second player character looting a knocked-out farmer before a policeman); the crime code moved none of the twelve others (no
other scenario builds the crime system, and the ledgers and pending crimes are hashed only while set, [Crimes and bounties](#crimes-and-bounties)).
The wild (2026-10-13, [Population](#population)) moved none of the thirteen: it runs only when `PopulationData.Biomes` is set, which no pinned scenario does; its own
world (`WildTests`: a synthetic biome with homeless squads and two nests) agrees at 1, 4 and 16 threads and in partitions of 4 slots.

**Asynchronous services** (paths, navmesh building, building a town's characters) run on their own threads with request and result
queues. In play a result is taken in at the first tick after it is ready, so timing can differ between runs. In the determinism tests they
run synchronously, so the test covers the simulation's own logic.

**Snapshots** (`WorldPublish.cs`, `WorldSnapshot.cs`). After each tick the world builds a `WorldSnapshot` of the living characters (a character
without an appearance is published with a null `Appearance` and drawn as a marker by the host) and keeps the one before. A `CharacterSnapshot`
holds id, appearance, position, yaw, animation layers (`AnimationLayer(record name, clip seconds, weight)`), faction, name, squad, `IsPlayer`,
`Selected`, `Body` (a `BodyStatus`: blood fraction, worst part, hunger, KO, dead) and, for selected characters only, `Skills` (a
`SkillSummary` of numbers the HUD formats), `Inventory` (text lines) and the remaining `Path`. `WorldSession` exposes `CurrentSnapshot`,
`PreviousSnapshot` and `SimulationAlpha`.

## World model

- **Project.** `src/Meitou.Simulation` (namespace `Meitou.Simulation`) depends on `Meitou.Data` only. Dependencies point one way: Core ← Data ←
  Simulation ← Engine ← Game, with Rendering and Navigation beside them; `Meitou.Game` is the only project that knows both the simulation
  and the navigation.
- **Entities.** Characters live in the `CharacterTable`; squads (`Squad` in `SquadRegistry`, `World.Squads`), roaming squads (`Platoon` in
  `PlatoonRegistry`, `World.Platoons`, the original's active and unloaded halves, [game-loop.md](game/game-loop.md#factions-and-squads)) and the
  player (`PlayerState`, `World.Player`) are plain objects; the registries are `SortedDictionary`s, so they enumerate in a fixed order. Towns,
  factions' state, buildings and loose items have no world state yet. Ids map onto the save's handles (`{Type, C, CS, I, S}`,
  [save.md](formats/save.md)) through the save mapping.
- **Character table** (`CharacterTable.cs`). `CharacterHot` is plain data every tick touches: `Alive`, `Generation`, `Position`, `Velocity`,
  `Yaw` (0 along +Z, turning towards +X), `Mode` (speed mode), `Flags` (`MoveFlags`), `Goal`, `PathCursor`, `Animation`/`AnimationTime` (the
  pre-animation-system movement layers, still written and hashed), `Health` (blood in per cent), `Task` (`CharacterTask`) and `TaskTime`,
  `MaxSpeed`, `WalkSpeed`. `CharacterCold` is an object per slot for the rest: identity (`Name`, `Faction`, `RecordId`, `SpawnedTick`,
  `Appearance`), squad (`SquadId`, `Role`, `FormationOffset`), movement (`Path`, `PathRequest`, `OrderQueue`, `FootprintRadius`, `WaterFactor`),
  `IsPlayer`, animation (`Animation`, `DrawnWeapon`), body (`Race`, `Stats`, `Medical`, `DiedTick`), items (`Inventory`, `NextProvisionTick`),
  combat (`InCombat`, `Fighter`) and `Save` (the link to a loaded save's character).
- **Ids.** `CharacterId` is (slot, generation). Slots are reused most-recently-freed first with the generation raised, so an old id never
  names the new character; the free list is part of the state hash. `CharacterTable.PeekNextId` gives the id a spawn will get (the body
  rolls are keyed by it before the spawn).
- **Static data** (`src/Meitou.Data/Gameplay/`). Typed views built once at load over `GameRecord`: `GameConstants` (the fields of
  [game-loop.md](game/game-loop.md#settings-and-constants) and [character-stats.md](game/character-stats.md#constants-used-by-this-subsystem)
  with the loader's rescalings applied, so a property is the stored value; a missing field takes the editor's default where the doc lists
  one), `FactionData` + `FactionRelations` (the initial table of [section 3.2](game/factions-squads-towns.md#32-initial-values-verified):
  explicit entry, else the smaller default, self 100; hostile at -30 or below, ally at 50 or above), `SquadTemplate` (section 4), `TownData`
  (section 7, with the setup rules for `town radius mult` and the NEST_MARKER foliage range), `NewGameStart`, `AnimationLibrary`, the body
  views in `Gameplay/Bodies/` (`RaceData`, `BodyPartTemplate`, `LimbReplacement`, `StatsEnumerated`, `StatsData`) and the combat views in
  `Gameplay/Combat/` (`CombatDatabase`). `RecordReading.cs` holds the shared record helpers and `ConstantsRecord` (finding the CONSTANTS
  record). AI packages and tasks are still read by field name. Probe tables are `[Slow]` tests: the CONSTANTS values, 103 factions with
  10,302 NPC pairs of which 3,356 hostile and 62 allied and 221 explicit entries, 959 squad templates, 346 towns by type.
- **Population data** (`PopulationData.cs`). What the systems share about the game: the database, the placed towns (`TownSite`), the
  factions and their index (`FactionIndex`), the player's faction (Nameless), the relations, the body options, and lazily the
  `BodyFactory`, `CombatDatabase` and `ItemFactory`. `CharacterAssembly` holds the race lookups and the body, fighter and inventory attachment
  shared by the population and the save loader.

## Systems and their order

`StandardSystems.Build(data, walkability, options)` (`src/Meitou.Simulation/StandardSystems.cs`) builds the system list the game runs; the
host never lists systems, and tests that mean the game's wiring ask it for the parts they need (`StandardParts`). The order is part of the
result (the golden hashes pin it):

| # | System | Phases it implements | Job |
|---|---|---|---|
| 1 | `PopulationSystem` | Inputs, SlowWorld | Zones, residents, bar and roaming squads, the wild (homeless squads and nests), the player's squad |
| 2 | `PlayerSystem` | Inputs, SlowWorld | Selection and stop; prunes the selection |
| 3 | `AiSystem` | Think, SlowWorld | Blackboards and package choice of squads with AI packages, the members' goals and their act-out ([AI](#ai)) |
| 4 | `PursuitSystem` | Schedule | Paths for attackers towards their targets |
| 5 | `MovementSystem` | Inputs, Schedule, Think, Move | Orders, paths, tasks, steering |
| 6 | `FirstAidSystem` | Inputs, Schedule, Act, Apply, SlowWorld | First aid and resting: treat / rest / medic job orders, the walk to the patient, the bandaging, NPCs treating by themselves ([Bodies](#bodies)) |
| 7 | `BodySystem` | Act, SlowWorld | Medical tick (resting from the `Rest` task), top speed, XP on the move, the fighter's encumbrance, down characters; removes corpses |
| 8 | `CarrySystem` | Inputs, Schedule, SlowWorld | Pick up, carry, put down, loot, the rescue job and NPC leader rescue ([Carrying](#carrying-rescue-and-looting)) |
| 9 | `CombatSystem` | Inputs, Schedule, Move, Act, Apply, SlowWorld | Fights, melee and crossbow (`Schedule` resyncs its slots with the table and flies the bolts, [Ranged](#ranged)) |
| 10 | `AnimationSystem` | Act | Animation layers |
| 11 | `FeedSystem` | SlowWorld | Eating |
| 12 | `RetaliationSystem` | SlowWorld | Attacked characters fight back (NPCs always, the player's while idle) |
| 13 | `AggroSystem` | Schedule, Act, Apply, SlowWorld | NPCs attack enemies of their faction they see or hear (the original's figures); the leash; back to the routine after a fight; hashes the live relation table ([Hostility](#hostility-and-relations)) |
| 14 | `FleeSystem` | SlowWorld | NPC squads of factions with run-away ratios break and run when beaten, until safe ([Hostility](#hostility-and-relations)) |
| 15 | `CrimeSystem` | SlowWorld | Pending crimes from the player's deeds (reported by combat and carrying), witnesses, booking by the police, bounty expiry ([Crimes and bounties](#crimes-and-bounties)) |
| 16 | `TradeSystem` | Inputs, SlowWorld | Shops of trader squads (stock, purse, restock), the open trade, buying, selling and fencing ([Trade](#trade)) |

The rules: **population first** (it spawns in `SlowWorld`, so later systems see its characters); **player before movement** (a
`MoveOrder` without ids reads the selection the player system just changed); **AI after the player, before pursuit and movement** (2026-10-14: its `Think` writes a
character's task and path flags and the movement system's `Think` follows them the same tick; its `SlowWorld` sees the squads the population made this tick; it adds nothing to
the hash until a squad has AI packages, so inserting it moved no pin); **pursuit before movement** (pursuit raises `NeedPath` in
`Schedule` and movement answers the same tick); **first aid after movement, before body** (2026-10-13: a move order is the movement system's and sets the task, so it replaces a treatment or a rest; the treatment is applied in the serial commit, after every `Act` of the tick including the body system's medical tick; first aid reads the patient only in its own `Act` barrier, where nothing writes it); **body before combat** (the body system ticks the medical state, combat runs with
`TickMedical` off and reads the result); **combat before animation and retaliation** (combat swaps its read and write arrays at the end of
its `SlowWorld`: a system after it sees the new combat state, one before it the old); **carry after body, before combat** (2026-10-13: its
`SlowWorld` sees this tick's knock-outs and removed corpses, so a carrier that goes down drops the body in the same tick; its `Inputs` run before
combat's, so a carrier ordered to attack lets go first; everything it does is serial); **feed after body**; **retaliation, then aggro, last** (both defer
orders for the next tick after combat swapped; the aggro's leash sweep sees the tasks every other system left this tick, and its look in `Act` reads only the last tick's
positions and the cold state no other `Act` writes). The game's `CombatOptions` are `SelfApproach = false, TickMedical = false`. The sandbox builds `All` without `Aggro`
(its hostile foe stands 30 units from the hero and must not start a fight by itself). **Crime last** (2026-10-13): its witness sweep sees the positions, carry links and pending crimes every other system left this tick; the deeds come in from combat (`Inputs`, the commit, the bolt flight in `Schedule`) and carrying (`SlowWorld`), all serial. **Trade after crime** (2026-10-14): a caught fence is reported to the crime system from the trade's `Inputs`; shops are opened and restocked in `SlowWorld`, after the population made the tick's squads. It adds nothing to the hash until a squad has a shop.

## Population

`PopulationSystem` (`PopulationSystem.cs`, `.Build.cs`, `.Player.cs`, `PopulationRoaming.cs`; settings in `PopulationSettings`).

- **Zones** (`ZoneActivation`). The focus activates every zone overlapping the box of +-340 units round it (**Observed**), plus a ring of one
  more zone when `Fast zone hopping` is on (**Verified**, [game-loop.md](game/game-loop.md)). The focus is the player's characters when a
  player exists, else the camera's target (`FocusCommand`, sent by the host when the camera has moved 50 units). Zones are looked at every 15
  ticks. `ActiveZones` is double-buffered: the host reads it within a frame, after the ticks. `ZoneGate` lets the host hold a zone back
  until its navmesh is ready.
- **Squad factory** (`SquadFactory.Plan`, pure given template, multiplier, seed and key;
  [factions-squads-towns.md 5.1](game/factions-squads-towns.md#51-counts-and-layout-observed-two-functions-share-the-count-rules-see-the-sentinel-note)):
  counts (v0 when v1 is 0 or the sentinel 100, else a uniform integer in [v0, v1], v1 below v0 gives v0; trunc(n x M) and at least 1 unless
  `dont multiply`), the creation order and roles (leader 2, `choosefrom` picks weighted with 0 as 100 and gated by world states, `squad` role
  0, `squad2` role 1, `animals`, `animals2`, `slaves` as squads of their own with role 4), the layout offsets (rows of 8, 3 apart, odd ones 1
  back; animals2 5 apart) and the animal age. Missing references become messages in `SquadPlan.Problems`. Not made: `prisoners` (they need
  cages), `housemates`, the unique-leader replacement, AI packages. World states start all false (**Unknown** in the original, 3.5,
  `IWorldStates`), so squads and `choosefrom` entries gated by one do not appear.
- **Residents** (6.4). A placed town in an active zone gets the town's `residents` plus the faction's, unless the town overrides with a list
  of its own; an entry is v0 squads, v0 = 0 dropped. Squads stand at seeded spots within 0.6 of the town radius (`size radius` x `town radius
  mult`) of the placed position, on dry ground. **How the original picks a home building is Unknown**, so this is an engine choice, and so is
  the unload grace of 60 game seconds (the original has three real-time timers of **Unknown** length): a town whose zone has been inactive
  that long loses its characters and gets new ones (new generations) when the zone comes back. Residents are not platoons; the town is their
  stand-in.
- **Bar squads** (6.4). The town's `bar squads` plus, for towns of type Town, the faction's: each entry is v0 squads, each present with a
  chance of v1 per cent (0 read as 100), made when the town loads, placed and walking like residents; the bar building is not modelled, so
  they do not sit.
- **Characters.** Members come from the squad plan; appearances from `CharacterGenerator` through `IAppearanceSource` (seeded from the world
  seed, the town, the squad and the member). Building runs on one background thread in the game (`PopulationSettings.Background`) and inside
  the tick in tests; results are taken in at the first tick after they are ready. Every member with a race gets a body, a fighter and an
  inventory (`CharacterAssembly`, [Bodies](#bodies), [Items and eating](#items-and-eating)) and its top speed from `Speed.Run`. Animals (ANIMAL_CHARACTER) get
  their record's race and an animal's body and natural weapon ([Animals](#animals)), an empty inventory and their species' look (`CharacterAppearance.BuildAnimal`, one
  shared instance per ANIMAL_CHARACTER from `GeneratedAppearances`; since 2026-10-13, before that they were drawn as markers).
  A member without a race runs at the stand-in 45 (`CharacterAssembly.StandInMaxSpeed`) and walks at 15. Resident squads are `Squad` objects (leader, members, home) in `World.Squads`, hashed.
- **Roaming squads** (`PopulationRoaming.cs`; 6.2, 6.4, [game-loop.md](game/game-loop.md#factions-and-squads), [ai.md](game/ai.md)). A loaded town
  with `roaming squads` draws one squad per look (every 60 ticks, 2 game seconds) while its pool is below the cap: 0.7 of the faction's
  `roaming population` (50 by default: 35 characters; the 0.7 is the nests' rule, for towns **Unknown**) times the population multiplier. The
  candidate weight is `max(0, v0 - squads of that template the town has)` (6.2's town branch); a squad that would pass the cap is skipped; a
  spawn within 36 units of a player character is refused. A roaming squad is a `Platoon` (hashed): the town it counts against, the template
  and key that reproduce its members, a position, a target, its money. It walks to one of the nearest four settlements (outposts, towns,
  villages, military) not hostile to its faction, waits 30 to 120 s there, goes home, waits, picks again. While a zone near it is active it
  has characters (the leader runs to the target, the others follow; at a town they mill about); when none has been for 10 s its characters
  are removed and **a stand-in moves at 25 units per second (90 beyond 25000), no physics, no paths**, the numbers of the original's
  `UnloadedPlatoon` (units not verified). When a zone near it becomes active the members are made again from the key at the stand-in's
  place. A squad that loses all its members leaves the pool. While its leader fights, the trip waits (an order would replace the fight's task).
- **The wild** (`PopulationWild.cs`, 2026-10-13; 6.2, 6.3). Only when `PopulationData.Biomes` is set (the game host builds it from `areasmap.tga` and the
  BIOME_GROUPs; worlds without it, every pinned scenario among them, make none). Every in-map zone cell is an **area sector** of its group; a sector whose
  group has `homeless spawns` and an amount gets, when its zone activates, ceil(P x `homeless spawn amount`) squads: what the original's area tick reaches
  for a sector loaded alone (it spawns while the island is below P x its amounts and the sector below its own share). Engine choices: the island-wide sum over
  several loaded sectors, the counters a and b and the halving near towns are not modelled; the squads are made at once, not one per 2 s. Templates by 6.2's
  homeless weights `max(0, v0 - k x count)` with k = (sum of v0) / (P x amount) over the sector's own picks, world-state gated templates skipped; the spot by
  `FUN_1408f50d0`'s rules (out of the nearest town to 2x its radius, other factions' towns to 1x, 1382.4 from every player character, walkable, 40 tries; else
  that squad is not made); none when the nearest town is the player's. They wander within 300 of their spot (`HomelessHomeRadius`, engine choice).
  **Nests** are laid out at the first look from the world seed (the original adds one per area tick up to `num nests` x the *Number of nests multiplier*,
  default **Unknown**, 1 here; the end count is the same): per group, a weighted pick of the `nests` TOWN (v0, 0 as 100, its `world state` gate, so with all
  world states false the Raider Camp and Gurgler Nest never appear), a seeded cell of the group with up to 8 candidate spots outside 2x the radius of every
  placed town (the first walkable one is the nest when it loads; none: dormant), or for `nests at fixed markers only` one of the area's placed NEST_MARKERs.
  With the base game and seed 1: 324 nests (62 Nest of Beak Things, 59 Wolf Den, 46 Swamp Raptor Nest, 34 Crab Nest, ...) and 2674 sectors with homeless
  spawns. A loaded nest fills its roaming pool to floor(0.7 x `nest resident population`) members from the TOWN's `roaming squads` (weight max(0, v0 -
  count); an entry whose expected size (6.1, `SquadFactory.ExpectedSize`) passes the free room is skipped), spots within 0.6 of the nest radius, not
  within 36 of a player character; they wander in the nest radius. A nest does not replace its dead while loaded (the respawn delay is **Unknown**).
  Wild sites load and unload like towns (zone gate, 60 s grace, built on the population thread, taken in at a tick boundary); every roll is keyed by the
  world seed and the zone or nest and the squad's number, never the tick, so a reload makes the same squads (the spots of homeless squads move when the
  player stands elsewhere). Wild squads are plain `Squad`s (`Town` -1), not platoons: they do not travel between towns and are not saved.
- **Not made:** roads for long trips (`road preference`), the AI package subclasses (`GoOutOnPatrol` and others; the plain packages run, [AI](#ai)), faction campaigns,
  the island-wide homeless budget and nest respawns (6.2, 6.3), the nests' debris and loot (section 8), unique squads, the residents' unloaded half (a town is all or nothing).

## Player

- **New game** (`meitou --new-game [start]`, default `Wanderer`; `--list-starts`). `NewGameStart` reads the NEW_GAME_STARTOFF record: money,
  start position and its `force pos` flag, squad, towns, faction relations, research, `force race` (**Verified** against the base records,
  `New_game_starts_match_the_documented_records`). `PopulationSystem.StartPlayer` builds the player's squad: the first listed town that is
  placed in the world, its centre, else the record's `StartPosition` when `force pos` is set (Rock Bottom); a seeded walkable spot within a
  third of the town radius. Every squad link of the start goes into one squad (SQUAD_TEMPLATE links through `SquadFactory`, CHARACTER links
  as single members; the first member leads when no template names one), faction `Nameless`, money from the record. The host loads the world
  round that point (`--at` is overridden), waits for the navmesh of the zones round it, then puts the camera on the leader. **Unknown** how
  the original applies at start, so not applied: the faction relation overrides, research, `force race`.
- **Player state is world state.** `PlayerState` (faction, money, squad, selection) lives in `World` and is hashed, so a recorded command
  stream replays exactly. `PlayerSystem` applies `SelectCommand` (additive or replacing; non-player characters dropped) and `StopCommand` in
  `Inputs`; dead or removed characters leave the selection after the commit. A `MoveOrder` with an empty character list orders the current
  selection (what the UI sends, so it never needs ids at click time).
- **Orders.** A plain move order replaces the character's order and queue; `Queued` (shift) appends to `OrderQueue`; on arrival the next target
  is popped; a blocked or failed path drops the queue; `Stop` ends the go-to and clears the queue. Several characters get a block of slots
  round the click (`Formation.Place`, [Movement and paths](#movement-and-paths)); queued orders get their own block. A right click on a living
  character that is not the player's orders the selection to attack it (`AttackOrder`; hostility is not checked: **engine choice**, the
  original attacks enemies and the red cursor says so, [ui-input.md](game/ui-input.md)). Attacking someone of another faction is an assault on that faction
  ([Hostility](#hostility-and-relations)).
- **Interface** (`src/Meitou.Game/PlayerInterface.cs`, `.Picking.cs`, `.Hud.cs`; host side only: it reads the published snapshot and enqueues
  commands at the world's tick):

| Input | Effect |
|---|---|
| left click | select the player's character under the cursor (a capsule from the feet, `PlayerInterface.CharacterHeight` 2.2 units tall, radius 1 or 1.2 % of the distance); a click on nothing deselects |
| left drag | box select: player characters whose chest projects inside the rectangle and lie within 7500 of the camera |
| shift | adds to the selection (click, box, digit, grave) |
| right click | on a living other character: attack it; else a `MoveOrder` to the ground under the cursor (ray marched against the CPU heightmap, then bisected); shift queues |
| `1`..`9` / `` ` `` | select the n-th player character / the whole squad |
| `R` | stop: `StopCommand` (combat disengages, the player system stops); not in the free camera, where `R` is up |
| right click on one of your own hurt characters (blood or a part below full; `BodyStatus.NeedsAid`) | the selection treats it with a first aid kit (`TreatOrder`); a lone selected character on itself treats itself. The original's verb is "First Aid" in the hold-right-button context menu (not built); engine choice |
| `H` | switches the medic job on or off for the selection (`MedicJobCommand`; the original's `medic` toggle is NUM7, which the cinema camera owns here); the HUD shows `medic job (H)` |
| `L` | the selection sits down and rests (`RestOrder`; no binding in the original, engine choice); a move or stop order ends it |
| Ctrl + right click on a knocked-out or dead character | the nearest selected character picks it up (`PickUpOrder`); on a body a selected character carries, puts it down (`PutDownOrder`). The original's verbs "Pick Up" / "Put Down" are in the hold-right-button context menu (not built); engine choice |
| right click on one of your own downed characters that needs no first aid | picks it up (`PickUpOrder`; the original's quick right click does the same, [carry-loot.md](game/carry-loot.md#31-the-players-orders)) |
| Alt + right click on a knocked-out or dead character | the nearest selected character takes everything that fits (`LootOrder`, [Carrying](#carrying-rescue-and-looting)); the original opens a loot window instead (not built) |
| `J` | switches the rescue job on or off for the selection (`RescueJobCommand`; the original's `rescue` key is NUM8, which the cinema camera owns here); the HUD shows `rescue job (J)` |
| `K` | the selected carriers put their bodies down (`PutDownOrder`); the HUD shows `carrying (K puts down)` |
| right click on a living trader (`CharacterSnapshot.IsTrader`) | the first selected character walks up to it and the trade window opens (`OpenTrade`, [Trade](#trade)); a trader of a hostile faction is attacked instead, as any other character. The original's right click on a neutral talks (dialogue, not built); engine choice |

Right mouse is the command button, so **orbit is the middle button only**; saved configs that bound `Mouse:Right` to orbit lose that entry on
load. The HUD (top left, `DebugOverlay` text and quads): game clock and day, money, clickable speed buttons (pause, 1x, 2x, 5x, the active one
lit), the selected characters' names; for the first selected character `Health: blood n%  worst part n%  state`, `Hunger: n of 3`, the trained
stats (attack, defence, dodge, toughness, strength, athletics) and up to 9 inventory lines; above the stats, in red, up to three `Bounty: c.n {faction} ({crimes}) expires in n hrs`
lines (or `never expires` from 10000; the original's status panel words) and, with several selected, the sum of their bounties ([Crimes and bounties](#crimes-and-bounties)). Selected characters get a green ring and their
remaining path a yellow line. A click on the HUD never selects. Screenshot option: `--move-to <x> <z>` (selects the squad and orders it to walk)
(with `--new-game --screenshot`); a `pickcheck` line in the log checks that the leader's own pixel picks it and a ray 60 px lower lands near it.

## Movement and paths

Facts: [pathfinding.md](game/pathfinding.md) ("Movement", "Speed", "Local avoidance and formation"). `MovementSystem` (`MovementSystem.cs`,
types in `Movement.cs`: `CharacterTask`, `MoveFlags` with `AnyPath`, `SpeedMode`).

- **Speed.** Top speed S in units (dm) per second is `MaxSpeed` (set by the body system from `Speed.Run`), capped by the speed mode: 0 the
  race walk speed, 1 at 55, 2 none. Wanderers walk, followers copy their leader's mode and run to catch up beyond 60 units, orders run.
  A path is followed at that speed at once (the nav agent; docs/game/pathfinding.md); the 15 per second ramp only in combat; arrival within 10 units.
- **Separation.** Repulsion proportional to 100 x (1 - d/R), R = 8 (twice the footprint radius 4), at most 26 neighbours, over the last tick's
  positions; the scale to a speed (0.15) is an engine choice.
- **Ground.** Every tick a character samples `IWalkability.GroundHeight` (on the navmesh that is the mesh where it is off the terrain, so it
  stands on floors) instead of interpolating between path points. Movement stays on walkable ground; a blocked character drops its path.
- **Tasks.** `Wander` (a random point within the squad's home radius, then a wait of 3 to 10 s), `GoTo` (a `MoveOrder`), `Follow` (the
  formation slot beside the leader, the factory's offsets x 2.5, repathing every 0.5 s when more than 14 units away), `Attack` (moved by
  `PursuitSystem`, faced by combat). A knocked-out or dead character is not moved (velocity 0, no separation push, orders ignored).
- **Formation** (`Formation.Place`). A move order for several characters sends each to a slot of a block centred on the click: about as wide as
  deep (`ceil(sqrt n)` columns), 9 units apart (twice the footprint radius plus a margin), rows across the line from the group's centre to the
  click; the characters nearest the click take the front row, and within a row the left-to-right order they have is kept so paths do not cross.
  The original's slot rules are **Unknown** (FACTION `squad formation` RANDOM / CARAVAN / MILITARY is not read).
- **Paths** (`PathService.cs`). `Think` flags a character that wants a path (`NeedPath`); the next `Schedule` step submits a `PathRequest`
  (with the character's footprint radius and water factor) to the `PathService`, which answers on a thread of its own in the game and inside
  the call in tests (`SynchronousPaths`). Answers are applied in a serial step in request order; a late answer to an older request is ignored
  (`CharacterCold.PathRequest`). A `RepathCommand` makes every character with a go-to ask again. The movement system owns and disposes the
  service.
- **Walkability** (`Walkability.cs`). `IWalkability`: `GroundHeight`, `IsWalkable`, `FindPath` (any thread). `IAgentWalkability` adds
  `FindPath(from, to, footprintRadius, waterFactor)`. `OpenGroundWalkability` is the stand-in the tests use: the terrain (from
  `Meitou.Data.World.GroundHeights`, the CPU heightmap window plus the coarse whole-world grid, immutable) everywhere above the water, no
  obstacles, straight paths refused when they dip under the water. The game uses the navmesh through `Meitou.Game/NavAdapter.cs`, the
  only file that knows `Meitou.Navigation`; the sandbox uses the stand-in.
- **Turning (engine):** the heading turns at most 720° per second and the speed is scaled by the cosine of the turn still to make, so a
  character turns about on the spot before it runs (`MovementSystem.TurnRate`, [pathfinding.md](game/pathfinding.md#turning-and-orders-on-the-move-ours)).
  A new order on the move keeps the old path until the new one arrives.
- **Not modelled:** water states and swimming, the shallow-water speed cap, roads for long trips, the combat speed multiplier.

## Navmesh

Facts about the original's navmesh, the generator inputs (groups, masks, path modes, seeds, settings), what was observed in The Hub, and the
open questions are in [pathfinding.md](game/pathfinding.md#what-our-builder-needs); the collision files in
[collision.md](formats/collision.md#reader-meitoudataphysics). Owner decision 3: our own Recast-style builder with our own cache, the
shipped Havok tiles only a reference (the original regenerates whenever a zone's buildings differ, so a drop-in replacement needs a
generator in any case).

- **Pipeline** (`src/Meitou.Navigation`). `ZoneGeometryGatherer` (partials `.Terrain`, `.Buildings`, `.Foliage`, `.Seeds`; `InteriorGatherer.cs`)
  collects a zone: terrain with the Y 100 water clamp, building and foliage collision from `Meitou.Data.Physics` with the mask and path-mode
  rules (60 degree building, 40 degree terrain and foliage slopes), interior-mask carvers skipped for `is gateway` buildings (the
  approximation of `BCTYPE_SHELL_WITH_INTERIOR`), door painters grown by one cell (`DoorInflateCells`), seeds. Foliage of the zone's ring is
  placed in parallel on a pool of `FoliageWorld`s sharing decoded overlay tiles and kept per zone, so the next zone places only its new
  neighbours. Placements are resolved by `Meitou.Data/World/BuildingPlacements.cs`. `ZoneNavMeshBuilder` (`.Stitch`, `.Doors`) runs a tiled
  DotRecast build (`NavBuildSettings`: cell 2, cell height 1, tile 48 cells, agent height 18, max climb 5; areas ground, water, door; monotone
  regions) with our own tile stitching; `SeedPruner` keeps the regions near seeds (`NavSeeds`: the `seeds.def` ray rule, linked-wall seeds,
  the 3 x 3 ground fallback; `NeighbourSeeds`: midpoints of the open borders of the loaded 4-neighbours, so a mesh depends on which neighbours
  were there first). `NavMeshPipeline.BuildZone` builds the exterior plus the interiors. No detail mesh: heights are the simplified surface
  (about 0.5 off on flat ground), so `GroundHeight` returns the heightmap where the mesh lies within 3 units of the terrain and the mesh
  elsewhere.
- **Interiors** (`NavInteriors`). Every building with an `interior mask` part that has collision (and is not `is gateway`) gets its own small
  mesh: its shapes in the interior mask, furniture standing inside the hull, the door painters, and the inverted hull (slabs beyond every hull
  edge remove everything outside it), seeded at the door's inner marker, built with the same tiled pipeline on a square of whole tiles, then
  appended to the zone's mesh and joined to the exterior along the door polygons of the same door (edges within 6 units in plan and 12 in
  height become links). To queries an interior is part of the zone.
- **Doors** (`NavDoors`, `walkability.Doors`). Door polygons keep area `Door` and the building they belong to (`ZoneNavMesh.DoorOf`); `Close`,
  `Open`, `Set`, `IsClosed` by the building's placement id, `Version` for callers that keep paths. A flip is one volatile write, no rebuild.
  Doors start open; `NavAgent.DoorsClosed` treats every door as closed for one query. Nothing in the game opens or closes doors yet.
- **Queries** (`NavQuery`, `NavWorld`). A* over polygons with portal clearance (2 x the agent's radius, default 4), the water factor, the door
  rules and a funnel; `NavWorld` joins loaded zones with cross-zone links between 4-neighbours (zones meeting only at a corner share a point,
  through which no agent fits).
- **Cache** (`NavMeshCache`). One file per zone under `%LOCALAPPDATA%\Meitou\navmesh`, keyed by the zone's building hash and a hash of the
  settings with `NavMeshCache.BuilderVersion` (3). The key does not cover foliage, record contents, `seeds.def` or the load order (an open item).
- **Public API.** `NavSystem` (builder thread; `LoadZone` returns a task, `LoadRing`, `UnloadZone` keeps the cache file, `ZoneReady`, `Doors`,
  `Walkability`, `Dispose`), `NavmeshWalkability`, `NavDoors`, `NavAgent`, `NavZoneReady`, `NavMeshOrigin`, `NavBuildSettings`, `NavMeshCache`,
  `NavWorld`, `NavRef`, `NavLink`, `ZoneNavMesh`, `NavQuery`, `NavArea`; the rest is internal (tests and `meitou-tools` through
  `InternalsVisibleTo`).
- **Threading.** `IsWalkable`, `GroundHeight`, `FindPath` and `Doors` are safe from any thread: loaded meshes are immutable and swapped in
  atomically, and a query sees a zone only when it is complete. Where a zone, or the start or goal of a path, is not loaded, the open-ground
  stand-in answers, so a query never waits. `FindPath` costs 20 to 50 ms for a town crossing: it runs on the path service's thread, never in a
  tick phase.
- **In the game** (`GameHost`, `NavAdapter`). The host loads the navmesh of `PopulationSystem.ActiveZones` each frame (idempotent) and unloads a
  zone inactive for 30 real seconds. Two holds keep the stand-in from being seen: `PopulationSystem.ZoneGate` keeps a town from loading until
  its zone's mesh is ready, and a new game (and a screenshot) waits for the zones round the start before placing the squad. A path asked for
  across a zone not ready yet is straight; when a mesh arrives the host sends a `RepathCommand`. `NavAdapter` hands the footprint radius
  (`CharacterCold.FootprintRadius` from the race's `pathfind footprint radius`, 0 for the default human) and the water factor
  (`CharacterCold.WaterFactor` from the race's `water avoidance`: a + 1, or 1 / (1 - a), halved for the player's faction) to `NavAgent`.
- **Tool.** `meitou-tools navmesh` (`tools/Meitou.Tools/NavmeshTool.cs`; usage in `Program.cs`): a zone or town, the ring (`--around`),
  geometry/OBJ/PNG output, a path (`--path`, `--closed` for closed doors), `--no-interiors`, `--verbose`,
  `--repeat n` (timings), `--fingerprint` (SHA-256 of the cache files of The Hub and its pinned path points: the gate for refactors). Images
  for review live outside the repo (`R:\VlcekM\MeitouClient-re\probes\nav\out\`).
- **Tests.** `HubNavmeshTests` (`[Slow]`): the pinned path points, gathered geometry stats, and door properties (the Storm House, placement 4933, is
  entered through its door and shut when it is closed). `HubNavigationTests` (`[Slow]`): the Wanderer's order across The Hub reaches the goal without leaving the mesh and with a detour
  of at least 10 % over the straight line.
- **Not done.** Exterior layouts and furniture the placements do not list, `interior terrain` buildings, a seed for interiors without a door,
  upper floors (in the mask, untested), an interior poking past its zone's border keeps the zone's bounds, parts with collision but no `.mesh`
  are dropped by the layout, no viewer overlay.

## Animation

The original's rules (decompilation) are in [animation.md](animation.md#run-time-blending); the data facts in
[animation.md](animation.md#selection-data-used-by-the-simulation). `AnimationSystem` (Act, after movement and combat, own characters only),
`CharacterAnimation` (cold, hashed), `AnimationLibrary` / `AnimationDefinition` / `AnimationStance` / `HandHold` / `AnimationLengths` in
`Meitou.Data/Gameplay/AnimationLibrary.cs`; the renderer's masks in `Meitou.Data/Characters/AnimationMask.cs`.

- **Layers.** Up to 8 weighted clips per character (`CharacterAnimation.MaxLayers`). Each tick the system picks what should play, fades the
  weights linearly at the CONSTANTS `animation blend rate` (4 per second, the same for every clip: engine choice), advances the times; the
  first clips a character plays start at full weight. `Publish` normalises as Kenshi's layers do: the upper body (upper and whole-body clips)
  to at most 1, the lower body to at most 1 minus the whole-body clips (`all` clips and actions; a block keeps the legs' clips); overlays alone.
  The renderer blends **cumulatively** (Kenshi sets it on every character's skeleton; the posture libraries add on top at weight 1).
- **Movement** (**Verified**). Every movement clip of a body half that fits the stance plays, weighted by the sigmoid of its speed ramp
  (`min speed` / `move speed` / `max speed`) times its leg ramp (the worse leg against that side's damage min / ideal / max), normalised
  (`AnimationDefinition.SpeedWeight`, `LegWeight`, `AnimationLibrary.Movement`). Synched clips share one phase per character advancing
  F x v x the weighted `play speed` of the lower clips **cycles** per second; unsynched movement clips run F x `play speed` x v clip seconds
  per second; F = 2 - H, H the body's movement scale (`CharacterShape.MovementScaleOf`). In combat v is signed along the facing, so backing off
  picks the `... combat shuffle long BK` clips. From 1 unit per second a character moves, else it stands (engine). The speed the animations see is the 3D velocity's length through Kenshi's 8-sample trimmed median (`CharacterAnimation.SmoothSpeed`; sampled per tick, not per frame, with the window cut to the same time as 8 frames at 60 fps, 4 ticks at 30 Hz: engine).
- **Idles.** Out of combat one of the whole-body idles by `idle chance`, kept for `idle time min` .. `max` seconds. In combat (engine
  choice after the original's footwork): the lower movement blend at the current speed (`walk lower combat shuffle short` at rest) under an
  upper-body guard idle (`AnimationLibrary.CombatIdles`: the `guard` records by the weapon kind in hand, `MA idle1` for fists; the hands'
  YES / NO fields are not tested because the original's arm check was not decoded).
- **Techniques.** COMBAT_TECHNIQUE records of humanoids (`animal` 8 or less) are definitions too (`AnimationDefinition.FromTechnique`,
  published by record name; the renderer's mask comes from `AnimationMask.FromTechnique`: a block that is not a dodge loses its lower-body
  tracks). The combat system's attack or reaction (`CombatSystem.Playing`, the last finished tick's state) sets the clip's time to progress x
  length, so the blow lands at its `anim blocked frame`. Attacks and dodges replace everything; a block plays over the legs' clips.
- **Crossbow** (engine choice of clips and timing, 2026-10-13). The RANGED records (category 8) are appended to the library after the techniques, so no other index
  moves, and are never chosen as movement, idles or stumbles. A character shooting (`CombatSystem.RangedPoseOf`) plays them like a technique: aiming raises `aimH`
  (clip `aimshoot`) through its first half over the 0.4 s steady time, the 0.3 s after a shot play its second half, a reload plays the crossbow's `reload anim` record
  (else `reload 1 phase`) stretched over the reload time. `aimwalk`, `shoot` and `stand 1 crossbow` are not used. What the clips look like was not checked in the viewer.
- **Hit reactions** (**Verified** rules): a hit records its tick, body part, heaviness (above the stumble threshold) and side on the combat slot
  (`CombatSlot.HitTick`, `HitPart`, `HitHeavy`, `HitBehind`). Unless the hit is light while a reaction is playing, one of the part's
  `stumbles` clips with `big stumble` = heavy-and-free and `stumble from` = the side plays to its end (`AnimationLibrary.Stumbles`;
  every base-game stumble has `chance` 0, so they are equally likely: engine choice). A technique cuts it short (engine).
- **Root motion** (**Verified** selection): `relocates` clips, attacks and dodges (not pure blocks) move the character by their `Bip01`
  track's ground part (`AnimationLengths.Root`, read from the skeletons) turned by its heading, onto walkable ground only; the renderer keeps
  only the height of that track. Fading one-shot clips keep advancing, so they keep moving the character, as in Kenshi. **Bodies collide** (engine stand-in for the physics controllers, 2026-10-12): the part of a root-motion step that would end nearer than the two footprint radii to another living character (last tick's positions, the grid's order) is dropped, the sideways part kept (`AnimationSystem.Relocate`); before, attacks that gain ground walked the fighters into one spot.
- **Down and up.** A character that goes down falls with the `Dodge back fall` technique's clip and lies in `sleeponfloor` (the original
  switches to its ragdoll: engine stand-in); coming round plays `standing up 3` to 86 % (**Verified**), cut short by walking or a technique.
- **Stance.** `CharacterCold.DrawnWeapon` and `InCombat` (set by combat) select the combat-mode and armed variants; the leg ramps use the lowest
- **Care** (engine choice, 2026-10-13). A standing character with the `Treat` task holds the whole-body `medic` clip (`is action`, loops), one with the `Rest` task `sitting idle`, through the same held-clip branch as the lying clip
  (`AnimationSystem.HeldClip`); walking, a technique or going down replaces them. There is no walk-to-kneel blend and the clip is not matched to the patient's position.
  `Fraction x 100` of each side's leg parts.
- **Carrying** (2026-10-13, [Carrying](#carrying-rescue-and-looting)). `AnimationStance.CarryingLeft` / `BeingCarried` select the carry clips; the
  `carry me` record (category 3) is appended after the ranged block, so no other index moves. A lifting or putting-down character plays
  `shoulder lift left` at an explicit time (forward from the lift's start, backwards for a put-down); a standing carrier plays a whole-body idle
  without the carry flag under `AnimationLibrary.CarryIdle` (`stand carrying left`); a walking one gets the `walk/jog carrying left` upper clips
  through the normal movement blend (lower-body clips ignore the carry flags, `FitsExceptLegs`). A carried body plays `carry me` instead of the
  lying clip, does not fall and is not pushed by root motion. The arm-only overlay is not used.
- **Animals** (2026-10-13; data in [animals.md "Animations"](game/animals.md#7-animations)). The library appends every ANIMAL_ANIMATION record and the creature
  techniques (`animal` 9 and up) after all the humanoid definitions (no hashed index of a person moves), in `AnimationLibrary.AnimalCategory` (-1) so no humanoid selector
  takes them; an animal's definitions are whole-body and published under their clip name (the records share names: two `sleeponfloor`s play `sleep` and `gotosleep`). Each
  ANIMAL_CHARACTER gets an `AnimalClips` from its own `animations` list: movement clips (non-zero `move speed`), idles (`idle`, by `idle chance`), and fall / lying candidates
  (its own `sleeponfloor` records, then every other one). `AnimationSystem` runs animals through `UpdateAnimal` with the clips its race's skeleton has
  (`AnimationLengths.For(race)`; a record whose clip the skeleton lacks, like the skimmer's `idle1`, is dropped): moving, the movement clips by their speed weights
  (unsynched, F x `play speed` x v clip seconds per second); standing, in a fight too, an idle (a non-looping one, `idle noloop`, only for its own length); a creature technique
  from the combat system over everything, timed by its own skeleton's length; down, `gotosleep` once, then `sleep` (or the fall's last frame held; a species with neither, the
  crab, robot spiders, gorillos, crimper, land bat, stays in its idle). No hit reactions (no animal record has `stumbles`) and no getting up (no clip; it fades back).
  **Engine readings**: F = 1 / the animal's size (the humanoid F = (2 - H) / g with H = 1 and g the race scale read as the size), and an attack's root motion is scaled by
  the size. The pinned scenarios have no animals (they run without the install), so no golden hash moved; `AnimalTests` still agree at 1, 4 and 16 threads.
- **Host.** Clip time and weight are interpolated between the two snapshots (a wrapped clip time shows the new value).
- **Not made:** strafing (`strafe lower` by the sideways speed), overlays (breathing, wound `pain anim`s, `carrying left arm only`), `head turning`, other
  `is action` clips, weather variants, the ragdoll, and the right-arm override clone of blocks.

## Bodies

Formulas, constants, units and the engine choices of the body code are in
[character-stats.md](game/character-stats.md#as-built-stage-7-meitousimulationbodies-and-meitoudatagameplaybodies). Code:
`src/Meitou.Simulation/Bodies/` (`CharacterStats`, `MedicalState` with `.Hits`, `.Tick`, `.Save`, `MedicalTypes`, `MedicalContext`,
`HealthPart`, `StatMultiplier`, `Speed`, `Encumbrance`, `XpService`, `BodyOptions`, `BodyRolls`) and, in the core, `BodyFactory`,
`BodySystem`, `BodyHash`.

- **Spawn** (`BodyFactory.Create`, through `CharacterAssembly`). Every character with a race gets `Race` (`RaceData`, read once per race),
  `Stats` (`CharacterStats.Create` from the STATS record the CHARACTER names, else from its five group fields, `CharacterStats.FromGroups`)
  and `Medical` (`MedicalState.Create(race, strength)`), seeded by the world seed and `Rng.Key(id)` (the id from `PeekNextId`). The five group
  fields fan out by the original's rule since 2026-10-13 (before, an engine guess). Animals get an animal's body ([Animals](#animals)).
- **Medical tick** (`BodySystem.Act`, own slots). For each living character with a body: `MedicalState.Tick(HoursPerTick, ctx, events)` with a
  `MedicalContext` (constants, options, race, toughness, strength, resting, encumbrance factor from the inventory, seed, character key,
  `BodyTimeScale`); blood, bleeding, part healing and degeneration, hunger, the KO timer. It sets `MaxSpeed = Speed.Run(race, stats, medical,
  encumbrance)` and `Health` (blood in per cent). `BodyTimeScale` (default 1) multiplies the time of the part and blood
  rates, not hunger or the KO timer. The medical tick runs once: the combat system's own (`TickMedical`) is off in the game; a test shows two
  ticking systems drain hunger twice as fast.
- **Down.** A knocked-out or dead character is stopped (orders and path dropped, velocity 0), whatever hurt it. A KO wakes when the medical tick
  says so and resumes wandering. A corpse is removed 12 game hours after death (`CorpseHours`; the original's rule is **Unknown**).
- **API** for later stages: `MedicalState.ChoosePart` and `ApplyHit(part, HitDamage, ctx, events)` (combat), `StatMultiplier(stat, StatUse)`
  (stat reads that should feel injuries and hunger), `Speed.Run` / `Speed.RunUnhurt`, `Encumbrance.Factor`, one shared `XpService` (`Combat`,
  `Continuous`, `StrengthFromCarrying`, `AthleticsFromRunning`, `Lockpicking`, `Medic`, `ToughnessFromLimbLoss`...), `MedicalState.Feed`,
  `Treat` and `KitDrain`. `CharacterStats` and `MedicalState` hold no references to shared mutable state, so characters tick in parallel.
- **Save keys.** `MedicalState.WriteSave` / `ReadSave` and `CharacterStats.WriteSave` / `ReadSave` use the keys of [save.md](formats/save.md)
  (`blood`, `bleeding`, `hung`, `fed`, `KO`, `flesh<k>`..., `strength`, `toughness2`...); limb states and wounds have no key there yet.
- **First aid** (`FirstAid/FirstAidSystem.cs`, 2026-10-13; facts in [character-stats.md](game/character-stats.md#first-aid-medkits-and-robot-repair), choices in its "As built"). Tasks
  `CharacterTask.Treat` (with `CharacterCold.TreatTarget`) and `Rest`. **Orders:** `TreatOrder(medics, patient)` (empty list = the selection; a patient among the medics treats itself),
  `RestOrder`, `MedicJobCommand`. A move, stop or attack order replaces the task, so it ends a treatment or a rest; a medic that is knocked out or fighting ignores the orders.
  **Walking:** `Schedule` is the pursuit system's pattern (a path to the patient at free speed, asked again when it has moved 12 units), ending at 14 units. **Treating:** `Act` (own slot, reads the
  patient, writes nothing of it) emits one effect per tick while the medic is in reach, still, awake, not fighting and has a working arm, and the patient is still or down; `Apply` (serial
  commit, in target then source order) picks the kit, bandages (`MedicalState.Treat`), drains the kit and trains Medic (`XpService.Medic`). So nothing on another character is
  written in a parallel phase, and the result does not depend on partitions or threads. It ends when nothing is left to bandage (under `MedicalState.FirstAidThreshold`, 0.5 HP), no kit of
  the right type is in reach, the patient is gone or the medic goes down. **Kit:** the medic's own best (highest record `quality`, `ItemInfo.Quality`; ordinary kit `item function` 1 for organic
  parts, robot kit 12 for robotic ones), else a squad mate's within 60 units; its `Charges` is the original's `remaining`, a used-up kit is removed. **Treating by themselves**
  (`SlowWorld`, every 15 ticks, serial): an idle, wandering or following character with a kit treats the most hurt squad mate within 250 units (knocked out first, then lowest blood; one medic
  per patient; a conscious patient that is moving is not picked, the original's `targetStandingStill`), unless someone in the squad is fighting; NPCs do (engine stand-in until stage 9), the player's characters only with the medic job (the original's `medic` toggle; whether its
  squad "Heal Allies" option is on by default is **Unknown**, so off), and go back to Wander or Follow afterwards.
- **Rest.** The `Rest` task sits the character down (animation `sitting idle`; a treating medic plays `medic`): `BodySystem` passes `MedicalContext.Resting`, so blood recovers at `resting heal rate mult`
  (x2) and hunger falls at `bed hunger rate` (0.33). **Beds are not done**: buildings have no world state yet, so there is no object to rest on (the bed bonus, degeneration 0 and the part-heal multiplier
  of character-stats.md "Sleep, beds and resting" need one).
- **XP on the move** (`BodySystem`): `XpService.AthleticsFromRunning` while moving faster than the race walk speed + 1 (whether walking trains is **Unknown**), `StrengthFromCarrying` while moving under
  a load that encumbers (factor below 1). The fighter's `EncumbranceFactor` is set from the inventory weight each tick.
- **Not done.** Beds, the shallow-water speed cap, the splint, the "sleep when injured" job (rescue is done, [Carrying](#carrying-rescue-and-looting)), the first aid context menu, treating a patient that keeps moving (a treatment ordered on one makes the medic follow it until it stops; the original has a "make target stand still" task),
  limb items for severed parts. Treatment and rest are not saved ([Saves](#saves)).
- **The time scale question.** See [Measurements](#measurements): at scale 1 nothing but hunger happens over days, at 100 the numbers read like
  play. The default stays 1 until a game session settles it.

## Items and eating

`src/Meitou.Simulation/Items/`: `ItemInfo`, `ItemInstance`, `Inventory`, `ItemFactory`, `InventoryText`, `FeedSystem`. Facts about item
fields and food are in [character-stats.md](game/character-stats.md#hunger-and-starvation) and [save.md](formats/save.md).

- **Item instances.** `ItemInstance` keeps the names of the save's INVENTORY_ITEM_STATE so the save mapping is one to one: `Record` =
  `base data sid`, `MaterialId` = `material sid`, `CompanyId` = `company sid`, `Section`, `X`/`Y` = `inventory x`/`inventory y`, `Quantity`,
  `Level`, `ItemFunction`, `Quality`, `Charges`; a worn backpack (section `backpack_attach`) holds an `Inventory` whose items are in
  `backpack_content`. Unit weight is `weight kg` (weapons times `weapon inventory weight mult`, 0.5); a bag's contents weigh times its
  `encumbrance effect`. `Inventory.ContentWeight()` is the load; `BodySystem` turns it into `Encumbrance.Factor(constants, load, false,
  strength, strength injury multiplier)` for the speed chain and the hunger rate.
- **Grids.** Footprints are `inventory footprint width x height`; a backpack's grid is its `storage size width x height`. Items take the first
  free cell (`Inventory.FindCell`). The character's own `main` grid is 8 x 6 cells: an engine choice (the real size is **Unknown**). An item that does not fit is not created.
- **Stacks** (2026-10-13; facts in [character-stats.md "Stacks"](game/character-stats.md#stacks-item-stackable)). `ItemInfo.StackLimit` / `ItemInstance.StackLimit` is ITEM
  `stackable` read as the int it is (**Verified**: before this it was read as a bool, which an int field never is, so nothing stacked); gear types are 1; `Inventory.LimitOf` applies
  a backpack's `stackable bonus minimum` / `mult` (`Inventory.StackBonusMinimum`, `StackBonusMult`, set from the CONTAINER) to what is inside it; the section flag that forces the
  base to 1 is not read. `Inventory.Add` tops up stacks of the same record and quality in the main grid, then in the backpack, then opens new cells (main, then backpack), at most a
  full stack per cell (split-off copies, `ItemInstance.WithQuantity`); it returns true only when every unit found a place, and what did not fit stays in the item's `Quantity`
  (`CarrySystem.TakeAll` counts such an item as left). So a record list of two rations (limit 1) gives two cells, not one item of quantity 2.
- **Starting inventory** (`ItemFactory.Build`). What `CharacterGenerator` rolled (`Loadout`): clothing in the section of its ARMOUR `slot`
  (`shirt`, `head`, `boots`, `armour` for body, `legs`; belt has no verified section name and goes to `main`), the backpack, crossbow and
  weapons at `hip`/`back` (row in `inventory y`, level and manufacturer kept); then the CHARACTER's `inventory` list, each ITEM entry with a
  first value above 0 being that many items. The player's squad gets what its CHARACTER records list (the start-off records have no item list,
  [ui-screens.md](game/ui-screens.md)). Worlds without appearances get only the record list. Animals get an empty inventory (their records list no items; a pack animal's `backpack` is not read). A rolled crossbow brings one item of its
  bolts (engine choice, 2026-10-13, [Ranged](#ranged)). Bolt items have `stackable` 1, so they do not stack in the original either.
- **Food.** A food item's charges are the record's `charges` times CONSTANTS `food quality mult`; eating adds **charges / 100** to the stomach,
  in hunger levels (the UI shows level x 100): Gohan 0.75, Bread 0.30, Rice Bowl 0.25, Dustwich 0.70. The unit is an **engine choice**: the
  nutrition value is **Unknown** in the original. Raw meat is not eaten by this rule.
- **Eating** (`FeedSystem`, `SlowWorld` every 30 ticks, serial in slot order). A living, conscious character with a hunger rate, hunger below 2
  ("Hungry") and an empty stomach eats one food item: its own, else a squad mate's within 60 units; the one that fills the missing hunger best
  without overshooting (else the smallest). Eating is instant. The player's characters follow the same rule (the original's auto-eat is
  **Unknown**).
- **Provisions.** What the original's NPCs eat from is **Unknown**. As a stand-in a hungry NPC (never the player's) with no food in reach is
  given the cheapest food by `value` per charge (Gohan, `ItemFactory.CheapestFood`) at most every 12 game hours; `FeedSettings.ProvisionNpcs`
  turns it off, and then NPCs die of `Starvation`. The player's characters starve unless they carry food.
- **Money.** `Squad.Money` is a squad's purse (hashed); `Platoon.Money` holds it while the platoon is unloaded (save key `money`). How money is
  earned or spent is the economy stage.
- **Not done.** An unloaded roaming squad's inventories are lost with its characters (it re-rolls on loading); no pickup, drop, trade or
  equipment bonuses. The fighter's encumbrance (`Fighter.EncumbranceFactor`) is set by `BodySystem` from the inventory weight each tick (2026-10-13; before, combat used 1). Kits: `ItemInfo.Quality` is the record's `quality`, read when needed (not
  copied to `ItemInstance.Quality`, the clothing grade); a CHARACTER gets its kits through the usual `inventory` list. In the base game the Wanderer, Rock Bottom, Hive and Empire starts carry none; the Cannibal Hunters, Holy
  Sword, Son of a Captain and Freedom Seekers starts do. Many NPC entries are `{0, 100}` (e.g. Mercenary Heavy, Militia): under the rule above (first value 0 never spawns) they give no kit, and what the
  second value means is **Unknown**.

## Combat

Formulas, constants and the data facts found while reading the install are in [combat.md](game/combat.md); this section is what the engine
does with them. Data: `src/Meitou.Data/Gameplay/Combat/` (`CombatDatabase.From(GameDatabase)`: `CombatConstants` with the loader's
rescaling applied once (cut/blunt 13 and 52, pierce 0.78, stumble 52, block 1.2 and 1.5 per level; a missing field takes the **base game's**
value, unlike `GameConstants`), `WeaponData`, `WeaponManufacturer`, `WeaponMaterial`, `ArmourData`, `CombatTechnique` with
`StrikeProgress(blow)`, `CrossbowData`, `GunData`; `WeaponCategories` maps a WEAPON's `skill category` to the `AnimationKind` of the stance and
the `TechniqueKind` of combat). Code: `src/Meitou.Simulation/Combat/`: `CombatSystem` (`.Act`, `.Apply`), `CombatSlot`, `CombatTypes`
(`AttackOrder`, `CombatOptions`, `CombatLogEntry`), `CombatTuning`, `CombatRolls`, `TechniqueChooser`, `HitOutcomes`, `HitResolver`,
`DamageFormulas`, `DefenceFormulas`, `ArmourFormulas` (`ArmourPiece`, `ArmourStack`), `WeaponStats` (`WeaponInstance`), `Fighter`; the pursuit and
retaliation systems are in `Fighting.cs`.

- **Fighters.** At spawn (`CharacterAssembly.MakeFighter`) a character with a race gets a `Fighter`: the first WEAPON item at `hip`, else `back`
  (quality = its level x 0.01, manufacturer and model from the loadout; none means fists) and every worn ARMOUR piece (quality / 100) in
  stacking order, with the gear's bonus sums. An animal's fighter is its natural weapon ([Animals](#animals)). A character without a race takes no part and cannot be targeted.
- **One tick** (`CombatSlot`s double-buffered inside the system, hashed through `IStateHashed`):

| Phase | What |
|---|---|
| `Inputs` | `AttackOrder` draws the weapon (`DrawnWeapon`, `InCombat`, task `Attack`), first attack after a 0.15 to 0.6 s pause; `StopCommand` ends a fight |
| `Move` | Face the target (with `SelfApproach`, off in the game, walk up in a straight line) |
| `Act` (own slot) | Optional medical tick; a down or staggered character drops its attack and reaction; the fight ends when the target is down; the swing emits a blow effect when its animation reaches the blow's `anim blocked frame` and ends at `acceptable end time`; **a free character first looks for blows aimed at it and picks a reaction, then, if still free and its pause is over, picks an attack** |
| `Apply` (commit order) | The outcome; for a hit: packet, `ChoosePart`, a coverage roll per piece, armour, toughness, `ApplyHit`, stagger, XP |
| `SlowWorld` | The slot arrays swap |

- **Attack choice** (`TechniqueChooser.ChooseAttack`; the original's is **Unknown**): not `disabled`, not a block or dodge, the technique for the fighter's weapon
  category (`TechniqueChooser.Fits`: a record's `animal` of 0 is matched by the weapon's type bit among its flags, a record with no flag never, the `1 handed` flag not
  tested; a non-zero `animal` only by the fighter's category, 5 for fists, 9 to 20 for a creature, which takes only its own; [animals.md](game/animals.md#4-techniques)), the prone flag
  equal, skill within `min skill` .. `max skill`, and the gap between the bodies (centre distance minus both footprint radii, default
  `CombatTuning.DefaultFootprint` 4) within the weapon's reach (`length` / 2; fists unlimited) and the technique's distance (`attack distance min
  vs static` against a target standing still, `attack distance` against a mover, a negative value meaning "not for that case"). Weighted pick on
  `chance`. `max encumbrance` and `anim hesitate point` are not used (**Unknown**).
- **Timing.** An attack lasts the clip's length (`AnimationLengths`, from the skeletons; 1 s when unknown) / (`anim speed mult` x the play rate,
  docs/game/combat.md "Animation speed", `CombatSystem.PlayRate`), in ticks; a block's play rate is its own, a dodge's 1; blow k arrives at `anim blocked frame k`; a blocked blow cuts the swing at its `anim stop frame` and the rest of a combo is not
  thrown (a hit or a dodge lets it go on).
- **Reaction** (`ChooseReaction`, the doc's `FUN_140887970`). Decided once per incoming blow from the attack's start (the defender perceives it at
  once), and **timed to be at its `anim blocked frame` when the blow arrives**, or started at once and less far along when there is less lead.
  Block chance `BlockChance(D, attacker's rating)`: D = melee defence x injury multiplier + weapon defence mod (if it can block) + gear bonus
  (+20 guarding); the attacker's rating = melee attack x injury multiplier + weapon attack mod + gear bonus (engine: the doc says only "attacker
  skill"). Success: block techniques of the blow's direction; failure: block techniques of another direction in the same class (front 0 to 6,
  rear 7 to 9), never a dodge. A fist fighter, or a weapon that cannot block, dodges when the roll against the variant chance passes (effective
  defence of martial arts; the "multiplier term" is **Unknown**, the gear's defence bonus is added instead).
- **Outcome** (`HitOutcomes.Decide`): block when the direction is equal and progress > 0.5, dodge when progress is in [0.1, 0.98], else hit; after
  a reach test (the blow misses when the gap is above the reach the attack began with + 6 units). A down or staggered defender cannot react.
- **Hit.** Part by weight x hitmult (a low strike excludes arms and head, direction 6 uses the weight alone), a coverage roll per piece in gear
  order, then `HitResolver.Resolve` (toughness resistance; `HitResolver.Land` does part choice, coverage, resolve and `ApplyHit`). Hits allocate
  nothing (a reused event list). A damage sum above the stumble threshold ("Heavy_Hit") staggers for 0.5 s (**Unknown** length, engine) from the
  next `Act`, so blows already thrown in the same tick still land and the result does not depend on the commit order. A technique's `power` is
  not applied (**Unknown**).
- **XP.** Hit: `XpEvent.HitDealt` / `HitTaken`; block: `GlancingHit` / `Defended`; dodge: `GlancingHit` and a small Dodge gain (engine; the dodge XP
  function was not read); a severed limb: `ToughnessFromLimbLoss`.
- **End.** A fight ends when the target is down (KO or dead) or on a `StopCommand`; the winner sheathes. A down fighter keeps being ticked by the
  body system. Nobody finishes off a downed target. A removed character needs nothing special: the arrays resync on the slot's generation.
- **Randomness** (`CombatRolls`): technique picks by attack number, pauses, block and reaction rolls by (attacker, attack number, blow),
  coverage rolls by blow and piece. A defender's decision reads only the attacker's slot of the last tick.
- **Approach** (`PursuitSystem`, `Schedule`). A character with the `Attack` task gets a path to its target at free speed (a new one when the
  target has moved 12 units from the goal, at most every 0.5 s); the path is dropped once the target is within `CombatTuning.CloseInGap` (3) or a
  swing or reaction is on.
- **Retaliation** (`RetaliationSystem`, an **engine rule** until the AI): every 15 ticks a character targeted by an attacker, awake, with a
  `Fighter` and not fighting, fights that attacker, and so do its free squad mates within 250 units (`RetaliationSystem.JoinMates`, shared with the aggro). NPCs always
  answer; **the player's characters answer only while idle** (task `Idle`: no move order, not treating or resting), so one sent away keeps walking (2026-10-13; the
  combat.md gap "player characters defending themselves when idle"). The original's orders panel has a Passive toggle (non-combat mode, NUM2,
  [ui-screens.md](game/ui-screens.md); GAMESTATE_CHARACTER `passive mode` in [save.md](formats/save.md)); it is not modelled, and its default is taken as off (**Unknown**;
  engine choice). The orders go through `World.Defer` and are `Defensive`: defending is not an assault.
- **Hostility** (`AggroSystem`): NPCs start fights with enemies of their faction in sight; see [Hostility and relations](#hostility-and-relations).
- **Host API.** `CombatSystem.StateOf(id)` (target, counters, `Down`), `Playing(id, tick)` (the technique playing and its progress) and, with
  `CombatOptions.RecordLog`, `Log` (every resolved blow: attacker, defender, outcome, part, damage, KO, death).
- **Gaps.** The clips are played by the animation system ([Animation](#animation)). Also not built: turrets (only the records
  are read), attack slots (`max num attack slots`), target choice, movement round the target beyond the path, fighting while prone, finishing off,
  prisoners, fist injury to the attacker, a stagger coupled to leg loss, mass-combat XP. Built 2026-10-13: auto-aggro by relations and the player's idle characters
  defending themselves (above).

### Ranged

Crossbows (2026-10-13), built from the facts of [combat.md "Ranged and turrets"](game/combat.md#ranged-and-turrets); turrets and harpoon turrets are not built. Code:
`Combat/CombatSystem.Ranged.cs` (the controller, shots, flight, `RangedTuning`), `Combat/RangedFormulas.cs` (`RangedFormulas`, `CrossbowInstance`, `Ammunition`), the
ranged fields of `CombatSlot` (`Ranged`, `RangedState`, `Loaded`, `ReloadLeft`, `Steady`, `Shots`, ...), `CrossbowData` in `Meitou.Data/Gameplay/Combat/RangedData.cs`.
Tests: `RangedTests` (a hit that spends a bolt, a shooter running dry, the 1 / 4 / 16 thread check) and `RangedInstallTests` (`[Slow]`, a real crossbowman).

- **The gun.** At spawn `CharacterAssembly.MakeFighter` gives `Fighter.Crossbow` from the first CROSSBOW in `back`, then `hip`: quality = its level x 0.01 (the
  generator's crossbows have level 0), stats by `CrossbowData.At(q, constants, G)` with `bow damage 1/99` (**Observed**). The melee `Weapon` is unchanged.
- **Ammunition at spawn** (**engine choice**): a character whose loadout rolled a crossbow gets one item of the crossbow's `ammo` (10 to 40 bolts by its `charges`) unless
  its record's list already gave some (`ItemFactory.AddAmmo`); only 3 of the 40 CHARACTER records with crossbows list bolts, and how the original arms its NPC
  crossbowmen is **Unknown**. A reload uses one charge of the first matching item, main inventory then backpack (**Observed**); a stack of several units refills from
  `ItemInstance.MaxCharges` and loses a unit (engine; bolts have `stackable` 1, so such a stack exists only inside a backpack with a stack bonus).
- **Crossbow or melee** (`ChooseMode`, own slot in `Act`; the original's switch is **Unknown**, combat.md "AI"): a fighting character with a crossbow uses it while it has a
  shot loaded or ammunition, no melee fighter targeting it is within **15** units (gap between bodies; a stand-in for `iAmBeingMeleeAttacked`) and its target's gap is at
  least **20**; from melee it takes the crossbow up again past a gap of **60**. Running dry (empty and no ammunition, `CombatSlot.Dry`) means melee. Switching is
  instant (draw time **Unknown**); `DrawnWeapon` becomes the bow kind. The player's attack orders go through the same rule, so a player character with a crossbow and
  bolts shoots.
- **The controller** (`TickRanged`, own slot in `Act`; the original's states 0 to 3): empty: a reload of clamp(lerp(s, max, min)) seconds with s = 0.01 (0.6 crossbows +
  0.4 dexterity x the gear's dexterity product), the bolt taken through a self-targeted `ReloadEffect` in the commit; out of range (d^2 >= h range^2) or the line of fire
  blocked: **closing in**, the pursuit system walks it to the target (`PursuitSystem` stands a shooter that aims or reloads; with `SelfApproach` the combat system walks it);
  in range: the **0.4 s** steady aim, then the fire decision and a `FireEffect` to the target. While closing in, the line must be clear for **0.3 s** before it aims
  again (the original's two ray flags are **Unknown**). XP per second: crossbows 1.2 and dexterity 0.5 while reloading, crossbows 1.2 and perception 3.0 while aiming in
  range; FRIENDLY_FIRE 20 or 100 per friend passed or hit (**Observed** amounts). The shot leaves on the decision (the original's trigger flag is **Unknown**).
- **Line of fire** (`LineOfFire`): the terrain sampled every 20 units, then the bodies along the line (the grid queried in 100-unit pieces). A friend first: hold the
  shot (**Observed**); an enemy first: fire (it takes the bolt); terrain first: **close in** (engine: the original holds, which would leave a shooter behind a dune
  standing for ever). Buildings are not tested (no building shapes in the simulation yet). Enemy = the character's target, someone fighting it, or hostile by
  `AggroSystem.IsEnemy`; in a world without a relation table, another faction (engine).
- **Geometry** (engine stand-ins): the barrel 15 units above the feet and 3 ahead (the record's `barrel pos` is in item space); the aim point 13 above the feet (for `Bip01
  Spine2`) plus the lead; a body is an upright cylinder of the race's footprint radius, 18 tall (the original tests its physics shape, **Unknown**); a down character and a
  character without a body (no race or medical state, so nothing to damage) do not stop bolts; animals have bodies since 2026-10-13 and stop them.
- **The shot** (`ApplyFire`, commit order): the cone D, the damage, range and speed with the height factor, the avoidance (all **Observed** formulas); rolls from
  `RngPurpose.Ranged` keyed by the shooter and its shot number (tilt, spin, damage). The bolt joins `projectiles` in commit order.
- **Flight** (`FlyProjectiles`, serial, in the combat system's `Schedule` after the slot copy): every bolt in firing order moves speed x tick, straight while range is left,
  then pitching down 8 degrees per second, dropped 5000 past its range. On each step the ground (sampled every 5 units) and the bodies (the grid, **the last tick's
  positions, the grid's own source**) are tested; the nearest wins, ties by slot. A friend in the way: a roll from `RngPurpose.Ranged` (bolt id, victim) against the
  avoidance passes it (it is not tested again), else it is hit. The ground: stuck, or dropped when |cos| to the normal is under 0.1. Up to **50** stuck bolts are kept
  (`harpoonLimit`'s value not read), oldest first out; they are visuals only.
- **A bolt hit** (`BoltHit`): pierce = the bolt's damage, bleed 2, no penetration, `HitResolver.Land` with direction 6 (part by weight, cover rolls keyed by the bolt),
  so the global damage multiplier applies a second time (**Observed**, invisible at its default 1); no block, no dodge. Stagger over the stumble threshold as in melee,
  the hit recorded for the hit reaction, the victim's "hit taken" XP with the shooter's crossbow skill as the opponent, no XP to the shooter, a player's kill a murder.
  `CombatLogEntry.Ranged` / `Intended` mark bolts. The victim answers through retaliation (it is the shooter's target).
- **Not applied** (no consumer found, **Observed**): the crossbow's `bleed mult` and target multipliers, armour `ranged skill mult`, `accuracy perfect skill`.
- **Threading.** Everything a character decides is in its own slot in `Act`; the inventory changes and new bolts come through effects in the commit's order; the flight
  is serial. Rolls are stateless. `RangedTests` checks 1, 4 and 16 threads on a skirmish cut into one-slot partitions.
- **Hash.** The ranged fields of a slot go into the state hash only once one is set (`CombatSlot.RangedUsed`), the bolt list only once a bolt has flown, so worlds without
  crossbows hash as before: none of the ten pinned scenarios moved. **Pinned 2026-10-13**: `ranged` (`RangedTests.Hashes`: six groups of three crossbowmen against three
  katana fighters, 1200 ticks), after agreeing at 1, 4 and 16 threads.
- **Drawing.** `WorldSnapshot.Projectiles` (`ProjectileSnapshot`: position, direction, stretch, stuck, the `live ammo` mesh). The host does not draw them yet.
- **Cost.** Per shooter and tick: one grid query of 60 units for melee threats and, only when a shot is due or while closing in, a line of fire (a few grid queries and
  terrain samples). Per bolt and tick: one grid query and up to 8 ground samples. The 1200-tick skirmish (36 characters, 20 bolts) runs in well under a second.
- **Not built:** turrets and harpoons, the AI's target choice for shooters (a shooter keeps its ordered target), crimes for shooting, picking bolts up (they are not
  recoverable in the original either), aiming while walking (`aimwalk`), the reload animation event, buildings in the line of fire, night and stealth in the decision.

### Animals

Built 2026-10-13 from [animals.md](game/animals.md). Code: `AnimalData` (`Meitou.Data/Gameplay/Bodies/AnimalData.cs`), `BodyFactory.Create(..., age)`,
`CharacterAssembly.MakeAnimalFighter`, `Fighter.IsAnimal` / `AnimalCategory` / `TechniqueCategory` / `CanAttack`, `TechniqueChooser.Fits`, `MedicalState.HasStatsObject` /
`ScaleK` / `ScaleZ`, `CharacterStats.Scale`. Tests: `AnimalTests` (`[Slow]`, the install's records).

- **Before.** This doc said animals had no race and no body. In fact the race fallback (`CharacterAssembly.RaceRecord`: the record's own `race`) gave them a person's
  body, stats and fists. Fists could not strike at all: the martial arts techniques have `animal` 5 and the chooser asked for 0. Both are fixed: people with fists now use the
  "ma" techniques (no pinned scenario moved: the synthetic ones have no fist fighter with real techniques).
- **Stats.** The ordinary constructor (`CharacterStats.FromGroups`, the original's fan-out, so `combat stats` c gives martial arts c/10 and dexterity, strength, toughness c)
  and the race multipliers; `CharacterStats.Scale` = the animal's age (the squad's `animal age`, 1 in the sandbox) multiplies every stat the combat formulas read
  (`CombatSystem.Eff`), not the stored values (training and saves see the raw stats). Speed comes from `Speed.Run` as for people (it does not read the scale).
- **Body.** `MedicalState.Create(race, strength, hasStats: false, hpMultiplier: lerp(age, scale min, scale max), bodyScale: HP mult)`: the parts scale by size and
  `HP mult`, the blood capacity is the race's spread x both (s = 1), the hunger rate x the size, and hits cannot sever limbs. These factors are not saved (a loaded animal
  gets 1s; [Saves](#saves)).
- **Natural weapon** (`MakeAnimalFighter`): the record's `weapon` as a `WeaponInstance` made by its `weapon level` manufacturer; the level and model are the manufacturer's
  first model's (Ancient: Mid-Grade Salvage, level 15), an **engine choice** (the original's level is **Unknown**). It is not of the unarmed category, so its blows use
  the weapon formulas (cut, blunt, pierce) with martial arts as the skill (`WeaponInstance.SkillOf`). The target kind `Animal` selects the weapons' `animal damage mult`.
  An animal without a weapon (Swamp Turtle) has a fighter that cannot attack (`CanAttack` false): it is targetable, it neither looks for fights nor answers.
- **Techniques.** `Fighter.TechniqueCategory` = the weapon's `skill category`; a creature takes only techniques whose `animal` equals it (a bull's weapon is category 20:
  the giraffe techniques). A creature technique's distance of 0 reads as contact, `CombatTuning.CloseInGap` (**engine choice**; literally, a goat could never strike).
  Creature weapons cannot block and no creature technique is a block or dodge, so animals never react (the data's rule).
- **Hostility.** The aggro and retaliation tests now pass animals (they have a fighter and a body): their squad's faction decides, as for anyone. A bonedog of a faction
  hostile to the player's attacks a player character it sees (`AnimalTests`). Fleeing is the faction rule ([Hostility and relations](#hostility-and-relations)); no
  animal-specific rule was found.
- **Determinism.** Everything new is spawn-time data or reads of the character's own slot; `AnimalTests` runs a pack fight (bonedogs, beak things and wild bulls
  against people, one-slot partitions) at 1, 4 and 16 threads and gets the same hashes. It is not pinned among the golden scenarios: those run without the install.
- **Cost.** Nothing per tick beyond what any fighter costs; the scale is one multiply per stat read.
- **Looks and animation** (2026-10-13). Animals are drawn (their race's mesh, textures and fur at their size, [character-renderer.md](character-renderer.md#animals)) and
  play their own clips ([Animation](#animation), animals); combat times a creature's blows by the clip lengths of its own skeleton (`AnimationLengths.For`), so a bonedog's
  bite takes 0.77 s of clip and a beak thing's 5 s instead of the 1 s default. Before, they had no look (markers), and the animation system gave them human layers.
- **Not done.** Smell, `VO_Creature_Alert`, `animal strength`, lifespan and ageing, `turn rate` (and the `turning anim` / `strafe anim` clips), pack animals' backpacks
  (the luggage meshes), death items and butchering, animal AI goals (eating, the dogs' limb games), taming and buying animals, saving the animal factors (a loaded animal is
  drawn at size 1), the ragdoll (a downed animal plays `gotosleep` / `sleep`).

### Hostility and relations

An **engine stand-in** for the stage 9 senses and target choice, with the original's sight and hearing figures ([perception.md](game/perception.md)); the relation rules are the original's
([factions-squads-towns.md section 3](game/factions-squads-towns.md#3-relations)). Code: `AggroSystem.cs`, `Relations.cs` (`PlayerRelations`), `FactionRelations` in
`Meitou.Data/Gameplay/FactionData.cs`; tests `AggroTests` (synthetic factions) and `TownAggroTests` (`[Slow]`, the install).

- **The table.** `PopulationData.Relations` is the game's one live table (no copy per world): built by the **Verified** rules of 3.2 (explicit entry, else the smaller default,
  self 100), with the 3.3 predicates (hostile at -30 or below, ally at 50 or above or self, coexistence from the FACTION list). The player's faction (Nameless) has no table:
  `FactionRelations.PlayerFaction` makes every query of its row ask the other side's entry about the player (3.1, **Observed**; player to player is 100), so the explicit list of
  the Nameless record is never read. A loaded save puts its values in (`SaveLoader`) and a save writes the changed ones back (`SaveCapture`, as before). Worlds built on one
  `PopulationData` share it: a test that changes relations builds its own data. The aggro system hashes it once `Changes` is above 0.
- **Deeds** (3.4 slot 5, `FactionRelations.Event`, **Observed** table: delta x amount only while above the floor or below the ceiling, then clamped; nothing for a `not real`
  faction; a change asked of the player's row goes to the other side). Which game events use which kind is **Unknown**; the engine's choice, after the `CrimeEnum` numbers the kinds
  line up with: a player's `AttackOrder` (not a defensive one) on a character of another faction is an **assault** (kind 5: -20, floor -75) on that faction's view of the player,
  once per victim while no player character fights it already; a killing blow by a player's character is a **murder** (kind 4: -20, floor -100). Amount 1. So two assaults
  turn a neutral (0) faction hostile. KOs, theft, prisoners and the drift (kind 8) are not wired.
- **Messages.** When a faction's view of the player crosses -30 or 50 the game's texts (`FUN_1406b28f0`, **Verified** thresholds): "{faction} are now hostile towards you", "...
  no longer hostile towards you", "... are now your allies" (above 50 only), "... no longer your ally" (the exact wording of the two "no longer" texts is an engine reading of
  the doc's summary). `World.Post`; the host shows them as a HUD notice and prints `relations ...` to the log.
- **Who is an enemy** (`AggroSystem.IsEnemy`): A picks a fight with B when A is hostile to B (one way: A's view) and neither lists the other in `coexistence` (**engine
  choice**: what the coexistence flag does in the original is **Unknown**; either side's is enough here). Same faction never (self is 100) and allies never (50 or above is not
  hostile). The "default-hostile faction" test of 3.3 is not used: the smaller-default rule already makes such factions hostile to almost everyone.
- **The look** (`Act`, parallel, own slot): an awake NPC with a `Fighter`, not fighting, not fleeing, not treating or resting, and not calm, looks once every 15 ticks (slot s
  on the ticks where tick % 15 = s % 15, so the work is spread over the ticks; **engine choice**: the original refreshes the senses of 8 characters per frame in turn,
  [perception.md](game/perception.md#1-the-sense-object-and-when-it-updates)) for the nearest living, awake character with a body whose faction is an enemy and that it
  perceives (2026-10-13, the original's figures, [perception.md](game/perception.md)):
  - **Sight** (`AggroSystem.Visibility`): R = **1500** units x its race's `vision range mult` (RACE +0x74; 0.5 to 2 in the data), the original's outdoor daylight range
    (`FUN_140851bc0`); the view cone factor v = (cos a - 0.2) / 0.8 from its facing (`Yaw`), so nothing is seen beyond 78.5 degrees off; seen when v > 0.01 and d <= R x v
    (`FUN_140852f70`, both characters lit alike, the target not sneaking). Not built: night (x0.5 range and the light terms; the world has no clock yet), weather,
    buildings (500 inside), stealth (no sneaking in the simulation), line of sight.
  - **Hearing** (`AggroSystem.NoiseRange`, `HearChance`): a moving target is heard within N = lerp(speed / top speed, 350, 600) with chance 0.75 x (1 - d^2/N^2) per look,
    rolled from `RngPurpose.Sense` (`FUN_140851d10`, `FUN_140857500`). A standing one makes no noise, so a still enemy behind an NPC goes unnoticed, as in the original.
  The grid of the last tick's positions; ties keep the grid's order (lower slot within a cell). It emits one effect. A crowd whose factions present have no enemy among
  each other skips the looks (worked out when the set of factions present or the table changes). Downed characters and characters without a body or fighter are never
  looked for and never look; animals take part like anyone (2026-10-13, [Animals](#animals)): their squad's faction decides, and one without an attack
  (`Fighter.CanAttack`, a swamp turtle) neither looks nor answers.
- **The decision** (`Apply`, serial, commit order): the looker if still free, and its free squad mates within **twice their own R** of the target (the original's
  squad-shared sightings: a target a mate saw or heard counts as seen within 2 R, `GroupSense`, [perception.md](game/perception.md#6-tags-memory-and-the-squads-shared-senses);
  was 250 units of the looker until 2026-10-13; never the player's), get an `AttackOrder` for the next tick (`World.Defer`). The target answers through retaliation.
  **The player's characters never look and never join**: they fight when ordered or, idle, when attacked.
- **Leash and standing down** (`SlowWorld`, every tick, serial): an NPC's position when it starts a fight (by aggro, retaliation or anything) is kept; beyond **3000 units**
  from it the NPC stops (a deferred `StopCommand`) and is calm (no looking) for **20 game seconds**; it still answers an attack. When its fight is over (target down, or
  stopped) it goes back to the task it had before (wander or follow; a go-to becomes idle, which the roaming squads order again; a knocked-out one is left to wake into
  wandering). Both numbers are engine choices (`AggroSettings`): the original's give-up rule was not found (**Unknown**); the leash is twice the daylight sight (750 until
  2026-10-13, when the sight was 300) so a fight begun at the edge of sight reaches its target.
- **What the data gives** (2026-10-13, `TownAggroTests`, base game, residents and bar squads with their looks, 60 game seconds each): The Hub (37 characters), Squin (66),
  Stack (145), Admag (82), Mongrel (160), Shark (78) and Sho-Battai (179) start no fight; Waystation (30) has United Cities attacking The Holy Nation (hostile in the data).
  The sandbox's foe (Bloodraiders, facing the hero as the game's sandbox places it) attacks an idle Nameless character 150 units away and is fought back. The same holds
  with the original's sight and hearing (re-run 2026-10-13). In the game (`--new-game "The Cannibal Hunters"`) the Cannibals of the start town attack the player's squad
  at once; the Wanderer's start in The Hub stays quiet.
- **Fleeing** (`FleeSystem.cs`, `StandardParts.Flee`, after aggro; serial `SlowWorld`, slot and squad-id order; its tests were cut in the 2026-10-13 cleanup). The original's facts are in
  [perception.md section 7](game/perception.md#7-fleeing): `Task_Runaway`'s movement and end are known, but what makes an ordinary squad run is not (its retreat package is
  only given to raids, and no reader of the FACTION run-away ratios was found). So:
  - **When** (**engine reading** of the FACTION fields' names): every 15 ticks, an NPC squad in a fight whose faction has `run away ratio of squad size` r1 or `run away ratio
    relative to enemy` r2 above 0 breaks when its members standing are fewer than its living members x r1, or fewer than the enemies standing within their sight x r2. 92 of
    103 factions have both at 0 and fight on; bandit-type factions have 0.2 to 0.5.
  - **How** (the original's, `Task_Runaway`): every member standing stops fighting (a deferred `StopCommand`) and runs (`GoTo`, speed mode Free, the task's 999 limit) to
    2000 + 400 r units from the centre of the enemies it sees, on its far side; at its goal (or within 200 of it, or when the path fails) it runs 1000 + 400 r units in a random
    direction (`RngPurpose.Flee`).
  - **Until** (the original's end conditions): no enemy standing within its R and nobody attacking it; it then wanders again. A fleeing character (`CharacterCold.Fleeing`,
    hashed only while set) neither looks for fights nor answers attacks (retaliation skips it; engine reading of the task's urgency). The player's characters never flee.
- **Not done:** night, weather, buildings, stealth, line of sight and the tags of the original's senses ([perception.md](game/perception.md)), its target choice and attack
  slots, the squad retreat package for raids, guards defending other factions' townsfolk (crimes and bounties are built: [Crimes and bounties](#crimes-and-bounties)), wars
  (slots 7, 8), dialogue relation effects, trust, the drift, animals' own senses (smell, `VO_Creature_Alert`), NPCs finishing off or looting the downed. The original's enemy test (Character slot 125) does
  not consult `coexistence`; the engine keeps it (above) until the place the flag acts is found.

## Carrying, rescue and looting

The original's rules are in [carry-loot.md](game/carry-loot.md) (crimes also in [economy.md 11.6](game/economy.md#116-pending-crimes-witnesses-and-the-cage-sentence)).
Code: `src/Meitou.Simulation/Carrying/` (`CarrySystem`, `CarryTypes.cs`), built 2026-10-13 as `StandardParts.Carry` after the body system
([order](#systems-and-their-order)). Everything it does runs in the serial phases (`Inputs`, `Schedule`, `SlowWorld`), in slot order.

- **The link.** `CharacterCold.Carrying` (carrier) and `CarriedBy` (body), set and cleared together. The body is always on the **left shoulder**
  (**Verified** in the original), so `CarrySystem.LeftArmUsable` (no collapsed part with the left-arm bit 4 of `collapse part`, limb slot 0 not a
  stump) decides who can carry. Each tick the body's position and yaw are set to the carrier's. The snapshot publishes `CarriedBy`, `Carrying`
  and `AttachBone` (`Bip01 L UpperArm`); **the renderer does not attach the body to the bone yet**: the host draws it at a fixed offset beside
  and above the carrier (`GameHost.CarriedOffset`, 2 units left, 14 up; a guess not checked in the viewer).
- **Who.** A carrier is awake, has a race, is not in combat, not carrying or carried, with a usable left arm (`CanCarry`). A body is knocked
  out or dead, of a `carriable` race (every base race but the Leviathan), not carried (`CanBeLifted`). Snatching a carried body is not allowed
  (engine choice; the original allows it for some AI goals).
- **Pick up** (`PickUpOrder`, task `PickUp`): the nearest eligible character of the list (the selection when empty) walks to the body like a
  first-aid medic (reach 14), then plays `shoulder lift left`; at 0.4 of the clip the link is made (if both are still eligible and within 28
  units), at 0.95 the task ends and the carrier goes back to the task it had (Follow, Wander, Idle). The clip length is read from the
  skeleton's animations (1 s without them).
- **Weight and speed.** `BodySystem` adds `carry person weight` (30) to the carrier's load (`Encumbrance.Factor(..., carrying: true)`);
  `MovementSystem` raises a carrier's walk mode to run (the original's speed mode 0 to 1). Separation does not push carried bodies, and a
  follower does not follow the leader it carries.
- **Put down** (`PutDownOrder`, `K`, task `PutDown`): the clip backwards, released below 0.2; the body is placed 3 units to the carrier's side
  (where walkable, else at the carrier) and, unless the carrier regards it as friendly (same faction, or allied at relation 50 or more when
  the live table is set: engine reading of the original's test), knocked out again for `MedicalState.PutDownKoMinutes` with the factor
  `0.1 + 0.3 x assassination / 100` (the KO formula as character-stats.md reads it, without its random roll: engine choice).
- **Drop.** At once (no clip) when the carrier is knocked out or dies, its left arm collapses or is cut off, or it is ordered to attack (an
  attack order is the player's way to fight; an NPC carrier picks no fight: the aggro look and retaliation skip carriers, as the original's AI
  weights do). Dangling links (a body removed) are cleared.
- **Waking on the shoulder** (engine choice; the original's put-down-if-they-can-walk goal was not decoded): a friendly body that comes
  round is set down; a non-friendly one is knocked out again on the shoulder, so a captive stays carried.
- **Rescue job** (`RescueJobCommand`, `J`; `CharacterCold.RescueJob`, published as `BodyStatus.RescueJob`). Every 15 ticks a character with
  the job, idle, wandering or following, able to carry and with no fight in its squad, picks up the nearest knocked-out (not dead),
  liftable member of its faction within its sight (`AggroSystem.RangeOf` of 1500, the race's vision multiplier; the original searches its
  sensed list, whose range is **Unknown**) that nobody else is lifting. **No beds** (buildings have no world state): after the pick-up the
  rescuer goes back to its task and carries the body until it comes round (then sets it down) or is ordered to put it down. The squad's
  "Rescue Allies" toggle is not built (own faction only).
- **NPC leader rescue** (`CarrySettings.RescueNpcLeaders`, on): an NPC squad whose leader is knocked out (not dead) sends its nearest free
  member to pick it up (the original's `FIND_AND_RESCUE_LEADER` stands in for the AI packages it belongs to). Wounded slaves and kidnapping
  are not built.
- **Loot** (`LootOrder`, Alt + right click, task `Loot`): the nearest looter walks to a knocked-out or dead character with an inventory and
  takes everything of its main inventory and its backpack that fits its own (`CarrySystem.TakeAll` through `Inventory.Add`: stacks join,
  then the first free cell of the main grid, then of the backpack); what does not fit stays. Worn gear and the backpack itself stay (engine
  choice: the fighter's gear is fixed at spawn, and the original has no "take all" rule for equipment). The host shows "{name} took n
  items from {victim}" (and how many did not fit). No loot window.
- **Witnessed theft** (engine stand-in for the original's pending-crime rules): when a player character loots a non-player, every awake,
  uncarried member of the victim's faction is checked with the aggro sight test (`AggroSystem.Visibility`: range, view cone, daylight); if
  one sees it, `PlayerRelations.Event(victim faction, Stealing)` (kind 3, -10, floor -50, [Hostility](#hostility-and-relations)) and the
  message "{name} was seen looting {victim}". The victim itself never notices (the original's steal roll always succeeds against a downed
  owner). This relation change stays as it was; the crime that goes with it (a pending Looting with the original's permission rules, booked only by police) is the crime
  system's ([Crimes and bounties](#crimes-and-bounties), 2026-10-13). **Not built:** the stolen flag on items, cage crimes, NPC looting (`LOOT_THE_DEAD`).
- **Not built:** beds, cages, `Put in Bed` / `Put in` / `Kidnap`; the context menu; items on the ground (no world item objects yet: picking
  up and dropping are not done); the renderer's bone attachment; the `carrying left arm only` overlay; swimming with a body. Carrying and
  looting are not saved ([Saves](#saves)).
- **Hash.** `CharacterCold` hashes `Carrying`, `CarriedBy`, the task's `CarryTarget` / `LiftTime` / resume task and `RescueJob` only while set,
  so worlds without carrying keep their pinned hashes. **Tests** (`CarryTests`, 4): lift timing and weight and speed, the drops, witness vs unseen loot,
  and the scripted scenario at 1, 4 and 16 threads (pinned `carry`).

## Crimes and bounties

The original's rules are in [economy.md section 11](game/economy.md#11-bounties-and-crimes) (the crime table, the ledger, the lifetime, booking, the `+0x40` faction flag, the
hit-on-police rule) and [carry-loot.md 4.3 and 4.6](game/carry-loot.md#43-crimes-for-taking-items) (the pending-crime setter, the permission to loot, the witness function by code,
kidnapping). Code: `src/Meitou.Simulation/Crime/` (`CrimeSystem`, `CrimeTypes.cs`: `CrimeCode`, `CrimeTable`, `BountyLedger`, `BountyEntry`, `PendingCrime`, `CrimeFactions`,
`CrimeSettings`), `Saving/BountySave.cs`; built 2026-10-13 as `StandardParts.Crime`, last in the [order](#systems-and-their-order). Tests: `CrimeTests` (7 cases) and
`CrimeInstallTests` (`[Slow]`, the base game's factions and towns).

- **State.** On the character: `CharacterCold.Pending` (one pending crime: code, victim faction, victim, timer in game seconds, booked and witnessed flags) and
  `CharacterCold.Bounties` (a `BountyLedger`: one `BountyEntry` per faction in faction order with amount, crime mask, claimed and the tick of the last raise). Both are changed only
  in serial phases and hashed only while set. `CharacterCold.NpcClass` is CHARACTER `NPC class` (**Verified**; `fcs.def` default MILITARY), set by `CharacterAssembly`; `None`
  for a character without a record (tests).
- **Factions** (`CrimeFactions`). A faction books bounties when `fundamental type` is LAW_ENFORCEMENT or `offers bounties` is set (`FactionData.BooksBounties`, **Observed** loader, base data:
  Police, Mercenary Police, United Cities, The Holy Nation, Shek Kingdom, Republic of Tertius, `CrimeInstallTests`); lawless = `not real` or `fundamental type` BANDIT.
- **Police-like** (`CrimeSystem.IsPoliceLike`, the original's `FUN_140599600`): never of a BANDIT-type faction; `NPC class` LAW_ENFORCEMENT; or MILITARY in a **resident squad of a town**
  of its own faction (**engine reading** of the original's test of its status object against its squad; roaming military are not police). The player's characters never are. In the
  base game's towns this makes most residents police: Admag 49, Stack 110, Heng 142, Blister Hill 117, Bad Teeth 105 (`CrimeInstallTests`, Observed), because 151 CHARACTER records
  are MILITARY.
- **Culprits.** Only the player's characters (**engine choice**: the simulation's NPCs fight only enemies or in defence, loot nothing and carry only their own; bandit-class NPCs could not
  be booked anyway).
- **Deeds** (all reported from serial phases):

| Deed | Where | Crime | Rules |
|---|---|---|---|
| An attack order that is not self-defence | `CombatSystem.Inputs` | Assault (500), timer 3 | the original's `FUN_140334070`: not on a victim wanted by the attacker's faction, of class BANDIT or with a pending crime; the undecoded victim test read as "its faction is not hostile to the attacker's" (engine) |
| A killing blow (melee or bolt) | `CombatSystem` commit, bolt flight | Murder (5000), timer 20 | **engine choice**: the original's setter has no caller with code 4; same terms as an assault |
| A loot-all from a downed character | `CarrySystem` | Looting (500), timer 3 | the right-click transfer's code (**Verified** call site; which path the usual loot takes is **Unknown**); no crime when the victim is of class SLAVE (stand-in for the slave status) or BANDIT or has any bounty, or the looter is LAW_ENFORCEMENT, MILITARY or DIPLOMAT |
| Carrying an "innocent" body past a witness who cares | the sweep | Kidnapping (5000), timer 20 | carry-loot.md 4.6; innocent = an NPC of another, real, non-bandit faction, not of class BANDIT, with no bounty (engine reading of the unknown status flag); the witness is not allied with the carrier and is allied with the body's faction. Putting a conscious captive down is not a case here (captives are knocked out again on the shoulder) |
| A landed hit between a police-like character and a player character | `CombatSystem` commit, bolt flight | Assault, timer 30, **booked at once** with the police's faction | the original's `FUN_140665fa0`, both ways (being hit by the police counts) |

  The setter (`SetPending`, the original's) refuses a police-like culprit, a victim of the culprit's own faction and a lawless victim faction; an Assault does not replace another
  pending crime (it raises that one's timer to 20); the same code again keeps the booked flag; a new code clears it. A pending crime's timer counts down by game seconds and the crime
  is forgotten at 0 (**engine choice**: the original's countdown was not found).
- **Witnesses** (`SlowWorld`, every 15 ticks, serial, slot order). For each player character with a pending crime (or carrying an innocent body), every awake, uncarried NPC of another
  faction without a pending crime of its own that **sees** it (`AggroSystem.Visibility`: 1500 x the race's vision multiplier, the view cone, daylight; hearing is not used) is tested as the
  original's witness function does: not when its faction is hostile to the victim's, the victim's is lawless, it books bounties and the victim's is a bandit faction, or it regards the
  culprit as friendly and the victim is of another faction; Theft and Looting need it to care about the victim (same faction or allied, 50 and up), Looting also that it does not regard the
  culprit as friendly; Kidnapping a victim and the care; an Assault a victim it does not regard as an enemy; a Murder nothing more. A witness that passes sets the timer to 20, the first one
  posts "{name} has been caught committing {crime}!" (the original's tooltip words; not for Looting, whose loot already says "was seen looting"), and a **police-like** witness of a faction that
  books bounties books it.
- **Booking** (`Book`, the original's `FUN_140853ec0`): once per pending crime; not for a culprit of class BANDIT, DIPLOMAT or LAW_ENFORCEMENT; Enslaving adds nothing. The crime table's
  amount goes into the entry of the **booking witness's faction** (the original's faction choice; its parent faction is **Unknown** and not used), the crime's bit is set and the raise time
  reset. Message: "{name} is wanted by {faction} for {crime}: bounty c.{total}".
- **Relations.** The crime system changes no relation: the deed-time events of [Hostility](#hostility-and-relations) and the carry system's witnessed-loot event stay as they were, so
  nothing is counted twice. In the original, witnessing gives a squad-level reaction (kind 7, not a relation event) and changes the witness faction's relations only when it is friendly
  with both sides (**Observed**, carry-loot.md 4.3); whether the original changes relations at the deed at all is **Unknown** (open question below).
- **Expiry** (the sweep): an entry is removed once more than 0.04 hours per cat have passed since its last raise (500: 20 hours; never from 10000, `CrimeTable.LifetimeHours`); all
  expired entries at once (the original removes one per tick); message "{name} is no longer wanted by {faction}".
- **The police** (`AggroSystem.Crimes`): a police-like NPC whose faction has an unclaimed bounty on a character it perceives (the aggro look's sight and hearing) attacks it as it attacks an
  enemy, with its squad mates (the aggro's join rule). The look's "no enemy here" skip counts the factions that want a character present. Fights already end at the knock-out (nobody
  finishes off), so the police knock the wanted down rather than kill them, but nothing makes their blows non-lethal: whether the original's police try to subdue is **Unknown**.
  Hitting back is an Assault booked at once (above), as in the original.
- **HUD and messages.** `CharacterSnapshot.Bounties` (`BountyLine`: faction, amount, crimes, hours left) for selected characters; the HUD lines are in [Player](#player); the messages go
  through `World.Post` to the HUD notice and the log.
- **Saves.** The player's characters' ledgers: `BountySave` writes and reads the GAMESTATE_CHARACTER keys of [save.md](formats/save.md) (`bountyfac<k>` the FACTION string id,
  `bountyexp<k>` the game hour of the last raise as day x 24 + hour, **Observed**, `amount<k>`, `crimes<k>`, `claim<k>`). Pending crimes are not saved (no key known).
- **Not built:** claiming bounties (turning a captive in, the 11.4 reward), bail, cages and sentences, escaping prison (the stolen flag and fencing are in [Trade](#trade)), theft from a conscious owner
  (no pickpocketing), trespassing and burglary (no buildings), spawn bounties (CHARACTER `bounty chance` / `amount` / `fuzz`, so NPCs are never wanted), NPC crimes, the dialogue
  reactions, the squad-level kind 7 tag, the police chasing an unseen culprit, witnesses hearing.
- **Cost.** Nothing until a crime is pending; then one pass over the slots per pending culprit every 15 ticks (a sight test per NPC), and the aggro's extra test per look.

## Trade

Item prices, shops and trading (2026-10-14), from [game/economy.md](game/economy.md) sections 3 to 9 and 13. Code: `Meitou.Data.Gameplay.Economy` (`PriceConstants`, `ItemValue`,
`TradeCulture`, `VendorList`) and `Meitou.Simulation.Trading` (`Market`, `Shop`, `ShopFiller`, `TradeSystem`); the windows are `src/Meitou.Game/PlayerInterface.Trade.cs`.

- **Prices.** `ItemValue` is the original's base value in 32-bit float with its truncations (the worked examples of economy.md 4.6 are its tests: a Katana at quality 50 is 43750 raw
  and 17500 with `sword price mult`, Edgewalkers 61250 / 24500, Samurai Armour 15450 / 9270, Chainmail 20587 / 12352). `Market.UnitPrice` and `StackPrice` are the item classes' slots
  81 and 82: weapons and armour from the base value with `loot price mult GEAR` when sold, the generic items with the town's market factor, `loot price mult` when sold unless an
  unstolen `trade item`, and the charge fraction; then the trader factor T = `buy mult` x culture price (the town's ITEMS_CULTURE before the faction's), halved for an item stolen from
  the player's faction (as read). A weapon's quality is its model level (`ItemInstance.Level`), armour's its `Quality`.
- **Engine readings where the original is Unknown.** The money item is the ITEM named `Cats` (priced at its `value`; which records make a `MoneyItem` is not decoded). Crossbows are
  priced like weapons. Maps and blueprints use their record `value` (the original's stored prices come from places not traced). The player-made sell rows and the armour's hard-coded
  0.3 never apply (no crafting; the condition is not decoded).
- **Market factors** (`MarketFactors`, economy.md 7.2): for every placed town except nests, nest markers, points of interest and null towns, each item of `all trade goods` gets a
  factor uniform in [0.7, 1.3) (`trade profit margins` 0.3), clamped to within d / 1,000,000 x 3 of every town set up before it (in site order; the original's list and order are
  **Unknown**). Rolled from the world seed (`RngPurpose.Trade`), so every load of a world has the same factors; the original saves them per town (`trade goods`), not done here.
- **Shops.** A squad whose template has `is trader` and vendor lists gets a `Shop` at the trade system's first look after it appears (every 15 ticks; squads of the player's faction
  never): `ShopFiller` rolls round(U(0.8, 1.2) x `vendors fill total amount`) units shared among the lists by weight, picked by weight from each list (a weight of 0 counts as 100,
  `forbidden items` of the town and faction cultures skipped; armour gets a grade from the faction's `armors 0..5` chances, a weapon a manufacturer and model from the list), merged
  into stacks and placed dearest first into the shop's 12 x 20 stock grid (what does not fit is destroyed). The purse (`Squad.Money`) is clamp(0.25 x the stock's value, 3000, 25000)
  plus the lists' money rolls (which the original adds as a stack of cats; **engine choice**). The stock is replaced and the purse refilled every `vendors refresh time` game hours.
  **Engine choice**: the stock is held by the squad (the original's squad stock goes to inventory sections not decoded; a building shop's to its containers, and buildings have no
  state here). A roaming trader that unloads loses its shop and gets a new one when it loads again (its inventories are rolled again in the same way).
- **Trading.** `OpenTrade(customer, trader)` (a right click, [Player](#player)) walks the customer to the trader (60 game seconds to get there) and holds the trader still (residents
  wander; the original's shopkeepers stand at their node); within 30 units (**engine choice**: the original's trading range is **Unknown**) the trade is open and `WorldSnapshot.Trade`
  publishes the stock with buy prices and the customer's main and backpack items with sell prices. `BuyItem` and `SellItem` (an `InventoryItemToken` and a count) move as many units
  as the payer can afford ("You can't afford that." / "The shopkeeper can't afford that.") and the receiver has room for ("No room for that item."), and the cats between
  `Player.Money` and the shop's purse. Walking away, a knock-out, a fight or a hostile trader closes it; `CloseTrade` too. The trader goes back to wandering, or to what the
  [AI](#ai) has it doing (the held trader is `CharacterCold.Engaged`, which the AI leaves alone).
- **Stolen goods and fencing.** A loot-all that is a theft by the crime rules (`CrimeSystem.IsLootTheft`) marks what it takes as stolen from the victim's faction
  (`ItemInstance.StolenFrom`, the item's "taken from" owner of carry-loot.md 4.4 reduced to its faction); an item is stolen while its holder is of another faction. Stolen and clean
  units do not stack together (**engine choice**). Selling a stolen item to a shop without `buys stolen` is caught unless a roll is below lerp(thievery / 100, 0.4, 1.25), halved
  when the item was stolen from the shop's own faction (economy.md 8.4): "Got caught selling stolen item.", no sale, a pending Fencing against the shop's faction booked at once
  with the shopkeeper as the witness. The window marks the risky items; the original's prompt with the percentage is not built. Smuggling (`buys illegal`, illegal goods) is not
  decoded and not built.
- **Hash and saves.** The shops (stock, restock tick, fill count) are hashed with their squads, so a world without shops hashes as before; the open trade is not world state. The
  stolen mark is **not hashed yet** (adding it moves the `carry` and `crime` golden hashes; [Open questions](#open-questions-and-known-gaps)). Nothing of the economy is saved beyond
  the player's money: shops are rolled again for the squads a load makes; inventories are not saved anyway.
- **The windows.** Faithful (2026-10-14): as in the original, trading shows the trader's stock as an inventory window (the original's trader variant of the inventory GUI,
  [ui-screens.md](game/ui-screens.md) section 2) beside the character's inventory window, opened when the trade opens and closed with it. Dragging stock onto the pocket or bag grid
  buys it into the cell under the drop (`BuyItem.Section/X/Y`; elsewhere when that cell is taken), dragging an own item onto the stock sells it (`SellItem.X/Y`); the whole stack, split
  to what the payer affords. Closing the trader window (its close button or Escape) ends the trade. **Engine readings**: the trader variant's layout is **Unknown** (the class is known,
  not its `.layout`), so the bag's `Kenshi_InventoryGenericWindow` is used, its grid found as `backpack_content`, else `main`, else the largest inventory-slot widget; prices are in the
  item captions (the original's tooltip is not built); the trader window is placed left of the inventory window. Meitou mode has its own panel (stock and goods lists, "1" and "ALL").
- **Not built:** the item tooltips with prices, the stack-splitting drag, building shops and `vendor money`, `special items`, artifacts, the culture's illegal goods, smuggling,
  slaves, prisoners and animals for sale (12), bail, NPC shopping (`Task_Shopping`), picking up money items, blueprint and research costs.

## AI

Stage 9, begun 2026-10-14, from [game/ai.md](game/ai.md) and [game/ai-tasks.md](game/ai-tasks.md). Code: `Meitou.Data.Gameplay.Ai` (`TaskType`, `AiDatabase`, `AiPackage`,
`AiGoal`) and `Meitou.Simulation.Ai` (`Blackboard`, `AiMind`, `TaskBehaviours`, `AiSystem`).

- **Data.** `TaskType` is the game's `taskType` with its numbers (the names of ai-tasks.md's first table; 0 has no name there, 289 and 290 have none in the exe). `AiTask` reads an
  AI_TASK's `enum`, `classification` (the tier), `targeting` and `ending`. `AiDatabase.Package` resolves an AI_PACKAGE as ai.md "Goal loading" says: `inherits from` first, an entry
  whose type and target are already listed is skipped (the parent's wins), the Squad list from `Squad AI Goals` else `Leader AI Goals`, Squad2 from `Squad2` else Squad else Leader,
  Slave from `Slave AI Goals` only, applied to each record of the chain (**engine reading**: the doc states the fallback for one record); an entry's hours are (val0, val1) and its
  weight val2 x 0.01, 0 meaning 1. `Packages(template)` gives a template's `AI packages` with their slots (v0); `RaceGoals` a RACE's `AI Goals` (weight val1 x 0.01, always in
  window). The FACTION `AI fallback` (one record uses it) is not read.
- **Blackboards.** A squad whose template lists AI packages, and which is neither the player's nor a roaming platoon's active half, gets a `Blackboard` at the AI's first look after
  it appears (the platoons keep their travel stand-in, [Population](#population)). Every 30 ticks it chooses the package by ai.md's slot rule: slots from the highest down; in a slot
  the current package stays unless finished (then its `Leads to` starts), an earlier entry that can start pre-empts it, and a lone slot-0 package starts even when it cannot. Plain
  packages (signals 0, 2, 3, 7, 8, 9, 18 and 21: the base class) can always start and never finish. **The subclasses are not built**: here they never start, except as that lone
  slot-0 package, and never finish, so a template's higher-slot patrols and wars stay idle and its slot-0 town package runs.
- **Choosing a goal.** Each member has an `AiMind` and thinks every 15 ticks (staggered by slot), and at once when its squad starts another package. The goals of its role list (the
  squad leader takes the Leader list, the others by squad role: Squad, Squad2, Slave), its race's `AI Goals` and the default IDLE (fluff tier, weight 0.1, ai.md "Default tasks")
  are scored tier by tier, urgent, then non-urgent, then fluff; the first tier with a positive score wins (ai.md's passes; the job and task-request lists do not exist yet).
  Score = desire x weight x crowding^n, x 1.25 for the goal it is acting out already (**Observed**; near ties do not flip). A goal outside its hour window (the integer hour of
  `World.GameHours`; start >= end wraps midnight) is skipped. **Engine choices**: the desire is 1 for a goal it can act out and 0 for the rest (the TaskData desire functions are
  not decoded), the crowding factor is 1 (per TaskData in the original, **Unknown**), and the sticky commitment timers are not modelled.
- **Acting out.** The original runs a `Task_*` class and a GOAP plan; towns have no buildings, beds or nodes here yet, so `TaskBehaviours` maps 22 goal types onto the movement
  tasks. IDLE mills about the character's post (wander within 25 units). PATROL_TOWN, PATROL, WANDERER, RELAX_IN_TOWN_PACKAGE and SHOPPING roam the squad's home. The
  stand-at-node goals (shopkeeper, guards, general), HOLD_POSITION, STAY_IN_HOME, MAN_THE_GATE and SIT_ON_THRONE walk to the post and stand. FOLLOW_SQUADLEADER, FOLLOW_SLAVEMASTER
  and BODYGUARD follow the leader (the leader itself cannot). GO_HOME_AND_GO_TO_BED, its SECURE twin and SLEEP_ON_FLOOR walk to the post and lie down (`Rest`); SIT_AROUND sits
  where it is. **The post** is where the character stood when the AI took it over, the stand-in for a node of its home building; three walks in a row that end elsewhere make the
  spot it stands on its post. Every other type (combat, rescue, prisoners, slaves, machines, doors, ...) is not acted out and never chosen; combat, fleeing, rescue and first aid
  stay with their systems.
- **Who runs whom.** The AI gives a character a task only while nothing else holds it (`AiSystem.Holds`): not the player's, not in a fight, fleeing, down, carrying, carried or
  trading, and its task is idle, wander, follow, or the AI's own walk, sleep or sit. A fight takes it over; the aggro's stand-down hands it back. **Known effect**: a sleeping
  (`Rest`) NPC is skipped by the aggro's look, as a resting one always was, so it fights only when attacked.
- **Time.** `World.GameHours` (day x 24 + hour, from `WorldSettings.StartHours`, default day 1 at 13:00) advances one tick of game time per tick; `WorldSession` sets it from its
  `GameClock` before each tick, so the debug hour slider and a loaded save's clock reach the AI. It is an input, not hashed.
- **Shown.** `CharacterSnapshot.Activity` is the goal's TaskData text (`Shopkeeper`, `Patrolling town`, `Going to bed`) while the AI runs the character; the host shows the name
  and the activity beside the cursor over any other character, in both UI modes (the original's mouse-over text is its `TooltipPanel`, layout **Unknown**).
- **Hash and saves.** Blackboards and minds are hashed with their squads and characters only when present, so worlds without AI packages hash as before (no pin moved). Nothing of
  the AI is saved (the original's `currentPackage`, `pjobT`, `tjobT` and the rest, [save.md](formats/save.md)): residents are made again on load and choose again.
- **Tests.** `AiTests`: the data rules (parent wins, fallbacks, weights, slots), the hour windows, the slot rule, a town whose shopkeeper keeps shop while its guards patrol at 13:00
  and walk back and lie down at 22:30, thread-count independence; `[Slow]` the merged base game's counts from ai.md (219 packages, 124 tasks, 804/561/58/64 goal entries, no
  unresolved link).
- **Not built:** the package subclasses (patrols out of town, wars, contracts, bar hang-outs, prisoner shipping) and `canActivate`/`isFinished` beyond the base class; the GOAP
  planner, TaskData conditions and desire functions; jobs and task requests; the urgent goals that need the senses (`SELF_PRESERVATION`, `PROTECT_ALLIES`, `ATTACK_ENEMIES`: the
  aggro and flee systems stand in); homes, beds, doors and nodes; dialogue's `AI contract` and `change AI`; the unloaded modes; saving the AI state.

## Saves

`Meitou.Data.Save` reads and writes the save folder ([save.md](formats/save.md#implementation-as-built-stage-10)); `Meitou.Simulation.Saving`
(`SaveLoader`, `SaveCapture`, `SaveModel.cs`) maps it to and from a `World`. The game host calls it for the quicksave and quickload keys ([below](#quicksave-and-quickload)).

**Loading** (`SaveLoader.Load(world, save, data, options)`, `world` not started and empty, `data` the `PopulationData` of the running game):

| From the save | Into the world |
|---|---|
| CAMERA `time day / hour / minute` | `LoadedSave.Clock` (a `SaveClock`); the host sets its `GameClock` from it |
| CAMERA `player money`, `pfaction name` | `World.Player.Money`, `World.Player.Faction` (the faction's position, else `204-gamedata.base`) |
| GAMESTATE_FACTION relation tables | `PopulationData.Relations`, the game's live table (`FactionRelations.Set`, on by default; the player's faction has no table and is answered by the others', [Hostility](#hostility-and-relations)); prosperity, platoon counters and trust stay in `LoadedSave.Factions` (`FactionState`) |
| The player's platoons that have a file | One `Squad` each (`Name` = platoon name, `TemplateId`, leader = the `is leader` character) and their characters: saved position (with height), yaw from the rotation quaternion, name, role (`squad mem type`), `IsPlayer`, speed from the race and the saved stats; `World.Player.Squad` is the first platoon |
| STATS, MEDICAL_STATE of those characters | `CharacterCold.Save` (`SavedCharacterLink`) with `Stats` and `Medical` (race from the appearance record or the CHARACTER); a dead character is not made and stays in the save |
| CAMERA `selected_character`, `selected_characters<k>` | `World.Player.Selection` |
| Other platoons with a placed base town and a squad template | A roaming `Platoon` stand-in (state `Unloaded`, saved position, size = character count, template, faction); members are made again from the template when a zone near it is active. `LoadedSave.RoamingPlatoons` maps the world's platoon id to the save's name |

**Saving** (`SaveCapture.Capture(world, data, clock, loaded, options)` returns a `SaveGame`; `SaveGame.Write(folder)` commits it). With the
`LoadedSave` the world came from, the loaded save is cloned and the modelled parts are written over it: the clock, money, relations changed
since loading (`RelationBaseline`), prosperity and platoon counters, and for every player squad the platoon and its characters (position,
facing (the saved quaternion is kept while the yaw has not changed), name, leader flag, STATS and MEDICAL_STATE from `SaveCaptureOptions.Bodies`
or the loaded ones, new recruits as new characters with the real key sets, characters that left the world removed with their items), the
selection, the CAMERA squad and member counts, the roaming platoons' positions. Everything else is carried through unchanged (towns, nests, war
state, weather, research, decals, zone files, the NPC platoons' own files, the portrait atlas, the CAMERA's unique-character and camera data).
Without a `LoadedSave` (a new game) a save is built from the data: all factions with the data's relations (prosperity 1000), war states at
defaults, one default GAMESTATE_TOWN per `SaveCaptureOptions.Placements`, empty BIOMES / RESEARCH / TERRAIN_DECALS, the player's squads as
platoons `Nameless_<n>`, no zones. The CAMERA `mods` list is `SaveCaptureOptions.DataFiles` (the base game's four by default). Saves go to our
own folder while testing, never into the original game's `save` folders.

**Checks.** `SaveWorldTests` (synthetic town): a new game captured, written, loaded into a second world (positions, yaw, names, money, clock,
selection, relations equal), saved again byte for byte; changes reach the file while a record the world does not hold survives. Against three
real saves (`[Slow]`): each loads (195 to 354 roaming stand-ins), loading and capturing again changes only the PLATOON and CAMERA records of
`quick.save` and nothing in the player's characters, a change made in the world survives writing and reading, and a world loaded from a real
save runs 150 ticks and captures again with the same platoons and characters.

**Not carried yet** (kept as the save had it, or regenerated): first aid and resting (the `Treat` / `Rest` tasks, the medic job: no key in MEDICAL_STATE or GAMESTATE_CHARACTER holds them; the squad toggles `heal`, `sleep`, `sit` of the CAMERA record have **Unknown** short-key meanings and are not mapped); carrying (GAMESTATE_CHARACTER
has `carrying` and the handle `carrying*`, but the carried body is usually an NPC, whose character is not saved, so a quicksave drops the link and
the body lies where it was), a pick-up, put-down or loot under way, the rescue job (the CAMERA squad toggle `rescue` is not mapped either) and what
a loot moved (inventories are not saved); and a kit's used charges with the rest of the inventory, the NPC platoons' characters (regenerated from the template; their files stay as
they were), towns and their state, war state, weather and season (the weather rolls anew on loading), research, zone buildings and loose items, inventories and equipment (a loaded character's are rebuilt from its record and look; a loaded animal gets its natural weapon back, but its age, size and HP mult factors
(`CharacterStats.Scale`, `MedicalState.ScaleK` / `ScaleZ` / `HasStatsObject`) come back as a person's 1s, [Animals](#animals)), AI jobs,
the saved look of a loaded character (rolled again from the saved serial through `IAppearanceSource`; the serial of a character Meitou saved is its look's seed), pending crimes (no key known), `5-redirect`.
The player's characters' bounty ledgers are carried (2026-10-13, `BountySave`: the GAMESTATE_CHARACTER keys `bountyfac<k>`, `bountyexp<k>`, `amount<k>`, `crimes<k>`, `claim<k>`;
the raise time converted between world ticks and the save's game hours, day x 24 + hour, [Crimes and bounties](#crimes-and-bounties)). **Unknown**:
whether the original loads a save written from nothing ([save.md](formats/save.md#implementation-as-built-stage-10)); the orientation convention
of the saved quaternion (read as a rotation about Y, yaw from +Z towards +X, written back the same way; an unchanged character keeps its
quaternion bit for bit).

**Wired by the host** (2026-10-13, [Quicksave and quickload](#quicksave-and-quickload)): `SaveLoader.Load` runs on a new `World` built with the same
`PopulationData` before its first tick, the `LoadedSave` is kept for the next `SaveCapture.Capture` with the host's clock, and a loaded
character gets its race, its saved stats and medical state, an inventory and a fighter (`CharacterAssembly.AttachSaved`, the loader does it, so the body, combat and
movement systems treat it like a new game's character); `SaveCapture` reads the live `CharacterCold.Stats` / `Medical` (`SaveCaptureOptions.Bodies` stays as an override). A character with a rolled
look is saved with the look's seed as the serial of its handle (save.md documents the serial only as the handle's own random number), so loading rolls the same face, body, hair and clothes
again; the inventory is rebuilt from the same look. Still to do: keep the population system from making residents for a town whose platoons were loaded as
stand-ins (a quicksave made by Meitou holds none, so this only matters when loading a save of the original game, which the host cannot do yet); the wound list and
the pain of the medical state are not in the save's keys and start empty and at 1 (the medical tick works the pain out again).

## Sandbox

`meitou --sandbox [n]` is a debug map for character animations: the real simulation (`StandardSystems`: movement, combat, animation) and the real
`CharacterRenderer`, but no world. It exists to compare a character's motion with the original game at a known speed, without a town in the way.

- **Floor.** `WorldFrame.LoadFlat` builds a `WorldScene` with no heightmap file: every height is the same (500 units, above the water at 100), terrain
  textures, objects, foliage, water, the streamer, the placed fog volumes, particles and clouds are off and the weather is held clear (`Default`;
  `--weather` and `--clouds` still override), the sky and sun are the usual. The 1000 x 1000 floor around the origin is drawn as
  screen-space overlay lines (`GameHost.Sandbox.cs`): a line every 10 units, a stronger one every 100, the X axis red and the Z axis blue. They have no
  depth test, so they also cross the characters. SSAO is off by default (it bands a perfectly flat floor).
- **Characters.** `PopulationSystem.SpawnSandbox` makes one idle character (the first member of the default new-game start, or `--sandbox-character <name>`, a CHARACTER or
  an ANIMAL_CHARACTER by name or string id,
  with the loadout the generator gives it, so combat works): the hero, the player's, selected, at the origin facing +X. With `n = 2` a second one of
  a faction hostile to the player's stands 30 units away facing it. With `n > 2` (up to 1000) the rest stand idle in rings 15 units apart round the
  pair, each with a look of its own, of the player's faction (so they keep out of the fight). No navmesh, no towns: paths are straight lines on open ground.
- **Speed.** The movement system has two sandbox-only overrides in `CharacterCold` (`ModeOverride`, `SpeedOverride`, set by `SandboxMovement`; they are not
  hashed and nothing in the game sets them). The mode replaces the one of every order (the game's right click is mode 2, free); a fixed speed replaces the
  whole speed chain, so the character walks at exactly that many units per second after the usual 15 units/s per second ramp.
- **Keys** (besides the game's: right click moves, R stops, Space and F2..F4 pause and slow or speed time).
  Z walk (cap = race walk speed), X run (cap 55), C free (no cap, the default); `[` / `]` fixed speed -5 / +5 units/s (0 is off, and Z/X/C also clear it);
  P patrols the hero along the line x = -200 .. 200 (z = 0), the next leg ordered on arrival (it stops at once at each end, as characters do);
  G makes the two fight (both attack each other); Y toggles the camera following the hero (the middle of the pair with a foe).
- **Panel** (bottom right): for the hero and the foe the speed v (the velocity the movement system set) and the speed measured between the last two
  snapshots, the speed chain's top speed, the mode with its cap, the fixed speed, the position and yaw, `CharacterAnimation.Phase` and `Rate`
  (the synch phase and the playback factor F), and every published layer as `name  t time/length  w weight` (lengths from `AnimationLengths`).
- **Command line.** `--sandbox-mode` (comma-separated: `walk`, `run` or `free`; a fixed speed in u/s; `patrol`; `fight`, the G key at the start, added 2026-10-13 for
  pictures of animals fighting; e.g. `run,patrol`) starts the hero in a state; with `--screenshot --ticks n`
  the picture is taken after n ticks of it (`--move-to x z` works as in a new game). The start camera is boom 70, pitch 22,
  yaw 35 (`--distance`, `--pitch`, `--yaw`).

## Duel

`meitou --duel [x,z]` (added 2026-10-13, for the trailer; `GameHost.Duel.cs`) is a scene on rails: two Samurai fight on the real map at sunset and every lap is
the same fight to the tick, so a camera flight recorded from any angle lines up with every other take. Engine choices throughout; nothing of it runs in the game.

- **Place and light.** The built-in spot is the owner's pick (2026-10-13, from a screenshot): a flat rock mesa in the desert dunes, top about 1350
  over x 35100 to 36100, z -112100 to -111900, dropping about 200 units to the south; the fight is at 35400, -111920, about 60 units from the south
  cliff and 370 east of the rock spire at the mesa's west end (35030, -111940, found by triangulating three wide screenshots: no BUILDING placement is
  near it), the setting sun to the west (the sun sets towards −X, `SkyClock`). Before it, a dune crest at 42398, -110400, the owner's first dune spot at
  41062, -110207 and a flat cliff top at -38016, -67824 with pines along the ridge was used,
  picked from a heightmap search for flat ground with a drop and no terrain above 0.6 degrees towards the setting sun, then by sunset screenshots; a
  hilltop above a bay at -90144, -120240 was the runner-up there (the sun on the water). Unless `--time` or `--weather` say otherwise the time of day is
  22:00 (the sun about 5 degrees up) and the weather is held clear (`Default`). The time of day is held still (`GameClock.HoldHourOfDay` after each
  frame's ticks; game time runs on for what animates by it, the heat haze), so the light is the same through a lap and across laps. The start camera looks west into the sun with the pair left and right (yaw 90).
- **World.** No towns (so no residents or roaming squads), aggro off, paths answered inside the tick (`SynchronousPaths`), the population system on
  the tick thread, the camera focus fixed on the spot (`SendFocus` sends nothing). The characters walk on open ground (`OpenGroundWalkability` on
  the heightmap), not the navmesh: the first spot, the cliff top, is a navmesh island the build prunes (`meitou-tools navmesh --zone 23,17`, **Observed**
  2026-10-13), so on the navmesh the pair could not reach each other.
- **The pair.** `PopulationSystem.SpawnSandbox` with a faction override: two NPCs of the CHARACTER record named `Samurai Heavy` (gamedata.base: Samurai
  Armour, Samurai Helmet, legplates, boots, a nodachi) with the looks of United Cities, 40 units apart across the sun, facing each other; both get an
  `AttackOrder` on tick 45. The looks are the first two variants whose rolled clothing has a helmet (slot 3, `PopulationSystem.SandboxLook`). The first
  choice, the record named `Samurai`, comes from the mod rebirth.mod in this install and never wears a helmet (its lists have none; **Observed** 2026-10-13).
- **The lap.** At start the duel is run once headless on a world of its own (`MeasureLap`; 5000 ticks in about 0.3 s, Release, this PC). Two of the same
  record are evenly matched: in the built-in duel the first one is down after 145.7 s (18 hits, 47 blocks, 3 dodges; **Observed**, seed 0; the mod's
  Samurai on the cliff top: 165.2 s, 28 hits, 61 blocks). A lap is the last 40 s before the knockout and 4 s after it; the ticks before it run headless
  at each restart (about a quarter of a second). A duel nobody loses in 5 minutes loops its first 60 s. The pair drifts as it fights (90 to 140 units),
  so the spawn is moved by the measured offset and the lap's fight is centred on the spot (the fight is unchanged by the move: same blows, same knockout tick).
- **Restart.** A new world from `WorldSession.ReplaceWorld` with the same setup, the fight before the lap run headless, the game clock back at the
  start hour (`GameClock.SetTime`), the simulation clock's phase dropped (`SimulationClock.ResetPhase`, so the ticks fall on the same frames), the
  animation clock (clouds, water, foliage sway) at 0 and fresh per-character renderer state (`worldEpoch`). It happens at the lap's end, on
  Backspace (the HUD says so), and whenever a cinema flight starts or a recording starts (also over a flight already playing; docs/viewer.md "Cinematic camera"): the
  cinema sets its state in the key handler, before the frame, so each take's first frame is the lap's first. The Tab panel's time of day slider sets the
  held hour.
- **Checked.** The measured run keeps the world's state hash at the lap's end; each lap that ends on that tick is compared with it and the log says
  `lap n ended as measured` or `DIFFERS`. Headless (`--duel --screenshot --ticks n`) three laps ended with the measured hash (again at the desert spot), and pictures of lap 1
  and lap 3 at the same moment were pixel-identical (`meitou-tools image-diff`, **Verified** 2026-10-13). A lap shows end − skip − 1 ticks (1319 here).
- **Not reset.** The particles (the leaves the map's effects blow) and the renderer's temporal history (upscaler, GI probes) carry on across a restart;
  `--no-particles` takes the leaves out where takes must match to the pixel.

## The game host

`src/Meitou.Game` (file layout in [engine.md](engine.md#the-game-host-srcmeitougame)). `GameHost.Boot` builds the `PopulationData`, the
walkability (the navmesh through `NavAdapter`, or `OpenGroundWalkability` in the sandbox), the systems through `StandardSystems`, the
`WorldSession` with `MinPartitionSize = 128`, and the player interface; `--no-population` leaves the world empty. Each frame it sends the focus,
keeps the navmesh on the active zones, and fills the renderer's `CharacterDrawList` from the two snapshots, interpolated
(`GameHost.Characters.cs`); characters without an appearance get a debug square coloured by faction (`DebugOverlay`). The character renderer
(`Meitou.Rendering.Characters`: skinned meshes, attachments, appearance, the animation layers, instancing, LOD) is described in
[character-renderer.md](character-renderer.md).

Three ground functions, on purpose: the camera follows the drawn terrain (`gpu.Terrain.HeightAt`), picking and the open-ground stand-in use the
CPU heightmap, and characters stand on `NavmeshWalkability.GroundHeight` (up to about 0.5 off the heightmap on flat ground, on floors in
buildings). They are not to be unified; `MoveOrder.Target.Y` is ignored (the movement system re-samples the goal's height).

### Quicksave and quickload

`F5` quicksaves and `F9` quickloads, the original's keys ([ui-input.md](game/ui-input.md#4-action-table); the fps counter that was on `F9` is on `Ctrl+F9`, and a saved
`ToggleFps` binding of `F9` is dropped at start like the old screenshot `F12`). `GameHost.Saving.cs`; the rules are engine choices.

- **Where.** `%LOCALAPPDATA%\Meitou\save\quicksave` (`QuickSave.Folder()`), Meitou's own slot next to its other caches. The original's quicksave is `%LOCALAPPDATA%\kenshi\save\quicksave`
  ([save.md](formats/save.md#autosave-and-quicksave)); the host never reads, writes or deletes anything under `kenshi\save`, so the user's saves of the original game are safe, and
  the original never meets a save with Meitou's differences. Nothing is written to `settings.cfg` (no `continue=`). A run without the town population (`--no-population`, the sandbox) says so on the HUD and does nothing.
- **Saving.** The key only sets a request (the control tick runs inside `WorldSession.Advance`); the frame loop takes it right after `Advance` returns, when no world tick is in flight.
  `SaveCapture.Capture` runs there (the clock is `GameClock.Day/Hour/Minute`, the placed towns and the install's load order go in): it only reads the squad (0.8 to 10 ms in a run with the full load order; the first F5 of a run took 50 ms, mostly the JIT, the host runs without tiered compilation. **Observed**, Release, this PC). The save it starts from, which costs 40 to 50 ms (the clone of the quicksave the world was loaded from, or the new save with every faction and town), holds nothing of the world and is made ahead on a worker (`SaveCapture.PrepareBase`, started whenever none is ready and discarded after a load); `SaveGame.Write` also runs on a worker
  (it writes into `_current<N>` beside the slot and swaps the folder in with two renames, so a crash or a full disk leaves the old quicksave). The worker only queues the result; the HUD message
  ("Saved", or "Save failed: ..." in red) is shown by the next frame. A save or load asked for while one is still running is refused with a message. There must be a player squad (`--new-game`,
  or a loaded quicksave); the first capture is from nothing (`SaveCapture` without a `LoadedSave`: all factions with the data's relations, a default state for every placed town, the squad as platoon `Nameless_<n>`), later ones carry the records of the quicksave the world was loaded from.
- **Loading.** The slot is read on a worker; when it is read the frame loop makes the new world at the same point: `StandardSystems.Build` again (a new population system with its thread, a new path
  service), `WorldSession.ReplaceWorld` builds the `World` with the same settings and walkability and runs `SaveLoader.Load` on it before its first tick. A load that fails (no folder, a record that does not read) leaves the running world
  untouched and shows "Load failed: ...". After the swap the old world and its systems are disposed; the game clock is set from the save (`GameClock.SetTime`: day, hour and minute, so up to a game minute is lost; it restarts the clock, whose game seconds and `HoursSinceStart` count the session), `population.ZoneGate`
  is set again, the camera is cut to the player's leader keeping the heading, pitch and boom (the free camera is left), the navmesh of the zones round the leader is awaited like a new game's start (at most 15 s), one tick runs (it publishes the snapshot,
  so a paused game draws the squad, and takes the first look at the zones), the draw list's per-character renderer state is not reused (the host's world counter is part of the key), the mouse state (drag box, steering right click) is dropped and the HUD says "Loaded".
  The selection is the saved one (CAMERA `selected_character`s), not cleared. The speed (paused or 1x/2x/5x) and the camera settings are the running game's. Loading works in a run that began without `--new-game` (it gives the player a squad).
- **Saved and loaded:** the clock, the player's money and faction, the factions' relations (the data's table is replaced by the save's on load) and prosperity, the player's squad(s) with each character's position, facing, name, role and leader flag,
  stats and medical state (blood, bleeding, hunger, parts: flesh, bandage, rig, stun, wear, hit multiplier), the look (as the seed in the handle), the bounties and the selection. **Not saved:** the town residents and roaming squads (made again from the towns near the player; they are not
  where they were), the weather (rolled anew), orders, paths, queued moves, animation and combat state (everyone stands idle and unengaged), inventories (rebuilt from the record and look: eaten food is back, loot is gone), the wound list and pain, pending crimes, buildings and
  items on the ground, the camera's position (the leader's), the game speed. The state hash of a loaded world is not the original's (the tick, paths and the rest above differ); `QuickSaveTests` compares the carried state field by field and that two loads run the same at 1 and 4 threads.

## Measurements

All on this PC (12 cores), Release builds, the base-game load order; timings on a busy machine move by tens of per cent.

**Tick cost** (2026-10-07; `HubTests.Tick_cost_of_the_hub_is_recorded`, `[Bench]`, flat ground, 600 ticks after the town loaded; a tick is the
same cost at every speed, so a real second costs 30 ticks at 1x and 150 at 5x). Before animation, bodies and combat:

| Characters (The Hub) | Threads | ms per tick | ms per real second at 1x | at 5x |
|---|---|---|---|---|
| 32 (9 squads) | 1 | 0.07 | 2.0 | 10 |
| 32 | 4 | 0.08 | 2.5 | 12 |
| 122 (Squad size multiplier 10) | 1 | 0.15 | 4.4 | 22 |
| 122 | 4 | 0.18 | 5.5 | 27 |
| 122 | 8 | 0.15 | 4.5 | 23 |

At these counts the barrier cost of the worker pool outweighs the parallel gain; threads pay off from some hundreds of characters on (stage 9
measures again with the AI).

**Frame rate** (2026-10-08, after the animation round; `--new-game --quit-after 10`, the host's `profile` line and the renderer's `characters`
line; per frame):

| | no population | `--new-game`, 8 sim threads | `--new-game`, 1 sim thread | `--new-game`, `MinPartitionSize` 128 (now) |
|---|---|---|---|---|
| fps | 192 | 135 | 162 | 165 to 183 |
| simulation advance, ms per frame (per tick) | 0.02 (0.08) | 0.41 (2.17) | 0.07 (0.41) | 0.06 to 0.08 (0.40 to 0.48) |
| host draw-list fill | - | 0.025 | 0.022 | 0.015 |
| world draw (render thread CPU) | 2.5 | 4.95 | 3.4 | 2.6 to 3.1 |

- The cost on our side was the worker pool: 34 characters cut into 2 partitions (`MinPartitionSize` 16) woke worker threads and waited at a
  barrier in every parallel phase of every system (2.2 ms per tick against 0.4 ms on one thread), and the stall showed in the draw time too.
  Hence the game's 128; `SystemPhases` later removed the barriers of phases a system does not implement. A Hub tick is 0.4 to 0.5 ms, of which
  about 0.15 ms is `Publish` building the snapshot lists (1.5 % of a frame at 30 ticks per second; left as is).
- Host fill and interpolation: 0.015 to 0.025 ms per frame for about 30 characters.
- Renderer side: the characters add about 0.1 to 0.2 ms of CPU update and 0.05 to 0.08 ms of draw recording, but `DrawWorld` is 0.5 to 1 ms above
  the empty world, mainly the character shadow cascades (about 2700 shadow draw calls per 10 s smoke) and the 70 to 90 draw calls of 21 to 34
  characters; the GPU colour pass of the characters is 0.04 ms. That residual is the renderer's ([character-renderer.md](character-renderer.md)).

**Navmesh** (The Hub, zones 20.32 and 21.32). Build 0.25 to 0.3 s per zone (2026-10-07; cell 1.5 / tile 64: 0.55 s; tile 128: 1.4 s). Gather
(2026-10-07): 1.2 to 1.9 s cold before the parallel foliage, after it about 0.55 to 1.0 s cold and 0.15 to 0.25 s for the next zone, so a first
zone is ready in about 1 to 1.3 s and later ones in about 0.5 s, off the simulation thread; a cached zone loads in milliseconds. Interiors
(2026-10-08): the 21 of the Hub zone build in 80 to 400 ms on the worker threads; `meitou-tools navmesh --town "The Hub" --repeat 3` gave 667 to
761 ms per build with interiors (21 interiors, 19231 polygons) and 506 to 628 ms with `--no-interiors` (17676 polygons). The new game's wait for
the zones round the start: 3.2 s cold, 75 ms cached. A Hub crossing west to east (about 3500 straight, 4200 walked) takes about 45 ms; a town
crossing 20 to 50 ms. A full test run once took 13.9 s for the zone test under load; quiet it is under 1 s, so the timing tests are `[Bench]`
or take the best of three.

**Combat** (2026-10-08). 60 duels of two equal katana fighters (all stats 50, katana at quality 0.5, no armour, katana techniques written out
like the install's): median 13 s of game time (385 ticks, range 4 to 43 s), 18 blows thrown (5 to 61), 10 hits (3 to 24); every duel ends in a
KO. A much better fighter (80 against 30) wins 24 of 24. Plate over the chest, stomach, arms and head cuts the cut damage per hit by two thirds.
A duel with the install's records (Greenlander, a Katana of a level-50 model, heavy armour on one side, real clip lengths, all 44 techniques) took
56 s and 43 blows and is identical at 1, 4 and 16 threads; a crowd of 140 fighters (60 duels and 20 thirds joining in) hashes the same at 1, 4
and 16 threads at six checkpoints. These tests set `MinPartitionSize` to 1 (duels) or 4 (the crowd) so the fighters really spread over 2, 8 and
32 partitions.

**Hostility** (2026-10-13; a `[Bench]` test of `TownAggroTests` cut in the 2026-10-13 cleanup, flat ground, The Hub with the looks, one thread, 1800 ticks after
the town loaded, the aggro system's phases timed inside the tick; the machine was busy, so the first (cold) runs read 5 to 20 us and are left out). Squad size 1, 37
characters: 0.5 to 1.1 us per tick of 22 to 35 us. Squad size 10, 127 characters: 1.3 to 1.4 us of 110 to 120 us (Schedule 0.5, Act 0.1, SlowWorld 0.7: the two passes over the
slots). The Hub has no hostile pair, so the looks are skipped; before that skip a look cost about 0.9 us (a 300-unit query over a crowd of 127: 7 columns of the grid, which
`SpatialGrid.Query` now searches once per column instead of once per cell, same order), 8 us per tick at squad size 10. A crowd with enemies in it pays that: about 1 us
per looking character every 15 ticks.

**Body time scale** (2026-10-08; `BodyFactory` / `MedicalState.Tick` on a base-game Greenlander, `dt` 11/36000 h per tick, CONSTANTS from the
install, unhurt and with one leg hit for 40 cut + 20 blunt, 72 game hours; at 5x the game runs 2.75 game hours per real minute, so 72 h take 26
real minutes):

| BodyTimeScale | Unhurt | Hit leg |
|---|---|---|
| 1 (documented) | hunger 3.00 -> 2.61 after 24 h -> 1.82 after 72 h (the speed factor drops once it is below 2: 70.5 -> 65.0); blood and parts unchanged | the leg stays at 40 % for 72 h (no healing, no degeneration); blood 89.4 % -> 88.0 % (bleeding stops after about 40 h) -> 89.6 % after 72 h, about 0.03 % per hour |
| 100 | same (hunger is not scaled) | blood back to 100 % within 3 h; the untreated wound degenerates: 40 % -> 37 % at 12 h -> 7 % at 24 h -> down at 25.8 h -> -300 % |

At scale 1 blood and healing rates look far too slow for game hours (**Observed**), and hunger alone starves a character in about 7 to 8 game
days (3 -> 0 at about 0.0164 per hour, extrapolated). At 100 the numbers read like play (a bleed clots and blood is back in hours, an untreated
wound rots in about a day). A scale between 1 and 100 is probably right (the original's `dt` is likely not hours).

**The wild** (2026-10-13; a `[Slow]` measurement in `WildTests` cut in the 2026-10-13 cleanup, not a pass/fail timing): the zone of a Wolf Den with every
fighting system on, 4 threads, flat ground: 11 characters, all wild (a wolf pack of 5 and the sector's homeless squad), 0.085 ms per tick. The wild's own
look is a lookup of the active zones in a table of the sites (2674 sectors and 324 nests with the base game) every 15 ticks; the squads are built on the
population thread. The caps keep it small: a sector makes at most ceil(P x amount) squads (3 with the base data at P = 1), a nest floor(0.7 N) members (5 to 21
for the nests the base data lays out).

## Open questions and known gaps

**About the original** (each with its doc): the unit of the medical `dt` and so `BodyTimeScale` (character-stats.md); how resident squads pick a
home building, the length of the three zone unload timers, the roaming cap for towns (factions-squads-towns.md, game-loop.md); the formation slot
rules and `squad formation` (pathfinding.md); the attacker's technique choice, `power`, `max encumbrance`, the Heavy_Hit length (combat.md); item
nutrition, the NPCs' food source, auto-eat, the `main` grid size (character-stats.md); how a start-off's relations, research and `force race` are
applied; the world states' initial values; for the wild (factions-squads-towns.md 6.2, 6.3) the islands of the area tick, the sector counters a and b, the nest respawn delay, the default of the nests multiplier and whether the 1250 of the nest spawner is squared; the corpse removal rule; the animation selection code (animation.md); the saved quaternion's
convention and whether the original loads a save written from nothing (save.md); the navmesh items in [pathfinding.md](game/pathfinding.md); the senses' unknowns, the leash and the run-away ratios ([perception.md](game/perception.md#unknowns)),
which game events use which relation event kind, what the coexistence flag does, the Passive toggle's default (ai.md, factions-squads-towns.md, [Hostility](#hostility-and-relations));
for crimes ([Crimes and bounties](#crimes-and-bounties), economy.md 11): how a killing becomes a Murder, the pending timer's countdown, the "innocent victim" flag, what the witness
function's squad-level kind 7 does, the faction parent `+0x38` a bounty may go to, the victim test `FUN_140436610` of an assault, whether witnesses also hear, and whether the original's
relation changes for deeds come only through witnesses (Meitou keeps its deed-time events).

**Engine gaps**: most of the AI (stage 9; [AI](#ai) lists what is built), towns and their state, the economy beyond prices, shops and trading, buildings, the UI screens, the host's use of saves, combat clips and ranged
combat, doors that open and close in play, physics.

**Refactor items skipped because they would change behaviour** (each moves a golden hash or the timing of something). Done 2026-10-13: retaliation no longer
enqueues into the host's `CommandQueue` from inside the tick; it and the aggro use the world's deferred list (`World.Defer`, [Threading](#threading)), no pin moved.
- No end-of-tick hook: the combat system swaps its arrays at the end of its `SlowWorld`, so the result depends on the system order
  ([Systems and their order](#systems-and-their-order)). Moving the swap would change what retaliation reads.
- The two `MedicalContext` builders (`BodySystem` and `CombatSystem.MakeContext`) agree on toughness, strength, encumbrance (the body system now writes it to
  `Fighter.EncumbranceFactor`, 2026-10-13) and seed, and differ in `BodyTimeScale` (combat's is 1) and `Resting` (combat's is false). So they are equivalent only at time scale 1 and for a
  character that is not resting; with the game's `TickMedical = false` the combat one only serves hits, where neither matters.
- Combat ticks are `int` (`CombatSlot.ReadyTick`, `StunUntil`) while the world's tick is `long`.
- `ItemInstance.StolenFrom` (2026-10-14) is not in the inventory hash: hashing it moves the `carry` and `crime` pins (behaviour is unchanged; only the theft marks would enter
  the hash). Re-pin on the owner's machine when it is added: the pins are platform-specific (a Linux run gives other numbers for every scenario, even before the change).
- `BodyHash` does not cover the private `starveGrace` and `starvationKo` of `MedicalState` (nor every flag such as `BloodKo`), so a divergence
  there shows in the hash only later.
- `CharacterHot.Animation`/`AnimationTime` (the old movement layers) and the name length are still hashed; removing them moves every hash.
- `HoursPerTick` is a constant of its own, not derived from `WorldSettings.TickSeconds`.
- Navmesh: unloading a zone whose build is queued or running does nothing, so the zone comes back when the build publishes (R3); the cache key
  misses foliage, record contents, `seeds.def`, collision files, the DotRecast version and the load order (R5).
- Not done, structure only: the `CharacterCold` regrouping (every system file and the hash order), a shared weighted-pick helper, moving
  `CollisionCache`/`PathMode` out of Navigation and splitting `NavQuery`.

## Owner decisions

Answered by the owner on 2026-10-07; each took the recommendation.

1. **Time model: a fixed game-time tick.** Small steps at every speed; behaviour independent of the speed and the frame rate; 5x costs 5x the
   CPU. (The alternative was the original's model: a real-time tick whose step grows with the speed.)
2. **Tick length: 1/30 s of game time**, the present default.
3. **Walkability: our own navmesh builder** (Recast-style, the DotRecast library, zlib licence) with our own cache; the Havok tiles only as a
   reference. (The alternative was a clean-room Havok tile reader plus our builder for changed zones.)
4. **Order:** simulation stages 0 to 3 with markers first; the character renderer in parallel.
5. **Physics: none for now** (terrain and navmesh only); BepuPhysics2 when ragdolls and thrown bodies are needed.

## History

The work ran on branch `sim` (worktree `../MeitouClient-sim`) so the graphics work on `master` went on undisturbed; each track had its own
worktree and branch off `sim` and was merged back after the gates (build, quick tests, the determinism hash, one game smoke run). Tracks: A
simulation core and host (stages 0 to 3 and 6, then the wiring of bodies, combat, items and the navmesh), B character renderer (4), C walkability
research (docs only, the input to 5), D navmesh (5), E bodies (7), F combat (8), G saves (10). The joins written first so no track waited on
another: `WorldSnapshot`, `CharacterDrawList`, `IWalkability` with `OpenGroundWalkability`, `CommandQueue` with `SimCommand` and `MoveOrder`.
An overnight refactor (2026-10-08) split the large files, added `StandardSystems`, `SystemPhases`, the shared helpers and the golden hashes,
without changing a hash.
