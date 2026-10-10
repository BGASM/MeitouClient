# Wwise sound banks (`.bnk`)

Kenshi's audio is 24 Wwise sound banks in `data/audio/` (388 MB). There are no loose `.wem` or `.pck` files: every sound the game plays
is embedded in a bank. This page is the clean-room layout, worked out by probing the 24 banks (hex dumps, then a reader that has to
consume every object to its exact declared size) plus the names in `kenshi_x64.exe`'s string table. No Wwise SDK code or third-party
code was used. Reader: `Meitou.Data.Audio.WwiseBank`. Dump tool: `meitou-tools audio [bank] [--media]`. Tests:
`tests/Meitou.Tests/Audio/WwiseBank*Tests.cs`.

## The banks

| Bank | Version | Contents |
|---|---|---|
| `Init.bnk` | 112 | Buses (9), effects (6), aux buses (2); global settings `STMG` (decoded) and `ENVS` (not decoded) |
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
| `STMG` | Only in `Init.bnk`: global settings, see [below](#global-settings-stmg) |
| `ENVS` | Only in `Init.bnk`: environment (obstruction / occlusion) curves. **Unknown**, not decoded |

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
- Game parameters (the 24 in `STMG`): `Camera_Zoom` (`5FE07BCE`, the one RTPC and layer-crossfade parameter of every ambience and weather
  bank), `Time_in_Game`, `User_Volume_Ambient_SFX`, `User_Volume_Music`, `User_Volume_SFX`, `User_Volume_VO`, `User_Volume_UI`,
  `Footstep_Volume`, `Building_Integrity`, `Intensity`; what the game sets them to is in [audio.md](../audio.md#game-parameters-and-states).
  `Rain_Intensity`, which the exe sets, is not among them: setting it does nothing. 14 of the 24 are not named.

## HIRC

u32 object count, then per object: u8 type, u32 size, then `size` bytes beginning with the u32 object id. **Verified**: in every bank
the objects tile the section exactly.

Types (the format's numbers): 1 state, 2 sound, 3 action, 4 event, 5 random/sequence container, 6 switch container, 7 actor-mixer,
8 bus, 9 layer (blend) container, 10 music segment, 11 music track, 12 music switch, 13 music random/sequence, 14 attenuation,
18 effect share set, 19 effect, 20 aux bus. The reader decodes 2, 3, 4, 5, 6, 7 and 9 in version 112 banks, buses (8) in part ([below](#buses)), and keeps the rest raw.

**Verified** (`Every_version_112_object_of_a_decoded_type_uses_exactly_its_size`): with the layouts below, all 2,492 objects of those types
in the 22 version-112 banks decode to exactly their size. The field order is certain; the meaning of a field is **Observed** unless
said otherwise.

### Shared pieces

- **Property list**: u8 count, that many u8 property ids, then a u32 value each (float or int by property); then u8 count, the ids,
  and a (f32 min, f32 max) random range each.
- **RTPC list**: u16 count; per entry u32 game parameter id, u8 type (0), u8 accumulation (1), u8 target property (0 volume, 2 pitch,
  3 low-pass), u32 curve id, u8 scaling (2 = dB for volume curves, 0 otherwise), u16 point count, points of (f32 x, f32 y, u32 interpolation:
  4 linear, 9 constant and others seen, the Wwise curve shapes). **Observed**: the y of all 111 volume curves (scaling 2) lies in −1 .. +0.08
  while pitch curves are in cents and low-pass curves in its units; read as linear gain − 1 (−1 silent, 0 unchanged) every one makes
  sense (the slider curves are silent at 0 and unchanged at 100; [audio.md](../audio.md#volume-curves-observed)). Layer crossfade curves
  are plain linear gains (all 1 in Kenshi).

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

## Global settings (`STMG`)

In `Init.bnk` only. f32 volume threshold (−80), u16 maximum voices (256), u32 state group count, per group u32 id, u32 default transition
(ms), u32 count and (u32 from, u32 to, u32 ms) transitions; u32 switch group count, per group u32 id, u32 game parameter, u32 count and
(f32, u32, u32) points; u32 game parameter count, per parameter u32 id, f32 **default value**, u32 ramp type, f32 ramp up, f32 ramp down,
u8 built-in. **Verified** by size (the 546 bytes are used exactly; the switch group entry layout is a guess, Kenshi has none). Kenshi: two
state groups (`Time_in_Game` 1000 ms, `C2E2475B` 500 ms) and 24 game parameters, `Camera_Zoom` 50, `Time_in_Game` 5, the user volumes 50
or 100 ([audio.md](../audio.md#game-parameters-and-states)).

## Buses

A bus (type 8) starts with u32 parent bus (0 for the two top buses), then a property list without ranges: ids `1A`, `1B`, `1C`, `1F`
(values 0, 100, 0, 100 on every bus; Unknown) and on three buses `05`, the **bus volume** in dB (+6 footsteps, −4 music, −9 on the aux
bus `5E1D37D4`). The rest of the layout is not decoded; the reader finds the bus's RTPC list by its shape (a u16 count of 1..8 followed by
well-formed entries), which gives each category bus its slider curve (**Observed**, the curves are listed in [audio.md](../audio.md#volumes)).
Sounds reach a bus through the nearest node with an override bus id (in the biome banks the top actor-mixer goes to the SFX bus
`59927CD0` and the one below it to the ambience bus `95836E4C`).

## How an ambience bank is built (Observed, e.g. `SFX_Amb_Desert`)

`Enter_Desert_Day` = stop all of `3A626E51` (the main theme: the music random/sequence container of `MT_Music_Bank`),
play a **layer container** holding one looping sound (a long stereo bed, `loop=0`), play a **random container** of short one-shots
(random volume -55..0 dB and pitch, positioned 3D along paths around the listener, transition 1000 ms, avoid repeating the last 4-6),
stop the night versions, and set the effect bypass of aux bus `5E1D37D4` (the outdoor reverb, in `Init.bnk` and `Reverb_Outdoor_Bank`). `Enter_Desert_Night` is the same with the night bed and one-shots.
All of these fade over 10 s (`0x0F` = 10000). `Leave_Desert` stops the biome's top mixer. Ashlands and Coast also have switch
containers on `Time_in_Game` (`Day` / `night` / `none`). The weather bank: `Weather_Rain_Start` plays a layer container of three looping
rain beds plus a random container of extra layers; `Weather_Wind_Start` plays two looping wind beds; the ends stop their mixers.

## Embedded media (WEM)

A WEM is RIFF/WAVE. **Verified** for the ambience banks (`Ambience_WEMs_are_Wwise_Vorbis...`): a 66-byte `fmt ` chunk with codec tag
`0xFFFF` (Wwise Vorbis), then directly a `data` chunk: there is no separate `vorb` chunk, so the Vorbis setup values are in the extended
`fmt `. The 292 ambience WEMs are 44100 Hz, 252 stereo and 40 mono (Observed with `meitou-tools audio <bank> --media`). Three tiny
ones in `SFX_Amb_Indoors_Bank` (`12C0F32E`, `0888326E`, `34EC0D31`, 461 to 1625 bytes) are encoded silence of 1, 5 and 10 s, the
gaps of the interior random containers (**Verified**: they decode to exact zeros).

## Wwise Vorbis

Reader: `Meitou.Data.Audio.WwiseVorbis` (WEM → standard Ogg Vorbis, which NVorbis, MIT, decodes). `meitou-tools audio <bank> --decode`
decodes a bank, `--wav <folder> [id]` writes WAV files.

**Verified**: every WEM in all 24 banks (2,786, about 5.5 hours, including `SFX_Bank`'s media) converts and decodes to exactly its sample
count (`--decode` over each bank; the ambience banks in `Every_ambience_WEM_decodes_to_exactly_its_sample_count`). A wrong setup layout
or codebook would make the decoder reject the stream; this does not show whether the window flags are right (see Audio packets).
Speed: about 200× real time on one core (Release).

### The `fmt ` chunk (66 bytes)

| Offset in `fmt ` | Field |
|---|---|
| 0x00 | u16 codec `0xFFFF` |
| 0x02 | u16 channels |
| 0x04 | u32 sample rate |
| 0x08 | u32 average bytes per second (× 8 is the nominal bitrate of the rebuilt identification header) |
| 0x0C, 0x0E | u16 block align, u16 bits per sample: 0 |
| 0x10 | u16 extra size: 48 |
| 0x12 | u16 0 |
| 0x14 | u32 channel mask (3 for stereo) |
| 0x18 | u32 **sample count** (frames the sound plays) |
| 0x1C | u32, equal to the first audio packet offset in the files looked at (Unknown) |
| 0x20 | u32, equal to the `data` chunk length in the files looked at (Unknown) |
| 0x24 | u32 (Unknown) |
| 0x28 | u32 **setup packet offset** in `data` (0) |
| 0x2C | u32 **first audio packet offset** in `data` |
| 0x30 .. 0x3B | three u32 (Unknown; perhaps packet size bounds) |
| 0x3C | u32 uid (the rebuilt stream's Ogg serial) |
| 0x40 | u8 log2 of the short block size (8: 256) |
| 0x41 | u8 log2 of the long block size (11: 2048) |

### Packets

The `data` chunk holds packets, each a u16 size and then the packet, with no granule positions. The first is the setup packet, the rest
are audio. There is no identification or comment packet: the converter writes both (from channels, rate, bitrate and block sizes).

### The setup packet

Bit-packed as Vorbis (least significant bit first): a shortened Vorbis setup header (**Verified**: rebuilt as below, every setup packet
in every bank is used up to within its last byte, which the converter checks, and the result decodes):

- No packet type and `vorbis` signature.
- u8 codebook count - 1, then per codebook a **10-bit index into the codebook library** (below) instead of the codebook.
- No time-domain transform list (the converter writes one entry of type 0).
- Floors: 6-bit count - 1, per floor the floor-1 fields of Vorbis without the 16-bit floor type (all are type 1).
- Residues: 6-bit count - 1, per residue a **2-bit** residue type instead of 16 bits, then the Vorbis fields.
- Mappings: 6-bit count - 1, per mapping no 16-bit type (all type 0), then the Vorbis fields.
- Modes: 6-bit count - 1, per mode a 1-bit block flag, no window or transform type (both 0), the 8-bit mapping.
- No framing bit.

### Packed codebooks

A library entry is a codebook in a packed form of the Vorbis layout (**Verified**: all 598 parse and fill their span as below, and the
expanded codebooks decode):

- No `BCV` sync. u4 dimensions (Vorbis: 16 bits), u14 entries (Vorbis: 24), u1 ordered.
- Ordered: u5 initial length - 1, then the Vorbis run counts.
- Not ordered: u3 bits per codeword length (1..5), u1 sparse, then per entry (after a u1 present flag when sparse) the length - 1 in that
  many bits (Vorbis: 5 bits).
- u1 lookup type (Vorbis: 4 bits; so only 0 and 1). Type 1: u32 minimum, u32 delta, u4 value bits - 1, u1 sequence, then the
  `lookup1_values` multiplicands as in Vorbis.
- Each takes `bits / 8 + 1` bytes: a whole extra byte when it ends on a byte boundary.

### The codebook library

The library is part of the game's Wwise decoder in `kenshi_x64.exe`: a table of 599 increasing 64-bit pointers in `.data` (598
codebooks and the end) into one block of packed codebooks in `.rdata` (about 72 KB). `WwiseCodebookLibrary.FromExecutable` finds it by
that shape (a run of 200+ increasing pointers at most 8000 bytes apart) and accepts it only if every entry parses to exactly its span.
So it is read from the user's install, never shipped, and no exe build's offsets are hardcoded. **Verified** on the Steam exe
(36,718,592 bytes): one such table, 598 codebooks (`The_exe_holds_a_library_of_598_packed_codebooks`).

### Audio packets

A Vorbis audio packet without its first bit (the packet type, 0) and, for a long block, without the previous/next window flags. The
converter writes the type bit, copies the mode number (`ilog(modes - 1)` bits), for a long block writes the flags from the neighbouring
packets' modes (previous = the previous packet is a long block, next = the next one is), then copies the rest. Granule positions are
rebuilt from the block sizes (each packet after the first adds a quarter of its own and of the previous block size), and the last page
gets the sample count so the decoder trims the end.

The flags follow the Vorbis definition (the encoder writes exactly the neighbours' block types), but no signal test singles them out:
at long/short transitions, forcing every flag to "short" gives identical output (both reconstruct completely there), while forcing them
to "long" or swapping them changes it, and a mismatched window both attenuates and aliases, so energy measures do not decide.
**Observed**; to be confirmed by listening.

## Version 88 (`SFX_Bank`)

The sections and object headers read the same way. The object bodies differ: e.g. a sound's source has a u32 where version 112 has
the u8 stream type (the source id is 3 bytes later). Not decoded; **Unknown**.
