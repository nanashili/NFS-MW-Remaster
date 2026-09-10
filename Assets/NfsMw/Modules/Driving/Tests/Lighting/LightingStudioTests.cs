using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Lighting.Editor;
using Object=UnityEngine.Object;
namespace NfsMwRemaster.Lighting.Tests
{
    public sealed class LightingStudioTests
    {
        Scene scene;List<Object> assets;AtmosphereProfile baseline;AtmosphereController owner;
        [SetUp] public void Setup(){scene=EditorSceneManager.NewPreviewScene();assets=new List<Object>();baseline=Profile();owner=Go("Owner").AddComponent<AtmosphereController>();owner.fallback=baseline;owner.worldCamera=Go("Camera").AddComponent<Camera>();owner.worldCamera.GetLightingCameraData();var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(cube,scene);owner.bakeGeometry=new[]{cube.GetComponent<Renderer>()};}
        [TearDown] public void TearDown(){Selection.objects=Array.Empty<Object>();Undo.ClearAll();if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);foreach(var asset in assets)if(asset&&!AssetDatabase.Contains(asset))Object.DestroyImmediate(asset);}
        GameObject Go(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
        AtmosphereProfile Profile(){var p=ScriptableObject.CreateInstance<AtmosphereProfile>();assets.Add(p);return p;}
        AtmosphereZone Zone(string id,float exposure,int priority=0){var z=Go(id).AddComponent<AtmosphereZone>();z.id=id;z.profile=Profile();var look=z.profile.look;look.exposure=exposure;z.profile.look=look;z.size=Vector3.one*10;z.priority=priority;return z;}
        LightingProbeBinding Probe(){var go=Go("Probe");var p=go.AddComponent<LightingProbeBinding>();p.probe=go.AddComponent<ReflectionProbe>();return p;}
        [Test] public void BatchCircuitEditPreservesCriticalAndUnclassifiedFixtures()
        {
            var street=Go("Street").AddComponent<LightingFixture>();street.source=street.gameObject.AddComponent<Light>();street.role=FixtureRole.Street;
            var signal=Go("Signal").AddComponent<LightingFixture>();signal.source=signal.gameObject.AddComponent<Light>();signal.role=FixtureRole.Signal;signal.source.intensity=2;
            var unclassified=Go("Unknown").AddComponent<LightingFixture>();unclassified.source=unclassified.gameObject.AddComponent<Light>();unclassified.source.intensity=4;
            owner.fixtures=new[]{street,signal,unclassified};Assert.AreEqual(1,LightingCommands.SetCircuit(owner,"default",10,20,LightShadows.Soft));Assert.AreEqual(10,street.source.intensity);Assert.AreEqual(2,signal.source.intensity);Assert.AreEqual(4,unclassified.source.intensity);
        }
        [Test] public void MissingRequiredScenarioCannotMasqueradeAsDynamic(){baseline.bakedScenarioId="missing-night-gi";baseline.allowRealtimeFallback=false;Assert.IsFalse(baseline.IsValid);}
        [Test] public void NonfiniteKeyRotationRejected(){baseline.look.keyEuler=new Vector3(float.PositiveInfinity,0,0);Assert.IsFalse(baseline.IsValid);}
        [Test] public void FingerprintDoesNotDependOnEditorNumberCulture()
        {
            var original=System.Globalization.CultureInfo.CurrentCulture;
            try{System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");string before=LightingAudit.Fingerprint(owner);System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("fr-FR");Assert.AreEqual(before,LightingAudit.Fingerprint(owner));}finally{System.Globalization.CultureInfo.CurrentCulture=original;}
        }
        [Test] public void AppliedDraftKeepsAssetIdentityNameAndPersistentFlags()
        {
            string folder="Assets/LightingApplyTest_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",folder.Substring(7));
            try{var source=LightingCommands.CreateProfile(folder+"/Look.asset");string id=source.id;var draft=Object.Instantiate(source);assets.Add(draft);draft.hideFlags=HideFlags.HideAndDontSave;draft.look.exposure=2;LightingCommands.ApplyDraft(source,draft,source.Revision);Assert.AreEqual(id,source.id);Assert.AreEqual("Look",source.name);Assert.AreEqual(HideFlags.None,source.hideFlags);Assert.AreEqual(2,AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(folder+"/Look.asset").look.exposure);}finally{AssetDatabase.DeleteAsset(folder);}
        }
        [Test] public void DefaultsAreValid(){Assert.IsTrue(baseline.IsValid);}
        [TestCase(0)][TestCase(1)][TestCase(999)]public void UnknownSchemaRejected(int version){baseline.schemaVersion=version;Assert.IsFalse(baseline.IsValid);}
        [Test] public void MissingIdentityRejected(){baseline.id="";Assert.IsFalse(baseline.IsValid);}
        [Test] public void NaNExposureRejected(){baseline.look.exposure=float.NaN;Assert.IsFalse(baseline.IsValid);}
        [Test] public void InvalidFogRangeRejected(){baseline.look.fogStart=500;baseline.look.fogEnd=10;Assert.IsFalse(baseline.IsValid);}
        [Test] public void ProfileRevisionChangesOnLookChange(){string before=baseline.Revision;baseline.look.exposure=1;Assert.AreNotEqual(before,baseline.Revision);}
        [Test] public void NestedHigherPriorityWins(){var outer=Zone("outer",1);var tunnel=Zone("tunnel",3,10);var look=AtmosphereResolver.Resolve(baseline,new[]{tunnel,outer},Vector3.zero);Assert.AreEqual(3,look.exposure);}
        [Test] public void EqualPriorityOrderIsIndependentOfRegistrationOrder(){var a=Zone("a",1);var b=Zone("b",2);Assert.AreEqual(AtmosphereResolver.Resolve(baseline,new[]{a,b},Vector3.zero).exposure,AtmosphereResolver.Resolve(baseline,new[]{b,a},Vector3.zero).exposure);Assert.AreEqual(2,AtmosphereResolver.Resolve(baseline,new[]{a,b},Vector3.zero).exposure);}
        [Test] public void DuplicateZoneIdentityFailsClosed(){var a=Zone("same",1);var b=Zone("same",2);Assert.Throws<InvalidOperationException>(()=>AtmosphereResolver.Resolve(baseline,new[]{a,b},Vector3.zero));}
        [Test] public void InactiveZoneDoesNotContribute(){var z=Zone("z",3);z.enabled=false;Assert.AreEqual(0,AtmosphereResolver.Resolve(baseline,new[]{z},Vector3.zero).exposure);}
        [Test] public void MissingZoneProfileFailsClosed(){var z=Zone("z",3);z.profile=null;Assert.Throws<InvalidOperationException>(()=>AtmosphereResolver.Resolve(baseline,new[]{z},Vector3.zero));}
        [Test] public void PerCategoryDistanceWeightsDiffer(){var z=Zone("z",3);z.blendMeters=new Vector4(10,20,40,0);var w=z.Weights(new Vector3(10,0,0));Assert.AreEqual(.5f,w.x,.0001);Assert.AreEqual(.75f,w.y,.0001);Assert.AreEqual(.875f,w.z,.0001);Assert.AreEqual(0,w.w);}
        [Test] public void ScaledZoneUsesWorldMetres(){var z=Zone("z",3);z.transform.localScale=Vector3.one*2;z.blendMeters=Vector4.one*10;Assert.AreEqual(.5f,z.Weights(new Vector3(15,0,0)).x,.0001);}
        [Test] public void UnownedCategoryIsUnchanged(){var z=Zone("z",3);z.categories=LookCategory.Fog;Assert.AreEqual(0,AtmosphereResolver.Resolve(baseline,new[]{z},Vector3.zero).exposure);}
        [Test] public void InstanceOverrideDoesNotMutateSharedProfile(){var z=Zone("z",1);z.overrideExposure=true;z.exposureOverride=2;Assert.AreEqual(2,z.Look.exposure);Assert.AreEqual(1,z.profile.look.exposure);}
        [Test] public void ExponentialSmoothingIsFramePartitionIndependent(){var target=baseline.look;target.exposure=4;var whole=AtmosphereResolver.Step(baseline.look,target,baseline,1);var half=AtmosphereResolver.Step(baseline.look,target,baseline,.5f);half=AtmosphereResolver.Step(half,target,baseline,.5f);Assert.AreEqual(whole.exposure,half.exposure,.0001);}
        [Test] public void NegativeTimeRejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>AtmosphereResolver.Step(baseline.look,baseline.look,baseline,-1));}
        [Test] public void CategoryTransitionPoliciesDiffer(){baseline.exposureSeconds=.2f;baseline.lightingSeconds=3;var target=baseline.look;target.exposure=4;target.keyIntensity=120000;var result=AtmosphereResolver.Step(baseline.look,target,baseline,1);Assert.Greater(result.exposure/4,(result.keyIntensity-baseline.look.keyIntensity)/(target.keyIntensity-baseline.look.keyIntensity));}
        [Test] public void EnvironmentSnapshotRestoresEvenAfterException(){var sky=RenderSettings.ambientSkyColor;bool fog=RenderSettings.fog;try{using(new LightingEnvironmentSnapshot()){RenderSettings.ambientSkyColor=Color.magenta;RenderSettings.fog=!fog;throw new Exception();}}catch(Exception){}Assert.AreEqual(sky,RenderSettings.ambientSkyColor);Assert.AreEqual(fog,RenderSettings.fog);}
        [Test] public void EnvironmentSnapshotRestoresKeyTransform(){var light=Go("Key").AddComponent<Light>();light.intensity=2;var rotation=light.transform.rotation;using(new LightingEnvironmentSnapshot(light))baseline.look.ApplyEnvironment(light);Assert.AreEqual(2,light.intensity);Assert.AreEqual(rotation,light.transform.rotation);}
        [TestCase(FixtureRole.Signal)][TestCase(FixtureRole.BrakeLight)][TestCase(FixtureRole.PoliceLight)][TestCase(FixtureRole.ObstacleCue)]public void CriticalFixtureRolesAreProtected(FixtureRole role){var f=Go("Fixture").AddComponent<LightingFixture>();f.role=role;Assert.IsTrue(f.Critical);}
        [Test] public void MovingGeometryInvalidatesBake(){var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(cube,scene);owner.bakeGeometry=new[]{cube.GetComponent<Renderer>()};string before=LightingAudit.Fingerprint(owner);cube.transform.position=Vector3.one;Assert.AreNotEqual(before,LightingAudit.Fingerprint(owner));}
        [Test] public void MovingLightInvalidatesBake(){var f=Go("Light").AddComponent<LightingFixture>();f.source=f.gameObject.AddComponent<Light>();owner.fixtures=new[]{f};string before=LightingAudit.Fingerprint(owner);f.transform.position=Vector3.right;Assert.AreNotEqual(before,LightingAudit.Fingerprint(owner));}
        [Test] public void ProfileEditInvalidatesBake(){string before=LightingAudit.Fingerprint(owner);baseline.look.exposure=2;Assert.AreNotEqual(before,LightingAudit.Fingerprint(owner));}
        [Test] public void IncompleteBakeCannotReplacePrevious(){var probe=Probe();owner.probes=new[]{probe};var old=ScriptableObject.CreateInstance<LightingBakeSet>();assets.Add(old);owner.approvedBake=old;var next=ScriptableObject.CreateInstance<LightingBakeSet>();assets.Add(next);next.sourceFingerprint=LightingAudit.Fingerprint(owner);Assert.Throws<InvalidOperationException>(()=>LightingCommands.ApplyBake(owner,next));Assert.AreSame(old,owner.approvedBake);}
        [Test] public void StaleBakeCannotReplacePrevious(){var next=ScriptableObject.CreateInstance<LightingBakeSet>();assets.Add(next);next.sourceFingerprint="old";Assert.Throws<InvalidOperationException>(()=>LightingCommands.ApplyBake(owner,next));Assert.IsNull(owner.approvedBake);}
        [Test] public void ApprovedProbeAssignmentCanBeUndone(){var probe=Probe();owner.probes=new[]{probe};var original=probe.probe.mode;var next=ScriptableObject.CreateInstance<LightingBakeSet>();assets.Add(next);var map=new Cubemap(16,TextureFormat.RGBA32,false);assets.Add(map);next.sourceFingerprint=LightingAudit.Fingerprint(owner);next.reflections=new[]{new BakedReflection{probeId=probe.id,texture=map}};Undo.IncrementCurrentGroup();LightingCommands.ApplyBake(owner,next);Undo.FlushUndoRecordObjects();Assert.AreSame(map,probe.probe.customBakedTexture);Undo.PerformUndo();Assert.IsNull(owner.approvedBake);Assert.AreEqual(original,probe.probe.mode);}
        [Test] public void CancellationDoesNotPublish(){owner.probes=new[]{Probe()};using(var job=new LightingBakeJob(owner,64)){job.Dispose();Assert.IsTrue(job.Finished);Assert.IsNull(job.Result);Assert.IsNull(owner.approvedBake);}}
        [Test] public void SourceChangedWhileStagingRejectsCapture(){owner.probes=new[]{Probe()};using(var job=new LightingBakeJob(owner,64)){baseline.look.exposure=1;Assert.Throws<InvalidOperationException>(()=>job.Tick());Assert.IsNull(owner.approvedBake);}}
        [Test] public void PreviewCloneContainsNoController(){int before=Resources.FindObjectsOfTypeAll<AtmosphereController>().Length;using(var preview=new LightingPreview(owner)){Assert.AreEqual(before,Resources.FindObjectsOfTypeAll<AtmosphereController>().Length);}Assert.AreEqual(before,Resources.FindObjectsOfTypeAll<AtmosphereController>().Length);}
        [Test] public void RepeatedPreviewDisposeReleasesScenes(){int before=EditorSceneManager.previewSceneCount;for(int i=0;i<5;i++){var preview=new LightingPreview(owner);preview.Dispose();preview.Dispose();}Assert.AreEqual(before,EditorSceneManager.previewSceneCount);}
        [Test] public void DifferenceHasExpectedValues(){var a=new Texture2D(16,16);var b=new Texture2D(16,16);assets.Add(a);assets.Add(b);a.SetPixels(Enumerable.Repeat(Color.black,256).ToArray());b.SetPixels(Enumerable.Repeat(Color.white,256).ToArray());a.Apply();b.Apply();var diff=LightingComparison.Difference(a,b,out float error);assets.Add(diff);Assert.AreEqual(1,error,.0001);}
        [Test] public void DifferenceRejectsIncompatibleDimensions(){var a=new Texture2D(16,16);var b=new Texture2D(32,16);assets.Add(a);assets.Add(b);Assert.Throws<InvalidOperationException>(()=>LightingComparison.Difference(a,b,out _));}
        [Test] public void DraftConflictRejectsOverwrite(){var draft=Object.Instantiate(baseline);assets.Add(draft);string revision=baseline.Revision;baseline.look.exposure=2;Assert.Throws<InvalidOperationException>(()=>LightingCommands.ApplyDraft(baseline,draft,revision));Assert.AreEqual(2,baseline.look.exposure);}
        [Test] public void NewZoneIdentityDoesNotChangeOtherObjects(){var a=Zone("a",1);var b=Zone("b",2);LightingCommands.NewId(a);Assert.AreNotEqual("a",a.id);Assert.AreEqual("b",b.id);}
        [Test] public void StandardPostParametersAreActuallyWritten(){var volume=ScriptableObject.CreateInstance<VolumeProfile>();assets.Add(volume);var look=baseline.look;look.exposure=2;look.bloom=.5f;look.ApplyPost(volume);Assert.IsTrue(volume.TryGet<Exposure>(out var exposure));Assert.AreEqual(2,exposure.compensation.value);Assert.IsTrue(exposure.compensation.overrideState);Assert.AreEqual(look.exposureEV100,exposure.fixedExposure.value);Assert.IsTrue(volume.TryGet<ColorAdjustments>(out var color));Assert.AreEqual(0,color.postExposure.value,"Apply exposure once, through HDRP's exposure control.");Assert.IsTrue(volume.TryGet<Bloom>(out var bloom));Assert.AreEqual(.5f,bloom.intensity.value);foreach(var component in volume.components)assets.Add(component);}
        [Test] public void VolumetricCloudProfileOwnsItsRuntimeParameters()
        {
            var volume=ScriptableObject.CreateInstance<VolumeProfile>();assets.Add(volume);
            WeatherVolumetricClouds.Initialize(volume);
            Assert.IsTrue(volume.TryGet<VisualEnvironment>(out var environment));
            Assert.AreEqual(RenderingSpace.Camera,environment.renderingSpace.value);
            Assert.IsTrue(environment.renderingSpace.overrideState);
            Assert.IsTrue(volume.TryGet<VolumetricClouds>(out var clouds));
            Assert.AreEqual(VolumetricClouds.CloudControl.Simple,clouds.cloudControl.value);
            Assert.AreEqual(VolumetricClouds.CloudPresets.Custom,clouds.cloudPreset);
            Assert.IsFalse(clouds.enable.value);
            Assert.IsTrue(clouds.enable.overrideState);
            Assert.IsTrue(clouds.cloudControl.overrideState);
            Assert.IsTrue(clouds.densityCurve.overrideState);
            Assert.IsTrue(clouds.fadeInMode.overrideState);
            foreach(var component in volume.components)assets.Add(component);
        }
        [Test] public void VolumetricCloudFrameGateDisablesFullResolutionSkyClouds()
        {
            var data=owner.worldCamera.GetLightingCameraData();
            LightingCamera.SetVolumetricClouds(data,true);
            Assert.IsTrue(data.customRenderingSettings);
            Assert.IsTrue(data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.VolumetricClouds]);
            Assert.IsTrue(data.renderingPathCustomFrameSettings.IsEnabled(FrameSettingsField.VolumetricClouds));
            Assert.IsTrue(data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.FullResolutionCloudsForSky]);
            Assert.IsFalse(data.renderingPathCustomFrameSettings.IsEnabled(FrameSettingsField.FullResolutionCloudsForSky));
        }
        [Test] public void WindowCloseReleasesTransientDraft(){var window=ScriptableObject.CreateInstance<LightingStudioWindow>();var so=new SerializedObject(window);so.FindProperty("source").objectReferenceValue=baseline;so.ApplyModifiedPropertiesWithoutUndo();typeof(LightingStudioWindow).GetMethod("LoadDraft",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(window,null);var field=typeof(LightingStudioWindow).GetField("draft",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);var draft=(AtmosphereProfile)field.GetValue(window);Object.DestroyImmediate(window);Assert.IsTrue(draft==null);Assert.IsTrue(baseline!=null);}
    }
}
