using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DungeonGen.Core.Combat;
using UnityEditor;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Dungeon Gen ▸ Auto-assign Ability VFX: gives every ability in the book the effects its shape needs, picked from
    /// the imported Piloto Studio pack by <see cref="VfxMatcher"/>, and saves them in Assets/AbilityVfxLibrary.asset.
    /// Roles: Projectile and Pierce get Projectile + Impact; lasting circles get Zone + Cast; instant circles, Target and
    /// Cone get Impact; Self gets Cast; Dash gets Cast + Impact. A Yes vote in VfxAudition.json (same ability, same role)
    /// always wins, and an ability whose only needed role is covered by a Yes in another role is left alone.
    /// Two abilities rarely share a prefab: each reuse of a path costs score. Zone prefabs are measured so a 1 m radius
    /// zone fits. Writes one line per ability to the console and to Logs/vfx-assignment.txt next to the Assets folder.
    /// Headless: <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.VfxAutoAssign.Run</c>.
    /// </summary>
    public static class VfxAutoAssign
    {
        private const string LibraryPath = "Assets/AbilityVfxLibrary.asset";
        private const string PrefabFolder = "Assets/Piloto Studio";
        private const float ReusePenalty = 9f;
        /// <summary>Log every particle system of every measured zone prefab (reach, size, shape) while tuning the measurement.</summary>
        public static bool Verbose = false;

        [Serializable] private sealed class VoteDto { public string Ability, Prefab, Role, Pile; }
        [Serializable] private sealed class VotesDto { public List<VoteDto> Items = new List<VoteDto>(); }

        [MenuItem("Dungeon Gen/Auto-assign Ability VFX")]
        public static void Run()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var catalogue = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            if (catalogue.Count == 0) { Debug.LogError("VfxAutoAssign: no prefabs under " + PrefabFolder); return; }

            var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AbilityVfxLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var votes = LoadYesVotes(projectRoot);
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<string>();
            var chosenPaths = new List<string>();
            var zonePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int weak = 0;

            foreach (var a in AbilityBook.All)
            {
                var entry = library.GetOrAdd(a.Id);
                entry.Cast = entry.Projectile = entry.Impact = entry.Zone = null;
                entry.ProjectileSpeed = 25f;
                entry.ZoneScalePerMetre = 1f;

                var picked = new Dictionary<VfxRole, string>();
                var notes = new List<string>();
                var yes = votes.Where(v => v.Ability == a.Id).GroupBy(v => v.Role).ToDictionary(g => g.Key, g => g.Last().Prefab);
                foreach (var kv in yes)
                    if (Enum.TryParse(kv.Key, out VfxRole yr) && AssetDatabase.LoadAssetAtPath<GameObject>(kv.Value) != null) { picked[yr] = kv.Value; notes.Add(yr + " voted"); }

                var needed = VfxMatcher.RolesNeeded(a);
                bool coveredByVote = needed.Length == 1 && picked.Count > 0 && !picked.ContainsKey(needed[0]);
                var profile = VfxMatcher.ProfileOf(a);
                foreach (var role in needed)
                {
                    if (picked.ContainsKey(role) || coveredByVote) { if (picked.TryGetValue(role, out var vp)) Use(used, vp); continue; }
                    var pick = Pick(a, role, profile, catalogue, used, out int score);
                    if (pick == null) { notes.Add(role + " NONE"); continue; }
                    if (score < VfxMatcher.FitThreshold) { notes.Add(role + " weak(" + score + ")"); weak++; }
                    picked[role] = pick;
                    Use(used, pick);
                }

                foreach (var kv in picked)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kv.Value);
                    entry.Set(kv.Key, prefab);
                    chosenPaths.Add(kv.Value);
                    if (kv.Key == VfxRole.Zone) zonePaths.Add(kv.Value);
                }
                if (picked.TryGetValue(VfxRole.Projectile, out var projPath)) entry.ProjectileSpeed = VfxMatcher.ProjectileSpeedFor(a, projPath);
                if (picked.TryGetValue(VfxRole.Zone, out var zonePath))
                {
                    float r = MeasureFootprintRadius(AssetDatabase.LoadAssetAtPath<GameObject>(zonePath), out string note);
                    entry.ZoneScalePerMetre = Mathf.Clamp(r > 0.01f ? 1f / r : 1f, 0.1f, 5f);
                    notes.Add($"zone r={r:0.00}m scale={entry.ZoneScalePerMetre:0.###} {note}");
                }

                rows.Add(Row(a, picked, entry, notes));
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            string header = $"VFX assignment: {AbilityBook.All.Count} abilities, {used.Count} distinct prefabs, {weak} weak picks, {votes.Count} Yes votes honoured";
            Directory.CreateDirectory(Path.Combine(projectRoot, "Logs"));
            File.WriteAllText(Path.Combine(projectRoot, "Logs", "vfx-assignment.txt"), header + "\n" + string.Join("\n", rows) + "\n");
            Debug.Log(header);
            foreach (var r in rows) Debug.Log("VFX " + r);

            DumpParticleSettings(projectRoot, chosenPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        // ---- picking ----

        private static void Use(Dictionary<string, int> used, string path, int by = 1) { used.TryGetValue(path, out int n); used[path] = n + by; }

        private static string Pick(AbilityDefinition a, VfxRole role, VfxMatcher.Profile profile, List<string> catalogue, Dictionary<string, int> used, out int rawScore)
        {
            string best = null; float bestTotal = float.NegativeInfinity; rawScore = 0;
            foreach (var p in catalogue)
            {
                int s = VfxMatcher.Score(a, role, p, profile);
                if (s < VfxMatcher.FitThreshold - 6) continue;            // weak picks only when nothing fits
                used.TryGetValue(p, out int n);
                float total = s - n * ReusePenalty + Jitter(a.Id, role, p);
                if (total > bestTotal) { bestTotal = total; best = p; rawScore = s; }
            }
            return best;
        }

        /// <summary>A stable 0..2 point nudge so equal scores split differently per ability instead of all taking the alphabetically first.</summary>
        private static float Jitter(string ability, VfxRole role, string path)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in ability + "|" + role + "|" + path) { h ^= c; h *= 16777619; }
                return (h % 200u) / 100f;
            }
        }

        private static List<VoteDto> LoadYesVotes(string projectRoot)
        {
            try
            {
                string file = Path.Combine(projectRoot, "VfxAudition.json");
                if (!File.Exists(file)) return new List<VoteDto>();
                var v = JsonUtility.FromJson<VotesDto>(File.ReadAllText(file));
                return v?.Items?.Where(i => i.Pile == "yes").ToList() ?? new List<VoteDto>();
            }
            catch (Exception ex) { Debug.LogWarning("VfxAutoAssign: could not read votes: " + ex.Message); return new List<VoteDto>(); }
        }

        // ---- table ----

        private static string Name(GameObject go, bool theme = true)
        {
            if (go == null) return "-";
            string path = AssetDatabase.GetAssetPath(go);
            return (theme ? VfxMatcher.Theme(path) + "/" : "") + Path.GetFileNameWithoutExtension(path);
        }

        private static string Row(AbilityDefinition a, Dictionary<VfxRole, string> picked, AbilityVfxLibrary.Entry e, List<string> notes)
        {
            var sb = new StringBuilder();
            sb.Append($"{a.Id,-24} {a.Name,-18} {a.Shape}{(a.IsZone ? "+zone" : "")}{(a.Element.HasValue ? "/" + a.Element : "")}{(a.Heal > 0f ? "/heal" : "")}");
            foreach (var role in new[] { VfxRole.Cast, VfxRole.Projectile, VfxRole.Impact, VfxRole.Zone })
                if (picked.ContainsKey(role)) sb.Append($" | {role}: {Name(e.Get(role))}");
            if (picked.ContainsKey(VfxRole.Projectile)) sb.Append($" | speed {e.ProjectileSpeed:0}");
            if (picked.ContainsKey(VfxRole.Zone)) sb.Append($" | zoneScale {e.ZoneScalePerMetre:0.###}");
            if (notes.Count > 0) sb.Append("  [" + string.Join("; ", notes) + "]");
            return sb.ToString();
        }

        // ---- measuring ----

        /// <summary>
        /// The horizontal reach of a prefab at its authored scale, in metres: the 90th percentile of live particle distances
        /// from the origin (plus half a particle) over a few moments, or the mesh bounds. Run in the editor on a scratch instance.
        /// </summary>
        public static float MeasureFootprintRadius(GameObject prefab, out string note)
        {
            note = "";
            if (prefab == null) return 0f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                var roots = systems.Where(ps => ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>(true) == null).ToArray();
                float particleReach = 0f; var reach = new List<float>();
                foreach (float t in new[] { 0.6f, 1.5f, 3f })
                {
                    foreach (var r in roots) r.Simulate(t, true, true, true);
                    foreach (var ps in systems)
                    {
                        var buf = new ParticleSystem.Particle[Mathf.Clamp(ps.main.maxParticles, 1, 4096)];
                        int n = ps.GetParticles(buf);
                        bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                        bool meshMode = ps.GetComponent<ParticleSystemRenderer>() != null && ps.GetComponent<ParticleSystemRenderer>().renderMode == ParticleSystemRenderMode.Mesh;
                        float sysMax = 0f, sizeMax = 0f;
                        for (int i = 0; i < n; i++)
                        {
                            var pos = local ? ps.transform.TransformPoint(buf[i].position) : buf[i].position;
                            float size = meshMode ? 0f : buf[i].GetCurrentSize(ps);   // a mesh particle's size is a scale, not a width
                            float d = new Vector2(pos.x, pos.z).magnitude + size * 0.5f;
                            reach.Add(d); sysMax = Mathf.Max(sysMax, d); sizeMax = Mathf.Max(sizeMax, size);
                        }
                        if (Verbose && Mathf.Approximately(t, 1.5f))
                            Debug.Log($"VFXZONE {prefab.name} :: {ps.name} n={n} maxReach={sysMax:0.0} maxSize={sizeMax:0.0} mode={ps.GetComponent<ParticleSystemRenderer>()?.renderMode} space={ps.main.simulationSpace} scaling={ps.main.scalingMode} shape={(ps.shape.enabled ? ps.shape.shapeType + " r=" + ps.shape.radius : "off")} lossy={ps.transform.lossyScale.x:0.00}");
                    }
                }
                if (reach.Count >= 5) { reach.Sort(); particleReach = reach[Mathf.Min(reach.Count - 1, (int)(reach.Count * 0.8f))]; }
                float mesh = 0f;
                foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (rend is ParticleSystemRenderer) continue;
                    var b = rend.bounds;
                    mesh = Mathf.Max(mesh, Mathf.Max(Mathf.Abs(b.center.x) + b.extents.x, Mathf.Abs(b.center.z) + b.extents.z));
                }
                float result = Mathf.Max(particleReach, mesh);
                if (result < 0.05f)
                {
                    var bounds = new Bounds(Vector3.zero, Vector3.zero);
                    foreach (var rend in go.GetComponentsInChildren<ParticleSystemRenderer>(true)) bounds.Encapsulate(rend.bounds);
                    result = Mathf.Max(bounds.extents.x, bounds.extents.z);
                    note = "(renderer bounds)";
                }
                if (result < 0.05f) note = "(unmeasured)";
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---- lifetime check ----

        /// <summary>For each chosen prefab: how many particle systems, how many loop, the longest duration and lifetime, and anything that is not a particle system.</summary>
        private static void DumpParticleSettings(string projectRoot, List<string> paths)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prefab | systems | looping | maxDuration | maxStartLifetime | otherComponents");
            int looping = 0, scripts = 0;
            foreach (var path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                int loops = systems.Count(ps => ps.main.loop);
                float dur = systems.Length == 0 ? 0f : systems.Max(ps => ps.main.duration);
                float life = systems.Length == 0 ? 0f : systems.Max(ps => ps.main.startLifetime.constantMax);
                var others = prefab.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && !(c is Transform) && !(c is ParticleSystem) && !(c is ParticleSystemRenderer) && !(c is MeshRenderer) && !(c is MeshFilter) && !(c is TrailRenderer) && !(c is LineRenderer) && !(c is SkinnedMeshRenderer))
                    .Select(c => c.GetType().Name).Distinct().ToList();
                if (loops > 0) looping++;
                if (others.Any(o => o != "Light" && o != "AudioSource")) scripts++;
                sb.AppendLine($"{VfxMatcher.Theme(path)}/{Path.GetFileNameWithoutExtension(path)} | {systems.Length} | {loops} | {dur:0.0} | {life:0.0} | {string.Join(",", others)}");
            }
            File.WriteAllText(Path.Combine(projectRoot, "Logs", "vfx-particle-dump.txt"), sb.ToString());
            Debug.Log($"VFX particle dump: {paths.Count} prefabs, {looping} with a looping system, {scripts} with extra components (Logs/vfx-particle-dump.txt)");
            foreach (var line in sb.ToString().Split('\n').Take(200)) if (line.Length > 0) Debug.Log("VFXDUMP " + line);
        }
    }
}
