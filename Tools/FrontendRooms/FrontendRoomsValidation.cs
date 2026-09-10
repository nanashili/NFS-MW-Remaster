// Task-owned import verification. Run in an isolated copy of the Unity project.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Newtonsoft.Json.Linq;

public static class FrontendRoomsValidation
{

    [Serializable] public class TextureInfo
    {
        public string path, format;
        public int width, height, mipCount;
        public long residentBytes;
        public bool readable, streaming, sRGB;
    }
    [Serializable] public class Report
    {
        public string unityVersion, renderPipeline;
        public int triangles, vertices, meshCount, materialCount, submeshes;
        public int cameras, lights, missingTextures, invalidMaterials;
        public bool allHaveNormals, allHaveTangents, allHaveLightmapUvs;
        public long textureResidentBytes;
        public string[] shaders;
        public TextureInfo[] textures;
    }

    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string room in new[] { "SafeHouse", "Showroom", "Performance", "Visual" }) Validate(room);
        Debug.Log("ALL_FOUR_ROOMS_IMPORT_PASS");
    }
    public static void RunSafeHouse()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Validate("SafeHouse");
    }
    static void Validate(string room)
    {
        string Root = "Assets/NfsMw/Content/Frontend/Models/Frontend" + room + "Remake";
        string Model = Root + "/" + room + ".gltf";
        var gltf = JObject.Parse(File.ReadAllText(Model));
        var normalFiles = new HashSet<string>();
        var linearFiles = new HashSet<string>();
        string ImageFile(JToken textureInfo) {
            int index = (int)textureInfo["index"];
            int source = (int)gltf["textures"][index]["source"];
            return Uri.UnescapeDataString((string)gltf["images"][source]["uri"]);
        }
        foreach (var m in gltf["materials"]) {
            if (m["normalTexture"] != null) normalFiles.Add(ImageFile(m["normalTexture"]));
            if (m["pbrMetallicRoughness"]?["metallicRoughnessTexture"] != null) linearFiles.Add(ImageFile(m["pbrMetallicRoughness"]["metallicRoughnessTexture"]));
        }
        foreach (var im in gltf["images"]) {
            if (!File.Exists(Root + "/" + Uri.UnescapeDataString((string)im["uri"]))) throw new InvalidOperationException("Missing glTF texture image " + im["uri"]);
        }
        var textures = new List<TextureInfo>();
        foreach (var file in gltf["images"].Select(im=>Root + "/" + Uri.UnescapeDataString((string)im["uri"])).Distinct())
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(file);
            bool normal = normalFiles.Contains("Textures/" + Path.GetFileName(file));
            bool packed = linearFiles.Contains("Textures/" + Path.GetFileName(file));
            var previous = importer.GetPlatformTextureSettings("Standalone");
            bool needsImport = !previous.overridden || previous.format != (normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7)
                || importer.isReadable || !importer.streamingMipmaps || !importer.mipmapEnabled || importer.anisoLevel != 4
                || importer.maxTextureSize != 2048 || importer.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default)
                || importer.sRGBTexture != (!normal && !packed);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !packed;
            importer.alphaSource = normal || packed ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = !normal && !packed && !file.Contains("BaseColor_2K");
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.isReadable = false;
            importer.anisoLevel = 4;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 90;
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = 2048;
            standalone.format = normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7;
            importer.SetPlatformTextureSettings(standalone);
            if (needsImport) importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(file);
            if (!texture) throw new InvalidOperationException("Missing texture: " + file);
            textures.Add(new TextureInfo { path=file, format=texture.format.ToString(), width=texture.width, height=texture.height, mipCount=texture.mipmapCount, residentBytes=Profiler.GetRuntimeMemorySizeLong(texture), readable=texture.isReadable, streaming=texture.streamingMipmaps, sRGB=importer.sRGBTexture });
        }
        var gltfImporter = AssetImporter.GetAtPath(Model);
        var serialized = new SerializedObject(gltfImporter);
        serialized.FindProperty("editorImportSettings.generateSecondaryUVSet").boolValue=true;
        serialized.FindProperty("importSettings.generateMipMaps").boolValue=true;
        serialized.FindProperty("importSettings.texturesReadable").boolValue=false;
        serialized.FindProperty("importSettings.anisotropicFilterLevel").intValue=4;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        gltfImporter.SaveAndReimport();
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (!root) throw new InvalidOperationException("glTF import returned no GameObject.");
        var meshes = root.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.sharedMesh).Distinct().ToArray();
        var materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(x=>x.sharedMaterials).Distinct().ToArray();
        int missing=0;
        foreach (var material in materials)
        {
            if (!material) { missing++; continue; }
            foreach (var property in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(property);
                if (texture && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture))) missing++;
            }
        }
        var report = new Report {
            unityVersion=Application.unityVersion,
            renderPipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline?.GetType().FullName,
            triangles=meshes.Sum(m=>(int)Enumerable.Range(0,m.subMeshCount).Sum(i=>(long)m.GetIndexCount(i))/3),
            vertices=meshes.Sum(m=>m.vertexCount), meshCount=meshes.Length,
            materialCount=materials.Length, submeshes=meshes.Sum(m=>m.subMeshCount),
            cameras=root.GetComponentsInChildren<Camera>(true).Length,
            lights=root.GetComponentsInChildren<Light>(true).Length,
            missingTextures=missing,
            invalidMaterials=materials.Count(m=>!m || !m.shader || m.shader.name.Contains("InternalError") || !m.shader.isSupported),
            allHaveNormals=meshes.All(m=>m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)),
            allHaveTangents=meshes.All(m=>m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)),
            allHaveLightmapUvs=meshes.All(m=>m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1)),
            textureResidentBytes=textures.Sum(t=>t.residentBytes),
            shaders=materials.Where(m=>m && m.shader).Select(m=>m.shader.name).Distinct().ToArray(),
            textures=textures.ToArray()
        };
        Directory.CreateDirectory("FrontendRoomsValidation");
        File.WriteAllText("FrontendRoomsValidation/" + room + "-import-report.json", JsonUtility.ToJson(report,true));
        int expectedTriangles = gltf["meshes"].SelectMany(m=>m["primitives"]).Sum(p=>(int)gltf["accessors"][(int)p["indices"]]["count"] / 3);
        int triangleBudget = room == "SafeHouse" ? 220000 : 50000;
        if (report.triangles != expectedTriangles || report.materialCount != gltf["materials"].Count() || report.triangles > triangleBudget || report.cameras != 0 || report.lights != 0 || report.invalidMaterials != 0 || missing != 0 || !report.allHaveNormals || !report.allHaveTangents || !report.allHaveLightmapUvs)
            throw new InvalidOperationException(room + " import failed validation; see report.");
        if (textures.Any(t=>t.readable || !t.streaming || (Math.Max(t.width,t.height)>1 && t.mipCount < 2) || Math.Max(t.width,t.height)>2048))
            throw new InvalidOperationException("Texture import settings failed validation.");
        if (textures.Any(t=>(linearFiles.Contains("Textures/" + Path.GetFileName(t.path)) || normalFiles.Contains("Textures/" + Path.GetFileName(t.path))) && t.sRGB))
            throw new InvalidOperationException("Normal and packed material data must import in linear colour space.");
        AssetDatabase.SaveAssets();
        Debug.Log(room + "_IMPORT_PASS " + report.triangles + " triangles, " + report.materialCount + " materials, " + report.textureResidentBytes + " texture bytes");
    }
}
