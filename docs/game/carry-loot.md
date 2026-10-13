# Carrying, rescue and looting

How `kenshi_x64.exe` lets a character pick up a downed character, carry it, put it down, put it in a bed or a cage, how the rescue job finds
its patients, and how looting a downed or dead character works, with the crimes and witnesses that follow. Labels as in
[../README.md](../README.md): **Verified** (two independent places, or code against data, and how), **Observed** (one function read),
**Unknown**.

Sources (2026-10-13, outside the repository): the decompile dump of `kenshi_x64.exe`, jump-thunk targets read from the exe's bytes with a
throwaway reader, and the install's merged game data (`gamedata.base` + `Newwworld.mod` + `Dialogue.mod` + `rebirth.mod`) read with the repo's
`GameDatabase`. No decompiled text is quoted; the rules are restated. The game was not run. Related: [character-stats.md](character-stats.md)
(the medical object, the KO time, encumbrance), [ai-tasks.md](ai-tasks.md) (task types, world states, the planner tables),
[economy.md](economy.md#11-bounties-and-crimes) (bounties; the crime rules found here are summarised in its section 11.6),
[perception.md](perception.md) (what a witness can see), [ui-input.md](ui-input.md) (keys and the context menu), [../animation.md](../animation.md)
(the carrying booleans of ANIMATION records). What Meitou builds from this is in [../simulation.md](../simulation.md#carrying-rescue-and-looting).

## Character fields used here

Offsets on the `Character` object; all **Observed** unless the row says otherwise. The names are ours.

| Offset | Meaning | Evidence |
| --- | --- | --- |
| `+0x348` byte | carries someone (`isCarrying`) | set by the attach `FUN_1405cff90`, cleared by the unlink `FUN_1405ce1e0`, read by the carried-weight function `FUN_1405c89d0`: **Verified**, three functions agree |
| `+0x3d4` byte | is being carried | set by `FUN_1405ced90` (the body's side of the attach), cleared by `FUN_1405ce1e0` / `FUN_1405ccba0`; makes "ragdolled or carried" (`FUN_1407d1440`) true: **Verified** |
| `+0x380` handle (type `+0x388`, ids `+0x38c..+0x398`) | the carry partner: the body for the carrier, the carrier for the body | written on both sides by `FUN_1405cff90` / `FUN_1405ced90`, reset to the null handle (type 0xb) by the unlinks: **Verified** |
| `+0x378` byte | carry side, 0 right shoulder, 1 left | picks the bone `Bip01 R UpperArm` or `Bip01 L UpperArm` in `FUN_1405ced90` |
| `+0x350` string | side suffix of the lift clip name (`shoulder lift left` / `right`) | `Task_LiftSomething` slots 1 to 3 |
| `+0x2e0` | the RACE object; RACE `carriable` is its byte `+0x7a` | the RACE loader `FUN_14042efa0` stores it there and the lift task reads it: **Verified** |
| `+0x2e8` | the inventory | `FUN_1405c89d0` |
| `+0x2f8` int | special state: 0 normal, 1 in a bed (bed handle at `+0x300`, type `+0x308`), 2 imprisoned (cage or restraint) | `FUN_14032e2f0` sets and clears 1; the attach clears it and releases a restraint (`uses_restrains_movement_unique` `FUN_140330600`) when it was 2 |
| `+0x650` -> `+0x318` | the medical object; its byte `+0x165` = right arm usable, `+0x166` = left arm usable | section 1.2 |
| `+0x5bc` byte | dead | set by the death function `FUN_1407a6200` ("{1} is dead.", "{1} has died from blood loss.") |
| `+0x5b9` byte | unconscious (inferred) | no writer found; every reader pairs it with dead as "cannot act" (`FUN_1407944d0`, `FUN_140997c70`, `FUN_1408cdf50`, `isReadyForAction` `FUN_140998550`) |
| vtable slot 6 (`+0x30`), `CharacterAnimal::vfunc_6` `FUN_1405c9e10` | `isDown`: dead, `+0x5b9`, ragdolled or carried, or leg state (slot 109, `+0xe0`) of 3 or more | |

## 1. Picking someone up (`Task_LiftSomething`)

The task class of `LIFT_PERSON` (68), `LIFT_OBJECT_BUT_HEAL_FIRST` (170), `LIFT_PERSON_SNATCHING_ALLOWED` (213), `LIFT_PERSON_PLAYER_ORDER` (225)
and `LIFT_PERSON_SNATCHING_ALLOWED_IN_TOWN_ONLY` (240) ([ai-tasks.md](ai-tasks.md)). Slots: 1 start `FUN_14034f990`, 2 update `FUN_140354ff0`,
3 end `FUN_1403471e0`, 4 "done" `FUN_140334d40` (returns the task's "lift finished" byte `+0x7a`). Other task fields: `+0x10` the target, `+0x78`
"body attached", `+0x79` "snatching allowed".

### 1.1 Who can be carried

- The target must be a character (handle type 1, or a second character-like type 0x5b resolved by `FUN_140791600`, **Unknown** what 0x5b is) and
  not the carrier (**Observed**, slots 1 and 2).
- Its RACE must be `carriable`, else the task fails at once. In the data every race is carriable except the **Leviathan** and the mod race
  Big-Bones (**Verified**, probe of the merged RACE list, 53 records, re-run 2026-10-13).
- For the AI goals the target must be down (`isTargetDown`, world state 7, evaluator `FUN_14099a5a0`, **Observed**): true for a null target;
  for a character when it is ragdolled or carried, unconscious, dead, in a bed or imprisoned (`+0x2f8` 1 or 2), or its leg state is 2 or more;
  false for any other kind of target; a character handle that no longer resolves counts as down unless `FUN_1407ac7b0` accepts the object. The
  player's order (225) does not list `isTargetDown` among its preconditions, so what makes "Pick Up" available is the context menu (section 3.1).
- A target carried by someone else: the task fails unless it was made with snatching allowed or a check of the target's partner handle
  against the lifter passes (the handle's vtable `+0x10`, **Unknown** what it compares). A snatch first unlinks the old carrier
  (`FUN_1405ce1e0(oldCarrier, 1, 0)`, section 2.1) (**Observed**, `FUN_14034fbe0`).
- No size or weight test was found besides `carriable`; animals can be carried. RACE `carry bone` names the carried body's own bone used by the
  carry attachment built in `uses_carry_bone` `FUN_1407d3850` (**Observed**; how it places the body is **Unknown**): empty for every humanoid,
  `Bip01 L Calf` for the Swamp Turtle, the robot spiders, Crab, Spider and Skimmer, `Bip01 Head` for the Beak Thing, `Bip01 L ForeArm` for the
  Crimper, Land Bat and Gorillos (**Verified**, probe). RACE `male carry ragdoll` / `female carry ragdoll` exist in `fcs.def`; their reader was
  not traced (**Unknown**).

### 1.2 The carrying arm is the left one

- World state 0x50 `isCarryingArmOK` (failure text "My arm is messed up too badly to lift anything.") returns byte `+0x166` of the medical
  object (`FUN_1405c7930`, read from the exe bytes). The player-order check `FUN_1405d1940` reads the same byte for the task types 68, 105,
  148, 170, 213 and 225 and answers "I can't carry anyone with this arm." when it is 0 (**Verified**: two readers).
- That byte is the medical object's **left arm usable** flag: the medical refresh `FUN_140649320` sets `+0x165` and `+0x166` to 1, then clears
  `+0x166` for each collapsed part whose LOCATIONAL_DAMAGE `collapse part` has bit 4 (left arm) and `+0x165` for bit 2 (right arm);
  `FUN_140649870` does the same per part while the character is not carried; `FUN_1406409f0` sets `+0x166` back to 1 (**Verified**: three
  writers; bit 4 is the left arm, [character-stats.md](character-stats.md)).
- The body always goes on the **left shoulder**: before every lift and in the attach, `FUN_1406220d0` sets the side string to "left" and
  `+0x378` to 1, and with 1 the body hangs from the carrier's `Bip01 L UpperArm` (the right-hand bone would be used for 0, but no writer of 0
  was found on this path) (**Verified**: the side setter, the bone choice and the arm check agree).
- So a character whose left arm is collapsed or missing cannot carry; the right arm does not matter. Losing the left arm (limb slot 0) while
  carrying drops the body: the limb removal `FUN_140645690` calls the unlink when `+0x348` is set (**Observed**).

### 1.3 Sequence and clips (**Observed**, one function each)

1. Start: unless the carrier is in deep water (water state 3, `FUN_1405c7fd0`), it plays the action clip `shoulder lift left` at speed 1
   (`FUN_1405203b0`).
2. Update: movement stopped. Progress p is the clip's normalised time (`FUN_1405b4460`), or 1 in deep water. At **p > 0.4** the body is
   attached once (`FUN_14034fbe0`, section 1.4); at **p >= 0.95** the task is finished and `+0x348` is set again.
3. End: both lift clips are removed; if the body was attached, `+0x348` is set (unless the partner handle is null).

Clip names (facts): `shoulder lift left`, `shoulder lift right`, `shoulder lift B` (the body's clip, stopped at attach and release),
`carry me` (the body's held pose, played if the animation set has it, blend 0.01). In the merged data (**Verified**, probe 2026-10-13):
`shoulder lift left` (record `1427-gamedata.base`) and `shoulder lift right` are whole-body actions of category 0; `carry me`
(`1423-gamedata.base`) is an upper-layer looping idle action of **category 3** with `being carried` set; the carrier's movement clips are
`walk carrying left` (move speed 14) and `jog carrying left` (45) on the upper layer, its standing pose `stand carrying left` an overlay idle,
and `carrying left arm only` an overlay idle (the right-side twins exist; `rebirth.mod` adds crouched and swimming variants). No lower-layer or
whole-body clip carries a carrying flag.

### 1.4 The attach (`FUN_14034fbe0` -> `FUN_1405cff90` -> `FUN_1405ced90`, **Observed**)

- **Breaking someone out of a cage**: if the target is imprisoned (`+0x2f8 == 2`) in a cage (`+0x300`) whose owner faction is not the carrier's,
  the carrier is of the player's faction and the cage faction's `+0x1d0` byte is clear, a pending crime is set on the **carrier** with code 7 and
  on the **target** with code 0xc (Escaping Prison), both against the cage's faction (`FUN_140852c80`, section 4.3), and a flag `+0xe8` of the
  target's `+0x1a0` object is set. Code 7 is "Terrorism" (5000) in the name table `FUN_140856530`; the table has a second "Terrorism" (9), into
  which an Assault (5) is turned under a victim test (section 4.3).
- A target already carried is first unlinked from its carrier.
- `FUN_1405cff90(carrier, target)`: only when the carrier neither carries nor is carried and the target is not carried. It releases the target's
  restraint, sets the side (left), sets `+0x348`, the partner handle and `+0x2f8 = 0` on the carrier, gives the carrier's animation object
  (`+0x448` -> `+0x1e8..`) the target for its carrying idles, then calls `FUN_1405ced90(target, carrier)`.
- `FUN_1405ced90(target, carrier)`: unlinks the target from another carrier without dropping it; puts it into a held ragdoll mode
  (`FUN_1405c8920(target, 1, 1, 0)`); takes it out of a bed; sets `+0x3d4`, releases a restraint, `+0x2f8 = 0`, the partner handle; sets AI flag
  0x10 and clears AI flag 4 (`FUN_14060e840` / `FUN_14060e8c0` on `+0x650`, meanings **Unknown**); switches the animation layer, starts `carry me`
  and writes the attach bone (`Bip01 L UpperArm`) to the target's animation object (`+0x448` -> `+0x1b0`).
- Then `shoulder lift B` is stopped on the body and the sound `Character_Pick_Up_Person` plays with the switch `Armor_Worn` = the body's armour
  class name (table at `141ceb0f0` indexed by `+0x68c`, the first entry "Cloth"). When the carrier is of the player's faction and the body is
  not, `FUN_14079c600(platoon of the body)` is called (**Unknown** purpose).

### 1.5 Weight and speed

- **Weight** (**Verified**: `FUN_1405c89d0`, and the CONSTANTS loader `FUN_14086b2b0` stores `carry person weight` as its field 0x4c in global
  `142133f00`, whose only readers are `FUN_1405c89d0` and the tooltip `FUN_1405d4260`): carried weight = own inventory weight + `carry person
  weight` (30 in the data) while `+0x348` is set. The body's inventory, race or size add nothing.
- **Speed**: the speed product (`FUN_140886460`, `FUN_140883db0`, `FUN_140884780`) does not read `+0x348`; the only effect is the encumbrance
  factor through the extra 30 ([character-stats.md](character-stats.md#encumbrance-and-carrying)). Whether the carrying movement clips cap the
  speed is **Unknown** (their caps were not read).
- **Speed mode** (**Observed**, `SpeedSelector::vfunc_0` `FUN_140342a30` and `Task_Move::vfunc_6` `FUN_140332390`): a carrier in speed mode 0 is
  raised to mode 1 ([pathfinding.md](pathfinding.md): mode 1 caps at 55, mode 0 at a per-task value), unless its squad forces a mode
  (`platoon +0x78 -> +0xb0 != 4`).
- Swimming XP is 10 instead of 5 while carrying (`FUN_1408c6660`, in [character-stats.md](character-stats.md)); the stealth tooltip shows
  "Carrying something:" (`FUN_14088f9a0`), the athletics tooltip "Carrying somebody:" (`FUN_140895350`); the strength help text names "Carrying
  people or overloading inventory" as a trainer.
- Arm overlay (**Observed**, `AnimationClassHuman::vfunc_30` `FUN_140520000`): `carrying left arm only` / `carrying right arm only` play as an arm
  overlay when the animation class's per-arm "busy" bytes say one arm is free and the speed is at most 50 (the inputs of those bytes were not
  traced).

### 1.6 Dropping

- **The carrier goes down** (`uses_female_ragdoll_male_ragdoll` `FUN_1405d0db0`, **Observed**; the mask names **Verified** against `fcs_enums.def`
  `RagdollPart { WHOLE=1, RIGHT_ARM=2, LEFT_ARM=4, ..., CARRY_MODE=2048 }`): a carrier switching to a ragdoll drops the body
  (`FUN_1405ce1e0(carrier, 1, 0)`) when the reason has `WHOLE` (knocked out or dead) or `CARRY_MODE`, or `LEFT_ARM` while carrying on the left
  (`RIGHT_ARM` on the right). So a knocked-out or dead carrier always drops, and a limp arm drops only on the carrying side (the left in
  practice).
- **Unlink callers** (13 call sites of the thunk `14000deae`, **Verified** by the call graph): the snatch in the attach, `Task_PutDownObject`
  (update and end), `Task_PutInSomething` (update), `Task_TakeIntruderOutside` (two), `FUN_1403567a0` (a corpse into a machine, "incinerated or
  cooked"), the death `FUN_1405ce9c0` (the dying carrier drops), the limb removal `FUN_140645690`, `FUN_140680560` (a large update that unlinks
  every carried member of a list), `FUN_140799af0` (object destroyed), `FUN_1405ced90` (a body taken over) and the ragdoll switch above.
- **Fighting while carrying** (**Observed**): the melee and ranged tasks have no `IS_CARRYING_SOMETHING` precondition (`DEFEAT_SQUAD` 101 and 248,
  the guard posts 38 / 268 / 157, `PATROL_TOWN`, `USE_TURRET`, beds and sitting do need it false). But the AI's per-target engagement weight
  (`AI::vfunc_35` `FUN_140598080`) and `AI::vfunc_59` `FUN_14059e1a0` return 0 while the character carries someone, so a carrier picks no fight;
  `AI::vfunc_14` `FUN_140599b90` halves a per-target value while carrying (**Unknown** meaning). Being hit does not make a carrier drop; only the
  rules above do.

## 2. Putting down, beds and cages

### 2.1 The unlink `FUN_1405ce1e0(character, ragdollArg, keepInPlace)` (**Observed**)

Acts from either side (when `+0x3d4` or `+0x348` is set; normally called on the carrier); skipped while the global `DAT_1421349ca` is set
(**Unknown**, probably a loading or teardown flag).

1. Clears `+0x3d4` and `+0x348` and the animation link (`+0x448`: handle null, `+0x240 = -1`).
2. If the partner's `+0x1a0` object has its `+0xe9` byte set, the partner does not regard this character as friendly (`CharacterAnimal::vfunc_126`
   false), and the partner is neither unconscious nor dead: relation event kind 4 (`FUN_1406744a0(partner, this, 4)`), a pending **Kidnapping**
   (code 0xf) on this character against the partner's faction with timer 10 (`FUN_140852e10`; **Verified** by disassembly: the code is the fifth
   argument, stored at `[rsp+0x20]` at `1405ce3d9`, and `r9d` = 10), and `FUN_140858f40(partner's +0x68 object, this, 1)`. Reading: setting down a
   conscious, non-friendly person whose flag `+0xe9` is set is a kidnapping (the meaning of `+0xe9` is **Unknown**).
3. The partner stops `shoulder lift B` and its partner handle is cleared.
4. Unless `keepInPlace`, the partner is moved to this character's position and dropped as a ragdoll (`FUN_1405ccba0(partner, ragdollArg, 1)`:
   `+0x3d4 = 0`, ragdoll on, handle cleared, position snapped by `FUN_1405b1b00`, AI flags 0x10 and 4 cleared, a state value from vtable `+0x3f8`
   written back to the AI object).

**Friendly** (`CharacterAnimal::vfunc_126(a, b, strict)` `FUN_1407923d0`, **Observed**): the same faction is friendly; otherwise not if
`FUN_140678960(a, b, 4)`; if a is of the player's faction, not if `FUN_140678960(b, a, 4)` and yes if `FUN_140678960(b, a, 3)`; then friendly
unless `FUN_140678960(a, b, 3)` or `FUN_140599590(a, b)`, else by the faction relation `FUN_1406b27b0` (a's faction relations against b's faction,
or b's town faction when strict). Kinds 3 and 4 of `FUN_140678960` are per-character relation lists (**Unknown** names); which relation test
`FUN_1406b27b0` makes is **Unknown**.

### 2.2 `Task_PutDownObject` (`PUT_DOWN_OBJECT` 69, also goal 169) (**Observed**: `FUN_14034a230` start, `FUN_1403504f0` update, `FUN_1403509f0` end)

- Start: `shoulder lift left` at speed **-1** (the lift played backwards).
- Update: when the clip's progress is **below 0.2** (or the sentinel 1.01) the body is unlinked and dropped (2.1). Then, if the carrier does not
  regard it as friendly (`vfunc_126(carrier, body, 1)`) and `FUN_14032e2a0(carrier, body)` is false (true when the body has a handle at `+0x328..`
  of a type other than 0xb, its `+0x320` byte set and its `+0x1a0 -> +0xe0` equal to the carrier's faction: probably "our slave"), the body is
  **knocked out again**: `FUN_140644980(body medical, f)` with **f = lerp(0.1, 0.4, carrier assassination / 100)** (the lerp helper
  `FUN_140015b63` -> `FUN_1409b0f40` is `a + (b - a) t`; assassination is CharStats `+0xb8`; the help text says assassination "determines how
  long they stay unconscious"). The notes read the timer it sets as `max(3, (2 x knockout time base + |blood capacity - blood| x lerp(knockout mult
  1, knockout mult 99, toughness / 100) x 0.5) x f x 4)`; [character-stats.md](character-stats.md#knock-out-coma-death) reads the same function on
  the leg-loss path as `(2 x base + all the KO terms) x (1 - toughness / 100) x 4`, floor 3, times a random number. Which terms it sums and whether
  the roll applies here is **Unknown**. Sound `Character_Drop_Person` with the `Armor_Worn` switch.
- End: removes both lift clips, unlinks if still linked, knocks a non-friendly body out the same way (without the slave test), stops
  `shoulder lift B` on it.
- So an enemy that is carried and set down stays out: from the base term alone 16 (assassination 0) to 64 (assassination 100) units of the KO
  timer, whose unit is **Unknown**.

### 2.3 `Task_PutInSomething` (`PUT_SOMEONE_IN_BED` 99, `PUT_IN_CAGE` 108) (**Observed**: `FUN_140358170`, `FUN_140358670`, `FUN_140358cc0`)

Fields: `+0x10` the bed or cage, `+0x78` the use type (100 bed, 107 cage, matching `GET_PUT_IN_BED` 100 and `USE_CAGE` 107), `+0x7c` a value
passed to the faction relations at the end, `+0x80` done.

- Start, a bed for a player-faction carrier: finds the bed's operator slot for the carrier and the body (building vtable `+0x4c0`), uses the
  smaller valid one, and `FUN_1403576c0` **charges bed rent**: no slot gives "This isn't mine" (fail); a bed the player does not own gives "Paid
  c.{1} for bed rental." or "Can't afford c.{1} for bed rental." (fail). The amount was not traced (probably the editor's `bed usage cost`,
  **Unknown**). Then the reversed lift clip plays and movement stops.
- Update: when the clip's progress is below 0.2 and the building accepts the body (vtable `+0x4f0` with its position), the body is unlinked
  without dropping (`FUN_1405ce1e0(carrier, 0, 1)`), its ragdoll released (`FUN_1405ccba0(body, 0, 0)`), then:
  - **Cage**: imprisoned (`+0x2f8 = 2`, the cage in `+0x300`); relation event kind 5 between body and carrier (`FUN_140673a20`),
    `FUN_14067aca0(carrier, body)`; the cage's lock (vtable `+0x400`) gets `+0x20 = 1` (locked); and **if the body has a bounty with the cage
    owner's faction** (`FUN_140854080 > 0`) the sentence starts (`FUN_140854950(ledger, FUN_140851fb0(faction))`): ledger `+0x98` = now, `+0xa0` =
    `trunc(bounty x 0.02 x prison time)` for bounties below 25001, 999999 above, 16 when the bounty read there is 0 (`prison time` is CONSTANTS
    field 0x5b, global `142133f3c`, whose only reader is `FUN_140854950`, 1.0 in the data: **Verified**; the formula **Observed**). The unit is
    presumably game hours (5000 cats give 100, 25000 give 500). `FUN_140851fb0(faction)` returns the faction's parent (`+0x38`), or the faction
    itself when that is null and its `+0x40` byte is set.
  - **Bed**: `FUN_14032e2f0(body, 1, bed)` (`+0x2f8 = 1`, the bed in `+0x300`, the body added to the bed's occupants, vtable `+0x4f8`), then a task
    of kind 4 made by `FUN_14038ab50` is given to the body (`FUN_1405ca680`; **Unknown**, probably the order that keeps it lying there).
  - Both: `FUN_140054200(body)`, an animation reset (`FUN_14051dfa0`), the **bounty claim** `FUN_140330d50(building, carrier, body)`
    ([economy.md 11.4](economy.md#114-claiming): caging a wanted prisoner for the police pays), and a report to the body's faction relations
    (vtable `+0x28`, with `+0x7c` and `FUN_140796020(body)`).
- End: removes the lift clips; when done, stops `shoulder lift B` on the body.
- In a bed (`+0x2f8 == 1`) a character heals blood x `resting heal rate mult` (2) and uses `bed hunger rate` (0.33)
  ([character-stats.md](character-stats.md#sleep-beds-and-resting)); nothing bed-specific was found on the put-in path.

## 3. Rescue

### 3.1 The player's orders

- The `rescue` key (event 0x20, NUM8) and the orders panel's Rescue button run `FUN_140721790`: the selected characters get the **job**
  `FIND_AND_RESCUE_IF_THERES_BEDS` (148) with no target (`FUN_1407fa650(PlayerInterface, 0x94, null)`) and the sound `Medic` plays (**Observed**;
  NUM7, event 0x1f, gives job 0x3a the same way, `FUN_1407fac20`). Tooltip (`FUN_14072b540`): "pick up all your wounded allies. If you own any
  buildings nearby, they will take the wounded and put them in beds. Make sure to heal their wounds first."
- A quick right click on a downed **friendly** character (`FUN_1407fb410`, **Observed**): untreated damage above 0.01 (`FUN_140643ca0(med, 0)`)
  gives `FIRST_AID_ORDER` (25, pointer `med`); else the second damage reading above 0.01 (`FUN_140643ca0(med, 1)`) gives task 60 ("Repairs",
  probably robot parts, **Unknown**); else, when the character is down, not carried, not the selected character itself and `carriable`,
  `LIFT_PERSON` (68, pointer `lift`); a character carried by the selected one gets `PUT_DOWN_OBJECT` (69). The order goes to the nearest selected
  player character that is not busy (`FUN_1407fae70`; a selected member that is itself carried passes it to its carrier when that carrier is of
  the player's faction), after the order check `FUN_1405d1940` (the left-arm message above).
- Context menu verbs (`FUN_14079b2d0`, **Verified** in [ui-input.md](ui-input.md#context-menu-verbs-fun_14079b2d0)): 68 / 213 / 225 "Pick Up",
  69 / 70 "Put Down", 99 "Put in Bed", 108 "Put in", 246 "Kidnap".

### 3.2 Goals and their target finders

TaskData (`FUN_140319a60`) holds a **target finder** at `+0x160` and a follow-up goal pair at `+0xc` / `+0x10` (**Observed**: the field writes; the
finders resolved from the thunks in the exe's bytes):

| Goal | Desire | Finder | Follow-up |
| --- | --- | --- | --- |
| `FIND_AND_RESCUE` 105 | `140599480` (exe bytes): 0 if CharStats `+0x12b` is set or the character carries, else 1 | `FUN_1408cd550` | - |
| `FIND_AND_RESCUE_IF_THERES_BEDS` 148 | `FUN_1405994c0`: 0 if CharStats `+0x12b` or carrying, else the bed search `FUN_1408cfe00(..., 1)` | `FUN_1408cd550` | 148, then **106** `FIND_BED_AND_PUT_IN` |
| `FIND_AND_RESCUE_LEADER` 142 | - | `FUN_140598540`: the squad leader (`platoon +0xa0`) when it is not the character itself and not dead | - |
| `FIND_BED_AND_PUT_IN` 106 | `FUN_1405a0d50`: 2 while carrying a body the carrier regards as friendly, whose slot `0x248` is null, that (for a police carrier, `FUN_140599600`) has no bounty with the carrier's faction, whose faction is not hostile to the carrier's (`FUN_1406b26d0`) and not in its town's list `FUN_140599390`; else 0 | none | - |
| `CARRY_WOUNDED_SLAVES` 168 | - | `FUN_1408cdf50` | - |
| `FIND_AND_KIDNAP` 132 | - | `FUN_1408cd5c0` | - |
| `FIND_A_DEAD_GUY` 138 / 230 | - | `FUN_1408cfb00` / `FUN_1408ce170` (the nearest dead, ragdolled, uncarried body; `carriable` when asked) | - |
| `LOOT_THE_DEAD` 177 | - | `FUN_14099e680` (via `FUN_14099ae10`) | - |
| `LOOT_STOLEN_GOODS` 212 | - | `FUN_1408cf6a0` (calls the stolen predicate) | - |
| `PICKUP_INTRUDERS` 223 / 273 | - | `FUN_14099c110` / `FUN_14099c250` | - |

**The rescue finder** `FUN_1408cd550` = `FUN_1408cd2d0(AI, out, include 1, exclude 0x4010, bedsToo false)` (**Observed**):

- Candidates are the character's **sensed characters** (the list at `+8` of the object at character vtable `+0x68`), filtered by each entry's
  category word (`+0x30 -> +0x30`): any bit of `include`, none of `exclude` (`FUN_140853960`). What the bits mean is **Unknown** (1 is presumably
  "ally"). There is **no range** of its own: whatever is sensed counts.
- A candidate qualifies when it is ragdolled or carried or cannot stand (slot 108, `+0x5b8`), is not carried, its `+0x2f8` is 0, it is **not
  dead**, and its slot `0x248` is null; with `bedsToo` (the kidnap finder) characters lying in a bed qualify too.
- For a player-faction rescuer with include bit 1, the body must be of the rescuer's own faction unless the global toggle **Rescue Allies**
  (`PlayerInterface+0x112`, saved as the squad setting `rescue`; tooltip "Characters with the rescue job will also rescue allied characters from
  other factions", `FUN_140494ce0`) is on.
- No squad mate may already have a task of this type with this target (`FUN_1402690e0` count below 1).
- The **nearest** by squared distance wins (weight 1 when found, else 0).
- `FIND_AND_KIDNAP` uses the same function with include 0x4008, exclude 0x811 and `bedsToo` true.

**Leader down** (`isLeaderDown`, state 0x65, `FUN_140997c70`): true when the squad has no leader or the leader is ragdolled or carried,
unconscious, dead, or cannot stand. `Pickup downed leader` (142) is in 7 package goal lists, mostly squad goals of town and caravan squads
(**Verified**, probe of the AI packages).

**Wounded slaves** `FUN_1408cdf50` (**Observed**): sensed category include 0x20, exclude 0x210; a character that is unconscious, dead or has a leg
state above 0 other than 1, whose owner (`+0x1a0 -> +0xe0`) is the finder's faction, not carried, state 0, slave status (`+0x1a0` int) non-zero,
not targeted by a squad mate; the nearest wins.

### 3.3 After the pick-up

- The planner (**Observed**, the tables in [ai-tasks.md](ai-tasks.md)): `FIND_AND_RESCUE*` has the effect `IS_CARRYING_TARGET`, reached by
  `LIFT_PERSON` (68: `isTargetDown`, `isCarryingArmOK`, at the location) or `LIFT_OBJECT_BUT_HEAL_FIRST` (170: `!needsFirstAid` first, so a medic
  step is planned before the lift; its effect `IS_CARRYING_TARGET_HEALTHY` = `FUN_14059ab30`: carrying the target and the target needs no first
  aid, `FUN_14099d8f0`: race `+0x7c` clear and untreated damage above 0.01).
- **To bed**: job 148's follow-up 106 `FIND_BED_AND_PUT_IN` (effect `isCarriedDudeInBed`) is planned through `PUT_SOMEONE_IN_BED` (99:
  `machineHasFreeOperatorSlot`, `IS_CARRYING_SOMETHING`, at the bed); the bed search `FUN_1408cfe00` was not decoded (**Unknown**: range,
  ownership, rent). For a player-faction carrier the rent of 2.3 applies. 106 is also in 7 AI packages (`Put cairried dude in bed`, e.g. town
  residents and law enforcers).
- `PUT_DOWN_CARRIED_DUDE_IF_THEY_CAN_WALK` (169, urgent, in 9 packages) ends a carry once the body can walk again (its preconditions want
  `!IS_CARRYING_SOMETHING`, met by `PUT_DOWN_OBJECT`); the "can walk" test was not read (**Unknown**).
- Nothing heals a body on a carrier's shoulder beyond the normal medical tick; first aid is a separate task (170 or the medic job).

## 4. Looting

### 4.1 What may be looted (`isItemLootable`, state 0xf, `FUN_14059dcc0`, **Observed**)

- A null target is lootable. A **character** is lootable when it is down (slot 6: unconscious, dead, ragdolled or carried, leg state 3 or more)
  **or** its `+0x2f8` is non-zero (in a bed or imprisoned). An **item** is lootable when its type is WEAPON 2, ARMOUR 3, ITEM 4, CONTAINER 0x2e,
  NEST_ITEM 0x56, MAP_ITEM 0x66, CROSSBOW 0x6b or LIMB_REPLACEMENT 0x6f (the FcsRecordType numbers; the same list as the stolen predicate, 4.4) and
  it still resolves in the global item list `DAT_142133fe0`. Anything else is not.
- No rule against taking **equipped** items was found on the loot path; equipped gear only lowers the pickpocket chance (4.3). Whether the
  inventory window lists a downed character's worn slots is a UI question not traced (**Unknown**).
- Knocked out and dead are alike for access; they differ only in the witness logic (a knocked-out or dead victim never notices; the permission
  test `CharacterAnimal::vfunc_82` does not look at the difference).

### 4.2 The loot order and its window

- Context verb of `LOOT_TARGET` (26) (`FUN_14079b2d0`, **Observed**): a target of the player's faction gives "Inventory" for a building (kind 0),
  else "Trade" (an exchange with a squad mate); any other target "Loot".
- `Task_Loot_Order` (26; slots `FUN_140354dc0`, `FUN_140354ef0`, `FUN_1403466a0`, **Observed**): not for target kinds 0x22 and 0x36; stops, opens
  the loot window (`FUN_140791830(GUI, looter, target, 3)`), sound `Character_Loot` for a character and `Character_Inspect` otherwise. It counts its
  updates; after 5 it ends once the window is closed or the looter's `+0x2b0` byte is set. End: clears the `squat T` clip (the looting crouch) and
  closes the window.
- `Task_Loot_AI` (`PICKUP` 3 for NPCs, `FUN_140355ad0`, **Observed**): a police looter (`FUN_140599600`) commits no crime. Otherwise a pending
  **Looting** (10) against the body's faction is set when the looter has any bounty or is a bandit (`NPC class` 8), and also when the permission
  test (4.3) fails; dialogue event 0x21 (0x20 on another branch) is said to the body; if the body is of the player's faction and its slot 0x248 is
  set, "{1} is butchering {2} for meat!!" appears.

### 4.3 Crimes for taking items

Taking first records a **pending crime** on the taker: `FUN_140852c80(taker ledger, code, victim faction, victim handle)`; the record at
`Character+0xf0` holds the code (`+0x58`), victim faction (`+0x60`), victim handle (`+0x70..`) and a freshness timer (`+0x90`, at least 3; set to
the given value by `FUN_140852e10`) (**Observed**). The setter refuses when the taker is police-like (`FUN_140599600`: `NPC class` 1
LAW_ENFORCEMENT, or 2 MILITARY when its `+0x1a0 -> +0x88` matches its squad's `+0x30`; never for a faction with `+0x34 == 8`), is imprisoned, the
victim faction is the taker's own (or its `+0x1a0` faction), or the victim faction has `+0x1d0` set or `+0x34 == 8` (the "lawless" factions whose
goods are free). An Assault (5) becomes 9 when the victim passes `FUN_140796240(victim, 1)`; otherwise an Assault does not replace another pending
crime (it raises that crime's timer to 20). A new code clears the "booked" flag `+0xa4`.

**Permission to take from a downed character** (`CharacterAnimal::vfunc_82(taker, victim, item)` `FUN_140793940`, **Observed**): no crime when the
victim is not a character; or is a slave (status 1 at `+0x1a0`) or status 2; or `FUN_14032e1e0(taker, victim)` (the victim's `+0x320` flag with
`+0x334` equal to the taker's `+100`: probably "my own prisoner"); or the victim has any bounty; or its `NPC class` is 8 (OT_BANDIT); or the taker's
`NPC class` is 1 (OT_LAW_ENFORCEMENT), 2 (OT_MILITARY) or 5 (OT_DIPLOMAT); or the item's owner is a character of the taker's faction (taking your own
things back) while the taker is free (status not 1 or 2, `+0xe8` clear) and the victim's class is not 1, 5 or 3. Otherwise it is a crime.
(`NPC class` = CHARACTER `NPC class`, `CharacterTypeEnum` OT_NONE 0, LAW_ENFORCEMENT 1, MILITARY 2, TRADER 3, CIVILIAN 4, DIPLOMAT 5, SLAVE 6,
SLAVER 7, BANDIT 8, ADVENTURER 9, stored at `+0x1a0 -> +0xdc` by `FUN_14062b210`: **Verified**, the loader, the `fcs.def` default
`CharacterTypeEnum.OT_MILITARY` and the editor's enum.)

**Which code** (**Verified** by disassembly of both call sites; they disagree):

- Right click or lift transfer out of another inventory (`FUN_140713d20`, call at `140713fe3`): a **conscious** owner gives **Theft** (3); a down
  owner without permission **Looting** (10).
- Drag and drop into one's own inventory (`InventoryGUI::placeItemFromMouse` `FUN_140715560`, call at `140715c30`): a character source whose owner
  is down without permission gives **Theft** (3); any other source (a conscious owner, a container, a building) **Looting** (10).
- Dropping a held item on another character or object (`FUN_1407136a0` -> `FUN_140712490`): a "stealing" transfer from a non-player owner sets a
  Theft and rolls the steal check below.
- `Task_Pickup` (an item on the ground): Theft when the picker stands inside a building and the item's owner (vtable `+0x2a8`) is a character of
  another faction that is not lawless (4.5).

Both transfer paths run only when the move is a steal at all (`uses_building_category(src, dst)` true and the owner not of the player's faction,
`FUN_1407916d0`). Which path the usual loot takes is **Unknown** (section "Unknowns").

**The steal roll** (`CharacterAnimal::vfunc_83` `FUN_140794810`, chance `FUN_1407944d0`, **Observed**):

- p = lerp(0, 1.1, thievery / 100). For a building or container owner, the watcher is the nearest member of the owning faction
  (`FUN_140792ce0`), t = lerp(0.5, 1, clamp(d / 200, 0, 1)) with d its distance to the container (`FUN_140027d90(d, 0, 200)`), x0.5 when the
  watcher is in the same building, then p = lerp(0.3, 2.25, thievery / 100 x t) = 0.3 + 1.95 x thievery / 100 x t; a sleeping or lying watcher
  (leg state 3) doubles it, a watcher on another floor x1.5. Pickpocketing a character: the victim is the watcher; an equipped item (`+0x129`)
  x0.75, and x0.75 again when the item's slot `0x2e0` is non-null.
- **p = 1 (always succeeds) when the watcher is unconscious, dead, carried or imprisoned**, or the thief is down. So taking from a knocked-out or
  dead body is never caught by that body; only the pending crime remains.
- Caught (roll above p): a watcher in bed may just say event 0x2f; else it gets up, says event 8 ("Got caught stealing.", message 8 of
  `FUN_14070f7e0`), a Theft with timer 30 is set against the owner's faction, and relation event 4 between watcher and thief. Thievery XP: 2 x 20 on
  success, 2 x 80 when caught, unless p >= 0.98, the thief is imprisoned or its pending timer is 10 or more. `Task_Pickup` gives thievery XP 20 for
  a stolen pickup (`FUN_1408c7310(stats, 10, 20)`).

**Witnesses** (`FUN_140854d10(witnessSense, suspect)`, called from the per-sensed-character update `FUN_1408580d0` when the suspect is seen in
that pass or has a pending crime whose timer is above 5; **Observed**):

- Nothing when witness and suspect share a faction, the suspect is carried or imprisoned, the witness is imprisoned, the suspect is a slave
  (inferred: the argument of that status read is lost in the decompile), the witness has status 2 or its `+0xe8` flag, the witness has a pending
  crime of its own, or the witness is down.
- When the witness's faction is not hostile to the suspect's (`FUN_1406b2630`) the carrying check (4.6) runs first.
- With a pending crime: for Theft or Fencing (3 / 0xd) and Looting (10, which also needs the witness not to regard the suspect as friendly) the
  witness must care about the victim (it regards the victim as friendly, or its faction is allied with the victim's, `FUN_1406b2630`); Kidnapping
  (0xf) needs a victim handle and that care. Then: a reaction of kind 7 (witness against suspect), the suspect's crime timer set to 20
  (`FUN_140851f40`), a reaction (`FUN_140684990`, dialogue event 0x29 / 0x2a / 0x2b / 0x3a / 0x22 / 0x11 / 0x39 by code), and **only when the
  witness is police-like and its faction's `+0x40` flag is set** the crime is booked as a bounty (`FUN_140853ec0(suspect ledger, witness
  faction)`, [economy.md 11.2](economy.md#112-how-a-bounty-is-gained)). Ordinary witnesses only react.
- More of the same function (re-read 2026-10-13, **Observed**): with a pending crime, the victim's faction must not be the witness's slave owner faction, nor one the witness's
  faction is hostile to (`FUN_1406b26d0`), nor lawless (`+0x1d0`), nor a BANDIT-type faction when the witness's faction has the `+0x40` flag; and a witness that regards the suspect
  as friendly reacts only to crimes against its own faction. An **Assault** (5) needs a victim that resolves and that the witness does not regard as an enemy (slot 125; a slaver
  branch for slave victims not decoded); a **Murder** (4) and the codes without a case need nothing more (no care). The "kind 7" call (`FUN_1406744a0`) goes to the witness's
  squad object (`FUN_140674300`), not to the faction relation table (the slot 5 table of factions-squads-towns.md 3.4 has no row 7); it reads as a squad-level tag on the suspect.
  Only when the witness regards both suspect and victim as friendly does the function change the witness faction's relations (a call with -20 and 1, purpose not decoded).

### 4.4 The stolen flag (`FUN_14079e550(item, strict)`, **Observed**; answers part of the economy.md Unknown)

- Item `+0x140` is a handle to the owner it was **taken from** (types and ids at `+0x148..`), `+0x160` the current holder (`+0x168` type, `+0x16c`
  id).
- When the from-owner resolves, the holder's faction is the holder character's, or, for a CONTAINER (0x2e) holder (a backpack) found in
  `DAT_142133fe0`, that container's owner's (`+0x230`). Not stolen when the from-owner's faction is the holder's, or the holder's faction is lawless
  (`+0x34 == 8` or `+0x1d0`).
- Otherwise stolen when the from-owner handle is set (type other than 0xb) and its id differs from the holder's (`strict` 1); `strict` 0 also wants
  the from-owner to still resolve.
- The from-owner is written by the item's slot `0x2b0` (`Task_Pickup` passes the theft victim; dropping an item without an owner makes the dropper
  its owner, `FUN_1407136a0`). The tooltip "Stolen from:" is `FUN_1407a0ea0`. The type list `{2, 3, 4, 0x2e, 0x56, 0x66, 0x6b, 0x6f}` in that
  function is the item kinds of 4.1, not a crime list. In the save the item's `ownedby*` handle ([../formats/save.md](../formats/save.md)) is the
  likely home of the from-owner (**Unknown**, not checked).

### 4.5 Items on the ground

- **No separate ground-item class** was found: items live in the global item list `DAT_142133fe0` whatever holds them; a ground item is one with
  no holding inventory, shown with `Kenshi_ItemDropLabel.layout` (`FUN_1401649e0`, `FUN_140162e60`; label styles
  `DropItemLabel...Default/Selected/SelectedStealing`, grade styles `ItemStandard`, `ItemArtifact`, `ItemGrade0..5`). The save keeps them as
  loose ITEM records in the zone files ([../formats/save.md](../formats/save.md)).
- Hovering a ground item (`FUN_1407fc440`): an owner (slot `0x2a8`) that is a character of a faction neither the player's nor lawless gives the
  pointer `steal` (0x15), and the right click needs ALT ("Hold down the ALT key to steal items"), the order then being type 0x1d (**Unknown** why
  29; the context menu calls `PICKUP` 3 "Steal" for the same owner test); otherwise the pointer `pick_item` (4) and `PICKUP` 3 (`FUN_1407fb3a0`).
- `Task_Pickup` (`FUN_14034f340` update, `FUN_1403470b0` end with the clip `pickup`, **Observed**): fails unless the target is a still-resolving
  item of the kinds of 4.1; items of function 0x11 go to `FUN_1405d0b50` for a picker with slot 0x248; the theft rule of 4.3; then into the
  inventory (slot 0x168). The whole stack: the item's `+0x1a0` handle cleared, removed from the world (slot 0x238), the from-owner set when stolen;
  part of it: the ground stack keeps the rest. Sounds: switch `Inventory_Items` = the item's inventory sound, event `Character_Pick_Up_Ground`.
  Nothing fits: a picker with slot 0x248 may equip it (`FUN_1405d0780`), else "I can't pick that up".
- **Drop** (`FUN_1407136a0`, **Observed**): letting go of a held item over a character or object gives it to that object (with the theft rules);
  otherwise the item takes the dropper as owner when it had none and is put down by the inventory owner's object (slot `0x38`), with
  `Inventory_Items` and `Character_Drop_Ground`.
- No automatic pickup by player characters was found (NPC goals do it: `LOOT_THE_DEAD` 177 "Looting corpses and food" in 14 package lines,
  `LOOT_STOLEN_GOODS` 212 "confiscate stolen goods" in 2, `EAT_FOOD_ON_GROUND` 259 for player characters, [ai.md](ai.md)). No despawn timer was
  found (**Unknown**); CONSTANTS and settings only cap arrows and harpoons left in the ground.

### 4.6 Carrying people past witnesses (`FUN_1408549f0`, **Observed**)

For a witness that sees a carrier:

- The body is **not** an "innocent victim" (slot 106 `CharacterAnimal::vfunc_106` is true only for a carried body with no bounty, `NPC class`
  not 8, status 0 and its `+0x1a0 -> +0xe9` set) and is a slave or of status 2: when the body is not the witness faction's slave, a player-faction
  (or `+8`-flag) witness that regards it as friendly and whose squad relations allow it gives the carrier a pending **Kidnapping** (0xf), timer
  20; when it is the witness faction's slave, code 7 against the witness's faction.
- An innocent victim: when the witness regards the body as an enemy (slot 125) or as friendly, or the carrier's `+0x1a0 -> +0x88` is set and the
  body's faction is not lawless and has no bounty with the witness's faction, the carrier gets a pending **Kidnapping** against the body's
  faction, timer 20 (and the carrier's `+0x180` = 20).
- So carrying an ordinary civilian past people is a kidnapping; carrying your own wounded never is (the witness function stops early for the
  witness's own faction, and the setter refuses same-faction victims).

## Summary for an implementation

- Carrying is a link pair: the carrier's "carrying" flag and partner, the body's "carried" flag and partner. Lift: the target down (or ordered),
  RACE `carriable`, the carrier's **left arm** usable, `shoulder lift left`, attach at progress 0.4 to `Bip01 L UpperArm`, done at 0.95; the
  body plays `carry me`. Put down: the same clip backwards, let go below 0.2; a non-friendly body is knocked out again with factor
  `0.1 + 0.3 x assassination / 100`. Weight +30 (`carry person weight`), nothing else; speed mode 0 becomes 1. Dropped when the carrier is knocked
  out or dies or its left arm goes limp or is cut off.
- Bed: let go below 0.2 into the bed (state 1, rent for the player), the bounty claim. Cage: imprisoned, locked, a sentence of
  `0.02 x bounty x prison time` hours (999999 above 25000), the bounty claim; freeing someone from another faction's cage is crime 7 for the
  carrier and 0xc for the freed.
- Rescue: job 148 from NUM8; the finder takes the nearest sensed, down, uncarried, living character of the same faction (or an ally's with the
  Rescue Allies toggle) that no squad mate targets; 106 then puts it in a bed; leaders through 142.
- Loot: any down, bedded or imprisoned character; taking without permission is a pending Theft or Looting (by path), turned into a bounty only
  by a police witness who cares about the victim; knocked-out and dead victims never catch you; items carry a "stolen from" handle.

## Unknowns

1. The **bed search** for rescue (`FUN_1408cfe00`): range, which beds (own buildings only, or rented town beds), and what "if there's beds"
   needs; without it job 148 cannot decide whether to run.
2. The **sensed-list category bits** (`FUN_140853960` masks 1, 0x4010, 0x20, 0x210, 0x4008, 0x811, 0x10): which bit is ally, enemy, dead, slave;
   and the size of the sensed list, which is the search range of every finder.
3. The **status object at `Character+0x1a0`** (int at +0: 1 slave, `FUN_1405a4940` "{1} has been enslaved"; 2 probably an escaped slave or
   prisoner); its `+0xe8`, `+0xe9` (the "innocent victim" flag of the kidnapping rules), `+0x88`, `+0xe0` (a slave's owner faction); its class name.
4. The **writer of `+0x5b9`**, read as "unconscious" from its readers only.
5. **Which inventory transfer the player's usual loot takes** (drag and drop gives Theft for a down owner, the right-click transfer Looting): both
   are real, the UI events behind `FUN_140713d20` and `FUN_140715560` were not mapped.
6. **Witness timing**: how often `FUN_1408580d0` runs per sensed character, with what range and line of sight, and how the pending timer `+0x90`
   counts down (`FUN_140851f40` sets it; the decrement was not found).
7. `PUT_DOWN_CARRIED_DUDE_IF_THEY_CAN_WALK` (169): its "can walk" test; the bed task given to a body put in a bed (kind 4 from `FUN_14038ab50`).
8. The **bed rent** amount (`FUN_1403576c0`) and whether allied factions' beds charge it.
9. A **despawn timer** for ground items.
10. The steal order type 0x1d of the ALT path (`FUN_1407fc440`): the taskType table says 29 is `MOVE_CUS_ORDERED`.
11. The **speed caps** of the `carrying left/right` movement clips, and the use of RACE `male/female carry ragdoll`.
12. `FUN_14079c600`, called when the player picks up a non-player character, and **`CharStats+0x12b`** (set by slot 97 order kind 0xc), which turns
    both rescue goals off.
13. The terms and the random roll of `FUN_140644980` on the put-down path (section 2.2), and the relation test `FUN_1406b27b0` of "friendly".

## As built

Meitou's `CarrySystem` (`src/Meitou.Simulation/Carrying/`) and the choices it makes where this page stops are described in
[../simulation.md "Carrying, rescue and looting"](../simulation.md#carrying-rescue-and-looting); the crimes of sections 4.3 and 4.6 (pending crimes, witnesses, kidnapping, booking) in [../simulation.md "Crimes and bounties"](../simulation.md#crimes-and-bounties).
