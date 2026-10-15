# AI task tables

Task names/IDs: `src/Meitou.Data/Gameplay/Ai/TaskType.cs` (all 288 named rows checked against this research). IDs 289/290 are unnamed in FCS. See [AI](ai.md) for runtime semantics. Tables retain factory mappings and all TaskData/state values; repeated enum names and condition names are represented by IDs.

## taskType to Task class

**Verified** (the Tasker factory `FUN_14032ebd0` switches on a byte table at `140330408` indexed by taskType - 1; the table was read
from the exe bytes with a probe, each case body was matched to the RTTI `Task_*` class its constructor installs, and the resulting
type-to-class list agrees for all 290 rows, including 147 default cases). 290 jump-table entries versus
289 names in `fcs_enums.def`: types 289 and 290 build `Task_Follow` and `Task_BashDoor` and have no enum name (**Unknown** why).
147 types fall through to `Task_Blank` (the factory's default case): no behaviour of their own from this factory.
The per-type record census is omitted; it can be regenerated from merged AI_TASK data.

Factory mappings grouped by identical class; each ID occurs exactly once:

- (goal; Task_Blank): 8, 9, 10, 11, 13, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24, 30, 32, 33, 34, 37, 38, 39, 41, 42, 43, 45, 46, 47, 48, 49, 50, 54, 55, 56, 57, 58, 59, 63, 66, 70, 84, 85, 86, 88, 89, 90, 91, 94, 101, 104, 105, 106, 109, 111, 112, 113, 114, 115, 117, 118, 120, 122, 125, 126, 131, 132, 135, 137, 138, 139, 142, 143, 144, 145, 148, 149, 151, 152, 153, 154, 155, 157, 158, 160, 167, 168, 169, 172, 173, 177, 178, 179, 183, 184, 187, 188, 189, 190, 191, 192, 193, 194, 195, 197, 202, 203, 204, 205, 206, 211, 212, 214, 217, 218, 222, 223, 230, 232, 233, 234, 237, 238, 239, 242, 243, 245, 248, 250, 251, 252, 254, 255, 259, 260, 265, 268, 270, 271, 273, 275, 276, 277, 279, 281, 282, 283, 288
- Task_AddMaterialsToBuilding: 71
- Task_BashDoor: 81, 123, 226, 272, 287, 290
- Task_BruteForceLock: 286
- Task_BruteForceShackles: 186
- Task_Build: 2, 95
- Task_CloseDoor: 73, 75
- Task_Crouch: 27
- Task_CuffTarget: 166
- Task_CutLock: 285
- Task_CutShackles: 185
- Task_Dismantle: 96
- Task_DogLimbRunningPlay: 278
- Task_EatingCrops: 231
- Task_EatPrisoner: 220
- Task_EmptyMachine: 284
- Task_EmptyMachine / Task_Pickup: 92
- Task_EmptyMachine_BuildMats / Task_Pickup: 247
- Task_EnterBuilding: 156
- Task_EquipBestWeapon: 6, 264
- Task_FeedCorpseIntoMachine: 136
- Task_FillMachine: 93, 124
- Task_FirstAid: 25, 60, 269
- Task_FirstAidRig: 249
- Task_FocusedMeleeAttack: 5, 61
- Task_Follow: 31, 44, 253, 289
- Task_Follow / Task_FollowAndTalk: 164
- Task_FollowLeader: 141, 171
- Task_GetOutOfBed: 116, 208, 257
- Task_GetOutOfCage: 133, 207, 256
- Task_GetSlave: 174
- Task_GetUp: 52, 65
- Task_GoHomeTown: 128
- Task_GoHomeTownFast: 127
- Task_grabOneFood: 244
- Task_HuntingSmell: 176
- Task_InvestigateAlarms: 209, 210
- Task_Kidnapping: 246
- Task_KillPrisoner: 134, 196
- Task_LeaveBuilding: 200
- Task_LiftSomething: 68, 170, 213, 225, 240
- Task_LockDoor: 77, 79, 162, 198
- Task_Loot_AI / Task_Pickup: 3
- Task_Loot_Order: 26
- Task_Loot_ResourcesWeHaveStorageFor: 241
- Task_MakeAnnouncement: 103, 129
- Task_MakeTargetStandStill: 64
- Task_ManTheGate: 180
- Task_MeleeAttack: 4, 227
- Task_Move: 1, 29, 40, 67
- Task_MoveBowRange: 267
- Task_MoveToDoor: 82
- Task_MoveToDoor_CurrentLocation: 83
- Task_MoveToDoor_Gate_ChooseSide: 121
- Task_MoveToDoor_Inside: 163
- Task_MoveToDoor_Outside: 199
- Task_OpenDoor: 72, 74
- Task_OperateMachine: 87, 221
- Task_OperateMachine / Task_UseTurret: 146, 235
- Task_Patrol: 22
- Task_PatrolTown: 36
- Task_PatrolTownViolent: 216
- Task_PickLock: 76
- Task_PickLock_Shackles: 201
- Task_PlayerTalkto: 12
- Task_ProcessNewSlave: 182
- Task_Prospecting: 150
- Task_PutDownObject: 69
- Task_PutInSomething: 99, 108
- Task_RangedAttack: 262, 263, 266
- Task_ReleasePrisoner: 110
- Task_Runaway: 35, 280
- Task_SheatheWeapon: 7
- Task_Shopping: 119
- Task_SleepOnFloor: 175, 261
- Task_StandAtNode: 51
- Task_StandStill: 62
- Task_StealthKO: 228, 229
- Task_StripWeapons: 181
- Task_Stumble: 147
- Task_TakeIntruderOutside: 224
- Task_TakeIntruderOutsideTown: 274
- Task_TakeOffCuffs: 219
- Task_TalktoPlayer: 102, 159, 161
- Task_TownStalker: 165
- Task_TravelToMissionTarget_ArmyFormation: 215
- Task_TravelToTargetTown: 53, 130
- Task_UnCrouch: 28
- Task_UnlockDoor: 78, 80, 140
- Task_UseBed: 98, 100, 258
- Task_UseCage: 107
- Task_UseTrainingDummy: 97
- Task_WorshipTarget: 236

## TaskData registry

**Verified** (disassembly of the table builder `FUN_140319a60`, each registration call `FUN_14060ea00(type, memberFn, ...)` parsed,
then every row re-parsed from the decompile of the same function: 253 registration calls with 253 distinct type numbers, and type,
text, flag bits and the contents and order of lists A and B agree for all 253 rows; the decompile drops some trailing call arguments,
so the `!` on an effect and the `*` marker rest on the disassembly alone and are **Observed**). 251 of the 288 named types plus the
two unnamed types 289 and 290 are registered; 37 named types are not: 9, 10, 11, 18, 23, 27, 33, 46, 48, 49, 50, 54, 56, 63, 66, 70, 109, 111, 117,
120, 126, 145, 151, 154, 160, 178, 179, 191, 193, 205, 206, 229, 251, 252, 265, 267, 271.
Columns: display text (field +0x138), registration flag bits, condition list A (TaskData +0x4c; names prefixed `!` are required
false; `*` marks an entry whose second flag byte (+5) is set, meaning **Unknown**), condition list B (+0xa8, at most 11) and the
single effect (+0x128). What list A versus list B mean is **Unknown**: A reads like "done when" conditions (isAtLocation,
isBuildingComplete) and B like "may start when" ones (isMovementAllowed, hasWeapon) in every row seen (**Observed**). The scorer
agrees with that reading in part: it drops a candidate whose list A is satisfied (`FUN_14060f570`) and the planner takes the first
unmet state id from list B (`FUN_14060f940`), but it also drops a candidate whose B holds and that has no effect, so the roles are
not settled. The `ALWAYS_IMPOSSIBLE_TASK` row's text is the string "null". Two states (33 and 85) are registered with an empty name.

A/B/effect use StateType IDs below. `!` and `*` retain their original meanings; literal `none`/`-` remain unchanged. Unmapped or ambiguous names remain literal. Row order is registration order.

| ID | Text | Flags | A | B | Effect |
| ---: | --- | --- | --- | --- | --- |
| 1 | Moving | 0x63 | isAtLocation, !69, 40, 41 | 27, !24, !83, 60, 90, 59 | - |
| 29 | Move order | 0x63 | isAtLocation | !24, !83, 60, 90, 59 | - |
| 67 | Running | 0x63 | isAtLocation, 40, 41 | 27, !24, !83, 60, 90, 59 | - |
| 82 | Moving to door | 0x61 | 57 | 27, !24, !83, 90, 60 | - |
| 163 | Moving to door inside | 0x41 | 111 | 27, !24, !83, 90, 60 | - |
| 83 | Moving to current door | 0x41 | 58 | 27, !24, !83 | - |
| 199 | Moving to door outside | 0x41 | 126 | 27, !24, !83, 90, 60 | - |
| 104 | null | 0x77 | - | 20 | - |
| 180 | Gate guard | 0x1 | - | 91, 52 | - |
| 121 | Moving to gate | 0x61 | 91 | 27, !26, 60 | - |
| 122 | Open gate | 0x61 | 90 | !54 | 52 |
| 123 | Bashing gate | 0x21 | !54, 56 | !70, !iAmBeingMeleeAttacked immediate, 165, hasWeaponEquipped, 91, !52 | - |
| 290 | Bashing gate | 0x21 | !54, 56 | !70, !iAmBeingMeleeAttacked immediate, 165, hasWeaponEquipped, 91, !52 | - |
| 272 | Smashing | 0x21 | 163 | !70, 165, !iAmBeingMeleeAttacked immediate, hasWeaponEquipped, isAtLocation | - |
| 270 | Assaulting town | 0x21 | 90 | - | 163 |
| 189 | Worker | 0x1 | - | - | 123 |
| 190 | Worker | 0x1 | - | - | 136 |
| 87 | Operating machine | 0x1 | 123 | 38, 73, 2, !hasWeaponEquipped, !63, !62, !89, !169, 93, isAtLocation | - |
| 221 | Operating machine | 0x1 | 136 | 38, 73, 2, 93, !hasWeaponEquipped, isAtLocation | - |
| 152 | Operating automatic machine | 0x1 | - | 2, !hasWeaponEquipped | !62 |
| 124 | Hauling to | 0x1 | - | 2, 67, isAtLocation | - |
| 114 | Ditching to make space | 0x1 | 82 | - | !62 |
| 242 | Ditching resources | 0x1 | 147 | - | !62 |
| 184 | Ditching loot | 0x1 | !121 | - | !62 |
| 92 | Collecting resource | 0x1 | 66, !63, !169 | 82, isAtLocation | - |
| 247 | Collecting resource | 0x1 | 150 | 82, isAtLocation | - |
| 284 | Looting | 0x1 | 68 | 82, isAtLocation | - |
| 115 | Finding building mats | 0x1 | 50 | - | 150 |
| 93 | Filling machine | 0x1 | !62, 63* | 2, 67, !169, isAtLocation | - |
| 94 | Try filling machine | 0x1 | 67 | - | 66 |
| 113 | Try emptying machine | 0x1 | !63, 105, !66, !89 | - | !62 |
| 91 | Unjamming machine | 0x1 | !64 | !62, !63 | - |
| 90 | Unjam all machines | 0x1 | !65 | - | !64 |
| 89 | Keeping everything running | 0x1 | - | !65 | - |
| 88 | Delivering | 0x1 | - | isAtLocation | - |
| 84 | Open current door | 0x41 | 60 | - | 53 |
| 74 | Opening door here | 0x41 | 53 | !55, 58 | - |
| 75 | Closing door here | 0x41 | !53 | 58 | - |
| 85 | Open destination door | 0x41 | 59 | - | 52 |
| 72 | Opening door | 0x41 | 52 | !54, 57 | - |
| 73 | Closing door | 0x41 | !52 | !56, 57 | - |
| 77 | Locking door | 0x41 | 54 | !52, 70, !56, 57 | - |
| 78 | Unlocking door | 0x45 | !54 | 70, 57 | - |
| 140 | Unlocking door | 0x1 | !54 | 70, 57 | - |
| 79 | Locking door here | 0x41 | 55 | !52, 70, 58 | - |
| 162 | Locking door from inside | 0x41 | 112 | 70, 111, !52 | - |
| 198 | Locking door from outside | 0x41 | 127 | 70, 126, !52 | - |
| 80 | Unlocking door here | 0x41 | !55 | 70, 58 | - |
| 287 | Bashing door | 0x21 | !55, 56, 52 | !70, !128, hasWeaponEquipped, 57 | - |
| 195 | Locking up cages | 0x1 | - | - | 54 |
| 155 | Locking up | 0x1 | 87 | !strangers in house, 107 | 112 |
| 197 | Locking up from outside | 0x1 | - | !strangers in house, !107 | 127 |
| 86 | Opening up | 0x1 | !87 | - | 52 |
| 156 | Entering building | 0x61 | 107 | 90, 59 | - |
| 216 | Ransacking town | 0x1 | - | hasWeaponEquipped | 107 |
| 200 | Leaving building | 0x61 | !107 | 59 | - |
| 76 | Lockpicking | 0x55 | !54 | 38, 166, isAtLocation, !hasWeaponEquipped | - |
| 201 | Lockpicking Shackles | 0x55 | !114 | 38, 167, isAtLocation, !hasWeaponEquipped | - |
| 219 | Removing Shackles | 0x45 | !134 | !114 | - |
| 202 | Escaping | 0x55 | none | !134, !79 | - |
| 185 | Cutting shackles | 0x55 | !114 | 38, 122, isAtLocation, !hasWeaponEquipped | - |
| 285 | Cutting lock | 0x55 | !54 | 38, 122, isAtLocation, !hasWeaponEquipped | - |
| 186 | Breaking shackles | 0x55 | !114 | 38, isAtLocation | - |
| 286 | Breaking lock | 0x55 | !54 | 38, isAtLocation | - |
| 133 | Get out of cage | 0x74 | !79 | !54 | - |
| 256 | Get out of cage | 0x74 | !79 | - | - |
| 207 | Escape cage | 0x74 | !79 | !54 | - |
| 81 | Bashing door | 0x21 | !54, 56, 52 | !70, !128, hasWeaponEquipped, 57 | - |
| 226 | Bashing door | 0x21 | !54, 56, 52 | !70, !128, hasWeaponEquipped, 57 | - |
| 95 | Repairing | 0x1 | !56, !71 | 38, isAtLocation, !26 | - |
| 125 | Engineer | 0x1 | - | - | 2 |
| 213 | Lifting/Snatching | 0x1 | 48 | 80, 27, !26, 7, isAtLocation | - |
| 240 | Capture Target | 0x1 | 48 | 95, 113, 80, 27, !26, 7, isAtLocation | - |
| 225 | Lifting/Snatching Order | 0x1 | 48 | 80, 27, !26, isAtLocation | - |
| 246 | Kidnapping | 0x1 | 48 | 80, 27, !26, 7, isAtLocation | - |
| 68 | Lifting | 0x1 | 48 | 80, 27, !26, isAtLocation, 7 | - |
| 170 | Lifting and healing | 0x1 | 115 | !78, !22, 80, 27, !26, isAtLocation | - |
| 69 | Putting down | 0x1 | !48, !47 | 47 | - |
| 2 | Building | 0x1 | 2, !71 | 38, 51, isAtLocation, !26 | - |
| 96 | Demolishing | 0x1 | none | isAtLocation | - |
| 71 | Adding materials | 0x1 | 51 | 50, isAtLocation, !26 | - |
| 3 | Collecting item | 0x65 | 3 | 15, isAtLocation | - |
| 14 | Idleness | 0x75 | - | !162, !hasWeaponEquipped | - |
| 28 | Standing | 0x25 | !24, !25, !26 | !83 | - |
| 65 | Getting up | 0x25 | !162 | 45 | - |
| 52 | Waking up | 0x65 | - | 45 | - |
| 147 | Stumbling | 0x21 | - | 45 | - |
| 6 | Equipping weapon | 0x65 | hasWeaponEquipped | !me swim, 38 | - |
| 264 | Equipping crossbow | 0x65 | hasWeaponEquipped | !me swim, 38 | - |
| 7 | Sheathing weapon | 0x65 | !hasWeaponEquipped, !hasWeaponEquipped, !hasWeaponEquipped | - | - |
| 269 | Fixing Legs | 0x45 | !103 | 38, 23, !me swim, !39, !hasWeaponEquipped | - |
| 25 | First aid | 0x45 | !22 | 38, 23, !target swimming, 42, isAtLocation, !hasWeaponEquipped | - |
| 249 | Splinting | 0x45 | !152 | 38, 151, !target swimming, 42, isAtLocation, !hasWeaponEquipped | - |
| 60 | Repairing | 0x45 | !133 | 38, 132, !target swimming, 42, isAtLocation, !hasWeaponEquipped | - |
| 58 | Medic | 0x45 | - | - | !22 |
| 250 | Splint Rigging | 0x5 | - | - | !152 |
| 57 | Robotics | 0x45 | - | !39 | !133 |
| 62 | Standing still | 0x75 | - | !me swim, !39 | - |
| 64 | Stand still dammit! | 0x45 | 42 | !target swimming, !16 | - |
| 237 | Worshiping | 0x41 | - | - | target worshiped |
| 236 | Worshiping | 0x41 | target worshiped | !hasWeaponEquipped, !47 | - |
| 131 | Looting food | 0x1 | - | - | 68 |
| 259 | Picking up food | 0x71 | 149 | - | 3 |
| 277 | Fetch | 0x20 | 149 | - | 3 |
| 278 | Playing 'Limbs' | 0x20 | !149 | - | - |
| 279 | Limb Jealousy | 0x20 | - | - | 28 |
| 243 | Getting food | 0x61 | - | - | 149 |
| 244 | Grabbing food | 0x61 | 149 | !12, 82, isAtLocation | - |
| 8 | Finding weapon | 0x45 | 4 | - | 3 |
| 177 | Looting the dead | 0x45 | - | - | 3 |
| 212 | Confiscating goods | 0x45 | - | - | 3 |
| 26 | Looting | 0x45 | !0 | isAtLocation | - |
| 238 | Foraging animals | 0x1 | - | 82 | 148 |
| 241 | Looting resources | 0x5 | 148 | isAtLocation | - |
| 35 | Running away | 0x61 | !39, 6 | 60, !26 | - |
| 280 | Running away | 0x61 | - | 60, !26 | - |
| 128 | Retreating | 0x63 | - | !26 | - |
| 127 | Retreating fast | 0x63 | 95 | !26 | - |
| 19 | Going home | 0x61 | 18 | - | isAtLocation |
| 59 | Get ready | 0x65 | 37 | !26, hasWeaponEquipped | - |
| 4 | Attacking | 0x41 | 6*, 7 | !target swimming, !me swim, 27, 60, 90, 59, 37, !69, 41 | - |
| 262 | Shooting | 0x1 | 6*, 7 | !me swim, !iAmBeingMeleeAttacked immediate, 161, !26, hasWeaponEquipped | - |
| 263 | Shooting | 0x1 | 7 | !me swim, !iAmBeingMeleeAttacked immediate, 161, !26, hasWeaponEquipped | - |
| 266 | Shooting | 0x1 | 7 | !me swim, !iAmBeingMeleeAttacked immediate, 161, !26, hasWeaponEquipped | - |
| 228 | Taking out | 0x1 | - | !target swimming, 27, 60, 90, 59, 38, !69 | - |
| 227 | Attacking | 0x20 | 6*, 7 | 27, !78, 60, 90, 59, 37, !69, hasWeaponEquipped, 41 | - |
| 13 | Attacking attackers of | 0x61 | !16, !39, !12* | - | 7 |
| 34 | Attacking trouble makers | 0x21 | !29 | - | 7 |
| 149 | Turret guard | 0x1 | 6* | 38 | using target turret |
| 222 | Turret home guard | 0x1 | 6* | - | using target turret |
| 146 | Turret gunner | 0x1 | using target turret*, using any turret | 38, !47, !iAmBeingMeleeAttacked immediate, 2, 93, !hasWeaponEquipped, isAtLocation | - |
| 235 | Turret gunner | 0x1 | 7 | using any turret | - |
| 234 | Turret guard | 0x1 | 6*, !39, !12* | 38 | using target turret |
| 101 | Assault | 0x61 | - | !47 | 7 |
| 248 | Attacking Squad | 0x61 | - | !iAmBeingMeleeAttacked general, !47 | 7 |
| 165 | Stalking | 0x1 | - | - | - |
| 103 | Diplomatting | 0x1 | 76 | !101, 131 | - |
| 129 | Approaching fast | 0x1 | 76 | !101, 131 | - |
| 159 | Talking | 0x41 | isDialogDelivered | isAtLocation | - |
| 161 | Talking | 0x55 | isDialogDelivered | - | - |
| 12 | Talking | 0x45 | none | isAtLocation | - |
| 102 | Making announcement | 0x41 | none | isAtLocation | - |
| 164 | Following and talking | 0x41 | none | !26, !iAmBeingMeleeAttacked immediate, 28 | - |
| 188 | Working the slaves | 0x1 | none | - | isDialogDelivered |
| 194 | Selling slaves | 0x1 | !155 | - | isDialogDelivered |
| 218 | Selling slaves | 0x1 | !155 | - | isDialogDelivered |
| 192 | Delivering slaves | 0x3 | - | !26, atDestinationTown | - |
| 158 | Shooing | 0x41 | !strangers in house | 107, !130 | isDialogDelivered |
| 283 | Shooing | 0x41 | !strangers in house | 107, !130 | isDialogDelivered |
| 17 | Protection | 0x61 | - | !16, !22 | - |
| 16 | Attacking enemies | 0x61 | - | - | 6 |
| 41 | Fighting for master | 0x21 | - | - | 6 |
| 21 | Attacking others | 0x61 | - | - | 7 |
| 144 | Home territory aggression | 0x61 | - | - | 6 |
| 32 | Self preservation | 0x65 | !iAmBeingMeleeAttacked general | - | !39 |
| 45 | Bodyguard | 0x61 | - | !16, !39 | - |
| 44 | Following | 0x61 | 28 | !16, 60, 90, 59 | - |
| 253 | Following | 0x61 | 28 | !7, 60, 90, 59 | - |
| 40 | Following | 0x61 | 28 | !7, !16, 60, 90, 59 | - |
| 141 | Following leader | 0x61 | - | 60, 90, 59, !26 | - |
| 171 | Following master | 0x61 | - | - | - |
| 31 | Staying close | 0x61 | - | !7, !16, 60, 90, 59 | - |
| 289 | Staying close | 0x20 | - | !7, !16, 60, 90, 168 | - |
| 166 | Restraining | 0x41 | 134, !124 | 38, isAtLocation, 118, 7 | - |
| 181 | Stripping weapons | 0x41 | 118 | 38, isAtLocation, 7 | - |
| 182 | Processing slave | 0x1 | 120 | isAtLocation, 134, 42 | - |
| 183 | Watching slaves | 0x1 | - | - | 120 |
| 260 | Watching for new slaves | 0x1 | - | - | 120 |
| 167 | Capturing slave | 0x1 | - | - | 134 |
| 174 | Ordering slaves | 0x1 | 116 | 38, 125, 134, isAtLocation | - |
| 173 | Gathering slaves | 0x1 | - | - | 116 |
| 217 | Gathering slaves for shipping | 0x1 | - | - | 116 |
| 245 | Gathering unholy slaves | 0x1 | - | - | 116 |
| 187 | Obedient Slave | 0x75 | none | - | - |
| 168 | Carrying slaves | 0x1 | - | - | 115 |
| 169 | Putting down | 0x1 | - | !47 | - |
| 15 | Protect allies | 0x61 | - | - | !12 |
| 288 | Protect allies | 0x61 | - | - | !12 |
| 143 | Protect close allies | 0x61 | - | - | !16 |
| 30 | Holding position | 0x75 | - | - | isAtLocation |
| 51 | Stand at node | 0x61 | 31 | isAtLocation | - |
| 20 | Shopkeeper | 0x41 | - | - | 31 |
| 38 | Static guard | 0x61 | - | !47 | 31 |
| 268 | Static guard indoors | 0x61 | - | !47 | 31 |
| 157 | Static town guard | 0x61 | - | !47 | 31 |
| 47 | general node | 0x1 | - | - | 31 |
| 42 | Getting a weapon | 0x45 | - | 38, 4 | - |
| 176 | Blood tracking | 0x61 | - | !26, 60 | - |
| 22 | Patrolling | 0x21 | - | !26, 60, !hasWeaponEquipped | - |
| 36 | Patrolling town | 0x21 | - | atDestinationTown, !47, !26, 60, !hasWeaponEquipped | - |
| 37 | Wandering town | 0x1 | - | !26, !hasWeaponEquipped | 31 |
| 118 | Shopping | 0x1 | - | 86 | (unnamed state 85) |
| 119 | Shopping | 0x1 | (unnamed state 85) | isAtLocation | - |
| 214 | Relaxing | 0x1 | - | 86 | 123 |
| 43 | Staying home | 0x61 | - | !26, !hasWeaponEquipped, 18 | 136 |
| 255 | Reigning | 0x41 | - | !26, !hasWeaponEquipped, 18 | 136 |
| 275 | Sitting around | 0x1 | - | !26, !47, !hasWeaponEquipped, 86 | 136 |
| 24 | Wandering | 0x21 | 86 | - | isAtLocation |
| 39 | Wandering trader | 0x1 | - | !hasWeaponEquipped, 32, atDestinationTown | - |
| 55 | Recruiting | 0x1 | - | - | isAtLocation |
| 53 | Travelling | 0x61 | atDestinationTown | !26, 32 | - |
| 130 | Travelling quickly | 0x61 | atDestinationTown | !26, 32 | - |
| 215 | Mission formation | 0x61 | 131 | !26 | - |
| 5 | Attacking target | 0x61 | 7 | !target swimming, !78, 37, !69 | - |
| 61 | Attacking target unprovoked | 0x61 | 7 | !target swimming, !78, 37, !69 | - |
| 150 | Prospecting | 0x41 | - | !me swim | - |
| 97 | Training | 0x1 | - | 38, 73, isAtLocation | - |
| 153 | Going to bed | 0x41 | - | !strangers in house P | 83 |
| 239 | Going to bed | 0x41 | - | !strangers in house, 87 | 83 |
| 98 | Sleeping | 0x41 | 72, 83 | !130, 73, !hasWeaponEquipped, !47, isAtLocation | - |
| 258 | Sleeping | 0x41 | - | !130, 73, !hasWeaponEquipped, !47, isAtLocation | - |
| 175 | Sleeping on ground | 0x61 | 72 | 6, !29, !95, !hasWeaponEquipped, !47 | - |
| 261 | Hiding | 0x61 | 72 | 6, !29, !hasWeaponEquipped, !47 | - |
| 99 | Put in bed | 0x1 | 77 | 73, 47, isAtLocation | - |
| 116 | Getting out of bed | 0x41 | !83 | 45 | - |
| 208 | Getting out of bed! | 0x41 | !83 | 45 | - |
| 257 | Getting out of bed | 0x41 | !83 | 45 | - |
| 105 | Find and rescue | 0x1 | - | - | 48 |
| 148 | Find and rescue | 0x1 | - | - | 48 |
| 106 | Find and put in bed | 0x1 | - | - | 77 |
| 100 | Sleeping | 0x41 | 72 | - | - |
| 142 | Find and rescue leader | 0x1 | - | 101 | 48 |
| 135 | Kill random prisoner | 0x1 | - | - | !98 |
| 134 | Kill prisoner | 0x1 | !98 | hasWeaponEquipped, isAtLocation | - |
| 139 | Eat random prisoner | 0x21 | - | - | 135 |
| 282 | Dinner time | 0x21 | - | - | 135 |
| 230 | Eat corpse | 0x61 | - | - | 135 |
| 233 | Eat body | 0x61 | - | - | 135 |
| 232 | Find crops | 0x61 | - | - | !am hungry |
| 231 | Eating crops | 0x61 | !am hungry | farm has food, isAtLocation | - |
| 220 | Eat prisoner | 0x61 | 135 | 142, !47, !hasWeaponEquipped, isAtLocation | - |
| 196 | Beat prisoner | 0x1 | 7 | 78, hasWeaponEquipped, isAtLocation | - |
| 136 | Disposing of corpse | 0x1 | 100 | 99, isAtLocation | - |
| 138 | Find corpses | 0x41 | 99 | - | 48 |
| 137 | Corpse disposal | 0x1 | - | 99 | 100 |
| 107 | Imprisoned | 0x55 | 79 | 73, isAtLocation | - |
| 108 | Put in prison | 0x1 | 81 | 73, 47, isAtLocation | - |
| 112 | Find and put in cage | 0x1 | 154 | !99 | 81 |
| 281 | Find and put in cage | 0x1 | 154 | - | 81 |
| 172 | Getting in my cage | 0x55 | - | - | 79 |
| 211 | Freeing prisoner | 0x1 | - | - | !78 |
| 276 | Liberating prisoners | 0x1 | - | - | !78 |
| 110 | Release from prison | 0x1 | 0, !78 | 125, !73, isAtLocation | - |
| 203 | Arresting | 0x1 | 129 | 7, 118, 48 | - |
| 204 | Bounty hunting | 0x1 | - | - | 129 |
| 223 | Remove Home Intruder | 0x1 | - | !47 | 48 |
| 224 | Intruder out | 0x41 | 137 | 47, 60 | - |
| 273 | Remove Intruder | 0x1 | - | !47 | 48 |
| 274 | Throwing out | 0x41 | 164 | 47, 60, 90 | - |
| 209 | Investigating | 0x1 | !130 | 86 | - |
| 210 | Investigating | 0x1 | !130 | 86 | - |
| 132 | Find and kidnap | 0x1 | 153 | !47 | 48 |
| 254 | Kidnapper | 0x1 | none | 153, 154 | - |

## StateType ids

**Observed** (registry `FUN_140610250`, one `FUN_140610170` call per id; names are the strings passed; the names of the other ids were re-read
from the decompile and agree with this table, except that the decompile shows no registration call for ids 0, 2, 3, 6, 13 and
20, whose names here come from the disassembly). No registration call was found for ids 8, 9, 10, 11, 14, 19, 21, 30, 34, 35, 44,
49, 61, 75, 96, 119, 138, 170; ids 33 and 85 are registered with an empty name.

| id | name |
|---|---|
| 0 | `none` |
| 1 | `isAtLocation` |
| 2 | `isBuildingComplete` |
| 3 | `isSubjectInInventory` |
| 4 | `hasWeapon` |
| 5 | `hasWeaponEquipped` |
| 6 | `isNoEnemiesInVicinity` |
| 7 | `isTargetDown` |
| 12 | `isAlliesFighting` |
| 13 | `isTargetEnemy` |
| 15 | `isItemLootable` |
| 16 | `isAggressionTowards` |
| 17 | `AM_IDLE` |
| 18 | `isAtHome` |
| 20 | `noneR` |
| 22 | `needsFirstAid` |
| 23 | `haveFirstAidKit` |
| 24 | `isCrouching` |
| 25 | `isLyingProne` |
| 26 | `IS_CROUCHING_OR_LYING` |
| 27 | `isMovementAllowed` |
| 28 | `isNearTo` |
| 29 | `isGeneralAggressionLevel` |
| 31 | `atNode` |
| 32 | `hasDestinationTown` |
| 33 | (empty name string) |
| 36 | `atDestinationTown` |
| 37 | `isReadyForAction` |
| 38 | `hasOneWorkingArm` |
| 39 | `isAggressionTowardsMe` |
| 40 | `isWithin10Meters` |
| 41 | `isWithin50Meters` |
| 42 | `targetStandingStill` |
| 43 | `iAmDown` |
| 45 | `canGetUp` |
| 46 | `isAtLocation` |
| 47 | `IS_CARRYING_SOMETHING` |
| 48 | `IS_CARRYING_TARGET` |
| 50 | `hasBuildingMaterials` |
| 51 | `buildingHasBuildingMaterials` |
| 52 | `DOOR_IS_OPEN` |
| 53 | `DOOR_IS_OPEN_HERE` |
| 54 | `isDoorLocked` |
| 55 | `DOOR_IS_LOCKED_HERE` |
| 56 | `DOOR_IS_BROKEN` |
| 57 | `AT_LOCATION_BUILDING_DOOR` |
| 58 | `AT_CURRENT_LOCATION_BUILDING_DOOR` |
| 59 | `DESTINATION_IS_ACCESSIBLE` |
| 60 | `LOCATION_IS_ACCESSIBLE` |
| 62 | `machineHasEmptyInputs` |
| 63 | `isMachineOutputFull` |
| 64 | `isMachineJammed` |
| 65 | `isAnyMachineJammed` |
| 66 | `haveSomeResourcesFrom` |
| 67 | `haveSomeResourcesFor` |
| 68 | `HAVE_SOME_LOOT_FROM` |
| 69 | `isRouteTraceBlocked` |
| 70 | `isObjectMine` |
| 71 | `isBuildingDamaged` |
| 72 | `isFullyRested` |
| 73 | `machineHasFreeOperatorSlot` |
| 74 | `isEnemyForceDefeated` |
| 76 | `isMessageDelivered` |
| 77 | `isCarriedDudeInBed` |
| 78 | `TARGET_IS_IMPRISONED` |
| 79 | `I_AM_IMPRISONED` |
| 80 | `isCarryingArmOK` |
| 81 | `CARRIED_DUDE_NOW_IN_CAGE` |
| 82 | `gotInventoryRoomForResources` |
| 83 | `isInBed` |
| 84 | `isAtAShop` |
| 85 | (empty name string) |
| 86 | `atATown` |
| 87 | `allDoorsLocked` |
| 88 | `isInsideTargetBuilding` |
| 89 | `haveSomeResourcesFromThisMachineButWantThemGoneIfPossible` |
| 90 | `DESTINATION_NOT_BLOCKED_BY_FORTIFICATIONS` |
| 91 | `AT_LOCATION_FORTIFICATION_GATE` |
| 92 | `isEnemy` |
| 93 | `BUILDING_HAS_POWER` |
| 94 | `atDestinationTown` |
| 95 | `isAtHomeTown` |
| 97 | `CAGES_ARE_ALL_FULL` |
| 98 | `MACHINE_OCCUPANT_IS_ALIVE` |
| 99 | `isCarriedDudeDead` |
| 100 | `CARRIED_DUDE_NOW_IN_DISPOSAL_MACHINE` |
| 101 | `isLeaderDown` |
| 102 | `isSquadmatesFighting` |
| 103 | `myLegsAreMessedUp` |
| 104 | `using target turret` |
| 105 | `isMachineOutputEmpty` |
| 106 | `machineHasFullInputs` |
| 107 | `I_AM_INSIDE_TARGET_BUILDING` |
| 108 | `strangers in house` |
| 109 | `isDialogDelivered` |
| 110 | `isDialogDelivered` |
| 111 | `AT_LOCATION_BUILDING_DOOR_INSIDE` |
| 112 | `DOOR_IS_LOCKED_AND_IM_INSIDE` |
| 113 | `targetAtATown` |
| 114 | `isWearingShackles_locked` |
| 115 | `IS_CARRYING_TARGET_HEALTHY` |
| 116 | `TARGET_IS_MY_SLAVE` |
| 117 | `TARGET_HAS_BEEN_LOOTED` |
| 118 | `TARGET_IS_UNARMED` |
| 120 | `TARGET_LOOKS_LIKE_A_SLAVE` |
| 121 | `CARRYING_EXCESS_LOOT` |
| 122 | `hasTools` |
| 123 | `IS_WORKING` |
| 124 | `isTargetEscapee` |
| 125 | `TARGET_IS_CONSCIOUS` |
| 126 | `AT_LOCATION_BUILDING_DOOR_OUTSIDE` |
| 127 | `DOOR_IS_LOCKED_AND_IM_OUTSIDE` |
| 128 | `isAlly` |
| 129 | `TARGET_IS_ARRESTED` |
| 130 | `ALARMS_IN_THE_VICINITY` |
| 131 | `atPackageTarget` |
| 132 | `haveroboAidKit` |
| 133 | `needsFirstAid_robot` |
| 134 | `isChained` |
| 135 | `TARGET_IS_EATEN` |
| 136 | `IS_WORKING_PRETEND` |
| 137 | `INTRUDER_IS_OUTSIDE` |
| 139 | `me swim` |
| 140 | `target swimming` |
| 141 | `strangers in house P` |
| 142 | `isTargetRestrainedOrKO` |
| 143 | `farm has food` |
| 144 | `am hungry` |
| 145 | `using any turret` |
| 146 | `target worshiped` |
| 147 | `DITCHED_ALL_RESOURCES` |
| 148 | `LOOTED_STORABLE_ITEMS` |
| 149 | `gotAFoodItem` |
| 150 | `HAVE_SOME_BUILD_MATS` |
| 151 | `hasSplintKit` |
| 152 | `needsSplint` |
| 153 | `FINAL_GOT_A_KIDNAP_VICTIM` |
| 154 | `FINAL_FOUND_AND_PUT_CARRIED_IN_A_CAGE` |
| 155 | `iHaveSomeSlaves` |
| 156 | `hasWeaponEquipped` |
| 157 | `hasWeaponEquipped` |
| 158 | `iAmBeingMeleeAttacked immediate` |
| 159 | `iAmBeingMeleeAttacked general` |
| 160 | `isWithinMyBowRange` |
| 161 | `hasTwoWorkingArm` |
| 162 | `iAmProne` |
| 163 | `isBuildingDestroyed` |
| 164 | `INTRUDER_IS_OUTSIDE_GATES` |
| 165 | `crowdLimit8` |
| 166 | `iHaveSufficientLockSkill` |
| 167 | `iHaveSufficientLockSkillForShackles` |
| 168 | `DESTINATION_IS_ACCESSIBLE_ANIMAL` |
| 169 | `isMachineInputInvalid` |

## Unknowns

- Why `taskType` 289 and 290 exist without an enum name (they build `Task_Follow` and `Task_BashDoor`; row 290 of the TaskData table repeats the data of `BASH_GATE`).
- What the 37 named types without a TaskData row do when an AI_TASK record uses them (the list is in the TaskData registry section), and whether any behaviour of the 147 "goal" types (factory default `Task_Blank`) lives outside this factory.
- What lists A and B of a TaskData row mean exactly, what the `*` marker on an entry (second flag byte) means, and what the registration flag bits (0x1, 0x21, 0x41, 0x55, 0x61, 0x63, 0x77, ...) select; [ai.md](ai.md#unknowns) lists the six category sets they register into.
- What the 18 StateType ids with no registration call and the two ids registered with an empty name (33 and 85) evaluate, and the semantics of most evaluators beyond their names.
- How the effect column (`!` prefix, single effect per row) is used by the planner: the planner search itself is **Unknown** ([ai.md](ai.md#packages-and-goals)).
