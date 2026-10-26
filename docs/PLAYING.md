# Playing the game

How to launch it, what the keys do, how to play with other people, and where your progress lives.
Working title: Dungeon Game.

## Launching

| Build | Where | Use it for |
|-------|-------|------------|
| Release | `D:\unity dungeon game\dungeongame\Builds\Win\DungeonGame.exe` | Playing. |
| Development | `D:\unity dungeon game\dungeongame\Builds\WinDev\DungeonGame.exe` | Testing: adds the effects audition (F1) and practice extras. |

Rebuild either one from the package repository with `bash Tools/Build/unity-batch.sh build-win`
(release) or `build-win-dev` (development). The Unity editor is not needed to play.

## A round, start to finish

1. **Create a character.** Pick a name and a class. Warden, Marksman and Mender are open from the
   start; Reaver, Shade and Elementalist unlock with account level. A new account gets a starter kit,
   200 silver and two health potions.
2. **Prepare.** Choose a spec (each weapon gives a different spec and ability set), how many enemy
   bot teams to face (1–3), and check the tier's gear-score wall. Then enter the dungeon.
3. **The countdown.** Ten seconds sealed in your spawn room. Then every door opens.
4. **Explore and loot.** Open chests and corpses (hold Space; looting is a channel that damage
   breaks), choose items in the loot panel, kill mobs and room bosses, collect souls from boss chests
   and fallen players. Carry weight slows you when you are overloaded.
5. **Shrines.** Spend souls to arm your ultimate, resurrect a fallen teammate, or buy supplies.
6. **10:00.** A mid-round event: a roaming elite with a relic, a vault that unseals for a minute,
   or a cheap shrine.
7. **Get out.** Extracts open after 4 minutes. Stand in an extract room and hold Space to channel
   out. Defeating the final boss, the Sovereign Below, opens an exit in the centre.
8. **Settlement.** Extract and you keep everything you carried out. Die and you lose everything you
   brought and found (full loot); account XP is paid either way. The summary shows what changed.

## Keys

| Action | Keyboard and mouse | Gamepad |
|--------|--------------------|---------|
| Move (screen-relative) | W A S D | Left stick |
| Aim | Mouse | |
| Basic attack | Left mouse | Right trigger |
| Bolt | Right mouse | Left trigger |
| Zone | Q | Right shoulder |
| Mobility | E | Left shoulder |
| Ultimate (armed at a shrine) | R | North button |
| Interact, loot, open, extract (hold) | Space | South button |
| Health potion | 1 | D-pad left |
| Antidote | 2 | D-pad right |
| Ping | F | West button |
| Full map (hold) | Tab | Select |
| Zoom | Mouse wheel | D-pad up and down |
| Inventory | I | |
| Pause menu | Esc | |
| Effects audition (development build only) | F1 | |

Mouse clicks on a panel act on the panel, not the dungeon. Offline, the pause menu pauses the
round; online it does not. Leaving a round from the pause menu counts as a death.

## Playing with other people

From the Prepare screen choose **Play online…**:

* **Host** a round on a port (default 7770). Players who join fill teams of three; empty teams can
  be filled with bots. The host starts the round from the lobby.
* **Join** with `address:port` (the last one is remembered). Your character and gear come with you;
  your profile is settled on your own machine from the round's result.
* **Dedicated server**: `DungeonGame.exe -batchmode -nographics -server -port 7770` (optional
  `-bind <address>`, `-autostart <seconds>`, `-minplayers <n>`, `-botteams <n>`).

Over the internet the host must forward the UDP port; there is no relay or matchmaking service yet.
A lost connection rejoins automatically within 60 seconds.

## Your progress

Your profile (characters, stash, silver, materials, account level) is saved to
`%USERPROFILE%\AppData\LocalLow\kcopelandmgmt\Dungeon Game\profile.dgpf` after every round and every
stash or vendor change, with one backup (`.bak`). Delete both to start over.

## Settings

Master, music and effects volume, and fullscreen or windowed, are in Settings on the main menu and
remembered between sessions. `-mute` on the command line silences the game entirely.
