using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonGen.Core.Combat;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The effects audition: for every ability in the game, a list of candidate prefabs from the imported effect
    /// packs that fit its shape and element; cast the current one as often as you like at the mouse, then file it
    /// under Yes, No or Maybe and move to the next. Votes persist in VfxAudition.json next to the Assets folder;
    /// a Yes also becomes the ability's prefab in the <see cref="AbilityVfxLibrary"/> for that role, so the game
    /// picks it up at once. Dev only: it casts straight into the in-process server with dev casting on, so there
    /// are no cooldowns, costs or spec limits. Prefab discovery needs the editor's AssetDatabase.
    ///
    /// Keys (while the panel is open): C cast (hold to repeat) · Y / N / M file it · [ ] previous / next candidate ·
    /// , . previous / next ability · T cycle the role (cast, projectile, impact, zone) · H show every prefab ·
    /// F1 hide / show the panel. WASD, mouse aim and the normal ability keys keep working.
    /// </summary>
    public sealed class VfxAudition : MonoBehaviour
    {
        public PracticeMode Practice;
        public AbilityEffects Effects;
        public AbilityVfxLibrary Library;
        public TopDownCamera Camera;
        public DungeonBuilder Builder;
        [Tooltip("Project folder searched for effect prefabs (editor only).")]
        public string PrefabFolder = "Assets/Piloto Studio";
        [Tooltip("File next to the Assets folder that remembers every vote.")]
        public string VotesFile = "VfxAudition.json";
        public int LocalPlayerId = 1;
        public bool Open = true;
        [Tooltip("Seconds between casts while C is held.")]
        public float RepeatSeconds = 0.3f;
        [Tooltip("Abilities with fewer matching candidates than this show the whole catalogue instead.")]
        public int MinCandidates = 6;

        [Serializable] private sealed class Vote { public string Ability, Prefab, Role, Pile; }
        [Serializable] private sealed class Votes { public List<Vote> Items = new List<Vote>(); }

        private List<AbilityDefinition> _abilities = new List<AbilityDefinition>();
        private int _ability;
        private List<string> _catalogue = new List<string>();      // asset paths
        private List<string> _candidates = new List<string>();
        private int _index;
        private bool _showAll;
        private VfxRole _role;
        private string _filter = "";
        private Votes _votes = new Votes();
        private readonly Dictionary<string, GameObject> _loaded = new Dictionary<string, GameObject>();
        private float _nextCast;
        private string _lastResult = "";
        private Vector2 _scroll;

        private AbilityDefinition Current => _abilities.Count > 0 ? _abilities[Mathf.Clamp(_ability, 0, _abilities.Count - 1)] : null;
        private string CurrentPrefabPath => _candidates.Count > 0 ? _candidates[Mathf.Clamp(_index, 0, _candidates.Count - 1)] : null;

        private void OnEnable()
        {
            _abilities = AbilityBook.All.OrderBy(a => a.Spec).ThenBy(a => (int)a.Kind).ToList();
            LoadCatalogue();
            LoadVotes();
            Select(0);
            if (Effects != null) Effects.Override = OverrideFor;
        }

        private void OnDisable() { if (Effects != null && Effects.Override == (Func<string, VfxRole, GameObject>)OverrideFor) Effects.Override = null; }

        private GameObject OverrideFor(string ability, VfxRole role)
        {
            var cur = Current;
            if (!Open || cur == null || ability != cur.Id) return null;
            // While auditioning one role the other roles stay silent, so what you see is only the candidate.
            return role == _role ? Load(CurrentPrefabPath) : null;
        }

        // ---- catalogue and candidates ----

        private void LoadCatalogue()
        {
            _catalogue.Clear();
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.IsValidFolder(PrefabFolder))
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
                    _catalogue.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            _catalogue.Sort(StringComparer.OrdinalIgnoreCase);
#endif
        }

        private GameObject Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_loaded.TryGetValue(path, out var go)) return go;
#if UNITY_EDITOR
            go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#endif
            _loaded[path] = go;
            return go;
        }

        /// <summary>The role a first pass auditions for each shape: what the ability most needs to be seen as.</summary>
        private static VfxRole DefaultRole(AbilityDefinition a)
        {
            switch (a.Shape)
            {
                case AbilityShape.Projectile: case AbilityShape.Pierce: return VfxRole.Projectile;
                case AbilityShape.Circle: return a.IsZone ? VfxRole.Zone : VfxRole.Impact;
                case AbilityShape.Target: return VfxRole.Impact;
                default: return VfxRole.Cast;
            }
        }

        private static readonly Dictionary<DamageType, string[]> ElementWords = new Dictionary<DamageType, string[]>
        {
            { DamageType.Fire, new[] { "Fire", "Flame", "Lava", "Burn", "Ember", "Magma" } },
            { DamageType.Frost, new[] { "Frost", "Ice", "Snow", "Freez", "Cold" } },
            { DamageType.Lightning, new[] { "Shock", "Lightning", "Thunder", "Tesla", "Electric", "Volt" } },
            { DamageType.Poison, new[] { "Poison", "Acid", "Toxic", "Goo", "Venom" } },
            { DamageType.Shadow, new[] { "Dark", "Void", "Shadow", "Crimson", "Blood", "Curse" } },
            { DamageType.Physical, new string[0] },
        };

        private static string[] ShapeWords(AbilityDefinition a)
        {
            switch (a.Shape)
            {
                case AbilityShape.Projectile: return new[] { "Projectile", "Bolt", "Arrow", "Shot", "Missile", "Ball", "Spear", "Orb", "Bullet", "Lance", "Dart", "Throw" };
                case AbilityShape.Pierce: return new[] { "Beam", "Ray", "Laser", "Pierce", "Arrow", "Spear", "Lance", "Line", "Projectile", "Shot" };
                case AbilityShape.Circle: return a.IsZone
                    ? new[] { "Area", "AOE", "Floor", "Field", "Pool", "Rain", "Ring", "Zone", "Puddle", "Circle", "Ground", "Totem", "Tower", "Trap", "Nova", "Aura", "Healing" }
                    : new[] { "Explosion", "Blast", "Nova", "Burst", "Impact", "AOE", "Area", "Smash", "Slam", "Crack", "Quake" };
                case AbilityShape.Cone: return new[] { "Slash", "Cone", "Breath", "Wave", "Swing", "Sweep", "Claw", "Cleave", "Fan", "Spray", "Flamethrower" };
                case AbilityShape.Dash: return new[] { "Dash", "Trail", "Blink", "Teleport", "Smoke", "Step", "Charge", "Rush", "Leap", "Warp" };
                case AbilityShape.Self: return new[] { "Aura", "Shield", "Buff", "Guard", "Barrier", "Armor", "Armour", "Glow", "Power", "Rage" };
                default: return a.Heal > 0f ? new[] { "Heal", "Holy", "Cure", "Bless", "Light", "Fairy", "Nature", "Restore" } : new[] { "Impact", "Hit", "Strike", "Stab", "Slash", "Bash", "Smash", "Crack" };
            }
        }

        private List<string> CandidatesFor(AbilityDefinition a)
        {
            var words = ShapeWords(a).ToList();
            if (a.Kind == AbilityKind.Ultimate) words.AddRange(new[] { "Meteor", "Storm", "Nova", "Rain", "Giant", "Mega", "Ultimate", "Explosion", "Apocalypse" });
            var element = a.Element.HasValue && ElementWords.TryGetValue(a.Element.Value, out var ew) ? ew : new string[0];
            var heal = a.Heal > 0f ? new[] { "Heal", "Holy", "Nature", "Fairy", "Light", "Bees" } : new string[0];
            bool Has(string s, string w) => s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
            var scored = new List<(string path, int score)>();
            foreach (var p in _catalogue)
            {
                string file = Path.GetFileNameWithoutExtension(p);
                int score = 0;
                if (words.Any(w => Has(file, w))) score += 2;
                if (element.Length > 0 && element.Any(w => Has(p, w))) score += 1;
                if (heal.Length > 0 && heal.Any(w => Has(p, w))) score += 1;
                if (score >= 2 || (element.Length > 0 && score >= 1 && words.Count == 0)) scored.Add((p, score));
            }
            return scored.OrderByDescending(s => s.score).ThenBy(s => s.path, StringComparer.OrdinalIgnoreCase).Select(s => s.path).ToList();
        }

        private void Select(int abilityIndex)
        {
            if (_abilities.Count == 0) return;
            _ability = (abilityIndex + _abilities.Count) % _abilities.Count;
            _role = DefaultRole(Current);
            Rebuild();
        }

        private void Rebuild()
        {
            var a = Current; if (a == null) return;
            var list = _showAll ? new List<string>(_catalogue) : CandidatesFor(a);
            if (!_showAll && list.Count < MinCandidates) list = new List<string>(_catalogue);
            if (!string.IsNullOrEmpty(_filter)) list = list.Where(p => p.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            // Unvoted first, then maybes, then yeses, then noes, so the list drains as you work.
            int Rank(string p) { var v = VoteFor(a.Id, p); return v == null ? 0 : v.Pile == "maybe" ? 1 : v.Pile == "yes" ? 2 : 3; }
            _candidates = list.OrderBy(Rank).ToList();
            _index = 0;
            _lastResult = "";
        }

        // ---- votes ----

        private string VotesPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", VotesFile));

        private void LoadVotes()
        {
            try { if (File.Exists(VotesPath)) _votes = JsonUtility.FromJson<Votes>(File.ReadAllText(VotesPath)) ?? new Votes(); }
            catch (Exception ex) { Debug.LogWarning("VfxAudition: could not read votes: " + ex.Message); _votes = new Votes(); }
        }

        private void SaveVotes()
        {
            try { File.WriteAllText(VotesPath, JsonUtility.ToJson(_votes, true)); }
            catch (Exception ex) { Debug.LogWarning("VfxAudition: could not save votes: " + ex.Message); }
        }

        private Vote VoteFor(string ability, string prefab) => _votes.Items.FirstOrDefault(v => v.Ability == ability && v.Prefab == prefab && v.Role == _role.ToString());

        private void File_(string pile)
        {
            var a = Current; var p = CurrentPrefabPath; if (a == null || p == null) return;
            var v = VoteFor(a.Id, p);
            if (v == null) { v = new Vote { Ability = a.Id, Prefab = p, Role = _role.ToString() }; _votes.Items.Add(v); }
            v.Pile = pile;
            SaveVotes();
            if (pile == "yes" && Library != null)
            {
                Library.GetOrAdd(a.Id).Set(_role, Load(p));
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(Library);
                UnityEditor.AssetDatabase.SaveAssets();
#endif
            }
            Next(1);
        }

        private void Next(int step)
        {
            if (_candidates.Count == 0) return;
            _index = (_index + step + _candidates.Count) % _candidates.Count;
            _lastResult = "";
        }

        // ---- casting ----

        private void TryCast()
        {
            var a = Current; if (a == null) return;
            var sim = Practice?.Server?.Server?.Sim;
            if (sim == null || Builder == null) { _lastResult = "no round running"; return; }
            var c = sim.Get(LocalPlayerId);
            if (c == null) { _lastResult = "no local player"; return; }
            Vector3 ground = Vector3.zero;
            if (Camera != null && Camera.Camera != null) Camera.ScreenToGround(Camera.AimScreenPosition, out ground);
            var aim = Builder.WorldToSim(ground);
            var r = sim.Cast(LocalPlayerId, a.Id, aim);
            _lastResult = r.Ok ? $"cast: touched {r.Targets.Count}" : "rejected: " + r.Rejection;
        }

        // ---- panel ----

        private void OnGUI()
        {
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F1) { Open = !Open; ev.Use(); }
            if (!Open || Practice == null || !Practice.Hosting) return;
            var a = Current; if (a == null) return;

            if (ev.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
            {
                bool used = true;
                switch (ev.keyCode)
                {
                    case KeyCode.C: if (Time.time >= _nextCast) { TryCast(); _nextCast = Time.time + RepeatSeconds; } break;
                    case KeyCode.Y: File_("yes"); break;
                    case KeyCode.N: File_("no"); break;
                    case KeyCode.M: File_("maybe"); break;
                    case KeyCode.LeftBracket: Next(-1); break;
                    case KeyCode.RightBracket: Next(1); break;
                    case KeyCode.Comma: Select(_ability - 1); break;
                    case KeyCode.Period: Select(_ability + 1); break;
                    case KeyCode.T: _role = (VfxRole)(((int)_role + 1) % 4); Rebuild(); break;
                    case KeyCode.H: _showAll = !_showAll; Rebuild(); break;
                    default: used = false; break;
                }
                if (used) ev.Use();
            }

            float w = 400f, x = Screen.width - w - 12f, y = 60f;
            GUILayout.BeginArea(new Rect(x, y, w, 420f), GUI.skin.box);
            int voted = _abilities.Count(ab => _votes.Items.Any(v => v.Ability == ab.Id && v.Pile == "yes"));
            GUILayout.Label($"EFFECTS AUDITION   ability {_ability + 1}/{_abilities.Count}   with a Yes: {voted}   (F1 hides)");
            GUILayout.Label($"{a.Spec} · {a.Name}  [{a.Kind}, {a.Shape}{(a.IsZone ? " zone" : "")}{(a.Element.HasValue ? ", " + a.Element : "")}{(a.Heal > 0f ? ", heals" : "")}]");
            GUILayout.Label(a.Text, new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Italic });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< ability (,)")) Select(_ability - 1);
            if (GUILayout.Button("ability (.) >")) Select(_ability + 1);
            if (GUILayout.Button($"role: {_role} (T)")) { _role = (VfxRole)(((int)_role + 1) % 4); Rebuild(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            string path = CurrentPrefabPath;
            string name = path != null ? Path.GetFileNameWithoutExtension(path) : "(no prefabs found: import the effect pack, then re-enter Play)";
            string folder = path != null ? Path.GetFileName(Path.GetDirectoryName(path)) : "";
            var vote = path != null ? VoteFor(a.Id, path) : null;
            GUILayout.Label($"candidate {(_candidates.Count == 0 ? 0 : _index + 1)}/{_candidates.Count}{(_showAll ? " (all)" : "")}   {(vote != null ? "filed: " + vote.Pile.ToUpperInvariant() : "unfiled")}");
            GUILayout.Label(name, new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold });
            GUILayout.Label(folder, new GUIStyle(GUI.skin.label) { fontSize = 11 });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< prev ([)")) Next(-1);
            if (GUILayout.Button("CAST (C, hold)")) TryCast();
            if (GUILayout.Button("next (]) >")) Next(1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.color = new Color(0.6f, 1f, 0.6f); if (GUILayout.Button("YES (Y)")) File_("yes");
            GUI.color = new Color(1f, 0.95f, 0.5f); if (GUILayout.Button("MAYBE (M)")) File_("maybe");
            GUI.color = new Color(1f, 0.6f, 0.6f); if (GUILayout.Button("NO (N)")) File_("no");
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_lastResult)) GUILayout.Label(_lastResult);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("filter", GUILayout.Width(40f));
            string f = GUILayout.TextField(_filter);
            if (f != _filter) { _filter = f; Rebuild(); }
            if (GUILayout.Button(_showAll ? "fitting only (H)" : "show all (H)", GUILayout.Width(120f))) { _showAll = !_showAll; Rebuild(); }
            GUILayout.EndHorizontal();

            var yes = _votes.Items.Where(v => v.Ability == a.Id && v.Pile == "yes").Select(v => Path.GetFileNameWithoutExtension(v.Prefab) + " (" + v.Role + ")").ToList();
            var maybe = _votes.Items.Count(v => v.Ability == a.Id && v.Pile == "maybe");
            var no = _votes.Items.Count(v => v.Ability == a.Id && v.Pile == "no");
            GUILayout.Label($"this ability: yes {yes.Count}, maybe {maybe}, no {no}");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(70f));
            foreach (var y1 in yes) GUILayout.Label("  ✓ " + y1);
            GUILayout.EndScrollView();
            GUILayout.Label("Aim with the mouse. Target abilities need a body under the cursor; ally ones need the ally bot.", new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true });
            GUILayout.EndArea();
        }
    }
}
