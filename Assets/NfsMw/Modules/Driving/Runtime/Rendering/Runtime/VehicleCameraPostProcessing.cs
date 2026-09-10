using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving
{
    /// <summary>A private local volume; no shared weather or authored profile is modified.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(VehicleCameraRig), typeof(HDAdditionalCameraData))]
    public sealed class VehicleCameraPostProcessing : MonoBehaviour
    {
        public const int VolumeLayer = 26;
        private VehicleCameraRig rig;
        private HDAdditionalCameraData data;
        private Volume volume;
        private VolumeProfile instance;
        private MotionBlur blur;
        private ChromaticAberration peripheral;
        private bool hadLayer;
        private int quality = -1;
        private float qualityScale;
        public float AppliedMotionBlur => blur ? blur.intensity.value : 0;
        private void OnEnable()
        {
            rig = GetComponent<VehicleCameraRig>(); data = GetComponent<HDAdditionalCameraData>();
            hadLayer = (data.volumeLayerMask.value & (1 << VolumeLayer)) != 0;
            data.volumeLayerMask |= 1 << VolumeLayer;
            var go = new GameObject("Vehicle camera speed volume") { layer = VolumeLayer, hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            var bounds = go.AddComponent<SphereCollider>(); bounds.radius = .05f; bounds.isTrigger = true;
            instance = ScriptableObject.CreateInstance<VolumeProfile>(); instance.hideFlags = HideFlags.HideAndDontSave;
            blur = instance.Add<MotionBlur>(); blur.intensity.Override(0);
            blur.maximumVelocity.Override(150); blur.minimumVelocity.Override(2);
            blur.specialCameraClampMode.Override(CameraClampMode.SeparateTranslationAndRotation);
            blur.cameraTranslationVelocityClamp.Override(.05f); blur.cameraRotationVelocityClamp.Override(.02f);
            peripheral = instance.Add<ChromaticAberration>(); peripheral.intensity.Override(0);
            volume = go.AddComponent<Volume>(); volume.isGlobal = false; volume.blendDistance = 0; volume.priority = 1000; volume.sharedProfile = instance;
            rig.PoseResolved += Apply; quality = -1;
        }
        private void Apply(VehicleCameraDiagnostics debug)
        {
            int level = HdrpQualityRuntime.CurrentLevel;
            if (quality != level)
            {
                quality = level;
                qualityScale = level <= 1 ? 0 : level == 2 ? .25f : level == 3 ? .7f : 1;
                blur.quality.Override(Mathf.Clamp(level - 2, 0, 2));
                if (!HdrpQualityRuntime.CurrentPreset.motionBlurUserControlled) qualityScale = 0;
            }
            // Track a configured volume anchor without changing any camera's volume anchor contract.
            volume.transform.position = data.volumeAnchorOverride ? data.volumeAnchorOverride.position : transform.position;
            blur.intensity.value = rig.MotionBlurEnabled ? debug.motionBlur * qualityScale : 0;
            peripheral.intensity.value = debug.peripheral * qualityScale;
        }
        private void OnDisable()
        {
            if (rig) rig.PoseResolved -= Apply;
            if (data && !hadLayer) data.volumeLayerMask &= ~(1 << VolumeLayer);
            if (volume) DestroyOwned(volume.gameObject);
            if (instance) { foreach (var component in instance.components) if (component) DestroyOwned(component); DestroyOwned(instance); }
            volume = null; instance = null; blur = null; peripheral = null;
        }
        private static void DestroyOwned(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
