# Build order

The rules are done. This is the order the game gets built around them, what each step needs,
and what "done" means. One developer, so the steps are serial; each ends in something playable
or measurable. Weeks are estimates for a solo developer working full time.

## Milestone 1: the greybox slice (about 10 weeks)

Four teams, Warden, Marksman and Mender, Iron Juggernaut and Hive Matron, Tier I, one look.
Everything is cubes. Done means: eight people can play a full round on a dedicated server and
the round summary is right.

1. **Scene from the layout** (1 week). **Greybox written, untested in Unity.** `DungeonBuilder`
   spawns tiles, interiors, hazards and markers, keeps door and hazard objects by id, and
   **Dungeon Gen ▸ Build Greybox Scene** assembles a playable scene from fallback cubes. Done: a
   4-team layout stands in a URP scene, walkable, with doors that open and close and vault doors
   that stay locked.
2. **Movement, aim and camera** (1 week). **Greybox written, untested in Unity.** New Input
   System through `LocalPlayerController` (LMB/RMB/Q/E/R/Space/Tab/F), intents to a 20 Hz
   `ServerSimulation`, snapshots per team back to `ClientWorldView`, `TopDownCamera`, enemies
   solid and allies passable, 5 m/s, no sprint, no prediction. Done: three people can run around
   the greybox together over Fishnet with the server authoritative on position.
3. **Fishnet transport** (1 week). **First draft written** (`FishnetMatchTransport`, gated by
   the `FISHNET` define; `LoopbackTransport` for one machine): descriptor broadcast, events and
   views per team, snapshots and intents, pings. Done: `DungeonMatchBootstrap` hosts a round,
   clients load views, a reconnect gets the current view, and the `--net` demo's event sequence
   runs live.
4. **Ability and damage pipeline** (3 weeks). **Core half done** (`AbilityBook`, `FightSim`,
   `FightBridge`, `FightLab`): every ability resolves on the grid with full friendly fire,
   mitigation, statuses, downed and finishing, and the hit log feeds the death recap. Left: the
   Unity side (casting from input is wired through `ServerSimulation`; projectile and zone
   visuals and hit feedback are not) and tuning with the lab. Done: the three slice classes' six specs and their ultimates play as written.
5. **Mobs and bosses** (2 weeks). **Core half done** (`MobController`, `BossController`,
   `EncounterDirector`): packs leash, share aggro, hold doors, keep range, reset and never
   respawn; all nine bosses run their mechanics, phases and enrage; deaths reach the round state.
   Left: the Unity side (the server tick drives `EncounterDirector`; animation and telegraph
   visuals are not drawn yet) and tuning the check bosses with the lab. Done: the two slice bosses play as written and the final boss
   opens the exit.
6. **Round flow** (1 week). **Core done, proven headless** (`MatchServer`, `RoundLab`): countdown
   and sealed doors, the clock, extracts, the mid-round event, round end and the summary all run
   on the real server loop with bot teams, and the tests keep rejected events at zero. Left: the
   announcement lines and the round summary screen in Unity. Done: a round plays start to finish
   in Unity with the summary built from the log.
7. **HUD** (1 week). Replace the OnGUI compass, minimap and `PlayerHud` with real UI; health, resources,
   ability bar, loot roll prompt, death recap. Done: everything a player needs to read is on
   screen and nothing an enemy should not see is.

The server loop itself (`MatchServer`) has no engine in it and already plays whole rounds headless
(`--sim round`), so a dedicated server is a console host around it, not a Unity build.

The first thing to do in Unity: install the package, run **Dungeon Gen ▸ Build Greybox Scene**,
press Play with `PracticeMode` on, and paste every console error back. The Unity layer has only
been compiled against stubs (`Tools/UnityStubs`), never run.

## Milestone 2: the game around the slice (about 8 weeks)

8. **Accounts and the hub** (2 weeks). PlayFab login, profile, stash, vendor, crafting bench,
   character slots, earned classes, menus. Done: a new account gets its starter kit and queues.
9. **Matchmaking and regions** (1 week). Tier and region queues, the two-team floor, the gear
   score gate at the door. Done: two groups in different regions never meet.
10. **The tutorial** (1 week). The solo dungeon with the two bots driven by `BotDirector`, the
    script steps, the shrine lesson. Done: a new player finishes it in fifteen minutes and knows
    what a soul is.
11. **The other three classes and six specs** (2 weeks), gated behind account levels.
12. **Easy Anti-Cheat, reporting, the transfer ledger review page** (1 week).
13. **Store and cosmetics** (1 week). Direct purchase, the weekly shelf, the season reward grants.

## Milestone 3: launch content (about 6 weeks)

14. **Art pass**: the one look. Room kits per room type, the eight boss models, class silhouettes.
15. **Audio**: full sound information, the two announcements, directional cues.
16. **Tiers II and III** enabled with their loot and tuning, already in code.
17. **Season one** with boards and rewards; mutators and weekly contracts wait for season two.

## Not before launch

Weekly contracts, a fourth tier, more languages, console. All designed, none built.
