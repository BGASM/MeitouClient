# Animals

How the original models wildlife and pack animals (beak things, bonedogs, goats, garru, bulls, spiders,
leviathans, crabs, gorillos, raptors...) as characters: their records, stats, body, natural weapon and
techniques. What the engine does with this is in [../simulation.md](../simulation.md#animals).

Sources: the install's records read with a throwaway FCS dumper (the base game's load order: `gamedata.base`,
`Newwworld.mod`, `Dialogue.mod`, `rebirth.mod`, 2026-10-13), and the Ghidra dumps of `kenshi_x64.exe`
(function addresses as in the other docs). Each claim is labelled.

## 1. Records

- **ANIMAL_CHARACTER** (type 76) is the animal's record, used where a CHARACTER would be (squad template `animals`
  / `animals2`, [factions-squads-towns.md 5.1](factions-squads-towns.md); saves name it as the character's
  `target`, [save.md](../formats/save.md)). The install's load order has 43 of them (**Verified**, counted).
- Fields (**Verified**, the union over the 43): `combat stats`, `unarmed stats`, `stealth stats`, `ranged stats`,
  `strength`, `stats randomise` (as on CHARACTER), `animal strength` (int), `HP mult`, `scale min`, `scale max`,
  `lifespan`, `smell blood`, `smell eggs`, `turn rate`, `attack type`, `female chance`, `inventory w/h`, the
  bounty fields, `unique`, `faction importance`, `sounds`; references `race` (every record, value 100), `weapon`,
  `weapon level`, `stats` (only the Leviathan), `faction` (only the Landbat), `animations`, `strafe anim`,
  `turning anim`, `death items`, `backpack`, `AI Goals`, `bird attractor`.
- The `race` is an ordinary RACE record (Beak Thing, Bonedog, Garru, Bull, Goat, Leviathan, Spider, Crab...), with
  the anatomy, blood, speeds and footprint of [character-stats.md "Races"](character-stats.md#races); the
  animals' forelegs are ARM parts ([character-stats.md](character-stats.md)).
- `weapon` is a WEAPON record of a creature `skill category` (9 ELEPHANT .. 20 GIRAFFE, [combat.md
  "Enums"](combat.md#enums-from-the-editors-decompile-facts)): `_dog type weapon` (10), `_giraffe type` (20),
  `_bull type` (20), `_elephant type` (9), `_garru` (17), `_gorillo` (16), `_duck type weapon` (15), `_spider
  type` (13), `_robot spider type` (12), `_skimmer` (13). None can block. Lengths 18 (duck, small spider) to 120
  (elephant). Swamp Turtle and "Pack Beast Dead" name no weapon (**Verified**, dump).
- `weapon level` is a WEAPON_MANUFACTURER: "Ancient" on all but the Skimmer ("Unknown") (**Verified**).
- `attack type` (an int in the creature category range) does **not** always match the weapon's category: Wild
  Bull 11 with a category-20 weapon, Cage Beast 14 and Landbat 19 with the dog weapon (10). The string "attack
  type" does not occur in `kenshi_x64.exe` (**Observed**: absent from the binary's string table), so the game does
  not read it; it is an editor-only field.

## 2. Stats

- The animal's stats object is `CharStatsAnimal` (**Observed**, RTTI). Its set-up (`CharStatsAnimal::vfunc_0`,
  `14064dc40`) reads `animal strength` into `+0x230` (as a float from the int), then runs the ordinary CharStats
  constructor (`14064cb30`): so an animal gets its stats exactly as a CHARACTER does, from its `stats` record if
  any, else from the five group fields, then `stats randomise`, then the race multipliers (**Observed**). The group
  fan-out is in [character-stats.md "Building a character's stats"](character-stats.md#building-a-characters-stats);
  for the usual animal (only `combat stats` c) it gives strength, dexterity, toughness, attack, defence and the
  weapon skills c, **martial arts c/10**.
- **Weapon skill** (**Observed**): CharStats keeps a pointer to the current weapon skill at `+0x1f8` and reads it
  through vtable slot 3; for an animal slot 3 returns martial arts (`+0x10c`) x `+0x34` and slot 5 points the
  skill at martial arts. So the creature weapon's damage uses martial arts as its skill (as the weapon category
  map already says: creature categories fall to the unarmed skill, [combat.md](combat.md)).
- **Stat multiplier** `+0x34` (**Observed**): `CharStatsAnimal::vfunc_4` sets it from the character's slot 0x398
  (`CharacterAnimal::vfunc_115`), which returns the animal's `+0x700`; `+0x700` is written by slot 0x388
  (`vfunc_113`). The strength getter (slot 2) returns strength x `+0x34` for both classes; slot 6 returns `animal
  strength` x `+0x34`. That `+0x700` is the **age** (0 baby, 1 adult; the squad's `animal age`) is an
  **engine reading**: its writer's callers were not traced, but the size below lerps by it.
- `animal strength`: no reader of slot 6 was found (**Unknown** use).
- The damage formulas read dexterity and strength from their fields times `+0x34` (`140883ba0`, `140883c60`,
  `140883d10`), and the skill through slot 3 (**Observed**). The weapon formula (cut, blunt, pierce) applies: a
  creature weapon is not of the unarmed category.

## 3. Body

`CharacterAnimal` (**Observed**, vtable `1416f2208`):

| Character slot | Human | Animal | Used by |
| --- | --- | --- | --- |
| 0x238 (`vfunc_71`) | 1 | `HP mult` (`+0x6dc`; read by `14062f000`, 1 when 0 or below) | part body scale (+0x5c), blood capacity |
| 0x250 (`vfunc_74`) | the humanoid stats object | null | blood capacity uses s = 1; the hit path cannot sever limbs |
| 0x390 (`vfunc_114`) | 1 | lerp(`+0x700`, `scale min`, `scale max`) | part HP multiplier (+0x58, rewritten each tick), blood capacity, hunger rate |
| 0x398 (`vfunc_115`) | 1 | `+0x700` | the stats multiplier `+0x34` |

- Blood capacity (`1406440a0`, [character-stats.md](character-stats.md#blood-and-bleeding)) = `min blood` +
  (`max blood` - `min blood`) x s x z x k with s = 1 for an animal, z = `HP mult`, k = the size. So an adult beak
  thing (scale max 1) holds its race's full 400, a megaraptor (HP mult 3) three times its spread (**Observed**
  formula; the save's 120.6 to 228.8 for 36 animals, [save.md](../formats/save.md), fits capacities of this kind).
- `lifespan`, `scale min`, `scale max`, `smell blood`, `smell eggs` and `HP mult` are read by `14062f000` into
  the CharacterAnimal (`+0x6f8`, `+0x6f0`, `+0x6f4`, `+0x6e8`, `+0x6ec`, `+0x6dc`) (**Observed**). What uses
  lifespan and the smells is **Unknown**.
- `self healing` is set on most animal races ([character-stats.md](character-stats.md#races)).

## 4. Techniques

COMBAT_TECHNIQUE `animal` (**Verified** over the records, 2026-10-13):

- 0 on every weapon technique (cuts, blocks, the "Dodge back" set, ground attacks): these are matched by their
  weapon type flags.
- **5 on the martial arts techniques** ("ma ..."), which also carry the `unarmed` flag.
- 9 to 20 on the 14 creature techniques: elephant bash / bite (9), dog attack / long (10), robot spider (12),
  insect spider (13), duck (15), gorillo 1 to 3 (16), gar long / legs (17), giraffe attack / short (20). They have
  **every weapon type flag off**, are neither blocks nor dodges, none is prone; `max simultaneous hits` 1, 2 or 99.
- So `animal` reads as the weapon category the technique belongs to (0 = "by the flags"); the animation
  preprocess keeps 8 and below as humanoid clips ([../animation.md](../animation.md)). That the chooser compares it
  with the fighter's weapon category is an **engine reading** (the attack chooser was not read, [combat.md
  "AI"](combat.md)); the data allows no other match: a bull's weapon (20) finds the giraffe techniques while its
  `attack type` 11 has none, and categories 11, 14, 18 and 19 have no techniques.
- Distances: most creature techniques have 0 for one case or both (`duck attack` 0 / 0, `gorillo` 0 against a
  moving target); what 0 means for them is **Unknown** (a literal reading would never let a goat strike).
- No creature technique is a block or a dodge and no creature weapon can block, so animals never parry or
  dodge (**Verified** data).

## 5. Factions and fleeing

- Only the Landbat names a faction; an animal's faction comes from its squad (the template's or the town's,
  [factions-squads-towns.md](factions-squads-towns.md)). Wild packs come from nests and the area spawns (6.2,
  6.3 there), which the engine does not build yet.
- An animal-type character (slot 73 non-null) plays `VO_Creature_Alert` on first seeing an enemy
  ([perception.md](perception.md)). No animal-specific run-away rule was found: the FACTION run-away ratios
  ([perception.md section 7](perception.md#7-fleeing)) are the only ones read (**Unknown** beyond that).

## 6. Not found

How the game levels the natural weapon item (`14062aa60` makes it from `weapon` and the `weapon level`
manufacturer without a level argument; the default was not traced, **Unknown**), the consumers of `animal
strength`, lifespan and the smells, how an animal ages (the writer of `+0x700`), animal AI goals.
