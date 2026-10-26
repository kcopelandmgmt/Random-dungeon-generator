#!/usr/bin/env bash
# One lane for the Unity editor: every headless run on the project goes through here and waits
# for its turn. Usage (from the repository root, Git Bash):
#   bash Tools/Build/unity-batch.sh compile
#   bash Tools/Build/unity-batch.sh method <Namespace.Type.StaticMethod>
#   bash Tools/Build/unity-batch.sh import "<path to .unitypackage>"
#   bash Tools/Build/unity-batch.sh tests <EditMode|PlayMode>
#   bash Tools/Build/unity-batch.sh build-win            # the release (non-development) Windows player into Builds/Win
#   bash Tools/Build/unity-batch.sh build-win-dev        # the development Windows player (practice assembly) into Builds/WinDev
#   bash Tools/Build/unity-batch.sh smoke                # run the dev player muted and minimised for its 30 s scripted test; prints smoke-result.json
#   bash Tools/Build/unity-batch.sh smoke-release        # run the release player muted and minimised: menu, a round, leave, summary; prints smoke-release-result.json
#   bash Tools/Build/unity-batch.sh smoke-net [port]     # two dev players muted and minimised over 127.0.0.1 (default port 7770): -autohost and -autojoin; prints net-host-result.json and net-client-result.json
# Exit code 0 means Unity finished without compiler errors (and, for tests, without failures).
set -u
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Unity.exe"
PROJECT="${UNITY_PROJECT:-D:/unity dungeon game/dungeongame}"
PROJECT_POSIX="$(echo "$PROJECT" | sed -E 's#^([A-Za-z]):#/\L\1#')"
LOGS="$PROJECT_POSIX/Logs"
mkdir -p "$LOGS"
LOCK="$LOGS/.claude-unity-lane.lock"   # Logs/, not Temp/: Temp only exists while Unity runs
cmd="${1:-compile}"; shift || true

# The player smoke test needs no editor and no lane: run it before the lock.
if [ "$cmd" = "smoke" ]; then
    exe="$PROJECT_POSIX/Builds/WinDev/DungeonGame.exe"; rm -f "$PROJECT_POSIX/Builds/WinDev/smoke-result.json"
    [ -f "$exe" ] || { echo "unity-batch: no player at $exe; run build-win-dev first" >&2; exit 2; }
    "$exe" -mute -minimized -autotest -shots "$PROJECT/Logs/player-shots" -logFile "$PROJECT/Logs/player.log" & pid=$!
    for _ in $(seq 1 120); do kill -0 $pid 2>/dev/null || break; sleep 1; done
    if kill -0 $pid 2>/dev/null; then echo "unity-batch: smoke test hung; killing the player" >&2; kill -9 $pid 2>/dev/null; taskkill //F //PID $pid >/dev/null 2>&1; code=1; else wait $pid; code=$?; fi
    cat "$PROJECT_POSIX/Builds/WinDev/smoke-result.json" 2>/dev/null || { echo "unity-batch: no smoke-result.json written (log: $PROJECT/Logs/player.log)"; code=1; }
    echo "unity-batch: smoke finished with exit $code"; exit $code
fi
# The release player's smoke test: a throwaway profile under Logs/, never the real save.
if [ "$cmd" = "smoke-release" ]; then
    exe="$PROJECT_POSIX/Builds/Win/DungeonGame.exe"; rm -f "$PROJECT_POSIX/Builds/Win/smoke-release-result.json"
    [ -f "$exe" ] || { echo "unity-batch: no player at $exe; run build-win first" >&2; exit 2; }
    rm -rf "$LOGS/release-profile"; mkdir -p "$LOGS/release-profile"
    "$exe" -mute -minimized -releasesmoke -profile "$PROJECT/Logs/release-profile/profile.dgpf" -shots "$PROJECT/Logs/release-shots" -logFile "$PROJECT/Logs/player-release.log" & pid=$!
    for _ in $(seq 1 240); do kill -0 $pid 2>/dev/null || break; sleep 1; done
    if kill -0 $pid 2>/dev/null; then echo "unity-batch: release smoke test hung; killing the player" >&2; kill -9 $pid 2>/dev/null; taskkill //F //PID $pid >/dev/null 2>&1; code=1; else wait $pid; code=$?; fi
    cat "$PROJECT_POSIX/Builds/Win/smoke-release-result.json" 2>/dev/null || { echo "unity-batch: no smoke-release-result.json written (log: $PROJECT/Logs/player-release.log)"; code=1; }
    echo "unity-batch: smoke-release finished with exit $code"; exit $code
fi
# The network smoke test: two development players on this machine, a host (-autohost) and a client (-autojoin), over 127.0.0.1 only
# (no firewall prompt), each with a throwaway profile under Logs/; both must exit 0. No lane needed.
if [ "$cmd" = "smoke-net" ]; then
    dir="$PROJECT_POSIX/Builds/WinDev"; exe="$dir/DungeonGame.exe"; port="${1:-7770}"
    rm -f "$dir/net-host-result.json" "$dir/net-client-result.json"
    [ -f "$exe" ] || { echo "unity-batch: no player at $exe; run build-win-dev first" >&2; exit 2; }
    rm -rf "$LOGS/net-host-profile" "$LOGS/net-client-profile" "$LOGS/net-shots"; mkdir -p "$LOGS/net-host-profile" "$LOGS/net-client-profile" "$LOGS/net-shots"
    "$exe" -mute -minimized -autohost -port "$port" -profile "$PROJECT/Logs/net-host-profile/profile.dgpf" -shots "$PROJECT/Logs/net-shots" -logFile "$PROJECT/Logs/player-net-host.log" & hpid=$!
    sleep 4
    "$exe" -mute -minimized -autojoin 127.0.0.1 -port "$port" -profile "$PROJECT/Logs/net-client-profile/profile.dgpf" -shots "$PROJECT/Logs/net-shots" -logFile "$PROJECT/Logs/player-net-client.log" & cpid=$!
    for _ in $(seq 1 260); do if kill -0 $hpid 2>/dev/null || kill -0 $cpid 2>/dev/null; then sleep 1; else break; fi; done
    hung=0
    for p in $hpid $cpid; do if kill -0 $p 2>/dev/null; then hung=1; kill -9 $p 2>/dev/null; taskkill //F //PID $p >/dev/null 2>&1; fi; done
    wait $hpid; hcode=$?; wait $cpid; ccode=$?
    [ $hung = 1 ] && echo "unity-batch: smoke-net hung; killed the players" >&2
    code=0
    cat "$dir/net-host-result.json" 2>/dev/null || { echo "unity-batch: no net-host-result.json (log: $PROJECT/Logs/player-net-host.log)"; code=1; }
    cat "$dir/net-client-result.json" 2>/dev/null || { echo "unity-batch: no net-client-result.json (log: $PROJECT/Logs/player-net-client.log)"; code=1; }
    [ $hung = 0 ] && [ $hcode = 0 ] && [ $ccode = 0 ] || code=1
    echo "unity-batch: smoke-net finished: host exit $hcode, client exit $ccode, overall $code"; exit $code
fi

# Wait for the lane. The editor's own lock file means a GUI editor or another batch run is open.
waited=0
while true; do
  if mkdir "$LOCK" 2>/dev/null; then break; fi
  if [ ! -f "$PROJECT_POSIX/Temp/UnityLockfile" ] && [ $waited -gt 900 ]; then rm -rf "$LOCK"; continue; fi
  sleep 5; waited=$((waited+5))
  if [ $waited -gt 3600 ]; then echo "unity-batch: gave up waiting for the Unity lane after an hour" >&2; exit 2; fi
done
trap 'rm -rf "$LOCK"' EXIT
if [ -f "$PROJECT_POSIX/Temp/UnityLockfile" ]; then
  # A crashed run leaves the editor's lock behind. Only clear it when no Unity process has this project open.
  if powershell -NoProfile -Command "\$p = Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | Where-Object { \$_.CommandLine -match 'dungeongame' }; if (\$p) { exit 1 } else { exit 0 }" >/dev/null 2>&1; then
    echo "unity-batch: removing a stale Temp/UnityLockfile (no Unity process has this project open)" >&2
    rm -f "$PROJECT_POSIX/Temp/UnityLockfile"
  else
    echo "unity-batch: a Unity editor already has this project open (Temp/UnityLockfile); close it first" >&2; exit 3
  fi
fi

stamp="$(date +%Y%m%d-%H%M%S)"
log="$LOGS/batch-$cmd-$stamp.log"
case "$cmd" in
  compile)
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -logFile "$log" ;;
  method)
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -executeMethod "$1" -logFile "$log" ;;
  import)
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -importPackage "$1" -logFile "$log" ;;
  tests)
    platform="${1:-PlayMode}"; filter="${2:-}"   # optional: a test class or name filter (Unity -testFilter, regex), e.g. ShrineTests
    if [ -n "$filter" ]; then
      results="$LOGS/tests-$platform-$(echo "$filter" | tr -c 'A-Za-z0-9' '_').xml"; rm -f "$results"
      "$UNITY" -batchmode -projectPath "$PROJECT" -runTests -testPlatform "$platform" -testFilter "$filter" -testResults "$results" -logFile "$log"
    else
      results="$LOGS/tests-$platform.xml"; rm -f "$results"
      "$UNITY" -batchmode -projectPath "$PROJECT" -runTests -testPlatform "$platform" -testResults "$results" -logFile "$log"
    fi
    code=$?
    if [ -f "$results" ]; then
      total=$(grep -o 'total="[0-9]*"' "$results" | head -1 | tr -dc '0-9'); failed=$(grep -o 'failed="[0-9]*"' "$results" | head -1 | tr -dc '0-9')
      echo "unity-batch: $platform tests total=$total failed=$failed (results: $results)"
      grep -B2 -A12 'result="Failed"' "$results" | grep -E 'test-case|<message>|CDATA' | head -40
      [ "${failed:-1}" = "0" ] && code=0 || code=1
    else
      echo "unity-batch: no results file written; see $log"; code=1
    fi
    grep -E "error CS" "$log" | sort -u | head -20
    exit $code ;;
  build-win)
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -executeMethod DungeonGen.Unity.Editor.PlayerBuild.WindowsRelease -logFile "$log" ;;
  build-win-dev)
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -executeMethod DungeonGen.Unity.Editor.PlayerBuild.WindowsDev -logFile "$log" ;;
  *) echo "unknown command $cmd" >&2; exit 2 ;;
esac
code=$?
errors=$(grep -E "error CS" "$log" | sed -E 's/^.*Random-dungeon-generator.//' | sort -u)
if [ -n "$errors" ]; then echo "$errors"; echo "unity-batch: compiler errors (log: $log)"; exit 1; fi
grep -E "Dungeon Gen:|DungeonGen:|Exception|Aborting batchmode" "$log" | grep -v "PackageCache" | head -20
echo "unity-batch: $cmd finished with exit $code (log: $log)"
exit $code
