using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Turns the Meshy downloads under <c>Assets/Generated/models/&lt;slug&gt;/</c> into game prefabs. The postprocessor keeps
    /// every model in that folder on the same import settings (file scale, no imported materials, no cameras or lights,
    /// Generic rig for rigged and animation files with the rigged file as the avatar source, except the rigs
    /// <see cref="AnimationSetup"/> made Humanoid, which stay Humanoid; medium mesh compression). <see cref="Build"/> then, per
    /// slug: configures clips (idle/walk/run loop), makes a URP Lit material from the base-colour PNG, builds an
    /// AnimatorController from code for rigged models (the animation packs' role controller for Humanoid rigs, the Meshy clips
    /// otherwise), measures the model and writes a normalised prefab to
    /// <c>Assets/Generated/Prefabs/&lt;slug&gt;.prefab</c> (feet at y = 0, size per <see cref="Specs"/>), fills the
    /// <see cref="AvatarVisualLibrary"/> and writes a report to <c>Logs/model-import.txt</c>.
    /// Run headless: <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.GeneratedModelImporter.Build</c>.
    /// </summary>
    public sealed class GeneratedModelImporter : AssetPostprocessor
    {
        public const string ModelsRoot = "Assets/Generated/models";
        public const string PrefabsRoot = "Assets/Generated/Prefabs";
        public const string MaterialsRoot = "Assets/Generated/Prefabs/Materials";
        public const string AnimatorsRoot = "Assets/Generated/Animators";
        public const string LibraryPath = "Assets/Generated/Prefabs/AvatarVisualLibrary.asset";
        public static readonly string[] HeroActions = { "idle", "walk", "run", "attack", "cast", "hit", "death" };
        public static readonly string[] LoopActions = { "idle", "walk", "run" };

        public enum Kind { Hero, Mob, Boss, Prop }
        public enum Fit { Height, MaxDim, Box, Footprint, FloorTile, Door }

        public sealed class Spec
        {
            public string Slug, Key; public Kind Kind; public Fit Fit; public Vector3 Size; public float Hover; public bool Rigged;
            public Spec(string slug, string key, Kind kind, Fit fit, Vector3 size, bool rigged = false, float hover = 0f) { Slug = slug; Key = key; Kind = kind; Fit = fit; Size = size; Rigged = rigged; Hover = hover; }
        }

        private static Spec Hero(string slug, string key, float h) => new Spec(slug, key, Kind.Hero, Fit.Height, new Vector3(0, h, 0), true);
        private static Spec Mob(string slug, string key, float h, bool rigged = true, Fit fit = Fit.Height, float hover = 0f) => new Spec(slug, key, Kind.Mob, fit, new Vector3(h, h, h), rigged, hover);
        private static Spec Boss(string slug, string key, float h, bool rigged = true, Fit fit = Fit.Height, float hover = 0f) => new Spec(slug, key, Kind.Boss, fit, new Vector3(h, h, h), rigged, hover);
        private static Spec Prop(string slug, Fit fit, Vector3 size, float hover = 0f) => new Spec(slug, null, Kind.Prop, fit, size, false, hover);

        /// <summary>Names that reuse another entry's model at a scale: (combatant label, source key, scale).</summary>
        public static readonly (string alias, string source, float scale)[] Aliases = { ("Revenant Champion", "Hollow Knight", 1.6f) };

        /// <summary>Every slug and its target size in metres. Heights for walkers; the longest side for crawlers and floaters; exact boxes for tiles.</summary>
        public static readonly Spec[] Specs =
        {
            Hero("warden", "Warden", 1.9f), Hero("reaver", "Reaver", 1.85f), Hero("marksman", "Marksman", 1.8f),
            Hero("elementalist", "Elementalist", 1.8f), Hero("mender", "Mender", 1.75f), Hero("shade", "Shade", 1.75f),

            Boss("iron_juggernaut", "Iron Juggernaut", 4.0f), Boss("ashen_colossus", "Ashen Colossus", 4.6f), Boss("glasswright", "Glasswright", 3.4f),
            Boss("plague_herald", "Plague Herald", 3.4f), Boss("the_sovereign_below", "The Sovereign Below", 5.0f),
            Boss("hive_matron", "Hive Matron", 4.5f, false, Fit.MaxDim), Boss("bone_choir", "Bone Choir", 3.8f, false),
            Boss("hound_of_vesh", "Hound of Vesh", 4.0f, false, Fit.MaxDim), Boss("lantern_wraith", "Lantern Wraith", 3.4f, false, Fit.Height, 0.5f),

            Mob("bandit", "Bandit", 1.8f), Mob("hollow_knight", "Hollow Knight", 2.1f), Mob("frostbound", "Frostbound", 2.0f),
            Mob("storm_acolyte", "Storm Acolyte", 1.85f), Mob("gravewalker", "Gravewalker", 1.8f),
            Mob("skitterer", "Skitterer", 1.1f, false, Fit.MaxDim), Mob("ember_wisp", "Ember Wisp", 0.9f, false, Fit.MaxDim, 0.9f),
            Mob("carrion_crawler", "Carrion Crawler", 1.8f, false, Fit.MaxDim),

            Prop("dungeon_floor_tile", Fit.FloorTile, new Vector3(2f, 0.25f, 2f)),
            Prop("dungeon_wall_block", Fit.Box, new Vector3(2f, 1.8f, 2f)),
            Prop("wooden_door", Fit.Door, new Vector3(2f, 1.8f, 0f)),
            Prop("vault_door", Fit.Door, new Vector3(2f, 1.8f, 0f)),
            Prop("stone_pillar", Fit.Box, new Vector3(1.5f, 2.0f, 1.5f)),
            // A run of stones along its own Z (raw 105 x 197 x 829): fill the cell along the run and keep it a wall, not a 2 m slab; DungeonBuilder turns it to the run.
            Prop("low_wall", Fit.Box, new Vector3(0.5f, 0.8f, 2f)),
            Prop("treasure_chest", Fit.MaxDim, new Vector3(1.1f, 1.1f, 1.1f)),
            Prop("boss_chest", Fit.MaxDim, new Vector3(1.6f, 1.6f, 1.6f)),
            Prop("shrine", Fit.Height, new Vector3(0f, 1.8f, 0f)),
            Prop("dungeon_key", Fit.MaxDim, new Vector3(0.6f, 0.6f, 0.6f), 0.6f),
            Prop("extract_portal", Fit.Height, new Vector3(0f, 3.2f, 0f)),
            Prop("spike_trap", Fit.Footprint, new Vector3(1f, 0.3f, 1f)),
            Prop("gas_vent", Fit.Footprint, new Vector3(1f, 0.3f, 1f)),
            Prop("lava_pool", Fit.Footprint, new Vector3(1f, 0.08f, 1f)),
            Prop("brazier", Fit.Height, new Vector3(0f, 1.4f, 0f)),
        };

        /// <summary>Base colour multipliers that pull a few loud textures toward the dungeon palette.</summary>
        private static readonly Dictionary<string, Color> Tints = new Dictionary<string, Color> { { "dungeon_wall_block", new Color(0.85f, 0.8f, 0.74f) } };

        private static readonly HashSet<string> Glowing = new HashSet<string> { "lava_pool", "ember_wisp", "extract_portal", "brazier", "lantern_wraith" };

        public static string Folder(string slug) => $"{ModelsRoot}/{slug}";
        public static string PrefabPath(string slug) => $"{PrefabsRoot}/{slug}.prefab";
        public static string Anim(string slug, string action) => $"{Folder(slug)}/{slug}_anim_{action}.fbx";

        // ------------------------------------------------------------------ postprocessor

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsRoot + "/", StringComparison.Ordinal)) return;
            var mi = (ModelImporter)assetImporter;
            ApplyBaseSettings(mi, assetPath);
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ModelsRoot + "/", StringComparison.Ordinal) || !assetPath.EndsWith("_basecolor.png", StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.anisoLevel = 4;
        }

        private static void ApplyBaseSettings(ModelImporter mi, string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            bool rigged = file.EndsWith("_rigged", StringComparison.Ordinal);
            bool anim = file.Contains("_anim_");
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importBlendShapes = false;
            mi.meshCompression = ModelImporterMeshCompression.Medium;   // the rigged meshes were most of the player's mesh bytes
            if (rigged || anim)
            {
                mi.importAnimation = anim;
                mi.optimizeGameObjects = false;
                if (anim)
                {
                    string slug = file.Substring(0, file.IndexOf("_anim_", StringComparison.Ordinal));
                    var avatar = AssetDatabase.LoadAllAssetsAtPath($"{Folder(slug)}/{slug}_rigged.fbx").OfType<Avatar>().FirstOrDefault();
                    // A rig AnimationSetup made Humanoid takes Humanoid clips; the rest stay Generic.
                    mi.animationType = avatar != null && avatar.isHuman ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                    mi.animationCompression = ModelImporterAnimationCompression.Optimal;   // keyframe reduction
                    if (avatar != null) { mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; mi.sourceAvatar = avatar; }
                    else mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }
                else
                {
                    // AnimationSetup.MakeHumanoid stores a bone mapping in the importer: keep such a rig Humanoid on every reimport.
                    bool human = AnimationSetup.IsHumanoidSlug(file.Substring(0, file.Length - "_rigged".Length)) && mi.humanDescription.human != null && mi.humanDescription.human.Length > 0;
                    mi.animationType = human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
        }

        // ------------------------------------------------------------------ batch build

        private sealed class Row
        {
            public string Slug, Kind, Rig = "-", Clips = "-", Raw = "-", Final = "-", Scale = "-", Prefab = "-", Notes = "";
        }

        [MenuItem("Dungeon Gen/Build Generated Model Prefabs")]
        public static void Build()
        {
            var rows = new List<Row>();
            bool failed = false;
            try
            {
                EnsureFolder(PrefabsRoot); EnsureFolder(MaterialsRoot); EnsureFolder(AnimatorsRoot);
                var lib = AssetDatabase.LoadAssetAtPath<AvatarVisualLibrary>(LibraryPath);
                if (lib == null) { lib = ScriptableObject.CreateInstance<AvatarVisualLibrary>(); AssetDatabase.CreateAsset(lib, LibraryPath); }
                lib.Entries.Clear();
                lib.Fallback = new AvatarVisualLibrary.Entry { Key = "*" };
                lib.RingMaterial = RingMaterial();
                lib.GhostMaterial = GhostMaterial();

                foreach (var spec in Specs)
                {
                    var row = new Row { Slug = spec.Slug, Kind = spec.Kind.ToString() };
                    rows.Add(row);
                    try
                    {
                        var prefab = BuildOne(spec, row, out float ringRadius);
                        if (prefab != null && spec.Key != null)
                            lib.Entries.Add(new AvatarVisualLibrary.Entry { Key = spec.Key, Prefab = prefab, Scale = 1f, RingRadius = ringRadius, IsBoss = spec.Kind == Kind.Boss });
                    }
                    catch (Exception ex)
                    {
                        row.Notes += "FAILED: " + ex.Message;
                        Debug.LogError($"Dungeon Gen models: {spec.Slug} failed: {ex}");
                        failed = true;
                    }
                }
                // Combatants with no model of their own borrow one: the mid-round roaming elite wears the Hollow Knight, larger.
                foreach (var (alias, source, scale) in Aliases)
                {
                    var src = lib.Entries.Find(e => e.Key == source);
                    if (src != null && lib.Entries.Find(e => e.Key == alias) == null)
                        lib.Entries.Add(new AvatarVisualLibrary.Entry { Key = alias, Prefab = src.Prefab, Scale = scale, RingRadius = src.RingRadius * scale, IsBoss = true });
                }
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                Debug.LogError("Dungeon Gen models: build threw: " + ex);
                failed = true;
            }
            WriteReport(rows);
            Debug.Log($"Dungeon Gen: model import finished, {rows.Count(r => r.Prefab != "-")} prefabs of {rows.Count}{(failed ? ", with failures" : "")}.");
            if (Application.isBatchMode && failed) EditorApplication.Exit(1);
        }

        private static GameObject BuildOne(Spec spec, Row row, out float ringRadius)
        {
            ringRadius = 0.6f;
            string folder = Folder(spec.Slug);
            string tex = $"{folder}/{spec.Slug}_basecolor.png";
            string mesh = $"{folder}/{spec.Slug}.fbx";
            string rigged = $"{folder}/{spec.Slug}_rigged.fbx";
            bool useRig = spec.Rigged && File.Exists(rigged);
            if (spec.Rigged && !useRig) row.Notes += "no rigged fbx, static mesh used; ";
            string source = useRig ? rigged : mesh;
            if (!File.Exists(source)) { row.Notes += "no model file; "; return null; }

            if (File.Exists(tex)) AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceUpdate);
            Reimport(source);

            var clips = new Dictionary<string, AnimationClip>();
            Avatar avatar = null;
            string motionNode = null;
            bool human = false;
            if (useRig)
            {
                avatar = AssetDatabase.LoadAllAssetsAtPath(rigged).OfType<Avatar>().FirstOrDefault();
                human = avatar != null && avatar.isValid && avatar.isHuman;
                row.Rig = avatar == null || !avatar.isValid ? "avatar invalid" : human ? "Humanoid" : "Generic";
                if (!human) motionNode = FindHips(AssetDatabase.LoadAssetAtPath<GameObject>(rigged));
                foreach (var action in HeroActions)
                {
                    string ap = Anim(spec.Slug, action);
                    if (!File.Exists(ap)) continue;
                    var clip = ConfigureClip(ap, spec.Slug, action, motionNode, row);
                    if (clip != null) clips[action] = clip;
                }
                row.Clips = string.Join(",", clips.Keys);
            }

            var material = MaterialFor(spec.Slug, AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
            if (material == null) row.Notes += "no base colour; ";
            // Humanoid rigs play the bought packs (AnimationSetup's role controller); the rest keep their Meshy clips.
            float runSpeed = 0f;
            AnimatorController controller = null;
            if (useRig && human)
            {
                controller = AnimationSetup.BuildRoleController(spec.Slug, out runSpeed, out string summary);
                row.Clips = controller != null ? "pack: " + summary.Split(':')[0] : row.Clips;
                if (controller == null) row.Notes += "no pack controller (" + summary + "), Meshy clips used; ";
            }
            if (controller == null) controller = useRig && clips.ContainsKey("idle") ? BuildController(spec.Slug, clips) : null;
            if (useRig && controller == null) row.Notes += "no idle clip, no animator controller; ";

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (asset == null) { row.Notes += "model did not import; "; return null; }

            GameObject root = null;
            try
            {
                root = new GameObject(spec.Slug);
                GameObject model;
                if (spec.Fit == Fit.Door)
                {
                    model = BuildDoor(spec, root, asset, material, row, !useRig);
                }
                else
                {
                    model = Place(asset, root.transform, "Model", material, !useRig);
                    var raw = Measure(model.transform, root.transform);
                    row.Raw = Fmt(raw.size);
                    var (scale, offset) = Normalise(spec, raw);
                    model.transform.localScale = ToLocal(model.transform.localRotation, scale);
                    model.transform.localPosition = offset;
                    var fin = Measure(model.transform, root.transform);
                    row.Final = Fmt(fin.size) + $" feet {fin.min.y:0.00}";
                    row.Scale = Fmt(scale);
                    ringRadius = Mathf.Clamp(Mathf.Max(fin.size.x, fin.size.z) * 0.5f, 0.45f, 3.5f);
                    if (spec.Kind != Kind.Prop) ringRadius = Mathf.Clamp(spec.Kind == Kind.Hero ? 0.6f : Mathf.Max(fin.size.x, fin.size.z) * 0.45f, 0.45f, 3.5f);
                    if (raw.size.z > raw.size.y * 1.6f && spec.Kind != Kind.Prop && spec.Fit == Fit.Height) row.Notes += "deeper than tall (lying down?); ";
                }

                if (useRig)
                {
                    var anim = model.GetComponent<Animator>();
                    if (anim == null) anim = model.AddComponent<Animator>();
                    anim.avatar = avatar;
                    anim.runtimeAnimatorController = controller;
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    var visual = root.AddComponent<AvatarVisual>();
                    visual.Animator = anim;
                    visual.Humanoid = human && controller != null && AnimationSetup.IsHumanoidSlug(spec.Slug);
                    visual.RunClipSpeed = runSpeed;
                }
                else
                {
                    foreach (var a in model.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);
                    if (spec.Kind != Kind.Prop || spec.Hover > 0f)
                    {
                        var bob = model.AddComponent<IdleBob>();
                        bob.Hover = spec.Hover;
                        bob.Amplitude = spec.Kind == Kind.Prop ? 0.08f : spec.Hover > 0f ? 0.12f : 0.025f;
                        bob.Frequency = spec.Hover > 0f ? 0.5f : 0.9f;
                        bob.SwayDegrees = spec.Kind == Kind.Prop ? 20f : 4f;
                    }
                    if (spec.Kind != Kind.Prop) root.AddComponent<AvatarVisual>();
                }
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = spec.Fit == Fit.FloorTile || spec.Fit == Fit.Footprint ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
                }
                if (spec.Fit == Fit.FloorTile) AddUnderlay(root.transform);
                if (spec.Slug == "brazier") AddLight(root.transform, 1.35f, new Color(1f, 0.62f, 0.3f), 10f, 2.2f);
                if (spec.Slug == "extract_portal") AddLight(root.transform, 1.6f, new Color(0.4f, 1f, 0.6f), 8f, 1.5f);

                var path = PrefabPath(spec.Slug);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                if (!ok || saved == null) { row.Notes += "prefab save failed; "; return null; }
                row.Prefab = path;
                return saved;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Reimport(string path)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return;
            ApplyBaseSettings(mi, path);
            mi.SaveAndReimport();
        }

        /// <summary>Path of the hips bone (or the skin's root bone) from the model root, for the clips' root motion node.</summary>
        private static string FindHips(GameObject model)
        {
            if (model == null) return null;
            Transform hips = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.IndexOf("hips", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("pelvis", StringComparison.OrdinalIgnoreCase) >= 0);
            if (hips == null) hips = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.rootBone).FirstOrDefault(b => b != null);
            if (hips == null) return null;
            return AnimationUtility.CalculateTransformPath(hips, model.transform);
        }

        private static AnimationClip ConfigureClip(string path, string slug, string action, string motionNode, Row row)
        {
            Reimport(path);
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            var defaults = mi.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0) { row.Notes += $"{action}: no take; "; return null; }
            var c = defaults[0];
            c.name = $"{slug}_{action}";
            bool loop = LoopActions.Contains(action);
            c.loopTime = loop;
            c.loopPose = false;
            // Root motion is thrown away (the server moves avatars): with the hips as motion node and XZ not baked, any travel
            // in a clip becomes root motion the Animator discards; height and rotation stay in the pose.
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = true;
            mi.clipAnimations = new[] { c };
            if (!string.IsNullOrEmpty(motionNode)) mi.motionNodeName = motionNode;   // Generic rigs only; a Humanoid clip's root is its body
            mi.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null) { row.Notes += $"{action}: clip missing; "; return null; }
            return clip;
        }

        private static AnimatorController BuildController(string slug, Dictionary<string, AnimationClip> clips)
        {
            string path = $"{AnimatorsRoot}/{slug}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Downed", AnimatorControllerParameterType.Bool);
            var sm = ctrl.layers[0].stateMachine;
            AnimationClip Clip(params string[] names) { foreach (var n in names) if (clips.TryGetValue(n, out var c)) return c; return null; }

            var idle = sm.AddState("Idle", new Vector3(300, 0)); idle.motion = Clip("idle");
            var walk = sm.AddState("Walk", new Vector3(550, -80)); walk.motion = Clip("walk", "run", "idle");
            var run = sm.AddState("Run", new Vector3(550, 80)); run.motion = Clip("run", "walk", "idle");
            var attack = sm.AddState("Attack", new Vector3(300, 220)); attack.motion = Clip("attack", "cast", "idle");
            var cast = sm.AddState("Cast", new Vector3(550, 220)); cast.motion = Clip("cast", "attack", "idle");
            var death = sm.AddState("Death", new Vector3(50, 220)); death.motion = Clip("death", "idle");
            AnimatorState hit = null;
            if (clips.ContainsKey("hit")) { hit = sm.AddState("Hit", new Vector3(800, 220)); hit.motion = clips["hit"]; }
            sm.defaultState = idle;

            AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float duration)
            {
                var t = from.AddTransition(to); t.hasExitTime = false; t.duration = duration; t.hasFixedDuration = true; return t;
            }
            AnimatorStateTransition Any(AnimatorState to, float duration, bool self)
            {
                var t = sm.AddAnyStateTransition(to); t.hasExitTime = false; t.duration = duration; t.hasFixedDuration = true; t.canTransitionToSelf = self; return t;
            }
            void Back(AnimatorState from, float exit)
            {
                var t = from.AddTransition(idle); t.hasExitTime = true; t.exitTime = exit; t.duration = 0.15f; t.hasFixedDuration = true;
            }

            Go(idle, walk, 0.15f).AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");
            Go(idle, run, 0.15f).AddCondition(AnimatorConditionMode.Greater, 3.2f, "Speed");
            Go(walk, run, 0.2f).AddCondition(AnimatorConditionMode.Greater, 3.2f, "Speed");
            Go(walk, idle, 0.2f).AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");
            Go(run, walk, 0.2f).AddCondition(AnimatorConditionMode.Less, 2.8f, "Speed");
            Go(run, idle, 0.2f).AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");

            Any(death, 0.15f, false).AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            Any(death, 0.15f, false).AddCondition(AnimatorConditionMode.If, 0f, "Downed");
            var up = Go(death, idle, 0.3f); up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead"); up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Downed");

            foreach (var (state, trigger) in new[] { (attack, "Attack"), (cast, "Cast") })
            {
                var t = Any(state, 0.06f, true);
                t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Downed");
                Back(state, 0.85f);
            }
            if (hit != null)
            {
                var t = Any(hit, 0.05f, false);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Downed");
                Back(hit, 0.8f);
            }
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        // ------------------------------------------------------------------ prefab pieces

        /// <summary>Meshy's unrigged downloads are Z-up (and in centimetres): <paramref name="zUp"/> stands them up with -90 degrees about X.</summary>
        private static GameObject Place(GameObject asset, Transform parent, string name, Material material, bool zUp)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = zUp ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity; go.transform.localScale = Vector3.one;
            if (material != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                }
            return go;
        }

        /// <summary>A door tile: a floor slab (the floor tile prefab's model) and the door leaf, 2 m wide, both driven by <see cref="DoorVisual"/>.</summary>
        private static GameObject BuildDoor(Spec spec, GameObject root, GameObject asset, Material material, Row row, bool zUp)
        {
            var floorSpec = Specs.First(s => s.Slug == "dungeon_floor_tile");
            var floorAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder(floorSpec.Slug)}/{floorSpec.Slug}.fbx");
            Transform floor = null;
            if (floorAsset != null)
            {
                floor = new GameObject("Floor").transform;
                floor.SetParent(root.transform, false);
                var f = Place(floorAsset, floor, "Model", MaterialFor(floorSpec.Slug, AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder(floorSpec.Slug)}/{floorSpec.Slug}_basecolor.png")), true);
                var fr = Measure(f.transform, floor);
                var (fs, fo) = Normalise(floorSpec, fr);
                f.transform.localScale = ToLocal(f.transform.localRotation, fs); f.transform.localPosition = fo;
                AddUnderlay(floor);
            }
            var leafPivot = new GameObject("Leaf").transform;
            leafPivot.SetParent(root.transform, false);
            var leaf = Place(asset, leafPivot, "Model", material, zUp);
            var raw = Measure(leaf.transform, leafPivot);
            if (raw.size.z > raw.size.x * 1.2f) { leaf.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * leaf.transform.localRotation; raw = Measure(leaf.transform, leafPivot); row.Notes += "door turned 90 to lie across X; "; }
            row.Raw = Fmt(raw.size);
            float sx = spec.Size.x / Mathf.Max(0.001f, raw.size.x), sy = spec.Size.y / Mathf.Max(0.001f, raw.size.y);
            float sz = Mathf.Min(sx, sy);
            var scale = new Vector3(sx, sy, sz);
            leaf.transform.localScale = ToLocal(leaf.transform.localRotation, scale);
            var m = Measure(leaf.transform, leafPivot);
            leaf.transform.localPosition = new Vector3(-m.center.x, -m.min.y, -m.center.z);
            var fin = Measure(leaf.transform, root.transform);
            row.Final = Fmt(fin.size) + $" feet {fin.min.y:0.00}";
            row.Scale = Fmt(scale);
            var dv = root.AddComponent<DoorVisual>();
            dv.Leaf = leafPivot; dv.Floor = floor; dv.SinkDepth = spec.Size.y + 0.4f;
            return leaf;
        }

        /// <summary>A scale given along the parent's axes, as the child's own localScale under a quarter-turn rotation.</summary>
        private static Vector3 ToLocal(Quaternion rotation, Vector3 parentScale)
        {
            var r = Vector3.one;
            for (int i = 0; i < 3; i++)
            {
                var axis = rotation * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
                float ax = Mathf.Abs(axis.x), ay = Mathf.Abs(axis.y), az = Mathf.Abs(axis.z);
                r[i] = ax >= ay && ax >= az ? parentScale.x : ay >= az ? parentScale.y : parentScale.z;
            }
            return r;
        }

        /// <summary>A dark 2 m square just under the tile's top, so the bevels between neighbouring tiles show grout, not the void.</summary>
        private static void AddUnderlay(Transform parent)
        {
            var go = new GameObject("Underlay");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * -0.22f;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(2.02f, 2.02f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GroutMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Material GroutMaterial()
        {
            string path = $"{MaterialsRoot}/floor_grout.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Lit) { name = "floor_grout" }; AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", new Color(0.13f, 0.12f, 0.12f));
            mat.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void AddLight(Transform root, float height, Color colour, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.up * height;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = colour; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
        }

        /// <summary>Scale and offset for the model child so the prefab has its feet at y = 0, centred on X/Z, at the spec's size.</summary>
        private static (Vector3 scale, Vector3 offset) Normalise(Spec spec, (Vector3 min, Vector3 size, Vector3 center) raw)
        {
            var size = new Vector3(Mathf.Max(raw.size.x, 0.001f), Mathf.Max(raw.size.y, 0.001f), Mathf.Max(raw.size.z, 0.001f));
            Vector3 s;
            switch (spec.Fit)
            {
                case Fit.Height: s = Vector3.one * (spec.Size.y / size.y); break;
                case Fit.MaxDim: s = Vector3.one * (spec.Size.x / Mathf.Max(size.x, size.y, size.z)); break;
                case Fit.Box: s = new Vector3(spec.Size.x / size.x, spec.Size.y / size.y, spec.Size.z / size.z); break;
                default:
                {
                    float fx = spec.Size.x / size.x, fz = spec.Size.z / size.z;
                    float fy = Mathf.Min(Mathf.Min(fx, fz), spec.Size.y / size.y);
                    s = new Vector3(fx, fy, fz); break;
                }
            }
            float lift = spec.Fit == Fit.FloorTile ? -(raw.min.y + size.y) * s.y : -raw.min.y * s.y + (spec.Fit == Fit.Footprint ? 0.01f : 0f);
            return (s, new Vector3(-raw.center.x * s.x, lift, -raw.center.z * s.z));
        }

        /// <summary>Bounds of every mesh under <paramref name="model"/>, in <paramref name="space"/>'s coordinates, in the rest pose.</summary>
        private static (Vector3 min, Vector3 size, Vector3 center) Measure(Transform model, Transform space)
        {
            var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity; bool any = false;
            var toSpace = space.worldToLocalMatrix;
            void Add(Bounds b, Matrix4x4 m)
            {
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toSpace.MultiplyPoint3x4(m.MultiplyPoint3x4(c));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true;
                }
            }
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) Add(mf.sharedMesh.bounds, mf.transform.localToWorldMatrix);
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.sharedMesh != null) Add(smr.sharedMesh.bounds, smr.transform.localToWorldMatrix);
            if (!any) return (Vector3.zero, Vector3.one, Vector3.zero);
            return (min, max - min, (min + max) * 0.5f);
        }

        // ------------------------------------------------------------------ materials

        private static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");

        private static Material MaterialFor(string slug, Texture2D baseMap)
        {
            if (baseMap == null) return null;
            string path = $"{MaterialsRoot}/{slug}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Lit) { name = slug }; AssetDatabase.CreateAsset(mat, path); }
            if (mat.shader != Lit) mat.shader = Lit;
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", Tints.TryGetValue(slug, out var tint) ? tint : Color.white);
            mat.SetFloat("_Smoothness", 0.18f);
            mat.SetFloat("_Metallic", 0f);
            if (Glowing.Contains(slug))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", baseMap);
                mat.SetColor("_EmissionColor", Color.white * (slug == "lava_pool" ? 1.6f : 0.9f));
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else { mat.DisableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material RingMaterial()
        {
            string path = $"{MaterialsRoot}/team_ring.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader) { name = "team_ring" }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.doubleSidedGI = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>URP Lit set to transparent alpha blending by hand (the URP editor GUI helpers are not referenced here).</summary>
        private static Material GhostMaterial()
        {
            string path = $"{MaterialsRoot}/avatar_ghost.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Lit) { name = "avatar_ghost" }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Lit;
            mat.SetColor("_BaseColor", new Color(0.65f, 0.75f, 1f, 0.3f));
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ helpers

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string Fmt(Vector3 v) => $"{v.x:0.00}x{v.y:0.00}x{v.z:0.00}";

        private static void WriteReport(List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Generated model import, {DateTime.Now:yyyy-MM-dd HH:mm}. Sizes in metres (X x Y x Z); raw = as imported, final = prefab.");
            sb.AppendLine();
            string[] head = { "slug", "kind", "rig", "clips", "raw size", "scale", "final size", "prefab", "notes" };
            var table = new List<string[]> { head };
            table.AddRange(rows.Select(r => new[] { r.Slug, r.Kind, r.Rig, r.Clips, r.Raw, r.Scale, r.Final, r.Prefab == "-" ? "-" : "yes", r.Notes.Trim() }));
            var widths = Enumerable.Range(0, head.Length).Select(i => table.Max(t => t[i].Length)).ToArray();
            foreach (var t in table) sb.AppendLine(string.Join(" | ", t.Select((c, i) => c.PadRight(widths[i]))).TrimEnd());
            var logs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "model-import.txt"), sb.ToString());
            Debug.Log("Dungeon Gen: model import report written to " + Path.Combine(logs, "model-import.txt") + "\n" + sb);
        }
    }
}
