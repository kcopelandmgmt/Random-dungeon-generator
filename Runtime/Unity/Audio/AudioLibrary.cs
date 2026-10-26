using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Sound keys → clips. A key is a manifest slug with any trailing variant number removed, so
    /// <c>hit_flesh_1..3</c> are three variants of <c>hit_flesh</c>. Built by <c>AudioLibraryBuilder</c> from the
    /// generated manifest; nothing here is hand-edited. Each category has a base volume; a key may scale it further.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon Gen/Audio Library", fileName = "AudioLibrary")]
    public sealed class AudioLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
            public string Category;
            public bool Loop;
            [Range(0f, 1f)] public float Volume = 1f;
            public AudioClip[] Clips = new AudioClip[0];
        }

        [Serializable]
        public sealed class CategoryVolume
        {
            public string Category;
            [Range(0f, 1f)] public float Volume = 1f;
        }

        /// <summary>A voiced line: the text it speaks (as the game prints it) and the key of its clip.</summary>
        [Serializable]
        public sealed class Line
        {
            public string Key;
            public string Text;
        }

        public List<Entry> Entries = new List<Entry>();
        public List<CategoryVolume> CategoryVolumes = new List<CategoryVolume>();
        public List<Line> Lines = new List<Line>();

        private Dictionary<string, Entry> _byKey;
        private Dictionary<string, string> _lineByText;

        private void OnEnable() { _byKey = null; _lineByText = null; }

        private void Index()
        {
            if (_byKey != null) return;
            _byKey = new Dictionary<string, Entry>();
            foreach (var e in Entries) if (e != null && !string.IsNullOrEmpty(e.Key)) _byKey[e.Key] = e;
            _lineByText = new Dictionary<string, string>();
            foreach (var l in Lines) if (l != null && !string.IsNullOrEmpty(l.Text)) _lineByText[Normalise(l.Text)] = l.Key;
        }

        public bool Has(string key) { Index(); return key != null && _byKey.ContainsKey(key); }

        public bool TryGet(string key, out Entry entry) { Index(); entry = null; return key != null && _byKey.TryGetValue(key, out entry); }

        /// <summary>A random variant of the key, or null if the key or its clips are missing.</summary>
        public AudioClip Pick(string key, out float volume)
        {
            volume = 1f;
            if (!TryGet(key, out var e) || e.Clips == null || e.Clips.Length == 0) return null;
            volume = e.Volume * CategoryVolumeOf(e.Category);
            return e.Clips[e.Clips.Length == 1 ? 0 : UnityEngine.Random.Range(0, e.Clips.Length)];
        }

        public float CategoryVolumeOf(string category)
        {
            foreach (var c in CategoryVolumes) if (c.Category == category) return c.Volume;
            return 1f;
        }

        /// <summary>The key of the voiced announcer line whose text matches (ignoring case and punctuation), or null.</summary>
        public string KeyForLine(string text)
        {
            Index();
            return !string.IsNullOrEmpty(text) && _lineByText.TryGetValue(Normalise(text), out var key) ? key : null;
        }

        /// <summary>Lower case, letters and digits only, single spaces.</summary>
        public static string Normalise(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            bool gap = false;
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch)) { if (gap && sb.Length > 0) sb.Append(' '); gap = false; sb.Append(char.ToLowerInvariant(ch)); }
                else gap = true;
            }
            return sb.ToString();
        }
    }
}
