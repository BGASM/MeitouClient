# Test cleanup audit

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

# Per-file audit


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

# Audit chunk 1 (Ogre, Physics, Rendering A-O)

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

# Audit chunk 2 (Rendering O-W, Save, Simulation)

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

# Audit chunk 4 (tests/Meitou.Tests/World, part 2)

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
