# UI screens

Original MyGUI stack/resources/layouts, MainBar/Overview and menu/inventory/trade/dialogue/build/
tutorial binary research. Catalog and named checks below. Current native compatibility:
[MyGUI](../formats/mygui.md), [parity](../faithful-ui-parity.md), [native API](../native-ui.md).
Input/action encoding: [ui-input](ui-input.md); gameplay backends are independent of UI appearance.

## Title artwork and location

**Unknown:** an exact playable-world location matching the cover/poster composition (wanderer,
flared ruined tower, moons). [Lo-Fi's wallpaper page](https://lofigames.com/kenshi-desktop-wallpapers/)
publishes the image without a location. **Observed:** [community discussion](https://www.reddit.com/r/Kenshi/comments/znnfwj)
describes it as artwork and suggests several different towers; this is not developer confirmation
that the scene is absent from the map.

**Verified against the installed base placement layers using `WorldLevelData.Buildings()` and
the merged building database:** the Watchtower record `4104-D-TwoStorey.mod` has placements
`2-rebirth-INGAME` in zone 19.13 at approximately X=-57608, Z=-86364 (nearest town: Burn's Tower)
and `2-Newwworld-INGAME` in zone 24.31 at X=-36274, Z=-707 (nearest town: Dust King Tower).
These are inspection candidates, not verified poster matches. Searching tower names/mesh paths
and their placements does not establish that every differently named ruin has been checked.

**Observed (file search of the install and `data/gui/images/kenshi_images.xml`):** the brush-stroke
"Kenshi" wordmark has no file of its own; it is painted into the title backgrounds
`data/gui/gfx/title_screen1/3/4.dds` (1920×1080). Meitou therefore ships the logo itself
(`assets/branding/kenshi-logo.png`).

## Flow and data

Main menu → start selection/character creation → HUD, with save/load/import/options and pause
state handled separately. Layout/widget resources do not imply all controls are implemented.
HUD consumes selection/inspection, medical/stats/skills/extended values, faction/state and squad
portraits; inspecting another faction clears order selection and displays its information only
(**Observed original behaviour**, not fully traced). Player commands must still target own squad.
Overview separates squad/faction/map/AI/jobs/research; inventory/trade needs footprint/quantity/
charge/prices/ownership/fit validation; dialogue/build/tutorials have distinct state/controllers.

Keep original widget IDs/skin/fonts/resources for GUI mods. Meitou styled mode is independent;
do not treat one mode's visual audit as the other's coverage. Table catalog and HUD/icon notes
retain new measured fields and current implementation limits.

### 2. Screen catalog

Addresses are the class constructor or layout loader (**Observed** unless noted). Layout names are **Verified**: each
`.layout` string is referenced by exactly the listed function in strings.tsv, and 49 of the 52 layout files are
referenced at all. Class names are RTTI names (**Verified** in rtti.tsv) except Splash, MapScreen and MessageBox,
which are descriptive (RTTI knows `MessageBoxManager::Box`; the constructor of a MessageBox is FUN_1406e70a0).

| Screen (class) | Address | Layout | Layer | Data shown |
|---|---|---|---|---|
| TitleScreen | 140918ef0 | Kenshi_MainMenu | Main | Continue, New game, Load game, Import game, Options, Credits, Exit; version text "Kenshi 1.0.68 - x64 (Newland)" |
| Splash | 140916140 (constructor), 140913720 (per-frame fade) | no layout file: widgets built in code from the image sets `Kenshi_Splash_Logos` (texture `physSplash.jpg`, 1024 x 768) and `Kenshi_Splash_LoFi` in `kenshi_images.xml` | - | logos; alpha rises at 3 per second (full in 1/3 s), a slide ends on any input after it is fully visible or after 4 s |
| EscMenu | 1409180e0 | Kenshi_MainMenuPopupPanel | Main | New game, Save game, Load game, Options, Exit game, Resume |
| NewGameWindow | 140917530 | Kenshi_NewGamePanel | Window | start-off title, description, Difficulty, Cash, Play Style; Begin / Advanced options / Cancel |
| NewGameOptionsWindow | 1409151d0 | Kenshi_AdvancedOptionsWindow | Window | difficulty sliders, see [combat.md](combat.md), [character-stats.md](character-stats.md#new-game-advanced-options-that-change-these-mechanics), [buildings-production.md](buildings-production.md#construction-and-production) and [factions-squads-towns.md](factions-squads-towns.md#population-and-contents) |
| LoadGameMenu | 140480b50 | Kenshi_LoadGamePanel | Window | save list, info panel, "Reset squad positions" |
| SaveGameMenu | 140481ed0 | Kenshi_SaveGamePanel | Window | name edit box |
| ImportGameMenu | 140481130 | Kenshi_ImportGamePanel | Window | ticks: positions, buildings, research, dead NPCs, relations; Advanced options; the import kind of the save request is described in [save.md](../formats/save.md#save-requests-fun_14047c310) |
| LoadingWindow | 14091a280 | Kenshi_LoadingScreen | Top | message text with a tip |
| MainBarGUI (HUD) | 14072d3b0 | Kenshi_MainPanel | Middle | see section 4 |
| Overview (ManagementScreen) | 14049f5b0 | Kenshi_OverviewWindow | Window | tabs Map, Faction, Research, Craft, Squads, Messages ("DIALOGUE"), AI |
| MapScreen | 140490210 | in Overview | - | world map, markers |
| ProspectingWindow | 14049c2e0 | Kenshi_ProspectingWindow | Window | resource lines (`Kenshi_ProspectingWindowResourceLine`) |
| CharacterStatsWindow | 1408ba700 | Kenshi_StatsWindow | Window | Attributes, Skills1..4, Statistics, Description1/2 data panels |
| Inventory (character) | 1401538e0 | inventory layouts | Window | slots: back, shirt, head, legs, boots, armour, main weapon, hip weapon, belt, backpack_attach, ARRANGE, LIMBS |
| Inventory variants | 140154cd0 generic, 140154f80 generic fixed, 140155220 animal, 140155910 research, 140155ee0 trader, 140155f90 building, 1401573d0 limbs | | Window | container contents |
| CharacterTradingWindow / Box | 14072a880 / 14072a070 | Kenshi_CharacterSelectionWindow / Box | Middle | character picker for trade ([economy.md](economy.md#12-slaves-prisoners-animals)) |
| DialogueWindow | 140725740 | Kenshi_ConversationPanel | Middle | portraits, names, dialog line, money (dialogue effects and AI contracts: [ai.md](ai.md#packages-and-goals)) |
| DialogueSpeechBubble | 140725f30 | Kenshi_SpeechPanel | Dialog | floating speech |
| ContextMenuGUI | 1407977a0 | Kenshi_ContextMenu | Popup | verbs, see [ui-input.md](ui-input.md#dispatch-and-selection) |
| BuildModeWindow | 1404e5210 | Kenshi_BuildWindow | Middle | categories, buildings, stats, Commands box, Confirm / Undo / Build, floor arrows |
| CharacterEditWindow | 1405f4dd0 | Kenshi_CharacterEditor | Back | character editor |
| MessageBox | 1406e70a0 | Kenshi_MessageBox | Info | message, buttons (Return = first, Esc = last) |
| MessageRoller | 140724af0 | Kenshi_MessagePanel | Popup | rolling event messages |
| Tutorial panel / list | 140975ca0 / 140977640 | Kenshi_TutorialPanel / Kenshi_TutorialsWindow | Window | tutorial text |
| OpenSaveFileDialog | 1401a0b10 | Kenshi_OpenSaveFileDialog | Window | file dialog |
| InteriorModeButtonWindow | 14077a1f0 | Kenshi_InteriorMode | Back / Main / Window (several widgets) | interior mode toggle |

## 4. HUD (MainBarGUI)

Layer Middle, per-frame update FUN_140728d50 (**Observed**):

- Shortcut buttons INV, STA, MAP, TCH (research), SQD, HLP; floor panel with text "Floor {1,num}"; four speed
  buttons ([game-loop.md](game-loop.md#speed-and-pause): pause, 1x, 2x, 5x); money / day / time panel (money `c.{1,num}`; the clock text is described in [game-loop.md](game-loop.md#frame-clocks), money in [economy.md](economy.md#units-stacks-and-traders)); squad tab control; medical panel; orders panel;
  information, tooltip, PAUSED, loading and build panels; town / outpost panel; biome name panel with fading text
  (biome at the camera position).
- Medical panel (the body parts it shows are in [character-stats.md](character-stats.md#hunger-blood-and-parts)): nine `LifeBar1`..`LifeBar9` widgets in the layout but the code looks up `LifeBar10`, giving the
  MyGUI.log warning "Widget with name 'LifeBar10' not found. [Kenshi_MainPanel.layout]" (**Verified**: log line
  and `Kenshi_MainPanel.layout`, which has LifeBar1..9 only). Life-bar skins in `data/gui/skins` (**Verified**):
  `Kenshi_LifeBarSkin`, `...HurtSkin`, `...HealedSkin`, `...StunnedSkin`, `...CrushedSkin`, `...RobotSkin`,
  `...SplintSkin`; each `LifeBarN` holds the layers `LifeBarNGrey` (Stunned skin), `...Red` (Hurt), `...Yellow` (Healed), `...White` (Splint),
  `...Green` (current), `...Crushed`, `...Robot` (**Verified**, layout).
- Text panels (**Verified**, decompile of the HUD update and its line-panel class "DatapanelGUI", 2026-10-14): `NameText`, `StateText`, `StatsText`
  and `ExtendedStatsText` are empty widgets in which the code builds a list of lines, each a label (from 3 % of the width, 96 % wide) and a value (same
  start, 93 % wide, aligned right), in `Kenshi_TextboxStandardText` (Exo2 SemiBold 20, default colour #140806, the palette's Secondary). Colours are
  inline `#rrggbb` from `data/gui/colours/kenshi_colours.xml`: Main #afa68b, Title #492620, Bad #59231a, BadBright #bf4834, Good #a8b774, Alert #000000.
  - Name: the character's name left and its faction's name right (the player's default faction name is "Nameless"), both Title.
  - State lines: `State:` Dead / Locked (lvl: N) / Sleeping / Unconscious (Rebooting for skeletons) / Defensive / Normal (Bad for the bad ones);
    `Goal:` the AI goal package's name, else Aimless; `Encumbrance:` (int)((1 - f) × 100) "%" with f the encumbrance factor in 0..1, Bad from 50 %.
    Extended (when expanded): Athletics XP, Strength XP, Toughness XP, KO point, Run speed, Hunger Rate.
    Extended values (**Verified**, decompile of `FUN_140895350`, 2026-10-14; Meitou: `BodySystem.ExtendedOf`, `PlayerInterface.ExtendedLines`):
    Athletics XP `{n}%` = (int)(100 × `FUN_1408c5ec0`): the encumbrance factor, ×1.5 when the current speed is over 80 % of the run value V (the `xp rate athletics` constant is applied elsewhere).
    Strength XP `{n}%` = (int)(100 × `FUN_1408c60c0`), the carrying rate (includes `xp rate strength from walking`); with a weapon in hand the original shows a "Weapon weight" figure from a field at `+0x184` instead (**Unknown**, Meitou keeps the carrying rate).
    Toughness XP `{n}x` (the suffix is the `x` at `0x1416bc008`, **Verified** in the exe) = `FUN_1408c5f40`: 1, but 2 with the left arm crippled, +2 right arm crippled, +2 blood-loss KO, +2 when a counter at `+0xa4` is under 15 (**Unknown**, Meitou omits it).
    KO point = (int)`FUN_140643ae0` (negative HP, [character-stats.md](character-stats.md#hunger-blood-and-parts)). Run speed = the current top speed S × 0.22369362 (units per second to mph) rounded, `{S} (+{d}) mph` or `(-{d})` where d is S minus V in mph; plain when equal. Hunger Rate `{n}%` = (int)(100 × `FUN_1408837a0`), the race's hunger rate × its situation factor.
    Each line also carries a tooltip with its breakdown (Encumbrance penalty, Full speed bonus, Carrying somebody, Crippled arms ...), not modelled.
  - Stats lines: Martial Arts when unarmed; Crossbows, Precision, Perception with a crossbow in ranged mode; else Attack and Defense; then Strength,
    Dexterity, Toughness and (not in ranged mode) Dodge. A value is the effective number, then its change in brackets: `{1} ({2})` all Bad when lower,
    `{1} (+{2})` all Good when higher, plain when equal. The code passes the labels in mixed case ("Martial Arts:"); the original shows them upper case in
    a light colour (**Observed**, screenshot), from a source **Unknown**.
  - `HealthText` words in priority: Dead, BEING EATEN ALIVE!!, Dying, Unconscious (N), Recovery coma, Playing dead..., Blood loss KO, Bleeding out,
    Crippled, Crippled arm, Critical, Limb trauma, Staying low, Starving (hunger &lt; 1), Bleeding, disguise words; else by the overall health h:
    Hungry (h ≥ 0.7 and hunger &lt; 2), Wounded (h &lt; 0.7), Slightly Injured (h &lt; 0.9), Normal. What h is exactly is **Unknown**; the worst part's
    fraction matches a screenshot (a leg at 77 % and "Slightly Injured", **Observed**).
- Medical rows (**Verified**, decompile): Blood (Oil for skeletons) first, then the body parts, Hunger last; each `LifeBarNDatapanel` gets the name centred
  and an arrow string right-aligned in Alert; the number goes in `LifeBarNValue`. Layers are sized as width fractions from the left (Grey is a full-width
  background). Part (H health, S a deduction, D a pending amount, Mx its maximum): Green (H − S)/Mx, Yellow (H + D − S)/Mx, Red (Mx − S)/Mx, White
  splint % × 0.01 + green, number floor(H − S + 0.1); arrows `<<   ` below 0 and degenerating, `<<!!   ` at the KO point, `!!!   ` destroyed. Blood:
  green blood / capacity, number floor(blood + 0.1), arrows a run of `<`, ceil(|bleed rate| / 0.05) of them (at most 100). Hunger (0..3): number
  (int)(hunger × 100), Green hunger − 2, Yellow hunger − 1, Red hunger; arrows `>   ` recovering, `<!   ` below 2, else `<   `. The parts come in the
  order head, stomach, chest, left arm, right arm, left leg, right leg for people (**Observed**, screenshot); Meitou reads the RACE `combat anatomy` references in another order (right leg first), so how the original orders its part list is **Unknown** and Meitou sorts by part name to match.
- Information panel: `ExtendingPanel` starts collapsed, its bottom moved onto the panel's bottom (behind the Overlapped `StatsPanel`), and slides over
  0.5 s to its layout place when `ExtendedStatsButton` is pressed (**Verified**). `BountyPanel` shows only for one selected character with a bounty or
  a status line (Imprisoned, Slave, Escaped slave, Committing Crime!, Leader...; **Verified**).
- Inspecting another faction's character (**Observed** in the original, not traced in the code): a left click on a character that is not yours drops your selection and fills the information and medical panels with that character (name, faction, state, stats, extended stats, medical rows). Meitou keeps this as display state, `PlayerState.Inspected` set by `InspectCommand` (not hashed, not saved); a click on your own character or the ground clears it, shift keeps your selection.
- Money panel: `MoneyLabelText` is set to "Money" (without the layout's colon; 25 % opacity when the amount would overlap it), `DayText` "Day: {1,num}", `TimeText` HH:MM (**Verified**). A "1x"
  seen next to the clock in a player's screenshot is not written by the vanilla code (**Observed**; most likely a mod).
- Squad tabs: one per player squad with members, captioned with the squad's name unchanged (**Verified**); the first player squad reads `Nameless_0`
  (**Observed**; where the name is made is **Unknown**). Tab width min(header width / tabs, ButtonWidth × UI scale), in the skin's `HeaderPlace`.
- `FloorPanel` stays as laid out and `FloorText` is "Floor {1,num}" of the viewed floor each frame; `BuildPanel` is always shown (**Verified** /
  **Observed**).
- Orders panel (FUN_14072b540): buttons Block, Hold, Chase (`OrdersChaseButton`), Passive, Jobs, Ranged, Taunt,
  Sneak, run-speed arrows (`SpeedArrowLeft/Right`; the layout has a symbol image `Run`, the labels Jog / Walk
  were not found), Medic, Rescue, Prospect, a behaviour slider (captions AGRESSIVE / CONFIDENT / SUBMISSIVE,
  **Verified** in `Kenshi_MainPanel.layout`) and a jobs list ([ai.md](ai.md#packages-and-goals)) with the hint "To add jobs, hold shift key when
  giving an order". Hold = hold position; Block = defensive mode ([combat.md](combat.md#techniques-block-and-dodge) has a +20 guarding-state term in the block chance; whether it is this stance is **Unknown** there) (+20 melee defence, no attacks; **Verified**
  against the tooltip string in the exe: "gain 20 to his melee defense skill ... will not attempt to make any
  attacks"); Passive = non-combat mode (tooltip strings).

## 6. Inventory, trade, dialogue, build

- **Observed** in the vanilla screenshot supplied on 2026-10-15 (Wolf, Thieves Backpack, Armour King): the character title is the character's name; the bag title is its item name;
  the shop title is the trader's name. The character pockets contain 8 columns by 10 rows of square cells. The bag is left of the character, stock right of it, with an independent
  vertical scrollbar. Equipment icons occupy their cell footprints; empty clothing slots are red, other empty cells pale. Stack counts are plain numbers in the lower-right corner;
  stock prices are not printed over every icon. The bag has a 10-by-14 grid with a fitted frame and an ARRANGE footer without the generic layout's editor capacity/type fields.
  The character parchment has separate label/value rows: Total Weight, a burden description, Disguise, Slave of, Looks like slave. **Unknown**: exact runtime sizing/rounding,
  burden description thresholds, and race/mod overrides of the pocket dimensions. Meitou uses 8 by 10 for new pockets and retains taller loaded inventories when saved items require it.
- Item placement failure messages (FUN_14070f7e0; the checks behind them are in [economy.md](economy.md#shops-and-transactions)): Out of trading range; No room for that item; You can't
  afford that; The shopkeeper can't afford that; That's not for sale; Got caught stealing; Smugglers only buy
  illegal goods. Stealing and fencing chance text: FUN_140712920. Item icon: FUN_140711200.
- Item icons (inventory and trader cells; code `ItemIcons`, `ItemIconStyle`, `ItemIconCache`, `PlayerInterface.AddItemCell`, `CharacterRenderer.Icons.cs`; the game's code is FUN_140711200
  building the cell and FUN_140847bf0 choosing the picture):
  - **Which kind** (**Verified**: FUN_140847bf0 decompiled, and against the saved pictures below). The game draws an item's icon from its mesh when the record is a WEAPON or its
    `auto icon image` is true (fcs.def default: true for CROSSBOW, ITEM, NEST_ITEM, WEAPON; false for ARMOUR, CONTAINER, LIMB_REPLACEMENT, MAP_ITEM; the base game's records all store it).
    Any other record shows the PNG its `icon` field names, else `default_icon.png` (`data/items/icons`; the game logs "[Icon] Missing image").
  - **Static files** (**Verified**, base game): 318 records (ITEM 268, ARMOUR 31, MAP_ITEM 17, CROSSBOW 2) name a PNG under `data/items/` (`.\data\items\icons\Fuel.png`, a few in
    `armour\pants\`); all 318 exist (test `Every_icon_file_a_base_game_record_names_exists`). The files are sized to the footprint at 25 px a cell (75x75 for 3x3, 150x25 for 6x1; **Observed**).
  - **The game's saved pictures**: it looks for the texture name in its `Icons` resource group first and draws only when it is not there, saving `<install>/data/icons/<name>`.
    The name is the item's string id, `.`, the instance's material string id and, only when the item has a colour record, its `dont colorise` is false and the material has a non-empty `color map`,
    `.` and the colour's string id (FUN_140845e80 and FUN_140847bf0, **Verified**). The folder is made on the player's machine, so it holds only items they have seen (**Observed**: 842 files,
    all of that form; 116 with a colour; also mod items; `data/icons/buildings/` holds BUILDING pictures, 618 files, not used yet). Every picture of a base-game record is of a drawn kind
    (test `Every_picture_the_game_saved_of_a_base_game_item_is_of_a_kind_it_draws`) except limbs.mod's eight severed limbs (ITEM with a static icon; why they are drawn is **Unknown**). The colour is
    mostly not the record's own `color` reference (15 of 116 are), so it is the instance's colour, which `ItemInstance` does not carry.
  - **How it is drawn** (**Verified** against FUN_1408473d0, FUN_140846060, FUN_140845f30 and FUN_140840380): a picture of 50 x 50 pixels a footprint cell, each side rounded up to a power of two (saved
    files: 5x4 256x256, 7x1 512x64, 8x2 512x128, 2x6 128x512, 3x3 256x256); a perspective camera, vertical field of view 45 degrees, aspect the footprint's, at (0, 0, 5 x `icon zoom`) looking at the origin;
    the item's node at (`icon offset H`, `icon offset V`, 0) turned by pitch (`icon offset pitch`), then yaw (`icon offset yaw` - 90), then roll (`icon offset roll`) about its own axes. A WEAPON uses the
    fixed pitch -90, yaw -90, roll 90, its `bare sword` mesh (the others `mesh`) and a node scale of (`scale thickness`, `scale length`, `scale width`) times `overall scale`, the item's times the material's.
    fcs.def defaults: zoom 2.7, V -8, yaw 90, the rest 0; WEAPON and CROSSBOW zoom 1.0 and V 0; WEAPON H -4. The material is a clone of `RTTIcons_Coloured` (mesh with vertex colours), `RTTIcons_DXT5N`
    (one item kind, 0x11) or `RTTIcons_Base`, double sided for `material type` 2, textured from the instance's material record. Lighting (rtticons.hlsl, lightingFunctions.hlsl): one white punctual
    light along the view axis, no ambient: colour = albedo x pi x N.L x 0.6 + GGX specular (roughness 1 - 0.99 x gloss, F0 0.4), stored linearly in an 8-bit target; the background is transparent.
    The reflection cube (`cloudy_noon.jpg`) is missing from the install, so the game's own pictures have none.
  - **Meitou's order** (`ItemIcons.Choose`): a drawn kind takes the game's saved picture of exactly that item and material without a colour part, else is drawn by `CharacterRenderer`
    (below); the other kinds take their static file, else `default_icon.png`; with nothing (or while the picture loads), the cell shows the item's name.
  - **Meitou's drawing** (`CharacterRenderer.Icons.cs`, `ItemIconStyle`): the item's mesh through the character shader's item mode with the lighting above (a negative `FogDistance` in the view
    constants selects it), into its own UNORM texture, drawn each frame until the textures it uses are resident, then kept (rows bottom first like every target: shown with `ImageFlipV`).
    Against six saved pictures (test `Icons_drawn_from_the_mesh_cover_the_same_part_of_the_picture_as_the_games_saved_ones`, silhouettes agree to two decimals on all): mean colour identical on two bags
    (142 114 91 and 131 104 81), 5 % darker on two armours (108 95 82 against 113 100 87), 10 % and 7 % darker on two swords (131 123 113 against 143 136 127; 122 119 117 against 130 128 125);
    why the weapons and armour run dark is **Unknown** (gloss, normal-map or colour-map handling). An icon is drawn undyed: the game's dye is the instance's colour, which `ItemInstance` does not carry
    (the saved picture of an armour whose record names a `color` has no colour part and shows none).
    **Verified** by `Cached_starter_icons_use_the_original_files_without_rendering_replacements` against the configured install: the sandbox preview's Rag Loincloth
    (`2308-clothes_v1.mod`, material `2312-clothes_v1.mod`) and Iron Club (`52295-rebirth.mod`, material `914-gamedata.base`) select their original cached PNG files directly.
    Their appearance is not evidence of the fallback renderer's accuracy; comparisons require the same item and material.
    **Observed** in installed `rtticons.material` / `rtticons.hlsl`: Base and Coloured variants discard fragments whose normal-map alpha is below 0.5;
    the DXT5N variant decodes X from alpha and Y from green without that transparency test. Meitou's fallback enables the Base normal-alpha cut-out at 0.5,
    separately from worn clothing's 0.6 threshold. `Icons_drawn_from_the_mesh_cover_the_same_part_of_the_picture_as_the_games_saved_ones` includes the starter icons and
    requires 99% silhouette agreement for the Rag Loincloth, whose transparent tears are missed by the opaque samples.
    Not done: BUILDING icons, the vertex-colour and DXT5N variants (the item shader is used), writing the pictures to disk (a readback inside the frame would stall it), bounding the icon textures
    (one per item and material seen, at most 512 x 512, colour only).
- Build window ([buildings-production.md](buildings-production.md#construction-and-production) for the rules it enforces): groups come from BUILDING `building category` and `building group`; placement error messages in
  FUN_1404e27e0 (**Observed**).

### 7. Tutorials

34 top-level `Tutorial_*` classes (85 with nested ones; **Verified**: rtti.tsv class names). `__tutorials.data`
(**Verified**: file bytes in the install - 136 bytes, little-endian u32 words `0xBAAAAAAD, 34, 0, 30, 4..33` - and
the writer FUN_140974500, whose log string names the class `TutorialManager::save`; the loader FUN_140974a50 reads
the same layout, **Observed**). The file is written to `./__tutorials.data` in the working directory:

| Field | Type |
|---|---|
| magic | u32 0xBAAAAAAD (bytes AD AA AA BA) |
| total | u32 = number of tutorial items held by the manager (34, equal to the top-level class count) |
| countA, then countA ids | u32 list taken from the manager object at +0x48 (written first), empty in the sample |
| countB, then countB ids | u32 list taken from the manager object at +0x28 (written second), 30 ids 4..33 in the sample |

## Unknowns

- Meaning of the two id lists in `__tutorials.data`.
- Why the code looks up ten life bars while the layout has nine (`LifeBar10` is missing); the medical panel's overall health h; where the stats labels' upper case and the default squad name come from.
- Exact tab index of the AI tab and which layout widgets belong to which tab for tabs 4 to 6.
- Trade, dialogue and research logic (other subsystems).
- Layer of the secondary layouts without a `layer` attribute (Kenshi_OrdersPanelOrder, PortraitCharacter, SquadBox, SquadsPanel_PortraitCharacter, TutorialTooltipPanel, ItemListItem, CharacterSelectionBox): they are children of other widgets (**Observed**, not traced).
