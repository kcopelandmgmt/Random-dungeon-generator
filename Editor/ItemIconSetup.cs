using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonGen.Core.Combat;
using UnityEditor;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Makes the painted item icons usable in player builds: imports every <c>Assets/Generated/item-icons/&lt;slug&gt;.png</c>
    /// (from <c>Tools/AssetGen/meshy_images.py run --set items</c>) as a 256-px mipmapped sprite and writes the
    /// <c>ItemIcons</c> lookup at <c>Assets/Generated/item-icons/Resources/ItemIcons.asset</c>. Also writes
    /// <c>Resources/PaperDollModels.asset</c>, a small <see cref="AvatarVisualLibrary"/> holding only the hero entries of the
    /// round's library (<see cref="GeneratedModelImporter.LibraryPath"/>), so the menu's paper doll can show the class model
    /// without dragging every mob and boss into Resources. Runs as a greybox step and on its own:
    /// <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.ItemIconSetup.Run</c>.
    /// </summary>
    public static class ItemIconSetup
    {
        public const string Folder = "Assets/Generated/item-icons";
        public const string ResourcesFolder = Folder + "/Resources";
        public const string LookupPath = ResourcesFolder + "/ItemIcons.asset";
        public const string ModelsPath = ResourcesFolder + "/PaperDollModels.asset";
        public const int MaxSize = 256;

        [GreyboxStep(35)]
        public static void Step(GreyboxContext ctx) => Run();

        [MenuItem("Dungeon Gen/Item Icons and Paper Doll Models")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            EnsureFolder(ResourcesFolder);
            var icons = new List<(string slug, string path)>();
            foreach (var abs in Directory.GetFiles(Abs(Folder), "*.png").OrderBy(f => f, StringComparer.Ordinal))
            {
                string slug = Path.GetFileNameWithoutExtension(abs);
                string path = Folder + "/" + Path.GetFileName(abs);
                ConfigureSprite(path);
                icons.Add((slug, path));
            }
            WriteLookup(icons);
            int heroes = WriteModels();
            AssetDatabase.SaveAssets();
            Debug.Log($"Dungeon Gen item icons: {icons.Count} icons in {LookupPath}; {heroes} hero models in {ModelsPath}.");
        }

        private static void ConfigureSprite(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); imp = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (imp == null) { Debug.LogWarning("Dungeon Gen item icons: no importer for " + path); return; }
            bool dirty = imp.textureType != TextureImporterType.Sprite || imp.maxTextureSize != MaxSize || !imp.mipmapEnabled || imp.spriteImportMode != SpriteImportMode.Single
                         || imp.textureCompression != TextureImporterCompression.CompressedHQ || imp.filterMode != FilterMode.Trilinear;
            if (!dirty) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.maxTextureSize = MaxSize;
            imp.mipmapEnabled = true;
            imp.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            imp.filterMode = FilterMode.Trilinear;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = false;
            imp.sRGBTexture = true;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }

        /// <summary>The UI's lookup asset. Its type lives in the UI assembly, which this editor assembly does not reference: made by name, filled through serialization.</summary>
        private static void WriteLookup(List<(string slug, string path)> icons)
        {
            var type = Type.GetType("DungeonGen.Unity.UI.ItemIcons, DungeonGen.Unity.UI");
            if (type == null) { Debug.LogError("Dungeon Gen item icons: no DungeonGen.Unity.UI.ItemIcons type; item tiles get no pictures."); return; }
            var asset = AssetDatabase.LoadAssetAtPath(LookupPath, type) as ScriptableObject;
            if (asset == null) { asset = ScriptableObject.CreateInstance(type); AssetDatabase.CreateAsset(asset, LookupPath); }
            var so = new SerializedObject(asset);
            var list = so.FindProperty("Entries");
            list.arraySize = icons.Count;
            for (int i = 0; i < icons.Count; i++)
            {
                var el = list.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("Slug").stringValue = icons[i].slug;
                el.FindPropertyRelative("Icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(icons[i].path);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>The hero entries of the round's avatar library, copied into a Resources library for the menu.</summary>
        private static int WriteModels()
        {
            var src = AssetDatabase.LoadAssetAtPath<AvatarVisualLibrary>(GeneratedModelImporter.LibraryPath);
            if (src == null) { Debug.LogWarning("Dungeon Gen item icons: no avatar library at " + GeneratedModelImporter.LibraryPath + "; the paper doll shows no model."); return 0; }
            var lib = AssetDatabase.LoadAssetAtPath<AvatarVisualLibrary>(ModelsPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<AvatarVisualLibrary>(); AssetDatabase.CreateAsset(lib, ModelsPath); }
            lib.Entries = new List<AvatarVisualLibrary.Entry>();
            foreach (var cls in ClassArchetype.DefaultRoster)
            {
                var e = src.Find(cls.Name);
                if (e == null || e.Prefab == null) continue;
                lib.Entries.Add(new AvatarVisualLibrary.Entry { Key = e.Key, Prefab = e.Prefab, Scale = e.Scale, RingRadius = e.RingRadius, IsBoss = false });
            }
            lib.Fallback = new AvatarVisualLibrary.Entry { Key = "*" };
            lib.RingMaterial = null; lib.GhostMaterial = null;
            EditorUtility.SetDirty(lib);
            return lib.Entries.Count;
        }

        private static string Abs(string assetPath) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
