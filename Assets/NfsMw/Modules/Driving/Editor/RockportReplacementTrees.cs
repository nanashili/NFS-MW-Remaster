using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Independent modeled trees. Reads planting markers, never source meshes or textures.</summary>
    public static class RockportReplacementTrees
    {
        public const string Folder = "Assets/NfsMw/Content/World/Maps/Rockport/Trees/Replacement/";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int>[] indices = { new List<int>(), new List<int>() };

            public void Branch(Vector3[] points, float radius, int sides)
            {
                int start = vertices.Count;
                for (int ring = 0; ring < points.Length; ring++)
                {
                    var tangent = (points[Mathf.Min(ring + 1, points.Length - 1)] - points[Mathf.Max(0, ring - 1)]).normalized;
                    var right = Vector3.Cross(tangent, Vector3.forward).normalized;
                    if (right.sqrMagnitude < .01f) right = Vector3.right;
                    var forward = Vector3.Cross(right, tangent).normalized;
                    float t = ring / (float)(points.Length - 1), r = radius * Mathf.Lerp(1, .06f, t);
                    for (int side = 0; side <= sides; side++)
                    {
                        float angle = side * Mathf.PI * 2 / sides;
                        Vector3 normal = Mathf.Cos(angle) * right + Mathf.Sin(angle) * forward;
                        vertices.Add(points[ring] + normal * r); normals.Add(normal); uv.Add(new Vector2(side / (float)sides, t * 5));
                        if (ring == 0 || side == sides) continue;
                        int a = start + (ring - 1) * (sides + 1) + side, b = a + sides + 1;
                        indices[0].AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    }
                }
            }

            public void Foliage(Vector3 center, Vector3 normal, float size, float roll, Vector3 crown)
            {
                var right = Vector3.Cross(normal, Vector3.up).normalized;
                if (right.sqrMagnitude < .01f) right = Vector3.right;
                var up = Vector3.Cross(right, normal).normalized;
                var rotation = Quaternion.AngleAxis(roll, normal); right = rotation * right * size * .5f; up = rotation * up * size * .65f;
                int n = vertices.Count;
                vertices.AddRange(new[] { center - right - up, center + right - up, center + right + up, center - right + up });
                var soft = Vector3.Lerp(normal, (center - crown).normalized, .7f).normalized;
                for (int i = 0; i < 4; i++) normals.Add(soft);
                uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                indices[1].AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2 });
            }

            public UnityEngine.Mesh Mesh(string name)
            {
                var mesh = new UnityEngine.Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.subMeshCount = 2;
                mesh.SetTriangles(indices[0], 0); mesh.SetTriangles(indices[1], 1); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                return mesh;
            }
        }

        private static float Range(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        private static Vector3 Direction(float angle) => new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));

        private static UnityEngine.Mesh Model(int species, int variant, int lod)
        {
            var random = new System.Random(7123 + species * 331 + variant * 71);
            var geometry = new Geometry();
            float lean = Range(random, -.035f, .035f);
            float radius = species == 3 ? .0095f : species == 2 ? .012f : .015f;
            int sides = lod == 0 ? 9 : lod == 1 ? 6 : 4;
            geometry.Branch(new[] { Vector3.zero, new Vector3(lean * .15f, .22f, 0), new Vector3(lean * .5f, .5f, lean * .3f), new Vector3(lean, .78f, 0), new Vector3(lean * .8f, 1, lean) }, radius, sides);
            bool conifer = species == 1, slender = species == 2, dead = species == 4;
            int branches = conifer ? 42 : slender ? 22 : 20;
            var crown = new Vector3(0, .72f, 0);
            for (int branch = 0; branch < branches; branch++)
            {
                random = new System.Random(7123 + species * 331 + variant * 71 + branch * 1771);
                float fraction = branch / (float)branches;
                float y = (conifer ? .18f : slender ? .26f : .32f) + fraction * (conifer ? .76f : .58f);
                float angle = branch * 2.399963f + Range(random, -.2f, .2f);
                var outward = Direction(angle);
                float spread = conifer ? .29f * (1 - fraction) : slender ? .105f * Mathf.Sin(fraction * Mathf.PI) + .035f : .31f * Mathf.Pow(Mathf.Sin((fraction * .8f + .1f) * Mathf.PI), .7f);
                spread *= Range(random, .78f, 1.12f);
                var start = new Vector3(lean * y, y, 0);
                var middle = start + outward * spread * .58f + Vector3.up * (conifer ? -.012f : .045f);
                var end = start + outward * spread + Vector3.up * (conifer ? .016f : .11f);
                if (lod < 2 || branch % 2 == 0) geometry.Branch(new[] { start, middle, end }, radius * (1 - fraction * .7f) * .38f, Math.Max(3, sides - 2));
                int twigs = conifer ? 3 : 4;
                for (int twig = 0; twig < twigs; twig++)
                {
                    random = new System.Random(9351 + species * 331 + variant * 71 + branch * 1771 + twig * 293);
                    float t = .45f + twig * .16f;
                    var at = Vector3.Lerp(middle, end, t);
                    var tip = at + Direction(angle + (twig % 2 == 0 ? .8f : -.8f)) * spread * .3f + Vector3.up * Range(random, .02f, .075f);
                    if (lod == 0) geometry.Branch(new[] { at, Vector3.Lerp(at, tip, .6f), tip }, radius * .09f, 4);
                    if (dead) continue;
                    int cards = lod == 0 ? 7 : lod == 1 ? 3 : 1;
                    float cardSize = (conifer ? .072f : slender ? .072f : .105f) * (lod == 0 ? 1 : lod == 1 ? 1.35f : 2.15f);
                    for (int card = 0; card < cards; card++)
                    {
                        var offset = new Vector3(Range(random, -.05f, .05f), Range(random, -.035f, .045f), Range(random, -.05f, .05f));
                        var normal = new Vector3(Range(random, -1, 1), Range(random, -.15f, 1), Range(random, -1, 1)).normalized;
                        geometry.Foliage(tip + offset, normal, cardSize * Range(random, .75f, 1.2f), Range(random, 0, 360), crown);
                    }
                }
            }
            return geometry.Mesh("Independent tree " + species + " variant " + variant + " LOD" + lod);
        }

        private static void Texture(string name, bool foliage, bool needles = false)
        {
            int size = 512; var texture = new Texture2D(size, size, TextureFormat.RGBA32, true); var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1), v = y / (float)(size - 1);
                if (!foliage)
                {
                    float grain = Mathf.PerlinNoise(u * 38, v * 3) * .4f + Mathf.PerlinNoise(u * 110, v * 7) * .25f + .3f;
                    float fissure = Mathf.Pow(Mathf.Abs(Mathf.Sin(u * 97 + Mathf.PerlinNoise(u * 5, v * 7) * 5)), 9);
                    pixels[y * size + x] = new Color(grain * .65f - fissure * .10f, grain * .47f - fissure * .08f, grain * .31f - fissure * .04f, 1);
                    continue;
                }
                Color color = new Color(.30f, .43f, .12f, 0);
                if (needles)
                {
                    float nearest = 1;
                    for (int needle = 0; needle < 32; needle++)
                    {
                        float stem = .08f + needle / 2 * .05f, side = needle % 2 == 0 ? -1 : 1;
                        var a = new Vector2(.5f, stem); var b = new Vector2(.5f + side * (.3f - stem * .12f), stem + .17f);
                        var edge = b - a; float t = Mathf.Clamp01(Vector2.Dot(new Vector2(u, v) - a, edge) / edge.sqrMagnitude);
                        nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(u, v), a + edge * t));
                    }
                    if (nearest < .013f) color = new Color(.42f, .68f, .38f, Mathf.Clamp01((.013f - nearest) * 300));
                    if (Mathf.Abs(u - .5f) < .009f && v > .05f && v < .97f) color = new Color(.26f, .32f, .12f, 1);
                    pixels[y * size + x] = color; continue;
                }
                for (int leaf = 0; leaf < 9; leaf++)
                {
                    float cy = .13f + leaf * .082f, cx = .5f + (leaf % 2 == 0 ? -.18f : .18f) * (1 - leaf * .035f);
                    float a = leaf % 2 == 0 ? -.62f : .62f, dx = u - cx, dy = v - cy;
                    float across = (Mathf.Cos(a) * dx - Mathf.Sin(a) * dy) / .105f;
                    float along = (Mathf.Sin(a) * dx + Mathf.Cos(a) * dy) / .185f;
                    float oval = across * across + along * along;
                    float serration = Mathf.Sin(Mathf.Atan2(along, across) * 18) * .045f;
                    if (oval > 1 + serration) continue;
                    float vein = Mathf.Exp(-Mathf.Abs(across) * 60) * .12f;
                    float light = .66f + .18f * Mathf.PerlinNoise(u * 75, v * 75) + vein + (1 - oval) * .14f;
                    color = new Color(light * .70f, light * .83f, light * .40f, Mathf.Clamp01((1 + serration - oval) * 40));
                }
                if (Mathf.Abs(u - (.5f + Mathf.Sin(v * 4) * .012f)) < .008f && v > .05f && v < .95f) color = new Color(.31f, .29f, .12f, 1);
                pixels[y * size + x] = color;
            }
            texture.SetPixels32(pixels); texture.Apply(); string path = Folder + "Textures/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.alphaIsTransparency = foliage; importer.mipmapEnabled = true;
            importer.wrapMode = foliage ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; importer.anisoLevel = 4; importer.SaveAndReimport();
        }

        public static void CreateLibrary()
        {
            foreach (string sub in new[] { "Meshes", "Textures", "Materials", "Prefabs" }) Directory.CreateDirectory(Folder + sub);
            AssetDatabase.Refresh();
            if (!File.Exists(Folder + "Textures/Bark.png")) Texture("Bark", false);
            if (!File.Exists(Folder + "Textures/LeafCluster.png")) Texture("LeafCluster", true);
            if (!File.Exists(Folder + "Textures/NeedleCluster.png")) Texture("NeedleCluster", true, true);
            for (int kind = 0; kind < 5; kind++) for (int variant = 0; variant < 4; variant++)
            {
                for (int part = 0; part < 2; part++)
                {
                    string path = Folder + "Materials/Tree_" + kind + "_" + variant + "_" + part + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path); bool existing = material;
                    if (!material) material = new Material(Shader.Find("HDRP/Lit")) { name = "New " + (part == 0 ? "bark" : "foliage") + " " + kind + " " + variant, enableInstancing = true };
                    material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Textures/" + (part == 0 ? "Bark" : kind == 1 ? "NeedleCluster" : "LeafCluster") + ".png"));
                    Color tint = part == 0 ? new Color(.75f, .73f, .66f) : kind == 1 ? new Color(.24f, .40f, .29f) : kind == 2 ? new Color(.42f, .58f, .27f) : variant == 0 ? new Color(.72f, .72f, .28f) : variant == 1 ? new Color(.90f, .52f, .14f) : variant == 2 ? new Color(.38f, .62f, .23f) : new Color(.85f, .36f, .13f);
                    material.SetColor("_BaseColor", tint); material.SetFloat("_Smoothness", part == 0 ? .15f : .28f);
                    if (part == 1) { material.SetFloat("_AlphaCutoffEnable", 1); material.SetFloat("_AlphaCutoff", .4f); material.SetFloat("_DoubleSidedEnable", 1); material.SetFloat("_DoubleSidedNormalMode", 0); }
                    HDMaterial.ValidateMaterial(material); if (existing) EditorUtility.SetDirty(material); else AssetDatabase.CreateAsset(material, path);
                }
                for (int lod = 0; lod < 3; lod++)
                {
                    string path = Folder + "Meshes/Tree_" + kind + "_" + variant + "_LOD" + lod + ".asset";
                    var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(path); var model = Model(kind, variant, lod);
                    if (existing) { EditorUtility.CopySerialized(model, existing); EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(model); }
                    else AssetDatabase.CreateAsset(model, path);
                }
            }
            AssetDatabase.SaveAssets();
        }

        public static void CreatePrefabs(int limit)
        {
            var markers = new Dictionary<int, List<string[]>>();
            foreach (string line in File.ReadAllLines(Folder + "planting-markers.tsv"))
            { var fields = line.Split('\t'); int index = int.Parse(fields[0], Invariant); if (!markers.TryGetValue(index, out var list)) markers[index] = list = new List<string[]>(); list.Add(fields); }
            var names = File.ReadAllLines("Assets/NfsMw/Content/World/Maps/Rockport/Trees/prototypes.tsv"); int created = 0, total = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int index = 0; index < names.Length; index++)
                {
                    string path = Folder + "Prefabs/" + names[index] + ".prefab";
                    if (File.Exists(path)) { total++; continue; }
                    if (created >= limit) continue;
                    var root = new GameObject(names[index]);
                    try
                    {
                        var renderers = new[] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
                        foreach (var fields in markers[index])
                        {
                            var position = new Vector3(float.Parse(fields[1], Invariant), float.Parse(fields[2], Invariant), float.Parse(fields[3], Invariant));
                            float height = float.Parse(fields[4], Invariant); int kind = int.Parse(fields[6], Invariant), seed = int.Parse(fields[7], Invariant), variant = seed % 4;
                            var rotation = Quaternion.Euler(0, seed * 137.508f % 360, 0);
                            for (int lod = 0; lod < 3; lod++)
                            {
                                var child = new GameObject("New tree " + seed + " LOD" + lod); child.transform.SetParent(root.transform, false);
                                child.transform.localPosition = position; child.transform.localRotation = rotation; child.transform.localScale = Vector3.one * height;
                                child.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(Folder + "Meshes/Tree_" + kind + "_" + variant + "_LOD" + lod + ".asset");
                                var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>(Folder + "Materials/Tree_" + kind + "_" + variant + "_0.mat"), AssetDatabase.LoadAssetAtPath<Material>(Folder + "Materials/Tree_" + kind + "_" + variant + "_1.mat") };
                                renderer.shadowCastingMode = ShadowCastingMode.On; renderers[lod].Add(renderer);
                            }
                        }
                        var group = root.AddComponent<LODGroup>(); group.SetLODs(new[] { new LOD(.22f, renderers[0].ToArray()), new LOD(.065f, renderers[1].ToArray()), new LOD(.001f, renderers[2].ToArray()) }); group.RecalculateBounds();
                        PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved); if (!saved) throw new IOException("Could not save replacement tree " + index);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                    created++; total++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets(); File.WriteAllText("Art/RockportTrees/Source/replacement-prefab-progress.txt", total + "/" + names.Length);
            Debug.Log("NEW_TREE_PREFABS created=" + created + " total=" + total + "/" + names.Length);
        }
    }
}
