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
    /// Humanoid animation for the generated characters, from the bought animation packs.
    /// <list type="number">
    /// <item><see cref="MakeHumanoid"/> turns a rig into a Unity Humanoid avatar: bones mapped by name (the Meshy auto-rig's
    /// Mixamo-like names, or 3ds Max Biped names), arms and legs straightened into a T-pose in the avatar's reference skeleton
    /// (Meshy rigs are bound in an A-pose), <c>ModelImporter.animationType = Human</c>.</item>
    /// <item><see cref="ConfigurePackClips"/> sets every clip kept under <see cref="ClipsRoot"/> (copied from the packs with
    /// their own .meta, so their own avatars) to one import: in place (root XZ becomes root motion the Animator throws away,
    /// rotation and height baked into the pose), loops for locomotion and channels, keyframe reduction.</item>
    /// <item><see cref="BuildRoleController"/> builds one AnimatorController per character from its role's clips (see
    /// <see cref="Roles"/>): a 2D freeform-directional locomotion blend on the ground velocity in the model's own frame
    /// (MoveX, MoveZ, in the avatar's human scale), positioned at each clip's measured root speed so feet do not slide, in states
    /// named Idle, Walk and Run; Attack, Cast, Hit, Death, Pray and Interact on the base layer, and Attack, Cast and Hit again on
    /// an upper-body override layer whose weight <see cref="AvatarVisual"/> raises with speed so the legs keep running under a swing.</item>
    /// </list>
    /// <see cref="GeneratedModelImporter.Build"/> calls the controller builder for every humanoid slug. Run everything headless:
    /// <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.AnimationSetup.Run</c>. Report: <c>Logs/animation-setup.txt</c>.
    /// </summary>
    public static class AnimationSetup
    {
        public const string Root = "Assets/Animation";
        public const string ClipsRoot = "Assets/Animation/Clips";
        public const string ControllersRoot = "Assets/Animation/Controllers";
        public const string UpperBodyMaskPath = "Assets/Animation/UpperBody.mask";

        /// <summary>The Meshy-rigged characters that get a Humanoid avatar and a pack controller, and the role each one plays.</summary>
        public static readonly Dictionary<string, string> SlugRoles = new Dictionary<string, string>
        {
            { "warden", "Warden" }, { "reaver", "Reaver" }, { "marksman", "Marksman" }, { "elementalist", "Elementalist" }, { "mender", "Mender" }, { "shade", "Shade" },
            { "bandit", "Fighter" }, { "hollow_knight", "Skeleton" }, { "frostbound", "Skeleton" }, { "storm_acolyte", "Elementalist" }, { "gravewalker", "Zombie" },
            { "iron_juggernaut", "Reaver" }, { "ashen_colossus", "Zombie" }, { "glasswright", "Mender" }, { "plague_herald", "Elementalist" }, { "the_sovereign_below", "Skeleton" },
        };

        public static bool IsHumanoidSlug(string slug) => slug != null && SlugRoles.ContainsKey(slug);

        // ------------------------------------------------------------------ roles

        private const string Synty = "SyntyLocomotion/", Paladin = "Paladin/", Mage = "CasterMage/", Melee = "HumanMelee/", Bow = "RPGBow/", Daggers = "TwinDaggers/",
            Actions = "RPGActions/", Skel = "SkeletonMonster/", Zomb = "ZombieMonster/";

        /// <summary>Synty's masculine locomotion: the fallback for any direction a role's own pack lacks.</summary>
        private static readonly Dictionary<string, string> Base = new Dictionary<string, string>
        {
            { "idle", Synty + "A_Idle_Standing_Masc.fbx" },
            { "walkF", Synty + "A_Walk_F_RootMotion_Masc.fbx" }, { "walkB", Synty + "A_Walk_BckStrafeB_RootMotion_Masc.fbx" },
            { "walkL", Synty + "A_Walk_FwdStrafeL_RootMotion_Masc.fbx" }, { "walkR", Synty + "A_Walk_FwdStrafeR_RootMotion_Masc.fbx" },
            { "runF", Synty + "A_Run_F_RootMotion_Masc.fbx" }, { "runB", Synty + "A_Run_BckStrafeB_RootMotion_Masc.fbx" },
            { "runL", Synty + "A_Run_FwdStrafeL_RootMotion_Masc.fbx" }, { "runR", Synty + "A_Run_FwdStrafeR_RootMotion_Masc.fbx" },
            { "pray", Actions + "Pray_1.fbx" }, { "interact", Actions + "Chest_Open_1_InPlace.fbx" },
        };

        /// <summary>
        /// Each role's clips: "Folder/File.fbx", or "Folder/File.fbx#Suffix" for the take whose name ends with Suffix. Keys: idle,
        /// walkF/B/L/R, runF/B/L/R, attack0/1 (basic attacks, alternated), cast0/1 (abilities, alternated), hit, death, pray, interact.
        /// Missing keys fall back to <see cref="Base"/>; a missing attack1/cast1 repeats attack0/cast0.
        /// </summary>
        public static readonly Dictionary<string, Dictionary<string, string>> Roles = new Dictionary<string, Dictionary<string, string>>
        {
            ["Warden"] = new Dictionary<string, string>
            {
                { "idle", Paladin + "idle01.fbx" },
                { "walkF", Paladin + "move_walk_front.fbx" }, { "walkB", Paladin + "move_walk_back.fbx" }, { "walkL", Paladin + "move_walk_left.fbx" }, { "walkR", Paladin + "move_walk_right.fbx" },
                { "runF", Paladin + "move_run_front.fbx" }, { "runB", Paladin + "move_run_back.fbx" }, { "runL", Paladin + "move_run_left.fbx" }, { "runR", Paladin + "move_run_right.fbx" },
                { "attack0", Paladin + "Attack01.fbx" }, { "attack1", Paladin + "Attack02.fbx" }, { "cast0", Paladin + "Attack_Shield01.fbx" }, { "cast1", Paladin + "Buff01.fbx" },
                { "hit", Paladin + "hit_body_front.fbx" }, { "death", Paladin + "dead01.fbx" },
            },
            ["Reaver"] = new Dictionary<string, string>
            {
                { "idle", Melee + "HumanM@Combat_Idle01 - 2H.fbx" },
                { "walkF", Melee + "HumanM@Walk01_Forward [RM].fbx" }, { "walkB", Melee + "HumanM@Walk01_Backward [RM].fbx" }, { "walkL", Melee + "HumanM@Walk01_Left [RM].fbx" }, { "walkR", Melee + "HumanM@Walk01_Right [RM].fbx" },
                { "runF", Melee + "HumanM@Run01_Forward [RM].fbx" }, { "runB", Melee + "HumanM@Run01_Backward [RM].fbx" }, { "runL", Melee + "HumanM@Run01_Left [RM].fbx" }, { "runR", Melee + "HumanM@Run01_Right [RM].fbx" },
                { "attack0", Melee + "HumanM@2HAttack01.fbx" }, { "attack1", Melee + "HumanM@2HAttack02.fbx" }, { "cast0", Melee + "HumanM@2HAttack04.fbx" },
                { "hit", Melee + "HumanM@Combat_TakeDamage01.fbx" }, { "death", Melee + "HumanM@Death01.fbx" },
            },
            ["Fighter"] = new Dictionary<string, string>
            {
                { "idle", Melee + "HumanM@Combat_Idle01 - 1H.fbx" },
                { "walkF", Melee + "HumanM@Walk01_Forward [RM].fbx" }, { "walkB", Melee + "HumanM@Walk01_Backward [RM].fbx" }, { "walkL", Melee + "HumanM@Walk01_Left [RM].fbx" }, { "walkR", Melee + "HumanM@Walk01_Right [RM].fbx" },
                { "runF", Melee + "HumanM@Run01_Forward [RM].fbx" }, { "runB", Melee + "HumanM@Run01_Backward [RM].fbx" }, { "runL", Melee + "HumanM@Run01_Left [RM].fbx" }, { "runR", Melee + "HumanM@Run01_Right [RM].fbx" },
                { "attack0", Melee + "HumanM@1HAttack01_R.fbx" }, { "attack1", Melee + "HumanM@1HAttack03_R.fbx" }, { "cast0", Melee + "HumanM@1HAttack03_R.fbx" },
                { "hit", Melee + "HumanM@Combat_TakeDamage01.fbx" }, { "death", Melee + "HumanM@Death02.fbx" },
            },
            ["Marksman"] = new Dictionary<string, string>
            {
                { "idle", Bow + "Bow_Idle_A.fbx" },
                { "walkF", Bow + "Bow_Walk_F.fbx" }, { "walkB", Bow + "Bow_Walk_B.fbx" }, { "runF", Bow + "Bow_Run_F.fbx" }, { "runB", Bow + "Bow_Run_B.fbx" },
                { "attack0", Bow + "Bow_Attack_A_1_All.fbx" }, { "cast0", Bow + "Bow_Skill_1.fbx" },
                { "hit", Bow + "Bow_Hit_F_1.fbx" }, { "death", Bow + "Bow_Die_F_1.fbx" },
            },
            ["Elementalist"] = new Dictionary<string, string>
            {
                { "idle", Mage + "Mage@Idle01.fbx" },
                { "runF", Mage + "Mage@Run [RM].fbx#- Forward [RM]" }, { "runB", Mage + "Mage@Run [RM].fbx#- Backward [RM]" }, { "runL", Mage + "Mage@Run [RM].fbx#- Left [RM]" }, { "runR", Mage + "Mage@Run [RM].fbx#- Right [RM]" },
                { "attack0", Mage + "Mage@Attack01.fbx" }, { "cast0", Mage + "Mage@Attack04.fbx#Support 01" }, { "cast1", Mage + "Mage@Attack02.fbx" },
                { "hit", Mage + "Mage@Damage01.fbx" }, { "death", Mage + "Mage@Death01.fbx" },
            },
            ["Mender"] = new Dictionary<string, string>
            {
                { "idle", Mage + "Mage@Idle02.fbx" },
                { "runF", Mage + "Mage@Run [RM].fbx#- Forward [RM]" }, { "runB", Mage + "Mage@Run [RM].fbx#- Backward [RM]" }, { "runL", Mage + "Mage@Run [RM].fbx#- Left [RM]" }, { "runR", Mage + "Mage@Run [RM].fbx#- Right [RM]" },
                { "attack0", Mage + "Mage@Attack03.fbx" }, { "cast0", Mage + "Mage@Attack05.fbx" }, { "cast1", Paladin + "Buff01.fbx" },
                { "hit", Mage + "Mage@Damage01.fbx" }, { "death", Mage + "Mage@Death01.fbx" },
            },
            ["Shade"] = new Dictionary<string, string>
            {
                { "idle", Daggers + "TwinDaggers_Idle_Inplace.FBX" },
                { "walkF", Daggers + "TwinDaggers_Walk_F_Loop_Root.FBX" }, { "walkB", Daggers + "TwinDaggers_Walk_B_Loop_Root.FBX" }, { "walkL", Daggers + "TwinDaggers_Walk_L_Loop_Root.FBX" }, { "walkR", Daggers + "TwinDaggers_Walk_R_Loop_Root.FBX" },
                { "runF", Daggers + "TwinDaggers_Run_F_Loop_Root.FBX" }, { "runB", Daggers + "TwinDaggers_Run_B_Loop_Root.FBX" }, { "runL", Daggers + "TwinDaggers_Run_L_Loop_Root.FBX" }, { "runR", Daggers + "TwinDaggers_Run_R_Loop_Root.FBX" },
                { "attack0", Daggers + "TwinDaggers_Attack1_1_Inplace.FBX" }, { "attack1", Daggers + "TwinDaggers_Attack3_Inplace.FBX" }, { "cast0", Daggers + "TwinDaggers_SPAttack2_Inplace.FBX" },
                { "hit", Daggers + "TwinDaggers_Hit_F_Inplace.FBX" }, { "death", Daggers + "TwinDaggers_Die_1_Inplace.FBX" },
            },
            ["Skeleton"] = new Dictionary<string, string>
            {
                { "idle", Skel + "2_bms_sword_Idle_C.fbx" }, { "walkF", Skel + "2_bms_sword_Walk_F.fbx" }, { "runF", Skel + "2_bms_sword_Run_F.fbx" },
                { "attack0", Skel + "2_bms_sword_ATK_01.fbx" }, { "attack1", Skel + "2_bms_sword_ATK_02.fbx" }, { "cast0", Skel + "2_bms_sword_Skill_03.fbx" },
                { "hit", Skel + "2_bms_sword_DMG_F.fbx" }, { "death", Skel + "2_bms_sword_Die.fbx" },
            },
            ["Zombie"] = new Dictionary<string, string>
            {
                { "idle", Zomb + "Zombie_Idle_C37.fbx" }, { "walkF", Zomb + "Zombie_Walk_F51.fbx" }, { "runF", Zomb + "Zombie_Run_F46.fbx" },
                { "attack0", Zomb + "Zombie_ATK_0105.fbx" }, { "attack1", Zomb + "Zombie_ATK_0207.fbx" }, { "cast0", Zomb + "Zombie_ATK_0207.fbx" },
                { "hit", Zomb + "Zombie_DMG_F19.fbx" }, { "death", Zomb + "Zombie_Die14.fbx" },
            },
        };

        /// <summary>Pack models whose Generic rigs this setup maps to Humanoid itself (the clips beside them copy the avatar).</summary>
        private static readonly string[] GenericPackModels =
        {
            Skel + "2_Monster Animations - Sword_Wielding Skeleton.fbx",
            Zomb + "3_Monster Animations - Zombie.fbx",
        };

        private static readonly string[] LoopKeys = { "idle", "walkF", "walkB", "walkL", "walkR", "runF", "runB", "runL", "runR", "pray", "interact" };

        // ------------------------------------------------------------------ entry points

        private static readonly StringBuilder Report = new StringBuilder();
        private static void Note(string line) { Report.AppendLine(line); Debug.Log("Dungeon Gen animation: " + line); }

        /// <summary>Everything, headless: Meshy rigs to Humanoid, pack clips configured, then the prefabs (and their controllers) rebuilt.</summary>
        [MenuItem("Dungeon Gen/Animation/Set Up Humanoid Animation And Rebuild Prefabs")]
        public static void Run()
        {
            bool ok = Prepare();
            GeneratedModelImporter.Build();
            WriteReport();
            if (Application.isBatchMode && !ok) EditorApplication.Exit(1);
        }

        /// <summary>Step 1 alone (the feasibility check): every Meshy rig and pack model to Humanoid, every kept clip configured; no prefabs touched.</summary>
        [MenuItem("Dungeon Gen/Animation/Make Rigs Humanoid (Feasibility)")]
        public static void Feasibility()
        {
            bool ok = Prepare();
            WriteReport();
            if (Application.isBatchMode && !ok) EditorApplication.Exit(1);
        }

        private static bool Prepare()
        {
            Report.Clear();
            Report.AppendLine($"Humanoid animation setup, {DateTime.Now:yyyy-MM-dd HH:mm}.");
            bool ok = true;
            AssetDatabase.Refresh();
            foreach (var slug in SlugRoles.Keys)
            {
                string path = $"{GeneratedModelImporter.Folder(slug)}/{slug}_rigged.fbx";
                if (!File.Exists(path)) { Note($"rig {slug}: no rigged fbx"); ok = false; continue; }
                if (!MakeHumanoid(path, out string note)) ok = false;
                Note($"rig {slug}: {note}");
            }
            foreach (var rel in GenericPackModels)
            {
                string path = $"{ClipsRoot}/{rel}";
                if (!File.Exists(path)) { Note($"pack model {rel}: missing"); ok = false; continue; }
                if (!MakeHumanoid(path, out string note)) ok = false;
                Note($"pack model {rel}: {note}");
            }
            if (!ConfigurePackClips()) ok = false;
            UpperBodyMask();
            AssetDatabase.SaveAssets();
            return ok;
        }

        private static void WriteReport()
        {
            var logs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "animation-setup.txt"), Report.ToString());
        }

        // ------------------------------------------------------------------ humanoid avatars

        /// <summary>Humanoid bone → candidate transform names (Meshy auto-rig, 3ds Max Biped). Case-insensitive, exact.</summary>
        private static readonly (string human, string[] names, bool required)[] BoneMap =
        {
            ("Hips", new[] { "Hips", "Bip001 Pelvis" }, true),
            ("Spine", new[] { "Spine", "Bip001 Spine" }, true),
            ("Chest", new[] { "Spine01", "Bip001 Spine1" }, false),
            ("UpperChest", new[] { "Spine02", "Bip001 Spine2" }, false),
            ("Neck", new[] { "neck", "Bip001 Neck" }, false),
            ("Head", new[] { "Head", "Bip001 Head" }, true),
            ("LeftShoulder", new[] { "LeftShoulder", "Bip001 L Clavicle" }, false),
            ("LeftUpperArm", new[] { "LeftArm", "Bip001 L UpperArm" }, true),
            ("LeftLowerArm", new[] { "LeftForeArm", "Bip001 L Forearm" }, true),
            ("LeftHand", new[] { "LeftHand", "Bip001 L Hand" }, true),
            ("RightShoulder", new[] { "RightShoulder", "Bip001 R Clavicle" }, false),
            ("RightUpperArm", new[] { "RightArm", "Bip001 R UpperArm" }, true),
            ("RightLowerArm", new[] { "RightForeArm", "Bip001 R Forearm" }, true),
            ("RightHand", new[] { "RightHand", "Bip001 R Hand" }, true),
            ("LeftUpperLeg", new[] { "LeftUpLeg", "Bip001 L Thigh" }, true),
            ("LeftLowerLeg", new[] { "LeftLeg", "Bip001 L Calf" }, true),
            ("LeftFoot", new[] { "LeftFoot", "Bip001 L Foot" }, true),
            ("LeftToes", new[] { "LeftToeBase", "Bip001 L Toe0" }, false),
            ("RightUpperLeg", new[] { "RightUpLeg", "Bip001 R Thigh" }, true),
            ("RightLowerLeg", new[] { "RightLeg", "Bip001 R Calf" }, true),
            ("RightFoot", new[] { "RightFoot", "Bip001 R Foot" }, true),
            ("RightToes", new[] { "RightToeBase", "Bip001 R Toe0" }, false),
        };

        /// <summary>
        /// Map a model's bones to Humanoid by name, straighten its arms and legs into a T-pose in the reference skeleton, and
        /// reimport it as Human. True when the resulting avatar is valid and human; the note says what happened.
        /// </summary>
        public static bool MakeHumanoid(string path, out string note)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) { note = "not a model"; return false; }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { note = "did not import"; return false; }
            var go = UnityEngine.Object.Instantiate(asset);
            try
            {
                go.name = asset.name;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var all = go.GetComponentsInChildren<Transform>(true);
                var found = new Dictionary<string, Transform>();
                var bones = new List<HumanBone>();
                var missing = new List<string>();
                foreach (var (human, names, required) in BoneMap)
                {
                    if (human == "Spine" || human == "Chest" || human == "UpperChest") continue;   // from the chain below
                    var t = all.FirstOrDefault(x => names.Any(n => string.Equals(x.name, n, StringComparison.OrdinalIgnoreCase)));
                    if (t == null) { if (required) missing.Add(human); continue; }
                    found[human] = t;
                }
                if (missing.Count > 0) { note = "FAILED: no bone for " + string.Join(", ", missing); return false; }
                // The spine by ancestry, not by name (the Meshy rig numbers it from the top: Hips > Spine02 > Spine01 > Spine > neck):
                // the bones between the hips and the neck (or head), lowest first, are Spine, Chest and UpperChest.
                var top = found.TryGetValue("Neck", out var neck) && neck.IsChildOf(found["Hips"]) ? neck : found["Head"];
                var chain = new List<Transform>();
                for (var t = top.parent; t != null && t != found["Hips"]; t = t.parent) chain.Insert(0, t);
                if (!top.IsChildOf(found["Hips"]) || chain.Count == 0) { note = "FAILED: no spine between the hips and the head"; return false; }
                string[] spineNames = { "Spine", "Chest", "UpperChest" };
                for (int i = 0; i < Mathf.Min(3, chain.Count); i++) found[spineNames[i]] = chain[i];
                if (found.TryGetValue("Neck", out neck) && !found["Head"].IsChildOf(neck)) found.Remove("Neck");
                foreach (var side in new[] { "Left", "Right" })
                    if (found.TryGetValue(side + "Shoulder", out var sh) && !found[side + "UpperArm"].IsChildOf(sh)) found.Remove(side + "Shoulder");
                foreach (var (human, _, _) in BoneMap)
                {
                    if (!found.TryGetValue(human, out var t)) continue;
                    var hb = new HumanBone { boneName = t.name, humanName = human };
                    hb.limit.useDefaultValues = true;
                    bones.Add(hb);
                }

                float bent = EnforceTPose(found);
                var skeleton = all.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                var hd = mi.humanDescription;
                hd.human = bones.ToArray();
                hd.skeleton = skeleton;
                hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
                hd.armStretch = 0.05f; hd.legStretch = 0.05f; hd.feetSpacing = 0f; hd.hasTranslationDoF = false;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.humanDescription = hd;
                mi.optimizeGameObjects = false;
                mi.SaveAndReimport();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                bool ok = avatar != null && avatar.isValid && avatar.isHuman;
                note = ok ? $"Humanoid OK ({bones.Count} bones mapped, spine {string.Join(" > ", chain.Take(3).Select(c => c.name))}, arms and legs straightened by up to {bent:0} degrees)" : $"FAILED: avatar {(avatar == null ? "missing" : avatar.isValid ? "not human" : "invalid")}";
                return ok;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Arms straight out sideways, legs straight down, in place on the instance (its root at the origin, unrotated).
        /// Sideways is measured from the hips (left thigh minus right thigh), so the model's facing does not matter. Returns the
        /// largest correction in degrees.
        /// </summary>
        private static float EnforceTPose(Dictionary<string, Transform> b)
        {
            var side = b["LeftUpperLeg"].position - b["RightUpperLeg"].position; side.y = 0f;
            if (side.sqrMagnitude < 1e-8f) { side = b["LeftUpperArm"].position - b["RightUpperArm"].position; side.y = 0f; }
            side = side.sqrMagnitude < 1e-8f ? Vector3.left : side.normalized;
            float worst = 0f;
            void Aim(string bone, string child, Vector3 dir)
            {
                var t = b[bone]; var c = b[child];
                var cur = c.position - t.position;
                if (cur.sqrMagnitude < 1e-10f) return;
                worst = Mathf.Max(worst, Vector3.Angle(cur, dir));
                t.rotation = Quaternion.FromToRotation(cur.normalized, dir) * t.rotation;
            }
            Aim("LeftUpperArm", "LeftLowerArm", side); Aim("LeftLowerArm", "LeftHand", side);
            Aim("RightUpperArm", "RightLowerArm", -side); Aim("RightLowerArm", "RightHand", -side);
            Aim("LeftUpperLeg", "LeftLowerLeg", Vector3.down); Aim("LeftLowerLeg", "LeftFoot", Vector3.down);
            Aim("RightUpperLeg", "RightLowerLeg", Vector3.down); Aim("RightLowerLeg", "RightFoot", Vector3.down);
            return worst;
        }

        // ------------------------------------------------------------------ pack clips

        /// <summary>Every kept clip file: in place, rotation and height baked, loops where the role table loops, keyframe reduction, no materials.</summary>
        public static bool ConfigurePackClips()
        {
            if (!AssetDatabase.IsValidFolder(ClipsRoot)) { Note("no clips folder " + ClipsRoot); return false; }
            var loops = new HashSet<string>(Roles.Values.SelectMany(r => r).Concat(Base).Where(kv => LoopKeys.Contains(kv.Key)).Select(kv => FilePart(kv.Value)));
            var used = new HashSet<string>(Roles.Values.SelectMany(r => r.Values).Concat(Base.Values).Select(FilePart));
            bool ok = true; int n = 0;
            foreach (var rel in used.OrderBy(x => x))
            {
                string path = $"{ClipsRoot}/{rel}";
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { Note($"clip {rel}: MISSING"); ok = false; continue; }
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false;
                mi.importAnimation = true;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
                // The clip's own avatar: the pack model it was authored on (kept beside it), or the Generic pack model this setup mapped.
                string generic = GenericPackModels.FirstOrDefault(g => FolderOf(g) == FolderOf(rel));
                if (generic != null && rel != generic)
                {
                    mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    mi.sourceAvatar = AssetDatabase.LoadAllAssetsAtPath($"{ClipsRoot}/{generic}").OfType<Avatar>().FirstOrDefault();
                }
                else if (mi.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && mi.sourceAvatar == null) mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                bool loop = loops.Contains(rel);
                var takes = mi.clipAnimations != null && mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
                foreach (var c in takes)
                {
                    c.loopTime = loop; c.loopPose = false;
                    c.lockRootRotation = true; c.keepOriginalOrientation = true;
                    c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                    c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = true;
                    c.mirror = false;
                    c.maskType = ClipAnimationMaskType.None; c.maskSource = null;   // the packs' own masks were not kept
                }
                mi.clipAnimations = takes;
                mi.SaveAndReimport();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault() ?? mi.sourceAvatar;
                var clips = Clips(path);
                bool good = clips.Count > 0 && clips.All(c => c.humanMotion);
                if (!good) ok = false;
                n++;
                Report.AppendLine($"clip {rel}: {clips.Count} take(s) [{string.Join(", ", clips.Select(c => $"{c.name} {c.length:0.00}s speed {c.averageSpeed.magnitude:0.00}"))}]{(loop ? " loop" : "")}{(good ? "" : " NOT HUMANOID")}{(avatar != null && !avatar.isValid ? " avatar invalid" : "")}");
            }
            Note($"{n} clip files configured{(ok ? "" : ", with problems")}");
            return ok;
        }

        private static string FilePart(string spec) { int i = spec.IndexOf('#'); return i < 0 ? spec : spec.Substring(0, i); }
        private static string FolderOf(string rel) => rel.Substring(0, rel.IndexOf('/'));

        private static List<AnimationClip> Clips(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();

        /// <summary>The clip a role entry names: the file's only take, or the take whose name ends with the "#Suffix".</summary>
        public static AnimationClip Resolve(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            int i = spec.IndexOf('#');
            string file = i < 0 ? spec : spec.Substring(0, i), suffix = i < 0 ? null : spec.Substring(i + 1);
            var clips = Clips($"{ClipsRoot}/{file}");
            // A file with several takes (start, load, release, transitions): the take named after the file, else the shortest name.
            if (suffix == null) return clips.FirstOrDefault(c => c.name == Path.GetFileNameWithoutExtension(file)) ?? clips.OrderBy(c => c.name.Length).ThenBy(c => c.name, StringComparer.Ordinal).FirstOrDefault();
            return clips.FirstOrDefault(c => c.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ?? clips.FirstOrDefault(c => c.name.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The role's clip for a key, falling back to the base locomotion and channels.</summary>
        private static AnimationClip Pick(Dictionary<string, string> role, string key)
        {
            if (role.TryGetValue(key, out var spec)) { var c = Resolve(spec); if (c != null) return c; Note($"  {spec}: no clip, using the fallback"); }
            if (key == "attack1") return Pick(role, "attack0");
            if (key == "cast1") return Pick(role, "cast0");
            if (key == "cast0") return Pick(role, "attack0");
            return Base.TryGetValue(key, out var b) ? Resolve(b) : null;
        }

        // ------------------------------------------------------------------ controllers

        /// <summary>The upper-body override layer (index 1) that plays actions over the running legs.</summary>
        public const string UpperLayer = "ActionUpper";

        /// <summary>Upper body (spine, head, arms) for the moving-attack layer.</summary>
        public static AvatarMask UpperBodyMask()
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "Animation");
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask == null) { mask = new AvatarMask { name = "UpperBody" }; AssetDatabase.CreateAsset(mask, UpperBodyMaskPath); }
            for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
                mask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.Head || p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm
                    || p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers || p == AvatarMaskBodyPart.LeftHandIK || p == AvatarMaskBodyPart.RightHandIK);
            EditorUtility.SetDirty(mask);
            return mask;
        }

        /// <summary>
        /// The controller for one humanoid slug from its role's clips. <paramref name="runSpeed"/> is the forward run clip's root
        /// speed in the avatar's human scale (what <see cref="AvatarVisual"/> divides its speed by past the end of the blend).
        /// Null when the role's idle clip is missing (the caller then keeps the Meshy controller).
        /// <para>Base layer: Idle (the idle clip), Walk and Run (both the one 2D directional blend of idle, walks and runs, switched by
        /// MoveSpeed so the state names say what the legs do), Attack / Attack 2 / Cast / Cast 2 / Hit full body (fired by
        /// <see cref="AvatarVisual"/> only while standing; moving off cuts back to Walk), Death (dead or downed), Pray and Interact
        /// (channels). Layer 1 (<see cref="UpperLayer"/>, upper-body mask): the same actions on AttackU / CastU / HitU, always fired;
        /// AvatarVisual raises its weight with speed, so a swing begun standing carries on over the legs once the body moves.</para>
        /// </summary>
        public static AnimatorController BuildRoleController(string slug, out float runSpeed, out string summary)
        {
            runSpeed = 0f; summary = "";
            if (!SlugRoles.TryGetValue(slug, out var roleName) || !Roles.TryGetValue(roleName, out var role)) { summary = "no role"; return null; }
            var idle = Pick(role, "idle");
            if (idle == null) { summary = $"{roleName}: no idle clip"; return null; }
            if (!AssetDatabase.IsValidFolder(ControllersRoot)) { if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "Animation"); AssetDatabase.CreateFolder(Root, "Controllers"); }
            string path = $"{ControllersRoot}/{slug}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            foreach (var p in new[] { "Speed", "MoveX", "MoveZ", "MoveSpeed" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "LocoRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            foreach (var s in new[] { "", "U" }) foreach (var t in new[] { "Attack", "Cast", "Hit" }) ctrl.AddParameter(t + s, AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Variant", AnimatorControllerParameterType.Int);
            foreach (var b in new[] { "Dead", "Downed", "Channel", "ChannelPray" }) ctrl.AddParameter(b, AnimatorControllerParameterType.Bool);

            // ---- the locomotion blend: idle in the middle, every walk and run at its measured root velocity
            var tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, path);
            tree.AddChild(idle, Vector2.zero);
            var used = new List<string> { $"idle {idle.name}" };
            var defaults = new Dictionary<string, Vector2>
            {
                { "walkF", new Vector2(0f, 1.5f) }, { "walkB", new Vector2(0f, -1.3f) }, { "walkL", new Vector2(-1.3f, 0f) }, { "walkR", new Vector2(1.3f, 0f) },
                { "runF", new Vector2(0f, 3.6f) }, { "runB", new Vector2(0f, -2.8f) }, { "runL", new Vector2(-3f, 0f) }, { "runR", new Vector2(3f, 0f) },
            };
            var placed = new List<Vector2>();
            float walkSpeed = 1.5f;
            foreach (var kv in defaults)
            {
                var clip = Pick(role, kv.Key);
                if (clip == null) continue;
                var pos = Position(clip, kv.Value);
                if (placed.Any(q => (q - pos).magnitude < 0.15f)) pos = kv.Value;   // two clips on one spot would make the blend undefined
                placed.Add(pos);
                tree.AddChild(clip, pos);
                if (kv.Key == "runF") runSpeed = pos.magnitude;
                if (kv.Key == "walkF") walkSpeed = pos.magnitude;
                used.Add($"{kv.Key} {clip.name} @({pos.x:0.00},{pos.y:0.00})");
            }
            if (runSpeed <= 0f) runSpeed = placed.Count > 0 ? placed.Max(q => q.magnitude) : 3.6f;
            float walkRun = Mathf.Max(0.6f, (walkSpeed + runSpeed) * 0.5f);

            // ---- base layer
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState State(string name, Motion motion, string tag, Vector3 at)
            {
                var st = sm.AddState(name, at); st.motion = motion; st.tag = tag; st.writeDefaultValues = false; return st;
            }
            var idleState = State("Idle", idle, "Locomotion", new Vector3(300, 0));
            var walk = State("Walk", tree, "Locomotion", new Vector3(550, -60));
            var run = State("Run", tree, "Locomotion", new Vector3(550, 60));
            foreach (var st in new[] { walk, run }) { st.speedParameter = "LocoRate"; st.speedParameterActive = true; }
            sm.defaultState = idleState;
            var death = State("Death", Pick(role, "death") ?? idle, "Death", new Vector3(50, 200));
            death.speed = Hurry(death.motion as AnimationClip, 2.0f);
            var pray = State("Pray", Pick(role, "pray") ?? idle, "Channel", new Vector3(800, 120));
            var interact = State("Interact", Pick(role, "interact") ?? idle, "Channel", new Vector3(800, -120));
            used.Add($"death {death.motion.name}, pray {pray.motion.name}, interact {interact.motion.name}");

            AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float d) { var t = from.AddTransition(to); t.hasExitTime = false; t.duration = d; t.hasFixedDuration = true; return t; }
            AnimatorStateTransition Any(AnimatorStateMachine m, AnimatorState to, float d, bool self) { var t = m.AddAnyStateTransition(to); t.hasExitTime = false; t.duration = d; t.hasFixedDuration = true; t.canTransitionToSelf = self; return t; }
            void NotDown(AnimatorStateTransition t) { t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead"); t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Downed"); }

            Go(idleState, walk, 0.15f).AddCondition(AnimatorConditionMode.Greater, 0.3f, "MoveSpeed");
            Go(idleState, run, 0.15f).AddCondition(AnimatorConditionMode.Greater, walkRun, "MoveSpeed");
            Go(walk, run, 0.25f).AddCondition(AnimatorConditionMode.Greater, walkRun, "MoveSpeed");
            Go(walk, idleState, 0.2f).AddCondition(AnimatorConditionMode.Less, 0.15f, "MoveSpeed");
            Go(run, walk, 0.25f).AddCondition(AnimatorConditionMode.Less, walkRun - 0.3f, "MoveSpeed");
            Go(run, idleState, 0.2f).AddCondition(AnimatorConditionMode.Less, 0.15f, "MoveSpeed");

            Any(sm, death, 0.15f, false).AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            Any(sm, death, 0.15f, false).AddCondition(AnimatorConditionMode.If, 0f, "Downed");
            var up = Go(death, idleState, 0.3f); up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead"); up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Downed");
            foreach (var (state, prayer) in new[] { (pray, true), (interact, false) })
            {
                var t = Any(sm, state, 0.2f, false);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Channel");
                t.AddCondition(prayer ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "ChannelPray");
                NotDown(t);
                Go(state, idleState, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Channel");
            }

            // ---- actions: full body on the base layer, and again on the upper-body layer
            var lsm = new AnimatorStateMachine { name = UpperLayer, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(lsm, path);
            ctrl.AddLayer(new AnimatorControllerLayer { name = UpperLayer, stateMachine = lsm, defaultWeight = 0f, avatarMask = UpperBodyMask(), blendingMode = AnimatorLayerBlendingMode.Override });
            var empty = lsm.AddState("Empty", new Vector3(300, 0)); empty.writeDefaultValues = false; empty.tag = "Empty";
            lsm.defaultState = empty;
            Any(lsm, empty, 0.1f, false).AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            Any(lsm, empty, 0.1f, false).AddCondition(AnimatorConditionMode.If, 0f, "Downed");
            var actions = new (string state, string key, string trigger, int variant)[] { ("Attack", "attack0", "Attack", 0), ("Attack 2", "attack1", "Attack", 1), ("Cast", "cast0", "Cast", 0), ("Cast 2", "cast1", "Cast", 1), ("Hit", "hit", "Hit", -1) };
            int row = 0;
            foreach (var (name, key, trigger, variant) in actions)
            {
                var clip = Pick(role, key);
                if (clip == null) continue;
                float speed = Hurry(clip, trigger == "Hit" ? 0.6f : trigger == "Attack" ? 1.2f : 1.5f), exit = trigger == "Hit" ? 0.8f : 0.88f;
                foreach (var upper in new[] { false, true })
                {
                    var m = upper ? lsm : sm;
                    var st = m.AddState(name, new Vector3(100 + 200 * row, upper ? 200 : 360)); st.motion = clip; st.writeDefaultValues = false; st.tag = trigger; st.speed = speed;
                    var t = Any(m, st, trigger == "Hit" ? 0.05f : 0.08f, true);
                    t.AddCondition(AnimatorConditionMode.If, 0f, trigger + (upper ? "U" : ""));
                    if (variant >= 0) t.AddCondition(AnimatorConditionMode.Equals, variant, "Variant");
                    NotDown(t);
                    var back = st.AddTransition(upper ? empty : idleState); back.hasExitTime = true; back.exitTime = exit; back.duration = 0.18f; back.hasFixedDuration = true;
                    if (!upper) Go(st, walk, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.6f, "MoveSpeed");   // moving off: the legs walk, the upper layer finishes the swing
                }
                row++;
                used.Add($"{key} {clip.name} ({clip.length:0.00}s{(speed > 1f ? $" at x{speed:0.0}" : "")})");
            }
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            summary = $"{roleName}: " + string.Join("; ", used);
            Note($"controller {slug} = {summary}");
            return ctrl;
        }

        /// <summary>Playback speed that fits a long one-shot into about <paramref name="seconds"/> (a 2.4 s monster swing, a 4 s skeleton skill), never slower than authored, at most 2.5x.</summary>
        private static float Hurry(AnimationClip clip, float seconds) => clip == null || clip.length <= seconds ? 1f : Mathf.Min(2.5f, clip.length / seconds);

        /// <summary>A locomotion clip's spot in the blend: its measured root velocity on the ground (human scale), or the default when it has none (an in-place clip).</summary>
        private static Vector2 Position(AnimationClip clip, Vector2 fallback)
        {
            var v = clip.averageSpeed;
            var p = new Vector2(v.x, v.z);
            if (p.magnitude < 0.3f || Vector2.Dot(p.normalized, fallback.normalized) < 0.5f) return fallback;   // in place, or pointing the wrong way: trust the name
            return p;
        }
    }
}
