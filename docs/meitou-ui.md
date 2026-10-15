# Meitou UI kit

Design reference for the Meitou UI mode ([native-ui](native-ui.md)): the game's materials (rust,
worn paper, charcoal button plates, Exo 2 SemiBold, `kenshi_colours.xml`) as floating plates with
soft corners, as on the Meitou main menu. Values: [tokens.json](meitou-ui/tokens.json). Live
components: [reference/](meitou-ui/reference/) (web, React 18): run
`tools/scripts/meitou-ui-textures.ps1` once (crops the textures from the install into git-ignored
`reference/textures/`), then open `reference/index.html`. Online copy (private):
https://claude.ai/artifact/U49EczXEMYKM29AyPSGJPm.

## Surfaces

| Surface | Build | Text |
| --- | --- | --- |
| Plate (windows, tooltips, dialogue, clock) | `rust` under `plate-tint`, 1px `plate-rim` inset, `radius-card`, `plate-drop` | `main` |
| Paper card (cards, primary action, save card) | `paper-inner` stretched whole, `paper-border` inset, `radius-card` | `secondary` |
| Button plate (buttons, selected tab) | Button1Skin 9-slice (6) at 70% brightness, clipped to `radius-btn`; pressed skin at 85% on hover/press | `main`, `paper-light` on hover |
| Message banner | MessageSkin (caps 82 / 53), square | `secondary` |

- Never stretch the uncropped GenericTextBoxSkin: its 5px alpha rim (insets L5 R5 T5 B4) reads as a
  halo. Use `paper-inner` and draw the edge (2px `paper-edge`, 1px faint line, 10px `paper-wear`).
- One paper (primary) action per screen; everything else is a plate.
- No window frames. Plates float over the world; behind a stack that must read over the scene, a
  vignette: `radial-gradient(ellipse 620px 470px at 4% 100%, rgba(8,6,5,.93) 25%, rgba(8,6,5,.6) 55%, transparent 80%)`.

## Layout and states

- Radii: buttons/tabs 7, cards/windows/tooltips 10, portraits/badges 4, bars/items 3.
- Main menu (1600×900): stack 320 wide, 56 from left, 36 from bottom; save card, 8 gap, plate
  buttons 38 tall 6 apart, Exit 4 further. Icon column 24, 12 to the label.
- Type: Exo 2 600. 24 window title, 22 primary label (upper, 0.08em), 20 headings, 16 body,
  14 menu items (upper, 0.1em) and stat rows, 12 captions, 10 key badges (upper).
- Focus 2px `brass-light` ring offset 2. Disabled `greyed-bright`, plate at 50%. Paper brightens 10%
  on hover and drops 1px on press. Motion 100 ms, filter/transform only.

## Copy

Nouns for labels, verbs for buttons, title case in source (menu styles uppercase it). Numbers in
full (`64 / 80`), money `c.4,250`, true minus `−14`, details joined with ` · `, no percentages,
emoji or exclamation marks.

## Textures

Atlas `data/gui/gfx/Kenshi_UI.png`; rectangles from `data/gui/skins/kenshi_skins.xml`
(**Verified**: read from the XML). The script lists every crop; notable:
GenericTextBoxSkin 341,446,186,102; Button1Skin 612,766 (pushed 741,766) 118×45; MessageSkin
761,251,170,70; rust 1342,120,492,546; menu icons from the order sprite 0,0,256,170.
