using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TestTools;
namespace NfsMwRemaster.Lighting.Tests
{
    public sealed class LightingRuntimeTests
    {
        [UnityTest] public IEnumerator OwnedStateExistsBeforeFirstRenderAndRestoresOnDisable()
        {
            var root=new GameObject("Lighting runtime test");root.SetActive(false);
            var profile=ScriptableObject.CreateInstance<AtmosphereProfile>();profile.lowFixtureIntensity=.2f;
            var cameraObject=new GameObject("World camera");cameraObject.transform.SetParent(root.transform);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            var data=camera.GetLightingCameraData();LightingCamera.SetPostProcessing(data,false);data.volumeLayerMask=1;
            var owner=root.AddComponent<AtmosphereController>();owner.fallback=profile;owner.worldCamera=camera;owner.quality=LightingQuality.Low;
            var lamp=new GameObject("Lamp");lamp.transform.SetParent(root.transform);var fixture=lamp.AddComponent<LightingFixture>();fixture.source=lamp.AddComponent<Light>();fixture.role=FixtureRole.Street;fixture.source.intensity=5;fixture.source.shadows=LightShadows.Hard;
            var signal=new GameObject("Signal");signal.transform.SetParent(root.transform);var critical=signal.AddComponent<LightingFixture>();critical.source=signal.AddComponent<Light>();critical.role=FixtureRole.Signal;critical.source.intensity=3;owner.fixtures=new[]{fixture,critical};
            try
            {
                root.SetActive(true);
                Assert.AreEqual(profile.look.keyIntensity,owner.Effective.keyIntensity,"Prepared during OnEnable, before HDRP evaluates camera volumes.");
                Assert.IsTrue(data.renderingPathCustomFrameSettings.IsEnabled(FrameSettingsField.Postprocess));
                Assert.AreNotEqual(0,data.volumeLayerMask.value&AtmosphereController.RuntimeVolumeMask);
                yield return null;
                Assert.AreEqual(1,fixture.source.intensity,.001f);Assert.AreEqual(3,critical.source.intensity,.001f);Assert.AreEqual(LightShadows.None,fixture.source.shadows);
                yield return null;Assert.AreEqual(1,fixture.source.intensity,.001f,"Quality scaling must not compound each frame.");
                owner.enabled=false;
                Assert.AreEqual(5,fixture.source.intensity);Assert.AreEqual(LightShadows.Hard,fixture.source.shadows);Assert.AreEqual(3,critical.source.intensity);
                Assert.AreEqual(1,data.volumeLayerMask.value);Assert.IsFalse(data.renderingPathCustomFrameSettings.IsEnabled(FrameSettingsField.Postprocess));
            }
            finally{Object.Destroy(root);Object.Destroy(profile);}
            yield return null;
        }
        [UnityTest] public IEnumerator IndependentCamerasHaveIsolatedVolumesAndReleaseTheirLayers()
        {
            var profile=ScriptableObject.CreateInstance<AtmosphereProfile>();var roots=new GameObject[2];var owners=new AtmosphereController[2];var cameras=new HDAdditionalCameraData[2];
            try
            {
                for(int i=0;i<2;i++)
                {
                    roots[i]=new GameObject("Camera owner "+i);roots[i].SetActive(false);
                    var camera=roots[i].AddComponent<Camera>();camera.enabled=false;cameras[i]=camera.GetLightingCameraData();cameras[i].volumeLayerMask=1;
                    owners[i]=roots[i].AddComponent<AtmosphereController>();owners[i].fallback=profile;owners[i].worldCamera=camera;roots[i].SetActive(true);
                }
                Assert.AreEqual(0,cameras[0].volumeLayerMask.value&cameras[1].volumeLayerMask.value&AtmosphereController.RuntimeVolumeMask);
                int layer=cameras[0].volumeLayerMask.value;owners[0].enabled=false;Assert.AreEqual(1,cameras[0].volumeLayerMask.value);
                owners[0].enabled=true;Assert.AreEqual(layer,cameras[0].volumeLayerMask.value);
                yield return null;LogAssert.NoUnexpectedReceived();
            }
            finally{foreach(var root in roots)if(root)Object.Destroy(root);Object.Destroy(profile);}
            yield return null;
        }
    }
}
