using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Keeps the Windows player download small by giving every texture the player actually includes a Standalone import budget:
    /// a maximum size and crunched DXT compression. Applied by code, never by hand-editing .meta files, and idempotent (an importer
    /// already at its budget is left alone). <see cref="PlayerBuild"/> calls <see cref="Apply"/> before every build, so a new effect
    /// pack or a freshly generated model cannot quietly bring the size back up.
    ///
    /// Which textures: those reachable from the build scenes and from every Resources folder (<c>AssetDatabase.GetDependencies</c>),
    /// i.e. what the player embeds. Source packs the game never references (most of Piloto Studio, 4 GB) are not touched and not shipped.
    ///
    /// The budget, per texture:
    /// <list type="bullet">
    /// <item>Hero and boss base colours (<c>Assets/Generated/models/&lt;slug&gt;/&lt;slug&gt;_basecolor.png</c>, per <see cref="GeneratedModelImporter.Specs"/>):
    /// 2048 max, crunch quality 85. Mobs and props: 1024 max, crunch 80 (the camera is top-down, a mob is about 100 px tall).</item>
    /// <item>Effect textures (<c>Assets/Piloto Studio</c>): 1024 max, 2048 for flipbooks and packed sheets (names with flip, sheet, packed, atlas or NxN),
    /// crunch quality 60. Normal maps and single-channel masks are resized but not crunched (crunch ruins them).</item>
    /// <item>Not touched: sprites (UI), cubemaps, lightmaps, cookies, and any texture marked Read/Write (code may read its pixels).</item>
    /// </list>
    /// The editor and PlayMode tests use the same Standalone settings, so a shot taken in the editor shows what the player shows.
    /// Menu: Dungeon Gen / Build Size. Headless: <c>bash Tools/Build/unity-batch.sh method DungeonGen.Unity.Editor.BuildSizeSetup.Run</c>.
    /// </summary>
    public static class BuildSizeSetup
    {
        private const string Platform = "Standalone";
        private const string VfxRoot = "Assets/Piloto Studio/";
        private const string ModelsRoot = "Assets/Generated/models/";
        private const int VfxCap = 1024, VfxSheetCap = 2048, HeroCap = 2048, PropCap = 1024;
        private const int VfxQuality = 60, HeroQuality = 85, PropQuality = 80;
        private static readonly Regex SheetName = new Regex(@"flip|sheet|packed|atlas|\d+x\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [MenuItem("Dungeon Gen/Build Size/Apply texture budget")]
        public static void Run()
        {
            var r = Apply(false);
            Debug.Log(r.Summary);
        }

        [MenuItem("Dungeon Gen/Build Size/Report texture budget (no changes)")]
        public static void Report() => Debug.Log(Apply(true).Summary);

        public struct Result
        {
            public int Considered, Changed, Skipped; public string Summary;
        }

        /// <summary>Everything the player embeds: the build scenes and every non-Editor Resources folder, plus their dependencies.</summary>
        public static HashSet<string> PlayerAssets()
        {
            var roots = new List<string>(PlayerBuild.Scenes);
            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.Contains("/Resources/") && !p.Contains("/Editor/") && !AssetDatabase.IsValidFolder(p)) roots.Add(p);
            }
            return new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true), StringComparer.OrdinalIgnoreCase);
        }

        public static Result Apply(bool dryRun)
        {
            var heroSlugs = new HashSet<string>(GeneratedModelImporter.Specs
                .Where(s => s.Kind == GeneratedModelImporter.Kind.Hero || s.Kind == GeneratedModelImporter.Kind.Boss).Select(s => s.Slug));
            var paths = PlayerAssets().Where(p => IsTexture(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            var res = new Result();
            var log = new StringBuilder();
            var pending = new List<TextureImporter>();
            try
            {
                foreach (var path in paths)
                {
                    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (imp == null) continue;
                    res.Considered++;
                    if (!TryBudget(path, imp, heroSlugs, out var want)) { res.Skipped++; continue; }
                    var cur = imp.GetPlatformTextureSettings(Platform);
                    if (Same(cur, want)) continue;
                    res.Changed++;
                    log.AppendLine($"  {path}: {(cur.overridden ? cur.maxTextureSize + (cur.crunchedCompression ? " crunch" : "") : "default")} -> {want.maxTextureSize}{(want.crunchedCompression ? " crunch " + want.compressionQuality : "")}");
                    if (dryRun) continue;
                    imp.SetPlatformTextureSettings(want);
                    pending.Add(imp);
                }
                if (!dryRun && pending.Count > 0)
                {
                    // Reimport in batches: crunching a 2048 texture takes a second or two.
                    for (int i = 0; i < pending.Count; i += 64)
                    {
                        AssetDatabase.StartAssetEditing();
                        try { for (int j = i; j < Math.Min(i + 64, pending.Count); j++) pending[j].SaveAndReimport(); }
                        finally { AssetDatabase.StopAssetEditing(); }
                    }
                }
            }
            catch (Exception e) { Debug.LogError("Dungeon Gen: texture budget failed: " + e); throw; }
            res.Summary = $"Dungeon Gen: build size texture budget{(dryRun ? " (dry run)" : "")}: {res.Considered} player textures, {res.Changed} {(dryRun ? "would change" : "changed")}, {res.Skipped} left alone.\n" + log;
            return res;
        }

        private static bool IsTexture(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd" || ext == ".tif" || ext == ".tiff" || ext == ".exr" || ext == ".bmp";
        }

        /// <summary>The Standalone settings this texture should have, or false if it is outside the budget (sprites, cubemaps, readable...).</summary>
        private static bool TryBudget(string path, TextureImporter imp, HashSet<string> heroSlugs, out TextureImporterPlatformSettings want)
        {
            want = null;
            if (imp.isReadable || imp.textureShape != TextureImporterShape.Texture2D) return false;
            bool crunchable;
            switch (imp.textureType)
            {
                case TextureImporterType.Default: crunchable = true; break;
                case TextureImporterType.NormalMap:
                case TextureImporterType.SingleChannel: crunchable = false; break;
                default: return false;
            }
            int cap, quality;
            if (path.StartsWith(ModelsRoot, StringComparison.OrdinalIgnoreCase) && path.EndsWith("_basecolor.png", StringComparison.OrdinalIgnoreCase))
            {
                var slug = path.Substring(ModelsRoot.Length).Split('/')[0];
                bool hero = heroSlugs.Contains(slug);
                cap = hero ? HeroCap : PropCap; quality = hero ? HeroQuality : PropQuality;
            }
            else if (path.StartsWith(VfxRoot, StringComparison.OrdinalIgnoreCase))
            {
                cap = SheetName.IsMatch(Path.GetFileNameWithoutExtension(path)) ? VfxSheetCap : VfxCap; quality = VfxQuality;
            }
            else return false;

            // A texture smaller than the cap keeps its own size (the cap never upsizes); the importer enforces that, we just state the cap.
            want = imp.GetPlatformTextureSettings(Platform);
            want.name = Platform;
            want.overridden = true;
            want.maxTextureSize = cap;
            want.format = TextureImporterFormat.Automatic;
            want.textureCompression = TextureImporterCompression.Compressed;
            want.crunchedCompression = crunchable;
            want.compressionQuality = crunchable ? quality : 50;
            want.allowsAlphaSplitting = false;
            return true;
        }

        private static bool Same(TextureImporterPlatformSettings a, TextureImporterPlatformSettings b) =>
            a.overridden == b.overridden && a.maxTextureSize == b.maxTextureSize && a.format == b.format && a.textureCompression == b.textureCompression
            && a.crunchedCompression == b.crunchedCompression && a.compressionQuality == b.compressionQuality && a.allowsAlphaSplitting == b.allowsAlphaSplitting;
    }
}
