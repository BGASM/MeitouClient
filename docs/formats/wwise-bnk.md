# Wwise sound banks (`.bnk`)

Kenshi's audio is 24 Wwise sound banks in `data/audio/` (388 MB). There are no loose `.wem` or `.pck` files: every sound the game plays
is embedded in a bank. This page is the clean-room layout, worked out by probing the 24 banks (hex dumps, then a reader that has to
consume every object to its exact declared size) plus the names in `kenshi_x64.exe`'s string table. No Wwise SDK code or third-party
code was used. Reader: `Meitou.Data.Audio.WwiseBank`. Dump tool: `meitou-tools audio [bank] [--media]`. Tests:
`tests/Meitou.Tests/Audio/WwiseBank*Tests.cs`.

## The banks

| Bank | Version | Contents |
|---|---|---|
| `Init.bnk` | 112 | Buses (9), effects (6), aux buses (2); sections `STMG` and `ENVS` (not decoded) |
| `Reverb_Indoor_Bank`, `Reverb_Outdoor_Bank` | 112 | One bus, effect share set, effects, aux bus each |
| `SFX_Amb_<Biome>` ×10 | 112 | Ambience beds of Ashlands, Boneyard, Canyons, Coast, Desert, Forest, Jungle, Plains, Swamplands, Tarsands |
| `SFX_Amb_Indoors_Bank` | 112 | Interior ambience (`Enter_Building`, `Enter_Building_Big`, `Leave_Building`) |
| `SFX_Amb_Weather_Bank` | 112 | Rain and wind beds |
| `<Biome>_Music_Bank` ×5, `MT_Music_Bank`, `Radio_Music_Bank` | 112 | Interactive music (Dunes, Savannah, Swamplands, Tarsands; the main theme; the radio) |
| `SFX_General_Bank` | 112 | UI, footsteps, combat, building (855 sounds, 88 events) |
| `VO_Bank` | 112 | Voice (465 sounds) |
| `SFX_Bank` | **88** | An older bank (981 sounds, 130 events) in an older layout; only its sections and object headers are read |

**Verified** (`Every_bank_is_version_112_except_SFX_Bank...`): the versions above, and every bank's id is the name hash (below) of its
file name. Savannah and Dunes have music banks but no ambience bank (the exe's biome list has 12 names: the 10 above plus these two).

## File layout

Little-endian. A bank is a sequence of sections: 4-byte ASCII tag, u32 body length, body. `BKHD` comes first. Unknown tags are skipped
by length. **Verified** for all 24 banks (the sections tile the file exactly).

| Tag | Body |
|---|---|
| `BKHD` | u32 version (112 or 88), u32 bank id, u32 language id (0 in every bank), u32 0, u32 `0x187` (Unknown), then 0 to 12 zero bytes (the section is 20 to 32 bytes) |
| `DIDX` | Media index: 12 bytes per embedded WEM: u32 source id, u32 offset into `DATA`, u32 length |
| `DATA` | The WEM files back to back, at the `DIDX` offsets |
| `HIRC` | The object hierarchy (below) |
| `STID` | Bank names: u32 string type (1), u32 count, per entry u32 bank id, u8 length, ASCII name. Lists the bank itself, and in `MT_Music_Bank` also `SFX_General_Bank` (which its event plays from). **Verified** |
| `STMG`, `ENVS` | Only in `Init.bnk`: global settings (state groups, switch groups, game parameters) and environment curves. **Unknown**, not decoded |

## Ids and names

Every id that has a name in the Wwise project (banks, events, game parameters, state groups, states, switches) is the 32-bit **FNV-1**
hash (not 1a) of the lowercased name: start at 2166136261, per byte multiply by 16777619 and then XOR the byte. **Verified**: all 24
bank ids match their file names, and the event names below all match objects in the expected bank. Other objects (sounds, containers,
actions) have ids that are not name hashes, and banks store no names except `STID`.

Names found (from the exe's strings, combined as the exe's fragments suggest; **Verified** by hash):

- Ambience, per biome bank: `Enter_<Biome>_Day`, `Enter_<Biome>_Night`, `Leave_<Biome>` (exactly these three events in each of the 10).
- Interiors: `Enter_Building`, `Enter_Building_Big`, `Leave_Building`.
- Weather: `Weather_Rain_Start`, `Weather_Rain_End`, `Weather_Wind_Start`, and **`Weather_Wind_End`**. The exe's string table holds
  `Weather_Wind_Stop`, which is no event in any bank (**Verified** by hash); if the game posts that name ([weather.md](weather.md#sounds-verified-decompiled-fun_1409e8f70-names-only)),
  the wind bed is never stopped by it. **Observed**, not tested in the game.
- Music: `Music_<Biome>_Play` / `Music_<Biome>_Stop` (Dunes, Savannah, Swamplands, Tarsands), `Create_or_Load_Game`.
- State group `Time_in_Game` with states `Day`, `night`, `none` (used by switch containers in Ashlands and Coast); interior switch `Wicker`.
- General: `Start_Emitter`, `Stop_Emitter`, `Footsteps`, `Door_Open`, `Map_Open`, UI clicks and many more in `SFX_General_Bank`.
- **Unknown**: game parameter `5FE07BCE`, the one RTPC and layer-crossfade parameter every ambience and weather bank uses (not
  `Intensity`, `Rain_Intensity`, `Wind`, `Distance` or the time-of-day names tried). Neither `Intensity` nor `Rain_Intensity`, which the
  exe sets for weather, drives anything in the decoded objects (buses in `Init.bnk` are not decoded, so they may).

## HIRC

u32 object count, then per object: u8 type, u32 size, then `size` bytes beginning with the u32 object id. **Verified**: in every bank
the objects tile the section exactly.

Types (the format's numbers): 1 state, 2 sound, 3 action, 4 event, 5 random/sequence container, 6 switch container, 7 actor-mixer,
8 bus, 9 layer (blend) container, 10 music segment, 11 music track, 12 music switch, 13 music random/sequence, 14 attenuation,
18 effect share set, 19 effect, 20 aux bus. The reader decodes 2, 3, 4, 5, 6, 7 and 9 in version 112 banks and keeps the rest raw.

**Verified** (`Every_version_112_object_of_a_decoded_type_uses_exactly_its_size`): with the layouts below, all 2,492 objects of those types
in the 22 version-112 banks decode to exactly their size. The field order is certain; the meaning of a field is **Observed** unless
said otherwise.

### Shared pieces

- **Property list**: u8 count, that many u8 property ids, then a u32 value each (float or int by property); then u8 count, the ids,
  and a (f32 min, f32 max) random range each.
- **RTPC list**: u16 count; per entry u32 game parameter id, u8 type (0), u8 accumulation (1), u8 target property (0 volume, 2 pitch,
  3 low-pass), u32 curve id, u8 scaling (2 = dB for volume curves, 0 otherwise), u16 point count, points of (f32 x, f32 y, u32 interpolation).

### Event (4)

u32 action count, the action ids in order.

### Action (3)

u16 action type, u32 target id, u8 target-is-bus, property list, then by kind (high byte of the type). The low byte is the scope
(Observed: 1/2 one object, 3/4 all, where 2 and 4 are limited to the posting game object). Kinds seen in version 112 banks:

| Kind | Name | Then |
|---|---|---|
| 0x01 | Stop | u8 fade curve, u32 exception count, exceptions (u32 id, u8 is-bus) |
| 0x04 | Play | u8 fade curve, u32 bank id (the target's bank) |
| 0x08 / 0x09 | Set / reset pitch | u8 fade curve, u8 value meaning (1 offset, 2 absolute), f32 value, f32 min, f32 max, exceptions as Stop |
| 0x0A / 0x0B | Set / reset volume | as pitch |
| 0x0C / 0x0D | Set / reset bus volume | as pitch |
| 0x0E / 0x0F | Set / reset low-pass | as pitch |
| 0x12 | Set state | u32 state group, u32 state |
| 0x1A | Bypass effects | u8 bypass, u8 slot mask, exceptions as Stop |

Action properties seen: `0x0F` (int ms, 1000 to 10000 on stops and plays: the fade time) and `0x0E` (int ms, 6000; probably a delay).

### Sound (2)

- Source: u32 plugin (`0x00040001` Wwise Vorbis for every real sound; `0x00650002` the silence source, 41 in `SFX_General_Bank`),
  u8 stream type (0 in every sound: embedded), u32 source id (the `DIDX` id), u32 file id (the bank's id), u32 offset, u32 size,
  u8 source bits (0). A source plugin (type nibble 2) adds u32 parameter size and the parameters (size 0 for every silence source).
  **Verified**: the offset is the WEM's offset **in the bank file** (not in `DATA`), and offset and size equal its `DIDX` entry; every
  Vorbis sound's media is in its own bank.
- Then the node base (below).

### Node base (sounds and containers)

1. u8 override parent effects, u8 effect count; if any: u8 bypass bits, then per effect u8 slot, u32 effect id, u8 is share set, u8 is rendered.
2. u8 **Unknown** (0 in every object).
3. u32 override bus id, u32 parent id (the actor-mixer or container above; 0 at the top).
4. u8 priority bits (0 everywhere).
5. Property list. Node properties seen: `0x00` volume (dB), `0x02` pitch (cents), `0x03` low-pass (0..100), `0x3A` loop count
   (int; 0 = forever on looping beds), `0x0D` (100 on mixers and some sounds, Unknown), `0x12` (-3, once), `0x3B` (0.15, Unknown).
   Ranges seen on volume, pitch and low-pass: each play adds a random offset in the range.
6. Positioning: u8 bits. Bit 0 = overrides the parent's positioning. If bit 0 and bit 3 (3D) are set: u8 3D bits, u32 attenuation id;
   and if the 3D type (bits 5-6) is 3: u8 path mode, s32 transition time (ms), u32 vertex count, vertices (f32 x, y, z, s32 duration ms),
   u32 path count, paths (u32 first vertex, u32 vertex count), then per path three floats (random range in x, y, z). Values seen:
   `C0` (no override), `C3` (override, 2D), `C9`/`D9` (3D), `79`/`F9` (3D with paths: the random one-shots of the ambience move along
   paths around the listener).
7. Aux sends: u8 bits; bit 3 set adds four u32 aux bus ids.
8. Advanced settings: u8 bits, u8 virtual voice behaviour (1), u16 max instances, u8 below-threshold behaviour, u8 bits.
9. States: u32 group count; per group u32 group id, u8 sync type, u16 count, (u32 state id, u32 state object id) pairs.
10. RTPC list.

### Containers

- **Actor-mixer (7)**: node base, u32 child count, child ids.
- **Random/sequence (5)**: node base, u16 loop count (0 forever), u16 loop min, u16 loop max, f32 transition time (ms), f32 transition
  min, f32 transition max, u16 avoid-repeat count, u8 transition mode, u8 random mode, u8 mode (0 random, 1 sequence), u8 flags
  (`0x12` or `0x1A`), u32 child count and ids, u16 playlist count, playlist (u32 child id, s32 weight; 50000 each = equal).
- **Switch (6)**: node base, u8 group type (1 = state group), u32 group id, u32 default switch, u8 continuous validation, u32 child count
  and ids, u32 switch count, per switch u32 switch id + u32 count + node ids, u32 node-parameter count, per node u32 id, u8 bits, u8 bits,
  s32 fade out, s32 fade in.
- **Layer (9)**: node base, u32 child count and ids, u32 layer count, per layer u32 layer id, RTPC list, u32 crossfade game parameter,
  u8 its type, u32 association count, per association u32 child id, u32 point count, points (f32, f32, u32).

## How an ambience bank is built (Observed, e.g. `SFX_Amb_Desert`)

`Enter_Desert_Day` = stop all of `3A626E51` (an object in no bank: probably the outer ambience mixer in `Init.bnk`'s hierarchy, Unknown),
play a **layer container** holding one looping sound (a long stereo bed, `loop=0`), play a **random container** of short one-shots
(random volume -55..0 dB and pitch, positioned 3D along paths around the listener, transition 1000 ms, avoid repeating the last 4-6),
stop the night versions, and turn an effect bypass on a bus off. `Enter_Desert_Night` is the same with the night bed and one-shots.
All of these fade over 10 s (`0x0F` = 10000). `Leave_Desert` stops the biome's top mixer. Ashlands and Coast also have switch
containers on `Time_in_Game` (`Day` / `night` / `none`). The weather bank: `Weather_Rain_Start` plays a layer container of three looping
rain beds plus a random container of extra layers; `Weather_Wind_Start` plays two looping wind beds; the ends stop their mixers.

## Embedded media (WEM)

A WEM is RIFF/WAVE. **Verified** for the ambience banks (`Ambience_WEMs_are_Wwise_Vorbis...`): a 66-byte `fmt ` chunk with codec tag
`0xFFFF` (Wwise Vorbis), then directly a `data` chunk: there is no separate `vorb` chunk, so the Vorbis setup values are in the extended
`fmt `. The 292 ambience WEMs are 44100 Hz, 252 stereo and 40 mono (Observed with `meitou-tools audio <bank> --media`). Decoding needs the Wwise Vorbis packet layout and its codebook
references, **Unknown**, next step.

## Version 88 (`SFX_Bank`)

The sections and object headers read the same way. The object bodies differ: e.g. a sound's source has a u32 where version 112 has
the u8 stream type (the source id is 3 bytes later). Not decoded; **Unknown**.
