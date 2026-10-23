# Classes

Six launch classes, three armour weights, one of each role. Multipliers are on the combat
baseline (`CombatBaseline`: 100 DPS, 1000 effective HP for "one average player"); the roster
averages to 1.0 on both so boss tuning and class balance share a single set of numbers.
Full loot PvP shapes every kit: no mid-fight resurrection, and every ability has a doorway use.

| Class | Role | Armour | STR | DEX | INT | CON | SPI | Primary | Kit | DPS | HP | Tagline |
|-------|------|--------|----:|----:|----:|----:|----:|---------|----:|----:|---:|---------|
| **Warden** | Tank | Heavy | 14 | 6 | 4 | 20 | 6 | STR | 0.625 | 0.70 | 1.50 | Holds the doorway so the team decides where the fight happens. |
| **Reaver** | Melee DPS | Medium | 16 | 12 | 4 | 10 | 8 | STR | 1.10 | 1.30 | 1.00 | Burst and mobility. Opens fights and ends them. |
| **Marksman** | Ranged DPS | Light | 8 | 17 | 8 | 7 | 10 | DEX | 0.95 | 1.15 | 0.85 | Controls sightlines down long corridors. |
| **Elementalist** | Caster | Light | 4 | 8 | 18 | 6 | 14 | INT | 0.97 | 1.20 | 0.80 | One class, three elements. The staff in hand decides which. |
| **Mender** | Support | Medium | 6 | 8 | 10 | 12 | 14 | SPI | 0.58 | 0.65 | 1.10 | Keeps the trio alive long enough for the math to work. |
| **Shade** | Skirmisher | Light | 8 | 16 | 8 | 5 | 13 | DEX | 0.85 | 1.00 | 0.75 | Finds the key, picks the moment, leaves with the loot. |

DPS and HP are derived (see *Player stats* in DESIGN.md): DPS = kit × (1 + 3% per primary point over 10),
HP = 1 + 5% per Constitution point over 10. Every class has exactly 50 attribute points.

Starting sheets (tier-0 gear), 100-damage hit taken by type:

| Class | HP | DPS | Crit | Armour | Carry | Physical | Fire / Frost / Lightning / Poison / Shadow |
|-------|---:|----:|-----:|-------:|------:|---------:|---:|
| Warden | 1500 | 70 | 3% | 100 (50%) | 58 kg | 50 | 107 |
| Reaver | 1000 | 130 | 6% | 60 (38%) | 62 kg | 63 | 94 |
| Marksman | 850 | 115 | 9% | 30 (23%) | 46 kg | 77 | 80 |
| Elementalist | 800 | 120 | 4% | 30 (23%) | 38 kg | 77 | 75 |
| Mender | 1100 | 65 | 4% | 60 (38%) | 42 kg | 63 | 85 |
| Shade | 750 | 100 | 8% | 30 (23%) | 46 kg | 77 | 76 |

The Warden takes *more* than 100 from magic: heavy iron and 6 Spirit. That is the trade; a Warden
walking into the Plague Herald's room wants a Mender more than anyone.

## Availability

Warden, Marksman and Mender are playable on a new account. Reaver unlocks at account level 10,
Elementalist at 15, Shade at 20 (`AccountRules`). Account levels come from extractions, kills and
bosses and gate nothing else but hand-offs and the shared bank at level 5; no class is stronger
for being earned.

## Kits

**Warden** — Shield Wall (block frontal damage 4s, allies behind take none) · Bulwark Slam (knockback cone, interrupts) · Hold the Line (root self, taunt, 40% DR for 6s) · Bastion (passive: immune to knockback in a doorway).
*Dungeon use:* body-blocks corridors and locked-door chokepoints; makes a vault defensible while a teammate loots it.

**Reaver** — Lunge (8m gap-close, next hit bleeds) · Execute (3× damage below 25%) · Blood Rush (kill resets Lunge, +30% speed 3s) · Frenzy (passive: +1% damage per 1% missing HP).
*Dungeon use:* fastest boss killer per player; the 2.5-player boss budget assumes one Reaver-grade DPS in the trio.

**Marksman** — Piercing Shot (line, corridor-length range) · Caltrops (slow field on a doorway, 8s) · Vault Back (backwards leap, brief invulnerability) · Deadeye (passive: +30% beyond 12m).
*Dungeon use:* punishes teams pushing through spine corridors; traps turn a hub room into a kill box.

**Elementalist** — the element is whatever weapon is held (Fire Staff, Frost Rod, Storm Wand), so losing the staff loses the specialisation and a crafted staff with a pinned element is a build. All three kits share one skeleton: a **bolt** (single target, applies the element's status), a **zone** (5–6 m area that applies it to anyone inside), a **mobility** move, and a **passive** that decides what the status does when it stacks.

| Element | Kit | On hit | Bolt | Zone | Mobility | Passive |
|---------|-----|--------|------|------|----------|---------|
| Fire | **Pyromancy** | Burn (DoT, spreads) | Firebolt | Firewall: 6 m line, 6 s, blocks movement | Cinder Step: teleport, leaves fire | Kindling: Burn spreads within 2 m, stacks to 3 |
| Frost | **Cryomancy** | Chill (slow, stacks) | Frost Shard | Glacial Field: 5 m circle, 6 s, Chills each second | Ice Slide: dash leaving ice that Chills | Deep Chill: 3 Chills Freeze for 1 s; hitting a frozen target shatters for +30% |
| Lightning | **Stormcalling** | Stagger (interrupt, chains) | Arc: chains to 2 within 4 m | Static Field: 5 m circle, 5 s, Staggers on entry | Blink: teleport | Conduction: Stagger on a Chilled target is a 1.5 s Stun |

*Dungeon use:* Fire closes a door without a Warden and clears adds (Hive Matron, Bone Choir corpses). Frost stops a chase (Hound of Vesh) or a retreat. Lightning ends a channel (Dirge, Overdrive) and is the only hard stun in the game, and only on a target someone has already chilled. Every room's mobs resist their own element, so a fire mage in an Ember Wisp room does 40% damage: specialising has a cost the dungeon collects.

**Mender** — Mend (HoT, stacks twice) · Sanctuary (circle, allies take 25% less) · Purge (cleanse bleeds, burns, slows) · Last Rites (stabilise a downed ally in 3s; no resurrection).
*Dungeon use:* the PartySustain factor (1.5) assumes a Mender; without one a trio fights a room boss at roughly duo odds.

**Shade** — Veil (invisible 5s while moving slowly) · Keen Sense (reveals keys, locked doors and the gated exit on the map for 10s) · Hamstring (slow + silence 2s) · Smoke Step (smoke cloud, dash through it).
*Dungeon use:* the extraction specialist: scouts vaults, sees who holds a key, gets a loaded team out of a contested extract.

## Weapon specs

Every class has two specs chosen by the weapon in hand (the Elementalist has three, one per
element). Same kit coefficient and attributes, so a spec changes *how* you fight, never how much.
A class holding a weapon it has no spec for fights with basic attacks at the off-stat penalty.

| Class | Weapon | Spec | Bolt | Zone | Mobility | Passive | Job |
|-------|--------|------|------|------|----------|---------|-----|
| Warden | Longsword | **Sentinel** | Shield Bash (Stagger) | Shield Wall | Hold the Line | Bastion | Holds the door; makes a vault defensible |
| Warden | Warhammer | **Breaker** | Crush (Stagger) | Shockwave: cone knockback + Stagger, long cooldown | Charge: knockback, interrupt | Momentum: +5% DR per hit while swinging, to 30% | Bruiser tank: clears a door from the other side, shoves a team off an extract. **Never breaks a lock.** |
| Reaver | Greataxe | **Berserker** | Cleave (Bleed) | Whirlwind | Lunge | Frenzy; kills refresh Lunge | Boss and add killer |
| Reaver | Longsword | **Duelist** | Thrust (Bleed) | Riposte: parry, reflect, interrupt | Lunge | Execute | The PvP killer |
| Marksman | Longbow | **Sharpshooter** | Piercing Shot | Caltrops | Vault Back | Deadeye | Corridor control |
| Marksman | Shortbow | **Volleyer** | Rapid Shot | Rain of Arrows | Tumble | Quickdraw | Anti-swarm, anti-rush |
| Shade | Twin Daggers | **Assassin** | Backstab | Smoke Step | Veil | Hamstring | Kills the key holder |
| Shade | Shortbow | **Trapper** | Snare Shot | Tripwire on a doorway | Smoke Step | Keen Sense (+ players through walls, 3 s) | Controls the extract |
| Mender | Holy Sceptre | **Warder** | Mend | Sanctuary | Purge | Last Rites | The reference healer |
| Mender | Censer | **Plaguebearer** | Smouldering Bolt (Poison) | Miasma: HoT cloud that Poisons enemies | Purge | Contagion: heals also Poison nearby enemies; −25% raw healing | The healer that fights |

Shortbow is shared by Marksman and Shade: the drop is contested and the class decides the kit.
Every weapon drop in the dungeon is a potential build change for someone.

## Ultimates (earned)

One per spec, armed at a shrine for two of your own souls, one use, lost on death, never
leaves the dungeon. Two souls is also a teammate's life, so every ultimate changes what the room
is rather than doing more damage. They obey full friendly fire, no exceptions, and an armed one is
a soft gold aura visible to anyone in line of sight.

| Job | Spec | Ultimate |
|-----|------|----------|
| Deny space | Breaker | **Cave-In**: collapse the doorway you stand in; rubble blocks everything for 15 s |
| Deny space | Trapper | **Lockdown**: every doorway of your room tripwired for 20 s |
| Deny space | Pyromancy | **Inferno**: your room burns for 6 s, Burn to everyone inside each second. Everyone. |
| Deny space | Volleyer | **Suppression**: 10 m zone, 8 s; inside it, Chill and no channels (no extract, revive, loot) |
| Deny space | Stormcalling | **Blackout**: 8 s, nobody within 10 m can channel anything, bosses included |
| Swing a fight | Sentinel | **Aegis**: 5 s, cannot be moved, allies within 5 m take half damage |
| Swing a fight | Duelist | **Perfect Guard**: 3 s, every hit parried and reflected; first attacker Staggered |
| Swing a fight | Berserker | **Bloodbath**: 8 s, hits heal 20%, cannot be downed; fall at the end if at zero |
| Swing a fight | Cryomancy | **Absolute Zero**: Freeze everyone within 8 m for 2 s, ignoring diminishing returns |
| Swing a fight | Warder | **Last Breath**: revive a downed ally instantly, both at full health; never a finished player |
| Swing a fight | Plaguebearer | **Outbreak**: enemies within 10 m go to max Poison and spread it by touch for 10 s |
| Find or mark | Assassin | **Death Mark**: a player your team sees through walls for 10 s; your hits on them crit |
| Find or mark | Sharpshooter | **Overwatch**: 10 s, every enemy entering your line of sight is shot once automatically |

## Composition notes

* Party size is 3. Warden / Reaver / Mender is the reference trio the boss numbers were checked against.
* Every trio should have an answer to a doorway (Warden, Pyromancy Firewall, Marksman caltrops) and a way to disengage (Reaver, Shade, Marksman, Blink).
* Two Elementalists on a team have a combo: Cryomancer Chills, Stormcaller Staggers, the target is Stunned. A Frost Rod drop is worth more to a team that already has a Storm Wand.
* Gear is the progression. Classes change *how* you fight, loot tier changes *how hard* you hit; the final boss expects tier-3/4 gear (×1.4) on all three.
* Keys are items in the inventory and drop on death like everything else. A Shade seeing who holds a key is a PvP target call, not a UI nicety.
