# Factions, squads and towns

Original FACTION/Platoon/population/town research: binary functions, FCS and merged database/save
checks. Tables/expressions retain fields and event values. Runtime: [simulation](../simulation.md);
[AI](ai.md), [economy](economy.md), [save format](../formats/save.md).

## Relations and world states

**Verified** initial A→B: explicit A.relations v0, otherwise min(A.default,B.default); self100.
Hostile<=-30, ally>=50; directional flags/coexistence and event changes are distinct. Storage,
trust/known/player-faction branches and dialogue/reaction events remain scoped Observed where
not independently checked. Start-off relation overrides and initial WORLD_EVENT_STATE values
are not fully decoded; false as an engine default is not original-game proof.

## Squad factory

**Observed/Verified sentinel checks:** per entry count=v0 when v1=0 or100; otherwise inclusive
v0..v1, reversed range givesv0. Apply size multiplier/truncation/min1 unless dont-multiply.
Create leader role2, random choosefrom then squad role0/squad2 role1, animals/animals2, slaves
role4 as recursive squads, prisoners into available home cages. First member can become leader.
Offsets `(3*(i%8),3*floor(i/8)+(i%2))`, animals2 spacing5. Age=max(0,r*animal-age-random-0.1+v2/100).
Unique-leader replacement/recreation needs saved used-unique/state semantics, not duplicate spawn.

Generic weighted refs: v0=0 means100; exclude nonpositive weights and false world states,
first cumulative weight exceeding uniform roll wins; single candidate no roll. SpawnInfoList
instead drops v0<=0 and budgets expected squad sizes using its separate sentinel/range rules.
Do not reuse one zero-weight convention for both.

## Population and contents

Area-sector grid uses the4608-zone coordinate system with9-unit offset; biome populations,
homeless/roaming pools, nests, town residents/bar/roam squads and campaigns have separate caps,
spawn distances/timers/owner rules. Nest debris and regenerating squads are stateful. Towns
resolve faction, homes/parts/material replacement, residents, interiors, contents and culture.
World-state replacement towns and faction ownership affect both population and building data.

**Sector and nest counts** (**Verified** by code): a platoon is filed in the area sector of its position (`FUN_1408f6c80`, from the platoon constructor and
`Platoon::vfunc_28`): persistent platoons and nest squads into a set that is not counted, the rest into the counted set (+0x60, size +0x70), moved to a third set
(+0x88, size +0x98) while imprisoned or dead but still loaded; a sector's load is +0x70 + 0.5 x +0x98 against its group's cap. A nest's or town's pool is the set
of platoons whose `hometown` is it, by `sqt` (1 residents, 2 roaming), counted in living characters ([save keys](../formats/save.md#npc-platoons-meitou-mapping)).

Loot/building inventories, artifacts, shop stock, prisoners/slaves and placed debris derive
from different reference lists. Preserve slot/count/quality/owner fields and forbidden-item
rules rather than treating every list as uniform random loot. Unique/campaign survey is data,
not decoded behaviour. Off-screen town lifecycle and full campaigns remain incomplete in Meitou.

### 1. Object model

| Game class (RTTI) | Role | Notes |
| --- | --- | --- |
| `Faction` | one per FACTION record (103 in the base game, the player's included); owns its record (+0x240), a `FactionRelations` (+0x78), the platoon lists, `FactionUniqueSquadManager` | `FUN_1407fe270` is the loader from the record (**Observed**). Player flag: the field at +0x250 is non-null only for the player's faction (**Observed**, also in [game-loop.md](game-loop.md)) |
| `FactionRelations` / `PlayerFactionRelations` | the relation table of one faction towards all others | section 3 |
| `Platoon` -> `ActivePlatoon` (characters exist) / `UnloadedPlatoon` (abstract) / `UniquePlatoon` | a squad | per-frame stepping is in [game-loop.md](game-loop.md#update-threads-and-budget); creation is section 5 |
| `TownBase` -> `Town`, `Nest` | a TOWN record placed in the world; nests are TOWN records of `type` 0 spawned by the game | section 7; `Town`/`Nest` share almost all virtuals, the nest overrides slot 103 (wiped out) |
| `BasePopulationManager` -> `TownPopulationManager`, `NestPopulationManager` | per town: two pools (residents, roaming) with a population budget and a `SpawnInfoList` | section 6.4 |
| `SpawnInfoList` | the weighted list of squad templates a pool or biome group may spawn | section 6.1 |
| `AreaSector`, `AreaBiomeGroup` | one cell of a 64 x 64 grid of 4608-unit cells, and the BIOME_GROUP data it belongs to | section 6.2 |
| `BuildingItemGroup` | an ITEM_PLACEMENT_GROUP placed in a building: one item slot with a respawn timer | section 9 |
| `FactionWarMgr`, `CampaignData`/`CampaignInstance` | faction campaigns (FACTION_CAMPAIGN, DIPLOMATIC_ASSAULTS) | out of scope; only listed |

### 3.1 Storage (**Observed**)

| Offset | Type | Meaning |
| ---: | --- | --- |
| +0 | byte | "allied" flag: forces `isAlly` true (set for the faction itself; other setters not found) |
| +2 | byte | at-war flag (set by declare-war, cleared by end-war) |
| +3 | byte | coexistence flag (set from the FACTION `coexistence` list) |
| +4 | float | **relation**, -100 .. +100 |
| +8 | float | accumulated positive trust |
| +0xc | float | accumulated negative trust |

### 3.3 Thresholds (**Verified**: -30 and 50 appear in the predicate functions, in the threshold-message function `FUN_1406b28f0` and in the declare-war gate `FUN_1406b3000`; re-read independently)

| Predicate | Rule | Where |
| --- | --- | --- |
| hostile(A -> B) | B != A and relation(A -> B) <= -30 (default value when no entry) | `FUN_1406b25d0`, `FUN_1406b26d0` (identical bodies); called by `FUN_14092c5e0` (town choice) and `FUN_1409a7e00` (world state) |
| "default-hostile faction" | the faction's own table default (+0x60) is **strictly below** -30 (**Observed**) | inline in `FUN_1404374d0` (target choice, together with fundamental type 0 or 8) and in `FUN_14092c5e0` (combined with `ally()`); this is not the pair relation |
| ally(A -> B) | B == A, or allied flag, or relation >= 50 | `FUN_1406b2630` |
| coexists(A -> B) | B == A or coexistence flag | `FUN_1406b23c0` |
| value as a fraction | relation x 0.01 (and its negative) | `FUN_1406b2460`, `FUN_1406b24c0` |

### 3.4 Changing relations (**Observed**; the event table cross-checked with the dialogue action numbers)

| Slot | Function | Effect |
| ---: | --- | --- |
| 3 | `FUN_1406b3190` | relation -= 35 (if > -100), clamp |
| 4 | `FUN_1406b2ea0` | relation += a x b, clamp to [-100, 100] (direct add; `DA_AFFECT_RELATIONS`, slave trading +/-4) |
| 5 | `FUN_1406b2d20` | event of kind k and amount x, see below; does nothing when the owning faction has `not real` |
| 6 | `FUN_1406b2560` | a > 0: trust += a x b, else negative trust += a x b |
| 7 | `FUN_1406b2f90` | end war: clear war flag; if relation <= -25 set it to -25 (just above hostile) |
| 8 | `FUN_1406b3000` | declare war: only if relation >= -30 (not hostile yet): log "War breaks out between {1} and {2}" if the flag was clear, set the flag, set relation to -35 |
| 9 | `FUN_1406b2530` | global trust += a (`DA_AFFECT_REPUTATION`) |

| Kind | delta | floor | ceiling |
| ---: | --- | ---: | ---: |
| 1 | -0.05 x | -20 | 100 |
| 2 | -2.5 x | -50 | 100 |
| 3 | -10 x | -50 | 100 |
| 4 | -20 x | -100 | 100 |
| 5 | -20 x | -75 | 100 |
| 8 | +0.15 x x dt | -100 | +5 |
| 12 | -10 x | -60 | 100 |
| others | none | | |

### 4. Squad templates

| Field | Meaning in the exe |
| --- | --- |
| `leader` (CHARACTER) | **Only the first entry**, one character (**Observed**, `FUN_140583a10`) |
| `squad` / `squad2` (CHARACTER, v0, v1) | the two member groups, see 5.1 for the counts (`squad2` is the "second half": member role 1) |
| `choosefrom list` + `num random chars` / `num random chars max` | N random characters from the list, weighted by v0 (chance, 0 counts as 100) |
| `animals` / `animals2` (ANIMAL_CHARACTER, v0, v1, v2 = age 0..100) | animal members, 5.1 |
| `slaves`, `prisoners` (SQUAD_TEMPLATE) | whole squads created recursively, 5.2 |
| `housemates` (SQUAD_TEMPLATE, min, max) | further squads spawned with this one (`FUN_14057f820`) |
| `faction` | owner faction; if absent the spawn code asks the owning town's faction, else searches every faction's `squads` / `squad default` for the template (`FUN_1402e7d10`), else falls back to the faction `Wildlife` (`3943-gamedata.base`) (**Observed**, `SpawnInfoList::vfunc_0` = `FUN_1408fe6b0`) |
| `world state` | AND gate for the spawn |
| `roaming military` | forces squad mode 2 ("roaming") even with a home town |
| `dont multiply` | exempts the template from the *Squad size multiplier* setting |
| `AI packages`, `blood smell mult`, `patrol approaches towns`, `force speed`, `personality`, `dialog *`, `nest`, ... | copied onto the platoon / its characters (`FUN_14057f4c0`, `read_AI_packages_n4`; the package system is in [ai.md](ai.md#other-records-that-touch-the-ai)) |
| `building`, `building dislike`, `building designation`, `layout interior/exterior`, `public day/night`, `public beds`, `bed usage cost`, `sell home`, `building ruined`, `initial door state` | how the squad's home building is chosen and set up (resident squads) |
| `vendors`, `is trader`, `vendor money`, `vendors fill total amount`, `vendors refresh time`, `buy mult`, `buys stolen`, `buys illegal`, `special items`, `special map items`, `*artifacts base value`, `regenerates` | shop and contents (section 8 for loot; the trading economy is in [economy.md](economy.md#shops-and-transactions)) |

### Town residents

**Observed** (kenshi-named dump) unless marked; Meitou: `PopulationHomes.cs`.

- **Table** (`FUN_140935d10`, `FUN_140936e60`): the faction's `residents` then the town's; with `residents override` the
  town's only. A template already listed is skipped (first wins). Per entry: v0 = count, v1 = priority (100 counts as 0,
  outside 0..5 an error box). `default resident`: the town's, else (no override) the faction's. `bar squads`: the
  faction's (TownType 2 only, no override) then the town's; with override the town's only.
- **When**: at zone populate (`FUN_140a0e760`) per non-player town, the zone's buildings sorted by handle serial (random-looking).
- **Candidates**: buildings of the town with an `interior` reference, not player-owned, holding no residents template
  or the default one; walls (`links together`, a vfunc override) never, gates (`is wall gate`) only for entries naming
  `building`s. Level data agrees: 1946 walls, none with a resident.
- **Driver** (`FUN_1409370d0`): re-owns the buildings to the town's faction; priority 5 down to 0, each entry with count
  left gets one building per pass, passes repeat while one placed and has more. An entry with no building left makes
  no more squads (Stack: civilians v0 9, 2 made). Then every free building whose interiors take the default resident's
  `layout interior` gets it (`FUN_14092e4b0`).
- **Chooser** (`FUN_14092e870`): a template with `layout interior` needs a building offering it (own layouts or
  `shares interiors with`; gates exempt); without one, an entry naming buildings takes only those. Weight 1, x 0.4 when
  `layout exterior` is not offered, x 10 named in `building`, x 0.05 in `building dislike`; highest wins, first on a tie.
- **Squads** (`FUN_14057f820`): `housemates` (v0, or Rand(v0, v1) when v1 > 0) first, then the squad. Platoon point: a
  random free node of NodeType 0 (SHOPKEEPER), else 2 (GUARD_POST), else 1 (GENERAL), else the building. Characters
  (`FUN_140583a10`): a GENERAL node, else SHOPKEEPER, at node + (0, 10, 0), else the platoon point. The squad's home is the building.
- **Bar squads** (`FUN_1409fcd20`): n = v0 > 2 ? (int)(v1 x 0.005 x v0) : 0, then +1 per Rand(0, 100) <= v1, stopping
  at the first failure, at most v0. Each at a random node of the town's node map (`FUN_14038b000`), else the town
  position; no home. Which nodes that map holds is **Unknown** (Meitou: GENERAL nodes of its bars).
- **New game vs level data**: level-data building states carry `residents template` / `squad template`; save 2 against
  them has the same multiset per town but shuffled across buildings (223 same, 125 moved), so a new game rerolls homes
  (**Observed**). Destroyed buildings stay candidates (the Hub's ruins house squatters).
- **Meitou engine choices**: the level data's assignment is kept where it fits the rules (interiors are drawn from it,
  not relaid to the template's layout); on a tie a building already laid out as the template wants wins; bar squads
  stand in the town's bars; a loaded town keeps its saved squads, homes and bar squads and rolls no new bar squads.

### 9. Settings and constants that matter

| Name | Value | Effect |
| --- | --- | --- |
| `Squad size multiplier` (`settings.cfg`) | 1 | M in 5.1 |
| `Global population multiplier` | 1 | P in 6.2 |
| `npc range` | settings.cfg value | relevance range, see [game-loop.md](game-loop.md) |
| CONSTANTS `max squads` / `max squad size` / `max faction size` | 10 / 20 / 30 | stored by the constants loader (`FUN_14086b2b0`); **the code that enforces them was not found**, but the Squads tab shows "Maximum number of squads reached." ([ui-screens.md](ui-screens.md#flow-and-data)), so the squad cap is at least checked in the UI (**Observed**) |
| area tick | 2 s scaled | 6.2 |
| min distance of a homeless spawn from the player | 1382.4 | 6.2 |

## Unknowns

- What sets relation event kinds 1..12 (call sites of `FactionRelations::vfunc_5`), and what the allied byte (+0) and the
  `trust` floats feed (dialogue conditions, "trustworthy" behaviour).
- What `building designation` changes beyond the bar (it does not enter the chooser); the town node map bar squads use.
- Nest respawn delay after "wiped out", the default of `DAT_142133588` (the Number of nests multiplier option; **Unknown**, the exe image holds zeros there), the rule that makes `TownPopulationManager::vfunc_4`
  declare a town wiped out (**Observed** gates: roaming pool cap <= 3 and <= 25 roaming members).
- Faction campaigns (FACTION_CAMPAIGN), raids and assaults (`raidSizeMult`, `raidFrequencyMult`), the unique squad
  replacement logic, `faction gen` at game start, `NEW_GAME_STARTOFF.faction relations`.
- Who enforces CONSTANTS `max squads`, `max squad size`, `max faction size`.
- Which override town wins among several qualifying candidates; what exactly repopulating a town from its override does.
- Whether the 1250 radius of the proximity helper is squared.
- Patrol routes: the patrol tasks (`Task_Patrol`, `Task_PatrolTown`, `GoOutOnPatrol*`) were not decoded here (class mapping: [ai-tasks.md](ai-tasks.md#tasktype-to-task-class); packages: [ai.md](ai.md#signal-functions-and-the-package-classes)); no fixed route data
  was found in FCS records (squad templates only name `AI packages`; `patrol approaches towns` is a bool).

## Expression reference

- Changing relations: `value += delta`

## Evidence index

Evidence scopes below apply only to their named sources/checks.

- 3.1 Storage (**Observed**)
- 3.2 Initial values (**Verified**)
- 3.3 Thresholds (**Verified**: -30 and 50 appear in the predicate functions, in the threshold-message function `FUN_1406b28f0` and in the declare-war gate `FUN_1406b3000`; re-read independently)
- 3.4 Changing relations (**Observed**; the event table cross-checked with the dialogue action numbers)
- 5.1 Counts and layout (**Observed**; two functions share the count rules, see the sentinel note)
- 5.2 Procedural squads from a FACTION_TEMPLATE (**Observed**)
