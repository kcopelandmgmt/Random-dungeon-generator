# Design decisions

A log of the calls that are settled, with the reasoning, so nobody reopens them by accident.
Newest first.

## 2026-10-22 · Fight rules — LOCKED AND BUILT (`FightRules`)

1. **Abilities cost a resource and have a cooldown.** Physical classes (Warden, Reaver, Marksman,
   Shade) spend **Stamina**; casters (Elementalist, Mender) spend **Mana**; basic attacks are free.
   Bolt 10% of the pool, zone 30%, mobility 25%; the pool regenerates 5%/s scaled by Spirit.
2. **Zero health is downed, not dead.** Downed for **60 s**; any friendly player revives with an
   **8 s channel** broken by damage to the reviver; the revived stand up at 30% health. An enemy
   who deals **50% of the downed player's max health** finishes them: then only a shrine and two
   souls bring them back. The timer running out is death. Solos cannot be revived.
   (`PlayerDowned`, `ReviveStarted` / `ReviveInterrupted` / `PlayerRevived`, `PlayerFinished`.)
3. **Looting is a channel, broken by damage:** chest 2 s, corpse 3 s, key 1 s. The player looting
   the boss chest is the easiest kill in the dungeon. (`LootStarted` / `LootInterrupted`; opening
   and key pickup are validated against the channel.)
4. **Mobs attack the nearest player; a taunt overrides.** Bosses keep their own targeting.
5. **Enemy players show a health bar, a class icon and ally/enemy colour.** No name, no gear:
   uniques are a surprise until the corpse.
6. **Hard control has diminishing returns:** full, half, quarter, then immune, within 10 s.
   Players only; mobs and bosses take it in full. (`ControlResistance`, wired into `StatusTracker`.)
7. **No sprint.** Speed is build and class.
8. **One of each unique.** `Equipment.Equip` refuses a second copy.
9. **A new account:** one character in the free kit, 200 silver, two health potions. (`PlayerProfile.NewAccount`.)
10. **Round start: a 10-second countdown** with the doors sealed and the spawn room on the minimap;
    then every door opens at once (`DoorsOpened` from the `RoundClock`). No protection after.

## 2026-10-22 · Technical and production decisions — LOCKED

Stated in code as `TechStack` and `VerticalSlice`; the rules with behaviour are built.

1. **Netcode: Fishnet.** Free, fast, client-side prediction built in, which a 5 m/s skillshot
   game needs. Implements `IMatchTransport`.
2. **Hosting: managed dedicated servers** (Edgegap or Hathora): upload the server build, instances
   per match by region, billed per hour. Full loot plus server authority rules out player hosts.
3. **Backend: PlayFab** for accounts, profiles, the shared bank and season leaderboards.
4. **Engine: Unity 6 with URP** (Forward+ for the dynamic lights the design leans on).
5. **Pathfinding: grid A\* on the server** on the layout's own tiles (`GridPath`); no NavMesh.
   Locked doors block mobs as they block players; leashes are a rectangle around the room.
6. **Art direction: stylized low-poly**, flat-shaded or hand-painted. Reads from 22 m, cheapest to
   keep consistent across bought packs, lights well.
7. **Vertical slice: 4 teams, 3 classes (Warden, Reaver, Elementalist with Pyromancy), 2 bosses
   (Iron Juggernaut, Hive Matron), one preset.** `VerticalSlice.Settings()`; the generator draws
   only the slice's bosses (`DungeonSettings.AllowedBosses`). Every system once, half the content.
8. **Platform: PC (Steam) first, controller supported, console later.**
9. **Team: solo, with bought assets.** The systems are sized for it; the art load is the
   constraint the slice cut addresses.
10. **Disconnects: the character stands where it was, killable, for a 60-second window.**
    Reconnecting replays the log; after the window the server kills it (`PlayerDisconnected`,
    `PlayerReconnected`, `MatchRules.ReconnectWindowSeconds`, `MatchState.ReconnectExpired`).
11. **Eight character slots per account, one class each, one shared bank** (stash, silver,
    materials on the profile; `PlayerProfile.MaxCharacters`).
12. **Hub: menus only.**
13. **Name: open.** Candidates proposed; see the conversation record. The working name stays
    until one is chosen; then package, namespace, docs and artifact rename in one commit.

## 2026-10-22 · Movement: WASD with mouse aim — LOCKED

WASD, screen-relative under the fixed-yaw camera (W is up the screen, which is grid north), the
character faces the mouse, movement is independent of facing. **No click-to-move**, not even as an
option: in PvP one scheme would be strictly worse in a fight and its players would learn it the
hard way. A controller maps to the same scheme (left stick move, right stick aim). No universal
dodge: mobility belongs to specs (Vault Back, Tumble, Blink, Cinder Step, Charge, Smoke Step).

Why not Albion's click-to-move: full friendly fire, mouse-aimed skillshots, zones that block your
own team, knockbacks and an extraction channel that any hit resets all assume a player can move one
way while aiming another and sidestep a telegraph without turning. Path of Exile 2 moved to WASD
for the same reason. Cost accepted: eight-direction locomotion blends per class, and one-handed
accessibility traded for a controller scheme.

Numbers (`MovementRules`): base 5 m/s = 2.5 cells/s. A Warden in plate ~4.4 m/s, a Shade ~5.3.
A corridor crossed in under a second; spawn to extract on the sample seed 13–20 s; the 8-team
dungeon edge to edge in about a minute.

## 2026-10-22 · Moment-to-moment and long-game rules — LOCKED AND BUILT

1. **Free starter kit, always.** `Vendor.ClaimStarterKit`: a Normal tier-0 set of the class's
   weight plus a weapon of its primary, free, any time. Worth nothing to a killer, everything to a
   naked player. Nobody is locked out.
2. **Full friendly fire, full strength.** Your damage, zones, knockbacks and slows apply to your
   team exactly as to enemies. A teammate kill grants no soul. (`CombatRules`.)
3. **Boss chests and corpses are open to anyone, immediately.** Corpses persist to round end.
   (`CorpseOpened`, `ItemTaken` from a corpse; `ContainerOpened` has no team restriction.)
4. **Extraction is an 8-second channel, reset by any damage.** Gates never close, no capacity.
   (`ExtractStarted` / `ExtractInterrupted` / `PlayerExtracted` validated against the channel.)
5. **Mobs leash to their room, attack any team, reset when the room empties, never respawn.** (`MobRules`.)
6. **Minimap of explored rooms only.** The server derives `RoomSeen` from line of sight; Keen
   Sense is the only way to see further. (`MatchState.Explored`.)
7. **Shrines sell consumables for one soul each**, to anyone, so solos have a use for souls.
   (`ShrinePurchase`.)
8. **Seasons with a leaderboard of value extracted and cosmetic rewards; a silver fee on
   crafting.** Stash and silver carry over; only the leaderboard resets. Fee 10/20/40/80/160 silver
   by level. (`Season`, `CraftingRules.SilverFee`, `MatchState.ExtractedValue`.)
9. **Sound is full information**: footsteps 12 m, combat 20 m, boss fights 30 m, vault doors 25 m,
   extraction channels 20 m, through walls at half per wall. (`AudioCues`.)
10. **Pings only, no built-in voice.** Team-scoped, transient, never logged. (`TeamPing`,
    `IMatchTransport.SendToTeam`.)
11. **Solo tutorial dungeon first.** One team, a small dungeon with one boss, one vault, two
    extracts, extracts open at once, a 45-minute clock. (`DungeonSettings.ForTutorial`,
    `MatchRules.Tutorial`, `MatchDescriptor.CreateTutorial`.)

## 2026-10-22 · Round, progression and economy rules — LOCKED AND BUILT

Answered in one sitting; each line is a decision, not a default. Code: `PlayerProfile` /
`Character` (flat), `Inventory` (weight), `Vendor`, `Matchmaker` (1–3, no brackets), `RoundClock`,
`DungeonTheme.Default`, `ElementPalette` (standard + colour-blind), `MatchState` (souls, res, keys, wipe).

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

## 2026-10-22 · Weapon specs for every class — BUILT (`WeaponSpec`)

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
