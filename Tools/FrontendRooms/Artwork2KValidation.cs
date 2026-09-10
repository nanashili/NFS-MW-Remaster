// Run in the isolated project after copying the updated texture files.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class Artwork2KValidation
{
    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var manifest = JObject.Parse(File.ReadAllText("Art/FrontendRooms/Artwork2K/artwork-report.json"));
        var textures = new JArray();
        foreach (var artwork in manifest["artworks"])
        foreach (var destination in artwork["destinations"])
        {
            string path = (string)destination;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing importer: " + path);
            var platform = importer.GetPlatformTextureSettings("Standalone");
            if (importer.maxTextureSize != 2048 || !platform.overridden || platform.maxTextureSize != 2048 || platform.format != TextureImporterFormat.BC7)
            {
                importer.maxTextureSize = 2048;
                platform.overridden = true;
                platform.maxTextureSize = 2048;
                platform.format = TextureImporterFormat.BC7;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture || texture.width != (int)artwork["size"][0] || texture.height != (int)artwork["size"][1]
                || texture.isReadable || !texture.streamingMipmaps || texture.mipmapCount < 2 || !importer.sRGBTexture
                || importer.alphaSource != TextureImporterAlphaSource.FromInput)
                throw new InvalidOperationException("Artwork import mismatch: " + path);
            textures.Add(new JObject { ["path"] = path, ["width"] = texture.width, ["height"] = texture.height,
                ["format"] = texture.format.ToString(), ["mipCount"] = texture.mipmapCount,
                ["streaming"] = texture.streamingMipmaps, ["readable"] = texture.isReadable,
                ["sRGB"] = importer.sRGBTexture, ["alphaSource"] = importer.alphaSource.ToString() });
        }
        var models = new JArray();
        foreach (var item in ((JObject)manifest["models"]).Properties())
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(item.Name);
            if (!root) throw new InvalidOperationException("Missing model: " + item.Name);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            var meshes = root.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).Distinct().ToArray();
            if (materials.Any(m => !m || !m.shader || !m.shader.isSupported || m.shader.name.Contains("InternalError")))
                throw new InvalidOperationException("Invalid material: " + item.Name);
            var expected = manifest["artworks"].SelectMany(a => a["destinations"]).Select(p => (string)p)
                .Where(p => p.StartsWith(Path.GetDirectoryName(item.Name).Replace('\\', '/') + "/")).ToArray();
            var referenced = new HashSet<string>(materials.SelectMany(m => m.GetTexturePropertyNames()
                .Select(p => m.GetTexture(p))).Where(t => t).Select(AssetDatabase.GetAssetPath));
            if (expected.Any(p => !referenced.Contains(p))) throw new InvalidOperationException("Unbound artwork: " + item.Name);
            string posterboardTransform = null;
            if (manifest["posterboardOrientationCorrection"] != null && item.Name.Contains("FrontendSafeHouseRemake"))
            {
                var board = materials.Single(m => m.name.Contains("CRIB1_POSTERBOARD"));
                var transform = board.GetVector("baseColorTexture_ST");
                if (!Mathf.Approximately(transform.x, -1) || !Mathf.Approximately(transform.z, 1))
                    throw new InvalidOperationException("Posterboard orientation correction was not imported.");
                posterboardTransform = transform.ToString();
            }
            models.Add(new JObject { ["path"] = item.Name, ["materials"] = materials.Length,
                ["posterboardTextureTransform"] = posterboardTransform,
                ["triangles"] = meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(i => (long)m.GetIndexCount(i)) / 3),
                ["artworkTexturesBound"] = expected.Length,
                ["normals"] = meshes.All(m => m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)),
                ["tangents"] = meshes.All(m => m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)),
                ["lightmapUvs"] = meshes.All(m => m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1)) });
        }
        Directory.CreateDirectory("FrontendRoomsValidation");
        File.WriteAllText("FrontendRoomsValidation/artwork-2k-import-report.json", new JObject {
            ["unityVersion"] = Application.unityVersion, ["textures"] = textures, ["models"] = models
        }.ToString());
        AssetDatabase.SaveAssets();
        Debug.Log("ARTWORK_2K_IMPORT_PASS " + textures.Count + " texture copies, " + models.Count + " model packages");
    }
}
