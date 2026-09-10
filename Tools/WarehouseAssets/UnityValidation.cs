// Task-owned import verification. Run in an isolated copy of the Unity project.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

public static class WarehouseUnityValidation
{
    const string Root = "Assets/NfsMw/Content/Frontend/Models/FrontendWarehouse";
    const string Model = Root + "/Warehouse.gltf";

    [Serializable] public class TextureInfo
    {
        public string path, format;
        public int width, height, mipCount;
        public long residentBytes;
        public bool readable, streaming;
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
        var textures = new List<TextureInfo>();
        foreach (var file in Directory.GetFiles(Root + "/Textures", "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(file);
            bool normal = Path.GetFileName(file).Contains("Normal_2K");
            var previous = importer.GetPlatformTextureSettings("Standalone");
            bool needsImport = !previous.overridden || previous.format != (normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7)
                || importer.isReadable || !importer.streamingMipmaps || !importer.mipmapEnabled || importer.anisoLevel != 4
                || importer.maxTextureSize != 2048 || importer.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = !normal && !file.Contains("BaseColor_2K");
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
            textures.Add(new TextureInfo { path=file, format=texture.format.ToString(), width=texture.width, height=texture.height, mipCount=texture.mipmapCount, residentBytes=Profiler.GetRuntimeMemorySizeLong(texture), readable=texture.isReadable, streaming=texture.streamingMipmaps });
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
        Directory.CreateDirectory("WarehouseValidation");
        File.WriteAllText("WarehouseValidation/import-report.json", JsonUtility.ToJson(report,true));
        if (report.triangles != 11745 || report.cameras != 0 || report.lights != 0 || report.invalidMaterials != 0 || missing != 0 || !report.allHaveNormals || !report.allHaveTangents || !report.allHaveLightmapUvs)
            throw new InvalidOperationException("Warehouse import failed validation; see report.");
        if (textures.Any(t=>t.readable || !t.streaming || t.mipCount < 2 || Math.Max(t.width,t.height)>2048))
            throw new InvalidOperationException("Texture import settings failed validation.");
        AssetDatabase.SaveAssets();
        Debug.Log("WAREHOUSE_IMPORT_PASS " + report.triangles + " triangles, " + report.materialCount + " materials, " + report.textureResidentBytes + " texture bytes");
    }
}
