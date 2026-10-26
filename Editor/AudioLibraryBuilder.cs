using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Reads <c>Assets/Generated/audio/manifest.json</c>, sets sensible import settings on every clip, and writes
    /// <c>Assets/Audio/AudioLibrary.asset</c>. Keys are manifest slugs with a trailing variant number removed
    /// (<c>hit_flesh_1..3</c> become <c>hit_flesh</c>). Run headless:
    /// <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.AudioLibraryBuilder.Build</c>.
    /// </summary>
    public static class AudioLibraryBuilder
    {
        public const string AudioRoot = "Assets/Generated/audio";
        public const string ManifestPath = AudioRoot + "/manifest.json";
        public const string LibraryPath = "Assets/Audio/AudioLibrary.asset";

        private sealed class Item { public string Slug, File, Category, Prompt; public bool Loop; }

        private static readonly Dictionary<string, float> CategoryVolumes = new Dictionary<string, float>
        {
            ["abilities"] = 0.8f, ["combat"] = 0.9f, ["world"] = 0.8f, ["ui"] = 0.6f, ["ambience"] = 0.6f,
            ["voice/announcer"] = 1f, ["voice/boss"] = 1f,
        };

        [MenuItem("Dungeon Gen/Build Audio Library")]
        public static void Build()
        {
            int problems = 0;
            var items = ReadManifest(ref problems);
            AssetDatabase.StartAssetEditing();
            try { foreach (var it in items) ApplyImportSettings(it); }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();

            if (!AssetDatabase.IsValidFolder("Assets/Audio")) AssetDatabase.CreateFolder("Assets", "Audio");
            var lib = AssetDatabase.LoadAssetAtPath<AudioLibrary>(LibraryPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<AudioLibrary>(); AssetDatabase.CreateAsset(lib, LibraryPath); }
            lib.Entries.Clear(); lib.CategoryVolumes.Clear(); lib.Lines.Clear();
            foreach (var kv in CategoryVolumes) lib.CategoryVolumes.Add(new AudioLibrary.CategoryVolume { Category = kv.Key, Volume = kv.Value });

            var byKey = new Dictionary<string, AudioLibrary.Entry>();
            var clipsByKey = new Dictionary<string, List<(string slug, AudioClip clip)>>();
            foreach (var it in items)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioRoot}/{it.File}");
                if (clip == null) { Debug.LogError($"Dungeon Gen audio: clip missing for {it.Slug} ({it.File})"); problems++; continue; }
                string key = KeyOf(it.Slug, it.Category);
                if (!byKey.TryGetValue(key, out var entry))
                {
                    entry = new AudioLibrary.Entry { Key = key, Category = it.Category, Loop = it.Loop, Volume = EntryVolume(key) };
                    byKey[key] = entry; lib.Entries.Add(entry);
                    clipsByKey[key] = new List<(string, AudioClip)>();
                }
                clipsByKey[key].Add((it.Slug, clip));
                if (it.Category == "voice/announcer" && !string.IsNullOrEmpty(it.Prompt)) lib.Lines.Add(new AudioLibrary.Line { Key = key, Text = it.Prompt });
            }
            foreach (var kv in clipsByKey)
            {
                kv.Value.Sort((a, b) => string.CompareOrdinal(a.slug, b.slug));
                var arr = new AudioClip[kv.Value.Count];
                for (int i = 0; i < arr.Length; i++) arr[i] = kv.Value[i].clip;
                byKey[kv.Key].Clips = arr;
            }
            lib.Entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"Dungeon Gen audio: library written with {lib.Entries.Count} keys from {items.Count} clips, {lib.Lines.Count} voiced lines, {problems} problems.");
            if (Application.isBatchMode && problems > 0) EditorApplication.Exit(1);
        }

        public static string KeyOf(string slug, string category) =>
            category != null && category.StartsWith("voice") ? slug : Regex.Replace(slug, @"_\d+$", "");

        private static float EntryVolume(string key)
        {
            if (key.StartsWith("music_")) return 0.8f;
            if (key.StartsWith("footstep")) return 0.6f;
            return 1f;
        }

        private static List<Item> ReadManifest(ref int problems)
        {
            var list = new List<Item>();
            if (!File.Exists(ManifestPath)) { Debug.LogError("Dungeon Gen audio: no manifest at " + ManifestPath); problems++; return list; }
            string text = File.ReadAllText(ManifestPath);
            foreach (Match m in Regex.Matches(text, "\"([^\"]+)\"\\s*:\\s*\\{([^{}]*)\\}"))
            {
                string body = m.Groups[2].Value;
                var it = new Item { Slug = m.Groups[1].Value, File = Str(body, "file"), Category = Str(body, "category"), Prompt = Str(body, "prompt"), Loop = Regex.IsMatch(body, "\"loop\"\\s*:\\s*true") };
                if (string.IsNullOrEmpty(it.File) || string.IsNullOrEmpty(it.Category)) { Debug.LogError("Dungeon Gen audio: malformed manifest entry " + it.Slug); problems++; continue; }
                list.Add(it);
            }
            return list;
        }

        private static string Str(string body, string field)
        {
            var m = Regex.Match(body, "\"" + field + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        private static void ApplyImportSettings(Item it)
        {
            string path = $"{AudioRoot}/{it.File}";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            bool streaming = it.Category == "ambience";
            var s = new AudioImporterSampleSettings
            {
                loadType = streaming ? AudioClipLoadType.Streaming : it.Loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = streaming ? 0.6f : 0.5f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = !streaming,
            };
            var cur = importer.defaultSampleSettings;
            bool mono = !streaming;                   // one-shots and voice are positioned or centred; only music and ambience stay stereo
            bool same = cur.loadType == s.loadType && cur.compressionFormat == s.compressionFormat && Mathf.Approximately(cur.quality, s.quality)
                        && cur.preloadAudioData == s.preloadAudioData && importer.forceToMono == mono && importer.loadInBackground == streaming;
            if (same) return;
            importer.defaultSampleSettings = s;
            importer.forceToMono = mono;
            importer.loadInBackground = streaming;
            importer.SaveAndReimport();
        }
    }
}
