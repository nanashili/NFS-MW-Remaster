using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving.Editor;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class RoadSurfaceTests
    {
        [Test]
        public void StreetCollisionResolvesPavementAndRaisedSidewalkSurfacesSeparately()
        {
            string folder = "Assets/RoadSurfaceTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            RoadNetworkAuthoring network = null; RoadAuthoring road = null;
            try
            {
                var asphalt = ScriptableObject.CreateInstance<SensorySurfaceProfile>(); asphalt.grip = 1;
                var concrete = ScriptableObject.CreateInstance<SensorySurfaceProfile>(); concrete.surface = SensorySurface.Concrete; concrete.grip = 0.8f;
                var material = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(asphalt, folder + "/Asphalt.asset"); AssetDatabase.CreateAsset(concrete, folder + "/Concrete.asset");
                AssetDatabase.CreateAsset(material, folder + "/Road.mat");
                var profile = RoadProfile.CreateLocalStreet();
                foreach (var band in profile.bands) { band.material = material; band.surface = band.kind == RoadBandKind.Driving ? asphalt : concrete; }
                AssetDatabase.CreateAsset(profile, folder + "/Street.asset");
                road = RoadAuthoringCommands.Create(profile, new[] { new Vector3(10000, 0, 10000), new Vector3(10000, 0, 10100) });
                network = RoadAuthoringCommands.CreateNetwork(new[] { road });
                RoadNetworkBake.Publish(network, folder + "/Bake.asset"); Physics.SyncTransforms();
                Assert.That(Physics.Raycast(new Vector3(10001.75f, 2, 10050), Vector3.down, out var pavement, 3), Is.True);
                Assert.That(pavement.collider.GetComponent<VehicleSurface>().Profile, Is.SameAs(asphalt));
                Assert.That(pavement.point.y, Is.EqualTo(0).Within(0.001));
                Assert.That(Physics.Raycast(new Vector3(9995.3f, 2, 10050), Vector3.down, out var sidewalk, 3), Is.True);
                Assert.That(sidewalk.collider.GetComponent<VehicleSurface>().Profile, Is.SameAs(concrete));
                Assert.That(sidewalk.point.y, Is.EqualTo(0.15f).Within(0.001));
            }
            finally
            {
                if (network != null) UnityEngine.Object.DestroyImmediate(network.gameObject);
                else if (road != null) UnityEngine.Object.DestroyImmediate(road.gameObject);
                AssetDatabase.DeleteAsset(folder); Undo.ClearAll();
            }
        }
    }
}
