# Audio

How Kenshi drives its sound, and how Meitou plays it. The bank and WEM formats are in [formats/wwise-bnk.md](formats/wwise-bnk.md); this
page is the runtime: what the game sends to Wwise, and Meitou's player (`src/Meitou.Audio`). Only the ambience (biome beds, weather beds)
plays so far; music, interiors, effects' and objects' emitters, footsteps, combat, UI and voices do not.

The facts about the exe come from its decompiled camera update and audio manager, traced 2026-10-10 (functions named per claim). Facts
about the banks are checked against the 24 banks of the Steam install.

## The pieces, simply

- **Banks** hold the recordings (Wwise Vorbis WEMs) and the instructions (events, containers, curves). See the format doc.
- **The game** only posts **event names** (`Enter_Desert_Day`), sets **game parameters** (numbers like `Camera_Zoom`) and **states**
  (`Time_in_Game` = `Day`); Wwise does the rest from the bank.
- **Meitou** reads the banks, converts the recordings to Ogg Vorbis and decodes them as they play (NVorbis), interprets the instructions
  in its own player, mixes everything in software to one stereo stream and hands it to the sound card through OpenAL Soft.

## What the game sends (Verified (decompiled) unless marked)

The ambience is run each frame from the camera update (strategy camera `FUN_1406b0f90`, free camera `FUN_1406b0960`, both ending in
`FUN_1406b0140`); the audio manager is the global `AudioSystemGlobal` object. The camera has a **pivot** node and an **eye** node (a child
of the pivot at the zoom distance).

### Biome ambience

- The biome is the BIOME_GROUP (weather region, `areasmap.tga`) **under the pivot**; its `ambience` field (enum `Ambience`: Ashlands,
  Boneyard, Canyons, Coast, Desert, Forest, Jungle, Plains, Swamplands, Tarsands; default Desert) names the bank `SFX_Amb_<name>`. Its
  `music` field (enum `BiomeMusic`: Dunes, Savanah, Swamp, Tarsands, None) picks the music in the same step. Base game: 74 regions; Plains
  15, Desert 14, Canyons 12, Coast 10, Boneyard 7, Tarsands 5, Forest 4, Swamplands 3, Ashlands 2, Jungle 2 (Verified, records).
- **Day** is `sunrise < hour < sunset` strictly (GLOBAL CONSTANTS, 5 and 23; `FUN_14066cb20`), otherwise night.
- Each frame the game compares one number, the biome index with bit 8 set at night, with the last (−1 at start). No delay, no hysteresis.
  On a change: if the biome changed (not only the time), `Leave_<OldBiome>`; then `Enter_<Biome>_Day` or `Enter_<Biome>_Night`. So day to
  night inside a biome re-posts `Enter_` with the other suffix and no `Leave_`; the night event's actions stop the day beds (10 s fade)
  and start the night ones.
- When the biome's bank is not loaded yet, the game loads it first and posts `Enter_` afterwards. **Bug in the game**: on that delayed path
  the suffix is inverted (night posts `_Day`), until the next change. Banks of biomes left behind are unloaded 60 s after they were last
  current (`FUN_140258e30`).
- Events go to one game object (100); the listener is the camera **eye** (with a broken orientation: the camera's up vector ends in the
  front slot, so 3D panning in the game is not what the camera faces; Verified by disassembly).
- The weather bank, the interior bank, Init, the main theme, the general sounds and the voices are loaded at start.

### Game parameters and states

| Name | Id | Value | Default (Init.bnk) |
|---|---|---|---|
| `Camera_Zoom` | `5FE07BCE` | (eye height − ground height under the eye) / 2000 × 100, each frame; not clamped; not set where there is no ground | 50 |
| `Time_in_Game` (parameter) | `5DBE7335` | **not the clock**: (real seconds mod 600) / 60, a 0..10 sawtooth every 10 real minutes, each frame | 5 |
| `Time_in_Game` (state group) | `5DBE7335` | `Day` / `Night`, set when day flips (`FUN_14066ff60`; seeded opposite so it fires once at start) | — (1000 ms transitions) |
| `User_Volume_Ambient_SFX`, `_Music`, `_SFX`, `_VO`, `_UI`, `Footstep_Volume` | | the settings.cfg sliders (`Ambient volume` …) × 100, at start and when the options close | 50 (Music, SFX, VO 100) |
| `Building_Integrity` | `00C01EF9` | the building's `interior integrity` (0..100) on entering it | 50 |
| `Intensity` | `933E38F4` | per emitter: input × scale + offset; the weather sets wind intensity × wind speed × 0.01 | 50 |
| `Rain_Intensity` | `9637D55D` | the rain level 0..100 ([weather.md](formats/weather.md)) — **not a game parameter in any bank**, so it does nothing | — |

`Camera_Zoom` is the parameter every ambience and weather bank uses: the beds' layers and the one-shot containers have volume curves on
it, so the critters fade out as the camera rises (0 at the ground, 100 at 2000 units up).

### Interiors (not played yet)

Each frame a ray straight down from (pivot x, 40000, pivot z) finds the building under the pivot. When it changes: `Leave_Building` for the
old one, then, if the new building's `interior ambience` (`InteriorSound`: None, Metal, Stone, Wicker, Wood) is not None, the switch
`Indoors` = that name, `Building_Integrity` = its `interior integrity`, and `Enter_Building`, or `Enter_Building_Big` when `cavernous`.

### Weather

`Weather_Rain_Start` / `Weather_Rain_End` when the rain level becomes non-zero / zero; `Weather_Wind_Start` when the wind starts. The game
posts `Weather_Wind_Stop` to stop it, which no bank has (the bank's event is `Weather_Wind_End`), so in the game the wind bed never stops
once started (Observed, by the hashes; not heard in the game).

### Volumes

The six sliders become the `User_Volume_*` game parameters × 100. Each category's **bus** in `Init.bnk` turns its parameter into a gain
through a curve: Ambience (`95836E4C`): silent up to 5, half at 65, full at 100, so the default 0.5 plays at gain 0.375 (−8.5 dB). The
other buses are alike (SFX, UI: half at 65; Music: half at 50 and −4 dB bus volume; VO: linear; footsteps +6 dB bus volume, unity at 50 rising to 1.5× at 100).

## Meitou's player

`src/Meitou.Audio`: `AudioSystem` wires these together for a host (the viewer); any missing part (no exe library, no device) leaves the
host running without sound.

- `WwiseCodebookLibrary.Load` reads the codebook library from `%LOCALAPPDATA%\Meitou\audio\codebooks.bin` (72 KB), made from the exe the
  first time and remade when the exe's size or date changes (54 ms from the exe, 14 ms from the cache).
- `WwisePlayer` loads a bank on a worker (reads it, converts every WEM to Ogg Vorbis in parallel: about 130 ms for a biome bank) and then
  hands it to the audio thread. Events wait (up to 10 s) for their bank. It interprets:
  - **Actions**: play (with the action's fade-in), stop (every playing instance whose node is the target or below it, with the fade),
    set state, set/reset volume, pitch and low-pass offsets, the delay property. Effect bypass is ignored.
  - **Sounds** loop by their loop property (0 forever). **Random/sequence containers**: continuous ones (flags bit 3) play one child after
    another (back to back; transition mode 3 waits the transition time), forever or for the loop count; step ones play one child. Random
    picks by weight, skipping the last *avoid repeat* picks. **Layer containers** play all children, each scaled by its layer's crossfade
    curve. **Switch containers** play the children of the current state and swap them with their fades when it changes.
  - **Gains** add up from the sound to the top of the hierarchy: volume properties (dB), a random roll in each node's ranges per play,
    RTPC curves of the game parameters, then the output bus and its parents (bus volume, the slider curves). Volume curves store y as
    linear gain − 1 (below). Pitch (cents) resamples; low-pass runs a one-pole filter.
  - **3D paths**: a positioned container's one-shots pick a random path and move along its vertices (listener-relative x, z); the player
    pans them left/right by direction (equal power). No distance attenuation (the ambience has attenuation 0), no reverb.
- `AudioOutput` keeps four blocks of 1024 frames (23 ms each at 44100 Hz) queued on one OpenAL Soft streaming source, refilled by a thread.
- `AmbienceDirector` is the game's rules above: biome under the pivot, day/night, `Camera_Zoom`, `Time_in_Game`, rain and wind. Two
  differences from the game: the delayed-load suffix bug is not copied (the right event always plays), and the wind is stopped with
  `Weather_Wind_End`.

### Volume curves (Observed)

All 111 volume RTPC curves in the version-112 banks (scaling 2, "dB") have y between −1 and +0.08, while pitch curves are in cents and
low-pass curves in its units. Read as **linear gain − 1** (−1 silent, 0 unchanged), they make sense everywhere: the sliders' curves are
silent at 0 and unchanged at 100, the critters fade out with height. The player multiplies the gain by 1 + y. Not checked against Wwise
itself.

### Levels (Observed)

The ambience is quiet: the Desert bed is mastered at −45 dBFS RMS (the general sound effects at −25 to −33), its layer container takes
12 dB, the slider's default another 8.5, so the desert by day mixes at about −66 dBFS RMS. That is what the banks say; whether the game
sounds the same was not measured. The viewer's `--audio-gain <dB>` raises the whole mix.

### Not done

Music (`Music_<Biome>_Play`, music segments, tracks and switches are not decoded), interiors (needs the building raycast), point emitters
(`Start_Emitter` / `Stop_Emitter` with the `Emitters` switch, AMBIENT_SOUND records), effects and reverb (aux buses), attenuation curves,
voice limits, and unloading banks left behind (they stay loaded: about 5 to 14 MB of Ogg each).

## In the viewer

On by default; `--no-audio` turns it off, `--audio-gain <dB>` raises or lowers everything. The console prints the device; F11's
statistics show the biome, day or night, rain, wind and the number of voices. Ambient volume comes from the install's `settings.cfg`.

## Tools

- `meitou-tools audio` surveys the banks; `audio <bank>` dumps a bank's events and tree; `--decode` decodes its WEMs; `--wav <folder> [id]`
  writes them as WAV.
- `meitou-tools audio render <seconds> <out.wav> [Bank.bnk …] [Event …] [Parameter=value …] [Group:State …]` mixes events offline (no
  sound card) and prints the level each second, e.g.
  `audio render 60 desert.wav SFX_Amb_Desert.bnk Camera_Zoom=5 Time_in_Game:Day Enter_Desert_Day`.
