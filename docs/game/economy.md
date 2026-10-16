# Economy

Kenshi1.0.68 research from ItemValue/trade/shops/town/crime binary functions, schema and merged
records. Tables retain constants, price classes and surveyed fields. `Meitou.Data.Gameplay.Economy`
and Simulation Trading/Crime implement researched rules; [carry-loot](carry-loot.md) owns theft.
No skill/reputation/time-of-day price factor was observed in the decoded trader-factor path.

## Item value

**Verified** arithmetic: gear truncates after each multiplication; plain/ingredient prices round
floor(x+0.5). Quality fraction s=quality/100. Engine lerp arguments are (t,a,b).
Weapon: truncate value*manufacturer-price-mod, then truncate lerp(logistic,15,160)*value,
logistic=1/(1+exp(-4*(2s-1))); apply combined sword/global factor with truncation.
Armour: quality>0 and coverage uses s² interpolation of classLo*matLo to classHi*matHi;
otherwise own value. Apply armour/clothing category, relative-price and slot factors separately:
hat3=0.3, legs6=0.5, shirt8=1.5, boots9=0.2, others1. Quality0 uses own value, not low endpoint.
Crossbow107/limb111: s² between value/value1, category multipliers are swapped (robotics for107,
crossbow for111). Container46 uses own integer value without multipliers.

Ingredients recursively price same quality with category flag1; sum rounded
ingredientValue*(v0/100)*(1+profitability). **Observed** v0 interpretation; other steps verified
in disassembly. Plain artifact skips multiplier; food-crop uses food/global, other items use
trade/global, including non-trade items. Gear stores value at construction; generic value can
recompute. Runtime tables below retain class-specific sale/player-crafted/armour reductions.

## Units, stacks and traders

**Verified** sell1 means player selling to shop; sell0 buying. Generic unit: base value, town
market factor with truncation, loot fraction on sale unless unstolen trade item, charge fraction
when count1, finally trader factor. Stack total: p*n-trunc(p-p*charges/maxCharges), except count1
uses unit directly. The trade-window's split of exchanged-stock units remains **Observed/Unknown**.

**Observed** trader T=buyMult*culturePrice, halved for the decoded stolen-to-player-faction case.
**Verified** buyMult comes from trader squad template and applies both directions. Town culture
wins over faction, default1; duplicate culture entries last-win. Trade-price percentage=v0/100,
zero means100%. Culture also defines illegal/forbidden goods and buildings.
**Observed** town market factor starts uniform[1-m,1+m), m0.3, then clamps against global-town
factors/default1 within distance/1e6*m*10. Actual setup-list membership/order remains unknown;
save factors span0.7..1.3. Only generic items use market factors; save v0=trunc(factor*10000),
load /10000, accepting legacy goods-key layout (**Verified**).

## Shops and transactions

Stock derives vendor/item lists and template count/money/restock fields, with culture-forbidden
items excluded. Squad and building shops have distinct setup paths. Keep stock/purse/refresh
state and inventories separate from displayed trade UI. Buy/sell checks availability, quantity,
charges, both parties' funds and inventory fit. Stolen goods require fence/catch logic; selling
success alone does not prove legal transfer. Restock, blueprints/research money, artifact and
money-item branches are scoped in the expressions/field tables and undecoded questions.

## Crimes, bounties and prisoners

Ledgers are per wanted faction; crimes pending at deed time and witness booking are distinct.
CONSTANTS categories define theft/assault/murder/looting values. Expiry is0.04 hours/cat and
permanent from10000; capture, claiming, bail and prison sentences use separate paths.
Witness/innocence/squad-level kinds and killing-to-murder details remain partly unknown.
Slavery/prisoner/animal transactions and loot generation are not equivalent to ordinary shop
trades. Consult [factions](factions-squads-towns.md), [carry-loot](carry-loot.md) and the retained
unknowns before implementing unsupported branches.

### 3. CONSTANTS price block

The loader `FUN_14086b2b0` reads the GLOBAL CONSTANTS record into the block at `142133dd0` (field address =
`142133dd0 + 4 * index`; the same loader as in [character-stats.md](character-stats.md#constants-used-by-this-subsystem)).
The consumer column is where the value is read. **Verified** for each row: the loader's read and a consumer found through the
data cross-references, and the base values from a probe over CONSTANTS; "[Editor]" is the `fcs.def` default.

| Index / address | FCS field | Base | [Editor] | Stored as | Consumer |
| --- | --- | --- | --- | --- | --- |
| 0x37 `142133eac` | `global price mult` | 1.0 | [1.0] | as is | only the seven rows marked "x global" below |
| 0x34 `142133ea0` | `armor price mult` | 0.6 | [1.0] | x global | `ItemValue` (armour with part coverage) |
| 0x38 `142133eb0` | `clothing price mult` | 1.0 | [1.0] | x global | `ItemValue` (armour without part coverage) |
| 0x35 `142133ea4` | `sword price mult` | 0.4 | [1.0] | x global | `ItemValue` (WEAPON) |
| 0x36 `142133ea8` | `trade price mult` | 0.6 | [1.0] | x global | `ItemValue` (plain items that are not `food crop`) |
| 0x28 `142133e70` | `food price mult` | 0.5 | [1.0] | x global | `ItemValue` (plain items with `food crop`) |
| 0x33 `142133e9c` | `robotics price mult` | 1.0 | [1.0] | x global | `ItemValue` (**CROSSBOW** records, see the swap below) |
| 0x32 `142133e98` | `crossbow price mult` | 1.0 | [1.0] | x global | `ItemValue` (**LIMB_REPLACEMENT** records) |
| 0x39 `142133eb4` | `trade profit margins` | 0.3 | [0.5] | as is | `Zones_TownSetup` (market factor spread, section 7) |
| 0x3a `142133eb8` | `loot price mult GEAR` | 0.25 | [0.15] | as is | sell fraction of weapons, armour, blueprints (slot 81) |
| 0x3b `142133ebc` | `loot price mult` | 0.5 | [0.4] | as is | sell fraction of maps, items, limbs (slot 81) |
| 0x3c `142133ec0` | `loot price mult player armour` | 0.25 | [1.0] | as is | sell fraction of armour the player crafted (`Armour::vfunc_81`) |
| 0x3d `142133ec4` | `loot price mult player weapons` | 0.25 | [0.5] | as is | same for weapons (`Weapon::vfunc_81`) |
| 0x3e `142133ec8` | `blueprint price mult` | 1.5 | [1.5] | as is | research cost (section 10) |
| 0x3f `142133ecc` | (none: the loader writes the constant 0.3 itself) | 0.3 | n/a | hard-coded | `Armour::vfunc_81` (see section 5) |

### 4.2 ARMOUR and clothing (type 3)

The class and material tables are the same two tables [combat.md](combat.md#armour-and-medical-hit) uses for defence, built by the
constructor `FUN_1408a8f90`; the price uses the **first value column** of each entry (offset +4), combat uses the later
columns. **Verified** (constructor stores and `ItemValue` loads read from the same four hash tables `142134ce0`,
`142134d20` (class, `ArmourClass`) and `142134d60`, `142134da0` (material, `ArmourType`)):

| `class` | name | `classLo` (quality 0) | `classHi` (quality 100) |
| --- | --- | --- | --- |
| 0 | GEAR_CLOTH | 100 | 5000 |
| 1 | LIGHT | 400 | 30000 |
| 2 | MEDIUM | 800 | 35000 |
| 3 | HEAVY | 400 | 40000 |

| `material type` | name | `matLo` | `matHi` |
| --- | --- | --- | --- |
| 0 | CLOTH | 0.2 | 0.8 |
| 1 | LEATHER | 0.5 | 1.0 |
| 2 | CHAIN | 1.0 | 1.5 |
| 3 | METAL_PLATE | 1.5 | 1.5 |

### 5. Runtime unit price: Item virtual slots 81 and 82

Every inventory item class has slot 81 ("value of one unit, with a sell flag") and slot 82 ("value of the whole stack").
RTTI gives the implementations (`rtti.tsv`; the slots 81 and 82 are at `+0x288` and `+0x290` of the vtable); **Verified** by reading each (also the slot 82 `FUN_1407915b0`, which calls slot 81 through `+0x288`). The byte `sell` is **1 when the item is being
sold to a shop** and 0 when it is being bought from one (**Verified** in the trade window `FUN_140715560`: the error
code is `(sell != 0) + 3`, i.e. 3 "You can't afford that." for a purchase and 4 "The shopkeeper can't afford that." for a
sale, and the same flag selects which price is computed).

| Class | Slot 81 (`sell` = 0) | With `sell` = 1 |
| --- | --- | --- |
| `Weapon::vfunc_81` `FUN_1408a52f0` | `trunc( T * stored )` | `trunc( T * stored * f )`, `f` = `loot price mult player weapons` if `Weapon+0x208` is set, else `loot price mult GEAR` |
| `Armour::vfunc_81` `FUN_1408a9c20` | `stored`, then if `Armour+0x1e8` is set `* 0.3` (the hard-coded 0.3 of section 3), then `trunc( T * . )` | the `stored` first gets `loot price mult player armour` if `Armour+0x208` is set, else `loot price mult GEAR`, and then the same 0.3 and `T` steps |
| `BlueprintItem::vfunc_81` `FUN_1402d30f0` | `trunc( T * value )` (`value` = the research `money` stored at `+0x1e8`, section 10) | `trunc( T * value * loot price mult GEAR )` |
| `MapItem::vfunc_81` `FUN_14075d950` | `trunc( T * value )` (the int at `MapItem+0x1e8`; where it is set was not traced) | `* loot price mult` |
| `MoneyItem::vfunc_81` `FUN_14001352a` (thunk to `FUN_14075f660`) | the record `value`, nothing else | same |
| everything else (`Item`, `InventoryItemBase`, `Gear`, `NestItem`, `ContainerItem`, `SeveredLimbItem`, `RobotLimbItem`) `FUN_1407a8f90` | see below | see below |

### 9.1 SQUAD_TEMPLATE fields (data = **Verified** by probe over 959 templates)

| Field | Meaning | Data |
| --- | --- | --- |
| `is trader` | the squad is a trading squad | needs a vendor list |
| `vendors` | list of VENDOR_LIST (val0 = weight of the list) | |
| `vendors fill total amount` | how many items the vendor holds (default 15 in the editor; 0 or below means 100 in the code) | 0 x279, 15 x397, 10 x14, 100 x19, 20-40 common, 1000 x1 |
| `vendor money` | the building-shop vendor's purse (building fillers only, see 9.2); 0 = derive from the stock | 0 x954, 1000 x4, 250 x1 (the five non-zero templates all come from mods of the probe load order, none from `gamedata.base`) |
| `vendors refresh time` | game hours between stock refreshes (<= 0 means 24000) | 24 x874, 12 x15, 48 x13, 72 x13, 70 x10, 99 x10, some 99999 |
| `buy mult`, `buys stolen`, `buys illegal` | section 6 and 8 | |
| `special items`, `special map items` | extra guaranteed stock (`read_special_items_n3` `FUN_140958740`) | not decoded |

### 11.1 Ledger

Per-character hash map keyed by faction (`Character+0xf0`). Entry (`FUN_1405e7ee0`): `+8` amount int, `+0xc` crime bitmask
(`1 << code`), `+0x10` claimed byte, `+0x18` double game-hour timestamp of the last raise (**Verified**: crime add, claim marker
`FUN_140854170`, save loader `FUN_140625e60` / writer `FUN_1406262b0` agree). Save keys: [save.md](../formats/save.md).

### 11.2 How a bounty is gained

- **Crime**: `FUN_140853ec0(crimeEvent, faction)`: if the crime code is nonzero and has not been processed yet (`+0xa4` flag), and the
  character's **`NPC class`** (`+0x1a0 -> +0xdc`, the same read as the police test, carry-loot.md 4.3; corrected 2026-10-13, this was called a "state") is not 8 (BANDIT),
  5 (DIPLOMAT) or 1 (LAW_ENFORCEMENT), then the crime's amount is **added** to a ledger entry chosen by `FUN_140852010` from the given faction: a caged character's prison
  owner's, else the given faction's parent (`+0x38`, which field sets it is **Unknown**) when it has one, else the given faction itself when its `+0x40` flag is set (else none).
  The given faction is the **booking police witness's** (or the police in the hit rule of 11.6), not the victim's (**Observed**: both callers pass their own faction); the crime bit is set, the timestamp is reset to now, and the "wanted" UI is refreshed. The very first crime
  code (1, Enslaving) sets the flag but adds nothing (**Observed**; the code returns before the add). The same function also sets a float (250.0) in a nearby object of the character at `+0x110` (purpose not traced).
- **Crime amounts** (`FUN_140851e40`; **Verified**: a switch with no other inputs; names from the game's own name table `FUN_140856530`; the editor's enum is not in `fcs_enums.def`, so a match with it was not checked):

| Code | Crime | Amount |
| --- | --- | --- |
| 1 | Enslaving | 100 (but see above: nothing is added) |
| 0xb (11) | Trespassing | 100 |
| 2 | Burglary | 1000 |
| 0xd (13) | Fencing | 1000 |
| 3 | Theft | 2500 |
| 0xc (12) | Escaping Prison | 2500 |
| 0x10 (16) | Uniform Theft | 2500 |
| 4 | Murder | 5000 |
| 7 | Terrorism | 5000 |
| 8 | Smuggling | 5000 |
| 9 | "Terrorism" (a second code with the same label) | 5000 |
| 0xf (15) | Kidnapping | 5000 |
| 6 | Treason | 50000 |
| 5, 10 (Looting), 0xe (14, Crop Theft), others | Assault, Looting, Crop Theft, default | 500 |

- **Spawn bounties** (`FUN_14062b210`, **Observed**): CHARACTER `bounty chance` vs an inclusive roll 0..100 (`roll < chance`);
  amount `trunc(bounty amount + 0.5*k)`, `k` an integer in `[0, 2*bounty amount fuzz]` (the fuzz only adds), for each
  `bounty factions` entry (else `defaultEmpireFactionSID`), added without a crime bit (`FUN_140853e20`).

### 11.3 Lifetime

`FUN_140851f10`: `0.04 * amount` hours below 10000, else never ("Notorious"). The character tick `FUN_1405cbb80` removes at most
one expired entry per tick and is skipped while `Character+0x2f8 == 2` (imprisoned); each crime resets the timestamp (**Observed**).

### 11.4 Claiming

`FUN_140330d50` (**Observed**): a player-faction captor hands a non-player captive to a police chief (designation 8, not of the
player's faction, not state 7); if the captive's entry for the chief's faction has an amount and is unclaimed, the player gets it
(halved, truncated, when `captive+0x5bc` is set; flag **Unknown**), the entry is marked claimed, "Received c.{1} reward." shows and
`Bounty_Fulfilled` plays. A claimed entry pays once.

### 11.5 Bail and sentence

- **Bail** (`CharacterTrading_PrisonerBailout` `FUN_1406ab890`, "Pay Bounty", **Observed**): `2 * bounty` (`FUN_140854080` with a
  zero faction argument: which entry is **Unknown**); at least 2000 when the prisoner is not of the player's faction.
- **Sentence** (`FUN_140854950`, the only reader of CONSTANTS `prison time` 1.0; reader **Verified**, formula **Observed**): caging a
  prisoner with a bounty for the cage owner's faction sets `trunc(bounty * 0.02 * prison time)` (presumably game hours; 999999 above
  25000) from now. Release and how bail ends it are not traced. `FUN_140851d10` (a lerp of 350/600 and 450/80-based values by a
  health fraction) is a different, undecoded function.

### 11.6 Booking factions and police hits (**Observed**)

- **Faction `+0x40`** (FACTION loader `FUN_1407fe270`): `fundamental type` 1 (LAW_ENFORCEMENT), else `offers bounties`. Base data:
  Police, Mercenary Police, United Cities, The Holy Nation, Shek Kingdom, Republic of Tertius (**Verified** by `CrimeInstallTests`).
  The loader also stores `fundamental type` at `+0x34` and `not real` at `+0x1d0` (the setter's "lawless" tests).
- **Hitting the police** (`FUN_140665fa0`): a landed blow between a police-like and a non-police character gives the non-police one a
  pending Assault (timer 30) against the police faction, booked with it at once, whichever struck.
- **Attack order** (`FUN_140334070`): a pending Assault against the victim's faction unless the victim has a bounty with the attacker's
  faction, its status flag `+0x114` is set, it is a BANDIT, it has a pending crime, or `FUN_140436610(victim, 1)` (undecoded) holds.
- **Murder**: no setter caller passes code 4 (25 checked); how a killing becomes Murder is **Unknown**.

### 12. Slaves, prisoners, animals

where `strength` = `CharStatsAnimal::vfunc_2`'s value (stat at `+0x80` times `CharStats+0x34`; `k` = `CharStats+0x34`), `attack` =
the melee-attack stat (`+0x120`), `dexterity` = `+0x88`, with the stat offsets from [character-stats.md](character-stats.md).
RACE `buy value` is 100 for most races (27 of 53 records); other values in the data: 400 x8, 10 x4, 50 x3, 75 x2, 200 x2, 250 x2,
1000 x2, 0 x2, 300 x1 (**Verified** by probe).

| Window | Class | Price (**Observed**) |
| --- | --- | --- |
| Selling a slave to a slaver | `CharacterTrading_SlaveSelling` `FUN_1406a6f50` | an explicit override if the caller gave one, else `clamp(buyValue / 2, 200, 2500)` |
| Buying a slave | `CharacterTrading_SlaveBuying` `FUN_1406a7970` | override or `buyValue`; **doubled** when the character is of the player's faction |
| Buying an animal | `CharacterTrading_AnimalBuying` `FUN_1406a9440` | override or 14000, times `(growth + 0.1)` where `growth` = the animal's `vfunc 0x398` (age/maturity 0..1) |
| Paying bail | `CharacterTrading_PrisonerBailout` | section 11.5 |

## Unknowns

- **The stolen predicate** `FUN_14079e550(item, 1)`: it reads two owner handles (`+0x140`, `+0x160`), a flag byte, and a type
  check against the type list `{2, 3, 4, 0x2e, 0x56, 0x66, 0x6b, 0x6f}`. What exactly makes an item "stolen" (the thief, the
  owning faction vs. the holder's, expiry) and the second halving condition in `FUN_140793490` are not decoded. The `0.5`
  stolen factor in `T` is therefore only roughly defined. *Partly resolved 2026-10-13* ([carry-loot.md 4.4](carry-loot.md#stolen-ownership)):
  `+0x140` is the owner the item was taken from, `+0x160` its current holder (a backpack's owner for a backpack); stolen = the from-owner is set,
  differs from the holder and is of another faction, unless the holder's faction is lawless; the type list is the lootable item kinds. Expiry and
  the halving condition remain open.
- **The meaning of `Armour+0x1e8`** (non-null multiplies the armour price by the hard-coded 0.3) and of `Weapon/Armour+0x208`
  (inferred as "crafted by the player" from the editor text of the two player rows; not shown by a writer).
- **Whether the town being set up is already in the global town list `DAT_142134100` when its own market factors are rolled.**
  If it were, its distance to itself is 0, the spread `w` is 0 and its own `p` is 1.0, which would force every factor to 1.0 and
  make the feature inert; so either the town is added after setup or something not yet seen intervenes. The order of the list
  decides which neighbours have factors already.
- **Whether `Zones_TownSetup` runs on a save load** and, if so, whether the saved `trade goods` factors (section 7.2, saved by `FUN_140376b80`,
  loaded by `FUN_1403715c0`) overwrite or are overwritten by a fresh roll. (That the factors are saved is settled.)
- **How the market factor reaches the display**: `FUN_1407ae130` ("Price mark-up", "Avg. Price") builds the tooltip text from the
  same functions; the exact strings and the average formula were not decoded.
- **What `FUN_140745e30` really does** (it forwards a money delta to virtual slot 54 of the object at `+0x80`; the purse mutation itself) and how
  the trade window decides the `k` units priced with the opposite flag (`FUN_14070e3b0`, section 5).
- **Where `MapItem+0x1e8` (the map price) comes from**, and which record `BlueprintItem+0xc0` points at (section 10).
- **How weapon quality is rolled for shop stock** and how VENDOR_LIST reference value 1 (and a value 0 in the first slot, as in
  `(0,100,0)`) is read for clothing, armour and weapon entries (`FUN_140956ba0` / `FUN_1409555d0` read them; "val0 is relative
  chance" per the editor, but 472 entries have (0, 100, 0)). It follows the NPC clothing rule in [characters.md](../characters.md)
  that quantity 0 never spawns, but this was not re-verified for shops.
- **The slave price override** (the fifth argument of the slave windows) and where it comes from.
- **The artifact placement** (`ArtifactTown`, which squad carries which artifact) beyond the six field readers.
- **The crime-witness faction selection** `FUN_140852010`: *mostly resolved 2026-10-13* (11.2: the prison owner's faction when `Character+0x2f8` is 2, which reads as
  imprisoned; else the given faction's parent `+0x38`, else the faction itself with the `+0x40` flag). Still open: what sets a faction's `+0x38`.
- **How a pending crime's timer counts down** (`+0x90`; set by `FUN_140851f40` / `FUN_140852e10`, the decrement was not found), and so how long an unseen crime stays pending.
- **Whether CHARACTER `money min` / `money max`** exist in the data and are unused (the editor lists `money`; not found
  being read for NPC purses).
- **The "Terrorism" label for code 9** (two crime codes share the name; the second may be a different crime in the editor enum).
- **What `FUN_140851d10` computes** (`prison time`'s consumer is the cage sentence, 11.6), how a sentence counts down and ends, and how bail
  ends it.
- **Research cost** beyond the formula of section 10, crafting costs, building purchase price, bed cost.
- **The purpose of `FUN_1405d4260`** (the crime-in-progress tooltip function; its behaviour remains untraced).
- **Which item records make a `MoneyItem`** (priced at their `value` alone, section 2): `Cats`, and probably `String of Cats` and the other value-only items, but the constructor's
  choice of class was not traced; Meitou tests the name `Cats` (found 2026-10-14 while building the trade).
- **The names of the VENDOR_LIST reference lists** (`items`, `weapons`, `clothing`, ... in section 9.2) and the reading of a v0 of 0 in them: Meitou reads every list and keeps
  each entry by its target's record type, with 0 counting as 100 (engine choices, [../simulation.md "Trade"](../simulation.md#trade)).
- **The FACTION field names of the armour vendor quality chances** (`armors 0..5` in section 9.2): read as the ints `armors 0` .. `armors 5`, falling back to the editor defaults; not
  checked against the data.
- **The trading range** ("Out of trading range.", section 8): Meitou opens the trade within 30 units.

## Expression reference

- Item base value: `ItemValue` (`FUN_140896e20`): `lerp(t, a, b) = a + (b - a) * t`; `ItemValue` (`FUN_140896e20`): `s = quality / 100`
- WEAPON: `v = value`; `v = trunc(v * price mod)`; `L(s) = 1 / (1 + e^(-(2s - 1) * 5 * 0.8))`; `raw = trunc( lerp(L(s), 15, 160) * v )`; `result = trunc( raw * sword price mult * global price mult )`
- ARMOUR and clothing: `t = s * s`; `lo = classLo[class] * matLo[material type]`; `hi = classHi[class] * matHi[material type]`; `v = trunc( lerp(t, lo, hi) )`; `v = trunc( v * armor price mult )`; `v = trunc( v * relative price mult )`
- CROSSBOW and LIMB_REPLACEMENT: `raw = trunc( lerp(s*s, value, value 1) )`
- Runtime unit price: Item virtual slots 81 and 82: `(sell != 0) + 3`; `u = ItemValue(record, item quality, 1)`; `u = trunc( u * marketFactor )`; `u = trunc( u * loot price mult )`; `u = trunc( u * charges / max charges )`; `n == 1`
- Trader factor `T` and the stolen test: `T = buyMult * (0.5 if stolen-to-the-player-faction) * culturePrice(item)`; `+0x250 == 0`
- Market factor: `f = uniform in [1 - m, 1 + m)`; `m = trade profit margins`; `w = d / 1,000,000 * m * 10`; `m = 0.3`; `p = 1.0`; `v0 = trunc(factor * 10000)`
- Buying, selling, fencing: `{1} = 100 - round(100 * p)`; `p = lerp(thievery / 100, 0.4, 1.25)`
- Stock size and money (`FUN_14095ab10`, squad shops; `FUN_14095b210`, building shops; **Observed**, decompile and: `fill = vendors fill total amount`; `wealth = clamp(FUN_1402dd530(faction), 0.2, 1.0)`; `n = round( uniform(0.8, 1.2) * fill * wealth )`; `money = trunc( clamp(0.25 * stockValue, 3000, 25000) * wealth )`; `money = trunc( (vendor money + r) * wealth )`
- Restock: `+0x58 == 0`
- Pending crimes, witnesses and the cage sentence: `+0x34 == 8`
- Slaves, prisoners, animals: `buyValue = clamp( trunc( strength * k + attack + dexterity * k ) * RACE buy value, 1000, 20000 )`

## Evidence index

Evidence scopes below apply only to their named sources/checks.

- 4. Item base value: `ItemValue` (`FUN_140896e20`)
- 4.4 CONTAINER (type 0x2e = 46)
- 4.7 WEAPON_MANUFACTURER `price mod` (data, Verified by probe)
- 6. Trader factor `T` (`FUN_14079e6d0`) and the stolen test
- 7. Towns: culture and the market factor (`Zones_TownSetup` `FUN_1409353c0`, `read_forbidden_items_n5` `FUN_1407fd9a0`)
- 9.1 SQUAD_TEMPLATE fields (data = **Verified** by probe over 959 templates)
- 9.2 Stock size and money (`FUN_14095ab10`, squad shops; `FUN_14095b210`, building shops; **Observed**, decompile and
- 9.3 Restock (**Observed**)
