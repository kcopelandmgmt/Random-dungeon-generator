#!/usr/bin/env bash
# One lane for the Unity editor: every headless run on the project goes through here and waits
# for its turn. Usage (from the repository root, Git Bash):
#   bash Tools/Build/unity-batch.sh compile
#   bash Tools/Build/unity-batch.sh method <Namespace.Type.StaticMethod>
#   bash Tools/Build/unity-batch.sh import "<path to .unitypackage>"
#   bash Tools/Build/unity-batch.sh tests <EditMode|PlayMode>
#   bash Tools/Build/unity-batch.sh build-win            # a Windows player into Builds/Win
#   bash Tools/Build/unity-batch.sh smoke                # run that player muted and minimised for its 30 s scripted test; prints smoke-result.json
# Exit code 0 means Unity finished without compiler errors (and, for tests, without failures).
set -u
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Unity.exe"
PROJECT="${UNITY_PROJECT:-D:/unity dungeon game/dungeongame}"
PROJECT_POSIX="$(echo "$PROJECT" | sed -E 's#^([A-Za-z]):#/\L\1#')"
LOGS="$PROJECT_POSIX/Logs"
mkdir -p "$LOGS"
LOCK="$LOGS/.claude-unity-lane.lock"   # Logs/, not Temp/: Temp only exists while Unity runs
cmd="${1:-compile}"; shift || true

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
  echo "unity-batch: a Unity editor already has this project open (Temp/UnityLockfile); close it first" >&2; exit 3
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
    platform="${1:-PlayMode}"
    results="$LOGS/tests-$platform.xml"; rm -f "$results"
    "$UNITY" -batchmode -projectPath "$PROJECT" -runTests -testPlatform "$platform" -testResults "$results" -logFile "$log"
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
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -executeMethod DungeonGen.Unity.Editor.PlayerBuild.Windows -logFile "$log" ;;
  smoke)
    exe="$PROJECT_POSIX/Builds/Win/DungeonGame.exe"; rm -f "$PROJECT_POSIX/Builds/Win/smoke-result.json"
    [ -f "$exe" ] || { echo "unity-batch: no player at $exe; run build-win first" >&2; exit 2; }
    "$exe" -mute -minimized -autotest -shots "$PROJECT/Logs/player-shots" -logFile "$PROJECT/Logs/player.log" & pid=$!
    for _ in $(seq 1 120); do kill -0 $pid 2>/dev/null || break; sleep 1; done
    if kill -0 $pid 2>/dev/null; then echo "unity-batch: smoke test hung; killing the player" >&2; kill -9 $pid 2>/dev/null; taskkill //F //PID $pid >/dev/null 2>&1; code=1; else wait $pid; code=$?; fi
    cat "$PROJECT_POSIX/Builds/Win/smoke-result.json" 2>/dev/null || { echo "unity-batch: no smoke-result.json written (log: $PROJECT/Logs/player.log)"; code=1; }
    echo "unity-batch: smoke finished with exit $code"; exit $code ;;
  *) echo "unknown command $cmd" >&2; exit 2 ;;
esac
code=$?
errors=$(grep -E "error CS" "$log" | sed -E 's/^.*Random-dungeon-generator.//' | sort -u)
if [ -n "$errors" ]; then echo "$errors"; echo "unity-batch: compiler errors (log: $log)"; exit 1; fi
grep -E "Dungeon Gen:|DungeonGen:|Exception|Aborting batchmode" "$log" | grep -v "PackageCache" | head -20
echo "unity-batch: $cmd finished with exit $code (log: $log)"
exit $code
