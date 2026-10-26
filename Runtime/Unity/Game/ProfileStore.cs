using System;
using System.IO;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Profile;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The local profile on disk: <c>Application.persistentDataPath/profile.dgpf</c>, written with <see cref="ProfileCodec"/>.
    /// Saves are atomic (a temp file beside it, then a replace that keeps the previous save as <c>.bak</c>); a load that
    /// finds the main file unreadable falls back to the backup. Tests point <see cref="PathOverride"/> somewhere else.
    /// </summary>
    public static class ProfileStore
    {
        public const string FileName = "profile.dgpf";

        /// <summary>Tests and tools: use this file instead of the one under persistentDataPath. Null for the real one.</summary>
        public static string PathOverride;

        /// <summary>The save file: <see cref="PathOverride"/>, else the <c>-profile &lt;path&gt;</c> launch flag (smoke runs), else persistentDataPath.</summary>
        public static string FilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(PathOverride)) return PathOverride;
                var flag = LaunchFlags.Value("-profile");
                if (!string.IsNullOrEmpty(flag)) return Path.GetFullPath(flag);
                return Path.Combine(Application.persistentDataPath, FileName);
            }
        }
        public static string BackupPath => FilePath + ".bak";
        public static string TempPath => FilePath + ".tmp";

        public static bool Exists => File.Exists(FilePath) || File.Exists(BackupPath);

        /// <summary>The saved profile, or null when there is none (first launch). Falls back to the backup if the main file is damaged.</summary>
        public static PlayerProfile Load()
        {
            if (File.Exists(FilePath))
            {
                try { return ProfileCodec.FromBytes(File.ReadAllBytes(FilePath)); }
                catch (Exception e) when (e is ProfileFormatException || e is IOException)
                {
                    Debug.LogWarning($"ProfileStore: {FilePath} could not be read ({e.Message}); trying the backup.");
                }
            }
            if (File.Exists(BackupPath))
            {
                try { return ProfileCodec.FromBytes(File.ReadAllBytes(BackupPath)); }
                catch (Exception e) when (e is ProfileFormatException || e is IOException)
                {
                    Debug.LogWarning($"ProfileStore: the backup {BackupPath} could not be read either ({e.Message}).");
                }
            }
            return null;
        }

        /// <summary>Write the profile: temp file, then replace (the old save becomes the .bak).</summary>
        public static void Save(PlayerProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var bytes = ProfileCodec.ToBytes(profile);
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(TempPath, bytes);
            if (File.Exists(FilePath))
            {
                try { File.Replace(TempPath, FilePath, BackupPath, true); }
                catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
                {
                    // Not every file system does an atomic replace; fall back to copy-then-move.
                    File.Copy(FilePath, BackupPath, true);
                    File.Delete(FilePath);
                    File.Move(TempPath, FilePath);
                }
            }
            else File.Move(TempPath, FilePath);
        }

        /// <summary>First launch: a brand-new account with one character in the free kit (<see cref="PlayerProfile.NewAccount"/>), saved.</summary>
        public static PlayerProfile CreateNew(string characterName, ClassArchetype cls)
        {
            var p = PlayerProfile.NewAccount("local-" + Guid.NewGuid().ToString("N").Substring(0, 12), characterName, cls);
            Save(p);
            return p;
        }

        /// <summary>Remove the save and its backup (tests).</summary>
        public static void Delete()
        {
            foreach (var f in new[] { FilePath, BackupPath, TempPath }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
