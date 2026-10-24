# Design decisions

A log of the calls that are settled, with the reasoning, so nobody reopens them by accident.
Newest first.

## 2026-10-24 · Mob and boss behaviour — BUILT, FIRST TUNING

The simulator's stand-in brains are now the real ones, in the engine-free core.

1. **Mobs** (`MobController`, `PackState`, `MobTraits`): the state machine the rules asked for.
   Idle at the post; the pack shares its first target; nearest living enemy in sight unless a
   taunt says otherwise; chase and attack inside the room's leash; return to the post when the
   leash is reached or the room empties, and reset to full health three seconds after the last
   enemy was seen; never respawn. Traits by archetype: Hollow Knights hold the doorway the
   enemy is coming through; Storm Acolytes keep the back of the room and shoot; Skitterers are
   fast; Ember Wisps float over hazards and pits; Gravewalkers rise once at half health unless
   the hit that killed them burned. Every action goes through the simulator's Cast and Move.
2. **Bosses** (`BossController`): targeting by archetype, taunt first always (the Hound hunts
   the lowest health, the Wraith the richest inventory, the rest the highest recent threat);
   the slam when two or more stand close; phases at 70% and 35%; enrage doubles damage after
   the budget's time; and the design sheet's mechanics as code: the Colossus's lava slams,
   Ember Shell and Collapse; the Glasswright's Partition (a temporary crystal wall of pillars
   across the room) and Shardstorm; the Matron's Brood, Pheromone and Molt; the Choir raising
   corpses and its interruptible Dirge; the Hound's Scent, Howl and 8-second chase leash; the
   Wraith's Covet, Snuff and Drift through doors; the Juggernaut's 3-second Crush, Trample into
   walls (it stuns itself) and Overdrive; the Herald's Blight, Miasma and Outbreak; the
   Sovereign's three phases. Swift, Venomous and Thorned affixes change behaviour; the numeric
   affixes are already in the generator's budget and are not applied twice.
3. **The director** (`EncounterDirector`) stations a room's pack in a ring and its boss at the
   centre from the layout, steps every brain, keeps brood adds on the Matron's mark, and hands
   deaths to the round state through the bridge as `MobPackCleared` and `BossKilled`.
4. **Two rules settled by the lab.** Damage over time is a fraction of the hit *as taken*, so
   armour and resistance shrink the burn and the poison too. And bosses have no resistances of
   their own: the budget is the whole of their toughness, as the tuner assumes (an early version
   gave each boss resistance to its own element and silently cut a party's damage against
   physical bosses by a third).
5. **First numbers, trio of Sentinel, Berserker and Warder at gear tier 2 in real rooms:** the
   Hound and the Wraith fall five times in six, the Matron and the Choir two in three, the
   Glasswright and the Juggernaut rarely, the Colossus, the Herald and the Sovereign never. The
   "check" bosses are doing their job against a plain brain that does not interrupt Overdrive
   or Dirge, does not kite lava well and has no Purge rhythm; a melee-heavy trio running out of
   floor against the Colossus is the design sheet's own prediction. Packs are cleared in seconds
   to a minute. Tuning continues with `--sim bosses`, `--sim packs` and `--sim roomtrace`.

## 2026-10-24 · The ability and damage pipeline — BUILT, FIRST TUNING

The first piece of engine work, done where it can be tested: every kit in the game now runs.

1. **Abilities are data** (`AbilityBook`): four per spec (basic, bolt, zone, mobility) for the ten
   weapon specs and three element kits, plus the thirteen ultimates, plus the mob and boss
   stand-ins. Each is a shape (self, target, projectile, pierce, cone, circle, dash), a filter,
   a range, a size, a damage multiple of the caster's DPS, an element, a status, a cooldown, a
   duration for zones, a knockback, and a buff. Everything that damages obeys full friendly
   fire. Passives that change numbers (Backstab, Deadeye, Execute, Frenzy, Momentum) are
   implemented by name in the simulator.
2. **The fight simulator** (`FightSim`) resolves a cast on the real grid: projectiles stop at
   the first body and at anything solid (pillars, low walls, pits, locked and closed doors);
   sight follows the same rules as the minimap; cones and circles need sight; dashes stop at
   walls; knockbacks stop at walls. The damage step runs physical through armour and the rest
   through resistances, then damage-reduction buffs; crits are 1.5× from a seeded RNG; statuses
   apply with the target's resistances and diminishing returns; damage over time ticks once a
   second from the hit that applied it; lifesteal, thorns, Bloodbath healing and Riposte and
   Perfect Guard reflects are in. Zero health downs a player (mobs die); a downed player bleeds
   out at 60 s; **only an enemy can finish a downed player** (a teammate's stray swing does
   nothing to a body), at half of max health as decided. Stagger is an interrupt, Stun and
   Freeze stop acting. Every hit goes to the `HitLog` the death recap reads.
3. **The bridge** (`FightBridge`) turns downed, finished, dead and revived into the match events
   the round state already validates, so the fight and the round agree through one door.
4. **The fight lab** (`FightLab`, `SimpleBrain`, `--sim duels|teams|boss|trace|bosstrace`) runs
   thousands of deterministic fights. Its brain avoids friendly fire with a target preview,
   flanks instead of stacking, taunts and shield-walls as a tank, heals and sanctuaries as a
   Mender, and turns a taunted boss on the Warden. First numbers: a geared trio beats the room
   boss every time in about a minute (the tuner's target), a fresh trio four times in five in
   about two minutes, a duo and a solo never. Healers lose duels, as designed. Mend was raised
   from 0.6 to 1.0 of DPS after the lab showed Burn stacking outran it.
5. **Still open for tuning:** the duel matrix is lopsided (Berserker and Duelist on top, the
   Shade specs and Warder at the bottom), which is partly the plain brain not kiting or
   stealthing and partly real. The numbers in the book are first tuning; the lab is the tool.

## 2026-10-23 · Interiors, doors, hazards, mutators, events, bots — LOCKED AND BUILT

Six additions, all deterministic from the seed and part of the public layout where they are
layout, all validated events where they are play.

1. **Room interiors** (`InteriorPlacer`). Pillars block sight and movement; low walls block
   movement and projectiles, not sight. Spawns and extracts stay bare. A one-cell ring inside
   every wall stays clear so no door is ever blocked, the centre stays clear for shrines and the
   exit, and a feature that would split a room is dropped. Boss rooms get symmetric pillars, the
   final boss a ring, everything else a few features by area. ASCII: `O` pillar, `-` low wall.
2. **Closable doors** (`DoorRules`, `DoorClosed` / `DoorOpened`). Shut instantly from the room or
   the room across the corridor; open with a 3-second channel broken by damage, from either side.
   A shut door blocks sight and paths for players and mobs. Vault doors are locks, not doors.
   Nobody breaks a door. Everyone who has explored the room or a neighbour hears it.
3. **Hazards** (`HazardPlacer`, `HazardRules`). Spike traps (25% of max health, rearm in 10 s),
   gas vents (poison while inside), pits (collapsed floor, impassable) and lava strips (burn while
   inside), placed by depth: deeper rooms carry more. Never in spawns, extracts, shrine rooms or
   boss rooms. Traps and vents are hidden to everyone but a Shade; a Shade alone disarms them
   (2-second channel, permanent). Pits and lava are obvious and permanent. ASCII: `^` trap, `%`
   vent, `_` pit, `~` lava.
4. **Season mutators** (`Mutator`). One rule twist per season from season two, rotating in order:
   Soulfeast (two souls per corpse and chest), Godless (shrines dark: no resurrection, ultimates or
   shrine goods), Keybearers (every boss carries a key), Dusk (half sight range), Twin Vaults. Each
   is one constant in `MatchRules` or `DungeonSettings`; `MatchDescriptor.CreateForSeason` applies
   it to a copy. Season one has none.
5. **Mid-round events** (`DungeonEventPlan`). One per round at the ten-minute mark, chosen from the
   public seed so clients know what and where, announced to everyone: a roaming elite in a hub
   carrying a relic (a unique rolled with the loot secret; when it dies the relic lies on the floor
   for whoever stands there), a vault whose lock fails for sixty seconds (anyone at the door may
   open it), or a shrine that resurrects for one soul for sixty seconds. The tutorial has none.
6. **Tutorial bots** (`BotRules`, `BotDirector`). Two bots on their own team, Warden and Marksman,
   that walk room by room to the loot room, open the chest, roll Need and take a piece, all as
   validated events; they fight at half strength, never finish a downed player and never loot the
   player's corpse. They exist in the tutorial and nowhere else: the matchmaker refuses a bot id.
   The tutorial dungeon now has two spawns.

Also: the console tool gained `--tier`, `--view TEAM`, `--no-interiors` and `--no-hazards`; the
README was rewritten to match the code; `docs/BUILD_ORDER.md` lays out the engine work.

## 2026-10-23 · After the audit: four rulings — LOCKED AND BUILT

1. **A player kill's soul lies on the corpse.** The blow grants nothing. The first ENEMY of the dead
   player to loot the corpse takes the soul, whoever landed the kill or the finish, and even if the
   dead player's own team rifled the body first; that team can take the gear but never the soul.
   A teammate kill or a mob kill leaves no soul. (`MatchState.CorpseSouls`, granted in
   `CorpseOpened`; `TeamView.Corpse.HasSoul` so the HUD can mark a corpse that still holds one.)
2. **The boss chest soul stays with whoever clicks the loot**, and **souls are never items**: no
   dropping, giving or trading (`FightRules.SoulsTransferable = false`). Taken, spent, or burned.
3. **Hand-offs have no account gate.** Day one, give or drop freely. The RMT guard is the tier
   walls, durability and the transfer ledger. Account levels now gate exactly one thing: the three
   earned classes.
4. **Thin queues above Tier I: two teams may start after three minutes**, in a dungeon sized for
   two (`Matchmaker.LongWaitSeconds` = 180, `MinTeamsAfterLongWait` = 2). Tier I keeps four teams
   after 90 seconds.

## 2026-10-23 · Audit: contradictions found and fixed — BUILT

A deliberate pass over every decision against the code found these. All fixed.

1. **The shared log was a wallhack.** "Clients apply the same log" meant every client received
   every team's `PlayerEnteredRoom`, rolls, souls, account levels and loot. And the client mirror
   could not even apply loot events, because its layout had the loot stripped. Replaced by
   per-team views: the server keeps the log; each team receives a `TeamView` of what it may know
   and `EventVisibility.Audience` decides who is told an event happened. The transport interface
   changed accordingly (`SendEventToTeam`, `SendViewToTeam`); reconnect is the next view.
2. **Clients could compute every chest from the seed.** `ClientView.Redact` existed but the client
   path regenerated the full layout, and loot was deterministic from the public seed anyway.
   Loot is now rolled from a server-only `lootSecret`; the descriptor carries the public
   fingerprint and the client builds without loot.
3. **Gear score used vendor value**, where a unique is twelve Normals: one unique from a Tier I
   boss threw a starter character out of Tier I. Gear score now has its own weights (1/2/3/4) and
   the bands are 0–50 / 35–170 / 130+. A starter kit is 6 (weapon and five armour pieces, not nine).
4. **Gating the shared bank behind account level 5 made no sense**: the bank moves gear between
   one account's own characters, which is no use to a mule. Removed; only hand-offs are gated.
5. **Free starter kits across eight slots were a materials farm.** Starter gear now sells for
   nothing and salvages to nothing (`Item.IsStarter`).
6. **Keys could not be handed over** although items could. `KeyGiven`, same room, no account
   gate (keys are worthless outside).
7. **Needing a unique you already carry** contradicted one-of-each. Such a Need counts as a Greed.
8. **Kill XP could be farmed between two accounts.** Paid once per victim per day (`KillXpTracker`).

Known and accepted, not changed:

* (The finisher soul, the boss chest soul, the hand-off gate and the thin queues were put to the
  designer and ruled on; see "After the audit" above.)
* **Affix tiers cap at item level 4**, so Tier III gear is better bases and more uniques, not
  bigger affixes. Tuning, not contradiction.

## 2026-10-23 · Combat feel and free-to-play operations — LOCKED AND BUILT

1. **Enemy bodies block, teammates pass through** (`MovementRules.EnemiesBlock / AlliesBlock`).
   A Warden can plug a doorway without trapping the healer behind them.
2. **Death recap: the killer's class and weapon spec, the last five hits, how long you lay
   downed. Never a name.** Hits are not match events; the server keeps the last five per victim
   in a `HitLog` and `DeathRecap.For` builds the screen. (`Runtime/Core/Net/DeathRecap.cs`.)
3. **Round-end summary from the match log**: extracted or not, value and silver walked out
   with, kills, boss kills, souls earned and spent, rooms explored, tier, account XP
   (`RoundSummary.For`).
4. **Store: direct purchase at a visible price plus a small featured shelf that rotates weekly.
   No loot boxes. No offer timer under seven days.** (`Monetisation`.)
5. **Easy Anti-Cheat (Epic Online Services) on the client**; server rejections remain the line
   (`TechStack.AntiCheat`).
6. **Reporting: cheating, real-money trading, abuse; one click from the death screen, scoreboard
   or spectator view; the report carries the match id. No automated action, ever**: reports are
   read by a human in order of distinct reporters (`ReportQueue`).
7. **Region: auto-picked by latency, manual override, a match never mixes regions**
   (`Region.Pick`, `QueuedGroup.Region`; the matchmaker keys queues by tier and region).
8. **No backfill.** Nobody joins after the doors open; a reconnect is not a join
   (`MatchRules.Backfill`, `PlayerSpawned` rejected once `DoorsOpen`).
9. **Seasons are three months** (`Season.LengthDays`, `Season.Starting`, `Season.Next`).
10. **English only at launch, every string externalized** (`Strings`): a missing key shows the
    key, a second language is a table loaded at runtime. The announcer and HUD labels already
    read from it.

## 2026-10-23 · Compass and minimap — LOCKED AND BUILT

* **A compass strip across the top of the screen.** Cardinal letters at their bearings (north is
  +Y, up the screen; the camera never rotates, so north is always up) and a marker for every
  teammate, every live team ping (8 s), and every extract, boss room, shrine, vault, the final
  boss and the centre exit **the team has seen**. Labels follow state: "Extract (open)", "Boss
  (dead)", "Vault (locked)", "Exit (open)". Nothing unexplored is on it. It exists for callouts
  ("shrine, north-east, 24 m") and for finding known things out of view, never for finding north.
  (`Compass`, `CompassBar`, `CompassHud`.)
* **The minimap in the corner, the full map on hold-Tab.** Explored rooms only, as decided
  earlier; corridors drawn only between two explored rooms; markers for self, teammates, live
  pings, locked vault doors and the open centre exit. Never an enemy. The corner map shows 18
  cells around the player. (`MiniMap`, `MiniMapModel`, `MiniMapHud`.)

## 2026-10-23 · Tiers, hand-offs, account levels and the business — LOCKED AND BUILT

1. **Three dungeon tiers, walled by gear score both ways.** Tier I admits gear score 0–50, Tier II
   35–170, Tier III 130 and up (`DungeonTier`; recalibrated in the audit below). You need the minimum to go up and are refused above
   the maximum coming down, so a geared player only meets geared players and a fresh one is never
   farmed. The bands overlap so a player at a wall can choose a side. Higher tiers add +2 / +4 to
   every drop's item level, multiply unique chances ×1.5 / ×2 (capped at 90%), and tune mobs and
   bosses for players 1.4× / 1.9× baseline. Tiers are separate queues (`QueuedGroup.Tier`); a match
   never mixes them. This partly supersedes "no brackets": inside a tier there are still none.
2. **Gear score = item level × rarity, worn AND carried** (`GearScore`, one item's score is its
   `Item.Value`). Everything you bring in counts, equipped or in the bag, so nobody carries a Tier
   III weapon into Tier I. Consumables and silver do not count. A starter kit scores 6.
3. **Silver splits.** A chest's purse divides evenly across the teammates near enough to roll for
   its gear (within two rooms); the remainder goes to the picker. Mob piles are the picker's.
4. **Hand-offs in the dungeon are allowed**: give an item to a player standing in your room
   (`ItemGiven`) or drop it on the floor (`ItemDropped`), where anyone in that room may take it,
   enemies included. The floor does not survive the round.
5. **Free to play, cosmetics only.** No battle pass, nothing for sale with a stat (`Monetisation`).
6. **Account levels, which mean nothing except "not on day one".** XP for extractions (120),
   enemy player kills (40), boss kills (60) and the final boss (150); never for time. Level 5
   (about a weekend) opens hand-offs (the shared bank is not gated, see the audit); the three earned
   classes open at 10 (Reaver), 15 (Elementalist) and 20 (Shade). Free accounts make mules and
   cheats cheap; this is the answer. (`AccountRules`, `PlayerProfile.AccountXp`.)
7. **RMT review.** Every hand-off is logged with its value (`MatchState.Transfers` →
   `TransferLedger`). Pairs whose flow runs one way (net 300+ over 3+ transfers) are flagged for a
   human. The tier walls already stop the obvious delivery routes: a seller cannot carry high gear
   into a low tier, and a buyer cannot enter a high tier without owning gear of that tier.
8. **Three classes on day one: Warden, Marksman, Mender.** Tank, ranged damage, healer. Reaver,
   Elementalist and Shade are earned. The vertical slice now builds the three day-one classes
   (Sentinel, Sharpshooter, Warder) and no element kit; the Elementalist lands with milestone two.
9. **A dead player spectates their team and may leave for the hub at any time**, forfeiting any
   resurrection (`PlayerLeft`). A wiped team goes straight to the hub.
10. **Two global announcements, nothing else**: the final boss falling and the centre exit opening,
    each with a sound (`Announcer`). Other teams' fates are learned by seeing and hearing.
11. **Leaderboards: value extracted is the main board; player kills, boss kills and final boss
    kills are side boards; every board per tier and in total** (`Season`, `LeaderboardKind`).
12. **Weekly contracts in season two**, three a week paying silver and account XP (`Roadmap`).
13. **No secure pouch.** Nothing you carry survives dying (`FightRules.SecurePouchSlots = 0`).

## 2026-10-23 · Need over greed — LOCKED AND BUILT

* **Group loot is rolled, World of Warcraft style**, so a player who queued with strangers has a
  fair shot. When a chest opens, every Magic-or-better piece in it goes to a roll among the opener's
  living teammates. Each answers **Need, Greed or Pass** within 20 seconds; silence is a Pass.
* **Anyone may Need.** Any Need beats any Greed. Among Needs everyone rolls a d100, and a player
  whose *current* class wears the piece (its weapon is one of the class's two spec weapons, its
  armour is the class's weight, jewellery fits everyone) adds **+50**. One on-class Need beats one
  off-class Need about seven times in eight, and two about four times in five. The off-class
  player still has a real chance; the on-class player is clearly favoured.
* **Only teammates within two rooms of the chest are in the roll.** The server reports each
  player's room (`PlayerEnteredRoom`); eligibility is fixed the moment the chest opens, so arriving
  late changes nothing. A teammate off doing their own thing gets no roll and no claim; the ones
  who stayed together share. Two rooms, not one, so the teammate holding the corridor or covering
  the door is still in.
* **Everyone passes: free for anyone on the team.** Solo players, one-player teams and a player
  whose teammates are all out of range never roll.
* **It binds teammates, not the dungeon.** Chests stay open to anyone. An enemy who reaches the
  open chest can grab a piece mid-roll, which ends that roll, and can take the winner's piece off
  their corpse. Rolling is a courtesy among teammates; full loot still rules.
* **The server decides.** Clients send `LootRolled`; the server runs `LootRollRules.Resolve` with
  its own RNG and publishes `LootRollResolved`, so nobody sees a number before choosing. The state
  validates that a Need was not beaten by a Greed, that a passer never wins, and that nothing is
  resolved before everyone standing has answered or the window has closed.
* Why: randoms are most of the queue, and the only thing that makes "I need that" fair between
  strangers is a roll everyone can see. Tying the bonus to the class you are playing, not the
  classes you own, stops a player Needing plate for an alt in the bank.

## 2026-10-23 · Souls are personal — LOCKED AND BUILT

* **A soul belongs to the player who earned it**, not the team. One for killing or finishing an
  enemy player (never a teammate). **One in every boss chest: whoever opens it first takes it**,
  not whoever landed the kill, and a boss chest cannot be opened while the boss lives.
* **Spending is personal.** A reviver pays two of their *own* souls at the shrine; you arm your own
  ultimate; a shrine consumable comes from your own pocket. A teammate's souls are not yours.
* **Death burns every soul you carried.** Not dropped, not transferred. The killer gets exactly one
  soul for the kill, whatever the victim held. Souls do not extract and do not survive the round.
* Why: a player carrying two souls is carrying a resurrection, an ultimate, or two potions, and
  losing a fight should cost that outright rather than hand it to the winner. It also means the
  gold aura is honest: an armed player is worth one soul to kill, never more.

## 2026-10-23 · Ultimates, settled — LOCKED AND BUILT

* **Every ultimate has one of three jobs:** deny space (Cave-In, Lockdown, Inferno, Suppression,
  Blackout), swing a fight (Aegis, Perfect Guard, Bloodbath, Absolute Zero, Last Breath, Outbreak),
  find or mark (Death Mark, Overwatch). The yardstick: two souls is a teammate's life; an ultimate
  must change what the room is, not just do more damage. (`WeaponSpec.UltimateJobs`.)
* **Six reworked** from the first draft: Glacier → **Absolute Zero** (Freeze 8 m for 2 s, ignores
  diminishing returns); Thunderclap → **Blackout** (no channels within 10 m for 8 s); Deadman's
  Mark → **Overwatch** (auto-shoot everyone entering line of sight for 10 s); Storm of Arrows →
  **Suppression** (10 m zone, Chill, no channels); Communion → **Last Breath** (instant revive of a
  downed ally, both at full health, never a finished one; `LastBreath` event); Pestilence →
  **Outbreak** (max Poison that spreads by touch).
* **Ultimates obey full friendly fire. No exceptions.** Inferno burns your team.
* **An armed ultimate is a soft gold aura on the character, visible to everyone in line of sight,
  not through walls, gone the instant it is used.** Things worth taking glow: Keybearer bosses,
  armed players.

## 2026-10-23 · Economy-in-the-dungeon and ultimates — LOCKED AND BUILT

1. **Silver drops in the dungeon, weightless.** A pile per mob pack (15 base), a purse per chest
   (20), more from boss chests (80) and vaults (150), all ×(1 + loot tier). Picked up once per
   room (`SilverPicked`; a chest's purse needs the chest open). **All of it drops on death** onto
   the corpse (`SilverLooted`). Extracted silver is recorded; silver left inside at round end is gone.
2. **No natural health regeneration**, in or out of combat. Menders, potions and lifesteal are the
   only healing. **Resources (Stamina, Mana) do regenerate.**
3. **Ultimates are earned, never given.** One per spec (thirteen in `WeaponSpec.Ultimates`), armed
   at a shrine for **two of the team's souls** (`UltimateArmed`), one use (`UltimateUsed`), lost on
   death, never leaves the dungeon. It competes with resurrection and consumables for the same
   currency, so a team that has won fights chooses between a second life, potions, or one big
   moment. Solos can arm one too.
4. **The dungeon is sized to the teams present:** the matchmaker's formed match carries
   `ForTeams(teams.Count)`.

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
7. **Vertical slice: 4 teams, 3 classes (Warden, Reaver, Elementalist with Pyromancy; changed
   2026-10-23 to the day-one trio Warden, Marksman, Mender), 2 bosses
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
   **player kills and boss chests** (one per enemy player killed, never teammates; one in each
   boss chest for whoever opens it first). Superseded in detail by "Souls are personal" above. **Once per player per round.** Shrines stand in hub rooms (at
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
