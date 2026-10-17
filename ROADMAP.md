# Roadmap

Goal: replace `kenshi_x64.exe` while loading original content, mods and saves unchanged.
Current capabilities and usage: [README.md](README.md). Research index: [docs/README.md](docs/README.md).
Project rules: [AGENTS.md](AGENTS.md). Engine constraints: [DECISIONS.md](DECISIONS.md).

## Original engine

**Observed from the install:** Ogre 2.0 Tindalos (Direct3D 11, Octree scene manager, Terrain, Overlay and MeshLodGenerator), MyGUI, ParticleUniverse, SkyX, PhysX 2.x (`PhysXLoader64`, `NxCharacter`), Havok, Wwise, OIS and Steamworks. Havok Behavior/Animation is linked, but characters animate through Ogre; see [animation.md](docs/animation.md). Format coverage: [overview](docs/formats/overview.md).

## Technology

- C# / .NET 10; Silk.NET windowing and Vulkan 1.3 native rendering.
- Independent Faithful MyGUI-compatible and styled Meitou UI modes; public control/style/action
  registration. [Native UI](docs/native-ui.md), [MyGUI](docs/formats/mygui.md).
- DotRecast navigation with our own builder/cache; original Havok tiles are references.
- Rigid-body physics: BepuPhysics2 (ragdolls). Audio: clean-room Wwise bank/WEM decoding
  and OpenAL Soft. Do not link proprietary SDKs or redistribute their DLLs.

## Compatibility work

| Area | Remaining work / authoritative reference |
| --- | --- |
| Data | Exact workshop subscription selection. [FCS](docs/formats/fcs-mod.md), [resource lookup](docs/formats/overview.md). |
| Characters | Remaining body/attachment/material parity and exact original random sequence. [Characters](docs/characters.md), [animation](docs/animation.md), [renderer](docs/character-renderer.md). |
| Navigation | Global pathing, dynamic doors and navmesh cache/lifetime gaps. [Pathfinding](docs/game/pathfinding.md), [simulation](docs/simulation.md#navmesh). |
| AI | Wars, prisoners/slavers, planner and full off-screen town simulation. [AI research](docs/game/ai.md), [runtime](docs/simulation.md#ai). |
| Dialogue | Dialogue runtime, conversation window, speech bubbles, event integration and recruiting. [Dialogue](docs/game/dialogue.md). |
| Economy | Building shops, production, construction, farming and research. [Economy](docs/game/economy.md), [production](docs/game/buildings-production.md). |
| Saves | Restore towns, NPC squads and world state beyond the player's squad; preserve original save compatibility. [Save format](docs/formats/save.md), [runtime mapping](docs/simulation.md#saves). |
| UI | Full control, font, mod and interaction fidelity; runtime code-module loading. [Parity](docs/faithful-ui-parity.md), [native UI](docs/native-ui.md). |
| Physics | Thrown bodies, physics props, character collision; ragdoll gaps in [simulation](docs/simulation.md#ragdolls). [Collision](docs/formats/collision.md). |
| Audio | The callers still missing: the sounds of doors, machines and effects, building floors as footstep surfaces, and the task and interface sounds whose trigger Meitou lacks (shackles, ground items, prospecting, research, the biome banner). [Audio](docs/audio.md). |

## Rendering

Current algorithms and limitations live in the feature docs, rather than a separate phase log:
[terrain](docs/render-terrain.md), [objects](docs/render-objects.md), [foliage](docs/render-foliage.md),
[water](docs/render-water.md), [sky](docs/render-sky.md), [clouds](docs/render-clouds.md),
[shadows](docs/render-shadows.md), [post](docs/render-post.md), [GI](docs/render-gi.md).
Faithful preserves original behaviour; Meitou quality improvements remain independently selectable.
Optional GI may trade performance for lighting quality.

## Open design question

Script-extender compatibility: [RE_Kenshi / KenshiLib feasibility](docs/formats/rekenshi.md). Existing DLLs need native object/hook compatibility; extension-data support and source ports are practical candidates. No runtime compatibility layer exists.
