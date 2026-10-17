# Audio

How Kenshi drives its sound, and how Meitou plays it. The bank and WEM formats are in [formats/wwise-bnk.md](formats/wwise-bnk.md); this
page is the runtime: what the game sends to Wwise, and Meitou's player (`src/Meitou.Audio`). Only the ambience (biome beds, weather beds) and the music
play so far (in the viewer and the game); interiors, effects' and objects' emitters, footsteps, combat, UI and voices do not.

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
- The banks loaded at start are listed under [Music](#music).

### Music

Traced 2026-10-17 from the audio manager (`AudioSystemGlobal`: biome music `FUN_1402587a0`, bank pass `vfunc_2` `140259130`, tick
`vfunc_4` `140258e30`, init `FUN_140259620`). Event and node ids checked by hashing the names against the banks' events.

- **Which music.** In the same camera step as the ambience (only when a region is found under the pivot) the region's `music` index goes
  to `FUN_1402587a0`; an index outside 0..4 counts as 4. The names come from the exe's own table (`FUN_14025e170`): 0 `Dunes`, 1
  `Savannah`, 2 `Swamplands`, 3 `Tarsands`, 4 = no music (the FCS labels `Savanah` / `Swamp` play no part). Day and night do not matter.
- **On a change** (nothing happens if the index is the same): if the old index was −1 (the first call after start), `MT_Music_Bank` is
  **unloaded**. For 0..3 with the bank `<Name>_Music_Bank` loaded: switch `Biomes` = `<Name>` on object 100, and the id of
  `Music_<Name>_Play` is remembered. Bank not loaded: it is queued for loading, and when it is loaded the switch is set and the id
  remembered. Index 4: the id is cleared. **Nothing is posted on a change**: no `_Play`, and the game never posts `Music_<Name>_Stop` (the
  exe only ever builds `Music_` + name + `_Play`; the four `_Stop` events in the banks are unused).
- **Only a timer starts music** (`vfunc_4`, every frame, skipped entirely while the title screen exists, `DAT_142133320`): when the clock
  passes the deadline, the next deadline is now + F × (0.8 + 0.6 × u), u = `rand()` / 32768 in [0, 1), F = `Music frequency` × 60 s;
  then, if an id is remembered **and** `Music volume` > 0, it posts `Music_<Name>_Play` on object 100. The deadline moves on even when
  nothing is posted (no music biome, volume 0, bank still loading), and nothing checks whether music is still playing, so `_Play` is posted
  again every 0.8-1.4 F. The first deadline is audio init + F (not randomised). Changing the setting moves the deadline earlier if
  now + new F is sooner (`FUN_140258200`), never later.
- **The clock** is a wall-clock stopwatch (`FUN_14025b3b0` on `DAT_1421349b0`, QueryPerformanceCounter, never stopped by anything), so game
  speed, pause, the title screen and loading do not slow it. A deadline that passes during the title screen fires on the first tick after
  it, usually before the camera has picked a biome, so that interval is spent silent: nothing plays music at once when a game starts.
- **`Music frequency`** (settings.cfg, minutes, default 5, slider "Music Frequency (minutes)" 1..20; `FUN_1403e84f0`, `FUN_1403f0260`): at
  the default, music starts every 4-7 real minutes. `Music volume` (default 0.5) → `User_Volume_Music` × 100 (`FUN_140257d80`), at init and
  when the options close (`FUN_1403e7290`).
- **Banks.** At start (bank pass after `Init.bnk`): `MT_Music_Bank`, `SFX_General_Bank`, `SFX_Amb_Weather_Bank`, `SFX_Amb_Indoors_Bank`,
  `VO_Bank`, then `Boot_Up_Game` (`7D68E3F0`, plays the main theme `3A626E51`) on object 100. Biome music banks load on demand, in the
  audio manager's background bank pass (a ThreadWannabe slot; Observed). A biome music bank is unloaded 60 s after it was last current
  (kept alive only while its `_Play` id is the remembered one, and only when no load is pending), which cuts whatever of it still plays
  (Observed: Wwise stops a bank's content on unload). `Radio_Music_Bank`, `SFX_Bank` and the reverb banks are never loaded.
- **Main theme.** Plays from `Boot_Up_Game` on the title screen and is never restarted: the audio manager is built once (`FUN_140259d10`
  from `FUN_14086a960`) and the title screen only by the start-up loading loop (`Main_LoadingStateLoop` `14082b370`); whether a return to
  the menu exists is Unknown. Stopped by `Create_or_Load_Game` (`6455137B`, in `MT_Music_Bank` and `SFX_General_Bank`: a click sound plus
  stop `3A626E51` with a 5 s fade, global scope) posted on the **GUI object 110** when a new game, load or import starts (`FUN_140917400`,
  `FUN_14047b210`, `FUN_14047b2f0`); by every ambience `Enter_` event; and finally by the bank unload above.
- **What `_Play` does** (Observed, banks): plays the biome's three layers directly, no `Biomes` switch involved; what they are, how long
  they last and how the day/night switch behaves: [wwise-bnk.md Music](formats/wwise-bnk.md#music).
- **Pause and speed.** `Pause_Game` / `Resume_Game` (`FUN_140787d40`, on object 100, when paused or speed 0 flips) duck the SFX bus
  `59927CD0` to −96 dB and the ambience bus `95836E4C` by −9 dB (1 s), and reset both (3 s); the music bus `BA70EAC8` (child of the master
  bus, −4 dB) is not touched, so music plays on through pause. Game speed sets the state `Game_Speed` (`C2E2475B`) to `Normal` / `x2` /
  `x4` at speeds 1 / 2 / 5 (`FUN_140787e40`, `FUN_140787fb0`); only `SFX_Bank` and `SFX_General_Bank` use it, so speed does not affect music.
- **Not in the game:** combat or danger music, stingers, intensity parameters, indoor rules for music, focus-loss muting
  (`MuteBackgroundMusic` is never called): no such strings or calls in the exe. **Radio:** `Radio_On` (`D272B88E`: stops the biome music
  root `00DBE1E6` over 10 s, plays a click and the radio playlist `34220ED8`), `Radio_Off` (`D0987A94`) and `FCC71927` (name Unknown, stops
  both) are in `Radio_Music_Bank` and `SFX_General_Bank`, but the exe has no "Radio" string and never loads the bank: unused content.

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

`src/Meitou.Audio`: `AudioSystem` wires these together for a host (the viewer, the game); any missing part (no exe library, no device) leaves the
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
- **Music engine** (`WwisePlayer.Music.cs`; the layouts and meanings are in [wwise-bnk.md](formats/wwise-bnk.md#music)). Music nodes are
  instances in the same tree as the sounds, so fades, stop actions (also on an ancestor, across banks), the gain chain up to the music bus
  (`BA70EAC8`: −4 dB, the `User_Volume_Music` curve) and the voices are the same code.
  - **Switch** (`MusicSwitchInstance`): the decision tree is read with the current values of its groups (the child keyed with the value,
    else the one keyed 0; `SetSwitch` shares the state table). When a value changes during playback the playlist playing is held after its
    current segment and the new pick starts at that segment's exit cue (**Unknown**: the other candidate is the rule that crossfades at once,
    ~10 s); a change back before the cue cancels it; once the last segment's cue has passed (only its tail left) a change does nothing.
  - **Playlist**: the tree is walked lazily, the next segment pulled at each exit cue and started there, the old one's tail sounding on.
    Segment items repeat by loop count (0 forever); groups make loop-count passes of one child (step) or all (continuous), in order
    (sequence) or by weight (random; shuffle = weighted without replacement). Avoid-repeat history and sequence positions are kept per
    playlist group across plays. A segment whose bank is not loaded is skipped.
  - **Segment**: one voice per clip at segment time `PlayAt + BeginTrim`, played from the begin trim to the end trim; silences hold the time.
  - **Timing.** A start is placed at the output frame at or after the exact time inside the block being mixed, with the fractional part as
    the clip's starting phase, and a clip fades into silence over its last frame, so a segment that follows another at its cue leaves no
    gap and no overlap: the main theme's 281.7 s loop, 12,423,326.475 output frames long, repeats without a missing or doubled frame.
  - Plays are not exclusive: a `_Play` posted while the last still sounds (a short `Music frequency`) starts a second instance (**Unknown**
    whether Wwise limits music nodes). Not done, since no Kenshi bank needs it: stingers, transition segments, special track types, clip
    automation, the transition rules' fades, `continue playback`, the radio's 3D attenuation. `UnloadBank` cuts what plays from the bank,
    drops its recordings and objects (those of the same id in another loaded bank stay).
- `MusicDirector` is the game's music rules above. `Update(music)` each frame takes the `music` of the region under the pivot (null when
  none): on a change it sets the `Biomes` switch and remembers `Music_<Name>_Play` (when the bank is loaded, else after loading it; the
  first change unloads `MT_Music_Bank`); a real-time stopwatch timer (first deadline construction + F, then now + F × (0.8 + 0.6 r)) posts
  the remembered event when set and `Music volume` is above 0, moving the deadline either way; no `_Stop` is ever posted; a biome bank
  not current for 60 s is unloaded. `StartMainTheme` (loads `MT_Music_Bank`, posts `Boot_Up_Game`, pauses the timer) and
  `OnCreateOrLoadGame` (posts `Create_or_Load_Game`, resumes it) are for a host with a title screen (the game, see below); the viewer has none and does not call
  them. `Music frequency` (minutes, default 5) and `Music volume` (default 0.5, sent as `User_Volume_Music` × 100) come from `settings.cfg`.

### Volume curves (Observed)

All 111 volume RTPC curves in the version-112 banks (scaling 2, "dB") have y between −1 and +0.08, while pitch curves are in cents and
low-pass curves in its units. Read as **linear gain − 1** (−1 silent, 0 unchanged), they make sense everywhere: the sliders' curves are
silent at 0 and unchanged at 100, the critters fade out with height. The player multiplies the gain by 1 + y. Not checked against Wwise
itself.

### Levels (Observed)

The ambience is quiet: the Desert bed is mastered at −45 dBFS RMS (the general sound effects at −25 to −33), its layer container takes
12 dB, the slider's default another 8.5, so the desert by day mixes at about −66 dBFS RMS. That is what the banks say; whether the game
sounds the same was not measured. The viewer's `--audio-gain <dB>` raises the whole mix.

The music at `Music volume` 0.5 (`User_Volume_Music=50`): the main theme's loudest second is about −29 dBFS RMS, a biome play's about −37 to −45
(one segment per layer, so seconds of silence between pieces). `Init.bnk` defaults the parameter to 100 (the curve is half at 50, so +6 dB),
which is what the offline render uses unless told.

### Not done

Pause ducking (`Pause_Game` / `Resume_Game` live in `SFX_General_Bank`, which is never loaded, and the player skips the bus-volume actions
`SetBusVolume` / `ResetBusVolume`), audio sliders in the game's settings, interiors (needs the building raycast), point emitters
(`Start_Emitter` / `Stop_Emitter` with the `Emitters` switch, AMBIENT_SOUND records), effects and reverb (aux buses), attenuation curves,
voice limits, the radio's 3D attenuation (type 14 is not decoded), and unloading ambience banks left behind (they stay loaded: about 5
to 14 MB of Ogg each; the music banks unload as above).

## In the viewer and the game

On by default; `--no-audio` turns it off, `--audio-gain <dB>` raises or lowers everything. The console (the game's `log.txt`) prints the device, or
why there is none: the host then runs silently. F11's statistics show the biome, day or night, rain, wind and the number of voices, and the music: the
main theme or the remembered biome music, seconds until the timer next posts it, music voices (`AudioSystem.StatsLine`). Ambient and music volume
and `Music frequency` come from the install's `settings.cfg` once at start (the game's own settings have no audio sliders). Both hosts feed
`AudioSystem.Update` each frame (region under the pivot, weather, hour, sunrise and sunset, eye height).

- **Viewer** (`WorldApp.Audio.cs`): pivot = the camera's target, eye = its position, the viewer's hour; no title screen, so no main theme.
- **Game** (`GameHost.Audio.cs`): the audio is made on a worker while the window and the world load. Pivot = `camera.Target` and eye =
  `camera.Eye` (the strategy or free camera as drawn), hour, sunrise and sunset from the game clock (`session.Clock`), region and weather from the
  weather scheduler (`gpu.Weather`).
  - **Title screen.** `StartMainTheme` when the world is up and the title is open (either UI mode: the faithful title or the Meitou game menu).
    While the title is open the host does not update the ambience or the music (**Unknown** whether the original's camera update runs there; if it did, its first biome call would unload the theme's bank), so no `Enter_` event or
    music-bank unload cuts the theme. A new game or a load posts `Create_or_Load_Game` (5 s fade) as it starts; `--new-game` and `--load` skip the
    title and start no theme. Importing is not offered by the game's menu.
  - **Return to the main menu** (Meitou's choice: the original has no such path): the ambience is ended (`AmbienceDirector.Silence`: `Leave_<biome>`,
    rain and wind end) and `StartMainTheme` runs again, so the menu sounds as at start-up; the next game's first update re-enters its biome.
  - Not in screenshots, the headless paths or tests: only the interactive run creates the audio.

## Tools

- `meitou-tools audio` surveys the banks; `audio <bank>` dumps a bank's events and tree; `--decode` decodes its WEMs; `--wav <folder> [id]`
  writes them as WAV.
- `meitou-tools audio render <seconds> <out.wav> [Bank.bnk …] [Event …] [Parameter=value …] [Group:State …]` mixes events offline (no
  sound card) and prints the level each second, e.g.
  `audio render 60 desert.wav SFX_Amb_Desert.bnk Camera_Zoom=5 Time_in_Game:Day Enter_Desert_Day`. A word of eight hex digits is an event
  id (`audio render 290 mt.wav MT_Music_Bank.bnk User_Volume_Music=50 7D68E3F0`); music: `… Dunes_Music_Bank.bnk Time_in_Game:Day Music_Dunes_Play`.
