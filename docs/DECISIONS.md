# Design decisions

A log of the calls that are settled, with the reasoning, so nobody reopens them by accident.
Newest first.

## 2026-10-22 · Round, progression and economy rules — LOCKED

Answered in one sitting; each line is a decision, not a default.

1. **Flat characters.** Attributes never change; gear tiers are the only vertical progression.
   Account-level unlocks are cosmetic or recipes. Boss tuning stays true forever.
2. **Round: 20 minutes, extracts open at 4.** All teams enter at once. At 20:00 anyone still
   inside dies and drops everything (`RoundEnded` kills everyone not extracted). The clock is a weapon.
3. **Resurrection, not spectating.** A dead teammate can be brought back at a **shrine** for
   **2 souls**; they return **naked**, and the team decides who gives up gear. Souls come from
   **player kills only** (one per enemy player killed; never teammates), so PvE teams cannot res and
   PvP has a reward beyond loot. **Once per player per round.** Shrines stand in hub rooms (at
   least one per dungeon), so a res is always a contested walk. (`PlayerResurrected`, `ShrinePlacer`.)
4. **Keys are destroyed if extracted unused.** Nothing back. Use it or lose it.
5. **Weight-only inventory.** No slot grid. Worn and carried kilograms count against carry
   capacity (30 + 2 per Strength + affixes); over the limit you move slower, down to half speed at
   double load. Heavy plate weighs 30 kg a set, medium 15, light 6. (`CarryRules`, `ItemBase.WeightKg`.)
6. **Teams of 1 to 3, one queue.** A solo knows the room boss is built to beat them.
7. **No gear brackets at launch.** Flat characters bound the gap to ~1.4×; watch the data.
8. **Consumables: three, from the vendor and ordinary chests.** Health Potion (30% over 3 s,
   60 s cooldown, 40 silver), Antidote (bottled Purge, 90 s, 60 silver), Salvage Kit (salvage in the
   dungeon, 25 silver). Silver's sink; a Mender-less trio survives a room boss at duo odds.
9. **Vendor only at launch.** No player trading or market until there is a population.
10. **One look at launch.** Themes later, once the loop is proven.
11. **No spawn protection.** The ring spacing is the only defence.
12. **Colour only for damage types, with a colour-blind palette option.** Shapes and sounds per
    element are not a launch requirement.

## 2026-10-22 · Presentation: top-down camera, 3D assets, mouse-aimed skillshots — LOCKED

Albion Online's model for the camera, Bloodline Champions' for control.

* **Camera.** 3D scene viewed from above, pitch 55–60°, **fixed yaw** so corridors stay
  axis-aligned to the generator's grid. No player rotation: an eight-team fight is only readable
  if everyone shares the frame. Slight lead toward the mouse so a Marksman can see down a hall.
  (`TopDownCamera`.)
* **Scale.** One grid cell = 2 m. A 9-cell boss arena is 18 m across; a corridor is a 2 m hall;
  the 8-team dungeon is ~310 m on a side. (`DungeonBuilder.CellSize`.)
* **Control.** WASD movement, abilities aimed with the mouse at the ground plane, telegraphs drawn
  on the floor. Positioning, not target selection.
* **Vision.** Line-of-sight fog: walls and locked doors occlude, you see what your character
  could see, not everything in a radius. This is where top-down gets its dread back and what makes
  a doorway, Veil, Shrouded rooms and Ember Wisp light into real mechanics. The rule lives in the
  core (`LineOfSight`) so the server can enforce it and use it for interest filtering.
* **Lighting is atmosphere.** 3D geometry under dynamic light, because the design already leans on
  it: Shrouded, lava glow, wisps lighting a room, the Lantern Wraith.

Why not full 3D / third person: every tactical element in the design is a floor-plane shape (a
Firewall across a door, a Glacial Field in a hub, a Warden in a corridor, Burn spreading between
adjacent enemies); the dungeon is a flat grid with small arenas; eight teams need readability over
immersion; and a fixed camera is a fraction of the art, animation and camera-code cost. What full
3D offered (fear of the corner) comes back through LoS vision.

## 2026-10-22 · Weapon specs for every class — AGREED, NOT YET BUILT

The Elementalist's "the weapon in hand is the spec" generalises: every class gets **two** specs
chosen by weapon base, on the same bolt / zone / mobility / passive skeleton, same kit coefficient
and attributes (tuning unchanged). Warden: Longsword Sentinel / Warhammer **Breaker**; Reaver:
Greataxe Berserker / Longsword Duelist; Marksman: Longbow Sharpshooter / Shortbow Volleyer; Shade:
Daggers Assassin / Shortbow Trapper; Mender: Sceptre Warder / Censer Plaguebearer.

The Breaker is a **bruiser tank** (Crush, Shockwave knockback, Charge, Momentum damage reduction
that builds while swinging). It does **not** break vault doors: a spec that bypasses a lock would
make keys optional and the key holder stop being a target. No spec is ever a skeleton key.

## 2026-10-22 · No repairs; vendor, salvage, crafting

Gear breaks for good. Broken items are sold or salvaged; salvage yields scrap, typed essences (one
per affix family, 24 of them) and prime shards; crafting picks a base and level, may pin up to two
essence families and force Rare with shards, and otherwise rolls like a drop. Uniques are never
crafted. Essences are typed, not generic: what you can craft depends on what you fought.

## 2026-10-21 · Uniques carry a legendary affix

Each unique has one behaviour no rolled item can have (lifesteal, thorns, resist cap, procs,
summon, second wind, heal splash, phasing), as a separate type from the affix pool so it is
unique-only by construction.

## 2026-10-21 · Eight boss archetypes, two per family, never repeated in a dungeon

Space denial, summoner, chaser, check. Affixes rolled 0–2 per boss multiply the budget and never
add mechanics, so the 2.5-player line holds.

## 2026-10-21 · Ring layout for 4+ teams, radial spine, candidate selection

Fairness measured as min/max walking distance per team to extract / final boss / nearest boss /
nearest rival; the generator keeps the fairest of four candidates per seed.
