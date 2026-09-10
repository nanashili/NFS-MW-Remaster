using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Temporary Play Mode fixtures; stopping Play Mode restores the authored map.</summary>
    [InitializeOnLoad]
    public static class RockportOceanSmoke
    {
        private const string Key = "RockportOceanSmoke.Active";
        private static double deadline;
        private static float began, minWave, maxWave;
        private static int stage, waveQueries;
        private static RockportOcean ocean;
        private static Rigidbody floating, sinking, dry, impactor;
        private static OceanBuoyantBody floatHull, dryHull;
        private static DestructibleProp tree;
        private static Vector3 treeStart;
        private static Quaternion treeRotation;
        private static RenderTexture target;
        private static bool collisionBrokeTree;

        static RockportOceanSmoke()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.delayCall += Resume;
        }

        public static void Run()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != "Assets/NfsMw/Scenes/World/RockportMap.unity")
                throw new InvalidOperationException("Run in the saved RockportMap edit scene.");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
            Resume();
        }

        private static void Resume()
        {
            deadline = EditorApplication.timeSinceStartup + 240;
            stage = waveQueries = 0; minWave = float.PositiveInfinity; maxWave = float.NegativeInfinity;
            collisionBrokeTree = false;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        private static Rigidbody Cube(string name, Vector3 position, float mass, bool gravity)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.position = position;
            var body = obj.AddComponent<Rigidbody>(); body.mass = mass; body.useGravity = gravity;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return body;
        }

        private static void Setup()
        {
            ocean = UnityEngine.Object.FindAnyObjectByType<RockportOcean>();
            Require(ocean && ocean.Contains(new Vector3(-6000, 0, 0)), "Ocean test position outside source coastline.");
            Require(!ocean.Contains(Vector3.zero), "Land origin incorrectly covered by ocean.");
            var camera = new GameObject("TEMP ocean physics camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(-6000, 5, -14); camera.transform.LookAt(new Vector3(-6000, 0, 0));
            camera.nearClipPlane = .1f; camera.farClipPlane = 200;
            camera.gameObject.AddComponent<HDAdditionalCameraData>();
            target = new RenderTexture(256, 256, 24); target.Create(); camera.targetTexture = target;
            began = Time.time;
        }

        private static void Fixtures()
        {
            floating = Cube("TEMP 500kg one cubic metre float", new Vector3(-6000, 3, 0), 500, true);
            // Deliberately omit the component: the ocean trigger must discover and enroll this body.
            sinking = Cube("TEMP 2500kg one cubic metre sink", new Vector3(-6005, 3, 0), 2500, true);
            sinking.gameObject.AddComponent<OceanBuoyantBody>().Configure(ocean, new Bounds(Vector3.zero, Vector3.one), 1);
            dry = Cube("TEMP dry control", new Vector3(0, -10, 0), 500, false);
            dryHull = dry.gameObject.AddComponent<OceanBuoyantBody>(); dryHull.Configure(ocean, new Bounds(Vector3.zero, Vector3.one), 1);
            floating.linearVelocity = new Vector3(3, 0, 0);
            DestructibleProp source = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<DestructibleProp>(FindObjectsSortMode.None))
                if (candidate.StableId.StartsWith("rockport.tree.")) { source = candidate; break; }
            Require(source, "No source breakable tree.");
            var clone = UnityEngine.Object.Instantiate(source.gameObject, new Vector3(1000, 500, 1000), Quaternion.identity);
            clone.name = "TEMP vehicle collision tree"; tree = clone.GetComponent<DestructibleProp>();
            treeStart = clone.transform.position; treeRotation = clone.transform.rotation;
            Physics.SyncTransforms(); var trunk = clone.GetComponent<CapsuleCollider>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "TEMP tree floor";
            floor.transform.position = new Vector3(trunk.bounds.center.x, trunk.bounds.min.y - .5f, trunk.bounds.center.z);
            floor.transform.localScale = new Vector3(40, 1, 40);
            var point = trunk.bounds.center; point.y = trunk.bounds.min.y + Mathf.Min(1, trunk.bounds.size.y * .3f);
            impactor = Cube("TEMP vehicle impactor", point + Vector3.left * 4, 1200, false);
            impactor.gameObject.AddComponent<VehicleController>().enabled = false;
            impactor.linearVelocity = Vector3.right * 16;
            began = Time.time;
        }

        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Ocean smoke stage " + stage);
                if (!EditorApplication.isPlaying || Time.time < .1f) return;
                if (stage == 0) { Setup(); stage = 1; return; }
                bool sampled = ocean.TrySample(new Vector3(-6000, 0, 0), out float height, out _, out _);
                if (sampled) { minWave = Mathf.Min(minWave, height); maxWave = Mathf.Max(maxWave, height); waveQueries++; }
                if (stage == 1)
                {
                    if (!sampled) return;
                    Fixtures(); stage = 2; return;
                }
                if (tree.IsBroken && !tree.GetComponent<Rigidbody>().isKinematic) collisionBrokeTree = true;
                if (Time.time - began < 20) return;
                floatHull = floating.GetComponent<OceanBuoyantBody>();
                Require(waveQueries > 20 && maxWave - minWave > .005f, "HDRP spectral wave height did not change.");
                Require(floatHull && floatHull.SuccessfulWaveQueries == 8, "Ocean trigger did not enroll floating body or CPU wave queries failed.");
                Require(ocean.TrySample(floating.position, out float floatWater, out _, out _), "Floating body left source footprint.");
                Require(Mathf.Abs(floating.position.y - floatWater) < 1.5f && floatHull.SubmergedFraction > .05f, "Low density body did not float.");
                Require(sinking.position.y < -5, "High density body did not sink.");
                Require(Mathf.Abs(floating.linearVelocity.x) < 2, "Water drag did not reduce lateral velocity.");
                Require(dryHull.SuccessfulWaveQueries == 0 && dryHull.LastBuoyancyForce == Vector3.zero && dry.position.y == -10, "Ocean forces leaked onto land.");
                Require(collisionBrokeTree, "Vehicle collider did not break tree.");
                float treeMovement = Vector3.Distance(tree.transform.position, treeStart);
                float treeAngle = Quaternion.Angle(tree.transform.rotation, treeRotation);
                Require(treeMovement > .2f || treeAngle > 10, "Released tree did not move through PhysX.");
                tree.ResetProp();
                Require(!tree.IsBroken && tree.GetComponent<Rigidbody>().isKinematic && Vector3.Distance(tree.transform.position, treeStart) < .001f, "Tree reset failed.");
                Finish("{\"status\":\"PASS\",\"simulationSeconds\":" + (Time.time-began).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"waveHeightRangeMetres\":" + (maxWave-minWave).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"floatHeightMetres\":" + floating.position.y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"sinkHeightMetres\":" + sinking.position.y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"waveQueries\":" + waveQueries + ",\"vehicleCollisionBreak\":true,\"treeReset\":true,\"automaticEnrollment\":true,\"dryLandNoForce\":true,\"lateralDrag\":true}");
            }
            catch (Exception exception) { Finish("{\"status\":\"FAIL\",\"error\":\"" + exception.Message.Replace("\"", "'") + "\"}"); Debug.LogException(exception); }
        }

        private static void Finish(string json)
        {
            EditorApplication.update -= Poll; SessionState.SetBool(Key, false);
            Directory.CreateDirectory("Art/RockportOcean/Source"); File.WriteAllText("Art/RockportOcean/Source/ocean-playmode-verification.json", json);
            Debug.Log("ROCKPORT_OCEAN_SMOKE " + json);
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorApplication.isPlaying = false;
        }
    }
}
