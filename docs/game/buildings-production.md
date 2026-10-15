# Buildings and production

Original building classes/function IDs, records, construction/producer/power/storage/farm/research
binary research; field/constants tables and expressions retained. Placement: [zones](../formats/zones.md),
prices/stock: [economy](economy.md). Meitou production/research gameplay is incomplete.

## Construction and production

**Verified** displayed build-hours=materials/(instance build-speed-mult*CONSTANTS build-speed*option),
CONSTANTS6; zero materials gives no time. This division contradicts editor wording. **Observed**
actual work scales dt*option*K, sequential builder factor0.75 (counter capped15/wrap100), stored
rate/target and possible mode*3; conversion from materials/data to progress rate is undecoded.
Do not substitute the UI duration formula for an unverified tick implementation.
Dismantle refund truncates random[0.6,1]*remaining amount, at least1; destruction trigger unknown.

Producer/crafter consumes input recipe quantities, worker/skill/race/research/environment and
functionality rate, then creates output with quality/manufacturer rules. Crafting queue,
storage/output capacity and machine operation are distinct. Keep stateful progress and
availability tests; do not infer all rates from the displayed output-rate tooltip. Farming
fertility, water, crop growth, harvest/yield and labour differ from a generic machine.

## Power, storage and research

**Observed** power-output is data plus additive research increase; capacity is data plus increase,
then health fraction. Positive generates, negative consumes. Wind/fuel and battery output/capacity
are separate. Town sums supply/reserve, fairly shares remaining power among consumers and uses
batteries for deficit/charges from surplus. Minimum powered share and wind/fuel units remain
unknown. Tested base generator/battery values remain in data/source checks, not universal defaults.

**Verified** inventory-building survey87/603; default inventory18x18, itemtype-limit4. Storage
filters and stack bonuses apply separately from character backpacks. Research owns tech levels,
prerequisites, costs/progress, unlock/improvement effects and blueprints; money prices are in
[economy](economy.md). Additive versus multiplicative improvement semantics must be decoded per
field. Building operational state, ownership and saved progress: [save](../formats/save.md).

### Production and crafting

Main functionalities in the base game (**Verified**, probe of the functionality table: `production mult` and `output
per day`; all those listed have `output per day` true except crafting benches, power, battery and the furnace):

| function | examples (production mult) |
|---|---|
| 1 MINE | Ore mine 64, Stone mine 128, copper mine 72, well 48 |
| 1 MINE (farm types) | farming wheat 64, veg 64, cactus / cotton / hemp / potatoes / rice 48 |
| 4 REFINERY | Campfire 192, grain silo 72, Iron / Steel / copper plates 48, Rum / Grog / Sake / bread / drugs 24, chainmail and plates 12, Textiles cotton 32 |
| 5 GENERATOR | Power 0.00125, wind power 1 |
| 10 CRAFTING | most benches 1, cooking general 2 |
| 14 ITEM_FURNACE | Furnace 0.25 |
| 23 BATTERY | battery 0.00125 |
| 26 RAIN_COLLECTOR | 48 |

## Unknowns

- How the data `build materials` and `build speed mult` feed the per-call build rate and target in `Building::vfunc_70`
  (the stored rate at offset `0x18c`, default 0.5, and the target at `0x188`), where its builder counter is reset, and
  what the flag that triples the work is. The UI hours formula is verified; the in-game rate is not.
- When the dismantle refund (`FUN_14029dc10`) runs, and the exact slope comparison and unit of `max slope`.
- The other event types of the queue at `DAT_1421346d8` (only type 8, research progress, was followed); how
  worker-assignment events reach buildings.
- The unit and exact use of generator fuel burn (`production mult` 0.00125) and of the wind fraction.
- The crafting quality curve (`FUN_1402b5900`), the unit conversion between `craft time hrs` and the per-tick
  crafting amount, and the minimum power share for "powered".
- Default values of the three option sliders and whether they multiply or divide in every use.
- Meaning of the percentage and flag of consumes / produces reference triples.
- Whether the 0.6^k research falloff counter is reset per frame (per call is what the code shows).
- The argument `p` of the farm efficiency `FUN_1400dfe10`, and the record field that carries `BuildingClassType`.
