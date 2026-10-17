# Save games

A save is a folder of FCS containers plus portrait PNG, not one blob. **Verified:** sample `wade`
(70 files, Kenshi1.0.68) and three later base-game saves round-trip through FcsReader/Writer;
`SaveReadTests.Every_file_of_a_save_reads_and_writes_back_to_the_same_bytes` checks bytes.
Early-sample absence does not establish absence in later/modded saves. Sources: SaveManager,
SaveFileSystem, GameDataContainer and named binary functions in the evidence/table references.

## File format

Usual header: int32 type15, nextId, recordCount, ordinary FCS records; no mod metadata/checksum.
The CAMERA `version` string is game version. Type17 editor-written platoon files also occur;
accept their real header/sizes/flags rather than forcing type15. [FCS layout](fcs-mod.md).

**Verified/Observed in later saves:** preserve invalid UTF-8 bytes with LosslessUtf8 (byte maps
to lone U+DC00+byte), and exact NaN/denormal float bits. TERRAIN_DECALS IDs can be binary int32
in a string slot. Record string IDs reflect quick/platoon/zone origin and `-INGAME-S<hex type>`;
do not infer numeric ID universally from StringId. Handles have C/CS/I/S/type components;
join by handle and instance slot, not row position. Slots can be sparse; char-count can lag.

`SaveGame.ChildCountSize`: byteSize=child count for instanced INVENTORY_STATE/INVENTORY_ITEM_STATE,
instanced ITEM_PLACEMENT_GROUP, quick.save INSTANCE_COLLECTION and zone1-itemlist; zero for
platoon collections, zone0-buildinglist, BIOMES, town/list and decals. Editor-written type17
uses actual byte sizes. Preserve trailers and unknown records/fields/instances losslessly.

## Folder and state mapping

User location `%LOCALAPPDATA%/kenshi/save/<name>` when User-save-location1, otherwise install/save.
`quick.save`: CAMERA/global clock/camera, factions/wars/relations, town state, research, biomes,
decals, platoon index and redirect records. `platoon/<faction>_<counter>.platoon`: squad and
character state, stats/medical/appearance/inventory, equipment and AI. `zone/zone.X.Y.zone`:
buildings/loose items layered over base placements by instance ID. `portraits_texture.png`:
2048² RGBA, 128px cells; exact serial/index selection remains unknown.

**Verified:** later CAMERA zone lists use hexadecimal keys in X-outer grid order; folders can
contain an extra zone not listed. Wars pair with factions by `faction`/gamedata StringId, not
position. Content-file `platoon/` alone means no platoon file, not an empty filename error.
Every GAMESTATE_BUILDING uses its zone container C, CS11111 and a listed slot. Overlay retains
base placements and saved targets for IDs present in both.

**Research** (RESEARCH record in `quick.save`, **Verified**: five sample saves): float `num finished`, strings `finished<k>` (RESEARCH ids in the
order finished; every sample holds only `finished0` = `5359-gamedata.base`), float `num currents` (0). Meitou (`Saving/ResearchSave`) round-trips the
finished list; research in progress is not modelled and its keys are left as they are.

**Player-built buildings** (Meitou `Saving/BuildingSave`): an instance in the zone's `0-buildinglist` with id `<n>-zone<X>.<Y>-INGAME` and a
GAMESTATE_BUILDING `<id>-S23` carrying `name`, `owner faction ID`, `floornum`, `destroyed`, `flags`, `is complete`, `pause`, `construction progress`,
`<i>construction mats`, `world Y pos`, `handle`, `isInsideBuilding` (key set from the original's writer `FUN_1405547f0`, **Verified** by code; no
sample save holds a player-built building, so a real file was not compared). Meitou-only `meitou parent` (instance id) names a parent the handle cannot.

Save runtime state, derive definitions/race/gear/material/terrain from merged base/mod data,
regenerate unsaved foliage/weather particles. Reused declared type IDs do not imply declared
field schema: ITEM, ITEM_PLACEMENT_GROUP, STATS, RESEARCH and BIOMES have saved-state keys.
Record tables below define the relationships and surveyed keys; use [character-stats](../game/character-stats.md),
[economy](../game/economy.md), [AI](../game/ai.md) for field semantics.

## Save/load contract

SaveFileSystem writes through working folders and queues file work; save completion and load
must synchronize with it. Save requests, autosave/quicksave/import and ordered global/platoon/zone
loading are scoped by the binary references. Meitou: `Meitou.Data.Save` folder readers/writers,
`SaveGame.ChildCountSize`, `SaveZone.Overlay`, `Meitou.Simulation.Saving` capture/load mapping.
Capture at a boundary; read/write on workers; replace the active world only after successful load.
[Simulation save coverage](../simulation.md#saves) lists un-restored state.

## Compatibility exceptions

Do not normalize string bytes, NaNs, zero/non-size byteSize, sparse slots or unknown key sets.
Optional character keys include shaved/psts/TI/dat/sentence/sheath/floor; optional platoon keys
include mission/owned/campaign/tags/inventory-refresh-time/slave. Optional town/nest and MEDICAL
limbs keys vary. Test record keys against surveyed saves, retain extension fields, and preserve
editor-authored files. Original acceptance of a save synthesized from scratch is unverified.

### Where saves live

| Setting / path | Meaning |
| --- | --- |
| `%LOCALAPPDATA%\kenshi\save\<name>\` | **Verified** (`save.log` paths, and the sample is there). Used when `settings.cfg` `User save location=1` (default 1) |
| `<install>\save\<name>\` | Legacy location, when the key is 0. **Observed** (`FUN_1403e84f0`): if the key is missing from `settings.cfg` and the install-relative save folder already holds saves (`FUN_14036b680` lists saves; presumably in the install-relative folder, since the setting is not applied yet) the game keeps using the install folder (sets 0); otherwise 1. The UI text: "Save games to user directory instead of the kenshi install path" |
| `settings.cfg` `continue=<name>` | The save the "Continue" button loads; written after every save (kind 1 in `FUN_14047c310`). Sample: `continue=wade` |
| `settings.cfg` `autosaveindex` | Autosave slot counter (see [Autosave](#saveload-contract)); absent in the sample (no autosave happened) |
| `<install>\save.log` | Log of the save file system (**Verified**: the sample's saves are all logged). In the install folder even when saves are in the user folder |

### Folder layout

| Item | Status |
| --- | --- |
| `.save`, `.platoon`, `.zone` all parse with `FcsReader` as file type 15 | **Verified** (70/70 files of the sample, to the exact end of records; `.zone` and `quick.save` have a trailer, below) |
| `portraits_texture.png` | **Verified** 2048 x 2048, 8-bit RGBA (PNG header). **Observed** (`FUN_140414380`): cells of 128 px, 16 x 16 portraits, class `PortraitImage`; written with the save. GAMESTATE_CHARACTER `portrait_serial` / `portrait_index` select the cell (**Unknown** exactly how) |
| Platoon file name | **Verified** (56/56): `<faction name>_<n>.platoon` where `<n>` is a per-faction counter; the PLATOON record's `content file` is `platoon/<platoon stringID>.platoon` |
| Zone file name | **Verified** (13/13 and by `FUN_14036c070`): `zone.<X>.<Y>.zone`, X and Y the zone grid indices ([terrain.md](terrain.md#world-coordinates)) |

### Folder layout — layout/example

```
<name>/
  quick.save                 global state                      (1 file, 2.26 MB in the sample)
  portraits_texture.png      character portrait atlas          (2048 x 2048 RGBA)
  platoon/<faction>_<n>.platoon   one file per squad           (56 files, 6-120 KB)
  zone/zone.<X>.<Y>.zone     one file per loaded zone          (13 files, 2 KB - 1.4 MB)
```

### Save requests (`FUN_14047c310`)

A queued request (kind at +0xA0 of the request object) is run on the next frame; **Observed**:

| Kind | Action |
| --- | --- |
| 1 | `SaveManager::saveGame` (`FUN_140375360`), then write `continue=<name>` to `settings.cfg` |
| 2 | `SaveManager::loadGame` (`FUN_140373f00`) |
| 3 | `SaveManager::importGame` (`FUN_140378a30`, flags in +0xA4): new world, keeps the squad of the chosen save |
| 4 | New game: sets the version string to "1.0.68", reloads the game data and starts the chosen NEW_GAME_STARTOFF |

### Object handles and slot lists

| Part | Meaning | Status |
| --- | --- | --- |
| `TYPE` | `FcsRecordType` of the object's class: 0 BUILDING, 1 CHARACTER, 13 TOWN, 34 PLATOON, 85 NEST; **11 (NULL_ITEM) = the null handle**. The null *object* the reader substitutes is `{type 11, 0, 0, 0, 0}`, but a written null handle may keep stale `CS` and/or `S` values (never `C` or `I`): 620 of the 7687 type-11 handles in the sample have a non-zero other part (always `CS` and/or `S`) (e.g. `TI town`, `residents`, `slaver`, `ownedby`) | **Verified**: object C++ class `hand` (RTTI); data at `141e3a600` holds ints 11,0,0,0,0 (read from the exe image); `hand::vfunc_0` (`140031b88`) is a validity test on `type != 11` only (decompile); the reader `FUN_1406bd000` returns that object when `<prefix>I` is absent. Value census over all 70 files: types 0, 1, 11, 13, 34, 85 only |
| `C` | the **container** id the object lives in | **Verified** for buildings: `handleC = Y * 64 + X + 1` for the zone file the building is in (1496 of 1496 GAMESTATE_BUILDING in 13 zone files); for characters `C` is the container id of the platoon: all characters of a `.platoon` file share the PLATOON record's `handleC` and `handleCS` (56 of 56) |
| `CS` | the container's **serial** | **Verified**: 11111 for all zone containers (1496 of 1496); for platoons a random 32-bit number shared by the platoon's characters |
| `I` | slot index of the object in its container | **Verified** within a zone: building `handleI` unique per file (0 duplicates in 1496) and always in that zone's slot list. Platoon members count 1, 2, 3... (**Verified**: the `I` values of the characters of each of the 56 files are exactly 1..n); the platoon's own handle has `I = 0`, `S = 0` (**Verified**, 56 of 56) |
| `S` | the object's random **serial** | **Observed** (`FUN_1400d1c20`, catalogued earlier as `Handle_NewRandomSerial`): set at allocation to `int(rand01 * 4e9)`, hence the negative values seen; used to detect a stale handle |

### CAMERA (type 56, id 1051): the world record

| Fields | Meaning |
| --- | --- |
| `version` (string) | "1.0.68", the game version that wrote the save (**Verified** equals `currentVersion.txt`); compared by `FUN_14047acf0` |
| `time day`, `time hour`, `time minute` (ints) | **Observed**: in-game clock (sample: day 1, 13:39); how the clock advances and wraps is in [game-loop.md](../game/game-loop.md#frame-clocks) (**Verified** there). Written by `FUN_14066d7d0` |
| `sky update current time`, `sky update total time`, `sky updating`, `sky ambient color mult`, `sky ambient color mult speed`, `sky clouds density`, `sky clouds density speed` | state of the camera's 30 s sky transition ([sky.md](sky.md)); **Observed**. The regions' weather is in [BIOMES](#biomes-type-28-quicksave-weather-per-region) |
| `player money` (int), `squads` (1), `members` (1), `floor num`, `formation` | player squad summary (`FUN_140375360` reads them from the player object) |
| `pfaction name` ("Nameless") | the player's faction name; **Verified** it equals the name of the GAMESTATE_FACTION whose `gamedata stringID` is `204-gamedata.base` (record 670) |
| `pos` (vec3), `rot` (vec4 x y z w), `zoom`, `alt` | camera pivot position, rotation and zoom ([camera.md](camera.md)); **Observed** |
| `selected_gui_object*`, `selected_character*`, `selected_characters<k>*` (handles) | current selection; the sample selects the player's character (handle C 1, I 1, TYPE 1) |
| `tracking*` (handle) | the character the camera follows (null in the sample) |
| `area` (string), `biome_text_groud` (BIOME_GROUP id), `biome_text_timer` (float) | the region name banner state; **Verified** `biome_text_groud` resolves to a BIOME_GROUP, `area` is "Okran's Gulf" |
| `usedUniques<k>` (CHARACTER ids), `num usedUniques`, `usedUniques<k>handle*`, `usedUniquesState<k>`, `usedUniquesPlayer<k>` | unique (story) characters already spawned; **Verified** the 3 ids resolve to CHARACTER records |
| bools `bl ditch eject ep feed heal help rescue share shootfirst sit sleep stay`, floats `bs cod gdm ht nnm ps rs`, `formation` | squad behaviour toggles and short-key game toggles (`FUN_1403eb710`, `FUN_1403ee910`): **Observed**; meaning of the short keys **Unknown** |
| reference list `zones` | one entry per zone **that has a file**, written in grid order (X outer, Y inner; `FUN_140375360` loops 64 x 64): target = the list index in **hexadecimal** ("0".."9", "a", "b", "c" for 13 entries), values `(x, y, 1)` = (zone X, zone Y, has-file flag). **Verified**: the 13 entries equal the 13 files of `zone/` (probe), and the key spelling is seen in the sample |
| reference list `mods` | one entry per loaded data file, target = the file's name without extension for the `.mod` files (`Newwworld`, `Dialogue`, `rebirth`) and `base` for `gamedata.base`, values `(-1, 0, 0)`; **Verified** (probe, `GameDatabase.Files`) that the four entries are in the load order of the base game (`gamedata.base`, `Newwworld.mod`, `Dialogue.mod`, `rebirth.mod`). **Unknown** whether `loadGame` compares it with the current mod list (no such string seen) |

### BIOMES (type 28, `quick.save`): weather per region

Written per region by `FUN_1408fad60` (region list walk `FUN_1408faf40`), read by `FUN_1408fae70` (walk `FUN_1408fafc0`); the weather part by
`FUN_1409ddfe0` / `FUN_1409de200`, the weather instance by `FUN_1409dd250` / `FUN_1409dd5e0`. **Verified** by code and by a census of the owner's six
saves (71 regions each, mods included):

| Key (`p` = prefix) | Meaning |
| --- | --- |
| string `<BIOME_GROUP id>` = `p` | the region's prefix: its index in the save's walk of the region table. The loader finds a region by its id and reads `p`'s keys; a region the record lacks keeps a fresh schedule. So `p` only pairs keys within one save |
| int `p_nestcount` | the BIOME_GROUP's `num nests` (equal in 71 of 71 regions, all saves) |
| string `p_w_s_id`, int `p_w_s_end` | current SEASON id and the day it ends (absolute day count). An id the region does not list: season 0 from now, a new weather |
| string `p_w_w_id` | current WEATHER id; one the season does not list: a new weather is chosen |
| int `p_w_wi_start`, `p_w_wi_end` | weather start / end in game minutes, `day × 1440 + hour × 60 + minute` (start ≤ now < end in 426 of 426) |
| float `p_w_wi_str`; int `p_w_wi_ef_str` | strength; the WEATHER's `effect strength min`, truncated to int (426 of 426) |
| float `p_w_wi_speed`, vec3 `p_w_wi_dir` | current wind speed and direction; direction = `(cos a, 0, −sin a)` of the angle below (rotation about Y of +X; every settled region) |
| int `p_w_wi_udp` | game minute of the next wind update |
| int `p_w_wi_bu_t_s` / `_t_e`, float `_bu_s_s` / `_s_e`, `_bu_a_s` / `_a_e` | the last wind change: start/end minute, speed and angle (radians) at each end, lerped linearly. On load one not running at the current minute is marked done and its times zeroed |
| float `p_w_wi_time` | game-speed seconds the weather has run (dt × speed, `FUN_1409dc0e0`); **Observed** = (now − start) × 20/11 in 57 of 71 regions of the later saves, larger for a new game's first weathers |
| float `p_w_fog_a` | written for the camera's region only (one per save, 0 in every sample): a fog value read from and restored into the fog manager; meaning **Unknown** |
| `p_<nest TOWN id>_count` / `_start` (int), `_respawn` (float) | the region's nests (`FUN_140103700`, `FUN_140104290`); **Unknown** |

Meitou (`Data/Save/SaveWeather`, the host's `WorldWeather.Restore`): reads every region's season, weather, strength and wind into `WeatherRegionSnapshot`
(heading = −angle; the fog fade is not saved, so a restored weather starts faded in) and writes them back at save time, with `_w_wi_time` as
(now − start) × 20/11 and a new region's `_nestcount` from its BIOME_GROUP. Nest keys, `_w_fog_a` and the `E<n>` effect instances are carried as read.
The CAMERA's sky transition is written as finished (`sky updating` false, both times 30, the current sky multiplier and cloud density); the loader snaps the sky.

### Saved, derived and regenerated

| Content | Where | Status |
| --- | --- | --- |
| Game time, clock, sky transition, weather and season per region | `quick.save` CAMERA, BIOMES | **Verified** present |
| Factions: relations, trust, prosperity, war campaigns | `quick.save` | **Verified** (present in the sample) |
| Town state (discovery, population, trade goods, artifacts, nests) | `quick.save` | **Verified** (present in the sample); town placements, names and shop lists come from `leveldata.level` |
| Squads and all character state (stats, wounds, inventory, appearance, AI) | `.platoon` | **Verified** (present in the sample) |
| Buildings' state, generated interiors, loose items, item placement groups | `.zone` | **Verified** for visited zones |
| Locks and doors (`lock lvl`, `locked`, `state<i>`, `wants lock<i>`, ...), cage/bed occupancy (`in something`, `in what`) | `.zone`, `.platoon` | **Verified** keys: [locks](../game/locks.md#save-keys) |
| Research, unique characters, selection, camera | `quick.save` | **Verified** (present in the sample) |
| Terrain, heightmap, foliage, grass, roads, base building placements, map textures, building and character definitions | data files | **Not in the save** (**Verified**: no such record types; foliage and terrain are rebuilt from the world data, see [foliage.md](foliage.md)) |
| Zones never loaded | nothing | **Observed**: absent from the `zones` list; the game loads them from the data files and generates their interiors and populations when first visited (`FUN_1409ffad0`) |
| Population of unvisited areas | nothing | **Observed**: 56 platoons for the start area only |

### Character appearance (CHARACTER_APPEARANCE in `.platoon`)

**Verified** (dump of save 2's `Nameless_0.platoon`): the same key set as a `.bod2` body file ([characters](../characters.md#body-files-bod2)):
bools `sex female`, `in editor`; ints `Age`, `body version` (2); floats: every body/face slider of the race's editor limits (`Height`, `Waist`...), the template's
other sliders at 100 (`Brow height`, `Cheekbone`... not in the human XML), face pose weights by pose name (`wide_nose` 0.02, `close_eyes` -0.64...), `Hair Colour`,
`Hair Saturation`, `Hair Brightness` (HSB x 100), `Posture`, a float `Age` besides the int; vec3 `Skin Tone`; strings `head` (HEAD), `hair style`, `beard`
(ATTACHMENTs, mod records allowed), `idle stance`; reference `race`. Meitou (`Saving/SavedKit`) draws every loaded character (player and NPC) from it: the record
is the look's appearance, worn items its clothes; a character with no saved items keeps clothes rolled from its serial but the saved body and face. A record
with only race and flags (no head, no floats) is not a look and the look is rolled. The record is written back only for a new character (the generated look) or
one changed in the character editor (its keys replaced by the edited copy's, so the key set and order stay the original's).

### NPC platoons (Meitou mapping)

Every saved NPC platoon with a `basetown` naming a placed town is restored (a characterless town-bound one is skipped); town-bound ones (template does not travel) are pinned to their town and replace
that many of its resident squads of the same template; a town holding any rolls no bar squads. Wild and nest platoons are kept in the file untouched. On save, a restored platoon is rewritten
only once loaded (its characters from the world), otherwise only its position/package; a squad the population made is written as a new PLATOON with the
`SaveBuilder` key set plus `basetown` (town id), `is resident` (false for a travelling template) and `currentPackage`. **Observed** (headless load of saves 1, 2,
wade): NPCs within 5 units of their saved positions (save 2: 4 of 107 differ in Y only, flat test ground); a Meitou save → load restores them
exactly with no fresh squads for save 2 and a new game. A pinned platoon is homed in the building its `homebuilding` handle names (state `<id>-S23`
of zone C-1, X = c % 64, Y = c / 64; ids repeat across zones); that building is taken out of the town's free homes. **Unknown:** how the original reuses the saved package.

### Locks, cages and sentences

Key types **Verified** by a census of the owner's five saves (every platoon and zone file): building `lock lvl` int, `locked` / `broken` bool, `hardness`
float; door i `state<i>` int (0/1), `doa<i>` float, `broken<i>` / `locked<i>` / `wants lock<i>` bool, `gatecode<i>` int (-1..6), `lock lvl<i>` int,
`hardness<i>` float; character `in something` int (0 8135, 1 1159, 2 218), `in what` handle (type 0 BUILDING when set, else 11; prisoners' point at
generated interior buildings, e.g. `19765-zone39.16-INGAME-S23`), `sentence` int and `psts` float (0 in every sample); platoon `imprisoned` bool.
Meanings: [locks](../game/locks.md#save-keys), [economy 11.5](../game/economy.md#115-bail-and-sentence) (`sentence` = start in game hours, `psts` = length).

Meitou (`Saving/HeldSave`): reads building keys from the zones' existing GAMESTATE_BUILDING records when the town registers and writes them back
into those records only (none for a new save, which has no zone files); the save ids of generated interiors are not mapped, so interior cages and
beds keep rolled state. `in what` resolves only to a placed building; Meitou-only keys `meitou in something` (int) / `meitou in what` (fixture id)
carry the rest and win on load. Unchanged held state keeps the original keys as read; a changed one without a building handle writes `in something` 0
and the null handle. `sentence`/`psts` are written while a sentence is set or the save had them; Meitou's start equals `World.GameHours` (day x 24 + hour).

Meitou-only crime keys (`Saving/CrimeSave`, `LockSystem`, `FixtureRegistry`; the original's keys for these facts are **Unknown**), each written only while set and removed otherwise:
- GAMESTATE_CHARACTER `meitou escaped` (bool, escaped-prisoner flag), `meitou recently wanted` (float, game seconds left), `meitou recognition` (float 0..1, bounty recognition meter).
- GAMESTATE_BUILDING `meitou house locked` (bool, a lock-up round locked this home and nothing unlocked it since).
- GAMESTATE_BUILDING `meitou lock <layout index>` (int level x 4 + broken x 2 + locked) for each interior cage or container whose lock is locked or broken (their ids are not mapped, see above).

### Player outposts

Towns made in play are saved in `quick.save` only, as entries of its one GAMESTATE_TOWN_INSTANCE_LIST (type 38, name "0",
id `<n>-quick.save-INGAME`), written from the town manager's player-town list (+0x68) and nest list (+0x80)
(`FUN_14092a6c0`). The level data's towns are not in it: their GAMESTATE_TOWN states are written separately
(`FUN_14036b710`). **Observed**: the owner's five saves' lists hold only nests (127 and 150 entries), each with a
`Nest state <name>` state (type-85 handle, no `instance` key); no save has an outpost.

An outpost's entry (**Verified** by code, `Town::vfunc_17` `14036eb20` with `FUN_140386d20`): id = the town's stored
instance id, or a new `<n>-quick.save-INGAME` (kept on the town, so later saves reuse it); target = the town's TOWN
record (`player_town_template`); position = the town's, absolute; quaternion (0, 0, 0, 0); state `<id>-S5e`. Its
GAMESTATE_TOWN (`Town::vfunc_85` `140376b80` and the TownBase writer) is named `Town state <name>` and has the base
towns' keys: `instance` = the entry id, `is nest` false, `building material`, `public` (false for an outpost), `plevel`,
`replacementTown`, `trade goods`, `faction` = the player faction, `factions here<i>`, `discovered` / `explored` /
`recently discovered`, `artifacts count`, the handle `hand*` (type 13 TOWN) and the population floats; plus int
`override` (Town+0x51c, 0 when made, meaning **Unknown**), written only for a town whose faction is the player's.
Buildings name it by their `town` handle. Whether the loader takes the name from the record name is **Unknown**.

### Cross-checks against base data (the sample vs `GameDatabase` of the four base files)

| Check | Result |
| --- | --- |
| Every GAMESTATE_FACTION `gamedata stringID` is a FACTION | 103 of 103 base FACTION records have a GAMESTATE_FACTION (`gamedata stringID`); `relationSID` 10,506 of 10,506 resolve |
| `GAMESTATE_BUILDING.owner faction ID` | 1496 of 1496 resolve to FACTION (one value is `nofac`, which is itself a FACTION record id), plus 25 suffixed `owner faction ID<k>` keys, all resolving |
| INSTANCE_COLLECTION targets in zones | BUILDING 1496, ITEM_PLACEMENT_GROUP 526 (the 1496 equals the GAMESTATE_BUILDING count) |
| `INVENTORY_ITEM_STATE.base data sid` | all 2181 resolve: ARMOUR 891, ITEM 1018, WEAPON 230, CROSSBOW 14, CONTAINER 26, MAP_ITEM 2 |
| `MEDICAL_STATE.sid<k>` | 1610 of 1610 resolve to LOCATIONAL_DAMAGE |
| `TOWN` states | 304 of 304 instances match base town placements; `trade goods` are 75 ITEM records |
| Strings that do **not** resolve | `company sid` / `material sid` values with a `.TECH.<n>` suffix (17), `GAMESTATE_TOWN.instance` (instance ids, not records), BIOMES `E<n>` instance targets (numbers) |

### Functions

| Address | Role |
| --- | --- |
| `FUN_140375360` | `SaveManager::saveGame` |
| `FUN_140373f00` | `SaveManager::loadGame` |
| `FUN_140378a30` | `SaveManager::importGame` |
| `FUN_14047c310` | per-frame request dispatcher (save/load/import/new game) |
| `FUN_14047be70` | autosave timer and slot rotation |
| `FUN_14047acf0` | "major.minor.patch" -> `major<<24 | minor<<12 | patch` |
| `FUN_140471de0`, `FUN_1404711e0`, `FUN_140470fc0`, `FUN_140471d10`, `FUN_140473c30`, `FUN_140473720`, `FUN_140472ac0` | `SaveFileSystem` (construct, temp folder, read path, write path, scan, commit, copy) |
| `FUN_1406bd570` | `GameDataContainer::save` (writes type 15 files); load is `FUN_1406c0b50` ([fcs-mod.md](fcs-mod.md#game-loader)) |
| `FUN_1406bd000`, `FUN_1406bca80`, `FUN_1400d1c20`, `140031b88` | handle read, handle write, handle allocation, `hand::vfunc_0` (type != 11 test) |
| `FUN_14036dde0`, `FUN_14036c070`, `FUN_14036e450`, `FUN_1409ffad0`, `FUN_140a0bef0` | zone save, zone file write, save all zones, zone load, zone-file check |
| `FUN_140372a60` / `FUN_14036f2e0`, `FUN_1409c6890` / `FUN_1409c9dc0` | faction write / read, war state write / read |
| `FUN_140372ef0` / `FUN_1407ed0f0`, `FUN_1406280a0` / `FUN_140626f50` | platoon write / read, character write / read |
| `FUN_14092a6c0`, `FUN_14036b710`, `FUN_14092ae80` | town list write ([outposts](#player-outposts)), level town states write, player-town check on load |
| `FUN_140552320` / `FUN_140552b70`, `FUN_140555650` | building state write / read ([keys](zones.md#placement-keys)); `FUN_14057cc70` (catalogued as `Zones_CreateDoor`) is the building factory |

## Unknowns

- What `byteSize` encodes beyond "child count for some record classes", and whether the game ever reads it (it does not at load).
- The two long slot lists (2503 and 2969 ints) at the end of `quick.save`; the `S` serial's use when a handle goes stale (what happens on a mismatch).
- The roles of BIOMES `E<n>` instances (EffectHandler state fields) and nest keys, `_w_fog_a`, the short-key CAMERA floats and bools (`bs cod gdm ht nnm ps rs`, `bl ep`), `TI day/dat0`, `sqt`, `towntime`, `jobhr`, `ssct`, and whether MEDICAL `fed` / `KO` are the stomach buffer and the KO timer (the other MEDICAL floats are explained above).
- Whether `loadGame` rejects or adapts to a `mods` list that differs from the current load order (nothing in the sample: single base-game save).
- Prisoners, dead characters, bounties, trade and mission state, and player outposts: not present in an early-game sample. Player-built building and outpost keys are known from the code only ([zones](zones.md#placement-keys), [outposts](#player-outposts)).
- Exactly when platoon files are written to the working folder between saves (only at save time is certain) and which zones are written on unload ("Delaying saving zone").
- The condition list of the autosave pause, and how `Import Game` selects squads (`FUN_140378a30`, `FUN_140377710`).
- How relations, trust and `trustNeg` change during play (only the fresh-world values are explained), and what the player faction (no relation table, but a `known` list and `global trust`) uses instead.
- Why 6 building placements of the sample have a different target in the save than in the base list (in zone 19.29 two placements have swapped targets, same positions: a random variant pick is a guess).
- Saves made by other game versions or with mods: layout of `mods` and unknown record types would show.

### Implementation (as built, stage 10)

| Type | Role |
| --- | --- |
| `SaveFile` | One file: an FCS file (any type, strings lossless) and its slot lists; writes back the same bytes |
| `Hand` | `{Type, C, CS, I, S}` by key prefix; `Null` is type 11 and `IsNull` tests the type only; `Prefixes` finds the handles of a record |
| `SaveFolder` | The layered lookup (a later layer hides an earlier one), names with `/`, the `platoon/` and `zone/` listing, zone file names |
| `SaveGame` | `Load(folder)`: `quick.save`, the platoon files named by the PLATOON records, every zone file, the portrait atlas; typed views (`SaveCamera`, `SaveFaction` with its war record and relation table, `SaveTown`, `SavePlatoon` with its `SaveCharacter`s and their six state records, `SaveZone` with `Overlay`), the zone-file check as `Problems`, `BuildHandles()` (`SaveHandles`: platoon, character, town and building lookups), `Clone()`, `UpdateDerived()` and `Write(folder)` |
| `SaveBuilder` | Makes the records of a new save (CAMERA, factions with war state, towns, platoons, characters with their six states) with the key sets above, the id patterns and seeded random serials; can also add to an existing save; `RemoveCharacter` |
| `SavePortraits` | The blank 2048 x 2048 atlas of a new save |

## Evidence index

Evidence scopes below apply only to their named sources/checks.

- Save requests (`FUN_14047c310`)
- What `saveGame` writes (`FUN_140375360`)
- `loadGame` (`FUN_140373f00`)
