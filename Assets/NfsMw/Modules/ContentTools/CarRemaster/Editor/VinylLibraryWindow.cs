using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.CarRemaster.Editor
{
    [Serializable] public sealed class VinylEntry
    {
        public string name, hash, asset, role, sha256, sourceAsset, sourceKind;
        public int width, height, alphaUsage;
    }
    [Serializable] public sealed class VinylArchive { public string source; public VinylEntry[] textures; }
    [Serializable] public sealed class VinylCatalog { public VinylArchive[] archives; }
    public sealed class VinylLibraryWindow : EditorWindow
    {
        const string VinylRoot = "Assets/NfsMw/Content/Customization/Vinyls";
        const string CatalogPath = VinylRoot + "/catalog.json";
        VinylEntry[] entries = Array.Empty<VinylEntry>();
        VinylEntry selected;
        Texture2D preview;
        string search = "";
        string kind = "All";
        string[] kinds = new[] { "All" };
        Vector2 scroll;
        [MenuItem("Tools/NFS MW/Modifications/Browse Vinyls")]
        public static void Open() => GetWindow<VinylLibraryWindow>("MW Vinyls");
        void OnEnable() => Reload();
        void Reload()
        {
            if (!File.Exists(CatalogPath))
            {
                entries = Array.Empty<VinylEntry>();
                kinds = new[] { "All" };
                return;
            }
            var catalog = JsonUtility.FromJson<VinylCatalog>(File.ReadAllText(CatalogPath));
            entries = catalog?.archives?.Where(a => a?.textures != null).SelectMany(a => a.textures).ToArray()
                ?? Array.Empty<VinylEntry>();
            kinds = new[] { "All" }.Concat(entries.Select(e => TypeFor(e)).Distinct().OrderBy(k => k)).ToArray();
            if (!kinds.Contains(kind)) kind = "All";
        }
        void OnDisable() { if (preview != null) DestroyImmediate(preview); }
        static string TypeFor(VinylEntry entry) => string.Equals(entry.sourceKind, "PREVINYL", StringComparison.OrdinalIgnoreCase)
            ? "PreVinyl" : entry.role == "mask" ? "Mask" : "Artwork";
        static string CanonicalPath(VinylEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.asset) ||
                !entry.asset.StartsWith(VinylRoot + "/", StringComparison.Ordinal) || entry.asset.Contains(".."))
                throw new InvalidDataException("Unexpected canonical vinyl path.");
            return entry.asset;
        }
        public static Texture2D Import(VinylEntry entry)
        {
            var path = CanonicalPath(entry);
            if (!File.Exists(path)) throw new FileNotFoundException("Canonical vinyl asset is missing.", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidDataException("Unity did not create a texture importer for " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = entry.role != "mask";
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        [MenuItem("Tools/NFS MW/Modifications/Validate Vinyl Sample Imports")]
        public static void ValidateSamples()
        {
            if (!File.Exists(CatalogPath)) throw new FileNotFoundException("Vinyl catalog is missing.", CatalogPath);
            var entries = JsonUtility.FromJson<VinylCatalog>(File.ReadAllText(CatalogPath)).archives.SelectMany(a => a.textures).ToArray();
            var samples = new[] { entries.First(e => e.role == "mask"), entries.First(e => e.role != "mask"),
                entries.First(e => string.Equals(e.sourceKind, "PREVINYL", StringComparison.OrdinalIgnoreCase)), entries.Last() };
            foreach (var entry in samples)
            {
                var texture = Import(entry);
                if (texture == null || texture.width != entry.width || texture.height != entry.height)
                    throw new InvalidOperationException("Vinyl import failed: " + entry.asset);
            }
            Debug.Log("Vinyl catalog: " + entries.Length + " records; " + samples.Length + " native sample imports passed.");
        }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Recovered artwork and masks. Select an image to preview it, then import it when needed. Original paint compositing and vehicle material assignment are not automatic.", MessageType.Info);
            search = EditorGUILayout.TextField("Search car or vinyl", search);
            using (new EditorGUILayout.HorizontalScope())
            {
                kind = kinds[EditorGUILayout.Popup("Type", Array.IndexOf(kinds, kind), kinds)];
                if (GUILayout.Button("Refresh", GUILayout.Width(70))) Reload();
            }
            var matches = entries.Where(e => (kind == "All" || TypeFor(e) == kind) &&
                (e.name + " " + e.asset).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            EditorGUILayout.LabelField(matches.Length + " matches — showing first 100.");
            using (new EditorGUILayout.HorizontalScope())
            {
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinWidth(250));
                foreach (var entry in matches.Take(100))
                {
                    if (GUILayout.Button(entry.name + " · " + entry.role))
                    {
                        selected = entry;
                        if (preview != null) DestroyImmediate(preview);
                        preview = new Texture2D(2, 2, TextureFormat.RGBA32, false, entry.role == "mask");
                        if (!ImageConversion.LoadImage(preview, File.ReadAllBytes(CanonicalPath(entry)))) throw new InvalidDataException("Invalid PNG " + entry.asset);
                    }
                }
                EditorGUILayout.EndScrollView();
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(240, position.width * .45f))))
                {
                    if (selected != null)
                    {
                        EditorGUILayout.LabelField(selected.name, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(TypeFor(selected) + " · " + selected.width + " × " + selected.height + " · " + selected.role);
                        EditorGUILayout.SelectableLabel(selected.asset, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        var rect = GUILayoutUtility.GetAspectRect(1);
                        if (preview != null) EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
                        if (GUILayout.Button("Import and select texture")) Selection.activeObject = Import(selected);
                    }
                }
            }
        }
    }
}
