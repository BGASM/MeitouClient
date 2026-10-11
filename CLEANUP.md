# Cleanup audit

Two parts: [Part 1, tests](#part-1-tests) and [Part 2, launch options and env vars](#part-2-launch-options-and-env-vars).

# Part 1: tests

An audit of every test file in `tests/Meitou.Tests`: 147 files with 1,103 `[Fact]`/`[Theory]` methods
(over 1.2k test cases once the Theory rows expand). Five reviewers each read about 30 files and the
production code those files test. No test has been changed yet; this file is the proposal.

**Goal:** keep about 20% of tests, the ones that carry about 80% of the value.
**Result as proposed:** keep about **286 of 1,103 (26%)**, delete 21 files outright, trim 121, and keep
5 whole. The section "Getting to 20%" lists further cuts that would reach about 225.

Verdicts: **KEEP** keeps the whole file, **TRIM** keeps only the named methods, and **DELETE** removes
the file. "Rendering" says whether the code under test is drawn (direct), feeds what is drawn
(indirect: format readers, meshes, textures, terrain data), or has nothing to do with drawing (none).

## By area

| Area | Files | Rendering (direct/indirect/none files) | Files to delete | Tests | Keep | Kept |
|---|---|---|---|---|---|---|
| (root) | 2 | 0/1/1 | 1 | 14 | 5 | 35% |
| Bodies | 4 | 0/0/4 | 0 | 74 | 12 | 16% |
| Characters | 6 | 4/2/0 | 0 | 40 | 14 | 35% |
| Combat | 5 | 0/0/5 | 0 | 61 | 13 | 21% |
| Engine | 6 | 0/3/3 | 1 | 57 | 10 | 17% |
| Fcs | 2 | 0/2/0 | 0 | 6 | 3 | 50% |
| Gameplay | 2 | 0/1/1 | 0 | 12 | 4 | 33% |
| Navigation | 2 | 0/0/2 | 0 | 23 | 7 | 30% |
| Ogre | 4 | 1/3/0 | 0 | 40 | 14 | 35% |
| Physics | 2 | 0/0/2 | 0 | 13 | 5 | 38% |
| Rendering | 40 | 33/2/5 | 11 | 266 | 71 | 26% |
| Save | 3 | 0/0/3 | 0 | 36 | 9 | 25% |
| Simulation | 16 | 0/1/15 | 2 | 88 | 25 | 28% |
| Textures | 5 | 0/5/0 | 0 | 29 | 11 | 37% |
| Vulkan | 16 | 16/0/0 | 5 | 60 | 14 | 23% |
| World | 32 | 24/8/0 | 1 | 284 | 69 | 24% |
| **Total** | **147** | **78/28/41** | **21** | **1,103** | **~286** | **26%** |

## What the reviewers kept and what they cut

**Kept:**
- `[Slow]` end-to-end checks against the real install: the FCS byte-exact round trip, every base-game
  material script compiling, the phs-vs-skeleton cross-check, real-save round trips, and terrain,
  collision and texture reads.
- Non-trivial algorithms: `SkeletonRigTests`, `AnimationCacheTests`, the terrain LOD crack and pop
  guards, shadow seams, and occlusion and fog culling against their CPU references.
- One determinism and golden-hash suite (`DeterminismTests`, `GoldenHashTests`).

**Cut:**
- Shader-source text checks, tuned constants, CLI and option defaults, and simple toggles.
- `InRange` checks on tuning values for Meitou-only looks: night air, night grade, planetshine,
  clouds, night sky.
- Tests that copy the implementation or doc tables, especially the body, combat and stat formulas,
  with constants written into the test.
- Near-identical variants of one case: GPU harness variants, texel-scale edge cases, impostor cache
  housekeeping.
- Tests left over from VkGl (deleted in phase 8) or tied to a refactor that's finished.

## Cross-file findings

**Duplicates:**
- **Thread-count hash tests:** "Same hash at 1/4/16 threads" is tested in at least 7 files (Animation,
  BodyWiring, Determinism, FightWiring, Inventory, Movement, Hub). Keep only `DeterminismTests`.
- **Frame-split determinism:** tested 3 times (`TimeTests`, `SimulationClockTests`,
  `WorldSessionTests`).
- **Combat thread determinism:** tested in both `DuelTests` and `RealDuelTests`.
- **Doc constant tables:** the same table is checked in `BodyInstallTests`, `GameplayInstallTests`
  and `CombatInstallTests`.
- **Command ordering:** tested in both `JoinTests` and `DeterminismTests`. Water refusal is tested
  in both `JoinTests` and `MovementTests`.
- **Fog culling:** `FogCullTests` and `FogSkyTests` both run random cull-versus-shader-alpha checks.
  `FogCullDirectionTests`, `AtmosphereTests`, `CloudLayerTests` and `CloudFogSkipTests` probably
  overlap them too.
- **Terrain LOD:** `TerrainQuadtreeTests.Selected_nodes_tile…` is a fast subset of
  `TerrainLodTests.Touching_squares…`.

**Dead or obsolete code:**
- **VkGl comparisons:** `WorldResourceTests`, `SkyWaterNativeTests` and `LegacySpirvGoldenTests`
  compare against VkGl, which is deleted. So does
  `GpuApiTests.Legacy_programs_get_the_modules_VkGl_gave_every_world_program`, together with its
  `WorldPrograms()` helper.
- **Old foliage cull:** `FoliageCullTests` has slow tests that compare against an "Old" cull
  reimplemented inside the test. That cull no longer exists in the code.
- **Not a test:** `FlowMapTests` is an analysis script.

**Brittle:**
- **Pinned install counts:** `CollisionInstallTests` (1136 files, 1335 convex…),
  `GameplayInstallTests`, `CombatInstallTests` and the `OgreMaterialTests` audit pin exact counts.
  Any game patch or mod changes them.
- **Wall-clock limits:** `HubNavmeshTests` asserts 1500 and 3000 ms but isn't marked `[Bench]`.
- **Wrong tag:** `OgreMaterialTests.Wildcard_match_is_a_glob` is marked `[Slow]` but is a pure
  unit test.

**Dependencies to respect when deleting:**
- **Golden hash helpers:** `GoldenHashTests` calls helpers in `DeterminismTests`, `PlayerTests`,
  `RoamingTests`, `DuelTests`, `BodyWiringTests` and `FightWiringTests` (the `Hashes`/`Scripted`
  helpers). Move these into a shared helper file first, or cut the matching golden scenarios with
  them.
- **Fog helpers:** `CloudFogSkipTests` uses helpers from `FogCullTests`.
- **Occlusion reference:** `OcclusionCullTests` holds the CPU reference rule that the shader is
  tested against. Keep the rule with whichever tests survive.
- **Vulkan boilerplate:** the Vulkan test files each repeat `TryCreate`/`ExpectClean`. Pull this
  into one fixture when trimming.

## Getting to 20% (~220)

1. **Simulation, Bodies, Combat, Navigation, Save, Gameplay, Physics:** these have no rendering
   relevance. Together they hold 307 tests and the audit keeps about 75. Keeping only the golden
   hashes, one determinism suite, the install and save round trips, and the navmesh path tests
   brings this to about 35 (−40).
2. **Chunk 1's extra trim:** drop the remaining GPU-vs-CPU parity variants and the secondary maths
   checks (−16).
3. **Pinned-count install tests:** turn them into one sanity test each ("non-empty, parses
   without error") rather than keeping several (−5).

Applying all three leaves about 225 tests.

---

## Per-file audit


### `tests/Meitou.Tests/Bodies/BodyInstallTests.cs` — 8 tests
- **Tests:** [Slow] the doc's probe tables against the base-game install: body GameConstants, the nine LOCATIONAL_DAMAGE records, races (Greenlander, Scorchlander, Shek, Garru, Leviathan, Skeleton), LIMB_REPLACEMENT records, STATS records, and building a MedicalState for every race.
- **Relevance:** Real-data checks of the RACE/LOCATIONAL_DAMAGE readers, but mostly pinned magic numbers. `Constants_of_the_body_rows...` only compares one hardcoded fixture to the install and is repeated by `GameplayInstallTests.Constants_match_the_probe_table_of_game_loop`.
- **Rendering:** none (body/medical simulation)
- **Verdict:** TRIM (keep: `Greenlander_matches_the_race_and_anatomy_tables`, `The_nine_locational_damage_records_match_the_doc_table`; ~2 of 8)

### `tests/Meitou.Tests/Bodies/MedicalTests.cs` — 33 tests
- **Tests:** Four classes (Hunger 8, BloodAndKo 13, Healing 9, Determinism 3). They cover hunger drain and starvation, blood/KO time, severing and dismemberment, bleeding and clotting, wear on robots, degeneration, treatment and kit drain, prosthetics, seeded hit location, and the save round trip.
- **Relevance:** Formula-by-formula tests with the constants written inline. Many assert one constant or a trivial branch (`Starvation_ko_threshold...`, `Situation_factor...`, `Blood_capacity...`, `Ko_point...`, `Chance_of_death_*`, `Self_healing...`, whose assertion is too weak to fail). A handful exercise the real tick pipeline and are worth keeping.
- **Rendering:** none (sim)
- **Verdict:** TRIM (keep: `Starving_characters_pass_out_and_then_die_at_zero`, `Ko_time_is_the_base_plus_the_documented_terms`, `Destroying_a_vital_part_kills_and_a_limb_is_severed_subject_to_the_setting`, `Wounds_clot_and_blood_recovers_once_bleeding_stops`, `Save_keys_round_trip`; ~5 of 33)

### `tests/Meitou.Tests/Bodies/StatMultiplierTests.cs` — 15 tests
- **Tests:** `StatMultiplier` tables (hunger, limbs, head, torso, bonus factors, fitted limbs), plus `SpeedAndEncumbranceTests` (encumbrance factor, capacity, run speed chain).
- **Relevance:** Reimplements the per-stat rows of the docs as expected values, so it mirrors the implementation and breaks on any retune. Two tests give broad coverage of the whole arm/leg/hunger chain.
- **Rendering:** none (sim)
- **Verdict:** TRIM (keep: `Both_arms_down_follow_the_arm_rows`, `Run_speed_chain_applies_legs_hunger_and_encumbrance_with_a_floor_and_a_water_cap`; ~2 of 15)

### `tests/Meitou.Tests/Bodies/StatsTests.cs` — 18 tests
- **Tests:** `StatsTests` (11): stat numbering and field names, core gain curve, medic sharing, race stat map, RACE record scan, water avoidance, body part limb slot, limb replacement interpolation, seeded stat creation, group fan-out and save round trip. `XpTests` (7): XP gain formulas.
- **Relevance:** `Stat_numbers_and_storage_follow_the_doc`, `Water_avoidance_is_transformed`, `Body_part_side...` and `Limb_replacement_interpolates...` assert constants or trivial maps. The XP tests are mostly `> 1` smoke checks. The record scan, the gain curve and the save round trip hold real logic.
- **Rendering:** none (sim)
- **Verdict:** TRIM (keep: `Core_gain_follows_the_squared_curve`, `Race_record_scan_continues_past_a_stored_zero_and_stops_at_the_first_missing_key`, `Stats_from_groups_fan_out_and_save_round_trips`; ~3 of 18)

### `tests/Meitou.Tests/Characters/CharacterAppearanceTests.cs` — 11 tests
- **Tests:** HSB conversion, quaternion from columns, and ten mostly [Slow] install tests: `.phs` weapon attachment points checked against the male skeleton's bind pose, every `.phs` reads, `.bod2` body files, building a Dust Bandit/Beep character, a drawn weapon, walk animation masks, human_male.mesh pose morphs.
- **Relevance:** The strongest tests here are the end-to-end ones that cross-check two real formats (phs against skeleton bind pose) and the character-assembly one. `Ruka_body_file_reads`, `Beep_uses_her_body_file` and `Drawn_weapon...` mostly repeat `Modern_body_files...` and `Dust_bandit_assembles`. `Every_attachment_phs_reads_or_is_refused` is a weak smoke test.
- **Rendering:** indirect (assembles the character the renderer draws; attachment points, morphs)
- **Verdict:** TRIM (keep: `Weapon_attachment_points_match_the_male_skeleton`, `Modern_body_files_read_and_legacy_ones_are_counted`, `Dust_bandit_assembles`, `Body_mesh_poses_are_named_morphs`; ~4 of 11)

### `tests/Meitou.Tests/Characters/CharacterGeneratorTests.cs` — 10 tests
- **Tests:** Rounding, armour-grade quality table, colour range blending, HSB round trip, plus [Slow] install tests of the editor limits XML, seeded character generation, the Dust Bandit rules over 60 seeds, repeating faces, seeded build, body files kept.
- **Relevance:** The XML limits reader and the 60-seed faction roll check real data end to end. The first four tests are tiny (a constants table and a rounding rule), and the HSB round trip repeats the first test of the appearance file. The rest are variations on the same generator.
- **Rendering:** indirect (random NPC appearance and colours)
- **Verdict:** TRIM (keep: `Human_editor_limits_read_with_kenshi_defaults`, `Dust_bandits_roll_by_the_games_rules`; ~2 of 10)

### `tests/Meitou.Tests/Characters/CharacterLayoutTests.cs` — 5 tests
- **Tests:** C# struct sizes against fixed constants (`CharInstance` 144, `CharacterMaterialRecord` 256, `CharacterPush` 16), plus substring checks of the shader source text for binding declarations.
- **Relevance:** The struct-size checks guard against std430 layout drift and are cheap, but they compare against their own constants. The slot-count and binding tests assert shader-string contents and are brittle.
- **Rendering:** direct (GPU layout)
- **Verdict:** TRIM (keep: `Instance_is_144_bytes`, `Material_record_is_256_bytes`; ~2 of 5)

### `tests/Meitou.Tests/Characters/CharacterShapeTests.cs` — 11 tests
- **Tests:** `CharacterShapeTests` (9): bone-scale maths for neutral/female/height/muscle/starvation/missing limbs, stat mappings, pose weight baking, slider overrides, and a [Slow] Ruka body-file check. `CharacterLodTests` (2): LOD selection and a [Slow] human_male LOD check.
- **Relevance:** Real math that feeds the skinned mesh. The Ruka and human_male tests tie it to real files. Neutral/female/muscle/stat-mapping/pose-weight/override tests are expected-value tables with the constants copied from the implementation.
- **Rendering:** direct/indirect (character skeleton scaling, LOD levels fed to the renderer)
- **Verdict:** TRIM (keep: `Height_scales_the_spine_and_legs_and_moves_the_hips`, `Ruka_gets_her_body_file_proportions`, `Select_follows_ogres_lower_bound`, `Human_male_body_has_one_reduced_level_at_200`; ~4 of 11)

### `tests/Meitou.Tests/Characters/MeshSimplifierTests.cs` — 2 tests
- **Tests:** The mesh simplifier reduces a grid to requested fractions with valid indices; it stops at the floor.
- **Relevance:** The first test checks a nontrivial algorithm's invariants. The second only checks a lower bound.
- **Rendering:** direct (LOD generation)
- **Verdict:** TRIM (keep: `Reduces_to_the_requested_fractions_and_keeps_valid_indices`; ~1 of 2)

### `tests/Meitou.Tests/Characters/SkeletonRigTests.cs` — 1 test
- **Tests:** With a real `male_skeleton.skeleton`, the bind pose without layers gives identity skin matrices.
- **Relevance:** A good invariant on the skeleton reader and skinning setup, run against a real asset. It skips when there is no install.
- **Rendering:** direct (skinning)
- **Verdict:** KEEP

### `tests/Meitou.Tests/Combat/CombatDataTests.cs` — 10 tests
- **Tests:** `CombatDataTests` reads invented records into typed views and lerps crossbow levels. `CombatInstallTests` [Slow] checks record counts, constants, the katana, pierce flags, technique timing, armour coverage and manufacturer models. `RealDuelTests` [Slow] runs a real-gear duel at 1/4/16 threads and checks it is deterministic.
- **Relevance:** `Record_counts_match_the_doc`, `Constants_of_the_base_game...` and `Pierce_is_set_on_eleven...` pin magic numbers. The real-gear duel and the timing facts are the useful end-to-end checks. The duel test is also the only place the full pipeline is run on real data.
- **Rendering:** none (sim)
- **Verdict:** TRIM (keep: `Records_read_into_typed_views`, `Technique_timing_facts_of_the_data`, `Real_gear_duel_resolves_and_matches_at_every_thread_count`; ~3 of 10)

### `tests/Meitou.Tests/Combat/DuelTests.cs` — 10 tests
- **Tests:** Headless duels with invented gear: a duel ends, stance flags, fighters close in, a better fighter wins 80% of 24 duels, plate armour reduces damage, XP gain, fists, and determinism across 1/4/16 threads (crowd, seed variation, duel log).
- **Relevance:** The crowd determinism test is the guard for hard-to-find threading bugs. The stochastic "much better fighter wins" and "plate armour" tests are slow-ish statistical checks. `Another_seed...` and `The_log_of_a_duel...` repeat the crowd test and `RealDuelTests`.
- **Rendering:** none (sim)
- **Verdict:** TRIM (keep: `A_duel_of_two_katana_fighters_ends_in_a_knockout_or_death`, `One_four_and_sixteen_threads_reach_the_same_state_in_a_crowd_fight`; ~2 of 10)

### `tests/Meitou.Tests/Combat/FightRulesTests.cs` — 8 tests
- **Tests:** Reach miss, stagger, KO ends the fight, stop sheathes, no body, combat flag reaching the animation stance, movement plus combat systems side by side, playing-technique exposure.
- **Relevance:** Behaviour tests on the fight loop. Most go over ground that `DuelTests` also runs. The system-interaction tests catch integration bugs.
- **Rendering:** none (sim)
- **Verdict:** TRIM (keep: `A_knocked_out_fighter_cannot_react_and_the_fight_ends`, `Movement_and_combat_systems_side_by_side_do_not_fight_over_the_position`; ~2 of 8)

### `tests/Meitou.Tests/Combat/FormulaTests.cs` — 21 tests
- **Tests:** Damage, block, armour and hit-resolution formulas against the docs' worked numbers.
- **Relevance:** Several are pure constants (`Constants_are_rescaled_once...`, `Stumble_threshold...`, `Dodge_skill`, `Toughness_resistance...`, `Coverage_is_a_percentage_roll...`). Another group repeats each formula's expected value from its implementation. A few have real structure (the block chance curve, armour stacking, `Resolve`).
- **Rendering:** none (sim)
- **Verdict:** TRIM (keep: `Cut_damage_of_a_katana_at_quality_half_and_skill_fifty`, `Block_chance`, `Armour_stacks_multiplicatively_and_penetration_scales_the_reduction`, `Resolve_applies_armour_then_toughness_then_the_global_multiplier`; ~4 of 21)

### `tests/Meitou.Tests/Combat/ReactionTests.cs` — 12 tests
- **Tests:** Hit outcome rules (block, dodge, dual-flag techniques), reaction technique choice, attack choice, frame fractions.
- **Relevance:** Mostly sweeps of a `pick` parameter over fixtures. The attack/block decision tests are fine; the rest are narrow variants of the same chooser.
- **Rendering:** none (sim)
- **Verdict:** TRIM (keep: `A_block_needs_the_right_direction_and_more_than_half_the_animation`, `Attack_choice_follows_weapon_distance_skill_and_the_static_rows`; ~2 of 12)

### `tests/Meitou.Tests/Engine/CameraTests.cs` — 18 tests
- **Tests:** Strategy camera: initial state, zoom formula and clamps, pitch limits and overshoot, max height, ground clearance, clearance constants, random-drive invariant, pivot movement, orbit drag, free camera, leaving free flight, previous/current interpolation.
- **Relevance:** Many tests assert Kenshi's camera constants (150, 30 degrees, 10/2000, -0.92, 0.2, 1840, 20+10). The random drive (3000 ticks, eye never below ground) and the free-flight handover cover the real risk.
- **Rendering:** indirect (view state the renderer consumes)
- **Verdict:** TRIM (keep: `State_reproduces_the_eye_through_the_viewers_orbit_formula`, `The_eye_stays_above_the_ground_over_a_long_random_drive`, `Leaving_free_flight_puts_the_pivot_where_the_view_meets_the_ground`; ~3 of 18)

### `tests/Meitou.Tests/Engine/GameClockTests.cs` — 6 tests
- **Tests:** Game hour length, start values and wrapping, minute floor, daytime bounds, daylight factor ramp (theory), sunrise/sunset fallback and argument checks.
- **Relevance:** Time-of-day drives the sun and lighting. `A_game_hour_is_1200_over_11...` is a constant and `Minutes_are_the_floor...` is one line.
- **Rendering:** indirect (day/night inputs)
- **Verdict:** TRIM (keep: `Start_values_and_wrapping`, `Daylight_ramps_over_two_hours_after_sunrise_and_before_sunset`; ~2 of 6)

### `tests/Meitou.Tests/Engine/InputTests.cs` — 11 tests
- **Tests:** Input state/binding semantics: press once per frame, press without a tick, taps, release and key repeat, press-release-press, two-key actions, mouse/wheel first tick only, focus loss, default bindings, overrides, round trips.
- **Relevance:** Eleven variations of one small state machine, each cheap. One covers the semantics; the rest are corner cases that will rarely regress.
- **Rendering:** none (input)
- **Verdict:** TRIM (keep: `A_press_is_seen_by_exactly_one_of_several_ticks_in_a_frame`; ~1 of 11)

### `tests/Meitou.Tests/Engine/SimulationClockTests.cs` — 12 tests
- **Tests:** Ticks per second by speed (theory), tick = 1/30 game second, pause/resume, requested pause, faster/slower, tick budget and dropped ticks, split-time equivalence, alpha while paused, speed key bindings, pause with camera still moving, camera speed independence, control tick ordering.
- **Relevance:** Overlaps with `TimeTests` and `WorldSessionTests` (frame-split determinism is checked three times). The pause/speed-key tests are narrow.
- **Rendering:** none (loop timing, with a thin link to camera interpolation)
- **Verdict:** TRIM (keep: `Tick_count_depends_only_on_the_summed_time_and_speed`, `Ticks_beyond_the_budget_are_dropped_and_the_achieved_speed_drops`; ~2 of 12)

### `tests/Meitou.Tests/Engine/TimeTests.cs` — 9 tests
- **Tests:** Fixed-step clock ticks and alpha, alpha bounds, catch-up cap, configurable rate, frame-split determinism over a scripted session, lerp, angle lerp, `Interpolated<T>`, camera state lerp.
- **Relevance:** The scripted-session determinism test is a good broad integration check. The angle lerp is real render-interpolation math. `Lerp_hits_endpoints...` is trivial and `The_tick_rate_is_configurable` mostly checks argument validation.
- **Rendering:** indirect (render interpolation)
- **Verdict:** TRIM (keep: `Different_frame_splits_of_the_same_time_give_the_same_ticks_and_state`, `Angles_interpolate_along_the_shortest_path_across_pi`; ~2 of 9)

### `tests/Meitou.Tests/Engine/WorldSessionTests.cs` — 1 test
- **Tests:** The same total time in different frame splits gives the same session state.
- **Relevance:** Duplicate of `TimeTests.Different_frame_splits...` and `SimulationClockTests.Tick_count_depends...`.
- **Rendering:** none
- **Verdict:** DELETE

### `tests/Meitou.Tests/Fcs/FcsRoundTripTests.cs` — 4 tests
- **Tests:** A synthetic V16/V17 FCS file round trips, truncated and invalid-UTF8 files throw, and four base-game files (`gamedata.base`, `Newwworld.mod`, `Dialogue.mod`, `rebirth.mod`) round trip byte for byte [Slow].
- **Relevance:** The `.mod` format is the base of all data loading. The byte-exact round trip against the real files is the best test in the repo for the reader. The two error tests are cheap.
- **Rendering:** indirect (every placed object and material comes from these records)
- **Verdict:** TRIM (keep: `Synthetic_file_round_trips`, `Base_game_file_round_trips_byte_for_byte`; ~2 of 4)

### `tests/Meitou.Tests/Fcs/FcsSchemaTests.cs` — 2 tests
- **Tests:** The `fcs.def` parser on an inline sample; every `FcsRecordType` name matches its `fcs.def` section against the base game [Slow].
- **Relevance:** The install test checks the record-type enum against real data, which is the useful part. The parser test is a single example of a text format.
- **Rendering:** indirect
- **Verdict:** TRIM (keep: `Record_type_names_match_fcs_def_against_base_game`; ~1 of 2)

### `tests/Meitou.Tests/GameDatabaseTests.cs` — 12 tests
- **Tests:** The merge rules of `GameDatabase` (override, rename, remove, orphan modify, references, instances, value tables, issues), `mods.cfg` parsing, the "Iron Rock" merge against the install, and the whole base game loading with no dangling references.
- **Relevance:** This is the central data layer, so the merge rules matter. Several are small variants of the override rule (`Renaming_record...`, `Rename_bit_alone...`, `Modified_record_without_base...`, `Each_value_kind...`, `Problems_are_reported...`). `Iron_rock...` is covered by the full base-game load.
- **Rendering:** indirect (all object and terrain records go through it)
- **Verdict:** TRIM (keep: `Later_file_overrides_listed_fields_and_keeps_the_rest`, `References_are_added_overwritten_and_removed_per_target`, `Instances_merge_by_id_and_empty_target_clears`, `Mods_cfg_follows_the_game_rules`, `Base_game_loads_with_every_record_accounted_for`; ~5 of 12)

### `tests/Meitou.Tests/GameInstallTests.cs` — 2 tests
- **Tests:** An empty directory is rejected, and the real install opens when configured [Slow].
- **Relevance:** Trivial. Every install-dependent test would fail on a bad install anyway.
- **Rendering:** none
- **Verdict:** DELETE

### `tests/Meitou.Tests/Gameplay/AnimationCacheTests.cs` — 1 test
- **Tests:** A brute-force grid over leg values checks the animation selection cache against a direct reference, and that the cache stays small.
- **Relevance:** A thorough check of a tricky cache, and it already protects against stale cache entries. One test, no install needed.
- **Rendering:** indirect (animation clip selection)
- **Verdict:** KEEP

### `tests/Meitou.Tests/Gameplay/GameplayDataTests.cs` — 11 tests
- **Tests:** `GameplayDataTests` (5): faction relations, hostile/ally thresholds, coexistence, `GameConstants` rescaling, town setup rules. `GameplayInstallTests` [Slow] (6): constants probe table, faction counts, animation walk/jog/run chain, squad template counts, new-game starts, town type histogram.
- **Relevance:** Most install tests pin documented counts (103 factions, 959 squad templates, 13 starts, 3356 hostile pairs) and shift with a mod or game patch. The animation chain test and the town histogram check real structure. The constants probe overlaps with `BodyInstallTests.Constants_of_the_body_rows...`.
- **Rendering:** none/indirect (factions and squads are gameplay; towns and animations touch rendering)
- **Verdict:** TRIM (keep: `Relations_start_from_the_explicit_entry_else_the_lower_default_and_self_is_100`, `Constants_apply_the_loaders_rescalings_and_fall_back_to_the_editor_defaults`, `The_base_games_animation_records_form_a_walk_jog_run_chain_and_weapon_and_idle_variants`; ~3 of 11)

### `tests/Meitou.Tests/Navigation/HubNavmeshTests.cs` — 9 tests
- **Tests:** [Slow] The Hub navmesh pipeline on zones 20.32 and 21.32: golden path pins, geometry gathering stats, carving and pruning, door polygons, street-to-house path, crossing the Hub (with a 1500 ms bound), the off-thread build service and cache, `NavSystem` ring loading, and a repeatable/fast build (3000 ms bound).
- **Relevance:** The golden path pins and the street-to-house path are strong end-to-end checks. The gather test exercises terrain and building geometry reading. Three tests assert wall-clock bounds (`Building_a_zone_is_repeatable_and_fast_enough`, the crossing, the cache load), which belong in `[Bench]`, and the service/NavSystem tests are plumbing with temp directories.
- **Rendering:** none/indirect (the gatherer reads the same terrain and building collision data)
- **Verdict:** TRIM (keep: `The_hub_paths_keep_their_exact_points`, `A_path_leads_from_the_street_into_a_house_through_its_door_and_back`, `The_hub_zone_gathers_terrain_buildings_doors_and_seeds`; ~3 of 9)

### `tests/Meitou.Tests/Navigation/NavigationTests.cs` — 14 tests
- **Tests:** Synthetic-zone navmesh tests: open ground, obstacle cut-out, seed pruning, no-seed fallback, narrow gap, zone borders and removal, `TryHighestAt`, water cost, doors, interiors joined through a door, walkability fallback, cache round trip, funnel smoothing.
- **Relevance:** Real algorithm tests that run without the game. Several overlap: `Without_any_seed...` is a variant of `Only_regions_near_a_seed_survive`, `Removing_a_zone...` of `Paths_cross_a_zone_border`, and `Open_ground...` is a smoke test the others cover. The interior test also covers the cache round trip with doors.
- **Rendering:** none (pathfinding)
- **Verdict:** TRIM (keep: `An_obstacle_is_cut_out_and_paths_go_around_it`, `Doors_are_open_ground_that_a_closed_door_blocks`, `An_interior_is_a_separate_mesh_joined_to_the_street_through_its_door`, `Paths_cross_a_zone_border`; ~4 of 14)

### `tests/Meitou.Tests/Ogre/OgreMaterialTests.cs` — 24 tests
- **Tests:** Ogre script lexer (3), parser/compiler (5), inheritance rules (wildcards, abstract, imports, cycles, variables), typed pass and texture-unit accessors and defaults, alias handling, invalid lines, duplicates, wildcard glob (wrongly marked [Slow]), plus three install tests: compile every base-game material script, mesh material audit counts, texture existence counts.
- **Relevance:** Material scripts reach the renderer through `MaterialResolver`/`WorldObjectRenderer`, and the compile-everything test is a strong end-to-end check. The audit tests pin magic counts (114 materials, 3748 submeshes, 155 textures). Lexer and many one-line accessor/default tests are narrow.
- **Rendering:** direct/indirect (material and texture resolution)
- **Verdict:** TRIM (keep: `Derived_material_overlays_base_and_its_own_properties_win`, `Imports_bring_bases_from_other_files`, `Variables_expand_from_object_scope_bases_and_globals`, `Texture_units_resolve_aliases_and_list_their_files`, `Compiles_every_base_game_material_script`; ~5 of 24)

**Chunk totals:** 311 tests, keep ~73.


### `tests/Meitou.Tests/Ogre/OgreMeshReaderTests.cs` — 5 tests
- **Tests:** Synthetic .mesh builder round-trips (geometry, submesh, skeleton link, bounds, names), TryReadBounds shortcut, generated LOD in two layouts, rejects bad version/truncation, and a [Slow] parse of every base-game mesh checking indices and positions inside bounds.
- **Relevance:** Core asset reader; the synthetic and install tests catch real regressions. TryReadBounds edge cases and reject tests are low value.
- **Rendering:** indirect — every mesh the renderer draws goes through this reader.
- **Verdict:** TRIM (keep: `Reads_geometry_submesh_skeleton_bounds_and_names`, `Reads_generated_lod_in_both_layouts`, `Reads_every_base_game_mesh`; ~3 of 5)

### `tests/Meitou.Tests/Ogre/OgreScriptResourcesTests.cs` — 3 tests
- **Tests:** resources.cfg parsing/order, mod folders joining groups and overriding by name, Find group preference.
- **Relevance:** Resource lookup decides which texture/material file wins; moderately nontrivial, no game needed. Find_prefers... is largely covered by the other two.
- **Rendering:** indirect — asset resolution.
- **Verdict:** TRIM (keep: `Sections_register_in_ordinal_order_and_land_is_rewritten`, `Mod_folders_join_the_matching_group_and_win_by_name`; ~2 of 3)

### `tests/Meitou.Tests/Ogre/OgreSkeletonReaderTests.cs` — 8 tests
- **Tests:** Synthetic skeleton read (bones, scale, parents), quaternion order, bone scale decided by stored length (Ogre quirk), animations/keyframes/links, 1.10 default blend mode, reject malformed; [Slow] every base-game skeleton parses and is consistent; [Slow] every skinned mesh matches its skeleton (with a hand-kept known-bad list).
- **Relevance:** Real-format end-to-end check plus a hard-won quirk test. Quaternion order and the 1.10 default are tiny. The mesh/skeleton cross-audit is a data audit with a brittle exception list.
- **Rendering:** indirect — skinned characters.
- **Verdict:** TRIM (keep: `Reads_bones_with_and_without_scale_and_their_parents`, `Bone_scale_is_decided_by_the_stored_length_not_the_name`, `Reads_animation_tracks_keyframes_base_info_and_links`, `Reads_every_base_game_skeleton`; ~4 of 8)

### `tests/Meitou.Tests/Physics/CollisionInstallTests.cs` — 4 tests
- **Tests:** [Slow] all install collision XML parse with exact hard-coded counts (1136 files, 1335 convex, ...); winding counts of triangle meshes (146/229/1); record counts (1019 parts, 546 with collision, 354 foliage); collision boxes overlap render-mesh bounds under the documented axis map (IoU, compared with wrong maps).
- **Relevance:** Install-end-to-end, but three of four are pinned to magic counts that break on any game update/mod and prove little. The IoU test validates the axis convention, a real nontrivial finding.
- **Rendering:** none — physics/collision (only the mesh-bounds cross-check touches render meshes).
- **Verdict:** TRIM (keep: `Every_xml_collision_file_in_the_install_parses_with_the_documented_counts` (drop the exact counts when trimming), `Collision_boxes_overlap_their_render_meshes_under_the_documented_axis_map`; ~2 of 4)

### `tests/Meitou.Tests/Physics/CollisionShapeTests.cs` — 9 tests
- **Tests:** Box/capsule triangulation volumes, convex hull of cube and random points, flat point sets, Pose parse/compose, axis map, cooked triangle mesh decode, rejects broken data.
- **Relevance:** Mostly small geometry checks of a physics-only feature; hull and cooked decode are the only nontrivial ones.
- **Rendering:** none — collision.
- **Verdict:** TRIM (keep: `Convex_hull_of_random_points_is_closed_and_outward`, `Cooked_triangle_mesh_decodes_with_8_bit_indices`, `Pose_rows_are_read_in_order_and_compose_inner_first`; ~3 of 9)

### `tests/Meitou.Tests/Rendering/BenchHarnessTests.cs` — 13 tests
- **Tests:** CLI option parsing for views/low-end/bench flags/sizes, bench statistics (summary, paired difference), picture diff thresholds, A/B toggle registry, GPU lock queueing (timing based), bench result save/compare.
- **Relevance:** Dev tooling. Option-parsing tests assert default constants (brittle, change with every tuning). Paired stats and picture diff are real algorithms used for the pixel gates. GPU lock test uses delays.
- **Rendering:** indirect — tooling for rendering benchmarks, no output of its own.
- **Verdict:** TRIM (keep: `PairedDifferenceFindsAStepThroughDrift`, `NoDifferenceIsNotSignificantAndDroppedFramesAreLeftOut`, `PictureDiffCountsThresholds`; ~3 of 13)

### `tests/Meitou.Tests/Rendering/CameraPathTests.cs` — 6 tests
- **Tests:** Spline camera flight: hits keys at their times, C1 smooth speed through uneven keys, rest at ends / easing, shortest-way yaw/hour, loop seam, Apply round-trip.
- **Relevance:** Real spline math, used for benchmark flights; velocity-continuity checks are good. Yaw wrap and Apply are small.
- **Rendering:** indirect — bench camera.
- **Verdict:** TRIM (keep: `The_flight_reaches_every_key_at_its_time`, `Speed_changes_smoothly_through_unevenly_timed_keys`, `A_loop_returns_to_the_first_key_and_keeps_moving_through_it`; ~3 of 6)

### `tests/Meitou.Tests/Rendering/EarlyDepthTests.cs` — 3 tests
- **Tests:** Asserts the generated GLSL text of the solid programs has no `discard`, the others keep theirs.
- **Relevance:** Shader-source string matching; no behaviour is exercised.
- **Rendering:** direct, but only as text checks.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/FogShadingRateTests.cs` — 7 tests
- **Tests:** VRS attachment codes, opacity-to-fragment-size thresholds (theory), device without 4x4, haze/volume opacity combination, monotonic rate, env threshold parsing (theory), rate-image tile count.
- **Relevance:** Small pure functions with tuned constants; mirrors implementation (threshold table, parse cases). Only the opacity maths and monotonicity carry any logic.
- **Rendering:** direct — fog shading rate.
- **Verdict:** TRIM (keep: `The_haze_and_the_volumes_multiply_what_shows_of_the_surface`, `A_more_opaque_surface_never_gets_a_finer_rate`; ~2 of 7)

### `tests/Meitou.Tests/Rendering/FoliageCardTrimTests.cs` — 12 tests
- **Tests:** Convex hull, hull simplification, polygon clip, mask hull with margin and partial overlap, mip-level masks, mask disk cache, quad trimming into a smaller polygon (interpolation, winding, area), opaque/transparent quads, non-affine kite, other texture repeat, coarser level for big cards.
- **Relevance:** Real geometric algorithm with good invariants (hull contains points, clip stays inside, winding kept). Many quad variants are edge cases of the same path; disk cache is plumbing.
- **Rendering:** direct — foliage overdraw reduction.
- **Verdict:** TRIM (keep: `The_convex_hull_contains_every_point_and_drops_inner_ones`, `A_clip_stays_inside_the_card_whichever_way_either_winds`, `A_quad_with_a_small_leaf_becomes_a_smaller_polygon_with_interpolated_corners`, `A_quad_all_opaque_is_left_alone_and_a_fully_transparent_one_is_dropped`; ~4 of 12)

### `tests/Meitou.Tests/Rendering/FoliageCullTests.cs` — 9 tests
- **Tests:** FoliageInstanceRecord std430 layout (96 B), 68-byte arena packing round trip, then 2M-iteration [Slow] equivalence tests of refactored math against a reimplemented "Old" (scalar distance bit-identical, squared range ulp flips, reciprocal fade within 1 ulp), fade packing, precomputed spheres vs per-frame test, CullGroup vs old loop, impostor split complementary.
- **Relevance:** The "old vs new" tests guard a finished refactor against code that no longer exists in the repo (reimplemented in the test); heavy, ulp-obsessed, pin implementation. The layout test is a cheap GPU-contract guard; CullGroup vs old and the impostor split check real cull behaviour.
- **Rendering:** direct — foliage culling CPU reference.
- **Verdict:** TRIM (keep: `Instance_record_is_std430_96_bytes`, `Cull_keeps_index_order_and_matches_the_old_decisions`, `Impostor_split_is_complementary`; ~3 of 9)

### `tests/Meitou.Tests/Rendering/FoliageGpuCullTests.cs` — 7 tests
- **Tests:** [Slow] GPU compute vs CPU reference, bit-for-bit, on synthetic data: CrSqrt correctly rounded, main cull, solid/fading split ordering, fog-hidden cull, impostor split, terrain-rock path, depth-pyramid occlusion.
- **Relevance:** Strong GPU-vs-CPU parity tests of the actual shader code, catching real shader regressions. Seven variants of the same harness; fog, split, rock and CrSqrt ones are narrower. 921 lines.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `Gpu_cull_matches_FoliageCull_on_synthetic_data`, `Gpu_impostor_split_matches_FoliageCull`, `Gpu_cull_leaves_out_what_the_depth_pyramid_hides_as_the_rule_does`; ~3 of 7)

### `tests/Meitou.Tests/Rendering/FoliageGrassGpuTests.cs` — 1 test
- **Tests:** [Slow] GPU grass cull against CPU `PrepareGrass` decisions (draws, blade counts, ordering).
- **Relevance:** Same GPU-vs-CPU parity style, guards a real shader.
- **Rendering:** direct.
- **Verdict:** KEEP

### `tests/Meitou.Tests/Rendering/FoliageLodTests.cs` — 6 tests
- **Tests:** LOD builder on a sphere (levels, monotone deviation), no levels for small meshes, deviation sees a thin spike, point-triangle distance vs dense sampling, LOD cache round trip, [Slow] GPU cull kernel picks the LOD level by projected size.
- **Relevance:** Builder and deviation measure are real algorithms; kernel test is solid. Triangle distance is covered indirectly by deviation; cache and "few triangles" are plumbing.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `A_smooth_mesh_gets_levels_with_a_small_never_decreasing_deviation`, `Deviation_sees_a_thin_part_that_a_level_loses`, `The_cull_puts_each_instance_in_the_level_its_size_picks`; ~3 of 6)

### `tests/Meitou.Tests/Rendering/FoliagePrepassTests.cs` — 3 tests
- **Tests:** `Assert.Contains` on shader source: `invariant gl_Position`, discard counts, dither expression equal between programs, "#version 450".
- **Relevance:** Pure shader-string checks.
- **Rendering:** direct, as text only.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/FoliageScreenLodTests.cs` — 8 tests
- **Tests:** Triangle extent formula, pushed deviations bounds, sub-pixel level choice (property sweep), far/near level choice, sub-pixel push condition, unknown size/off changes nothing, billboard transition sooner/floor rules, resolution scaling.
- **Relevance:** Pure maths of a tuning rule; many tests re-derive the same formulas with magic numbers. The sweeping property test and the transition test carry the intent.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `A_sub_pixel_rule_only_ever_picks_a_coarser_or_equal_level_and_exactly_one_level_takes_each_size`, `The_billboard_comes_sooner_for_small_triangles_but_never_nearer_than_the_atlas_frame_allows_nor_later_than_before`; ~2 of 8)

### `tests/Meitou.Tests/Rendering/FoliageSizeTests.cs` — 3 tests
- **Tests:** Size class thresholds, [Slow] named base-game meshes land in expected size classes, IsVegetation folder test.
- **Relevance:** The install test exercises mesh decode, foliage catalog and classification end to end (skips if a mesh is missing). Thresholds and IsVegetation assert constants/paths.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `Base_game_meshes_fall_into_the_expected_classes`; ~1 of 3)

### `tests/Meitou.Tests/Rendering/GiFogHideTests.cs` — 7 tests
- **Tests:** GI probe reach box conservative, hidden layers per column, reach box inside fog block, no hide without fog cull, probe list unchanged without hiding, hidden probes listed once per period, far cascade periodic listing.
- **Relevance:** Niche GI-in-fog optimisation; scheduling invariants (once per period) and the conservative box are the meaningful ones. The rest are scenario variants with tuned numbers.
- **Rendering:** direct — GI probes.
- **Verdict:** TRIM (keep: `The_reach_box_holds_every_point_that_can_read_the_probes_of_the_column`, `A_hidden_probe_is_listed_once_in_each_period_of_its_rounds_and_the_others_every_time`; ~2 of 7)

### `tests/Meitou.Tests/Rendering/GrassMotionReachTests.cs` — 4 tests
- **Tests:** Phase step across wrap, no scale for degenerate input, reach formula, reach capped at range.
- **Relevance:** Four one-line formulas for a small feature; only the wrap case is a classic bug.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `The_phase_step_is_the_short_way_round`; ~1 of 4)

### `tests/Meitou.Tests/Rendering/ImpostorBudgetTests.cs` — 6 tests
- **Tests:** Atlas VRAM budget percentages and caps (theories), small-budget floor, override/pressure, far-mip skip per distance doubling, planned bytes equal texture bytes.
- **Relevance:** Budget policy tests restate constants (8%, 5%, 1 GB, 256 MB). The two-implementations-agree test and mip skip arithmetic are the useful ones.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `Far_mips_drop_a_level_for_every_doubling_of_the_distance_and_keep_the_transitions_level_at_the_transition`, `The_planned_bytes_are_what_the_textures_take`; ~2 of 6)

### `tests/Meitou.Tests/Rendering/ImpostorPrepassTests.cs` — 5 tests
- **Tests:** Shader source string checks (invariant, shared statements between prepass and colour, no discard after prepass), quad without box, packed half-extents never shrink the box.
- **Relevance:** Three of five are shader text matching; the pack test is a real conservative-bound property.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `The_packed_half_extents_never_shrink_the_box`; ~1 of 5)

### `tests/Meitou.Tests/Rendering/ImpostorTests.cs` — 26 tests
- **Tests:** Layout maths (hemi-octahedral and normal encode round trips, frame basis, weight selection, transition distance, class sizing, bias, small-class transition, coverage scale), atlas file format (round trip, from level, damaged files), BC1/BC3/BC5 encode-decode, an 8-test impostor disk cache suite (hit/miss, LRU eviction, no cap, clear, stale removal, env cap, use timestamps, default path), assembler pooling determinism, [Slow] GPU bake renders nearest surface in right orientation.
- **Relevance:** Encodings, selection weights, file round trip, BC encoders and the GPU bake are valuable. The cache suite is filesystem housekeeping; the class/threshold tests restate constants.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `Hemi_octahedral_map_round_trips_and_puts_the_horizon_on_the_border`, `Normal_encoding_round_trips_over_the_whole_sphere`, `Selection_weights_sum_to_one_and_pick_a_frame_alone_on_its_direction`, `Atlas_file_round_trips`, `Bc3_keeps_both_colours_of_a_two_colour_block_and_its_coverage`, `Bc1_cut_out_keeps_covered_colours_and_transparent_texels`, `Assembler_output_does_not_depend_on_the_pooled_memory_it_reuses`, `Bake_renders_the_nearest_surface_in_the_frame_orientation`; ~8 of 26)

### `tests/Meitou.Tests/Rendering/LandmarkTests.cs` — 5 tests
- **Tests:** WorldOptions default distances in Meitou vs Faithful mode, CLI override order, foliage class range constants, placement scale by largest axis, landmark radius threshold between 1488 and 2007.
- **Relevance:** Asserts tuned constants and CLI parsing; will churn with every tuning pass.
- **Rendering:** direct, but constants only.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/LightShaftTests.cs` — 4 tests
- **Tests:** Shaft air by time of day (smoothstep ramp), cloud fade, overcast from rain, [Slow] shaft scan kernel's SPIR-V reflection bindings/uniform offsets.
- **Relevance:** Formula checks restating tuning constants; the reflection test pins the shader's binding layout and member order (implementation detail, brittle).
- **Rendering:** direct.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/MeitouToggleTests.cs` — 2 tests
- **Tests:** The Meitou on/off key saves and restores the switch mix.
- **Relevance:** Trivial state toggle.
- **Rendering:** none (UI toggle).
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/MeitouWaterTests.cs` — 8 tests
- **Tests:** Refraction slice rule (theory), reflection fog shader compiles (plain and MS), glint cloud fade, waves paused with game clock, breaker advance/wind growth, wave count wrap, polar grid triangle validity and winding, foam bake deterministic and tiling.
- **Relevance:** The shader compile smoke test catches broken GLSL; polar-grid winding/index validity is a geometry invariant. Others restate constants.
- **Rendering:** direct — water.
- **Verdict:** TRIM (keep: `Reflection_fog_shader_compiles_for_plain_and_multisampled_depth`, `Polar_grid_covers_the_disc_with_valid_triangles`; ~2 of 8)

### `tests/Meitou.Tests/Rendering/ObjectLodGenTests.cs` — 7 tests
- **Tests:** Generated LOD distances, merge of generated and file levels into a curve (three scenarios), blend vs switch, min gain per level, file triangle counts for missing submesh indices, UV-island guard in mesh simplifier.
- **Relevance:** Merge logic is intricate; the UV guard protects texture seams during simplification (a hard-to-see bug). Distance and gain tests are tuning-ish.
- **Rendering:** direct — object LOD.
- **Verdict:** TRIM (keep: `File_levels_stay_in_the_plain_curve_and_a_generated_level_that_undercuts_the_next_file_level_replaces_it`, `Generated_levels_switch_at_their_distance_and_file_levels_still_cross_fade`, `The_uv_guard_keeps_a_triangle_from_taking_corners_of_two_islands`; ~3 of 7)

### `tests/Meitou.Tests/Rendering/ObjectStreamingTests.cs` — 13 tests
- **Tests:** MipStreaming drop levels by distance/view/FOV/bias (6 tests), texel-scale estimation for UV meshes (4 tests), LOD level held vs viewer blend, far-user mesh vertex compaction, VRAM-based mark scaling.
- **Relevance:** Texture/mesh streaming correctness matters, but the mip tests re-derive the formula and FOV/bias are near-duplicates. Texel-scale variants are four edge cases of one function.
- **Rendering:** direct — texture and mesh streaming.
- **Verdict:** TRIM (keep: `Near_users_keep_every_level_and_far_ones_lose_the_top_ones`, `Texel_scale_takes_the_stretched_direction`, `The_level_held_follows_the_lod_value_with_the_blend_band`, `A_mesh_made_for_far_users_holds_only_what_their_levels_use`; ~4 of 13)

### `tests/Meitou.Tests/Rendering/OcclusionCullTests.cs` — 4 tests
- **Tests:** HiZ level sizes, property test that the (test-local) occlusion rule never calls a visible sphere hidden (1500 random scenes with ray checks), edge cases of the rule, [Slow] GPU pyramid build vs CPU across odd sizes.
- **Relevance:** The conservative-occlusion property test and GPU pyramid parity are high-value; level-size and edge-case tests are small. The "rule" lives in the test, with the shader tested against it in FoliageGpuCullTests.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `A_sphere_the_rule_calls_hidden_is_hidden_from_every_eye_within_the_step`, `The_kernels_build_the_least_and_greatest_distance_of_every_block_at_every_level`; ~2 of 4)

### `tests/Meitou.Tests/Rendering/OceanTests.cs` — 6 tests
- **Tests:** FFT ocean spectrum: mirror pairing, empty bins/bands, determinism/wind scaling/direction, spreading normalisation, dispersion repeat; [Slow] GPU FFT vs hand-computed single wave.
- **Relevance:** The GPU test verifies the whole evolve/IFFT/assembly path against closed form (excellent). Mirror pairing is necessary for real-valued heights. The rest are statistical sanity checks of tuning.
- **Rendering:** direct — ocean.
- **Verdict:** TRIM (keep: `Spectrum_pairs_each_wave_with_its_mirror`, `Gpu_transform_gives_the_wave_worked_out_by_hand`; ~2 of 6)

**Chunk totals:** 204 tests, keep ~66.


### `tests/Meitou.Tests/Rendering/OverlayWrapTests.cs` — 3 tests
- **Tests:** word-wrap of the F11 debug statistics panel lines (`DebugOverlay.Wrap`).
- **Relevance:** Text formatting of a debug overlay. Cosmetic; a regression is visible instantly on screen. Note: `grep DebugOverlay.Wrap` finds no caller in `src` under that exact spelling, so it may be only called internally.
- **Rendering:** none — debug UI text layout.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/ParticleSliderTests.cs` — 1 test
- **Tests:** mapping of renderer options (`ParticleDivisor`, low-res flag) to a 0-4 weather particles slider value.
- **Relevance:** Mirrors a tiny switch in `WorldFrame`; asserts constants.
- **Rendering:** none — settings UI mapping.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/RiverFlowTests.cs` — 7 tests
- **Tests:** CPU bake of the river flow map on synthetic heights (direction, reversal, diagonal, ponds/lakes, crossings, width fade) plus a `[Slow]` test on the game's real heights that neighbouring texels do not turn sharply.
- **Relevance:** Real geometric algorithm; the "crossing is still / no pinwheel / no seams" test guards a bug that was hard to find. The `[Slow]` test is the end-to-end one against the install. The other synthetic cases are variations.
- **Rendering:** direct — water renderer input.
- **Verdict:** TRIM (keep: `Directions_turn_smoothly_and_a_crossing_is_still`, `Real_rivers_turn_smoothly`; ~2 of 7)

### `tests/Meitou.Tests/Rendering/ShadowFogCapTests.cs` — 6 tests
- **Tests:** shadow range cap derived from placed fog block (quantisation, hysteresis, hold frames, pending frames, randomised hide-distance property).
- **Relevance:** The randomised property test (given hide distance means every frustum ray is opaque past it) is a real correctness check of geometry. The hysteresis test guards against shadow popping. The rest are step-by-step state-machine variations and constants.
- **Rendering:** direct — shadow pass range.
- **Verdict:** TRIM (keep: `The_hide_distance_is_given_only_where_the_block_hides_the_whole_view_and_then_every_ray_is_opaque_past_it`, `A_radius_that_wobbles_within_8_percent_keeps_the_cap_still`; ~2 of 6)

### `tests/Meitou.Tests/Rendering/ShadowLodTests.cs` — 7 tests (5 in ShadowLodTests, 2 in TerrainFloorLevelTests)
- **Tests:** per-cascade LOD choice for far shadow casters (object level floor, terrain floor level, impostor threshold, rock tolerance, cascade applicability) and the terrain quadtree floor level (no finer nodes, same area covered).
- **Relevance:** Mostly pure arithmetic mirroring the implementation with magic numbers (swamp texels). The quadtree test (area preserved, no node below floor) and the object-level test are the meaningful ones. `Only_the_far_cascades...` and `Generated_levels...` assert constants/trivial rules; the negative-floor test is trivial.
- **Rendering:** direct — shadow LOD.
- **Verdict:** TRIM (keep: `An_object_takes_the_level_of_the_distance_where_a_pixel_is_as_wide_as_the_cascade_texels`, `No_node_is_finer_than_the_floor_and_the_same_ground_is_covered`; ~2 of 7)

### `tests/Meitou.Tests/Rendering/ShadowScheduleTests.cs` — 8 tests
- **Tests:** spreading of far shadow cascade redraws over frames (`ShadowSchedule.Pick`): budget, fairness, forced redraw, must-draw, zero budget, reset, simulated steady demand.
- **Relevance:** Small scheduling logic; several cases are single-assert variants. The forced-after-cap test and the long simulated-demand bound capture the real invariant (no cascade starves).
- **Rendering:** direct — shadow pass scheduling.
- **Verdict:** TRIM (keep: `A_redraw_waiting_the_cap_is_forced_beyond_the_budget`, `A_steady_demand_below_the_budget_never_waits_more_than_a_frame_or_two`; ~2 of 8)

### `tests/Meitou.Tests/Rendering/ShoreFieldTests.cs` — 16 tests
- **Tests:** shore distance-field bake on synthetic terrain (straight beaches at angles, island radius, open sea vs pond exposure, islet vs island surf, narrow bay, uniform fields, gradient continuity, snapping), scratch-buffer reuse and allocation counts, interleaving, height snapshot, plus a `[Bench]` bake time.
- **Relevance:** The geometric tests (beach distance error bound, island, sea exposure, islet/island surf, gradient continuity) check non-trivial distance-transform math. The scratch/alloc/interleave/uniform tests check plumbing and a GC allocation ratio (brittle); snap/centre and shoreline-between-texels are minor; `[Bench]` is timing.
- **Rendering:** direct — water shore effects.
- **Verdict:** TRIM (keep: `Straight_beach_distance_is_the_perpendicular_distance_with_water_positive`, `Island_distance_is_the_radius_difference`, `Open_sea_is_exposed_and_a_small_pond_is_not`, `An_islet_has_no_surf_round_it_and_an_island_does`; ~4 of 16)

### `tests/Meitou.Tests/Rendering/TerrainLateColourTests.cs` — 2 tests
- **Tests:** string checks on generated shader source (no `discard`, same vertex text twice).
- **Relevance:** Shader-source-text assertions; one test compares a function result to itself (tautological).
- **Rendering:** direct — but worthless as a regression guard.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/TerrainLayerSkipTests.cs` — 2 tests
- **Tests:** substring checks in the terrain fragment shader and a range assert on a constant (`CliffEps`).
- **Relevance:** Shader text and constants; breaks on any shader refactor.
- **Rendering:** direct — but shader text only.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/TriangleBinsTests.cs` — 13 tests
- **Tests:** the bench's triangle-size histogram tooling: area bins, coarse bins, folding samples to triangles, histogram scale/add, program-name categories, GLSL instrumentation text, SPIR-V compile of the instrumented shader, JSON round trip and table printing.
- **Relevance:** Developer/benchmark tooling, not shipped rendering. Many tests assert constants, bin tables and printed text. The fold (statistical estimate) is the only non-trivial maths.
- **Rendering:** none — bench tooling.
- **Verdict:** TRIM (keep: `FoldEstimatesTrianglesFromTheSamplesTheyShaded`; ~1 of 13)

### `tests/Meitou.Tests/Rendering/UploadStepTests.cs` — 6 tests
- **Tests:** GPU upload queue time-budget rules (first step always runs, learned step costs, slack), step-cost forgetting, and the slab/row sizing of impostor level uploads.
- **Relevance:** The budget/learned-cost behaviour is real scheduling logic that prevents frame hitches. The slab theory recomputes the implementation's own formula, and the "kind never seen" test is a minor variant.
- **Rendering:** direct — streaming upload pacing.
- **Verdict:** TRIM (keep: `The_first_step_runs_even_when_it_alone_overruns_the_budget_and_the_queue_moves_every_call`, `A_step_of_a_kind_that_has_cost_more_than_what_is_left_waits_for_a_call_of_its_own`; ~2 of 6)

### `tests/Meitou.Tests/Rendering/UpscalingTests.cs` — 6 tests
- **Tests:** Halton sequence, jitter phase count, jitter projection shift, render-size/scale parsing, camera reprojection to the previous frame, still-camera motion vector.
- **Relevance:** Reprojection (far-from-origin float precision, rotation + translation, jitter) and jitter-matrix correctness are exactly the kind of bugs that are hard to find visually and break TAA/DLSS. Halton/phase count/render-size are constants and trivial formulas.
- **Rendering:** direct — TAA/upscaler motion vectors.
- **Verdict:** TRIM (keep: `Reprojection_carries_a_surface_point_into_the_previous_frame`, `Jitter_moves_the_picture_by_render_pixels`; ~2 of 6)

### `tests/Meitou.Tests/Rendering/VramGuardTests.cs` — 9 tests
- **Tests:** VRAM pressure guard state machine with fake clock (idle, enter pressure, log once, hysteresis/recovery, relapse, sampling rate, unknown budget, priority scratch grants, squeeze pacing).
- **Relevance:** Real stateful logic protecting against OOM; but nine tests over one small state machine is excessive. Enter-pressure and recovery/hysteresis cover the main paths.
- **Rendering:** direct — memory management for rendering.
- **Verdict:** TRIM (keep: `Enters_pressure_at_90_percent_pauses_streaming_and_clamps_ranges_step_by_step`, `Pressure_ends_under_80_percent_and_the_ranges_come_back_only_after_calm_under_74`; ~2 of 9)

### `tests/Meitou.Tests/Rendering/WeatherSurfaceTests.cs` — 3 tests
- **Tests:** rain amount clamp, FrameConstants uniform/texture offsets and size, distinct mesh-surface bits.
- **Relevance:** Constants and struct offsets; mirrors the implementation. Layout mistakes would show immediately on screen.
- **Rendering:** none worth keeping — constants.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Rendering/WorldResourceTests.cs` — 2 tests
- **Tests:** `[Slow]` GPU: native mesh vertex attributes equal a hard-coded table copied from VkGl's GL vertex arrays; native mipmaps equal a re-implemented VkGl `GenerateMipmap` blit chain.
- **Relevance:** Migration tests from "Phase 8 stage 1" against VkGl, which has since been deleted. The reference is a copy of the implementation, so they are tautological now. Needs a Vulkan 1.3 device.
- **Rendering:** direct — but obsolete migration check.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Save/SaveReadTests.cs` — 13 tests
- **Tests:** save-file layer on synthetic data (slot lists, non-UTF8 strings, hands, camera lists, layered folders) then `[Slow]` per-user-save checks: byte-exact round trip of every file, zone list order, platoon/character consistency, handle completeness, slot-list derivation, zone building containers, handle resolution, zone overlay on base placements.
- **Relevance:** The byte-exact round trip over the user's real saves and the non-UTF8 string round trip are the high-value compat guards. The Zones overlay test (needs install) ties the save to world placements. The other `[Slow]` ones assert observed properties of three sample saves (e.g. exact handle type sets, "Extra zone file: zone.41.13.zone") and are brittle to the sample set.
- **Rendering:** none (indirect only for the zone overlay) — save/gameplay data.
- **Verdict:** TRIM (keep: `Every_file_of_a_save_reads_and_writes_back_to_the_same_bytes`, `Strings_that_are_not_utf8_survive_a_round_trip`, `Zones_overlay_the_base_placements_by_instance_id`; ~3 of 13)

### `tests/Meitou.Tests/Save/SaveWorldTests.cs` — 10 tests
- **Tests:** capture a simulated world into a save and load it into another (synthetic town), idempotent re-save, changes reaching the save, non-player member kept, joiners getting records, load needing a fresh world, rotation conversions; `[Slow]` real saves loaded into a world, re-captured keeping every record, and surviving a changed chain.
- **Relevance:** The synthetic new-game capture/load round trip and the real-save "captured again keeps every record" are the end-to-end checks. The remaining synthetic ones are scenario variations, and the third `[Slow]` duplicates much of the second.
- **Rendering:** none — sim/save integration.
- **Verdict:** TRIM (keep: `A_new_games_world_is_captured_into_a_save_and_loaded_into_another_world`, `A_real_save_captured_again_keeps_every_record`; ~2 of 10)

### `tests/Meitou.Tests/Save/SaveWriteTests.cs` — 13 tests
- **Tests:** building saves (ids, handles, derived lists), write/read-back, atomic replace, failed install rollback, removing a character, camera zones, child-count rule, blank portrait PNG validity/atlas, `[Slow]` real saves written back identically, derived data equals the game's, builder-made records having the same key sets as real ones.
- **Relevance:** The real-save write-back, derived-data and key-set tests verify that written saves are what the game would write (compat); the failed-install test protects users' saves from data loss. Child-count, PNG chunk walk, hand prefixes, replace-folder duplicates and ids are lower value.
- **Rendering:** none — save writing.
- **Verdict:** TRIM (keep: `A_loaded_save_written_to_a_folder_is_the_same_files`, `Derived_data_computed_for_a_real_save_is_what_the_game_wrote`, `New_records_have_the_key_sets_of_the_real_ones`, `A_failed_install_puts_the_old_save_back`; ~4 of 13)

### `tests/Meitou.Tests/Simulation/AnimationTests.cs` — 9 tests
- **Tests:** animation weight maths (sigmoid speed ramp, leg weights), weapon variant picks, idle to walk crossfade, synched phase, sword walking and idle holding, stumble selection, technique/hit playback with root motion, world integration, 1/4/16-thread hash equality.
- **Relevance:** The movement clip weight test (concrete numbers from Kenshi's rules) and the technique/hit root-motion test are the substantive ones. The rest are scenario variants; thread-count test duplicates other files.
- **Rendering:** indirect — produces the animation layers the character renderer plays.
- **Verdict:** TRIM (keep: `Every_movement_clip_weighs_the_sigmoid_of_its_speed_ramp_normalised_per_body_half`, `A_technique_follows_the_combat_timing_and_a_hit_plays_its_reaction_that_moves_the_character`; ~2 of 9)

### `tests/Meitou.Tests/Simulation/BodyWiringTests.cs` — 8 tests
- **Tests:** stats/medical states at spawn, water-avoidance factor, hunger, knock-out, kill + corpse removal, limp/lying clip choice, thread-count independence, hit/time-scale visible in state hash.
- **Relevance:** Mostly scenario variations; thread-count and hash-moves tests duplicate DeterminismTests and GoldenHashTests (which calls `BodyWiringTests.Hashes`, so keep that helper). Corpse lifetime is the most end-to-end behaviour.
- **Rendering:** none — sim.
- **Verdict:** TRIM (keep: `A_character_killed_by_a_hit_stops_and_its_corpse_is_removed_after_twelve_game_hours`; ~1 of 8; keep the `Hashes` helper)

### `tests/Meitou.Tests/Simulation/CoreTests.cs` — 10 tests
- **Tests:** RNG purity and distribution, random stream, state hasher, worker pool exact-once and exception survival, character table slot reuse/generations, double-buffer semantics, effect ordering, spatial grid order.
- **Relevance:** Foundations of determinism, but RNG-hash-differs tests and hasher inequality tests are near-trivial. Slot generations, deterministic effect ordering and the pool are real invariants.
- **Rendering:** none — sim core.
- **Verdict:** TRIM (keep: `Slots_are_reused_most_recent_first_with_a_higher_generation`, `Effects_are_ordered_by_target_source_system_and_sequence_whatever_the_buffers`, `The_pool_runs_every_index_exactly_once`; ~3 of 10)

### `tests/Meitou.Tests/Simulation/DeterminismTests.cs` — 5 tests
- **Tests:** state hashes equal across 1/4/16 threads and across runs; the wander workload really exercises births/deaths/slot reuse; command ordering; snapshot publishing.
- **Relevance:** The cross-thread hash test is the central determinism guard; the workload test keeps it honest. Command ordering is duplicated by `JoinTests.CommandsComeOutByTickThenQueueOrder`. `Hashes` helper is used by GoldenHashTests.
- **Rendering:** none (snapshot publishing is the sim/render interface, but trivially tested).
- **Verdict:** TRIM (keep: `One_four_and_sixteen_threads_reach_the_same_state_at_every_checkpoint`, `The_workload_really_exercises_deaths_births_and_slot_reuse`; ~2 of 5)

### `tests/Meitou.Tests/Simulation/FightWiringTests.cs` — 3 tests
- **Tests:** combat wired into the system order (walk, fight, retaliation), medical ticked exactly once, thread-count independence of a fight.
- **Relevance:** The end-to-end fight and the double-tick check (documents why `TickMedical = false`, a subtle bug) are worthwhile; the thread test duplicates others (but `Hashes` is used by GoldenHashTests).
- **Rendering:** none — sim combat.
- **Verdict:** TRIM (keep: `An_attacker_walks_to_its_target_fights_and_the_attacked_and_their_mate_retaliate`, `The_medical_state_is_ticked_once_per_tick_by_the_body_system`; ~2 of 3; keep the `Hashes` helper)

### `tests/Meitou.Tests/Simulation/FormationTests.cs` — 3 tests
- **Tests:** group formation slots (centred, spaced), order preservation/front row, determinism of `Formation.Place`.
- **Relevance:** Small algorithm; the order-keeping test guards path crossing; spacing test and the "same input same output" test are weak.
- **Rendering:** none — gameplay.
- **Verdict:** TRIM (keep: `Characters_keep_their_left_to_right_order_and_the_ones_nearest_the_target_take_the_front_row`; ~1 of 3)

### `tests/Meitou.Tests/Simulation/GoldenHashTests.cs` — 1 test
- **Tests:** pins state hashes of eight scripted scenarios (wander, player, roaming, bodies, fight, duels, partitioned wander/town).
- **Relevance:** One test that catches any unintended sim behaviour change across subsystems; brittle by design (must be updated with intended changes) but the best value-per-test in the sim. It depends on helpers in `DeterminismTests`, `PlayerTests`, `RoamingTests`, `DuelTests`, `BodyWiringTests`, `FightWiringTests`; deleting those files requires removing scenarios here.
- **Rendering:** none — sim.
- **Verdict:** KEEP

### `tests/Meitou.Tests/Simulation/HubNavigationTests.cs` — 1 test
- **Tests:** `[Slow]`, install: a move order across The Hub on the real navmesh detours through the gates and stays on the mesh.
- **Relevance:** Real end-to-end check of navmesh generation from game data plus movement; guards hard-to-find nav bugs.
- **Rendering:** none (navigation from game data).
- **Verdict:** KEEP

### `tests/Meitou.Tests/Simulation/HubTests.cs` — 3 tests
- **Tests:** `[Slow]`, install: Hub residents follow template rules and stand in town; thread-count independence of the Hub; `[Bench]` tick cost.
- **Relevance:** The resident test checks population rules against the real game data; the thread test is another duplicate; the bench asserts a loose timing bound.
- **Rendering:** none.
- **Verdict:** TRIM (keep: `The_hubs_residents_follow_the_template_rules_and_stand_in_the_town`; ~1 of 3)

### `tests/Meitou.Tests/Simulation/InventoryTests.cs` — 8 tests
- **Tests:** starting items, grid add/stack/full, overload slowing, eating own food, squad-mate feeding, provisioning vs starvation, thread independence of eating, squad purse in the hash.
- **Relevance:** The grid logic and provisioning/starvation scenario are the useful ones. Others are scenario variants; thread test is a dup; purse-in-hash is trivial.
- **Rendering:** none.
- **Verdict:** TRIM (keep: `Items_take_free_cells_and_stack_and_a_full_grid_refuses`, `Nobody_starves_when_npcs_are_provisioned_but_the_player_gets_nothing_free`; ~2 of 8)

### `tests/Meitou.Tests/Simulation/JoinTests.cs` — 2 tests
- **Tests:** command queue ordering by tick then queue order; `OpenGroundWalkability` refusing water.
- **Relevance:** Misnamed file. Command ordering is duplicated by `DeterminismTests.Commands_apply_at_their_tick_in_queue_order`; water refusal is also covered by `MovementTests.Characters_do_not_walk_into_the_water...`.
- **Rendering:** none.
- **Verdict:** TRIM (keep: `CommandsComeOutByTickThenQueueOrder`; ~1 of 2) — or DELETE entirely if `DeterminismTests.Commands_apply...` is retained

### `tests/Meitou.Tests/Simulation/MovementTests.cs` — 9 tests
- **Tests:** move order arrival, stat-speed path following, walk/run/free speed caps, water avoidance and unreachable goals, separation, squad wander/follow, 1/4/16-thread hash equality, async paths, spawn/remove in parallel phase throws.
- **Relevance:** Move-order, water avoidance and the parallel-phase misuse guard are valuable; the thread test duplicates Determinism; speed caps assert constants; separation is a single assert.
- **Rendering:** none.
- **Verdict:** TRIM (keep: `A_move_order_walks_the_character_to_the_target_and_it_idles_there`, `Characters_do_not_walk_into_the_water_and_unreachable_goals_give_up`, `Spawning_or_removing_in_a_parallel_phase_fails_loudly`; ~3 of 9)

**Chunk totals:** 189 tests, keep ~50.

### `tests/Meitou.Tests/Simulation/PlayerTests.cs` — 6 tests
- **Tests:** New-game squad setup, selection rules, move/queued/stop orders, zones stay active near player squad, and a scripted game hashed at 1/4/16 threads.
- **Relevance:** Mostly gameplay-rule checks on a synthetic town; the thread-determinism hash is the only one guarding a hard-to-find bug class. `Scripted(int)` is also used by `GoldenHashTests` (keep the helper).
- **Rendering:** none — simulation/commands.
- **Verdict:** TRIM (keep: `A_scripted_game_of_selects_and_orders_hashes_the_same_at_1_4_and_16_threads`; ~1 of 6)

### `tests/Meitou.Tests/Simulation/PopulationTests.cs` — 11 tests
- **Tests:** Squad size/offset rules, plan ordering and purity, world-state gating, resident list merge, zone box math, load/unload with grace period, background build equals synchronous build.
- **Relevance:** The two integration tests (load/unload lifecycle; background == sync) catch real regressions; the rest are rule/constant mirrors of `SquadFactory`.
- **Rendering:** none — sim.
- **Verdict:** TRIM (keep: `Residents_appear_when_the_focus_is_near_and_leave_after_the_grace_period`, `Background_building_gives_the_same_town_as_building_in_the_tick`; ~2 of 11)

### `tests/Meitou.Tests/Simulation/RoamingTests.cs` — 6 tests
- **Tests:** Bar-squad entry rules, bar squads added to residents, roaming pool fill/cap, platoon unload to stand-ins and reload, visit/home cycle, determinism hash across threads.
- **Relevance:** Long-running scenario tests tightly coupled to the synthetic town's numbers. The reload lifecycle and the thread hash are the valuable ones.
- **Rendering:** none — sim.
- **Verdict:** TRIM (keep: `Roaming_squads_hash_the_same_at_1_4_and_16_threads`, `Roaming_squads_leave_the_active_zones_as_stand_ins_and_come_back_loaded_when_a_zone_near_them_is_active`; ~2 of 6)

### `tests/Meitou.Tests/Simulation/SystemPhasesTests.cs` — 3 tests
- **Tests:** That `SystemPhases` leaves null the Think/Move/Act delegates a system does not override; act-once-per-partition count.
- **Relevance:** Implementation detail of the tick scheduler; trivially covered by any world test that runs.
- **Rendering:** none.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Textures/BlockCompressionTests.cs` — 5 tests
- **Tests:** BC1/BC3 encoder round trip error bound, odd/flat sizes, BC1->BC3 and BC3->BC1 transcoding incl. 3-colour/reversed endpoints.
- **Relevance:** Real algorithm checks on texture transcoding (feeds GPU textures). Odd-size, BC1->BC3 equality and 3-colour tests are near duplicates of the two kept.
- **Rendering:** indirect — texture compression/transcode for upload.
- **Verdict:** TRIM (keep: `Encoded_blocks_decode_close_to_the_source`, `Bc3_to_Bc1_keeps_the_colours_including_reversed_endpoints`; ~2 of 5)

### `tests/Meitou.Tests/Textures/DdsReaderTests.cs` — 12 tests
- **Tests:** Hand-built DDS files for BC1/2/3/4/5 decoding, mip chains, cubemaps, DX10 header, uncompressed formats, bad files; plus a `[Slow]` survey of every base-game .dds (header parse, exact file fill, decode sample, pinned counts).
- **Relevance:** Core asset reader. Keep the end-to-end game-install test and the synthetic tests for the formats the game really uses (DXT1/DXT5, odd mips, cube). DX10, BC4/5, BC2, uncompressed and bad-file cases are not used by the game.
- **Rendering:** indirect — every texture the renderer samples.
- **Verdict:** TRIM (keep: `Bc1_four_colour_block_interpolates_thirds`, `Bc3_alpha_has_eight_value_and_six_value_modes`, `Odd_sized_mip_chain_rounds_up_to_whole_blocks`, `Cubemap_has_six_images_each_with_its_mip_chain`, `Reads_every_base_game_dds`; ~5 of 12)

### `tests/Meitou.Tests/Textures/TextureLoaderTests.cs` — 2 tests
- **Tests:** Synthetic DXT1 through `TextureLoader` with all mips; `[Slow]` load of every base-game tga/jpg and a sample of png.
- **Relevance:** First test duplicates `DdsReaderTests`; the second is a real game-install end-to-end check of the non-DDS paths.
- **Rendering:** indirect — texture loading.
- **Verdict:** TRIM (keep: `Loads_base_game_png_tga_and_jpg`; ~1 of 2)

### `tests/Meitou.Tests/Textures/TextureQualityTests.cs` — 7 tests
- **Tests:** Mip-drop rules by quality level/name/group (20-case theory), memory budget constants, `DropTopMips` reslicing and limits, `ReadKept` partial file reads.
- **Relevance:** The drop-rule theory encodes verified game behaviour; `ReadKept` guards real I/O offset logic. Budget constants and the small `DropTopMips` edge cases are low value.
- **Rendering:** indirect — texture quality / VRAM.
- **Verdict:** TRIM (keep: `LevelsToDropFollowsTheListener`, `ReadKeptReadsOnlyTheMipsThatAreKept`; ~2 of 7)

### `tests/Meitou.Tests/Textures/TgaReaderTests.cs` — 3 tests
- **Tests:** TGA origin flip and BGR swap, 32-bit alpha with top-left origin, rejection of truncated/RLE files.
- **Relevance:** Only 11 TGA files in the game; the orientation/BGR test is the one that can silently break an image.
- **Rendering:** indirect — texture reader.
- **Verdict:** TRIM (keep: `Bottom_left_origin_rows_are_flipped_and_colours_are_swapped_from_BGR`; ~1 of 3)

### `tests/Meitou.Tests/Vulkan/BindlessTests.cs` — 4 tests
- **Tests:** Format->bindless-kind mapping, integer textures read back exactly through bindless arrays (GPU), shader declaring the set wrongly is refused (error text), index registration/update/free semantics across frames in flight (GPU, sync validation).
- **Relevance:** The two GPU tests verify real, subtle synchronisation/descriptor behaviour used by the terrain. Mapping and error-message tests are mirrors. `[Slow]`, needs Vulkan device.
- **Rendering:** direct — bindless table.
- **Verdict:** TRIM (keep: `Integer_textures_read_back_exact_values_through_the_bindless_arrays`, `Indices_registered_or_updated_in_a_frame_are_seen_in_that_frame`; ~2 of 4)

### `tests/Meitou.Tests/Vulkan/CoreTests.cs` — 9 tests
- **Tests:** Device feature smoke, randomized allocator coalescing, allocator read-ahead spare blocks, lean allocator, clear+readback, frame-ring deferred deletion (theory x2), timeline semaphore, pipeline cache file round trip, dispose with no validation errors.
- **Relevance:** Mostly Vulkan smoke tests and allocator tuning with sleeps/timeouts (brittle). The randomized allocator test is the one that finds real bookkeeping bugs. All `[Slow]`.
- **Rendering:** direct — GPU memory/frames.
- **Verdict:** TRIM (keep: `Allocator_mixed_sizes_coalesce_on_free`; ~1 of 9)

### `tests/Meitou.Tests/Vulkan/GpuApiTests.cs` — 10 tests
- **Tests:** Legacy program draws interpolated triangle (pixel-checked), 29-program SPIR-V hash golden, prepared pipelines not late, barrier planner table, timestamp ring, upload-after-use throws, compute-written indirect args + bindless draw, PreFrame ordering, buffer arena reuse, secondaries recorded in order on job threads.
- **Relevance:** The triangle, PreFrame and secondary-order tests exercise real GPU paths with validation. The hash golden is refreshed every shader edit (brittle, needs `WorldPrograms()` only for itself and the other golden). The rest are API bookkeeping or overlap with PreFrame (indirect draw).
- **Rendering:** direct — renderer core.
- **Verdict:** TRIM (keep: `Legacy_program_draws_the_interpolated_triangle`, `A_dispatch_recorded_into_PreFrame_runs_before_the_frames_own_commands`, `Secondaries_recorded_on_job_threads_execute_in_the_order_they_were_queued`; ~3 of 10)

### `tests/Meitou.Tests/Vulkan/LegacySpirvGoldenTests.cs` — 1 test
- **Tests:** SHA-256 of every world program's SPIR-V (plain and remapped) against stored hashes.
- **Relevance:** Pure change-detector; hashes were edited by hand on 2026-10-11 twice already. Its stated purpose (VkGl output preservation) is obsolete since VkGl was deleted. `[Slow]`.
- **Rendering:** direct, but only as a shader-text snapshot.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Vulkan/MergedKernelTests.cs` — 3 tests
- **Tests:** Reflection of storage images; binding numbers/sizes of the merged velocity and exposure compute kernels.
- **Relevance:** Asserts binding indices and push-constant sizes of shader text (implementation details, mirror of the C# push code). `[Slow]`.
- **Rendering:** direct, but shader-string checks.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Vulkan/NativeHostTests.cs` — 4 tests
- **Tests:** Host rendering with guest segments (pixel-checked quarters, error states), frame globals default-fill, flush-after-bind texture semantics, profiler timestamps in host pass.
- **Relevance:** The host/guest test is the end-to-end pixel check of the native frame model all passes use. Globals/flush/profiler overlap it and are narrower. All `[Slow]`.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `A_host_clears_and_its_guests_record_into_its_rendering_with_the_viewport_and_state_it_hands_them`; ~1 of 4)

### `tests/Meitou.Tests/Vulkan/NativeShaderTests.cs` — 2 tests
- **Tests:** Native shader port keeps legacy body lines in order (string checks); every native variant compiles and its uniform/push blocks match C# struct field offsets and sizes.
- **Relevance:** The second test is a valuable layout guard between GLSL blocks and C# structs (silent corruption otherwise). The first is shader-text comparison.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `Native_variants_compile_and_their_blocks_match_the_CSharp_structs`; ~1 of 2)

### `tests/Meitou.Tests/Vulkan/OverlayTests.cs` — 1 test
- **Tests:** Debug overlay panel + profiler chart draw natively and PNG screenshot readback equals the texels.
- **Relevance:** Debug UI plus a screenshot helper; asserts "some pixels differ from clear colour". Needs a system monospace font.
- **Rendering:** direct, but diagnostics only.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Vulkan/PostProcessNativeTests.cs` — 1 test (theory, 2 cases)
- **Tests:** Post chain (Off and TAA upscaler) allocates named targets, survives resize, reads back exposure 0.5 for a grey scene, and writes the exact expected composite grey to the caller's target.
- **Relevance:** Genuine end-to-end of the post chain with numeric pixel/exposure checks and sync validation. The allocation-name list is brittle, but the rest is valuable. `[Slow]`.
- **Rendering:** direct.
- **Verdict:** KEEP

### `tests/Meitou.Tests/Vulkan/RayQueryTests.cs` — 2 tests
- **Tests:** BLAS/TLAS built from a triangle, ray hits at expected distance/custom index with front/back culling; refitted BLAS hit at moved height.
- **Relevance:** First is the real acceleration-structure correctness and winding test the probe GI depends on. The refit test repeats its scaffolding. RT per-pixel work is shelved but GI uses these.
- **Rendering:** direct (GI).
- **Verdict:** TRIM (keep: `A_placed_triangle_is_hit_where_it_lies_and_counter_clockwise_from_above_is_its_front`; ~1 of 2)

### `tests/Meitou.Tests/Vulkan/ShaderCompilerTests.cs` — 11 tests
- **Tests:** Every renderer shader pair compiles with matching interfaces and binding ranges (theory, ~28 cases), pair coverage count, Meitou shadow programs compile, vertex attribute locations, terrain/mesh default-block reflection, `FindUniform` name handling, named blocks, error message text, clip-depth remap, disk cache round trip/corruption.
- **Relevance:** The theory is the single best guard against shader breakage in the GLSL files. The rest are reflection details of two shaders, a count of `>= 23`, or cache/error-text behaviour.
- **Rendering:** direct.
- **Verdict:** TRIM (keep: `PairCompilesAndInterfacesMatch`; ~1 of 11)

### `tests/Meitou.Tests/Vulkan/ShaderInterfaceTests.cs` — 5 tests
- **Tests:** The GLSL location-injection rewriter: qualifiers/arrays/matrices/multi-name declarations, explicit locations across stages, const-sized arrays, split declarations, remap keeps main.
- **Relevance:** Nontrivial text-parsing code that the legacy shaders depend on; the first test covers most cases, the others are narrower string checks.
- **Rendering:** direct (shader pipeline).
- **Verdict:** TRIM (keep: `QualifiersArraysMatricesAndMultipleNames`; ~1 of 5)

### `tests/Meitou.Tests/Vulkan/ShaderReflectionTests.cs` — 3 tests
- **Tests:** Unused samplers/blocks reported inactive, use-through-function counts as active, vertex uniform array reflection.
- **Relevance:** Reflection of tiny hand-written programs; the inactive logic is exercised implicitly by compiling every real shader. `[Slow]`.
- **Rendering:** direct, low value.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Vulkan/ShadowPassNativeTests.cs` — 1 test
- **Tests:** The Meitou `ShadowPass` renders four cascades, owns named atlas/blocker allocations, publishes globals, blocker map cleared to 1.0, sync validation clean.
- **Relevance:** Only GPU-level test of the shadow pass; partially a naming/globals check but it does run the pass. `[Slow]`.
- **Rendering:** direct.
- **Verdict:** KEEP

### `tests/Meitou.Tests/Vulkan/SkyWaterNativeTests.cs` — 2 tests
- **Tests:** Native sky/water textures get the same sampler/format/levels GL gave them (compared with an expected `SamplerDesc.FromGl`); sky publishes globals and shadows-off blocks.
- **Relevance:** Written to prove parity with VkGl, which no longer exists (tautological now); second is bookkeeping of published names. `[Slow]`.
- **Rendering:** direct, obsolete purpose.
- **Verdict:** DELETE

### `tests/Meitou.Tests/Vulkan/VolumetricCloudNativeTests.cs` — 1 test
- **Tests:** Volumetric cloud passes under resize/reset/toggles with clean sync validation; clouds change the picture, `--clouds` override and its release, horizon pixels equal fully hazed terrain reference, overcast and fog equivalence across cloud renderers.
- **Relevance:** Large, real pixel-level integration test of a currently active feature, with several hard-won invariants. `[Slow]`.
- **Rendering:** direct.
- **Verdict:** KEEP

### `tests/Meitou.Tests/World/AtmosphereTests.cs` — 12 tests
- **Tests:** SkyX constant values, day sky colour ordering, Mie lobe, sun colour/daylight formulas, exposure band, haze ramp constants, haze growth with distance, haze below-eye clamps, haze boundedness from high up, ambient map built from biomes vs shipped png (game install), base-game default weather, exposure constants from DB.
- **Relevance:** Constant-echo tests (SkyX constants, haze ramp fractions, exposure constants, weather values) are cheap to cut. Keep the formula-level sun/daylight check, the haze consistency and boundedness (a real overview-shot bug), and the ambient-map byte-for-byte check against the game file.
- **Rendering:** direct — sky/haze/lighting math.
- **Verdict:** TRIM (keep: `Sun_colour_and_daylight_scale_follow_the_sky_controller`, `Kenshi_haze_colour_grows_with_distance_to_the_skys_at_the_dome_radius`, `Kenshi_haze_stays_bounded_from_far_above_the_games_camera_heights`, `Ambient_map_built_from_the_biomes_matches_the_shipped_one`; ~4 of 12)

### `tests/Meitou.Tests/World/CloudFogSkipTests.cs` — 3 tests
- **Tests:** The cloud march's fog early-out is exact (texel left out only if every pixel reading it sees no sky; theory, 3 cases), horizon hidden/zenith not, whole-march skip equals shader's weather fog term.
- **Relevance:** Property tests proving an optimisation never changes the picture; they depend on `FogCullTests` helpers (`Alpha`, `Block`, `Atmosphere`, another chunk). Second test is a constant spot check, third mirrors the formula.
- **Rendering:** direct (clouds/fog).
- **Verdict:** TRIM (keep: `A_texel_is_left_out_only_where_every_pixel_that_reads_it_sees_no_sky`; ~1 of 3)

### `tests/Meitou.Tests/World/CloudLayerTests.cs` — 5 tests
- **Tests:** Density offset/darkness formula table, horizon alpha/band table, zenith light, wind offset accumulation, coverage table computed from the shipped Clouds.dds/CloudsTile.dds.
- **Relevance:** Four tests echo formulas and constants from the exe; the coverage table against the real textures is the one that verifies data end to end. `[Slow]`.
- **Rendering:** direct (clouds), indirect for the data check.
- **Verdict:** TRIM (keep: `Coverage_table_matches_the_shipped_textures`; ~1 of 5)

### `tests/Meitou.Tests/World/FlowMapTests.cs` — 1 test
- **Tests:** An analysis of `flowmap.png` against the heightmap that mostly prints statistics and optionally writes PNGs; asserts a few "claims" (flow does not follow channel, B higher in rivers).
- **Relevance:** Research/exploration script posing as a test, 200 lines, `[Slow]`, depends on install; findings are already in docs/formats/terrain.md.
- **Rendering:** indirect (water flow data).
- **Verdict:** DELETE

### `tests/Meitou.Tests/World/FogCullDirectionTests.cs` — 4 tests
- **Tests:** Direction-based fog hide-distance finds a distance at Shark where the plain bound finds none; randomized property test that all paths past R are covered by the shader's formula; edge cases (no distance/budget); the frame's cull uses the smaller bound.
- **Relevance:** The randomized property test (about 20k rays) guards the math that decides what is culled and so what is visible. The rest are narrow edge cases or are covered by it.
- **Rendering:** direct (fog culling).
- **Verdict:** TRIM (keep: `Every_path_past_the_distance_from_any_eye_within_the_margin_is_covered_by_the_shaders_formula`; ~1 of 4)

**Chunk totals:** 140 tests, keep ~37.


### `tests/Meitou.Tests/World/FogCullTests.cs` — 8 tests
- **Tests:** The fog-volume cull: which boxes inside a fog block are hidden; the weather-fog cull distance; hide-distance maths.
- **Relevance:** The property test (`Everything_the_cull_hides...`) checks the cull against the shader's own alpha formula with random boxes, so it guards a real visual-correctness bound. The rest are example-based variants and constants of the same logic. `FogCullDirectionTests.cs` (not in this chunk) likely overlaps.
- **Rendering:** direct, culling for fog volumes.
- **Verdict:** TRIM (keep: `Everything_the_cull_hides_is_covered_by_at_least_0_9998_alpha_by_the_shaders_own_formula`; ~1 of 8)

### `tests/Meitou.Tests/World/FogFeatureTests.cs` — 11 tests
- **Tests:** `fogfeatures.dat` reader (hand-built file), block geometry, volume ordering and packing, effect fades, shader-array-size string check, one install test (Shark inside the swamp's fog).
- **Relevance:** The reader test and the install test exercise the real format. Ordering, fade, packing and the shader-string check mirror implementation or constants.
- **Rendering:** direct, fog volumes.
- **Verdict:** TRIM (keep: `Reads_every_type_with_the_games_field_order`, `The_installs_file_puts_Shark_inside_the_swamps_fog`; ~2 of 11)

### `tests/Meitou.Tests/World/FogSkyTests.cs` — 3 tests
- **Tests:** Sky early-out when fog volumes cover the view; sampled bound on exit distance; one example case.
- **Relevance:** The randomized soundness test (`Where_the_sky_counts_as_hidden...`) covers the other two. The exit-bound test is an optimization bound, and the third is an example.
- **Rendering:** direct, sky and fog.
- **Verdict:** TRIM (keep: `Where_the_sky_counts_as_hidden_every_ray_of_the_view_is_fogged_at_the_far_clip_by_the_shaders_formula`; ~1 of 3)

### `tests/Meitou.Tests/World/FoliageLayoutCacheTests.cs` — 10 tests
- **Tests:** On-disk cache of foliage layouts: round trip, grouped read, key derivation, wrong-key/zone rejection, truncated/damaged files, disabled, maintenance/eviction, code-hash vs. source files, one real-game round trip.
- **Relevance:** This is a startup-speed cache, not rendering output. The round-trip and real-zone tests catch real serialization bugs. The key, rejection, maintenance and code-hash tests are plumbing, and the code-hash test walks the source tree (brittle).
- **Rendering:** indirect, caches foliage placement.
- **Verdict:** TRIM (keep: `A_saved_zone_reads_back_equal_in_a_new_cache`, `Real_zones_read_back_equal_to_the_ones_just_placed` [Slow]; ~2 of 10)

### `tests/Meitou.Tests/World/FoliageTests.cs` — 13 tests
- **Tests:** MT19937 foliage RNG and exe-derived noise hash, layer seed, ground height/slope, grass blade generation, visibility ranges, plus 4 Slow game tests (catalog and zone load, far-only equals whole layout, grass footprint).
- **Relevance:** The RNG and hash tests pin facts that are hard to rediscover (exe multipliers). The Slow far-only equivalence and zone-load tests are real end-to-end checks. The rest are small unit or constant checks.
- **Rendering:** indirect, foliage placement.
- **Verdict:** TRIM (keep: `Random_is_the_standard_mersenne_twister`, `Noise_hash_uses_the_exe_multipliers`, `Catalog_and_a_zone_load_from_the_game`, `Far_only_layout_places_exactly_the_far_instances_of_the_whole_layout`; ~4 of 13)

### `tests/Meitou.Tests/World/HeatHazeTests.cs` — 3 tests
- **Tests:** Heat-haze target and step formulas; base-game weather haze values and haze texture format.
- **Relevance:** The two formula tests restate the implementation. The Slow test checks the real records and DDS files.
- **Rendering:** direct, heat haze post effect.
- **Verdict:** TRIM (keep: `Base_game_weathers_and_textures` [Slow]; ~1 of 3)

### `tests/Meitou.Tests/World/LevelFileTests.cs` — 8 tests
- **Tests:** `.zone`/`.level` file round trip (formats 10-16), trailer rules, legacy format 13, zone file names, and 3 Slow tests against the install (all 715 files parse and round-trip, placements consistent, heights vs heightmap).
- **Relevance:** The Slow tests are the valuable ones, since they cover every shipped file. The hand-built tests are subsumed by "every base game level file round-trips".
- **Rendering:** indirect, world placements (buildings, roads, towns) feed object rendering.
- **Verdict:** TRIM (keep: `Every_base_game_level_file_parses_to_its_end_and_round_trips`, `Base_game_world_placements_are_consistent`; ~2 of 8)

### `tests/Meitou.Tests/World/MapFeatureFileTests.cs` — 2 tests
- **Tests:** MapFeature file reader on a synthetic file, and the base game's file (zones, quaternions, record types).
- **Relevance:** The Slow install test covers the reader end to end. The synthetic one is redundant.
- **Rendering:** indirect, placed map features.
- **Verdict:** TRIM (keep: `Base_game_features_lie_in_their_zones_and_name_map_features` [Slow]; ~1 of 2)

### `tests/Meitou.Tests/World/MeitouCloudsTests.cs` — 11 tests
- **Tests:** The Meitou-only cloud lighting model: phase function, transmittance, key light by day/night, ambient grey, shadow plane height/sweep/fade, parallax, smoothing, switch wiring.
- **Relevance:** Mostly tuning constants and range checks (`InRange`) of an aesthetic invented here. The parallax-consistency test and `Every_switch_is_wired...` (a real regression from 2026-10-11) are worth keeping.
- **Rendering:** direct, but Meitou-only extras.
- **Verdict:** TRIM (keep: `The_sky_sees_the_same_plane_point_the_shadow_reads`, `Every_switch_is_wired_in_the_options_and_the_live_list`; ~2 of 11)

### `tests/Meitou.Tests/World/MeitouNightSkyTests.cs` — 15 tests
- **Tests:** Meitou night sky: celestial frame and pole maths, star rise/set direction, air mass and transmission curves, scintillation, star field cube mapping, counts, evenness, edge margin, hash quality, brightness, colours, switch default.
- **Relevance:** A custom procedural star system; most tests are range or constant checks. The star-direction test and the sphere-uniformity test catch real math bugs.
- **Rendering:** direct, but Meitou-only.
- **Verdict:** TRIM (keep: `Stars_rise_in_the_east_and_set_in_the_west_like_the_sun`, `Star_density_is_even_over_the_sphere_not_denser_at_the_centres_of_cube_faces`; ~2 of 15)

### `tests/Meitou.Tests/World/NightAirTests.cs` — 10 tests
- **Tests:** Meitou night-air glow: airglow plus planet light, crescent vs gibbous, weight in twilight, horizon falloff, radiance, haze brightness through twilight, Lambert disc radiance, glint fallback, planet veil.
- **Relevance:** Aesthetic tuning with many magic ranges. The twilight monotonic/no-jump sweep guards a visible pop, so it stays.
- **Rendering:** direct, but Meitou-only.
- **Verdict:** TRIM (keep: `The_far_haze_never_gets_brighter_than_it_was_at_the_horizon_through_twilight`; ~1 of 10)

### `tests/Meitou.Tests/World/NightGradeTests.cs` — 10 tests
- **Tests:** Meitou scotopic night grading: rod weights, tint, adaptation weight, pixel weight, Apply behaviour, composite-shader constant text, preset defaults.
- **Relevance:** Constants and shader-string checks (`Composite_shader_carries_the_same_constants`) are the typical low-value kind. One behaviour test of `Apply` is enough.
- **Rendering:** direct, but Meitou-only post effect.
- **Verdict:** TRIM (keep: `At_night_orange_ground_goes_blue_grey_at_nearly_the_same_brightness`; ~1 of 10)

### `tests/Meitou.Tests/World/NightSkyTests.cs` — 4 tests
- **Tests:** Game starfield dome UV mapping, planet placement constants, planet spin, and a Slow check of the planet mesh and textures against the install.
- **Relevance:** The dome mapping reproduces the game's sky. The mesh/texture test is end-to-end. Planet placement and spin are constants.
- **Rendering:** direct, game sky.
- **Verdict:** TRIM (keep: `Starfield_maps_the_dome_round_the_zenith`, `Planet_mesh_is_a_uv_sphere_of_the_recorded_radius` [Slow]; ~2 of 4)

### `tests/Meitou.Tests/World/ObjectLodTests.cs` — 9 tests
- **Tests:** Mesh LOD selection and cross-fade blending, part rendering distance, edge/rise weights, and 4 Slow install tests (distant mesh placement, distant town meshes, vertex colours, LOD ordering).
- **Relevance:** Blend continuity guards visible LOD pops. The distant-town test is a substantial end-to-end check against the real data. Constants-style tests (part distance, weights, vertex-colour sampling) can go.
- **Rendering:** direct, object LOD.
- **Verdict:** TRIM (keep: `Blend_is_the_games_choice_away_from_the_bands_and_continuous_across_them`, `Base_game_distant_town_meshes_are_the_merged_distant_meshes_of_their_buildings`, `Building_part_meshes_with_lod_levels_read_into_ascending_distances`; ~3 of 9)

### `tests/Meitou.Tests/World/ParticleTests.cs` — 22 tests
- **Tests:** ParticleUniverse script parser, dynamic curves/splines, emitter sizing, simulation determinism/quota/wrap, colour ramp, weather effect groups (region area, altitude placement, respawn, map placers, backlog), and 4 Slow install tests (every shipped script reads, drifting foliage, forced weather effects, twister fog).
- **Relevance:** The parser and the "all 93 shipped scripts read" test are valuable, as are the two bug-guard tests (parametric splines, scale-affector precedence). The group, placement and wrapping tests are mostly behaviour examples of internals.
- **Rendering:** indirect, particle effects (rain, dust).
- **Verdict:** TRIM (keep: `Script_reads_blocks_properties_and_dynamic_attributes`, `Splines_are_parametric_over_the_x_range`, `Emitter_sizes_and_the_scale_affector_follow_the_plugin`, `Every_shipped_script_and_material_reads` [Slow], `Forced_weather_gives_its_camera_effects` [Slow]; ~5 of 22)

### `tests/Meitou.Tests/World/PlanetshineTests.cs` — 10 tests
- **Tests:** Meitou planetshine: phase angle, Lambert phase, solid angle ratio, dusk vs dawn, light scaling, weight, combine, a smooth dusk/dawn sweep (Theory), mean albedo, Slow albedo range of the real texture.
- **Relevance:** Meitou-only lighting; most tests are small formula or constant checks. The dusk/dawn sweep is the only one guarding an actual visible artifact (pop), so it stays.
- **Rendering:** direct, but Meitou-only.
- **Verdict:** TRIM (keep: `The_light_through_dusk_and_dawn_changes_smoothly_and_turns_in_the_dark`; ~1 of 10)

### `tests/Meitou.Tests/World/ShadowTests.cs` — 14 tests
- **Tests:** Cascaded shadow maps: split scheme, map size table, light rotation, cascade frustum fit, texel snapping, unused cascades, receiver matrix, cascade select, horizontal light, PCF offsets, stable size, Meitou landmark box, shimmer when the sun turns.
- **Relevance:** This is real algorithmic code. The frustum-fit, texel-snap, sun-slide and landmark-box tests guard hard-to-find shimmer and coverage bugs. Split, size-table, PCF-offset and stable-size tests mirror formulas.
- **Rendering:** direct, shadows.
- **Verdict:** TRIM (keep: `Every_cascade_holds_its_frustum_from_the_near_plane_to_its_split`, `Moving_the_camera_moves_the_map_by_whole_texels_only`, `A_turning_sun_does_not_slide_the_meitou_grid_far_from_the_origin_when_it_is_anchored_near_the_eye`, `Landmark_box_holds_every_landmark_and_its_sunward_side_is_nearer`; ~4 of 14)

### `tests/Meitou.Tests/World/TerrainBiomeTests.cs` — 4 tests
- **Tests:** Hand-built blend-info reader, and 3 Slow install tests (slot masks vs blend map, blend map channels vs biome map and BIOMES records, overlay alpha marks roads).
- **Relevance:** The Slow tests are strong end-to-end checks of the terrain texturing data. The synthetic reader test is redundant with them.
- **Rendering:** direct, terrain texturing.
- **Verdict:** TRIM (keep: `Base_game_slot_masks_describe_the_blend_map`, `Base_game_blend_map_channels_weight_the_cell_slots`; ~2 of 4; `Base_game_overlay_alpha_marks_roads` is a reasonable third if wanted)

### `tests/Meitou.Tests/World/TerrainLodTests.cs` — 7 tests
- **Tests:** Screen-space error LOD threshold and split distances, level ranges, no-getting-finer-with-distance, no pops while the eye flies, and crack-free adjacent levels over many (rule x terrain x eye) combos.
- **Relevance:** The two Slow tests (`Moving_the_eye...`, `Touching_squares...`) hold the real value: they catch visual popping and cracks. The threshold, range and split tests are formula restatements. `Touching_squares...` is a superset of the check in `TerrainQuadtreeTests.Selected_nodes_tile...`.
- **Rendering:** direct, terrain LOD.
- **Verdict:** TRIM (keep: `Touching_squares_differ_by_one_level_and_meet_without_cracks` [Slow], `Moving_the_eye_changes_the_surface_smoothly_without_pops` [Slow]; ~2 of 7)

### `tests/Meitou.Tests/World/TerrainQuadtreeTests.cs` — 10 tests
- **Tests:** Height window coordinates and normals, grid triangle winding and quadrants, height bounds (coarse, fine, late apply), quadtree level structure, tiling and crack-freedom (Theory), culling, heightmap window reads, and a Slow check against the install.
- **Relevance:** `Selected_nodes_tile...` is a fast crack/tiling test (overlaps with TerrainLod's Touching test, which is richer but Slow). Height bounds guard culling correctness. The rest are small helpers.
- **Rendering:** direct, terrain.
- **Verdict:** TRIM (keep: `Selected_nodes_tile_the_world_and_meet_without_cracks`, `Height_bounds_cover_the_coarse_grid_and_the_fine_window`, `Base_game_window_matches_point_samples` [Slow]; ~3 of 10)

### `tests/Meitou.Tests/World/TerrainTests.cs` — 7 tests
- **Tests:** TIFF reader (byte orders, strips, rejection), heightmap bilinear sampling, world layout constants, raw tile reader, Slow install tests (heightmap geometry, legacy tiles share edges).
- **Relevance:** The TIFF reader and heightmap interpolation are the foundation of terrain. The install tests validate real data. `World_layout_maps_points...` mostly asserts constants. `Raw_tile` and the reject test are minor.
- **Rendering:** direct, terrain heightmap.
- **Verdict:** TRIM (keep: `Tiff_reader_reads_samples_in_either_byte_order_and_strip_layout`, `Heightmap_interpolates_world_heights_bilinearly`, `Base_game_heightmap_covers_the_zone_grid` [Slow]; ~3 of 7)

### `tests/Meitou.Tests/World/VolumetricCloudsTests.cs` — 4 tests
- **Tests:** Cloud slab intersection, periodic noise, wind-wrap continuity, atlas padding wrapping.
- **Relevance:** Seam-continuity tests (atlas padding, wrap) guard artifacts that are hard to spot. Slab intersection and plain periodicity are simple.
- **Rendering:** direct, but Meitou-only volumetric clouds.
- **Verdict:** TRIM (keep: `Atlas_padding_wraps_each_slice_without_leaking_into_another_slice`, `Wrapping_the_weather_wind_keeps_every_density_band_continuous`; ~2 of 4)

### `tests/Meitou.Tests/World/WaterTests.cs` — 3 tests
- **Tests:** SkyClock sun direction and phase; Slow base-game water parameters and colour map; Slow "low buildings are water structures" (observed trivia).
- **Relevance:** The SkyClock test is the sun-path foundation used by many sky features. The water-parameter test validates real records and the colour map. The third is a loose observation (count ranges) with little regression value.
- **Rendering:** direct, water and sky.
- **Verdict:** TRIM (keep: `Sun_rises_at_plus_x_and_culminates_at_the_latitude_angle`, `Base_game_water_parameters_and_colour_map` [Slow]; ~2 of 3)

### `tests/Meitou.Tests/World/WeatherGroupsTests.cs` — 14 tests
- **Tests:** Orchestration of weather particle groups: staged warm-up, cancellation, first groups, background build and swap, region dwell, flip-flop, newer change, strength passthrough, no-background mode, forced weather, frozen ground height in a worker, world copy, simulator thread.
- **Relevance:** Mostly scheduler state-machine tests with `Thread.Sleep` polling and implementation details. The staged-equals-prewarm test and the thread-safety frozen-ground-height test guard real bugs.
- **Rendering:** indirect, weather particles.
- **Verdict:** TRIM (keep: `Staged_warm_up_queues_the_time_and_simulates_to_the_same_particles_as_prewarm`, `A_background_warm_up_reads_the_frozen_ground_height_and_the_groups_go_back_to_the_live_world`; ~2 of 14)

### `tests/Meitou.Tests/World/WeatherTests.cs` — 31 tests
- **Tests:** Weather scheduler: season calendar, weighted picks, rerolls, time windows, durations, strength, determinism, snapshots, wind, fog distance/fade/blending, camera region switching, sky fade, rain, wetness/dust ramps, frame times, time conversion, area cells, and 4 Slow install tests (town to region map, loader rules, seasons/regions, whole-year run).
- **Relevance:** This is sim-ish, but the output drives fog, rain, sky and wetness in rendering. The Slow install tests validate the real data. Many fine-grained hand-made tests overlap each other (wind, ramps, time, frame times).
- **Rendering:** indirect, weather feeds fog, sky, rain.
- **Verdict:** TRIM (keep: `Season_lengths_are_shares_of_the_year_rounded_with_the_last_taking_the_rest`, `Fog_weights_blend_the_regions_of_the_four_corner_samples`, `Areas_map_places_known_towns_in_their_regions` [Slow], `Base_game_records_follow_the_loader_rules` [Slow], `Base_game_seasons_and_regions_follow_the_calendar_and_fallback_rules` [Slow], `The_whole_base_world_runs_for_a_year_with_sane_values_and_is_deterministic` [Slow]; ~6 of 31)

### `tests/Meitou.Tests/World/WorldLightTests.cs` — 7 tests
- **Tests:** Placing lights on building parts (transforms, scale), RNG draw order for lights, attenuation curve, spot factor, daylight ramp and effects, Slow base-game light placement.
- **Relevance:** The RNG-order test and the Slow placement test guard fidelity with the original. Attenuation, spot and daylight tests are small formula checks.
- **Rendering:** direct, lights.
- **Verdict:** TRIM (keep: `Each_light_draws_its_power_roll_and_a_second_roll_before_later_part_choices`, `World_lights_are_placed_on_buildings_and_layouts_around_the_hub` [Slow]; ~2 of 7)

### `tests/Meitou.Tests/World/WorldObjectLayoutTests.cs` — 9 tests
- **Tests:** Instance transform, MSVC `rand()` sequence, seed hashing, part choice rolls and group order, and 4 Slow install tests (buildings have meshes and materials, town handles, destroyed buildings, layout names), plus a layout placement unit test.
- **Relevance:** Reproducing the game's RNG, part-choice and group order is essential for drop-in fidelity. The base-game building test is the key end-to-end guard. The town, destroyed and layout-name tests are observed-count trivia, and the transform ones are trivial.
- **Rendering:** direct, building and object placement.
- **Verdict:** TRIM (keep: `Random_is_the_msvc_rand_sequence`, `Part_choice_follows_the_games_rolls`, `Base_game_buildings_have_meshes_and_materials` [Slow]; ~3 of 9)

**Chunk totals:** 259 tests, keep ~62.

---

# Part 2: launch options and env vars

An audit of every `--flag` and environment variable read in `src` and `tools`: **415 items**. That is
about 235 flags and 180 env vars (`MEITOU_*`, plus `NAV_DUMP`/`NAV_VERBOSE`). Five reviewers traced
each item to every place it is read or mentioned: code, tests, scripts and docs. Nothing has been
changed yet.

Verdicts: **KEEP**, **MERGE** (fold into the named option) or **REMOVE**. REMOVE also deletes the code
the option gates. Where an option is a switch, the default it has today becomes hardcoded.

## Summary

| Group (defining files) | Items | Keep | Merge | Remove |
|---|---|---|---|---|
| 0: `Meitou.Data` … `FoliageRenderer.Lod.cs` (game host, foliage, impostors, JIT, caches) | 81 | 25 | 9 | 47 |
| 1: `FoliageRenderer.Lod.cs` … `PostProcessOptions.cs` (GI, LOD gen, post, mips) | 75 | 6 | 11 | 58 |
| 2: `PostProcessOptions.cs` … `WaterRenderer.cs` (post switches, shadows, terrain, reflections) | 78 | 9 | 9 | 60 |
| 3: `WaterRenderer.cs` … `WorldFrame.cs:486` (world options, quality flags) | 91 | 37 | 10 | 44 |
| 4: `WorldFrame.cs:487` … tools (bench, debug, game host, navmesh, weather) | 90 | 41 | 11 | 38 |
| **Total** | **415** | **118 (28%)** | **50** | **247 (60%)** |

Most of the 247 removals are env vars: almost all of the roughly 180 env vars go.

## Main findings

**What stays:**
- The user-facing quality and window settings.
- The upscaler (`--upscaler`, `MEITOU_STREAMLINE_PATH`).
- `--meitou`/`--faithful <id>`, `--low-end`, `--ab` and the bench harness entry points.
- A few diagnostics worth keeping, such as `--log-spikes`.

**Startup env vars for `--ab` switches:** about 30+ env vars only set the starting value of a switch
`--ab` already toggles at runtime. These cover GI, post-merge and layers, the impostor prepass and
quad, object LOD gen, shadow coarse/fogcap/budget, fog skip, terrain late and layer skip, and water
lean. Remove the env var and hardcode the default.

**Standalone `--no-X` flags:** `--ab` already covers every "turn it off to compare" case: fog cull,
fog volumes, occlusion, fog-vrs, lights, reflections, water-lean, occlusion vs `OCC_ALT`. The
standalone `--no-X` and env forms are redundant.

**Duplicates of `--meitou/--faithful <id>`:**
- `--dither`, `--particles-low`, `--shafts`, `--ssao` (and their `--no-` forms), and `--no-night-grade`.
- `--low-end` should use `--meitou particles`.
- Fold `--particles-low` and `--particle-divisor` into `--particle-density`.

**Flags and env vars that do the same thing:**
- `--no-load-cache` and `MEITOU_NO_LOAD_CACHE`.
- `--impostor-budget` and `MEITOU_IMPOSTOR_BUDGET_MB`.
- `--impostor-cache-mb` and `MEITOU_IMPOSTOR_CACHE_MB`.
- `--grass-density` and `MEITOU_GRASS_DENSITY`.
- `--log-spikes` and `MEITOU_LOG_SPIKES`.
- `MEITOU_GPU_GRASS` is the same as `MEITOU_GPU_CULL=0`.

**Families to merge into one option:**
- **Caches:** six cache-dir overrides become one `MEITOU_CACHE_DIR` (plus `--no-load-cache`).
- **Debug views:** `--debug`, `--wireframe`, `--debug-shadows`, `--gi-debug`, `LOD_DEBUG`, `WATER_DEBUG`,
  `FOLIAGE_DEBUG` and `FFX_DEBUG` become one `--debug-view <name>`.
- **Bench skip frames:** `PASS_STATS_SKIP`, `BENCH_SKIP` and `SPIKE_SKIP` become one setting.
- **Bench motion:** `--fly-benchmark` and `--bench-frames` overlap. `MEITOU_FLY_TURN` and
  `MEITOU_BENCH_ORBIT` fold into `--bench-motion`. `--fly-pipelined` is the inverse of `--bench-serial`.
- **VRAM:** `VRAM_KILL` folds into `VRAM_GUARD`, alongside `VRAM_BUDGET_MB`.
- **Logs:** `MEITOU_STREAM_LOG` and `MEITOU_PARTICLE_LOG` fold into `--log-spikes`.

**Families to remove together:**
- the nine `MEITOU_TRIM_*` and `MEITOU_CARD_TRIM`;
- the `MEITOU_IMPOSTOR_*` kill-switches and atlas knobs;
- `MEITOU_CHARACTER_*` and `MEITOU_CROWD_ANIMATE`;
- `MEITOU_MESH_STREAM` and `MEITOU_MIP_*` (log, skip, margin, top);
- the CPU reference paths `MEITOU_GPU_CULL`, `_VERIFY` and `MEITOU_GPU_GRASS`;
- the four JIT flags, `JitSwitches.cs` and `MEITOU_JIT_RELAUNCHED`;
- `MEITOU_TERRAIN_PROBE*` (about 116 lines plus their branches);
- the `MEITOU_REFL_*` helper family;
- the foliage and terrain `*_TIMING`/`*_LOD_LOG`;
- `MEITOU_ALLOC_STATS`, `MEITOU_JOB_STATS` and `MEITOU_FOLIAGE_TRIS`;
- `MEITOU_DRAW_LOG`, which only existed for VkGl parity;
- `MEITOU_LANDMARK_SURVEY`;
- the terrain ramp flags.

**Dead or ignored:**
- **No effect:** `--renderer` (also in `ImpostorApp`), `--gi`, `--fog-vrs`, `--info` and
  `--no-textures`. `--world` only picks the viewer mode.
- **Game host:** it ignores `--windowed` and `--monitor`. `--free-camera`, `--no-navmesh`,
  `--body-time-scale` and `--attack-nearest` are referenced only by `GameOptions`.
- **Settings file duplicates:** the game's `--fps-limit`, `--tick-rate`, `--sim-threads` and `--vsync`
  repeat `meitou.user.json`.
- **Not an option:** `MEITOU_FRAGMENT` is a shader macro.
- **Sharpness:** DLSS never sets it, so `--sharpness` only affects FSR.

**Stale docs and help text:**
- **`--faithful` help:** it lists a `dust` id that doesn't exist and leaves out 13 real ids.
- **Wrong defaults in Usage:** `--water-reflection`, `--reflection-range` and `--texture-quality`.
- **Water grid:** `docs/render-water.md:273` says the default grid is 128; the code says 256.
- **Bench doc:** `docs/bench.md:80` cites `MEITOU_REFL_MOVING` and `MEITOU_REFL_MOVE_FORCE`, which
  exist nowhere in the code.

**Dependencies to respect when removing:**
- **`--low-end`:** it expands into about 20 other flags (`WorldOptions.LowEnd`,
  `WorldFrame.cs:205-219`), and `BenchHarnessTests` asserts the expansion. Make it assign fields
  directly before cutting the flags it uses.
- **Tab panel:** it isn't saved anywhere, so for a quality value a launch flag is today the only way
  to start with something other than the default. Don't drop real quality choices until settings
  persist; the audit removed only the fine-tuning knobs.
- **Tab slider duplicates:** about 13 flags also exist as Tab sliders (`--shafts-*`, `--tonemap*`,
  `--night-grade`, `--heat-haze-strength`, `--sharpness`). Remove the flag and keep the slider.

## Per-option audit


Worktree `R:\VlcekM\MeitouClient-cleanup`, read-only. "Used by" means tests, scripts, docs and other code found by grep over src, tools, tests, docs, README/ROADMAP/CHANGELOG/DECISIONS; `.claude` does not exist in the worktree. Items are grouped by the file that defines (parses or reads) them; where the first-seen file in `grp00` is only a comment or a caller, that is noted.

Cross-group notes that affect these verdicts are collected at the end.

### src/Meitou.Data/World/FoliageLayoutCache.cs

- **`MEITOU_NO_LOAD_CACHE`** (env) — default unset (caches on); *debug kill-switch*; used by docs (`docs/formats/foliage.md:443`, `WorldFrame.cs:235` help text); rendering: indirect (start-up time). Exact duplicate of the flag `--no-load-cache` (`WorldFrame.cs:383` sets the same `LoadCaches.Disabled`). **Verdict:** REMOVE — keep the flag; change `LoadCaches.Disabled` to a plain `{ get; set; } = false`, delete the env read and the `; MEITOU_NO_LOAD_CACHE=1 too` in the help text and docs.
- **`MEITOU_FOLIAGE_CACHE`** (env) — default `%LOCALAPPDATA%\Meitou\foliage`; *tuning knob* (cache folder); used by docs (`foliage.md:425`) only, no test sets it (the constructor takes a `root` argument, which tests use); rendering: none. **Verdict:** REMOVE — delete the env fallback in the `FoliageLayoutCache` constructor (`FoliageLayoutCache.cs:62`), keep the default path and the `root` parameter. If a movable cache root is ever wanted, one shared `MEITOU_CACHE_DIR` for all caches is better (see cross-group notes).
- **`MEITOU_FOLIAGE_CACHE_MB`** (env) — default 2048; *tuning knob*; used by docs (`foliage.md:426`) only; rendering: none. **Verdict:** REMOVE — hardcode `DefaultMaxBytes` (constructor already takes `maxBytes`); delete the env parse at `FoliageLayoutCache.cs:64` and the doc sentence.

### tools/Meitou.ModelViewer/AllocationLog.cs (first seen in src/Meitou.Data/World/ZoneArrays.cs:6, a comment)

- **`MEITOU_ALLOC_STATS`** (env) — default off; *debug/diagnostic* (every GC and allocation by thread and type, from runtime events, with `--fly-benchmark`); used by docs (`docs/engine.md:258,273`, `docs/renderer-native.md:3565,3588`) and one call in `WorldApp.Benchmark.cs:41`; rendering: indirect (stutter hunting). The plain `gc` and `flight` lines that `--fly-benchmark` always prints already give collections, pause total and allocated MB. **Verdict:** REMOVE — delete `AllocationLog.cs` (126 lines), `allocLog` use in `WorldApp.Benchmark.cs`, and the doc mentions; the comment in `ZoneArrays.cs:6` can lose the parenthesis.

### src/Meitou.Rendering/WorldFrame.cs (`--faithful` / `--meitou`; first seen in src/Meitou.Game/GameHost.Interactive.cs:53)

- **`--faithful`** (flag) — default none (all Meitou); *user-facing Faithful/Meitou switch* (`<all|id,id...>`, parsed at `WorldFrame.cs:438`, `Enhancements.Apply`); used by tests (`MeitouCloudsTests`, `LandmarkTests`), `--low-end` expansion, `GameHost.Interactive.cs:53` (skips saved switches when given), `tools/scripts` (`parity.sh` runs via extra args), ~15 docs; rendering: direct. **Verdict:** KEEP. Note the usage text (`WorldFrame.cs:289`) lists ids `ao,dither,haze,aa,shadows,range,impostors,dust,reach`; the real ids (`Enhancements.Create`) are `ao dither haze aa shadows range impostors reach water particles lod gi tonemap shafts heathaze night planetshine stars nightgrade clouds cloudshadows`, and `dust` does not exist. Fix the help text.
- **`--meitou`** (flag) — default none; *Faithful/Meitou switch* (inverse of `--faithful`, so options after `--low-end` can turn one back on); used by `GameHost.Interactive.cs:53`, docs (`engine.md:105`); rendering: direct. **Verdict:** KEEP.

### src/Meitou.Game/GameOptions.cs

The game (`meitou.exe`) is a simulation host on top of the renderer, so rendering relevance is none/indirect for most of these. Five of them (`--fps-limit`, `--vsync`, `--tick-rate`, `--sim-threads` and the saved Tab sliders) also live in `meitou.user.json` (`UserConfig`), which the command line only overrides.

- **`--fps-limit`** (flag) — default 240 from `meitou.user.json`; *user-facing setting* (frame cap when vsync is off); used by `GameHost.Interactive.cs:28,236`, docs (`engine.md:60`), no tests; rendering: indirect. **Verdict:** KEEP.
- **`--tick-rate`** (flag) — default 30 (config `tickRate`); *tuning knob* of the control-tick rate; used by `GameHost.cs:98`, `engine.md:26`; rendering: none. **Verdict:** MERGE → `meitou.user.json` `tickRate` (already read as `g.TickRate ?? config.TickRate`); delete the parse line, the `TickRate` field and the usage line.
- **`--sim-threads`** (flag) — default half the cores, 1 to 8 (config `simThreads`); *tuning knob* (result never depends on it); used by `GameHost.cs:100`, `engine.md:55`, `simulation.md:122,680` (benchmark table with 1 vs 8 threads); rendering: none. **Verdict:** MERGE → `meitou.user.json` `simThreads`; delete the parse line and field, keep the config path.
- **`--seed`** (flag) — default 0; *user-facing setting* (world seed into `WorldSettings.Seed`); used by `GameHost.cs:100`, `engine.md:55`; the same name in `meitou-tools weather` is a different option; rendering: none. **Verdict:** KEEP (one line, real game input).
- **`--vsync`** (flag) — default off (config `vsync`); *user-facing setting*; used by `GameHost.Interactive.cs:27`, `engine.md:60`; rendering: indirect. **Verdict:** KEEP.
- **`--no-vsync`** (flag) — default off; *user-facing setting*; also parsed by the shared `WorldFrame.cs:470` for the viewer, and expanded by `--low-end`. In the game `GameOptions` takes it first and the game ignores `o.VSync` (it reads `g.VSync ?? config.VSync`), so `--low-end` does not turn vsync off in the game. **Verdict:** KEEP (pair with `--vsync`).
- **`--ticks`** (flag) — default 0; *bench/screenshot harness* (control ticks to run before a `--screenshot`); used by `GameHost.Screenshot.cs:37`, `engine.md:576`, `simulation.md:641`; rendering: indirect. **Verdict:** KEEP.
- **`--free-camera`** (flag) — default off; *debug*; used by `GameHost.cs:131` only (no docs besides usage, no tests); the `;` key toggles the same camera at run time. **Verdict:** REMOVE — delete the parse line, `FreeCamera` field, usage line and the 5-line block at `GameHost.cs:131-136`.
- **`--no-population`** (flag) — default off; *bench/isolation switch* (empty world: no residents, no character renderer, no sim cost); used by `GameHost.cs:72,126`, `Program.cs:69` (conflicts with `--new-game`), `engine.md:50`, `simulation.md:649`; rendering: indirect (isolates renderer cost). **Verdict:** KEEP.
- **`--no-navmesh`** (flag) — default off; *debug* (open-ground walkability, "as in the tests"); used by `GameHost.cs:75`, `simulation.md:338,648`; tests use `OpenGroundWalkability` directly, not the flag; rendering: none. **Verdict:** REMOVE — hardcode the navmesh path (keep `!IsSandbox`), delete the field, parse and usage lines.
- **`--body-time-scale`** (flag) — default 1; *tuning knob* for body/blood rates; used by `GameHost.cs:88` and `simulation.md:456`; no tests set it through the flag; rendering: none. **Verdict:** REMOVE — delete the flag, field and usage line; `MedicalContext.BodyTimeScale` / `StandardSystemOptions.BodyTimeScale` stay (tests may build them).
- **`--new-game`** (flag) — default off, optional start name (default Wanderer); *user-facing* (entry to the real game); used by `Program.cs:53`, `GameHost.Screenshot.cs`, `simulation.md:274`; rendering: none. **Verdict:** KEEP.
- **`--list-starts`** (flag) — default off; *user-facing helper*; used by `Program.cs:57,66`, `simulation.md:274`; rendering: none. **Verdict:** KEEP (could become `--new-game list`, not worth it).
- **`--sandbox`** (flag) — default off, count 1 or 2; *animation debug map* (flat wireframe floor, real sim and character renderer); used by `Program.cs:41`, `GameHost.Sandbox.cs`, `PopulationSystem.Sandbox.cs`, `simulation.md:621`, CHANGELOG; rendering: indirect (character animation pictures). **Verdict:** KEEP.
- **`--sandbox-character`** (flag) — default first squad member of the default start; *harness*; used by `GameHost.Sandbox.cs:34`; rendering: indirect. **Verdict:** KEEP.
- **`--sandbox-mode`** (flag) — default none; *harness* (hero starts walking, running or free, for pictures); used by `GameHost.Sandbox.cs:59`, `simulation.md:641`. The interactive keys Z/X/C already do this. **Verdict:** KEEP as the one sandbox "start state" option, and fold the next two into it.
- **`--sandbox-speed`** (flag) — default 0; *harness* (fixed speed, same as the `[` `]` keys); used by `GameHost.Sandbox.cs:61`, `simulation.md:641`; rendering: none. **Verdict:** MERGE → `--sandbox-mode` (accept `walk|run|free|<u/s>`; the switch already throws on anything else, so this is a few lines in `GameHost.Sandbox.cs:59-61`).
- **`--sandbox-patrol`** (flag) — default off; *harness* (the `P` key, as a start state); used by `GameHost.Sandbox.cs:63`, `simulation.md:641`; rendering: none. **Verdict:** MERGE → `--sandbox-mode` (a `,patrol` suffix), or drop: the `P` key and a `--screenshot` after `--ticks` cover it.
- **`--select-player`** (flag) — default off; *screenshot staging* (selected squad in the picture); used by `GameHost.Screenshot.cs:31`, `simulation.md:307,642`; `--attack-nearest` sets it implicitly. **Verdict:** MERGE → `--move-to` (a move order already selects the squad; a "selected but not moving" picture is not worth a flag).
- **`--attack-nearest`** (flag) — default off; *screenshot staging* for one fight picture; used by `GameHost.Screenshot.cs:39`, `simulation.md:307,642`; no tests; rendering: none. **Verdict:** REMOVE — delete the flag, `AttackNearest` field and the block at `GameHost.Screenshot.cs:39-45`.
- **`--move-to`** (flag) — default none; *screenshot/harness* (`<x> <z>`: order the squad to walk, picture after `--ticks`); used by `GameHost.Screenshot.cs:31-34`, `simulation.md:307,642`; rendering: none. **Verdict:** KEEP as the single sim-staging flag.
- **`--quit-after`** (flag) — default off; *bench/smoke-test entry point* (game: `GameOptions`, viewer: `SmokeTest.Strip` in `tools/Meitou.ModelViewer/Program.cs`, the same option parsed in two places); used by `GameHost.Interactive.cs:234`, ~8 docs; rendering: indirect. **Verdict:** KEEP.

### src/Meitou.Rendering.Display/JitSwitches.cs

The JIT mode experiment is decided (DECISIONS 11 and 19: tiered compilation and tiered PGO off in both csproj files). `docs/bench.md:216-220` records that the modes gave the same fps and the shipped mode is the one to use. The runtime's own `DOTNET_TieredCompilation` / `DOTNET_TieredPGO` variables, which these switches only set in a relaunched child, do the same job without any code.

- **`--tiered-jit`** (flag) — default off; *A/B harness for a decided experiment*; used by `Program.cs` of the game and viewer (via `JitSwitches.Apply`), `bench.md:216`, CHANGELOG:43, `docs/README.md:64`, `viewer.md:211`; rendering: indirect (render-thread CPU). **Verdict:** REMOVE — delete `JitSwitches.cs` (59 lines), the two `Apply` calls (`src/Meitou.Game/Program.cs:11-12`, `tools/Meitou.ModelViewer/Program.cs:8-9`), the "JIT mode" section of `bench.md` and the references in `docs/README.md` and `viewer.md`; set the env vars by hand when needed.
- **`--no-tiered-jit`** (flag) — default off; forces the shipped mode against inherited variables; same files; rendering: none. **Verdict:** REMOVE (goes with `JitSwitches.cs`).
- **`--pgo`** (flag) — default off; *A/B harness* (tiering plus dynamic PGO, relaunch); same files; rendering: indirect. **Verdict:** REMOVE (goes with `JitSwitches.cs`).
- **`--no-pgo`** (flag) — default off; same as above; rendering: none. **Verdict:** REMOVE (goes with `JitSwitches.cs`).
- **`MEITOU_JIT_RELAUNCHED`** (env) — default unset; internal marker set on the relaunched child; read only in `JitSwitches.cs:14,28`; not a user option; rendering: none. **Verdict:** REMOVE (goes with `JitSwitches.cs`).

### src/Meitou.Rendering.Display/VulkanDisplay.cs

- **`MEITOU_PIPELINE_CACHE`** (env) — default `%LOCALAPPDATA%\Meitou\pipeline-cache.bin`; *kill-switch/path override* (`0` turns the Vulkan pipeline cache off, otherwise a file path); used by `VulkanDisplay.cs:106-111` and `engine.md:148` only; rendering: indirect (start-up time, shader-compile hitches). **Verdict:** MERGE → `--no-load-cache` (that flag already means "do not read or write the derived-data caches"; have it also skip the pipeline cache) and drop the path override.

### src/Meitou.Rendering/BackgroundWork.cs

- **`MEITOU_JOB_STATS`** (env) — default off; *debug/diagnostic* (allocation and time per `BackgroundWork` call site, with `--fly-benchmark`); used by `WorldApp.Benchmark.cs:40,121`, docs (`engine.md:257`, `renderer-native.md:3563-3581`, `viewer.md:192`, `DECISIONS.md:101`) as the method of a finished stutter investigation; rendering: indirect. **Verdict:** REMOVE — delete `ReportJobs`, `Measure`, `sites`, `Measured`, `Report` and the `Measure ? … :` branches in the three `Run` overloads (`BackgroundWork.cs:46-58`, `60-86`), and the two lines in `WorldApp.Benchmark.cs`.
- **`MEITOU_UNLOAD_IDLE`** (env) — default 60 s; *tuning knob* "for measurements" (a huge value turns unloading off); used by `StreamingTuning.IdleSeconds` in `BuildingLodMesh.cs:519,521`, `FoliageRenderer.cs:1025`, `WorldTextureCache.cs:149,151`; docs `render-terrain.md:81,86`, `renderer-native.md:3438`; rendering: indirect (residency). **Verdict:** REMOVE — replace `StreamingTuning.IdleSeconds` by a `const 60`, drop the `> 1e8` high-water special case in `BuildingLodMesh.cs:521` and `WorldTextureCache.cs:151`, delete `StreamingTuning`. (Tests and the impostor baker already set `IdleSeconds` and `HighWaterMb` directly.)

### src/Meitou.Rendering/WorldFrame.cs and Bench/NamedViews.cs (view and camera flags; first seen in NamedViews.cs)

These are the world view's core options; `--view` is the bench harness entry (tests: `BenchHarnessTests`).

- **`--view`** (flag) — default none; *bench harness* (`swamp`, `swamp-rain`, `swamp-low`, `dust`, `hub`, expanded in place by `NamedViews.Expand`, `WorldFrame.cs:347`); used by `BenchHarnessTests`, `Program.cs:13` of the viewer, ~18 docs; rendering: direct (benchmarks). **Verdict:** KEEP.
- **`--bench-motion`** (flag) — default `still`; *bench harness* (`still|orbit|fly|turn`, `WorldFrame.cs:493`); used by `CameraMotion.cs`, `WorldApp.Bench.cs:132,281`, `BenchHarnessTests`, `bench.md`; the views only use `orbit`; rendering: direct. **Verdict:** KEEP (see the cross-group note about `--bench-orbit` / `--bench-turn`).
- **`--distance`** (flag) — default radius-derived; *user-facing view option*; used by `WorldFrame.cs:373`, the game's `rig.Place` (`GameHost.cs:128`), `BenchHarnessTests`, `parity.sh`, ~23 docs; rendering: direct. **Verdict:** KEEP.
- **`--pitch`** (flag) — default 35 (game: Kenshi's); *view option*; used by `WorldFrame.cs:372`, `parity.sh`, game `GameHost.cs:128`, ~24 docs; rendering: direct. **Verdict:** KEEP.
- **`--radius`** (flag) — default 1.5 zones; *view/loading option* (half-size of the loaded square); used by `WorldFrame.cs:369,519,652,696`, views, ~16 docs; rendering: direct. **Verdict:** KEEP.
- **`--time`** (flag) — default 13; *user-facing* (hour of day); used by `WorldFrame.cs:399`, `parity.sh`, ~19 docs; rendering: direct. **Verdict:** KEEP.
- **`--town`** (flag) — default "The Hub" when no place is given; *user-facing* (place to start at); used by `WorldFrame.cs:368`, viewer `Program.cs:12`, game `Program.cs`, `BenchHarnessTests`; (`meitou-tools navmesh` has its own `--town`); rendering: direct. **Verdict:** KEEP.
- **`--world`** (flag) — a no-op in the option parser (`WorldFrame.cs:365`); it only selects the viewer mode in `tools/Meitou.ModelViewer/Program.cs:13` (together with `--view` and `--bench-compare`), and is in `package-release.ps1`'s `.bat`, `parity.sh`, README and 26 docs; rendering: direct. **Verdict:** KEEP (documented entry point). Optional tidy: since the mesh and character viewers are gone (phase 8), `WorldApp` could be the default mode and `--world` stay accepted but unneeded.
- **`--yaw`** (flag) — default 30; *view option*; used by `WorldFrame.cs:371`, `parity.sh`, game `GameHost.cs:128`, ~20 docs; rendering: direct. **Verdict:** KEEP.
- **`--weather`** (flag) — default `auto` (the weather scheduler); *user-facing/test* (force a WEATHER record); used by `WorldFrame.cs:439`, views `swamp-rain` and `dust`, `parity.sh`, ~11 docs; rendering: direct. **Verdict:** KEEP.

### src/Meitou.Rendering/BuildingLodMesh.cs

- **`MEITOU_MESH_STREAM`** (env) — default on (`0` loads every level of every mesh); *A/B kill-switch for a feature that is now always on* ("the base's behaviour, for comparisons"); used by `FarForms` at `BuildingLodMesh.cs:161,167,193,269`, docs (`render-foliage.md:150`, `renderer-native.md:3207,3238`); rendering: direct (object LOD memory/pop-in). **Verdict:** REMOVE — hardcode on: delete `FarForms`, the `!FarForms ||` guards (two) and make `near` at line 269 the `Math.Max(m.WantedNear - LookAhead, 0)` arm. Related `MEITOU_MIP_STREAM` / `MEITOU_MIP_*` (other groups) are the same family.

### src/Meitou.Rendering/Characters/CharacterSwitches.cs (`MEITOU_CHARACTER_LOD` is read here; first seen in CharacterContent.cs:217, a comment)

All five are documented in `docs/character-renderer.md:15-20` and have no tests.

- **`MEITOU_CHARACTER_LOD`** (env) — default Meitou (`faithful` keeps the mesh files' own levels); *Faithful/Meitou switch done as an env var*; used by `CharacterContent.cs:185`; `character-renderer.md:76` already lists "make it a Faithful/Meitou switch" as open work; rendering: direct. **Verdict:** MERGE → the existing `lod` switch in `Enhancements` ("Foliage levels"; rename to cover characters), so `--faithful lod` and the Tab checkbox cover characters too. If that is not wanted, hardcode the generated levels and delete `CharacterLod.Levels` use.
- **`MEITOU_CHARACTER_LOG`** (env) — default off; *debug dump* (a line per part of every appearance built); used by `CharacterContent.cs:295` only; rendering: none. **Verdict:** REMOVE — delete `CharacterSwitches.Log` and the block at `CharacterContent.cs:295-`.
- **`MEITOU_CHARACTER_MORPH`** (env) — default on (`0` turns face poses off); *kill-switch for a feature that is now always on*; used by `CharacterContent.cs:259`; rendering: direct. **Verdict:** REMOVE — hardcode on (drop `CharacterSwitches.Morphs &&`).
- **`MEITOU_CHARACTER_MOTION`** (env) — default on (`0` leaves characters out of the temporal upscalers' motion vectors); *kill-switch*; used by `CharacterRenderer.Motion.cs:55`; rendering: direct. **Verdict:** REMOVE — hardcode on; delete `CharacterSwitches.MotionVectors` and the early return.
- **`MEITOU_CROWD_ANIMATE`** (env) — default off; *harness tweak* for `--crowd` (real-time clock even for stills; otherwise stills hold `--crowd-time`); used by `CrowdHarness.cs:96` only; rendering: none. **Verdict:** REMOVE — hardcode "stills use `--crowd-time`" (`interactive ? clock : fixed`); then `CharacterSwitches.cs` is empty and can be deleted.

### src/Meitou.Rendering/FogShadingRate.cs

- **`MEITOU_FOG_VRS`** (env) — default `0.8,0.97` (opacity thresholds for 2x2 and 4x4 shading); *tuning knob* (the feature itself is toggled by `--no-fog-vrs`); used by `PostProcess.FogRate.cs:137`, `FogShadingRateTests.cs:63-78` (tests `ParseThresholds`, not the env), docs `render-post.md`; rendering: direct. **Verdict:** REMOVE — make `Thresholds` a constant of `DefaultTwoByTwo` / `DefaultFourByFour`; delete `ParseThresholds` and its theory test.

### src/Meitou.Rendering/FoliageCardMask.cs and FoliageCardTrim.cs (card trimming)

Card trimming (`--ab card-trim`, bench.md:68, render-foliage.md:241-247) is decided and on; it measured about 0.12 ms GPU. The A/B toggle `card-trim` (`AbToggles` in `WorldApp.Bench.cs:67`) stays. All of the env vars below are read once into static properties or record defaults, are used only by their own docs, and are not set by any test (the test uses an explicit `FoliageCardTrimOptions`). Rendering: direct for all. Delete the env helpers `EnvInt` / `EnvFloat` after removal.

- **`MEITOU_TRIM_COARSEST`** (env) — default 16 (px of the coarsest mask level); *tuning knob*; part of the mask cache file name (`_s{CoarsestSize}`), so changing it makes parallel caches. **Verdict:** REMOVE — `const int CoarsestSize = 16`.
- **`MEITOU_TRIM_SLACK`** (env) — default 0.1; *tuning knob* (alpha-to-coverage slack under the threshold); baked into the cut-off byte in the cache key. **Verdict:** REMOVE — `const float Slack = 0.1f`.
- **`MEITOU_TRIM_CACHE`** (env) — default `%LOCALAPPDATA%\Meitou\cardtrim`; *path override*; `CacheRoot` already has a setter. **Verdict:** REMOVE — delete the env fallback (see the shared cache-dir note).
- **`MEITOU_TRIM_VERTICES`** (env) — default 6; *tuning knob* (`FoliageCardTrimOptions.MaxVertices`). **Verdict:** REMOVE — hardcode 6 in the record default.
- **`MEITOU_TRIM_TRIANGLE_COST`** (env) — default 0.05; *tuning knob*. **Verdict:** REMOVE — hardcode.
- **`MEITOU_TRIM_MIN_SAVING`** (env) — default 0.1; *tuning knob*. **Verdict:** REMOVE — hardcode.
- **`MEITOU_TRIM_MARGIN`** (env) — default 0.25; *tuning knob* (no docs besides the code comment). **Verdict:** REMOVE — hardcode.
- **`MEITOU_TRIM_MIN_PIXELS`** (env) — default 16; *tuning knob*. **Verdict:** REMOVE — hardcode.
- **`MEITOU_CARD_TRIM`** (env) — default on (`0` never makes the trimmed index lists); *kill-switch for a feature that is now always on*; used by `FoliageCardTrimming.Enabled` (`FoliageCardTrim.cs:404`), `FoliageRenderer.Trim.cs:17,34`; the `card-trim` A/B toggle covers the comparison. **Verdict:** REMOVE — delete `Enabled`; `CardTrim` property defaults to true; drop the `!FoliageCardTrimming.Enabled` guard at `Trim.cs:34`.

### src/Meitou.Rendering/FoliageGpuCull.cs

- **`MEITOU_FOLIAGE_TRIS`** (env) — default off; *debug/diagnostic* (GPU cull's indirect arguments tallied by view kind and by mesh name, "top" list in `--fly-benchmark`); used by `FoliageGpuCull.NamedTally` (`:270`), `FoliageRenderer.cs:219-260,1872-1900`, `WorldApp.Benchmark.cs:166-167`, docs (`render-distance-benchmark.md:21,54,316`, `foliage.md:526`, `impostors.md:698`, `viewer.md:158`); the unnamed tally (`CountTriangles`) is already driven by `--bench-tris` and the F11 overlay; rendering: indirect. **Verdict:** REMOVE — delete `NamedTally`, `TallyNames`, `TallyByName`, `TallyShadowByName`, `DrawTallyTop`, the name-collection block at `FoliageRenderer.cs:1876-`, and the `top` line in `WorldApp.Benchmark.cs`; `DrawTally` becomes `CountTriangles`. Per-mesh triangle ranking is a one-off investigation that is documented.

### src/Meitou.Rendering/FoliageLod.cs

- **`MEITOU_LOD_CACHE`** (env) — default `%LOCALAPPDATA%\Meitou\lods`; *path override*; used by `FoliageLodCache.Root` (`FoliageLod.cs:263`); tests set `Root` directly (`FoliageLodTests.cs:112`); docs `foliage.md:494`, `render-foliage.md:106`; rendering: indirect. **Verdict:** REMOVE — delete the env fallback and the doc mentions (see the shared cache-dir note).

### src/Meitou.Rendering/FoliagePrepass.cs

- **`MEITOU_FOLIAGE_PREPASS`** (env) — default on (`0` starts with the prepass off); *A/B start state for a switch now on*; used by `FoliagePrepass.Enabled` (`:11`), `FoliageRenderer.cs:1244`, `FoliageRenderer.Impostors.cs:1009`, `--ab foliage-prepass` (`WorldApp.Bench.cs:63`), docs (`render-foliage.md:227`, `bench.md:93`); rendering: direct. **Verdict:** REMOVE — `Enabled { get; set; } = true`; the `--ab foliage-prepass` toggle keeps the comparison. Same pattern as `MEITOU_IMPOSTOR_PREPASS` (other group).

### src/Meitou.Rendering/FoliageRenderer.Grass.cs

- **`MEITOU_GPU_GRASS`** (env) — default on (`0` walks the pages on the CPU); *kill-switch for a feature that is now always on* ("the reference" path); used by `GpuGrass` (`:29`), docs (`renderer-native.md:1044,1249,2703,3032`, `render-foliage.md:129`); redundant with `MEITOU_GPU_CULL=0`, which already includes grass in the same expression; the `MEITOU_GPU_CULL_VERIFY` mode (other group) compares the CPU path with the GPU path; rendering: direct. **Verdict:** REMOVE — `GpuGrass` defaults to `true` (the GPU programs still fall back to CPU when the device lacks the kernels, `GpuGrassActive`); `MEITOU_GPU_CULL` remains the one switch. Remove the CPU walk too if `MEITOU_GPU_CULL` and the verify mode go.
- **`MEITOU_GRASS_ARENA_MB`** (env) — default 160; *tuning knob* (blade arena size); used by `GrassArenaBytes` (`:34`), `renderer-native.md:1037`; rendering: direct. **Verdict:** REMOVE — `const` 160 MB; delete the `Env` read.
- **`MEITOU_GRASS_SLOTS`** (env) — default 32768; *tuning knob* (cull table slots); used by `GrassSlotCapacity` (`:35`), `renderer-native.md:1039`; rendering: direct. **Verdict:** REMOVE — `const` 32768.

### src/Meitou.Rendering/FoliageRenderer.Impostors.cs (and ImpostorBudget.cs, Impostors/ImpostorCache.cs)

All ten are documented in `docs/impostors.md` and `render-foliage.md`; no test sets any of them except one test that reads `MEITOU_IMPOSTOR_CACHE_MB` only to skip an assertion.

- **`MEITOU_IMPOSTOR_BUDGET_MB`** (env) — default none (8% of the card's budget); *user-facing setting* duplicated by the flag `--impostor-budget <MB>` (`WorldFrame.cs:387`, both set `ImpostorBudgetMb`); read in `ImpostorBudget.FromEnvironment` (`ImpostorBudget.cs:46`); `impostors.md:290,516`; rendering: indirect (VRAM). **Verdict:** MERGE → `--impostor-budget`; delete `FromEnvironment`, init `ImpostorBudgetMb` to null.
- **`MEITOU_IMPOSTOR_FARMIP`** (env) — default on (`0` = one level per atlas, no refine, no coarsen); *kill-switch for a feature that is now always on*; used by `FarMips` (`:51`, uses at `:399,404,501,798-799`), `impostors.md:543,563`; rendering: direct. **Verdict:** REMOVE — hardcode true; delete the `FarMips ? … : …` fallbacks.
- **`MEITOU_IMPOSTOR_MIP_MARGIN`** (env) — default 1 level; *tuning knob*; used by `FarMipMargin` (`:53`, `:380`), `impostors.md:535`, `renderer-native.md:3313`; rendering: direct. **Verdict:** REMOVE — `const float FarMipMargin = 1`.
- **`MEITOU_IMPOSTOR_LOG`** (env) — default off; *debug log* (a line per atlas load, bake, refusal, eviction); used by `ImpostorLog` at about 8 sites in this file (`:462,477,600,771,804,823,897`), `impostors.md:292`; rendering: none. **Verdict:** REMOVE — delete the flag and the `if (ImpostorLog) Console.WriteLine(…)` lines; the pending/atlas statistics (`ImpostorDescription`) already cover the numbers.
- **`MEITOU_IMPOSTOR_CASTERS`** (env) — default on (`0` keeps mesh casters in the cascades); *A/B kill-switch for a feature that is now always on*; used by `ImpostorCasters` (`:66`) and `FoliageRenderer.cs:1404`, `impostors.md:292,337`; rendering: direct. **Verdict:** REMOVE — `impostorView = Impostors && …`: the `!depthPass ||` clause stays true; the `--faithful impostors` switch still turns impostors (and their casters) off as a whole.
- **`MEITOU_IMPOSTOR_BAKE_MSAMPLES`** (env) — default 4 (million samples per frame); *tuning knob* (was 40; `render-foliage.md:99` still says 40, stale); used by `ImpostorBakeSamplesPerFrame` (`:116`), `impostors.md:439`; rendering: indirect (bake stutter). **Verdict:** REMOVE — `const double = 4e6`; fix the stale doc line.
- **`MEITOU_IMPOSTOR_CACHE_MB`** (env) — default 4096; *user-facing setting* duplicated by the flag `--impostor-cache-mb` (`WorldFrame.cs:388`, sets `ImpostorCacheMb`); read in `ImpostorCache.FromEnvironment` (`ImpostorCache.cs:44`); `ImpostorTests.cs:518` only checks the default when the env is unset; docs `impostors.md:160`, `render-foliage.md:98`; rendering: none. **Verdict:** MERGE → `--impostor-cache-mb`; delete `FromEnvironment`, use `DefaultMaxBytes`, simplify the test guard.
- **`MEITOU_IMPOSTOR_ROCKS`** (env) — default on (`0` keeps TERRAIN-mode rocks as meshes); *A/B kill-switch for a feature that is now always on*; used by `RockImpostorsEnabled` (`:176,189`), `impostors.md:712,716`, `renderer-native.md:3331`; `--faithful impostors` already disables it, since `RockImpostorsActive` requires `Impostors`; rendering: direct. **Verdict:** REMOVE — drop `RockImpostorsEnabled` from the `RockImpostorsActive` expression.
- **`MEITOU_IMPOSTOR_ROCK_MIN_SURFACE`** (env) — default 0.06; *tuning knob* (surface share under which a rock stays a mesh); used by `RockMinSurface` (`:178`, `:772`), `impostors.md:692`; rendering: direct. **Verdict:** REMOVE — `const float RockMinSurface = 0.06f`.
- **`MEITOU_IMPOSTOR_ROCK_TINT`** (env) — default on (`0` drops the colour-map tint on rock impostors); *kill-switch*; used by `RockTint` (`:185`, `:1060`: `Flags … | 8u`), shader `uRockFlags & 8u` in `ImpostorShaders.cs:665`, `impostors.md:684`; rendering: direct. **Verdict:** REMOVE — always set bit 8; the shader's `& 8u` test can go with the other half of its condition.

### src/Meitou.Rendering/FoliageRenderer.Lod.cs

- **`MEITOU_LOD_PIXELS`** (env) — default 4 px (`DefaultLodTolerance`); *tuning knob* for the generated-level tolerance; the Tab slider "Foliage LOD distance x" (`WorldFrame.cs:1111`) sets the same `LodTolerance` property; used by `FoliageRenderer.Lod.cs:24`, docs `foliage.md:485`, `render-foliage.md:107`; rendering: direct. **Verdict:** REMOVE — `LodTolerance = DefaultLodTolerance`; the slider covers it.
- **`MEITOU_LOD_FAR`** (env) — default 2500 units (`0` turns the growth off); *tuning knob* and a second off-switch for the `lod-far` A/B (`WorldApp.Bench.cs:61` mentions it in its description); used by `FoliageRenderer.Lod.cs:33`, `foliage.md:524`; rendering: direct. **Verdict:** REMOVE — `LodFarDistance = DefaultLodFarDistance`; update the `lod-far` A/B description.
- **`MEITOU_LOD_NORMAL`** (env) — default 0.02; *tuning knob* (shading-angle weight in a level's deviation); the same constant is hardcoded in `ObjectLodGen.cs:36`; used by `FoliageRenderer.Lod.cs:41`, `foliage.md:501`; rendering: direct. **Verdict:** REMOVE — hardcode 0.02 and share one constant with `ObjectLodGen`.

**Group totals:** 81 items: keep 25, merge 9, remove 47.

#### Cross-group observations

- **Dead or no-op parser entries.** `--world` is a no-op in `WorldOptions.Parse` (it only selects the viewer mode), and `--faithful`'s help text lists a non-existent `dust` id and omits 13 real ids. `--free-camera`, `--no-navmesh`, `--body-time-scale` and `--attack-nearest` are not referenced anywhere except `GameOptions`, the one consumer and a usage line.
- **Flag and env doing the same thing.** `MEITOU_NO_LOAD_CACHE` and `--no-load-cache`; `MEITOU_IMPOSTOR_BUDGET_MB` and `--impostor-budget`; `MEITOU_IMPOSTOR_CACHE_MB` and `--impostor-cache-mb`. Also `MEITOU_GPU_GRASS` is covered by `MEITOU_GPU_CULL=0`, and `MEITOU_LOD_PIXELS` by the Tab slider. The game's `--fps-limit`, `--tick-rate`, `--sim-threads` and `--vsync` duplicate `meitou.user.json`, and `--quit-after` is parsed twice (game and viewer).
- **Cache path/size envs should be one thing.** `MEITOU_FOLIAGE_CACHE(_MB)`, `MEITOU_LOD_CACHE`, `MEITOU_TRIM_CACHE`, `MEITOU_PIPELINE_CACHE` and the impostor cache all have their own override. If the owner wants any, a single `MEITOU_CACHE_DIR` plus `--no-load-cache` is enough; none of the individual ones is used by a test or script.
- **Families that should go together.** The `MEITOU_TRIM_*` set (9), the `MEITOU_IMPOSTOR_*` kill-switches and tuning knobs (8; the rest of the family in other groups, such as `MEITOU_IMPOSTOR_PREPASS`), the `MEITOU_CHARACTER_*` set (4 plus the LOD merge), `MEITOU_MESH_STREAM` with `MEITOU_MIP_STREAM` / `MEITOU_MIP_*`, `MEITOU_GPU_GRASS` / `MEITOU_GPU_CULL` / `MEITOU_GPU_CULL_VERIFY` (removing the CPU reference path deletes a lot), and the JIT flags (`JitSwitches.cs` removed whole).
- **Diagnostics that could be dropped as a set.** `MEITOU_ALLOC_STATS`, `MEITOU_JOB_STATS` and `MEITOU_FOLIAGE_TRIS` (with `MEITOU_PASS_STATS` and similar in other groups) were the method of finished stutter investigations; the `--fly-benchmark` output already has GC counts, pause time and allocation.


Conventions used below.

- "Bench switch" = an `AbToggles.Register(...)` in `tools/Meitou.ModelViewer/WorldApp.Bench.cs` (`--ab <id>`), which flips the same property at run time. Most env vars in this group are the *startup* form of such a switch, so they are redundant with it. "REMOVE the env var" always means: delete the `Environment.GetEnvironmentVariable` read, hardcode the current default (the "on" side), leave the property so the bench switch still works, and strike the env mention from the doc lines named. If the owner also retires the `--ab` switch for decided experiments, the property, the `AbToggles.Register` line and the off-branch go too.
- Docs hits are historical measurement notes (docs/renderer-native.md, docs/bench.md, docs/render-*.md, docs/impostors.md). Nothing in `tests/`, `tools/scripts/` (package-release.ps1, parity.sh, parity-compare.sh), README/ROADMAP/CHANGELOG or `.claude` (absent in this worktree) reads any env var of this group, except `MEITOU_IMPOSTOR_CACHE` (one test) and `MEITOU_PASS_STATS` / `MEITOU_STREAM_LOG` / `MEITOU_LANDMARK_SURVEY` (ModelViewer tool code).
- Searched with a whole-worktree grep (src, tools, tests, docs, root md files).

### src/Meitou.Rendering/FoliageRenderer.Lod.cs

- **`MEITOU_SCREEN_LOD_TRI`** (env) — default 1.5 (square px under which a mean triangle counts as unseen); *Kind*: tuning knob of the `screen-lod` feature; used by docs/render-foliage.md:110 only; rendering: direct. **Verdict:** REMOVE — decided constant; make `ScreenLodTriPixels` a plain `= 1.5f` (Lod.cs:51), drop the `Env(...)` read and the doc mention.
- **`MEITOU_SCREEN_LOD_MULT`** (env) — default 2; *Kind*: tuning knob of `screen-lod`; used by docs/render-foliage.md:111; rendering: direct. **Verdict:** REMOVE — hardcode 2 (Lod.cs:54), drop doc mention.
- **`MEITOU_LOD_TEXELS`** (env) — default 2 (shadow texels a generated level may deviate in a cascade); *Kind*: tuning knob; used by docs/formats/foliage.md:486 and docs/render-foliage.md:107 (mentions only); rendering: direct (shadow cascades). **Verdict:** REMOVE — hardcode 2 (Lod.cs:70), strike the two doc mentions.
- **`MEITOU_LOD_ALT`** (env) — default off; draws generated levels on even frames only so one run times both (`LodAlternate`, adds stage name `fol rocks+lod`); *Kind*: bench/A-B measurement switch superseded by `--ab lod` (docs/bench.md:32 already says so); used by docs/bench.md:32, formats/foliage.md:497,539, render-foliage.md:108; rendering: direct. **Verdict:** REMOVE — delete `LodAlternate` (Lod.cs:73), the `&& !(LodAlternate && frame odd)` term in `LodWanted` (Lod.cs:301), the `"fol rocks+lod"` stage branch (FoliageRenderer.cs:1304), and the doc lines.
- **`MEITOU_LOD_LOG`** (env) — default off; prints one line per mesh file with its levels' triangles and deviations; *Kind*: debug dump; used by docs/formats/foliage.md:497,523 and render-foliage.md:108 (observations already recorded); rendering: none (console only). **Verdict:** REMOVE — delete `LodLog` (Lod.cs:74) and the `if (LodLog) Console.WriteLine(...)` block (Lod.cs:197-201). (The unrelated `MEITOU_TERRAIN_LOD_LOG` in TerrainRenderer.cs belongs to another group.)

### src/Meitou.Rendering/FoliageRenderer.cs

- **`MEITOU_FOLIAGE_DEBUG`** (env, values `nograss` / `nomeshes`) — default unset; starts with the grass or the foliage meshes not drawn; *Kind*: debug/A-B switch; used by docs/render-foliage.md:211 and the bench's `grass` (WorldApp.Bench.cs:60) and `foliage-meshes` (:62) switches, whose description strings name it; rendering: direct. **Verdict:** REMOVE — the two bench switches cover it at run time; make `DrawGrass`/`DrawMeshes` plain `= true` (FoliageRenderer.cs:63-64), fix the two switch descriptions and the doc line.
- **`MEITOU_GPU_CULL`** (env) — default on (`=0` starts the CPU cull `FoliageCull`); *Kind*: kill-switch for a feature that is now always on (GPU cull has been the default since step A2; the CPU path is only the reference); used by ~10 docs hits (renderer-native.md, impostors.md, render-foliage.md, formats/foliage.md, fogfeatures.md) and by the property `GpuCull` in FoliageRenderer.Impostors.cs:189, Lod.cs:152,301, FoliageRenderer.cs:1176; no tests/scripts read the env; rendering: direct. Note `GpuCull=0` already degrades the picture (no generated levels, no rock impostors: foliage.md:492, impostors.md:323), so it is no longer a like-for-like A/B. **Verdict:** REMOVE — hardcode `GpuCull = true` (FoliageRenderer.cs:112). Optional follow-up together with `MEITOU_GPU_GRASS` (another group): delete the CPU path (`FoliageCull.cs`, 188 lines; `FoliageCullTests`/`FoliageGpuCullTests` use it as the reference, so keep the class while those stay).
- **`MEITOU_GPU_CULL_VERIFY`** (env) — default off; with the GPU cull also culls on the CPU and compares the lists a frame late, prints a summary on dispose; *Kind*: debug/verification harness for the GPU cull (done when the cull shipped; test `Gpu_impostor_split_matches_FoliageCull` covers the split); used by docs (renderer-native.md:931,1029,1044,2083,2704; impostors.md:322; render-foliage.md:127,221; formats/foliage.md:492) and the `GpuCullVerify` guards at FoliageRenderer.cs:1161,1180,1244,1896,1899, Grass.cs:215,228,283,306, GrassVerify.cs:82, Impostors.cs:189, Lod.cs:152,301; rendering: indirect. **Verdict:** REMOVE — delete `GpuCullVerify` (FoliageRenderer.cs:115) and everything gated on it (`CheckVerify`, `verifyPending`, `QueueGrassVerify`/`CheckGrassVerify`, `FoliageRenderer.GrassVerify.cs` 87 lines, the `!GpuCullVerify` terms) plus the doc lines. Goes with the GPU_GRASS verify path.
- **`MEITOU_FOLIAGE_RANGE`** (env) — default 4 (start value of the Tab slider "Foliage draw distance x"; only matters in Faithful range mode, in Meitou mode `NearReach` uses the class ranges); *Kind*: user-facing setting, startup form; used by docs/engine.md:491, formats/settings.md:350, render-foliage.md:169, renderer-native.md:2096; rendering: direct. No CLI flag exists (unlike `--grass-density`, `--range-large/medium/small`). **Verdict:** MERGE → a `--foliage-range <x>` CLI flag next to the existing `--range-*` options (the slider stays); then drop the env read (FoliageRenderer.cs:119) and the `Env` helper (:127).
- **`MEITOU_GRASS_RANGE`** (env) — default 8 (start value of the "Grass draw distance x" slider; used in both range modes); *Kind*: user-facing setting, startup form; used by docs/engine.md:491, settings.md:350, render-foliage.md:169, render-distance-benchmark.md:30 (range presets of a benchmark); rendering: direct. **Verdict:** MERGE → a `--grass-range <x>` CLI flag (one flag form for all three sliders), then remove the env read (FoliageRenderer.cs:120).
- **`MEITOU_GRASS_DENSITY`** (env) — default 1 (start value of the "Grass density x" slider); *Kind*: user-facing setting; **duplicate of the existing flag `--grass-density`** (WorldFrame.cs:472, applied at :973); used by docs/settings.md:350, render-foliage.md:169; rendering: direct. **Verdict:** MERGE → `--grass-density`; delete the env read (FoliageRenderer.cs:121) and doc mentions.
- **`MEITOU_GRASS_VELOCITY`** (env) — default on (`=0` redraws all blades in the motion pass); *Kind*: kill-switch of the `grass-velocity` Meitou feature; used by docs/render-foliage.md:221 and bench switch `grass-velocity` (WorldApp.Bench.cs:69); rendering: direct. **Verdict:** REMOVE — hardcode true (FoliageRenderer.cs:123); keep the bench switch only while the A/B is still wanted.
- **`MEITOU_GRASS_VELOCITY_PX`** (env) — default `GrassMotionReach.DefaultThresholdPixels` (0.5); *Kind*: tuning knob; used by docs/render-foliage.md:221 and the bench switch description (WorldApp.Bench.cs:69); rendering: direct. **Verdict:** REMOVE — `GrassVelocityPixels` = the constant (FoliageRenderer.cs:125).
- **`MEITOU_FOLIAGE_UPDATE_STATS`** (env) — default off; prints mean CPU ms per update part every 300 non-settling updates; *Kind*: debug/stats, added with the "unreproduced 4.39 ms reading" note; used by docs/render-foliage.md:5 only; rendering: none (CPU timing). **Verdict:** REMOVE — delete `updateStats` (FoliageRenderer.cs:385), the accumulation and print at :524-529 and the `updateParts` fields, and the render-foliage.md:5 paragraph (keep the recorded numbers).
- **`MEITOU_STREAM_LOG`** (env) — default off; prints slow streaming steps (>3 ms steps, >8 ms foliage updates, >15 ms frames) with the upload profiler's breakdown; *Kind*: diagnostic dump; read in 7 places: FoliageRenderer.cs:536,1018 (+Impostors.cs), ShoreField.cs:209, WorldTextureCache.cs:109, TerrainStreamer (`Log`), `Gpu/UploadProfile.cs:16` (`On`), `tools/Meitou.ModelViewer/WorldApp.cs:158`; used by docs/viewer.md:150,192, renderer-native.md:3484,3525, render-objects.md:23,50, render-water.md:224, particle-universe.md:339; rendering: indirect. Overlaps what `--log-spikes` (SpikeLog) is for (why frame N was slow, with notes per frame). **Verdict:** MERGE → `--log-spikes` (the slow-step lines become spike notes; `UploadProfile.On` follows `SpikeLog.Enabled`); drop the env name from the 7 read sites and the docs. Medium-size change; if the owner wants to keep one streaming diagnostic, this is the one, but as a flag.
- **`MEITOU_FOLIAGE_TIMING`** (env) — default off; sums the CPU time of foliage `Draw` steps per kind (colour/depth), prints on dispose (also the GPU cull's dispatch recording and rock draws); *Kind*: bench/diagnostic (wave-3 probe); used by 9 docs hits (renderer-native.md:1282,1509,1667,1928,2084,2094,2157; render-foliage.md:130; render-distance-benchmark.md:233), all historical measurements; rendering: indirect. The bench harness (`cpu:*` stages, `--ab`) supersedes it. **Verdict:** REMOVE — delete `FolTiming`, `FolAccount`, the `fol*` arrays and the print (FoliageRenderer.cs:1319-~1360, call at :1306) and the doc sentences. Goes with `MEITOU_MESH_TIMING` (another group).
- **`MEITOU_FOLIAGE_TIMING_SKIP`** (env) — default 160 calls skipped; only read by the timing above; *Kind*: diagnostic knob; used by nothing else; rendering: none. **Verdict:** REMOVE — with `MEITOU_FOLIAGE_TIMING` (FoliageRenderer.cs:1325).

### src/Meitou.Rendering/FrameProfiler.cs

- **`MEITOU_PROFILER_PRE`** (env) — default on (`=0` leaves the pre-frame command buffer's stamps out); *Kind*: one-off measurement of what the profiler costs (docs/bench.md:75 records the answer: under the noise); used by docs/bench.md:75 only; rendering: none. **Verdict:** REMOVE — delete `PreStamps` (FrameProfiler.cs:47) and the `!PreStamps ||` test at :160, drop the sentence from bench.md:75.
- **`MEITOU_PROFILER`** (env, `gpu`|`cpu`) — default off; starts the profiler chart in a mode; *Kind*: debug overlay start state; used by docs/bench.md:75 only; rendering: none. The chart is toggled by F12 in the interactive viewer and there is no `--profiler` flag. **Verdict:** REMOVE — F12 does the same; `Showing = Mode.Off` (FrameProfiler.cs:90). If starting it from the command line is wanted (screenshots), add `--profiler gpu|cpu` instead of the env.

### src/Meitou.Rendering/Gi/GiProbes.cs

- **`MEITOU_GI_RAYS`** (env, 32 or 64) — default 32 (it was 64 until 2026-10-11); *Kind*: tuning knob / startup form of bench switch `gi-rays` (WorldApp.Bench.cs:88, which names it); used by docs/render-gi.md:134,401,444, bench.md:92; rendering: direct. **Verdict:** REMOVE — `DefaultRays = 32` (GiProbes.cs:20). The decision is recorded in render-gi.md; if 64 is dead the workgroup variant and the switch can go too.
- **`MEITOU_GI_BASES_FULL`** (env) — default unset (incremental base-height sampling on; `=1` resamples every column); *Kind*: kill-switch, result identical, perf only; no bench switch; used by docs/bench.md:94, render-gi.md:478; rendering: indirect. **Verdict:** REMOVE — hardcode `IncrementalBases = true` (GiProbes.cs:49) and delete the full-resample branch.
- **`MEITOU_GI_FAR_PERIOD`** (env) — default 2 (far cascade traced every 2nd round); *Kind*: tuning knob / startup form of `gi-far` (WorldApp.Bench.cs:89); used by docs/bench.md:28,92, render-gi.md:402; rendering: direct. **Verdict:** REMOVE — `FarPeriod = 2` (GiProbes.cs:66).
- **`MEITOU_GI_HIDDEN_PERIOD`** (env) — default 16 (0 off); *Kind*: tuning knob / startup form of `gi-fog`; used by docs/render-gi.md:264,303,310 (A/B numbers); rendering: direct. **Verdict:** REMOVE — `HiddenPeriod = 16` (GiProbes.cs:90).

### src/Meitou.Rendering/Gi/GiScene.cs

These five form a family of GI cost kill-switches, each with a `gi-*` bench switch (WorldApp.Bench.cs:84-87) and a row in the render-gi.md table (:400-403, 475-478); `LazyScene` needs `ReuseTop`, so retire them together.

- **`MEITOU_GI_TOP_REUSE`** (env) — default on (`=0` off); *Kind*: kill-switch of a GI optimisation; bench switch `gi-top-reuse` (:87); used by docs/render-gi.md:400, bench.md:28,92; rendering: indirect (GI). **Verdict:** REMOVE — `ReuseTop = true` (GiScene.cs:46).
- **`MEITOU_GI_TOP_TRACE`** (env) — default on; *Kind*: kill-switch (build the top level for fast tracing); bench switch `gi-top-trace`, which render-gi.md:441 also uses as the null/no-op A/B for the noise floor; used by render-gi.md:403, bench.md:28,92; rendering: indirect. **Verdict:** REMOVE — `TopFastTrace = true` (GiScene.cs:52).
- **`MEITOU_GI_TERRAIN_REFIT`** (env) — default on; *Kind*: kill-switch (refit instead of rebuild terrain acceleration structures); bench switch `gi-terrain-refit` (:86); used by render-gi.md:475, bench.md:94; rendering: indirect. **Verdict:** REMOVE — `RefitTerrain = true` (GiScene.cs:54).
- **`MEITOU_GI_LAZY_SCENE`** (env) — default on; *Kind*: kill-switch; bench switch `gi-lazy-scene` (:84); used by render-gi.md:476, bench.md:94; rendering: indirect. **Verdict:** REMOVE — `LazyScene = true` (GiScene.cs:60).
- **`MEITOU_GI_SHARED_TOP`** (env) — default on; *Kind*: kill-switch (one top-level structure for all frame slots); bench switch `gi-top-shared` (:85); used by render-gi.md:477, bench.md:94; rendering: indirect. **Verdict:** REMOVE — `SharedTop = true` (GiScene.cs:63).
- **`MEITOU_PASS_STATS`** (env; `_SKIP`, `_TAG` are other groups') — default off; turns on `PassMeter` (per-stage frame cost table + CSV, the pre-frame GPU time in `FrameLoop`, stage hooks in `StageClock`) for `--fly-benchmark`; read at `tools/Meitou.ModelViewer/PassMeter.cs:82`, `Gpu/FrameLoop.cs:28`, with a note in GiScene.cs:168 (the GI scene splits its list while the meter is on); *Kind*: bench harness entry point (the fly-benchmark's breakdown; `--bench-frames/--ab` is the newer harness and gives per-stage GPU ms with intervals, but the pass meter's parts rows and `DetailedStats` frame are still cited, e.g. docs/bench.md:76,92, engine.md:293-307, render-clouds.md:208, render-distance-benchmark.md:21); rendering: indirect. **Verdict:** KEEP — still how the fly benchmark reports passes. Candidate for a later MERGE into the `--bench-frames` harness, or a single `--pass-stats[=skip]` flag so the three env vars become one flag.

### src/Meitou.Rendering/Gpu/Core/VulkanDevice.cs

- **`MEITOU_FORCE_INTEGRATED`** (env) — default off; makes `IsIntegrated` true on any GPU so the integrated-GPU budgets (impostor budget, star layers, cloud steps) can be seen on the desktop card; *Kind*: debug/test switch for the owner's real iGPU-laptop target; used by docs (character-renderer.md:71, formats/sky.md:515, impostors.md:528,575, renderer-native.md:3312) and the comment in SkyRenderer.cs:667; rendering: direct (budgets, quality). **Verdict:** KEEP — the only way to exercise iGPU paths without the laptop; could be merged with the next into one `--simulate-gpu integrated,<MB>` flag.
- **`MEITOU_VRAM_BUDGET_MB`** (env) — default unset; caps what `VideoMemory()` reports so the VRAM guard and impostor budget can be tested as on a smaller card; *Kind*: debug/test switch; used by docs (impostors.md:529,568, render-distance-benchmark.md:300, renderer-native.md:3107,3137,3267, character-renderer.md:71, render-foliage.md:136); rendering: direct (what gets loaded). **Verdict:** KEEP — pair with `MEITOU_FORCE_INTEGRATED` (see above).
- **`MEITOU_VK_VALIDATION`** (env, `1`/`sync`/`gpu`/`0`) — default: validation on when the layer is installed; picks the validation level (read in `VulkanDeviceOptions.FromEnvironment`, VulkanDeviceOptions.cs:23); *Kind*: debug (validation gate; "0 errors" results are cited in a dozen docs: engine.md:146,155, impostors.md:465,584,712, render-gi.md:446,522, render-distance-benchmark.md:304, character-viewer.md:93, viewer.md:16); rendering: none (but guards correctness). **Verdict:** KEEP — standard Vulkan debugging switch; could become `--validation [sync|gpu|off]` for consistency but the env form is conventional.
- **`MEITOU_NO_VRS`** (env) — default off; skips `QueryShadingRate` so the device is treated as one without VK_KHR_fragment_shading_rate; *Kind*: test/safety switch for fog VRS (`fog-vrs` bench switch and `MEITOU_FOG_VRS`, the latter another group's); used by nothing but its own comment (docs only describe the extension: renderer-native.md:100, render-post.md:51); rendering: direct (disables fog VRS). **Verdict:** REMOVE — delete the early return at VulkanDevice.cs:900; `fog-vrs` / `MEITOU_FOG_VRS` already turn the feature off, and the no-extension path runs on any card without it.

### src/Meitou.Rendering/Gpu/DrawLog.cs

- **`MEITOU_DRAW_LOG`** (env, a file path) — default off; writes a per-draw log of one frame; *Kind*: debug dump built for the VkGl-vs-native parity gate (renderer-native.md 7.6); VkGl is deleted (Vulkan-only since phase 8), so its purpose is gone; used by `meitou-tools draw-log-diff` (tools/Meitou.Tools/DrawLogDiff.cs, Program.cs:51), Gpu/GpuFrame.cs:141 (creates the log), ~10 `Log?.` calls in `Gpu/CommandList.cs` (lines 160-313), and docs/renderer-native.md:2365; no tests; rendering: none. **Verdict:** REMOVE — delete `DrawLog.cs` (move `DrawLog.ProgramHash`, used by `ShaderLibrary.cs:86`), the `Log` field in `GpuContext.cs:74`, the `Log?.` calls in `CommandList.cs`, the `GpuFrame.cs:141-143` hook, `tools/Meitou.Tools/DrawLogDiff.cs` and its `Program.cs` entry, and docs section 7.6.
- **`MEITOU_DRAW_LOG_FRAME`** (env) — default 3; only read with the log above; *Kind*: debug knob; used by nothing else; rendering: none. **Verdict:** REMOVE — with `MEITOU_DRAW_LOG` (DrawLog.cs:104).

### src/Meitou.Rendering/Gpu/Recording.cs

- **`MEITOU_RECORD_THREADS`** (env, `0`/`1`/other) — default 2 (secondaries recorded on job threads); 0 = single-threaded command stream, 1 = secondaries recorded serially; *Kind*: kill-switch / diagnostic for threaded recording (mode 1 told a secondary-buffer difference from a race); bench switch `record-threads` (WorldApp.Bench.cs); used by docs/viewer.md:169, impostors.md:323, render-distance-benchmark.md:241,270, renderer-native.md (a dozen historical parity-gate mentions), bench.md:77; no tests; rendering: indirect (pictures 0 px identical). **Verdict:** REMOVE — `Mode = 2` (Recording.cs:39), keep the property for the bench switch; drop the viewer.md:169-172 bullets.
- **`MEITOU_RECORD_MIN_DRAWS`** (env) — default 32; *Kind*: tuning knob; used by docs/viewer.md:173, renderer-native.md:1189,1252; rendering: indirect. **Verdict:** REMOVE — `MinThreadedDraws = 32` (Recording.cs:62), delete the viewer.md bullet.

### src/Meitou.Rendering/Gpu/Shaders/GlslProgramCompiler.cs

- **`MEITOU_FRAGMENT`** — **not an option**: a GLSL preprocessor macro the compiler defines in fragment stages (GlslProgramCompiler.cs:158; used in MeitouShadowShaders.cs:74 and mentioned in ShaderCompilerTests.cs:107, renderer-native.md:515,611). No env or flag reads it. *Kind*: n/a (false positive of the scan); rendering: indirect. **Verdict:** KEEP — drop it from the options list; nothing to change.

### src/Meitou.Rendering/Impostors/ImpostorCache.cs

- **`MEITOU_IMPOSTOR_CACHE`** (env, a directory) — default `%LOCALAPPDATA%\Meitou\impostors`; *Kind*: cache location override, used to measure an empty cache (docs/impostors.md:147,443, viewer.md:190) and by `tests/Meitou.Tests/Rendering/ImpostorTests.cs:596` (test skips when it is set); one of several cache-dir overrides (`MEITOU_FOLIAGE_CACHE`, `MEITOU_OBJECT_LOD_CACHE`, `MEITOU_LOD_CACHE`, `MEITOU_TRIM_CACHE`, `MEITOU_PIPELINE_CACHE`); rendering: indirect. **Verdict:** MERGE → one `MEITOU_CACHE_DIR` root (each cache a subfolder `impostors`, `objlods`, `foliage`, `lods`, `cardtrim`, `shaders`); adjust the test to point it at a temp dir. (The size var `MEITOU_IMPOSTOR_CACHE_MB` and flag `--impostor-cache-mb` are another group's.)

### src/Meitou.Rendering/Impostors/ImpostorLayout.cs

All of these are constants chosen in docs/impostors.md (sections 4, 11, 13), documented as "for experiments", and part of the atlas cache key (an override produces a different cache entry); none is read by tests or scripts.

- **`MEITOU_IMPOSTOR_SMALL`** (env) — default on (`=0` switches the small-impostor class off); *Kind*: kill-switch for a feature now always on; used by docs/impostors.md:555,599,615, render-distance-benchmark.md:286 (A/B numbers); rendering: direct. **Verdict:** REMOVE — `SmallEnabled = true` (ImpostorLayout.cs:107), then drop the `SmallEnabled` branches. `--faithful impostors` still turns all impostors off.
- **`MEITOU_IMPOSTOR_SMALL_MIN_RADIUS`** (env) — default 2; *Kind*: tuning knob; used by docs/impostors.md:598; rendering: direct. **Verdict:** REMOVE — const 2 (ImpostorLayout.cs:108).
- **`MEITOU_IMPOSTOR_SMALL_MIN_TRIANGLES`** (env) — default 100; *Kind*: tuning knob; docs/impostors.md:598; rendering: direct. **Verdict:** REMOVE — const (ImpostorLayout.cs:109).
- **`MEITOU_IMPOSTOR_SMALL_FRAME`** (env) — default 32; *Kind*: tuning knob; docs/impostors.md:600; rendering: direct. **Verdict:** REMOVE — const (ImpostorLayout.cs:110).
- **`MEITOU_IMPOSTOR_SMALL_GRID`** (env) — default 12; *Kind*: tuning knob; docs/impostors.md:600 (as `_GRID`); rendering: direct. **Verdict:** REMOVE — const (ImpostorLayout.cs:111).
- **`MEITOU_IMPOSTOR_GRID`** (env) — default 12; *Kind*: tuning knob "for experiments"; docs/impostors.md:77, render-foliage.md:101; rendering: direct. **Verdict:** REMOVE — `DefaultGrid = 12` (ImpostorLayout.cs:136); the `EnvFloat` helper (:132-133) goes with the last user.
- **`MEITOU_IMPOSTOR_MAGNIFY`** (env) — default 1.4; *Kind*: tuning knob; docs/impostors.md:74, render-foliage.md:102; rendering: direct. **Verdict:** REMOVE — const (ImpostorLayout.cs:138).
- **`MEITOU_IMPOSTOR_BIAS`** (env) — default 1 (0 turns the bake LOD bias off "for comparisons"); *Kind*: tuning/A-B knob; render-foliage.md:102; rendering: direct (bake quality). **Verdict:** REMOVE — const 1 (ImpostorLayout.cs:140); delete the `BiasScale` multiplications.
- **`MEITOU_IMPOSTOR_ROCK_MAX_FRAME`** (env) — default 256; *Kind*: tuning knob; docs/impostors.md:688; rendering: direct. **Verdict:** REMOVE — const (ImpostorLayout.cs:158).

### src/Meitou.Rendering/Impostors/ImpostorPrepass.cs, ImpostorQuad.cs

- **`MEITOU_IMPOSTOR_PREPASS`** (env) — default on (`=0` off); *Kind*: kill-switch of the `impostor-prepass` Meitou feature; bench switch (WorldApp.Bench.cs:65); used by docs/bench.md:96, render-foliage.md:233,237; rendering: direct. **Verdict:** REMOVE — `Enabled = true` (ImpostorPrepass.cs:12); keep the property for the bench switch (or retire the whole switch).
- **`MEITOU_IMPOSTOR_QUAD`** (env) — default on; *Kind*: kill-switch of the `impostor-quad` feature; bench switch (:66); same docs; rendering: direct. **Verdict:** REMOVE — `Enabled = true` (ImpostorQuad.cs:13).

### src/Meitou.Rendering/Landmarks.cs

- **`MEITOU_LANDMARK_SURVEY`** (env) — default off; surveys the whole world's placements by bounding radius and exits (it picked the 2000-unit landmark threshold); *Kind*: one-off diagnostic, decision made (`LandmarkClass.MinRadius`); used by docs/renderer-native.md:3391 (the result), `tools/Meitou.ModelViewer/WorldApp.cs:57-62` (the entry that returns 0 after the survey), `LandmarkClass.Survey` (Landmarks.cs:87 on); rendering: none. **Verdict:** REMOVE — delete the `if` block in WorldApp.cs:57-62 and `LandmarkClass.Survey` with its helper types (nothing else calls it); keep the results in the doc.

### src/Meitou.Rendering/MeshTexelScale.cs, MipStreaming.cs

(Family with `MEITOU_MESH_STREAM`, `MEITOU_MIP_SKIP` from other groups.)

- **`MEITOU_MIP_TOP`** (env) — default 0.005; *Kind*: tuning knob; docs/render-foliage.md:150, renderer-native.md:3193; rendering: direct. **Verdict:** REMOVE — const 0.005 (MeshTexelScale.cs:18).
- **`MEITOU_MIP_MARGIN`** (env) — default 2 levels; *Kind*: tuning knob; same docs; `tests/Meitou.Tests/Rendering/ObjectStreamingTests.cs:55` reads the field `MipStreaming.Margin` (not the env); rendering: direct. **Verdict:** REMOVE — `Margin = 2f` as a const (MipStreaming.cs:21); the test keeps working.
- **`MEITOU_MIP_STREAM`** (env) — default on (`=0` turns mip streaming off "for comparisons"); *Kind*: kill-switch for a feature now always on (it exists to save VRAM; off is only for measuring pop-in); used by docs/renderer-native.md:3193,3238 (the base-build comparison), render-foliage.md:149-150; rendering: direct. **Verdict:** REMOVE — `Allowed = true` (MipStreaming.cs:34) and delete the `Allowed` tests at the call sites.

### src/Meitou.Rendering/ObjectLodGen.cs

- **`MEITOU_OBJECT_LOD_GEN`** (env) — default on (`=0` generates nothing); *Kind*: kill-switch for generated object levels; bench switch `object-lod-gen` (WorldApp.Bench.cs:107); used by docs/render-objects.md:84-87 region; rendering: direct. **Verdict:** REMOVE — `Enabled = true` (ObjectLodGen.cs:18).
- **`MEITOU_OBJECT_LOD_PIXELS`** (env) — default 1.5; *Kind*: tuning knob; docs/render-objects.md:84 (indirect); test `ObjectLodGenTests.cs:32` reads the property `Tolerance`; rendering: direct. **Verdict:** REMOVE — const 1.5 (ObjectLodGen.cs:30).
- **`MEITOU_OBJECT_LOD_TRI`** (env) — default 1.5; *Kind*: tuning knob (mirrors foliage `MEITOU_SCREEN_LOD_TRI`); no docs; rendering: direct. **Verdict:** REMOVE — const (ObjectLodGen.cs:33).
- **`MEITOU_OBJECT_LOD_MULT`** (env) — default 2; *Kind*: tuning knob (mirrors `MEITOU_SCREEN_LOD_MULT`); no docs; rendering: direct. **Verdict:** REMOVE — const (ObjectLodGen.cs:34).
- **`MEITOU_OBJECT_LOD_NORMAL`** (env) — default 0.004; *Kind*: tuning knob; no docs; rendering: direct. **Verdict:** REMOVE — const (ObjectLodGen.cs:37); then `ObjectLodGen.Env` (:47) is unused and goes.
- **`MEITOU_OBJECT_LOD_CACHE`** (env, directory) — default `%LOCALAPPDATA%\Meitou\objlods`; *Kind*: cache location override; docs/render-objects.md:87; rendering: indirect. **Verdict:** MERGE → the common `MEITOU_CACHE_DIR` root (see `MEITOU_IMPOSTOR_CACHE`).

### src/Meitou.Rendering/ParticleRenderer.cs

- **`MEITOU_PARTICLE_LOG`** (env) — default off; one line per frame whose particle work exceeds 2 ms, with the phase breakdown, plus weather-swap lines; *Kind*: diagnostic dump (the stutter hunt of 2026-10-10 is done: docs/formats/particle-universe.md:315,340,377); rendering: none. Overlaps `--log-spikes`. **Verdict:** MERGE → `--log-spikes` (feed the >2 ms line as a spike note), or REMOVE outright: delete `PhaseLog` (ParticleRenderer.cs:263), `LogPhases` and the `phase*` fields if nothing else uses them, and the three `PhaseLog` prints (:289-296, :392).
- **`MEITOU_COVERAGE_LOG`** (env) — default off; prints `coverage N draws, mask ...` each time `DrawCoverage` runs (and reads the env every frame); *Kind*: debug print; no docs, no tests; rendering: none. **Verdict:** REMOVE — delete ParticleRenderer.cs:527.

### src/Meitou.Rendering/PostProcess.Merged.cs, PostProcess.SceneLayers.cs, PostProcess.Shafts.cs, PostProcess.SunMotion.cs, PostProcess.cs

These ten are the startup forms of Meitou post-process switches made default-on on 2026-10-09/11. Each decided one has an `--ab` switch in WorldApp.Bench.cs (post-merge :51, layers-lean :43, ssao-mask-skip :45, shafts-scan :47, plus layer-merge, fog-half, shafts-half, sun-motion) and a docs/render-post.md paragraph that names the env var. They are performance restructurings with the same picture, so `--faithful all` does not touch them.

- **`MEITOU_POST_MERGE`** (env, `0`/`velocity`/`exposure`) — default on; *Kind*: kill-switch; bench switches `post-merge`, `post-merge-velocity`, `post-merge-exposure`; docs/render-post.md:56,60; rendering: indirect. **Verdict:** REMOVE — `MergeVelocity = MergeExposure = true` (PostProcess.Merged.cs:24,26).
- **`MEITOU_LAYER_MERGE`** (env) — default on; *Kind*: kill-switch; `layer-merge` bench switch; docs/render-post.md:58; rendering: direct. **Verdict:** REMOVE — `MergeLayers = true` (SceneLayers.cs:19).
- **`MEITOU_FOG_HALF`** (env) — default on; *Kind*: kill-switch (fog at half size); `fog-half` bench switch; docs/render-post.md:58, bench.md:81; rendering: direct. **Verdict:** REMOVE — `FogHalf = true` (SceneLayers.cs:21).
- **`MEITOU_SHAFTS_HALF`** (env) — default on; *Kind*: kill-switch; `shafts-half`; docs/render-post.md:59; rendering: direct. **Verdict:** REMOVE — `ShaftsHalf = true` (SceneLayers.cs:24).
- **`MEITOU_LAYERS_LEAN`** (env) — default on; *Kind*: kill-switch; `layers-lean` (WorldApp.Bench.cs:43); docs/render-post.md:59; rendering: direct. **Verdict:** REMOVE — `LeanLayers = true` (SceneLayers.cs:27). The bench aggregates `layers-all` and `ssao-layers` collapse with these.
- **`MEITOU_SHAFT_SCAN`** (env) — default on (`=0` uses the two fragment passes); *Kind*: kill-switch; `shafts-scan` (:47, registered only where storage images are supported); docs/render-shafts.md:52; rendering: direct. **Verdict:** REMOVE — `ShaftScan = true` (Shafts.cs:65); the support check stays.
- **`MEITOU_SHAFT_STAMPS`** (env) — default off; splits the shafts stage into per-pass timing stamps (`shafts-inject/-integrate/-scatter/-scan`); *Kind*: bench/diagnostic profiling switch; docs/bench.md:82,222, render-shafts.md:65 (measurements recorded); rendering: none. **Verdict:** MERGE → one shared "fine post stamps" switch with `MEITOU_SSAO_STAMPS`, turned on by the bench harness (e.g. `--bench-frames --bench-stamps`) instead of two env vars (Shafts.cs:129).
- **`MEITOU_SUN_MOTION`** (env) — default on; *Kind*: kill-switch of the `sun-motion` feature; bench switch; docs/render-post.md:57; rendering: direct. **Verdict:** REMOVE — `SunMotion = true` (SunMotion.cs:13).
- **`MEITOU_SSAO_MASK_SKIP`** (env) — default on; *Kind*: kill-switch; `ssao-mask-skip` (:45); docs/render-post.md:59; rendering: direct. **Verdict:** REMOVE — `MaskSkip = true` (PostProcess.cs:1134).
- **`MEITOU_SSAO_STAMPS`** (env) — default off; per-pass stamps for the SSAO stage (`ssao gen`, `ssao blur x/y`, `layers lean`); *Kind*: bench/diagnostic profiling switch; docs/bench.md:82, render-post.md:59; rendering: none. **Verdict:** MERGE → the shared post-stamps switch with `MEITOU_SHAFT_STAMPS` (PostProcess.cs:1136).

### src/Meitou.Rendering/PostProcessOptions.cs

- **`--post`** (flag, `meitou`|`kenshi`|`off`) — default `meitou`; chooses the post-processing preset (`CopyFrom(Create(...))`, must come before the individual options); *Kind*: user-facing setting (Faithful/Meitou level); used by docs/render-post.md:14,29, formats/post-processing.md:220, WorldFrame.cs usage text :317; rendering: direct. **Verdict:** KEEP — the preset switch; individual features are reachable through `--faithful`/`--meitou <ids>`.
- **`--ssao`** (flag) — default on in the Meitou preset (off in `kenshi`/`off`); forces SSAO on; *Kind*: user-facing setting, **duplicate of `--meitou ao`** (Enhancements.cs:58, id `ao`, `post.Ssao`; also F1 at run time); used by docs/render-post.md:15 and the usage strings PostProcessOptions.cs:96, WorldFrame.cs:318 only; no scripts or tests; rendering: direct. **Verdict:** MERGE → `--meitou ao`; delete the case at PostProcessOptions.cs:123, the usage text and the doc mention.
- **`--no-ssao`** (flag) — forces SSAO off; *Kind*: duplicate of `--faithful ao` (the `--low-end` preset already uses `--faithful ao`, WorldFrame.cs:211); used by nothing besides the usage strings; rendering: direct. **Verdict:** MERGE → `--faithful ao`; delete PostProcessOptions.cs:124 with `--ssao`. (`--ssao-radius`, `--ssao-strength`, `--ssao-character-strength` are tuning flags in another group.)

---

**Group totals:** 75 items: keep 6, merge 11, remove 58.

Keep (6): `MEITOU_PASS_STATS`, `MEITOU_FORCE_INTEGRATED`, `MEITOU_VRAM_BUDGET_MB`, `MEITOU_VK_VALIDATION`, `MEITOU_FRAGMENT` (not an option), `--post`.
Merge (11): `MEITOU_FOLIAGE_RANGE`, `MEITOU_GRASS_RANGE`, `MEITOU_GRASS_DENSITY` (-> flags), `MEITOU_STREAM_LOG`, `MEITOU_PARTICLE_LOG` (-> `--log-spikes`), `MEITOU_IMPOSTOR_CACHE`, `MEITOU_OBJECT_LOD_CACHE` (-> `MEITOU_CACHE_DIR`), `MEITOU_SHAFT_STAMPS`, `MEITOU_SSAO_STAMPS` (-> one stamps switch), `--ssao`, `--no-ssao` (-> `--meitou ao` / `--faithful ao`).
Remove (58): everything else.

Cross-group families worth removing together: GI kill-switches (`MEITOU_GI_*`, with the `gi-*` bench switches), post-merge/layers family (`MEITOU_POST_MERGE`, `LAYER_MERGE`, `FOG_HALF`, `SHAFTS_HALF`, `LAYERS_LEAN`, `SHAFT_SCAN`, `SUN_MOTION`, `SSAO_MASK_SKIP`, plus other groups' `MEITOU_FOG_VRS`, `MEITOU_WATER_LEAN`, `MEITOU_CARD_TRIM`, `MEITOU_SKY_FOG_SKIP`, `MEITOU_OCC_ALT`), the mip/mesh streaming knobs (with `MEITOU_MESH_STREAM`, `MEITOU_MIP_SKIP`), the CPU-reference path (`MEITOU_GPU_CULL`, `_VERIFY`, `MEITOU_GPU_GRASS`), the lod tuning knobs (`MEITOU_LOD_PIXELS/FAR/NORMAL`, `MEITOU_OBJECT_LOD_*`, `MEITOU_SCREEN_LOD_*`), the impostor tuning knobs, and the timing/log diagnostics (`MEITOU_FOLIAGE_TIMING`, `MEITOU_MESH_TIMING`, `MEITOU_STREAM_LOG`, `MEITOU_PARTICLE_LOG`, `MEITOU_DRAW_LOG`) which `--log-spikes` or the bench harness replace.


Worktree `R:\VlcekM\MeitouClient-cleanup`, read-only. Flag parsing for the first 42 items is one switch, `PostOptions.TryParse` (called from `WorldFrame.cs:473`). Nothing in `tests/`, `tools/scripts/` or `.github/` passes any of the 42 flags on a command line. Tests read the `PostOptions` fields directly, plus `BenchHarnessTests.LowEndExpandsInPlaceAndLaterOptionsWin`, which checks `--low-end` expands to `LowResParticles=true, ParticleDivisor=4, Fxaa=false`.

Conventions used below:
- "slider" = a Tab-panel slider in `WorldFrame.cs` ~1150-1190. Its value is saved to the user's settings by `GameHost.Interactive.cs:239`, so a flag that duplicates a slider is redundant.
- "switch" = a Faithful/Meitou entry in `Enhancements.Create`, set from the command line with `--meitou <id>` / `--faithful <id|all>` (`WorldFrame.cs:437`).
- "Used by: docs only" means the option appears only in prose or measurement recipes under `docs/`; those mentions go stale with the flag.

#### Flags: `src/Meitou.Rendering/PostProcessOptions.cs`

### src/Meitou.Rendering/PostProcessOptions.cs

- **`--dither`** (flag) — default on in Meitou; sets `Dither=true` (the ordered-noise add at the end of the composite shader). *Kind:* Faithful/Meitou duplicate. Used by: only the Usage text in `WorldFrame.cs:318`. Rendering: direct. **Verdict:** MERGE → `--meitou dither` (the `dither` switch, `Enhancements.cs:60`). Delete the parse line.
- **`--no-dither`** (flag) — sets `Dither=false`. *Kind:* Faithful/Meitou duplicate. Used by: Usage text only. Rendering: direct. **Verdict:** MERGE → `--faithful dither`. Delete the parse line and the `WorldFrame.cs:318` Usage text.
- **`--particles-low`** (flag) — default on in Meitou; the weather's big alpha/additive particle sprites are drawn into a half or quarter size target. *Kind:* Faithful/Meitou duplicate. Used by: `--low-end` (`WorldFrame.cs:212`), `BenchHarnessTests` (through `--low-end`). Rendering: direct. **Verdict:** MERGE → `--meitou particles`. Replace it inside the `LowEnd` array with `"--meitou","particles"` and delete the parse line.
- **`--no-particles-low`** (flag) — draws every particle at full size, as the game does. *Kind:* Faithful/Meitou duplicate. Used by: docs only (`render-post.md:31`, `formats/particle-universe.md:215`, which already say `--faithful particles` does the same). Rendering: direct. **Verdict:** MERGE → `--faithful particles`. Delete the parse line and the two doc mentions.
- **`--particle-divisor <0|1|2|4>`** (flag) — default 0 (target size follows the sprites' mean screen size); 1/2/4 force one divisor "for measuring". *Kind:* tuning/measurement. Used by: `--low-end` (`4`), `ParticleSliderTests` (field), the Tab "weather particles" slider (modes quarter/half/by-size/full), docs only for the measurements. Rendering: direct. **Verdict:** MERGE → one flag shaped like the slider, e.g. `--particles <full|auto|half|quarter>`, replacing `--particles-low`, `--no-particles-low` and `--particle-divisor`. Keep `ParticleDivisor` as a field (the slider and `WorldFrame.cs:1030` use it). Delete the 1-divisor forced path only if nobody measures it again.
- **`--shafts`** (flag) — default on in Meitou; light shafts (shadowed haze) on. *Kind:* Faithful/Meitou duplicate. Used by: docs only (`bench.md`, `render-shafts.md`), and a mention in `LightShaftShaders.cs:179`. Rendering: direct. **Verdict:** MERGE → `--meitou shafts` (switch `shafts`, `Enhancements.cs:100`). Delete the parse line.
- **`--no-shafts`** (flag) — turns the shafts off. *Kind:* Faithful/Meitou duplicate. Used by: `CHANGELOG.md:13`, `render-post.md:30`, `render-shafts.md:3` (already says `--faithful shafts` does the same). Rendering: direct. **Verdict:** MERGE → `--faithful shafts`. Delete the parse line; fix the three doc mentions (the CHANGELOG entry is history and can stay).
- **`--shafts-strength <0..1>`** (flag) — default 0.8; how much haze in shadow goes. *Kind:* tuning knob. Used by: Tab slider "Light shafts strength" (`WorldFrame.cs:1185`), `render-shafts.md:19`. Rendering: direct. **Verdict:** REMOVE the flag; the slider and saved settings own it. Keep `ShaftStrength` as a field (read at `WorldFrame.cs:1530`).
- **`--shafts-sky <0..1>`** (flag) — default 0.5; how far the sky darkens behind shadowed air. *Kind:* tuning knob, decided. Used by: `render-shafts.md:25` only; read once at `PostProcess.Shafts.cs:230`. Rendering: direct. **Verdict:** REMOVE. Turn `ShaftSky` into `const 0.5f`; delete the parse line and the doc mention.
- **`--shafts-near <u>`** (flag) — default 50; where the first froxel slice starts. *Kind:* tuning knob, decided. Used by: `render-shafts.md:33`; read at `PostProcess.Shafts.cs:164,181`. Rendering: direct. **Verdict:** REMOVE. `ShaftNear` becomes `const 50f`.
- **`--shafts-debug`** (flag) — default off; makes the picture the share of haze the sun reaches (red/green/blue debug view). *Kind:* debug view. Used by: `render-shafts.md:78,147`, `bench.md:222` (stills); the only consumer is the shader branch `LightShaftShaders.cs:438` fed by `PostProcess.Shafts.cs:230`. Rendering: direct. **Verdict:** REMOVE. Delete `ShaftDebug`, the `uShaftParams.z > 0.5` shader branch and the third component in `parameters` (`PostProcess.Shafts.cs:230`), the parse line and the two doc sections.
- **`--shafts-air <x>`** (flag) — default 1 (0 = no air layer); multiple of the base air density. *Kind:* tuning knob. Used by: Tab slider "Light shafts air" (`WorldFrame.cs:1186`), `render-shafts.md:116`. Rendering: direct. **Verdict:** REMOVE the flag; the slider owns it. Keep `ShaftAir` as a field.
- **`--shafts-air-height <u>`** (flag) — default 800 (scale height). *Kind:* tuning knob, decided. Used by: `render-shafts.md:117`; read at `PostProcess.Shafts.cs:164,179`. Rendering: direct. **Verdict:** REMOVE. `ShaftAirHeight` becomes `const 800f`.
- **`--shafts-air-phase <0..0.95>`** (flag) — default 0.6 (Henyey-Greenstein g). *Kind:* tuning knob, decided. Used by: `render-shafts.md:121`; read at `PostProcess.Shafts.cs:179`. Rendering: direct. **Verdict:** REMOVE. `ShaftAirPhase` becomes `const 0.6f`.
- **`--shafts-air-dawn <x>`** (flag) — default 1; air density multiple at sunrise and sunset. *Kind:* tuning knob. Used by: Tab slider "Light shafts air at dawn and dusk" (`WorldFrame.cs:1187`), `LightShaftTests.cs` (field), `render-shafts.md:124`. Rendering: direct. **Verdict:** REMOVE the flag; the slider owns it. Keep the field.
- **`--shafts-air-day <x>`** (flag) — default 0 (no air layer at midday). *Kind:* tuning knob, decided. Used by: `LightShaftTests.cs:18-24` (reads `o.ShaftAirDay`), `render-shafts.md:125`; read at `WorldFrame.cs:1519`. Rendering: direct. **Verdict:** REMOVE. Make `ShaftAirDay` a `const` and change the test to `PostOptions.ShaftAirDay`.
- **`--shafts-air-ramp <h>`** (flag) — default 5.5 hours. *Kind:* tuning knob, decided. Used by: `LightShaftTests.cs` (field), `render-shafts.md:125`; read at `WorldFrame.cs:1518`. Rendering: direct. **Verdict:** REMOVE. `ShaftAirRamp` becomes `const 5.5f`; same test edit as above.
- **`--shafts-clouds <0..1>`** (flag) — default 0.85 (`ShaftCloudShade`). *Kind:* tuning knob, decided. Used by: `LightShaftTests.cs:36-37` (field), `render-shafts.md:108`; read at `WorldFrame.cs:1546`. Rendering: direct. **Verdict:** REMOVE. Make it a `const`; test uses `PostOptions.ShaftCloudShade`.
- **`--shafts-clouds-from <c>`** (flag) — default 0.3 (`ShaftCloudFrom`). *Kind:* tuning knob, decided. Used by: `LightShaftTests.cs` (field), `render-shafts.md:108`; read at `WorldFrame.cs:1545`. Rendering: direct. **Verdict:** REMOVE. Same treatment as `--shafts-clouds`.
- **`--shafts-grid <cells>,<slices>[,<samples>]`** (flag) — default 160,64,2 (the froxel grid). *Kind:* tuning/measurement knob. Used by: `render-shafts.md:35,140` (a cost table). Read at `PostProcess.Shafts.cs:146,164,200`. Rendering: direct. **Verdict:** REMOVE. `ShaftCells/Slices/Samples` become consts (`ShaftSamples` still feeds the `uShaftSamples` uniform); delete the parse block with its three `int.Parse`s.
- **`--fxaa`** (flag) — default on; sets `Fxaa=true`. *Kind:* default restated. Used by: `formats/settings.md:352` only. Rendering: direct. **Verdict:** REMOVE. It only undoes `--no-fxaa`; `--faithful aa` or the AA slider does that. Delete the parse line and the doc mention.
- **`--no-fxaa`** (flag) — turns the game's FXAA off (the game's own `FXAA=0` setting). *Kind:* user-facing, mirrors a game setting. Used by: `--low-end` (`WorldFrame.cs:211`), `BenchHarnessTests` (via `--low-end`), `WorldFrame.cs:318` Usage, docs (`render-post.md:18`, `formats/post-processing.md:229`, `formats/settings.md:352`). Rendering: direct. **Verdict:** KEEP. It is the only command-line route to "no anti-aliasing at all" (the `aa` switch's Faithful side is FXAA).
- **`--heat-haze <x>`** (flag) — default off; replaces the weather's `heat haze` field with x, for testing in a weather without it. *Kind:* debug/test override. Used by: docs only (`render-post.md:22`, `formats/weather.md:273,646`, `renderer-native.md:1813`, `formats/settings.md:352`). Read at `WorldFrame.cs:1563,1565`. Rendering: direct. **Verdict:** REMOVE. Delete `HeatHazeOverride`, simplify `WorldFrame.cs:1551-1565` to the weather's value (testing is done by forcing a hot weather with `\` or `--weather`), the parse line and the `Describe()` branch.
- **`--no-heat-haze`** (flag) — turns the heat-haze pass off (the game's `HeatHaze=0`). *Kind:* user-facing, mirrors a game setting. Used by: docs (`render-post.md:22,43`, `formats/post-processing.md:195,232`, `formats/weather.md:274`, `formats/settings.md:352`), `WorldFrame.cs:319` Usage. Rendering: direct. **Verdict:** KEEP. (`--heat-haze-strength 0` has the same effect, but there is no heat-haze on/off switch otherwise.)
- **`--heat-haze-strength <x>`** (flag) — default 0.5 in Meitou (1 = the game's). *Kind:* tuning knob. Used by: switch `heathaze` (`Enhancements.cs:98`), Tab slider "Heat haze strength" (0..3), docs (`render-post.md:24`, `formats/post-processing.md:232`). Rendering: direct. **Verdict:** REMOVE. The `--meitou/--faithful heathaze` switch and the slider cover it. Keep the `HeatHazeStrength` field.
- **`--tonemap <clamp|shoulder|aces|hybrid>`** (flag) — default hybrid in Meitou. *Kind:* tuning/A-B. Used by: switch `tonemap` (`Enhancements.cs:92`, hybrid vs clamp), Tab slider "Tone map" (0..3), docs (`render-post.md:23`, `formats/post-processing.md:224`). Rendering: direct. **Verdict:** REMOVE. `--faithful tonemap` gives clamp, the slider gives shoulder/aces. Keep the enum (the shader uses it).
- **`--tonemap-mix <0..1>`** (flag) — default 0.75 (hybrid's ACES share). *Kind:* tuning knob. Used by: Tab slider "Hybrid tone map: ACES share" (`WorldFrame.cs:1180`), `render-post.md:23`. Rendering: direct. **Verdict:** REMOVE. The slider owns it.
- **`--grade`** (flag) — default on in Meitou; saturation/contrast grade on. *Kind:* Faithful/Meitou duplicate. Used by: docs (`render-post.md:23`, `formats/post-processing.md:228`, which still says "off by default", stale). Rendering: direct. **Verdict:** REMOVE. The `tonemap` switch turns grade and curve on together; the "Grading" slider does it alone. Delete the parse line and fix the stale doc.
- **`--no-grade`** (flag) — grade off. *Kind:* Faithful/Meitou duplicate. Used by: nothing (not even docs). Rendering: direct. **Verdict:** REMOVE. `--faithful tonemap` or the "Grading" slider covers it.
- **`--saturation <x>`** (flag) — default 1.06. *Kind:* tuning knob, decided. Used by: `render-post.md:23`; read at `PostProcess.cs:695`. Rendering: direct. **Verdict:** REMOVE. `Saturation` becomes `const 1.06f` (the slider's text only prints it).
- **`--contrast <x>`** (flag) — default 1.05. *Kind:* tuning knob, decided. Used by: `render-post.md:23`; read at `PostProcess.cs:696`. Rendering: direct. **Verdict:** REMOVE. `Contrast` becomes `const 1.05f`.
- **`--night-grade <0..1>`** (flag) — default 0.7 in Meitou (`NightGrade.MeitouStrength`). *Kind:* tuning knob. Used by: switch `nightgrade` (`Enhancements.cs:110`), Tab slider "Night grading strength", `NightGradeTests` (field), docs (`render-post.md:25`, `formats/post-processing.md:231`). Rendering: direct. **Verdict:** REMOVE. Switch and slider cover it.
- **`--no-night-grade`** (flag) — `NightGradeStrength=0`. *Kind:* Faithful/Meitou duplicate. Used by: `render-post.md:25` only. Rendering: direct. **Verdict:** MERGE → `--faithful nightgrade`. Delete the parse line and the doc mention.
- **`--heat-haze-view-distance <u>`** (flag) — default 12000 (the install's `view distance`); sets the heat haze's depth scale D = 10 x this. *Kind:* tuning knob, decided (the doc says 5000 "brings the earlier look back"). Used by: docs only (`render-post.md:22`, `formats/post-processing.md:178,191`, `formats/settings.md:347`, `formats/weather.md:275`). Read once at `WorldFrame.cs:1569`. Rendering: direct. **Verdict:** REMOVE. Use `Meitou.Data.World.HeatHaze.ViewDistanceSetting` directly at `WorldFrame.cs:1569`; delete `HeatHazeViewDistance`, the parse line and the doc mentions.
- **`--post-debug <ao>`** (flag) — default off; shows the SSAO term alone. *Kind:* debug view. Used by: docs only (`render-post.md:20`, `formats/post-processing.md:93,101`, `renderer-native.md:2876`, `render-foliage.md:223`). Consumers: `uDebug == 1` at `PostProcessShaders.cs:690`, `PostProcess.cs:691`, `TerrainRenderer.cs:489` (a different, terrain `options.Debug`). Rendering: direct. **Verdict:** REMOVE. Delete `Debug`, the `uDebug` uniform and its shader branch in the composite, the `CopyFrom` assignment, the parse line and the doc mentions (the general `--debug`/V key stays).
- **`--upscaler <off|taa|fsr|dlss>`** (flag) — default `taa` (Meitou), `off` in Faithful. *Kind:* user-facing. Used by: `package-release.ps1:30,91`, `parity.sh:5`, `GameHost.cs:150`, `WorldApp.cs`, heavily by `docs/bench.md` and the render docs. Rendering: direct. **Verdict:** KEEP.
- **`--render-scale <0.25..1|native|quality|...>`** (flag) — default 0.83 (Meitou temporal), 1 otherwise. *Kind:* user-facing. Used by: `--low-end` (`WorldFrame.cs:211`), `--ab render-scale`, `Upscaling.cs:37`, bench docs. Rendering: direct. **Verdict:** KEEP.
- **`--sharpness <0..1>`** (flag) — default 0.3. *Kind:* tuning knob. Used by: Tab slider "Upscaler sharpness" (`WorldFrame.cs:1168`), `docs/engine.md`. Only FSR reads it (`FsrUpscaler.cs:189-190`); `DlssOptions.Sharpness` is declared but never set, and TAA ignores it. Rendering: direct. **Verdict:** REMOVE the flag; the slider keeps it. (If FSR is ever dropped, the slider and `UpscaleOptions.Sharpness` go too; the docs line "FSR's RCAS, DLSS's own" is wrong for DLSS.)
- **`--exposure <x>`** (flag) — default 1; linear scale of the scene before the composite. *Kind:* tuning knob. Used by: `-` / `=` keys (`WorldApp.cs:388-389`), docs (`render-post.md`, `formats/post-processing.md`). Rendering: direct. **Verdict:** REMOVE the flag; the live keys keep the field.
- **`--ssao-radius <units>`** (flag) — default 12. *Kind:* tuning knob, decided. Used by: only the Usage text; read at `PostProcess.cs:1147`. Rendering: direct. **Verdict:** REMOVE. `SsaoRadius` becomes `const 12f`; delete the parse line and Usage text.
- **`--ssao-strength <x>`** (flag) — default 4. *Kind:* tuning knob, decided. Used by: only the Usage text; read at `PostProcess.cs:1148`. Rendering: direct. **Verdict:** REMOVE. `SsaoStrength` becomes `const 4f`.
- **`--ssao-character-strength <0..1>`** (flag) — default 0.25; SSAO kept on characters' own pixels. *Kind:* tuning knob, decided. Used by: docs only (`render-post.md:21`, `formats/post-processing.md`), Usage text; read at `PostProcess.cs:1170,1174`. Rendering: direct. **Verdict:** REMOVE. `SsaoCharacterStrength` becomes `const 0.25f`. The `MaskSkip` A/B (`ssao-mask-skip`) is a separate switch.

#### Env vars

### src/Meitou.Rendering/ReflectionPass.cs

- **`MEITOU_REFL_AB_SAMPLES`** (env) — default 1; the "B" sample count of `--ab refl-msaa`. *Kind:* bench/A-B harness knob. Used by: `WorldApp.Bench.cs:119` (through `rp.AbSamples`), `docs/render-water.md:259`, `docs/bench.md:28`. Rendering: direct. **Verdict:** REMOVE. Make `AbSamples` a plain `const 1`; the `refl-msaa` toggle keeps working.
- **`MEITOU_REFL_OBJECTS`** (env) — default 2000; the reflection's object distance (`ReflectionPass.ObjectDistance`). *Kind:* tuning knob, decided (the `refl-cull` A/B governs). Used by: `ReflectionPass.cs:63-64` comment, `bench.md:63`. Rendering: direct. **Verdict:** REMOVE. Delete the env read.
- **`MEITOU_REFL_`** (env prefix) — the `Env(name, default)` helper at `ReflectionPass.cs:64`; it builds `MEITOU_REFL_OBJECTS`, `_FOLIAGE` (2200), `_LOD` (3), `_AGE` (3), `_AB_SAMPLES` and an unlisted `_TLOD` (0.5). *Kind:* tuning knobs, decided. Used by: `docs/render-foliage.md:189-190` (`_FOLIAGE`), `docs/render-water.md:258` (`_AGE`); `bench.md:80` cites `MEITOU_REFL_MOVING` / `_MOVE_FORCE`, which exist nowhere in code (stale). Rendering: direct. **Verdict:** REMOVE. Delete the helper and the six lines (`ReflectionPass.cs:65-70`); the property defaults are the shipped values. `_FOLIAGE`, `_LOD`, `_AGE`, `_TLOD` go with it.
- **`MEITOU_REFL_FOG`** (env) — default on; `=0` skips the fog pass over the reflection (`ReflectionPass.NoFog`). *Kind:* kill-switch for a feature that is now always on (no `--ab` toggle registered). Used by: `docs/render-water.md:248,250`. Read at `ReflectionPass.cs:342,477`. Rendering: direct. **Verdict:** REMOVE. Delete `NoFog` and the `!NoFog` test at line 342; fix the two doc lines.

### src/Meitou.Rendering/ShadowLod.cs

- **`MEITOU_SHADOW_COARSE`** (env) — default on; `=0` starts the coarse far-cascade casters off. *Kind:* kill-switch for an always-on feature (starting value of the `--ab shadow-coarse` toggle). Used by: `docs/render-shadows.md:74`; `ShadowLodTests` sets `Enabled` directly. Rendering: direct. **Verdict:** REMOVE the env read (`Enabled { get; set; } = true`); the `--ab` toggle stays as the way to flip it.
- **`MEITOU_SHADOW_OBJECT_TEXELS`** (env) — default 1. *Kind:* tuning knob, decided. Used by: `docs/render-shadows.md:69,74`; read through `EnvZero` at `ShadowLod.cs:22`. Rendering: direct. **Verdict:** REMOVE. Make `ObjectTexels` a constant.
- **`MEITOU_SHADOW_TERRAIN_TEXELS`** (env) — default 1 ("does nothing at the default, on purpose"). *Kind:* tuning knob, decided. Used by: `docs/render-shadows.md:69,70,74`. Rendering: direct. **Verdict:** REMOVE. Make `TerrainTexels` a constant.
- **`MEITOU_SHADOW_ROCK_TEXELS`** (env) — default 4. *Kind:* tuning knob, decided. Used by: `docs/render-shadows.md:69,74`. Rendering: direct. **Verdict:** REMOVE. Constant.
- **`MEITOU_SHADOW_IMPOSTOR_TEXELS`** (env) — default 8 (0 = never cast impostors). *Kind:* tuning knob, decided. Used by: `docs/render-shadows.md:69,74`. Rendering: direct. **Verdict:** REMOVE. Constant; delete the `EnvZero` helper (`ShadowLod.cs:75`) once all four are gone.

### src/Meitou.Rendering/ShadowPass.Meitou.cs, ShadowPass.cs

- **`MEITOU_SHADOW_CADENCE`** (env) — default `1,4,16,32`; frames between redraws of each shadow cascade. *Kind:* bench/A-B knob (an older schedule `1,2,4,4` was measured against it). Used by: `docs/bench.md:32-33` (says "it stays"), `docs/formats/shadows.md:267,287`, `docs/render-shadows.md:72`. Read at `ShadowPass.Meitou.cs:85`. Rendering: direct. **Verdict:** REMOVE. The schedule is decided: `Cadence` becomes a literal array and `ParseCadence` goes; the `bench.md` sentence that says it stays should change with it.
- **`MEITOU_SHADOW_BUDGET`** (env) — default `ShadowSchedule.DefaultBudget` (1 far cascade per frame; 0 = no limit). *Kind:* A/B knob. Used by: `docs/render-shadows.md:63,72`, `docs/bench.md:62`; the `shadow-spread` toggle (`WorldApp.Bench.cs:54`) already flips `FarBudget` at run time. Rendering: direct. **Verdict:** REMOVE the env read (`FarBudget = ShadowSchedule.DefaultBudget`); the toggle covers the A/B.
- **`MEITOU_SHADOW_FOGCAP`** (env) — default on; `=0` starts with the shadow range not capped at the placed fog's hide distance. *Kind:* kill-switch for an always-on feature (starting value of the `shadow-fogcap` toggle). Used by: `docs/bench.md:85`, `docs/render-shadows.md:83`. Rendering: direct. **Verdict:** REMOVE the env read (`FogCapEnabled { get; set; } = true`); the toggle stays.

### src/Meitou.Rendering/SkyRenderer.Clouds.cs, SkyRenderer.cs

- **`MEITOU_CLOUD_FOG_SKIP`** (env) — default on; `=0` starts the volumetric-cloud fog early-out off. *Kind:* kill-switch for an always-on feature (starting value of `--ab cloud-fog-skip`). Used by: `docs/render-clouds.md:129`. Rendering: direct. **Verdict:** REMOVE the env read; keep the `--ab` toggle.
- **`MEITOU_SKY_FOG_SKIP`** (env) — default on; same for the sky pass (`--ab sky-fog-skip`). *Kind:* kill-switch. Used by: `docs/render-clouds.md:181,216`. Rendering: direct. **Verdict:** REMOVE the env read; keep the `--ab` toggle.
- **`MEITOU_STAR_LAYERS`** (env) — default all layers (the integrated-GPU default is `StarField.IntegratedLayers` = 2); `0..3` forces a count. *Kind:* debug/tuning override. Used by: `docs/formats/sky.md:515` only. Read at `SkyRenderer.cs:669`. Rendering: direct. **Verdict:** REMOVE. Delete `SkyRenderer.cs:669`; `MEITOU_FORCE_INTEGRATED` (another group) still shows the iGPU layer count.
- **`MEITOU_SKY_BENCH`** (env) — default off; with `--screenshot`, times 100 sky passes and prints the CPU record time. *Kind:* one-off diagnostic (written for the GL port; the code notes the native API cannot wait). Used by: `WorldApp.cs:238`, docs (`render-sky.md:31`, `renderer-native.md:2738`). Rendering: indirect. **Verdict:** REMOVE. Delete the block at `WorldApp.cs:238-244` and `SkyRenderer.Benchmark` (only caller); the frame profiler and `--bench-frames` report the sky pass.

### src/Meitou.Rendering/SpikeLog.cs

- **`MEITOU_LOG_SPIKES`** (env) — default off; same as `--log-spikes`. *Kind:* debug/diagnostic, duplicate of a flag. Used by: `docs/viewer.md:188`. Rendering: indirect. **Verdict:** REMOVE the env read (`Enabled` defaults false, `--log-spikes` sets it).
- **`MEITOU_SPIKE_FACTOR`** (env) — default 1.5 (a spike is over this many medians). *Kind:* tuning knob on a diagnostic. Used by: nothing (docs list `--log-spikes` as 1.5 x). Rendering: none. **Verdict:** REMOVE. `Factor` becomes a constant.
- **`MEITOU_BENCH_SKIP`** (env) — default 1 for `--fly-benchmark` (first frames left out of the percentiles); also the fallback of the spike log's skip. *Kind:* bench-harness entry point. Used by: `WorldApp.Benchmark.cs:107`, `SpikeLog.cs:19`, very widely in docs (`engine.md`, `impostors.md`, `render-foliage.md`, `renderer-native.md`, `viewer.md`, `formats/particle-universe.md`). Rendering: none (measurement). **Verdict:** KEEP. If any env var should become a flag it is this one (`--bench-skip`), but it is the only warm-up control the fly benchmark has.
- **`MEITOU_SPIKE_SKIP`** (env) — default falls back to `MEITOU_BENCH_SKIP`, else 0. *Kind:* tuning knob; duplicates `MEITOU_BENCH_SKIP`. Used by: nothing but the fallback chain (`SpikeLog.cs:19`) and the `viewer.md:188` sentence. Rendering: none. **Verdict:** REMOVE. Read only `MEITOU_BENCH_SKIP`.
- **`MEITOU_SPIKE_MIN_MS`** (env) — default 2 ms (spike floor). *Kind:* tuning knob on a diagnostic. Used by: nothing. Rendering: none. **Verdict:** REMOVE. `MinMs` becomes a constant.

### src/Meitou.Rendering/TerrainLateColour.cs, TerrainLayerSkip.cs

- **`MEITOU_TERRAIN_LATE`** (env) — default on; `=0` starts with the terrain colour drawn before the occluders. *Kind:* kill-switch for an always-on feature (starting value of `--ab terrain-late-colour`). Used by: `docs/render-terrain.md:158,172`, `docs/bench.md:72`. Read at `TerrainLateColour.cs:11`, consumed at `WorldFrame.cs:1410`. Rendering: direct. **Verdict:** REMOVE the env read. When the A/B is retired too, hardcode `defer: true`, delete `TerrainLateColour` and the non-deferred terrain path.
- **`MEITOU_TERRAIN_LAYER_SKIP`** (env) — default on; `=0` samples the cliff layer at every nonzero weight. *Kind:* kill-switch for an always-on feature (`--ab terrain-layer-skip`). Used by: `docs/render-terrain.md:149,172`, `docs/bench.md:72`, `TerrainLayerSkipTests`. Rendering: direct. **Verdict:** REMOVE the env read; same follow-up as above (when the A/B goes, drop the `uCliffEps = 0` path).
- **`MEITOU_TERRAIN_CLIFF_EPS`** (env) — default 0.03. *Kind:* tuning knob, decided. Used by: `docs/render-terrain.md:149`; `TerrainLayerSkipTests.cs:20` asserts it is in 0.01..0.06. Rendering: direct. **Verdict:** REMOVE. `CliffEps` becomes `const 0.03f` (adjust the test to the constant).

### src/Meitou.Rendering/TerrainRenderer.cs, TerrainShaders.Probe.cs

- **`MEITOU_TERRAIN_LOD_LOG`** (env) — default off; prints terrain LOD ranges and drawn nodes per level. *Kind:* debug print. Used by: `docs/viewer.md:147` (adjacent text, not this variable). Read at `TerrainRenderer.cs:85`, used at `263,268,813`. Rendering: indirect. **Verdict:** REMOVE. Delete `LodLog` and its three prints. (Foliage has its own `MEITOU_LOD_LOG` in another group; remove them together.)
- **`MEITOU_MESH_TIMING`** (env) — default 0; `1` prints the CPU time of `DrawMeshes`, `2` adds a warm repeat. *Kind:* diagnostic from the native-port phase. Used by: `docs/renderer-native.md:1388-1428,2045,2157,2292` (historical measurements). Rendering: indirect. **Verdict:** REMOVE. Delete `MeshTiming`, `ReportMeshTiming` and the stamp code at `TerrainRenderer.cs:745-780,1104-1185`; the bench `stages` line and frame profiler replace it.
- **`MEITOU_TERRAIN_TIMING`** (env) — default off; `1` prints per-call CPU time for terrain patches, meshes, blocker map and sweep. *Kind:* diagnostic from the native-port phase. Used by: `docs/renderer-native.md:1627,2045`. `StepTiming` has 38 call sites. Rendering: indirect. **Verdict:** REMOVE. Delete the `StepTiming` class and its `Now()/Add()` calls. Same family as `MEITOU_FOLIAGE_TIMING` and `MEITOU_PASS_STATS` (other groups).
- **`MEITOU_TERRAIN_PROBE`** (env) — default unset; `name[,name...]` builds a second terrain colour program with named parts cut. *Kind:* one-off cost probe (finished: "Where the terrain's 0.99 ms goes"). Used by: `docs/render-terrain.md:115`, `docs/bench.md:70`, `--ab terrain-probe`. Read at `TerrainShaders.Probe.cs:12`; consumed at `TerrainRenderer.cs:109,278-315`. Rendering: direct (changes the picture). **Verdict:** REMOVE. Delete `TerrainShaders.Probe.cs` (116 lines), `patchProbe` and the `TerrainProbe.On/NoDraw` branches in `TerrainRenderer.Draw`, and the `terrain-probe` A/B registration (`WorldApp.Bench.cs:109`).
- **`MEITOU_TERRAIN_PROBE_ON`** (env) — default off; starts the probe program on for screenshots. *Kind:* same probe. Used by: `TerrainShaders.Probe.cs` only. Rendering: direct. **Verdict:** REMOVE with the probe.

### src/Meitou.Rendering/Upscalers/

- **`MEITOU_DLSS_PRESET`** (env) — default 0 (Streamline's default preset); `1..15` = A..O for every mode. *Kind:* tuning knob for a user-facing upscaler. Used by: `docs/engine.md:235-236` (a CNN preset, E, is the only case where the water hint matters). Read at `DlssUpscaler.cs:80,118`. Rendering: direct. **Verdict:** KEEP. It is one line and the only advanced DLSS control; document it in the README next to `MEITOU_STREAMLINE_PATH`.
- **`MEITOU_FFX_PATH`** (env) — default unset (then next to the exe); location of `amd_fidelityfx_vk.dll`. *Kind:* user-facing setup. Used by: `README.md:84`, `DECISIONS.md:74`, `docs/engine.md:221`. Rendering: direct (enables FSR). **Verdict:** KEEP.
- **`MEITOU_FFX_DEBUG`** (env) — default off; turns on FSR's input checks and message callback. *Kind:* debug. Used by: `docs/engine.md:225`. Read at `FsrUpscaler.cs:70`, used at `116,119`. Rendering: indirect. **Verdict:** MERGE → `MEITOU_STREAMLINE_LOG`, renamed `MEITOU_UPSCALER_LOG=1` ("vendor-library diagnostics for whichever upscaler runs"), so one variable serves both.
- **`MEITOU_FFX_DEBUGVIEW`** (env) — default off; FSR's own debug overlay (rows appear upside down in our picture). *Kind:* debug view. Used by: `docs/engine.md:226`. Read at `FsrUpscaler.cs:197`. Rendering: direct. **Verdict:** REMOVE. Delete the flag expression (`Flags = 0`) and the doc sentence.
- **`MEITOU_STREAMLINE_PATH`** (env) — default unset (then next to the exe); folder with the Streamline/DLSS DLLs. *Kind:* user-facing setup. Used by: `README.md:84`, `DECISIONS.md:86`, `docs/engine.md:231`, nearly every bench recipe in `docs/`, and the owner's DLSS launch setup. Rendering: direct (enables DLSS). **Verdict:** KEEP.
- **`MEITOU_STREAMLINE_LOG`** (env) — default off; shows Streamline's info-level log lines (warnings and errors always print). *Kind:* diagnostic. Used by: `docs/engine.md:236`. Read at `Streamline.cs:115`. Rendering: indirect. **Verdict:** KEEP (renamed `MEITOU_UPSCALER_LOG` if `MEITOU_FFX_DEBUG` is merged in). It is the only way to see why DLSS failed to start.

### src/Meitou.Rendering/WaterRenderer.cs

- **`MEITOU_WATER_DEBUG`** (env) — default 0; `1` shows the shore fields (distance, breaker phase, exposure), `2` the river weight and flow direction. *Kind:* debug view. Used by: docs only (`render-water.md:66,141,178`). Read at `WaterRenderer.cs:1197`, uniform `uWaterDebug` and two branches in the fragment shader (`824-825`), set at `1167`. Rendering: direct. **Verdict:** REMOVE. Delete `DebugView`, the `uWaterDebug` uniform and both shader branches.

**Group totals:** 78 items: keep 9, merge 9, remove 60.


Worktree `R:\VlcekM\MeitouClient-cleanup` (branch `cleanup`). Read-only audit. 2 env vars + 89 flags.

Context that applies to many entries:

- **The Tab panel is not persisted** (no settings file anywhere in `tools/Meitou.ModelViewer` or `SettingsPanel`), so a CLI flag is today the only way to launch with a non-default quality value. Most flags below are "start value of a Tab slider". I keep the ones that are real launch-time quality choices and drop the fine-tuning ones (Tab slider stays; `--low-end` sets the field directly).
- **`--low-end` is a string expansion** (`WorldOptions.LowEnd`, WorldFrame.cs:205-219) into ~20 other flags. Removing any of them means rewriting that array (or turning it into a method that assigns `WorldOptions` fields). `BenchHarnessTests.cs:27-33` asserts the expansion (`ObjectLod`, `GrassDensity`, `WaterReflection`, `Anisotropy`, `TextureQuality`, `MaterialDistance`, `ShadowsOff`), so that test changes too.
- **`--ab <name>` already covers many "turn it off to compare" flags** (`occlusion`, `fog-cull`, `fog-volumes`, `fog-vrs`, `lights`, `reflections`, `shadow-pass`, `foliage-draw`, `objects-draw`, `water-draw`, `anisotropy`, `weather-particles`, `material-distance`, `gi-debug`, `refl-*`, `water-lean`; registered in `tools/Meitou.ModelViewer/WorldApp.Bench.cs:26-121`). The `--ab` stills are paired, so the standalone `--no-X` flag adds nothing for those.
- **Stale `--help` text** in `WorldFrame.Usage` (WorldFrame.cs:222-335) disagrees with the code in places: `--water-reflection` (text: default 2, code: 4), `--reflection-range` (text 0.6, code 3), `--texture-quality` (text 1, code 0). `docs/render-water.md:273` says the default water grid is 128 but `WaterRenderer.DefaultGridSegments` is 256 (WaterRenderer.cs:851) and the Usage text says 256.
- "Parse" below means the `case` line in `WorldOptions.Parse` (WorldFrame.cs:342-486, `--material-distance` at :511) plus the Usage line and, where it exists, the `LowEnd` entry. "Doc lines" means the places in `docs/*.md` that name the flag. Most of these are historical measurement recipes (renderer-native.md, bench.md, impostors.md); delete the flag from the recipes only where a recipe is meant to stay runnable.

---

### src/Meitou.Rendering/WaterRenderer.cs

- **`MEITOU_RIVERS`** (env, :992) — default on (anything but `0`); *Kind:* kill-switch for a feature that is now always on (river flow-map bake, 2026-10-08); used by: `docs/render-water.md:141` only, no test, script or `--ab`; rendering: direct. **Verdict:** REMOVE — drop the `GetEnvironmentVariable("MEITOU_RIVERS") != "0" &&` clause from the `rivers` expression (the data checks after it stay) and the doc phrase "`MEITOU_RIVERS=0` leaves it out for comparisons". Hardcode on.
- **`MEITOU_WATER_LEAN`** (env, :1061) — default on (anything but `0`); *Kind:* A/B harness switch for an optimization that is decided (water-optimization was merged, 0c75a2d); used by: the `water-lean` `AbToggles` entry (`WorldApp.Bench.cs:29`, whose note names the env var), `docs/render-water.md:280`; rendering: direct. **Verdict:** REMOVE — the env var duplicates `--ab water-lean`; delete the `Environment.GetEnvironmentVariable` initialiser (make `Lean` a plain property defaulting to `true`) and the doc/note mentions. Follow-up once the owner is happy with the measurements: drop the `water-lean` toggle, the `Lean` property, the `uLean` uniform and the five `uLean < 0.5 ||` shader branches (WaterRenderer.cs:360, 528, 648, 662, 712, 754) and hardcode lean.

### src/Meitou.Rendering/WorldFrame.cs

**Draw distances and Meitou/Faithful ranges (all are also Tab sliders)**

- **`--landmark-distance`** (flag, first seen LowEnd :205) — default 150000 in Meitou, none in Faithful, `0` = off; *Kind:* tuning knob (Tab slider "Landmark distance"); used by: `--low-end` (5000), `LandmarkTests.cs:45-48`, docs (viewer.md, render-objects.md, renderer-native.md); rendering: direct (which huge objects are drawn). **Verdict:** MERGE → `--object-distance` — landmarks are "never less than the object distance" already; make the landmark range a constant (or a fixed multiple of the object distance) and keep `--faithful reach` for "none". Delete `LandmarkDistance` + `LandmarkDistanceFor`'s override, the parse/Usage/LowEnd entries, and rewrite the `LandmarkTests` parse cases.
- **`--object-distance`** (flag, :205) — default 20000 Meitou / 12000 Faithful; *Kind:* user-facing setting (the `reach` switch's value, Tab slider); used by: `--low-end`, `BenchHarnessTests.cs:27`, `LandmarkTests.cs:37-38`, 8 doc lines; rendering: direct. **Verdict:** KEEP — the headline draw-distance setting.
- **`--object-lod`** (flag, :205) — default 1 (0.25 to 4); *Kind:* tuning knob (Tab "Object LOD distance x"); used by: `--low-end` (0.25), `BenchHarnessTests.cs:33`, no docs; rendering: direct. **Verdict:** REMOVE — Tab slider stays; `--low-end` sets `ObjectLod` directly. Delete parse (`case "--object-lod"`), Usage line and the `LowEnd` pair; `WorldOptions.ObjectLod` is still read at :959 area for the slider start value, so keep the field.
- **`--range-large`** (flag, :206) — default 50000; *Kind:* user-facing quality (foliage draw range; the `range` switch, F6, Tab slider); used by: `--low-end` (5050), docs (19 lines: bench recipes in impostors.md, renderer-native.md, render-distance-benchmark.md, render-foliage.md), no test/script; rendering: direct. **Verdict:** KEEP — the one foliage-range option to keep (see next two).
- **`--range-medium`** (flag, :206) — default 12000; *Kind:* tuning knob (Tab slider); used by: `--low-end` (2050), docs (11 lines, the same recipes as `--range-large`); rendering: direct. **Verdict:** MERGE → `--range-large` — medium/small follow it at the default ratios (12000/50000 and 3500/50000). Delete `MediumRange` override, parse, Usage, LowEnd.
- **`--range-small`** (flag, :206) — default 3500; *Kind:* tuning knob (Tab slider); used by: `--low-end` (1050), 1 doc line; rendering: direct. **Verdict:** MERGE → `--range-large` (as `--range-medium`).
- **`--impostor-distance`** (flag, :207) — default 4000; *Kind:* tuning knob (Tab slider, the impostors switch F7); used by: `--low-end` (1150), 4 doc lines; rendering: direct. **Verdict:** REMOVE — Tab slider stays; `--low-end` sets the field. Delete `o.ImpostorDistance` plumbing at :970 (`o.ImpostorDistance ?? f.ImpostorDistance`), parse, Usage, LowEnd.
- **`--large-impostor-distance`** (flag, :207) — default 12000; *Kind:* tuning knob (Tab slider); used by: `--low-end` (1100), 2 doc lines; rendering: direct. **Verdict:** REMOVE (as `--impostor-distance`; the :971 `if (o.LargeImpostorDistance is …)` line goes).
- **`--grass-density`** (flag, :208) — default 1 (0.1 to 2); *Kind:* user-facing quality (grass blades x; Tab slider; a large perf lever); used by: `--low-end` (0.1), `BenchHarnessTests.cs:33`, no docs; rendering: direct. **Verdict:** KEEP — the cheap lever for weak GPUs, no other launch-time route.
- **`--reflection-range`** (flag, :209) — default 3 in code (Usage text says 0.6, the game's value); *Kind:* tuning knob (Tab slider "Reflection range x"); used by: `--low-end` (0.1, which is a no-op because `--water-reflection 0` is given in the same preset), docs (settings.md, 7 lines); rendering: direct. **Verdict:** REMOVE — keep the fixed default and the Tab slider; delete `o.ReflectionRange` (feeds `ReflectionPass.Range` at :951), parse, Usage, the LowEnd pair, fix the stale "default 0.6".
- **`--water-reflection`** (flag, :209) — default 4 in code (Usage says 2); *Kind:* user-facing setting (the game's `water reflection` 0..4, Tab slider); used by: `--low-end` (0), `--ab refl-foliage/refl-objects` notes, ~35 doc lines (most are historical parity recipes `--water-reflection 4`); rendering: direct. **Verdict:** KEEP — the game's own setting; `--no-reflections` folds into `--water-reflection 0`.
- **`--shadow-filter`** (flag, :210) — default 2 (0 to 2); *Kind:* user-facing quality for low-end GPUs (Tab slider); used by: `--low-end` (0), `ShadowPass.Meitou.cs:26`, CHANGELOG:54, shadows.md; rendering: direct. **Verdict:** KEEP — it is the low-end shadow lever.
- **`--shadow-range`** (flag, :210) — default 10000 Meitou / 5000 Faithful; `0` = shadows start off; *Kind:* user-facing setting (the game's `Shadow Range`, Tab slider); used by: `--low-end` (0), `BenchHarnessTests.cs:31`, `Enhancements.cs:40`, `WorldApp.Benchmark.cs:21`, 7 doc lines; rendering: direct. **Verdict:** KEEP — also absorbs `--no-shadows` (`0`).
- **`--particle-density`** (flag, :212) — default 1 (0.1 to 1); *Kind:* user-facing quality (weather particle rate; Tab slider); used by: `--low-end` (0.1), `--ab weather-particles` doc, 1 doc line; rendering: indirect (particles are drawn by the renderer). **Verdict:** KEEP — the one particle quality option; the other group's `--particles-low` / `--particle-divisor` should fold into it.
- **`--anisotropy`** (flag, :213) — default 16; *Kind:* user-facing setting (texture filtering, Tab slider, `--ab anisotropy`); used by: `--low-end` (1), `BenchHarnessTests.cs:33`, render-post.md:40; rendering: direct. **Verdict:** KEEP.
- **`--material-distance`** (flag, :213; parsed at :511, outside my range) — default 30000 (as the game); *Kind:* user-facing quality (terrain full-material range; no Tab slider, `--ab material-distance`); used by: `--low-end` (15000), `BenchHarnessTests.cs:32`, 9 doc lines, `TerrainRenderer.cs:492`, `ReflectionPass.cs:306`; rendering: direct. **Verdict:** KEEP — the only lever for it.
- **`--texture-quality`** (flag, :213) — default 0 in code (Usage says 1); *Kind:* user-facing setting (the game's `texture resolution gimping` 0..4, needs a restart); used by: `--low-end` (3), `BenchHarnessTests.cs:33`, 14 doc lines (also used as a VRAM saver in bench recipes); rendering: direct. **Verdict:** KEEP — fix the Usage default.
- **`--low-end`** (flag, :218) — preset; *Kind:* user-facing setting (potato-PC preset); used by: `BenchHarnessTests.cs:27`, CHANGELOG:26, viewer.md:134; rendering: direct. **Verdict:** KEEP — rewrite the expansion without the flags this audit removes (and the other groups' ones).

**Where and how to load and take pictures**

- **`--size`** (flag, :351) — default 1280x960 (1280x720 for bench runs); *Kind:* user-facing/bench harness (offscreen picture size); used by: `parity.sh`, `BenchHarnessTests.cs:60`, 71 doc lines; rendering: direct. **Verdict:** KEEP.
- **`--at`** (flag, :366) — default the world's centre; *Kind:* user-facing/bench harness (world point); used by: `parity.sh`, the viewer's own camera printout (`WorldApp.cs:408`), `GameOptions.cs:53`, 42 doc lines; rendering: indirect. **Verdict:** KEEP (`meitou-tools weather --at` is a separate parser).
- **`--zone`** (flag, :367) — default none; *Kind:* user-facing/bench harness (centre of zone i,j); used by: `parity.sh:13`, `GameOptions.cs:53`, docs; rendering: indirect. **Verdict:** KEEP (parity view `zone14_30`; `meitou-tools navmesh --zone` is a separate parser).
- **`--step`** (flag, :370) — default auto (smallest power of two keeping <= 2048 cells per side); *Kind:* tuning knob (heightmap sample step); used by: `render-terrain.md:36`, `terrain.md:221`, `viewer.md:140`, no test/script; rendering: direct. **Verdict:** REMOVE — keep the auto step. Delete `WorldOptions.Step` and the `int step = o.Step ?? 1; if (o.Step is null) while …` at :654-655 (make it the loop only), parse, Usage, viewer.md mention.
- **`--screenshot`** (flag, :374) — default none (interactive window); *Kind:* user-facing/bench harness (offscreen PNG); used by: `parity.sh`, game and viewer, `BenchHarnessTests`, 55 doc lines; rendering: direct. **Verdict:** KEEP.
- **`--no-textures`** (flag, :380) — default textured; *Kind:* dead from the CLI (`Meitou.Game/Program.cs:45` sets the field itself for the sandbox; the flag is never used in docs except one list in viewer.md:143); used by: nothing else; rendering: direct. **Verdict:** REMOVE — delete the parse/Usage/viewer.md word; keep the `NoTextures` field (the game sandbox sets it).
- **`--no-objects`** (flag, :381) — default objects drawn; *Kind:* debug isolation (skips loading buildings; `O` key and `--ab objects-draw` cover the runtime case); used by: `WorldApp.Bench.cs:77` note, game sandbox sets the field, 4 doc lines; rendering: indirect. **Verdict:** KEEP — it skips the (long) object load, which `--ab` cannot. If the isolation flags are ever consolidated, fold `--no-objects/--no-foliage/--no-water/--no-particles` into one `--hide <list>`.
- **`--no-foliage`** (flag, :382) — default foliage drawn; *Kind:* debug isolation (skips foliage; `F` key, `--ab foliage-draw`); used by: game sandbox sets the field, `GameOptions.cs:53`, 6 doc lines (render-foliage.md measurement recipes); rendering: indirect. **Verdict:** KEEP (same family as `--no-objects`).
- **`--no-load-cache`** (flag, :383) — default caches on; *Kind:* debug diagnostic (bypass the foliage layout cache); same effect as env `MEITOU_NO_LOAD_CACHE=1` (`FoliageLayoutCache.cs:14`); used by: foliage.md:443; rendering: none. **Verdict:** KEEP the flag — and drop the env var `MEITOU_NO_LOAD_CACHE` (its row is in another group): keep one route, the flag, which the docs name first.
- **`--impostor-budget`** (flag, :387) — default 8% of the card's budget (5% on iGPU), 48-1024 MB; *Kind:* user-facing setting (VRAM budget for impostor atlases); duplicates env `MEITOU_IMPOSTOR_BUDGET_MB` (`ImpostorBudget.cs:48`); used by: impostors.md (8 lines), FoliageRenderer.Impostors.cs; rendering: indirect. **Verdict:** KEEP the flag, drop the env var (other group).
- **`--impostor-cache-mb`** (flag, :388) — default 4096 (0 = no cap); *Kind:* tuning knob (disk cache cap); duplicates env `MEITOU_IMPOSTOR_CACHE_MB` (`ImpostorCache.cs:45`); used by: impostors.md:160, render-foliage.md:98; rendering: none. **Verdict:** REMOVE — a constant cap is enough; delete `ImpostorCacheMb` plumbing at :972, parse, Usage. Drop the env var as well (other group).
- **`--distant-range`** (flag, :393) — default 10 zones (the game's maximum); *Kind:* user-facing setting (the game's distant town range; Tab slider "Distant towns"); used by: render-objects.md, settings.md, render-distance-benchmark.md:31 (rows use it), no test/script; rendering: direct. **Verdict:** KEEP — also absorbs `--no-distant` (`0`).
- **`--no-distant`** (flag, :394) — default off; *Kind:* debug isolation; used by: game sandbox sets the field, 3 doc lines; rendering: direct. **Verdict:** MERGE → `--distant-range` (`--distant-range 0`). Delete the `NoDistant` read at :959 (`DistantRange = NoDistant ? 0 : …`), parse, Usage; the game sandbox can set `DistantZones = 0`.
- **`--layer-size`** (flag, :395) — default 2048 (power of two up to 2048); *Kind:* tuning knob; `--texture-quality` already lowers it (`min(layerSize, 2048 >> drop)` at :869); used by: `TerrainTextures.cs:293` error message ("try a smaller --layer-size"), 4 doc lines; rendering: direct. **Verdict:** REMOVE — hardcode 2048 and let texture quality shrink it; change the TerrainTextures message to name `--texture-quality`; delete `LayerSize`, parse, Usage.
- **`--debug`** (flag, :396) — default 0; *Kind:* debug view (terrain blend-map weights / layer weights; the `V` key cycles the same `render.Debug`, `WorldApp.cs:395`); used by: nothing else (impostor app has its own `--debug`); rendering: direct. **Verdict:** MERGE → one `--debug-view <name>` option together with `--wireframe`, `--debug-shadows` and `--gi-debug` (the offscreen screenshot path has no keys, which is why the start-state flags exist). If the merge is not wanted: REMOVE, `V` stays.
- **`--wireframe`** (flag, :397) — default off; *Kind:* debug view (`X` key toggles it at runtime); used by: renderer-native.md:2606/2647/2658 (parity recipes); rendering: direct. **Verdict:** MERGE → `--debug-view` (see `--debug`).
- **`--info`** (flag, :398) — default off; *Kind:* debug (load the world, print its stats, exit before rendering); used by: `WorldApp.cs:54` and `Meitou.Game/Program.cs:89`; the docs hits are for the removed mesh viewer; rendering: none. **Verdict:** REMOVE — delete the `Info` field, the two `if (options.Info) return 0;` lines and the parse/Usage entries.

**Time and water**

- **`--time-speed`** (flag, :400) — default 0 (stopped), game hours per real minute; *Kind:* user-facing setting (Tab slider "Time speed") and bench (`WorldApp.Benchmark.cs:72` advances the hour with it); used by: `WorldApp.cs:300/527`, 6 doc lines; rendering: indirect. **Verdict:** KEEP.
- **`--no-water`** (flag, :401) — default water drawn; *Kind:* debug isolation (skips creating the water renderer; `G` key, `--ab water-draw`); used by: game sandbox sets the field, impostors.md:752 recipe; rendering: direct. **Verdict:** KEEP (isolation family, see `--no-objects`).
- **`--water-seconds`** (flag, :402) — default 0; *Kind:* debug (start the Meitou water clock at s game seconds for pictures of a moment); used by: `gpu.WaterClockHours` at :950, a comment in `MeitouWaterTests.cs:77` (the test sets the clock itself), 2 doc lines; rendering: direct. **Verdict:** REMOVE — delete `WaterSeconds`, the :950 assignment, parse, Usage and render-water.md:178 phrase. The stills are already deterministic at clock 0.
- **`--water-grid`** (flag, :403) — default 256 in code (docs claim 128); *Kind:* user-facing quality (water mesh density; creation-time, no slider); used by: render-water.md (6 lines), the `water-lean` doc note; rendering: direct. **Verdict:** KEEP — an iGPU lever; reconcile the default with render-water.md:273, and consider adding `--water-grid 128` to `--low-end` (though `--low-end` currently forces Faithful water, which has no grid).
- **`--no-water-refraction`** (flag, :404) — default refraction on; *Kind:* tuning knob (saves a scene copy per depth slice for integrated GPUs); used by: `WaterRenderer.cs:1057` comment, 2 doc lines, no `--ab`; rendering: direct. **Verdict:** REMOVE — the water-optimization change already skips the copy for far slices; delete `WaterRefraction` (:38), the :948 assignment, parse, Usage. Keep `WaterRenderer.Refraction` only if `--faithful water` needs it.
- **`--water-ocean`** (flag, :405) — default 256 (64/128/256 FFT size per cascade); *Kind:* user-facing quality (iGPU: 128 is a quarter of the work); used by: 1 doc line (`render-water.md`), no test; rendering: direct. **Verdict:** KEEP — pair it with `--water-grid` as the two Meitou-water quality levers.
- **`--no-reflections`** (flag, :406) — default reflections on; *Kind:* debug isolation, the same as `--water-reflection 0` (said in the Usage) and the `R` key / `--ab reflections`; used by: impostors.md:752 recipe, `WorldApp.Bench.cs:113` note; rendering: direct. **Verdict:** MERGE → `--water-reflection` (`0`). Delete the `NoReflections` field (the game sandbox sets it; give it `WaterReflection = 0`), parse, Usage.
- **`--reflection-samples`** (flag, :407) — default 4 (`ReflectionPass.DefaultSamples`, 1 to 8); *Kind:* tuning knob (reflection MSAA, `--ab refl-msaa` measured it); used by: `ReflectionPass.cs:30`, `WorldApp.Bench.cs:119`, 3 doc lines; rendering: direct. **Verdict:** REMOVE — the default won; delete `ReflectionSamples`, the :951 initialiser entry, parse, Usage (`--ab refl-msaa` compares against `rp.AbSamples`, unaffected).

**Shadows**

- **`--no-shadows`** (flag, :411) — default shadows on; *Kind:* debug/low-end switch (no `ShadowPass` at all, so zero-filled shadow blocks, `SkyRenderer.cs:1334`); used by: `--ab shadow-pass` note, `SkyWaterNativeTests.cs:90` comment, 11 doc lines; rendering: direct. **Verdict:** MERGE → `--shadow-range 0` (shadows made but off; the Tab slider can switch them on). Delete `NoShadows`, the `if (!o.NoShadows)` guard (:976), parse, Usage; if nothing else builds a world without a `ShadowPass`, the zero-block fallback in `SkyRenderer.cs:1334` goes too.
- **`--shadow-quality`** (flag, :412) — default 1 (map side 1024/2048/4096); *Kind:* user-facing setting (the game's `shadow quality`; creation-time, no slider); used by: settings.md:348, render-shadows.md:9; rendering: direct. **Verdict:** KEEP.
- **`--debug-shadows`** (flag, :421) — default 0 (1 cascade maps, 2 cascade term, 3 term over the picture); *Kind:* debug view (the parity gates use it in docs, 27 doc lines, mostly "0 px" recipes); used by: `gpu.DebugShadows` at :985, no test/script; rendering: direct. **Verdict:** MERGE → `--debug-view` (see `--debug`).

**Global illumination**

- **`--gi-debug`** (flag, :422) — default 0 (views 1 hit distance, 2 one-bounce on grey, 3 traced scene normals); *Kind:* debug view of the ray-traced scene, also forces ray queries (`WantsRayTracing`); used by: `--ab gi-debug` (`WorldApp.Bench.cs:94`), `VulkanDisplay.RayTracing` comment, render-gi.md; rendering: direct. **Verdict:** MERGE → `--debug-view` (see `--debug`). Views 1 and 2 belong to the shelved per-pixel RT path and could go with `GiDebugPass`; view 3 is the useful one.
- **`--gi-samples`** (flag, :423) — default 4; *Kind:* debug (bounce rays in `--gi-debug 2` only); used by: render-gi.md:101; rendering: direct. **Verdict:** REMOVE — delete `GiSamples`, the `GiDebugPass { Samples = … }` initialiser, parse, Usage.
- **`--gi-range`** (flag, :424) — default 6000; *Kind:* tuning knob (objects traced within this distance); used by: render-gi.md (3 lines of cost tables), no test; rendering: direct. **Verdict:** REMOVE — hardcode 6000 (`GiScene.Range`), delete `GiRange` and the debug print's use.
- **`--gi`** (flag, :425) — default on; *Kind:* dead (sets `Gi = true`, which is already the default; only matters after `--no-gi`; docs say "`--gi` still parses"); used by: render-gi.md and parity recipes only; rendering: none. **Verdict:** REMOVE — delete the parse/Usage and fix the doc recipes.
- **`--no-gi`** (flag, :426) — default GI on where the GPU has ray queries; *Kind:* user-facing setting (the device is then made without ray queries); used by: `Enhancements.cs:91` text, render-gi.md, `VulkanDeviceOptions.cs:67`; rendering: direct. **Verdict:** KEEP — different from `--faithful gi`, which still builds the traced scene.
- **`--gi-phases`** (flag, :427) — default 4 (1, 2, 4, 8); *Kind:* tuning knob (probe update shares per frame); used by: render-gi.md (4 lines, a cost table); rendering: direct. **Verdict:** REMOVE — hardcode 4 (`GiProbes.Phases`); delete `GiPhases` and the :1000 initialiser entry.
- **`--gi-foliage`** (flag, :428) — default 150 (0 = no foliage in the traced scene); *Kind:* tuning knob; used by: render-gi.md (3 lines); rendering: direct. **Verdict:** REMOVE — hardcode 150 (`GiScene.FoliageMinSize`).
- **`--gi-strength`** (flag, :429) — default 1; *Kind:* tuning knob (how much the probes replace sky ambient); used by: render-gi.md:153 only; rendering: direct. **Verdict:** REMOVE — hardcode 1 (`GiProbes.Strength`); `--faithful gi` is the way back to the game's ambient.
- **`--gi-resolve`** (flag, :430) — default 2 (probes read once per pixel after the scene at 1/n size; 0 = per fragment); *Kind:* A/B-decided tuning knob (`--ab gi-resolve` measured it); used by: render-gi.md:530/554, `gpu.GiResolve` at :1001; rendering: direct. **Verdict:** REMOVE — hardcode 2; keep `gpu.GiResolve` and the `gi-resolve` `--ab` toggle if wanted.
- **`--lights`** (flag, :431) — default on (`--lights off`); *Kind:* kill-switch used only for parity/compare (render-lights.md:7 says "the parity gates use it"; `--ab lights` covers it); used by: render-lights.md, render-post.md:30, render-shafts.md:75/80 recipes, `WorldLamps.cs:34`; rendering: direct. **Verdict:** REMOVE — delete `Lights` (:940 `{ Enabled = o.Lights }`), parse, Usage; recipes use `--ab lights` instead.

**Sky, haze and weather**

- **`--simple-sky`** (flag, :432) — default off; *Kind:* kill-switch for a superseded path (the old colour-model sky, squared-distance fog and display-referred post, `B` key toggles it); used by: `gpu.Sky.Physical` (SkyRenderer, WorldApp keys, WorldFrame.cs:1286, WorldStats), 11 doc lines; rendering: direct. **Verdict:** REMOVE — delete the `SimpleSky` field and parse, the `B` key and its Tab label (`WorldApp.cs:352/398`), the `Physical == false` branches in SkyRenderer/post/WorldFrame:1286/WorldStats, and the doc passages. Large deletion; confirm the Faithful look does not use the simple sky (it does not: Faithful is the game's haze with the physical atmosphere).
- **`--haze`** (flag, :433) — default `kenshi` (`physical` = a viewer alternative, "not the game's"); *Kind:* experiment that was decided; used by: sky.md:240-275, render-post.md:48, `SkyRenderer.KenshiHaze` (:458/915/917); rendering: direct. **Verdict:** REMOVE — hardcode the Kenshi haze; delete `PhysicalHaze`, the parse, `SkyRenderer.KenshiHaze` and the `physical` branch of `atmoApply`, and the sky.md `physical` section.
- **`--haze-distance`** (flag, :434) — default `KenshiHaze.FarDistance(ViewDistanceSetting)` = 50000 (view distance 5000); *Kind:* tuning knob; used by: sky.md:246, render-post.md:48; rendering: direct. **Verdict:** REMOVE — hardcode; delete `HazeDistance` and the :888 override.
- **`--haze-strength`** (flag, :435) — default 0.93 (Meitou; 1 = the game's); *Kind:* the Meitou/Faithful `haze` switch's value (Tab slider "Haze strength"); used by: `Enhancements` via `Switches(o)` (:336), sky.md:268, viewer.md; rendering: direct. **Verdict:** REMOVE — `--faithful haze` gives 1; the slider stays; `o.HazeStrength` remains as the field the switch reads.
- **`--planetshine-strength`** (flag, :436) — default 60 (x the physical value); *Kind:* tuning knob (Tab slider; the `planetshine` switch); used by: sky.md:356; rendering: direct. **Verdict:** REMOVE — `--faithful planetshine` is the off route; delete the parse/Usage and keep the field for the slider.
- **`--wetness`** (flag, :440) — default the weather's value; *Kind:* test override (docs: "testing"); used by: `WeatherSurfaces.WetnessOverride` (:910), weather.md:541, viewer.md:145; rendering: direct. **Verdict:** REMOVE — `--weather <record>` gives real weather states. Delete `Wetness`, `WeatherSurfaces.WetnessOverride`, parse, Usage.
- **`--rain`** (flag, :441) — default the weather's value (0..100); *Kind:* test override; used by: `WeatherSurfaces.RainOverride`, weather.md:541; rendering: direct. **Verdict:** REMOVE (as `--wetness`).
- **`--dust`** (flag, :442) — default the weather's value; takes `x[,inside,slope]`; *Kind:* test override; used by: `WeatherSurfaces.DustOverride`, weather.md:541; rendering: direct. **Verdict:** REMOVE (as `--wetness`; also drops the `Vector3` parse block).
- **`--day`** (flag, :450) — default 0 in the viewer (the game starts on its clock's day); *Kind:* user-facing setting (weather schedule start day); used by: `Meitou.Game/GameHost.cs:62` (`o.Day ??= …`), `WorldWeather` (3 sites), weather.md; rendering: indirect. **Verdict:** KEEP.
- **`--weather-seed`** (flag, :451) — default 1 (as `meitou-tools weather`); *Kind:* tuning/test knob; used by: `WeatherWorld.Create(…, o.WeatherSeed, …)` at :900, weather.md:640; rendering: indirect. **Verdict:** REMOVE — hardcode 1 (the doc cross-check against `meitou-tools weather --seed` keeps that tool's own option); delete `WeatherSeed`, parse, Usage.
- **`--no-particles`** (flag, :452) — default particles on; *Kind:* debug isolation (skips the pass and its load; used heavily in measurement recipes: 29 doc lines incl. bench.md:47 "for a pure picture comparison"); used by: `--ab weather-particles` doc; rendering: indirect. **Verdict:** KEEP.
- **`--no-fog-volumes`** (flag, :453) — default volumes on; *Kind:* "comparison" kill-switch, covered by `--ab fog-volumes` (`WorldApp.Bench.cs:35`); used by: `FogVolumes.cs:48` comment, `WorldApp.cs:220`, 10 doc lines; rendering: direct. **Verdict:** REMOVE — delete `NoFogVolumes`, the `Enabled = !o.NoFogVolumes` initialiser (:885, `Enabled` defaults true), parse, Usage.
- **`--no-fog-cull`** (flag, :454) — default cull on; *Kind:* "comparison" kill-switch, covered by `--ab fog-cull`; used by: `FogVolumes.cs:50`, `WorldApp.cs:221`, 6 doc lines; rendering: direct. **Verdict:** REMOVE (as `--no-fog-volumes`; `CullEnabled = !o.NoFogCull` at :885).
- **`--no-occlusion-cull`** (flag, :455) — default cull on; *Kind:* "comparison" kill-switch, covered by `--ab occlusion` (`WorldApp.Bench.cs:31`); used by: `PostProcess.cs:51` comment, `WorldApp.cs:224`, 5 doc lines; rendering: direct. **Verdict:** REMOVE — delete `NoOcclusionCull` and the :886 assignment.
- **`--fog-vrs`** (flag, :456) — default already on; *Kind:* dead (sets `FogVrs = true`, the default; only matters after `--no-fog-vrs`); used by: 2 doc lines; rendering: indirect. **Verdict:** REMOVE.
- **`--no-fog-vrs`** (flag, :457) — default VRS on where the device supports it; *Kind:* kill-switch for a feature that is now always on, covered by `--ab fog-vrs` (`WorldApp.Bench.cs:38`); used by: bench.md:71 recipe, render-post.md; rendering: indirect. **Verdict:** REMOVE — delete `FogVrs` (:887 `gpu.Post.FogVrs = o.FogVrs;` goes; the property defaults on), parse, Usage.
- **`--particle-prewarm`** (flag, :458) — default the longest particle life; *Kind:* bench knob (`--particle-prewarm 0` removes the first-picture particle stall in the stutter recipes, renderer-native.md:3525/3564, viewer.md:145/192); used by: `particles.PrewarmSeconds` at :918, particle-universe.md:268; rendering: none. **Verdict:** KEEP — two live measurement recipes depend on it.
- **`--particle-dwell`** (flag, :459) — default 2 s; *Kind:* tuning knob (hysteresis before rebuilding particles for a new region); used by: `particles.RegionDwell` (:919), particle-universe.md:331; rendering: none. **Verdict:** REMOVE — hardcode 2 (the field default of `RegionDwell`); delete `ParticleDwell`, :919, parse, Usage.
- **`--particle-area`** (flag, :462) — default the weather region; *Kind:* test override (disc of particles round the start point); used by: `WorldFrame.cs:924-927`, `EffectGroups.cs:15` comment, no docs; rendering: none. **Verdict:** REMOVE — delete `ParticleArea`, the `DiscArea` branches at :924/927 (keep `RegionArea`); `DiscArea` stays only if the "no region known" fallback uses it.
- **`--particle-seed`** (flag, :463) — default 1; *Kind:* test knob; used by: `particles.Seed` (:920), no docs; rendering: none. **Verdict:** REMOVE — hardcode 1.
- **`--particle-only`** (flag, :464) — default all effects; *Kind:* test filter (name substrings); used by: `gpu.ParticleOnly` (:925), no docs; rendering: indirect. **Verdict:** REMOVE — delete `ParticleOnly`, `Gpu.ParticleOnly` and its filter in the particle renderer.
- **`--clouds`** (flag, :465) — default the weather's density; *Kind:* test override (0..1) that is used to look at the new volumetric clouds (clouds.md "Seen" runs, CLEANUP.md:816 names it in a test list); used by: `SkyRenderer.CloudCoverage`, `VolumetricCloudNativeTests.cs:53` comment; rendering: direct. **Verdict:** KEEP for now (clouds work only merged 2026-10-11); revisit and REMOVE when the clouds settle, `--weather <record>` covers it.
- **`--cloud-wind`** (flag, :466) — default the weather's wind; *Kind:* test override (the drift is held still in `--screenshot` anyway); used by: `WorldWeather.CloudWindOverride` (:901), `SkyRenderer.CloudWind` (:881), clouds.md/sky.md; rendering: direct. **Verdict:** REMOVE — delete `CloudWind`, both consumers' overrides, parse, Usage.

**Camera, interaction, benchmark**

- **`--no-stream`** (flag, :467) — default streaming follows the camera; *Kind:* legacy comparison (the pre-streaming behaviour); used by: `gpu.Anchor = scene.Focus` (:943), render-terrain.md (3 lines of old measurements), viewer.md:148; rendering: indirect. **Verdict:** REMOVE — delete `NoStream`, `Gpu.Anchor` (:762) and the `gpu.Anchor ?? eye` fallbacks at :1254-1263 (use `eye`).
- **`--show-keys`** (flag, :468) — default overlay hidden (F10 toggles); *Kind:* user-facing/debug (start with the key list; the only way to render the overlay offscreen for parity runs); used by: `WorldApp.cs:226/297`, renderer-native.md (4 lines); rendering: indirect. **Verdict:** KEEP.
- **`--play-shots`** (flag, :469) — default off; *Kind:* user-facing (start flying the saved camera keys on a loop); used by: `WorldApp.cs:275`, `Cinema.cs:138`, viewer.md:282; rendering: none. **Verdict:** KEEP.
- **`--camera-at`** (flag, :474) — default the loaded point; *Kind:* streaming test (camera away from the loaded area); used by: render-terrain.md:15 (old measurement), viewer.md:147, `WorldFrame.cs:699/926`; rendering: indirect. **Verdict:** REMOVE — `--camera-code` positions the camera and loads round it. Delete `CameraX/CameraZ`, :699 and the `startX/startZ` fallback at :926 (use `scene.Focus`).
- **`--camera-code`** (flag, :475) — default none; *Kind:* user-facing/bench harness (the viewer's Ctrl+C code; the bench recipes in bench.md all use it); used by: `WorldCameraCode.cs`, `WorldApp.cs:101`, 26 doc lines; rendering: indirect. **Verdict:** KEEP.
- **`--fly-to`** (flag, :482) — default none; *Kind:* bench/streaming test (with `--screenshot`: fly there, report frame times, then take the picture); used by: `WorldApp.cs:138`, render-gi.md:341/519 and render-foliage.md:209 (recent GI/pop-in checks), render-terrain.md:18; rendering: indirect. **Verdict:** KEEP — the GI and foliage recipes rely on it; `--bench-motion fly` is the benchmark-side equivalent.
- **`--fly-benchmark`** (flag, :483) — default 0; *Kind:* bench harness entry point (frame-time percentiles over a flight); used by: `WorldApp.Benchmark.cs`, `WorldApp.cs:134`, `DECISIONS.md:25`, 96 doc lines; rendering: indirect. **Verdict:** KEEP.
- **`--renderer`** (flag, :484) — default none; *Kind:* dead (accepted and ignored; Vulkan is the only backend, `IgnoreRenderer` only prints a note); used by: `ImpostorApp.cs:71`, 5 doc lines; rendering: none. **Verdict:** REMOVE — delete the `case "--renderer"` here and in `ImpostorApp`, `WorldOptions.IgnoreRenderer`, the Usage line, the doc mentions.
- **`--fly-radius`** (flag, :485) — default 12000; *Kind:* bench harness parameter (also `--bench-motion fly`, `CameraMotion.cs:18/23`); used by: `WorldApp.Benchmark.cs:30`, 11 doc lines; rendering: indirect. **Verdict:** KEEP.
- **`--fly-speed`** (flag, :486) — default 150 units per frame; *Kind:* bench harness parameter (as `--fly-radius`); used by: `CameraMotion.cs`, `WorldApp.Benchmark.cs:62`, 61 doc lines; rendering: indirect. **Verdict:** KEEP.

---

**Group totals:** 91 items: keep 37, merge 10, remove 44.

(Keep: `--object-distance`, `--range-large`, `--grass-density`, `--water-reflection`, `--shadow-filter`, `--shadow-range`, `--particle-density`, `--anisotropy`, `--material-distance`, `--texture-quality`, `--low-end`, `--size`, `--at`, `--zone`, `--screenshot`, `--no-objects`, `--no-foliage`, `--no-load-cache`, `--impostor-budget`, `--distant-range`, `--time-speed`, `--no-water`, `--water-grid`, `--water-ocean`, `--shadow-quality`, `--no-gi`, `--day`, `--no-particles`, `--particle-prewarm`, `--clouds`, `--show-keys`, `--play-shots`, `--camera-code`, `--fly-to`, `--fly-benchmark`, `--fly-radius`, `--fly-speed`. Merge: `--landmark-distance`, `--range-medium`, `--range-small`, `--no-distant`, `--debug`, `--wireframe`, `--no-reflections`, `--no-shadows`, `--debug-shadows`, `--gi-debug`.)


Worktree `R:\VlcekM\MeitouClient-cleanup` (branch `cleanup`), read-only audit. Verdict words: KEEP / MERGE / REMOVE. "Used by" lists consumers outside the parse line; docs counts are files, not lines.

Notes that apply to several entries:
- `WorldOptions.Parse` serves both `meitou-viewer` and the game (`src/Meitou.Game/Program.cs`). The game honours `--fog` and the camera/terrain options; it never reads `--windowed` or `--monitor` (its window is built in `GameHost.Interactive.cs:34`, maximized, bordered), nor any bench/crowd/sway option except through `WorldFrame` (crowd, sway-start).
- Two bench harnesses exist side by side: the newer `--bench-frames` / `--ab` harness (`WorldApp.Bench.cs`) and the older `--fly-benchmark` (`WorldApp.Benchmark.cs`, not in this group). Several items below are the old harness's private knobs that the new one replaced (`docs/bench.md:32` says so).
- `--bench-motion` (WorldFrame.cs:493) and `--material-distance` (:511) sit inside my line range but were not on my list; I have not given them a verdict. `--material-distance` is redundant with `--ab terrain-material` / `--ab material-distance` and `MEITOU_AB_MATERIAL_DISTANCE` (see below).

### src/Meitou.Rendering/WorldFrame.cs

- **`--fly-pipelined`** (flag) — default off; *bench harness switch*; read only by `FlyBenchmark` (`WorldApp.Benchmark.cs:49`); docs: 8 files recommend it for GPU numbers (engine, impostors, render-distance-benchmark, render-foliage, renderer-native, shadows, viewer); rendering: indirect. The new harness is pipelined by default and has the inverse flag `--bench-serial`; the old one is paced by default and has this flag. **Verdict:** MERGE → `--bench-serial`: make `--fly-benchmark` pipelined by default and let `--bench-serial` give the paced, waiting run; delete `FlyPipelined` (field, parse, usage line) and the doc mentions become "default".
- **`--ab`** (flag) — default none; *bench harness entry*; used by `WorldApp.Bench.cs`, `WorldApp.cs`, `BenchHarnessTests`, bench.md and ~10 render docs; rendering: direct (it measures it). **Verdict:** KEEP — the A/B harness entry point. (The switch registry behind it has several entries that duplicate other options: `solid-first`, `material-distance`, `terrain-material`, `occlusion`; see the groups that own those.)
- **`--ab-period`** (flag) — default 1; *bench harness*; used by `WorldApp.Bench.cs:23`, test, 7 docs; rendering: indirect. **Verdict:** KEEP — needed for any switch with history (shadows need 64).
- **`--ab-drop`** (flag) — default `min(period/4, 16)`; *tuning knob*; read once (`WorldApp.Bench.cs:24`); no test, no doc passes it (bench.md:29 only describes the default). rendering: none. **Verdict:** REMOVE — hardcode the default; delete `AbDrop` field, parse line, usage text, the `o.AbDrop ??` term.
- **`--bench-frames`** (flag) — default 600 with `--ab`, else off; *bench harness entry*; used by `WorldApp.cs:63,132`, test, ~20 doc lines; rendering: indirect. **Verdict:** KEEP.
- **`--bench-out`** (flag) — default `%TEMP%\meitou-bench-<view>-<ab>-<time>.json`; *bench harness*; needed for `--bench-compare` (bench.md:15), test; rendering: none. **Verdict:** KEEP.
- **`--bench-turn`** (flag) — default 0.5 deg/frame; *tuning knob* of `--bench-motion turn`; read in `CameraMotion.For`; docs only state the default (bench.md:49); no measurement passes another value. rendering: none. **Verdict:** REMOVE — hardcode 0.5 in `CameraMotion.For`; delete `BenchTurn` field, parse, usage text.
- **`--bench-orbit`** (flag) — default 3 px/frame; *tuning knob* of `--bench-motion orbit`; bench.md:88-89 use `--bench-orbit 0.25` for the slow-orbit shadow-cap measurement. rendering: none. **Verdict:** KEEP — the one motion-rate knob that a recorded measurement used. It also replaces `MEITOU_BENCH_ORBIT` (below).
- **`--no-gpu-lock`** (flag) — default off (lock taken); *bench harness*; `WorldApp.Bench.cs:194`, test, bench.md:53. rendering: none. **Verdict:** KEEP.
- **`--bench-serial`** (flag) — default off; *bench harness*; `WorldApp.Bench.cs:160`, bench.md:40,59; rendering: indirect. **Verdict:** KEEP — and the target for `--fly-pipelined` (above).
- **`--bench-tris`** (flag) — default off; *bench/diagnostic*; also switches on three device features at start (`WorldApp.cs:83` `VulkanDisplay.TriangleMeasurements`); bench.md section "Triangles per pass", render-foliage/objects/terrain cite its numbers. rendering: direct. **Verdict:** KEEP.
- **`--bench-compare`** (flag) — *bench harness*; `WorldApp.cs:37`, `Program.cs:13`, test, bench.md; rendering: none. **Verdict:** KEEP.
- **`--log-spikes`** (flag) — default off; *debug/diagnostic*; sets `SpikeLog.Enabled`; same switch as env `MEITOU_LOG_SPIKES=1` (SpikeLog.cs:13, a duplicate; env belongs to another group); used by `FrameProfiler`, `FrameLoop`, `WorldApp.Benchmark.cs:55`; docs: 7 files (viewer.md:188, shadows, impostors). rendering: indirect. **Verdict:** KEEP — the spike tool is used in measurements. Drop the `MEITOU_LOG_SPIKES` env duplicate (keep the flag).
- **`--orbit-step`** (flag) — default 0; *test/bench*; `--screenshot` only: camera yaw += step per warm-up frame (`WorldApp.cs:171-177`); docs: engine.md:247, particle-universe.md:387, renderer-native.md:3470 (one check of DLSS motion vectors). rendering: indirect. **Verdict:** MERGE → `--bench-motion orbit` + `--bench-orbit`: let the screenshot path use `CameraMotion` too; delete `OrbitStep` and the `Step()`/`Clock()` special case.
- **`--monitor`** (flag) — default none; *user-facing (window)*; `WorldApp.WindowFor` → `CameraCode.OnMonitor`; docs viewer.md:184; not honoured by the game host. rendering: none. **Verdict:** KEEP (but wire it into `GameHost.Interactive.cs` or say in usage that it is viewer-only).
- **`--windowed`** (flag) — default off (borderless full screen, Alt+Enter toggles); *user-facing (window)*; `WorldApp.cs:263`; docs viewer.md:277. Not honoured by the game host. rendering: none. **Verdict:** KEEP (same remark as `--monitor`).
- **`--sway-step`** (flag) — default 0 (grass held still); *bench/test switch*; `WorldApp.cs:178` and `WorldApp.Bench.cs:167`; docs: bench.md (3 runs), engine, render-foliage, render-post. rendering: direct (grass motion, upscaler motion vectors). **Verdict:** KEEP — needed so the grass-velocity / post-merge measurements are not static.
- **`--sway-start`** (flag) — default 0; *tuning knob*; `WorldFrame.cs:965` sets `SwaySeconds` for offscreen runs; docs engine.md:215,249 (one check). rendering: direct but trivial. **Verdict:** REMOVE — hardcode 0 (`gpu.Foliage.SwaySeconds = 0` offscreen); delete `SwayStart` field, parse, usage text.
- **`--crowd`** (flag) — default 0; *test/bench harness for the character renderer*; `CrowdHarness.Create` (WorldFrame.cs:984); docs character-renderer.md, README, render-post, post-processing; rendering: direct (characters). **Verdict:** KEEP — the only way to get N characters in a headless view; the game itself spawns real ones.
- **`--crowd-seed`** (flag) — default 1; *tuning knob*; `CrowdHarness.cs:51,66`; one doc line. rendering: none. **Verdict:** REMOVE — hardcode 1; delete `CrowdSeed` field, parse, usage text.
- **`--crowd-time`** (flag) — default 0.35 s (pose time of stills); *tuning knob*; `CrowdHarness.cs:95`; docs character-renderer.md only; related env `MEITOU_CROWD_ANIMATE` (other group) overrides it with a real-time clock. rendering: none. **Verdict:** REMOVE — hardcode 0.35; delete `CrowdTime` field, parse, usage text, the `CharacterSwitches` doc reference.
- **`--view-distance`** (flag) — default 450000 (the whole world); *tuning knob*; sets `WorldCamera.ViewDistance`/`MinViewDistance` (WorldFrame.cs:703); `WorldFrame.cs:1297` already raises the far plane to `FogDistance/0.7`; docs viewer.md, render-terrain, settings.md; no measurement ever changes it. rendering: direct (terrain far plane). **Verdict:** MERGE → `--fog`: derive the far plane from the haze distance (`MinViewDistance` 0 and the existing `max(min, fog/0.7)`), so one distance setting remains; delete the flag, `ViewDistance` option field and its usage line.
- **`--fog`** (flag) — default 250000; *user-facing/tuning (distance where the haze is complete)*; honoured by the game (`GameHost.cs:206`) and the viewer (passed to `WorldFrame.Draw` at `WorldApp.Bench.cs:171,545`, `WorldApp.Benchmark.cs:73`); also bounds fog culling; docs viewer.md, sky.md, fogfeatures.md, render-distance-benchmark. rendering: direct. **Verdict:** KEEP — the one far-distance lever (a low-end preset could use it).
- **`--terrain-error`** (flag) — default 16 Meitou / 10 Faithful; *user-facing quality (also a Tab slider)*; `TerrainErrorFor`, `LandmarkTests`, docs (settings, terrain, render-distance-benchmark, viewer). rendering: direct. **Verdict:** KEEP.
- **`--terrain-far-error`** (flag) — default = near error (no ramp); *experiment now decided* (terrain.md:283: a far ramp "makes pond outlines visibly blocky, so no ramp by default"); read at WorldFrame.cs:710; docs terrain.md, settings.md, viewer.md. rendering: direct. **Verdict:** REMOVE — hardcode far = near; delete `TerrainFarError` option, the parse and `DefaultTerrainFarPixelError` scaling, `WorldRenderOptions.TerrainFarPixelError`, the Tab-slider/A-B scale copies (WorldFrame.cs:1067,1139; `WorldApp.Bench.cs:98-99`; `ReflectionPass.cs:304`), and the ramp parameters of `TerrainLod` if its tests allow.
- **`--terrain-ramp`** (flag) — default 7500; *tuning knob* of the far ramp above; same code. **Verdict:** REMOVE together with `--terrain-far-error` (`TerrainRamp`, `TerrainRampStart`).
- **`--help`** (flag) — prints `Usage`; *user-facing*. **Verdict:** KEEP.

### src/Meitou.Rendering/WorldFrame.cs (env)

- **`MEITOU_VRAM_GUARD`** (env) — on unless `0`; *kill-switch for a safety feature*; read once in `WorldFrame.cs:1007` (builds `VramGuard`); docs bench.md:75 (turned off because another viewer shared the GPU), render-foliage.md:136, renderer-native.md:3114. rendering: indirect (clamps ranges, pauses streaming). **Verdict:** KEEP — needed when benchmarking beside other viewers; make `=0` also disable the headless `VramWatch` (see `MEITOU_VRAM_KILL`). Family: `MEITOU_VRAM_BUDGET_MB` (other group).
- **`MEITOU_GC_LATENCY`** (env) — default `SustainedLowLatency`; *tuning knob*; `WorldFrame.cs:807`; docs engine.md:269 only. rendering: none. **Verdict:** REMOVE — hardcode `SustainedLowLatency`; delete the `switch`.
- **`MEITOU_GRASS_MOTION`** (env) — on unless `0`; *kill-switch for a feature now always on* (grass motion vectors for the upscalers); `WorldFrame.cs:1233,1401`; docs engine.md:213, render-foliage.md:223 (one "switched off against on" picture). The related `--ab grass-velocity` stays in other group. rendering: direct. **Verdict:** REMOVE — hardcode on; delete the `GrassMotion` static and the `&& GrassMotion` term.
- **`MEITOU_OCC_ALT`** (env) — off; *A/B measurement hack*; alternates the occlusion cull on even frames and renames the foliage stages `fol meshes+occ` / `fol rocks+occ` (`WorldFrame.cs:1420,1574`, `FoliageRenderer.cs:1257,1304`); docs bench.md:32 itself says `--ab occlusion` does the same, foliage.md:393,498 describe it. rendering: indirect. **Verdict:** REMOVE — superseded by `--ab occlusion`; delete `OcclusionAlternate` and the two stage-name ternaries (and the sibling `MEITOU_LOD_ALT` the same way, other group).
- **`MEITOU_SHADOW_LOG`** (env) — off; *debug dump* (one line per cascade drawn per frame); `WorldFrame.cs:1577,1596`; docs render-shadows.md (3 mentions as a method note), bench.md. rendering: indirect. **Verdict:** REMOVE — the numbers it produced are recorded in render-shadows.md; delete `ShadowLog` and the `Console.WriteLine`.

### src/Meitou.Rendering/WorldObjectRenderer.cs (env)

- **`MEITOU_LOD_DEBUG`** (env) — 0; *debug visualisation* (1 colours by LOD level, 2 wireframe by level); `WorldObjectRenderer.cs:43,622,943`; docs render-objects.md:49, renderer-native.md. rendering: direct. **Verdict:** MERGE → `--debug` (and `--wireframe`): there are already `--debug 1|2|3`, `--debug-shadows`, `MEITOU_WATER_DEBUG`, `MEITOU_FOLIAGE_DEBUG`, `MEITOU_FFX_DEBUG`; fold the debug visualisations into one `--debug <name>` family and drop the env.
- **`MEITOU_OBJECT_PART_RANGE`** (env) — default 3000 (`ObjectRanges.ObjectsViewRange`); *tuning knob for one benchmark*; `WorldObjectRenderer.cs:130`; docs render-distance-benchmark.md (3 mentions), viewer.md:160. rendering: direct. **Verdict:** REMOVE — hardcode `ObjectRanges.ObjectsViewRange`; delete `PartViewRange` parse (the experiment is documented).
- **`MEITOU_LANDMARK_LOG`** (env) — off; *debug dump* of resolved landmarks appended to `Describe()`; `WorldObjectRenderer.cs:222-227`; docs renderer-native.md only; `MEITOU_LANDMARK_SURVEY=1` (`WorldApp.cs:57`, other group) prints the same data. rendering: indirect. **Verdict:** REMOVE — delete `LandmarkDetail()` and its use in `Describe()`.
- **`MEITOU_OBJECT_TIMING`** (env) — off; *diagnostic* (summed CPU time of `Draw` steps); touches 25 lines in `WorldObjectRenderer.cs` (`ObjTiming`, `ObjAccount`, `ReportObjectTiming`, five accumulators); docs renderer-native.md:1584,1980 (old measurements). Superseded by `StageClock`/`PassMeter` and the bench harness. rendering: indirect. **Verdict:** REMOVE — delete all of the above.
- **`MEITOU_OBJECT_TIMING_SKIP`** (env) — default 160; *tuning knob* of the above; no doc. **Verdict:** REMOVE with `MEITOU_OBJECT_TIMING`.
- **`MEITOU_AB_SOLID_FIRST`** (env) — on unless `0`; *kill-switch for a decided A/B* (solid draws first, early depth); `WorldObjectRenderer.cs:978`; no doc; the same toggle is `--ab solid-first` (`WorldApp.Bench.cs:108`). rendering: direct. **Verdict:** REMOVE — make `SolidFirst` a constant true (keep it a settable property only while `--ab solid-first` stays; if that goes too, delete `SolidFirst`, the `if` at :960).

### src/Meitou.Rendering/WorldTextureCache.cs (env)

- **`MEITOU_MIP_LOG`** (env) — off; *debug log* (a line per image loaded with dropped levels and per swap); `WorldTextureCache.cs:111,651`; docs render-foliage.md:150, renderer-native.md:3194 ("for experiments"). rendering: indirect. **Verdict:** REMOVE — delete `MipLog` and its `if` block. Family with `MEITOU_MIP_MARGIN`, `MEITOU_MIP_TOP`, `MEITOU_MIP_STREAM` (other groups): the mip-streaming knobs should be cut together.
- **`MEITOU_MIP_SKIP`** (env) — none; *experiment knob* (keep named textures whole); `WorldTextureCache.cs:112,189` (sets `KeepAllMips`, which is also set in code for the distant-town texture); docs as above. rendering: indirect. **Verdict:** REMOVE — delete `MipSkip` and line 189.
- **`MEITOU_UNCOMPRESSED_TEXTURES`** (env) — off; *kill-switch for the old path* (decode to RGBA8 on the CPU); `WorldTextureCache.cs:470,483`; docs render-terrain.md:72 ("brings the old path back for comparison"). rendering: direct (GPU memory 4-8x). **Verdict:** REMOVE — hardcode compressed upload (`if (CanUploadCompressed(dds))`); delete `Uncompressed`. The RGBA8 fallback itself stays (used for non-BC formats).
- **`MEITOU_TEX_STATS`** (env) — off; *debug dump* at `Dispose` (resident textures, duplicates, size histogram); `WorldTextureCache.cs:672-685`; docs render-foliage.md:150 only. rendering: none. **Verdict:** REMOVE — delete the block.

### tools/Meitou.ModelViewer/CameraMotion.cs (env)

- **`MEITOU_BENCH_ORBIT`** (env) — 0; *old fly-benchmark knob*; read at `WorldApp.Benchmark.cs:53` and passed to `CameraMotion.Fly`; docs bench.md:32 ("old … still work"), viewer.md. Same as `--bench-motion orbit --bench-orbit` of the new harness. rendering: none. **Verdict:** MERGE → `--bench-orbit` (+ `--bench-motion orbit`): let `--fly-benchmark` read `o.BenchMotion/BenchOrbit` through `CameraMotion.For`; delete the env read and `CameraMotion.Fly`'s `orbitPixels` parameter.
- **`MEITOU_FLY_TURN`** (env) — 0 (radians per frame); *old fly-benchmark knob* for the occlusion cull's turn test; `WorldApp.Benchmark.cs:202`; docs bench.md:32, foliage.md:410, viewer.md. Same as `--bench-motion turn`. rendering: none. **Verdict:** MERGE → `--bench-motion turn`: delete `FlyTurn` and `CameraMotion.Fly`'s `turnRadians` parameter. (Units differ: radians here, degrees there.)

### tools/Meitou.ModelViewer/CardTrimSurvey.cs

- **`--card-trim-survey`** (flag) — mode switch; *one-off diagnostic* (trims every alpha-tested foliage mesh and prints the savings); `Program.cs:14`; docs render-foliage.md:241 only. The measured gain was small (foliage stage -0.05 ms, render-foliage.md:248). rendering: indirect. **Verdict:** REMOVE — delete `CardTrimSurvey.cs`, its `Program.cs` line and the doc sentence.
- **`--verbose`** (flag) — only for the survey above; `CardTrimSurvey.cs:18`. **Verdict:** REMOVE with it.
- **`--grass`** (flag) — only for the survey above (grass sprite outlines); `CardTrimSurvey.cs:23`. **Verdict:** REMOVE with it.

### tools/Meitou.ModelViewer/ImpostorApp.cs

- **`--impostor-preview`** (flag) — mode switch; *tool* (offscreen mesh-vs-impostor pictures); `Program.cs:15`; README.md:79, docs impostors.md (many), viewer.md, engine.md. rendering: direct. **Verdict:** KEEP.
- **`--impostor-bake-all`** (flag) — mode switch; *tool* (bake or load every eligible mesh, print times and sizes; validates atlases byte-identical across baker changes, impostors.md:455); impostors.md (5), renderer-native.md. The game bakes lazily, so this only pre-warms/validates the cache. rendering: direct. **Verdict:** KEEP.
- **`--out`** (flag) — default `C:\Temp\meitou-impostors`; *tool*; `ImpostorApp.cs:61`; impostors.md:221. **Verdict:** KEEP.
- **`--rebake`** (flag) — off; *tool* (ignore the cache); `ImpostorApp.cs:67,135`; impostors.md (3). **Verdict:** KEEP.
- **`--blend`** (flag) — off; *debug alternative* (blend the three frames instead of dithering) passed to `ImpostorPreview.Blend`; docs impostors.md:253 (once as a diagnostic). rendering: direct. **Verdict:** REMOVE — delete `Blend` option, parse, usage text, and `ImpostorPreview.Blend` if nothing else uses it.
- **`--class`** (flag) — default automatic; *tool* (force `small|medium|large`); `ImpostorApp.cs:216`; impostors.md:221,606, renderer-native.md:3308. **Verdict:** KEEP — the only way to preview the small class.

### tools/Meitou.ModelViewer/ImpostorApp.cs (env)

- **`MEITOU_IMPOSTOR_SURVEY`** (env) — off; *one-off diagnostic* (prints the meshes skipped for being too small during `--impostor-bake-all`); `ImpostorApp.cs:173`; impostors.md:592 (a recorded survey). rendering: none. **Verdict:** REMOVE — delete the `if` and its `WriteLine`.

### tools/Meitou.ModelViewer/PassMeter.cs (env)

- **`MEITOU_PASS_STATS_SKIP`** (env) — default 60; *tuning knob* of the pass meter (frames ignored); `PassMeter.cs:75`; docs: engine, bench, render-gi, render-clouds, render-distance-benchmark, renderer-native (about 15 command lines, usually 80). Same job as `MEITOU_BENCH_SKIP` (fly benchmark percentiles, default 1) and `MEITOU_SPIKE_SKIP`. rendering: none. **Verdict:** MERGE → `MEITOU_BENCH_SKIP` (one "frames to skip" for all three, or better a `--bench-skip` flag); delete this env read.
- **`MEITOU_PASS_TAG`** (env) — default `run`; *tuning knob*: label in the `PASSCSV|<tag>|…` lines for scripts; `PassMeter.cs:76`; docs engine.md:293 only; no script in the repo reads `PASSCSV`. rendering: none. **Verdict:** REMOVE — drop the tag column or hardcode `run`.
- **`MEITOU_VK_MICRO`** (env) — off; *one-off micro-benchmark* of `vkCmd*` call cost through Silk.NET vs the raw pointer; `WorldApp.Benchmark.cs:189` calls `PassMeter.VkCallMicro` (about 60 lines); the result is recorded (engine.md:484, renderer-native.md:3007). rendering: indirect. **Verdict:** REMOVE — delete `VkCallMicro` and the call.

### tools/Meitou.ModelViewer/VramWatch.cs (env)

- **`MEITOU_VRAM_KILL`** (env) — default 0.95 (0 = off); *safety watchdog threshold* for headless runs (exit code 9); `VramWatch.cs:58`; docs render-distance-benchmark.md:301 (overridden once to 0.995), render-foliage.md:136, renderer-native.md (3). rendering: none. **Verdict:** MERGE → `MEITOU_VRAM_GUARD`: `MEITOU_VRAM_GUARD=0` should also switch the watch off; hardcode 0.95 otherwise. Delete the env read.

### tools/Meitou.ModelViewer/WorldApp.Bench.cs (env)

- **`MEITOU_AB_MATERIAL_DISTANCE`** (env) — default 6000; *tuning knob of an A/B switch*; `WorldApp.Bench.cs:102` (side B of `--ab material-distance`); no doc. `--ab terrain-material` and the `--material-distance` flag already cover "material on/off" and "material distance". rendering: direct. **Verdict:** REMOVE — delete the env read, and with it the `--ab material-distance` registration (`WorldApp.Bench.cs:100-103`).

### tools/Meitou.ModelViewer/WorldApp.Benchmark.cs (env)

- **`MEITOU_BENCH_SHADOW_RANGE`** (env) — none; *tuning knob*: shadow range above the command line's cap (cap is `CommandLineMaxShadowRange` 15000; the comment calling the game limit 9000 is stale); `WorldApp.Benchmark.cs:21-27`; docs renderer-native.md:3343 only. `--shadow-range` already reaches 15000. rendering: direct. **Verdict:** REMOVE — delete the block.
- **`MEITOU_FLY_SHOT`** (env) — none; *debug* (save PNGs at given frames of the fly benchmark, next to `--screenshot`); `WorldApp.Benchmark.cs:84,205`; docs render-foliage.md:55, render-post.md. rendering: none. **Verdict:** REMOVE — delete `FlyShots` and the `if (… FlyShots.Contains(i) …)` block.

### tools/Meitou.ModelViewer/WorldApp.cs (env)

- **`MEITOU_PARTICLE_LIST`** (env) — off; *debug dump* of every live particle group; `WorldApp.cs:218`; no doc; `ParticleRenderer.Describe(eye, max)` has no other caller. rendering: none. **Verdict:** REMOVE — delete the line and `ParticleRenderer.Describe`.

### tools/Meitou.Tools/NavmeshTool.cs

All of these belong to `meitou-tools navmesh`, a navigation dev tool (rendering: none). Usage is in `Program.cs:42-45`; docs are `docs/simulation.md:388` and `docs/game/pathfinding.md:583`; `HubPathPins.cs` cites `--fingerprint`.

- **`--obj`** (flag) — writes the navmesh/geometry OBJ; used as a refactor gate (pathfinding.md:586). **Verdict:** KEEP.
- **`--png`** (flag) — top-down PNG; same. **Verdict:** KEEP.
- **`--scale`** (flag) — default 4 units/pixel for `--png`. **Verdict:** KEEP (parameter of `--png`).
- **`--geometry`** (flag) — gather and write only the input geometry, no build. **Verdict:** KEEP.
- **`--cell`** (flag) — overrides `NavBuildSettings.CellSize` (2); *build-parameter experiment*; nothing documents a value. **Verdict:** REMOVE (parse + usage; the setting itself stays in `NavBuildSettings` for the cache key).
- **`--tile`** (flag) — overrides `TileCells` (48); same. **Verdict:** REMOVE.
- **`--near`** (flag) — lists the buildings/parts near x,z,radius with their collision; used with `NAV_DUMP`; one doc line. **Verdict:** KEEP (absorbs `NAV_DUMP`).
- **`--no-interiors`** (flag) — skip building interiors; listed in simulation.md:389. **Verdict:** KEEP.
- **`--to-y`** (flag) — goal height for `--path` (multi-storey). **Verdict:** KEEP.
- **`--from-y`** (flag) — start height for `--path`. **Verdict:** KEEP.
- **`--hide-pruned`** (flag) — cosmetic: do not draw pruned polygons in the PNG (`static hidePruned`). **Verdict:** REMOVE (hardcode current default: drawn).
- **`--no-neighbour-seeds`** (flag) — skips `NeighbourSeeds.Collect`, which production always does (`NavMeshService.cs:86`); an A/B toggle of a decided behaviour; simulation.md:389 lists it. **Verdict:** REMOVE — keep neighbour seeds always on in the tool.
- **`--closed`** (flag) — path with doors closed. **Verdict:** KEEP.
- **`--watershed`** (flag) — builds regions with the watershed algorithm instead of monotone; only reachable via this flag (`ZoneNavMeshBuilder.cs:149`, `NavBuildSettings.Watershed`, `NavMeshCache.cs:32` key term); no doc. **Verdict:** REMOVE — hardcode monotone; delete the `Watershed` setting and branch; keep the `0` slot in the cache key (or bump `BuilderVersion`).
- **`--threads`** (flag) — worker count of the zone build; `NavBuildSettings.Threads` is also set by a test (`NavigationTests.cs:11`), so the property stays; no doc passes it. **Verdict:** REMOVE (flag only).
- **`--around`** (flag) — ring of neighbouring zones; simulation.md:388, `--fingerprint` basis. **Verdict:** KEEP.
- **`--path`** (flag) — find a path between two points. **Verdict:** KEEP.
- **`--repeat`** (flag) — rebuild n times for timings; simulation.md:699. **Verdict:** KEEP.
- **`--fingerprint`** (flag) — SHA-256 of the cache files of The Hub and the pinned paths; the refactor gate (`HubPathPins.cs`, pathfinding.md:586). **Verdict:** KEEP.
- **`--box`** (flag) — PNG window `x0,z0,x1,z1`. **Verdict:** KEEP (parameter of `--png`).

### tools/Meitou.Tools/Program.cs

- **`--keep-handles`** (flag) — third argument of `draw-log-diff`: do not rename Vulkan handles by first use; `DrawLogDiff.Run(…, keepHandles: true)`; no doc beyond the usage and renderer-native.md:1358. The draw log (`MEITOU_DRAW_LOG`) was the GL-vs-Vulkan parity aid of the native port; with VkGl gone it compares two native builds only. rendering: none. **Verdict:** REMOVE — delete the pattern line at `Program.cs:25` and the `keepHandles` parameter; consider removing `draw-log-diff` and `MEITOU_DRAW_LOG` altogether (owner decision, other group).

### tools/Meitou.Tools/WeatherTool.cs

`meitou-tools weather` (rendering: none); documented in `docs/formats/weather.md:603,640`.

- **`--region`** (flag) — name of the region to print. **Verdict:** KEEP.
- **`--days`** (flag) — day range, default 0..100. **Verdict:** KEEP.
- **`--list`** (flag) — print the regions with calendars. **Verdict:** KEEP.
- **`--extents`** (flag) — per region: cell count, bounding box and the cell nearest the mean (for placing a camera); undocumented. **Verdict:** KEEP (the one placement helper).
- **`--cells`** (flag) — centres of one region's areas-map cells, "to place the viewer" (weather.md:603); `--extents` gives the same answer for all regions in one call. **Verdict:** MERGE → `--extents`: delete the `cells` branch (about 8 lines) and the doc mention.

### src/Meitou.Navigation/NavDebug.cs

- **`NAV_DUMP`** (env) — any value; *debug dump*: `--near` also lists every field of a building record; `NavmeshTool.cs:173`; pathfinding.md:583. rendering: none. **Verdict:** MERGE → `--near`: print the fields only with `--verbose`, delete the env and `NavDebug.Dump`.
- **`NAV_VERBOSE`** (env) — any value, even `0`, enables it (`is not null`); *debug* listing of painters, seeds, interior listings, door joins and path points; used five times in `NavmeshTool.cs`; pathfinding.md:583. It is the only reason `NavDebug` exists; the library (`Meitou.Navigation`) never reads it. rendering: none. **Verdict:** MERGE → a `--verbose` flag on `meitou-tools navmesh`; delete `NavDebug.cs`.

**Group totals:** 90 items: keep 41, merge 11, remove 38.
