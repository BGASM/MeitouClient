# Engine architecture

Projects: Core installation; Data clean-room readers/typed game data; Simulation headless world;
Navigation own navmesh; Engine WorldSession/time/input/camera; Rendering Vulkan/content/streaming;
Rendering.Display window/device/presenter; Game host/UI; ModelViewer debug world/atlas; Tools surveys.
GPU and simulation ownership stay separate. [Simulation contracts](simulation.md),
[native renderer](renderer-native.md), [viewer controls](viewer.md).

## Loop, input and camera

WorldSession owns world, clocks, input/bindings, camera and last two snapshots. Control30Hz real
(default config tickRate), max10 ticks/frame; simulation1/30 game second, speed0/1/2/5 and
6*speed budget. Control precedes simulation; interpolation uses each clock's alpha, shortest
angle rotation and cuts on mode changes. The authoritative time/order/determinism details are
in simulation. Host copies camera state into WorldCamera and snapshots into CharacterDrawList.

KenshiCamera follows pivot/boom/zoom/pitch/ground constraints from [camera](formats/camera.md).
Engine choices: pivot500*clamp(boom/600,0.1,1), keys1.5rad/s, mouse0.005rad/pixel; terrain-ground
pivot rather than building support. Free-camera ; follows smoothed flight, exits at ground hit.
InputState consumes events once per tick; commands apply at world boundaries, not UI callbacks.
meitou.user.json persists fps/vsync/fullscreen/tickRate/simThreads/graphics/bindings; renderer
settings and current key list belong in viewer. Quicksave F5/loadF9; cinematic F6/hideUI F7.

## Host and loading

GameHost partials own boot/interactive/offscreen, characters, saving, transitions and duel;
PlayerInterface owns picking/HUD/panels/commands. StandardSystems supplies order. NavAdapter
bridges Navigation; never let UI/rendering duplicate simulation state.
GUI executable attaches a caller's console; folder prompt creates its own when necessary.
Native splash precedes Vulkan; worker loading and render-thread GPU steps keep the window
responsive. Scene transitions draw loading between stages, wait on asynchronous results, and
preserve world on failure. Warm-up hides streaming/first-frame work until <40ms for0.5s,
max5s. Cancel on window close. Helpers/services dispose/join with their owning scene/session.

Meitou UI mode presents title controls over a live view of the real world; Faithful retains
the original title image and loading screen. Scene loads in Meitou use `SkyArrival`: 4s ascent
about a fixed eye (lighting takes the shorter way to midnight, so the dawn title rewinds), a midnight
sky hold, then 4.8s descent at the destination. Speed ramps smoothly over the first and last 40%
of each pan, with a short constant-speed cruise between them. Integrated smoothstep velocity brings
angular velocity and acceleration to zero at both ends of each flight. Save reads and
new-game system preparation overlap the ascent; world replacement waits until terrain is outside
the view. Escape/Space skips the flight, never pending load work. Progress appears after 2s.
Camera/hour overrides belong to presentation; simulation does not advance during the flight,
and weather scheduling uses the actual game hour. Sky-only frames avoid world callbacks while
loading; warm-up renders the destination before descent. Errors drop the flight and restore UI.
`TitleComposition` shows the real world at a fixed spot near Okran's Shield (Okran's Pride),
given as a viewer camera code (`TitleComposition.Spot`, 50° lens), just after sunrise (`Sunrise + 0.75` h,
the sun peeking over the hill right of the valley; clear weather, cloud coverage 0.38). The host's renderers stream round that camera as
round any other; booting to the title starts the world there. Everything below is presentation only, set
while the title draws or shows and cleared when it closes; simulation and saves never see it:
- `SkyClock.SunTurn` turns the sun path about the vertical (height unchanged) to ~26° right of the heading.
- The sky renderer's `PlanetFill` (0.8; 0 in play) lights the moons from the viewer's side so their whole face reads,
  and `PlanetTurn` (identity in play) moves them ~1.6° up and ~2.4° left in the view.
- The clouds drift right to left across the view at 250 units/s (`CloudWind`), faster than the game's wind.
- The shafts' air layer is raised to 2x and their strength to 1, so the hill the sun peeks over casts
  visible rays through the haze; `FogVolumes.HidePlaced` leaves out the placed `fogfeatures.dat` volumes
  (one stood as a dark dome where the sun is). `PostOptions.Balance` (1.1, 0.98, 0.88; 1 in play), a white balance
  before the tone map, keeps the orange of a lower sun.
- `WorldObjectRenderer.Staged` adds the foreground: a cliff top for the wanderer, the flat-topped hoodoo `FLatTop_Pillar01`
  (drawn through its FOLIAGE_MESH record `FlatTop_Hoodoo01`, terrain texturing), its top ~45 units ahead at the bottom of the
  frame and the block moved 125 units left, so its sheer right side drops to the valley just right of him; on that top, a
  little scree and a stone scaled to pebbles, desert shrubs on its left and a tiny one at its right edge (FOLIAGE_MESH records
  drawn as staged meshes), a few more on the far hill; and the
  buildable `Watchtower` building on the valley floor ~2600 units ahead at the far right, turned 35°, laid out as a placed
  building (setting or clearing it lays their zones out again). The object streamer reads a staged FOLIAGE_MESH's TERRAIN
  texturing from `material type` (a map feature's from `texture mode`); without it the mesh loses its level indices and
  draws white (**Observed** on this title).
- `FoliageRenderer.Shift` moves the ghost-town ruin (FOLIAGE_MESH houses and scrap piles) ~400 units left,
  applied as foliage zones are accepted.
- One wanderer (sandbox character in drifter's gear, with a stronger build: `CharacterAppearance.Shape`, null in play, sets 0.6
  muscle and broader shoulders, frame, chest, arms and neck) stands on the plateau's top where the ray through his screen point
  (x 0.337, y 0.93) meets it, close to the eye, lit by a warm fill (`CharacterRenderer.Fill`, 0 in play: added ambient,
  three times as strong at the silhouette) so his back, turned to the sun, is not a flat silhouette, ~300 px tall at 900 p, his back to the camera looking
  10° left of the heading, in a frozen `idle_stand_confident` pose; the character renderer draws him
  alone. The camera's `MaxNear` is lowered to 20 for
  the title (the near plane is a quarter of the eye's ~350-unit clearance, ~87, which clipped him).
The title menu is the [Meitou UI kit](meitou-ui.md)'s main menu (`MeitouTitleMenu`): its stack, save card and plate buttons at the kit's
1600×900 sizes scaled with the height, over the kit's vignette. The card (paper, edge, rounded corners, drop shadow), the plates (Button1Skin
9-slice at 70%, pushed skin at 85% under the mouse) and the chips are baked on the CPU from crops of `Kenshi_UI.png` and uploaded as
the atlas is (UNORM, so they draw in its colours); icons are atlas rectangles. The card shows the newest save's squad, day, place,
money and members (play time is not read from saves yet) and is Enter's default. The original buttons stay as invisible hit targets at the
same bounds, so input is the faithful screen's. Lettering is Exo 2 from the painter's 16 px atlas (ASCII and Latin-1 up to U+00B7),
or a 48 px bake past 1.25x, so it stays sharp.
The Meitou wordmark, top right, is `assets/branding/kenshi-logo.png` (Lo-Fi's logo, embedded; see THIRD_PARTY_NOTICES),
because the game ships none as a file ([ui-screens](game/ui-screens.md#title-artwork-and-location)).
No actor enters simulation or save data; the normal camera lens and character source
are restored when leaving the title.
Warm-up waits for objects (the ridge included), foliage and character assets before exposing the menu (subject to the normal 5s limit).
`--title-preview --screenshot <path>` captures it for visual QA. Save arrival still uses the
loader's leader-focused camera contract; saved camera pose restoration remains unimplemented.

## Rendering and upscaling

WorldFrame prepares/streams sky/shadows/reflection, scene depth slices and post. Algorithms
live in render-* references; Data holds backend-independent terrain/placement/light rules.
Own GLSL compiles through bundled shaderc, explicit native resource/state recording and ordered
secondary jobs. [Native API](renderer-native.md).

Single-sample jittered HDR reconstruction, separate near/far depth, unjittered reflection/shadows.
FSR Vulkan SDK1.1.4 runtime DLL; Streamline2.14.1 manual hook, initialize before Vulkan, request
features and proxy present. Bottom-up images require matching clip matrices/presentation.
Vendor DLLs are not committed; paths/packaging/licensing: [notices](../THIRD_PARTY_NOTICES.md).
Mip biases and temporal/GI contracts: [post](render-post.md). GI requested by default unless
--no-gi, unsupported ray queries fall back to flat ambient; gi Enhancement chooses output.

## Validation and performance

Tiering/PGO off, serverGC4 heaps, compact after loading then SustainedLowLatency; avoid streaming
allocations and measure tails. Offscreen fixed grass sway enables repeatable pictures. Parity
scripts compare10views; establish baseline noise for temporal vendors. Game --screenshot/--ticks,
--quit-after and --load exercise host wiring; [bench](bench.md) documents measurement.
**Observed** NVIDIA OpenGL/Vulkan sampling: anisotropy -0.25 bias reduced rock mean1.21→0.0009;
other vendors untested. Alpha-to-coverage differs; depth usesD32_SFLOAT. These are reference
observations, not a supported OpenGL backend.
