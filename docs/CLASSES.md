# Classes

Six launch classes, three armour weights, one of each role. Multipliers are on the combat
baseline (`CombatBaseline`: 100 DPS, 1000 effective HP for "one average player"); the roster
averages to 1.0 on both so boss tuning and class balance share a single set of numbers.
Full loot PvP shapes every kit: no mid-fight resurrection, and every ability has a doorway use.

| Class | Role | Armour | DPS | HP | Tagline |
|-------|------|--------|----:|---:|---------|
| **Warden** | Tank | Heavy | 0.70 | 1.50 | Holds the doorway so the team decides where the fight happens. |
| **Reaver** | Melee DPS | Medium | 1.30 | 1.00 | Burst and mobility. Opens fights and ends them. |
| **Marksman** | Ranged DPS | Light | 1.15 | 0.85 | Controls sightlines down long corridors. |
| **Pyromancer** | Caster | Light | 1.20 | 0.80 | Area denial. Burns rooms, not people. |
| **Mender** | Support | Medium | 0.65 | 1.10 | Keeps the trio alive long enough for the math to work. |
| **Shade** | Skirmisher | Light | 1.00 | 0.75 | Finds the key, picks the moment, leaves with the loot. |

## Kits

**Warden** — Shield Wall (block frontal damage 4s, allies behind take none) · Bulwark Slam (knockback cone, interrupts) · Hold the Line (root self, taunt, 40% DR for 6s) · Bastion (passive: immune to knockback in a doorway).
*Dungeon use:* body-blocks corridors and locked-door chokepoints; makes a vault defensible while a teammate loots it.

**Reaver** — Lunge (8m gap-close, next hit bleeds) · Execute (3× damage below 25%) · Blood Rush (kill resets Lunge, +30% speed 3s) · Frenzy (passive: +1% damage per 1% missing HP).
*Dungeon use:* fastest boss killer per player; the 2.5-player boss budget assumes one Reaver-grade DPS in the trio.

**Marksman** — Piercing Shot (line, corridor-length range) · Caltrops (slow field on a doorway, 8s) · Vault Back (backwards leap, brief invulnerability) · Deadeye (passive: +30% beyond 12m).
*Dungeon use:* punishes teams pushing through spine corridors; traps turn a hub room into a kill box.

**Pyromancer** — Firewall (6m line of fire 6s, blocks movement) · Meteor (delayed AoE, telegraphed) · Ignite (DoT that spreads between adjacent enemies) · Cinder Step (short teleport, leaves fire).
*Dungeon use:* Firewall closes a door without a Warden; Meteor answers a team turtling in a vault.

**Mender** — Mend (HoT, stacks twice) · Sanctuary (circle, allies take 25% less) · Purge (cleanse bleeds, burns, slows) · Last Rites (stabilise a downed ally in 3s; no resurrection).
*Dungeon use:* the PartySustain factor (1.5) assumes a Mender; without one a trio fights a room boss at roughly duo odds.

**Shade** — Veil (invisible 5s while moving slowly) · Keen Sense (reveals keys, locked doors and the gated exit on the map for 10s) · Hamstring (slow + silence 2s) · Smoke Step (smoke cloud, dash through it).
*Dungeon use:* the extraction specialist: scouts vaults, sees who holds a key, gets a loaded team out of a contested extract.

## Composition notes

* Party size is 3. Warden / Reaver / Mender is the reference trio the boss numbers were checked against.
* Every trio should have an answer to a doorway (Warden, Pyromancer, Marksman caltrops) and a way to disengage (Reaver, Shade, Marksman).
* Gear is the progression. Classes change *how* you fight, loot tier changes *how hard* you hit; the final boss expects tier-3/4 gear (×1.4) on all three.
* Keys are items in the inventory and drop on death like everything else. A Shade seeing who holds a key is a PvP target call, not a UI nicety.
