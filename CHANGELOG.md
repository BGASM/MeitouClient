# Changelog

What changed in each release, newest first. The release workflow copies the section of the version it builds into the GitHub release
notes and refuses to release a version without one: add a `## vX.Y.Z` section with one `- ` line per change before running it.

## v0.7.0

- Night sky: Kenshi's two planets in the sky, and a new star field that twinkles and fades towards the horizon (Faithful keeps the game's)
- Planetshine: at night the big planet lights the land, and shadows and water reflections follow its light
- Night grading: at night dim colours shift towards a moonlit blue-grey, while lamps keep their colour
- Night air: a faint glow and planet-lit haze near the horizon; the planets are seen through the mist instead of cutting dark holes in it
- Volumetric clouds with cloud shadows that drift over land and water
- Clouds lit by the sun and the planet through their depth, with smoother cloud textures
- Light shafts and the sun's glint on water fade under heavy cloud and rain
- Water: see-through distance and clarity sliders, glint only where the reflection shows sky, swamp fog in the reflections, swamp ponds no longer turn teal
- No more straight band across distant water at sunrise and sunset
- No more DLSS streaks beside the sun while the clock runs, and shadows no longer shimmer as the sun moves
- Weather dust keeps up with the camera and blows with the wind; particle sizes match the game's
- Faster: global illumination, ambient occlusion, fog, light shafts, foliage and water all cost less, and the sky is skipped when fog hides it
- Fewer stutters while travelling: weather changes, uploads and memory are handled in the background (worst frame of a test flight 229 ms to 30 ms)
- The viewer starts in borderless full screen (Alt+Enter toggles it, --windowed for a window)
- Cinematic camera: numpad shots, an F2 timeline editor with timed keys and path recording, smooth flights, letterbox and fixed-rate video recording
- F9 shows an fps counter; grass reaches further
- The Meitou DLLs ship beside the exes, for code mods
- Many unused launch options removed

## v0.6.1

- Tab panel: sliders and checkboxes spread over three columns, so the panel no longer runs off the bottom of the screen
- Viewer: the time speed slider goes up to 240 game hours per real minute

## v0.6.0

- Light shafts: hills, buildings and trees cast shadows into the haze and weather fog, and ridges cast dark wedges into the sky towards a low sun (on by default, --no-shafts turns it off)
- Fog volumes such as the swamp's fog banks are darker where the sun is shadowed
- Morning and evening mist: a thin layer of air near the ground that lights up towards the sun, strongest at sunrise and sunset and gone by midday (Tab sliders)
- New default look: a tone map between the game's clip and ACES, so bright skies and sunlit sand roll off instead of clipping, plus a slight saturation and contrast grade (Faithful keeps the game's)
- Viewer: --time-speed and a Tab slider make the clock run (game hours per real minute)
- Viewer: --camera-code puts the camera at a code copied with Ctrl+C
- Fixed blotches and bands in the Fog Islands' fog

- Global illumination: on GPUs with ray tracing, light bounces between surfaces, so corners, interiors and the ground under overhangs darken and sunlit ground lights what faces it (on by default, --no-gi turns it off)
- Lamps: the game's outdoor point and spot lights now light the world
- F1 in the viewer (Shift+F1 in the game) turns all Meitou improvements off and back on, replacing the per-feature F keys
- Tab panel: tone map (clamp, shoulder, ACES), grading and heat haze strength sliders
- Tab panel: anisotropic filtering, texture quality, weather particles and particle density sliders; anti-aliasing can be turned off
- --low-end preset for weak PCs; render scale also works without an upscaler
- Various optimizations: simpler models for distant objects and buildings, cheaper shading where fog hides the scene, cheaper terrain, foliage, water reflections and far shadows
- Fewer stutters: at most one far shadow cascade is redrawn per frame

## v0.4.0

- Meitou water: ocean waves, sets of breakers that roll in as whitewater and wear away into lace, refraction, depth tint, caustics and glitter
- Rivers flow along their channels, with no waves or surf on them
- Swamp fog: the game's fog volumes, including the twisters' dust balls, now appear in the world
- Dense fog hides what is behind it, so foggy weather costs less to draw
- Ambient occlusion fades in haze and fog
- No white band at the horizon in overcast weather, such as the Vain's red rain
- Faster: plants and trees get simpler models in the distance and are skipped when something blocks them, far shadows are cached, and weather particles are drawn at lower resolution
- Second and later starts load the swamp's plants in about 1 s instead of 25 s
- Tab panel: slider for how far the simpler plant models start
- F11 shows frame time (mean and max) and triangles per frame
- Fewer stutters when tree billboards are generated
- --log-spikes logs slow frames, --tiered-jit and --pgo test .NET JIT settings, --bench runs benchmark views

## v0.3.0

- Weather: the game's weather schedule per region and season drives the sky, clouds, fog and heat haze; Tab panel button rerolls the weather
- Weather effects: rain, ash and other weather particles, fog banks, wet ground, objects and plants in the rain, dust on buildings and rocks, rain ripples on water
- Cloud layer drawn like the game's, drifting with the wind
- Animations: smoother blending between moves, the game's movement and attack speeds, combat clips, stumbles and falls
- Landmarks cast their shadows however far away they are
- Trees turn into billboards at the normal distance but are drawn as far as before; rocks and ruins keep their full meshes to 12000
- Characters no longer look dirty from ambient occlusion
- Low-end GPUs: shadow filter setting (Tab, --shadow-filter) and the shadow distance slider goes to 0 to turn shadows off
- Faster: weather particles, and small plants no longer drawn into the far shadow cascades
- Sandbox mode (--sandbox) for testing movement and animations on a flat grid

## v0.0.2

- The world comes alive: the Hub's residents, bar patrons and roaming squads walk around with walk, jog, run and idle animations
- New game: pick a start, select your squad with a click or a box, right-click to move (Shift queues orders), HUD with health, skills and inventory
- Pathfinding on a navmesh, through building doors and interiors
- Characters drawn with bodies, clothing, hair, weapons and facial expressions; crowds use generated detail levels
- Bodies and medical state, stats and XP, encumbrance, eating; first melee combat (attacks, blocks, armour, knockouts)
- Game keys: F10 key list, F11 statistics, F12 profiler, F8 / Print Screen screenshot, Shift+F1.. the Faithful / Meitou switches
- Tab panel: Faithful / Meitou switches as checkboxes (saved), "Delete billboard cache" button
- Faster terrain and tree billboards (about 15% less GPU time per frame in forests)
- Small plants drawn to 3500 (was 800); large trees and rock stacks stay full meshes to 12000 before turning into billboards
- Viewer: fps in the window title; long F11 lines wrap instead of running off the screen

## v0.0.1

- First release: the game (meitou.exe) and the world viewer (meitou-viewer.exe)
