using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class VehicleMirrorBinding
    {
        public string id = "";
        public Transform view;
        public Renderer[] surfaces = Array.Empty<Renderer>();
        public Vector3 localEuler;
        public bool horizontalFlip;
        [Range(1, 179)] public float fieldOfView = 70f;
        [Min(.01f)] public float nearClip = .03f;
        [Min(1)] public float farClip = 120f;
        [Min(40)] public int resolution = 512;
        [Min(.05f)] public float refreshSeconds = .08f;
        public int cullingLayer = -1;
    }

    /// <summary>HDRP-compatible render-request mirror capture, one target per mirror.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleMirrorRenderer : MonoBehaviour
    {
        [SerializeField] private VehicleMirrorBinding[] mirrors = Array.Empty<VehicleMirrorBinding>();
        [SerializeField] private Camera observerCamera;
        [SerializeField] private VehicleCameraRig observerRig;
        [SerializeField] private LayerMask cullingMask = ~0;
        [SerializeField] private float maxDistance = 120f;
        [SerializeField] private string textureProperty = "_BaseColorMap";
        private Camera[] cameras = Array.Empty<Camera>();
        private RenderTexture[] textures = Array.Empty<RenderTexture>();
        private MaterialPropertyBlock[][] blocks = Array.Empty<MaterialPropertyBlock[]>();
        private bool[][] priorEnabled = Array.Empty<bool[]>();
        private float[] nextRefresh = Array.Empty<float>();
        private RenderPipeline.StandardRequest[] requests = Array.Empty<RenderPipeline.StandardRequest>();
        private bool[] supportFailureReported = Array.Empty<bool>();
        private bool built;
        private int texturePropertyId;
        private int textureScaleId;
        private VehiclePresentationBindings presentation;
        private VehicleConfiguration configuration;

        public VehicleMirrorBinding[] Mirrors => mirrors;
        public void Configure(Camera camera, VehicleMirrorBinding[] configured) { observerCamera = camera; mirrors = configured ?? Array.Empty<VehicleMirrorBinding>(); if (Application.isPlaying) Rebuild(); }

        private void Awake() { if (!built) Rebuild(); }
        private void Rebuild()
        {
            Release();
            presentation = GetComponentInParent<VehiclePresentationBindings>();
            configuration = GetComponentInParent<VehicleConfiguration>();
            if (!observerCamera) observerCamera = Camera.main;
            if (!observerRig && observerCamera) observerRig = observerCamera.GetComponent<VehicleCameraRig>();
            texturePropertyId = Shader.PropertyToID(string.IsNullOrEmpty(textureProperty) ? "_BaseColorMap" : textureProperty);
            textureScaleId = Shader.PropertyToID((string.IsNullOrEmpty(textureProperty) ? "_BaseColorMap" : textureProperty) + "_ST");
            int count = mirrors == null ? 0 : mirrors.Length;
            cameras = new Camera[count]; textures = new RenderTexture[count]; blocks = new MaterialPropertyBlock[count][]; priorEnabled = new bool[count][]; nextRefresh = new float[count];
            requests = new RenderPipeline.StandardRequest[count]; supportFailureReported = new bool[count];
            if (!Eligible) return;
            built = true;
            HdrpQualityPreset quality = HdrpQualityRuntime.CurrentPreset;
            for (int i = 0; i < count; i++)
            {
                VehicleMirrorBinding binding = mirrors[i]; if (!Valid(binding)) continue;
                GameObject cameraObject = new GameObject("Mirror camera " + binding.id); cameraObject.transform.SetParent(transform, false);
                cameras[i] = cameraObject.AddComponent<Camera>(); cameras[i].enabled = false; cameras[i].fieldOfView = binding.fieldOfView; cameras[i].nearClipPlane = binding.nearClip; cameras[i].farClipPlane = Mathf.Max(binding.nearClip + 1, Mathf.Min(maxDistance, Mathf.Min(binding.farClip, quality.mirrorDistance)));
                cameras[i].clearFlags = CameraClearFlags.SolidColor; cameras[i].backgroundColor = Color.black;
                var hdCamera = cameras[i].GetComponent<HDAdditionalCameraData>() ?? cameras[i].gameObject.AddComponent<HDAdditionalCameraData>();
                var observerData = observerCamera ? observerCamera.GetComponent<HDAdditionalCameraData>() : null;
                hdCamera.clearColorMode = observerData ? observerData.clearColorMode : HDAdditionalCameraData.ClearColorMode.Sky;
                hdCamera.backgroundColorHDR = observerData ? observerData.backgroundColorHDR : Color.black;
                if (observerData)
                {
                    hdCamera.volumeLayerMask = observerData.volumeLayerMask.value & ~(1 << VehicleCameraPostProcessing.VolumeLayer);
                    hdCamera.volumeAnchorOverride = observerData.volumeAnchorOverride ? observerData.volumeAnchorOverride : observerCamera.transform;
                }
                cameras[i].cullingMask = binding.cullingLayer >= 0 ? 1 << binding.cullingLayer : cullingMask;
                int size = Mathf.Clamp(Mathf.Min(binding.resolution, quality.mirrorResolution), 64, 2048);
                textures[i] = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { name = "Mirror " + binding.id, useMipMap = false, autoGenerateMips = false }; textures[i].Create(); cameras[i].targetTexture = textures[i];
                requests[i] = new RenderPipeline.StandardRequest { destination = textures[i] };
                Renderer[] surfaces = binding.surfaces ?? Array.Empty<Renderer>(); blocks[i] = new MaterialPropertyBlock[surfaces.Length]; priorEnabled[i] = new bool[surfaces.Length]; for (int j = 0; j < surfaces.Length; j++) blocks[i][j] = new MaterialPropertyBlock();
            }
        }

        private void LateUpdate()
        {
            if (!Eligible) { if (built && cameras.Length > 0 && Array.Exists(cameras, camera => camera)) Release(); return; }
            if (!built) Rebuild();
            if (!observerCamera) observerCamera = Camera.main;
            if (mirrors == null || observerCamera == null || observerRig != null && !observerRig.IsHoodView || !HdrpQualityRuntime.CurrentPreset.planarReflections) return;
            for (int i = 0; i < mirrors.Length; i++)
            {
                VehicleMirrorBinding binding = mirrors[i]; Camera camera = cameras[i];
                if (binding == null || camera == null || !binding.view || !VisibleToObserver(binding.view)) continue;
                if (Time.unscaledTime < nextRefresh[i]) continue;
                nextRefresh[i] = Time.unscaledTime + Mathf.Max(.05f, binding.refreshSeconds);
                CaptureNow(i);
            }
        }

        /// <summary>Captures one mirror immediately for deterministic rendering tests and controlled presentation updates.</summary>
        public bool CaptureNow(int index)
        {
            if (!isActiveAndEnabled || !Eligible || !built || observerCamera == null || index < 0 || index >= cameras.Length || observerRig != null && !observerRig.IsHoodView || !HdrpQualityRuntime.CurrentPreset.planarReflections) return false;
            VehicleMirrorBinding binding = mirrors[index]; Camera camera = cameras[index];
            if (binding == null || camera == null || !binding.view || !VisibleToObserver(binding.view)) return false;
            nextRefresh[index] = Time.unscaledTime + Mathf.Max(.05f, binding.refreshSeconds);
                camera.transform.SetPositionAndRotation(binding.view.position, binding.view.rotation * Quaternion.Euler(binding.localEuler));
                bool[] enabled = priorEnabled[index];
                RenderTexture previousTarget = RenderTexture.active;
                try
                {
                    SuppressAllMirrorSurfaces(true);
                    var request = requests[index];
                    if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
                    else { if (!supportFailureReported[index]) { supportFailureReported[index] = true; Debug.LogWarning("HDRP does not support StandardRequest mirror rendering for " + binding.id, this); } return false; }
                }
                finally
                {
                    RenderTexture.active = previousTarget;
                    SuppressAllMirrorSurfaces(false);
                    for (int j = 0; j < enabled.Length; j++) if (binding.surfaces[j]) binding.surfaces[j].enabled = enabled[j];
                }
                Apply(index, binding.surfaces);
            return true;
        }
        public RenderTexture GetCaptureTexture(int index) => index >= 0 && index < textures.Length ? textures[index] : null;
        private bool Eligible => (!configuration || !configuration.Definition || configuration.Definition.Supports(VehicleCapabilities.Mirrors))
            && (!presentation || presentation.role == VehiclePresentationRole.Player || presentation.role == VehiclePresentationRole.Garage);

        private bool VisibleToObserver(Transform view)
        {
            Vector3 delta = view.position - observerCamera.transform.position;
            return delta.sqrMagnitude <= maxDistance * maxDistance && Vector3.Dot(observerCamera.transform.forward, delta) > 0f;
        }
        private bool Valid(VehicleMirrorBinding binding)
        { return binding != null && binding.view && binding.fieldOfView > 0 && binding.fieldOfView < 180 && binding.nearClip > 0 && binding.farClip > binding.nearClip && binding.resolution >= 40 && binding.refreshSeconds > 0; }
        private void SuppressAllMirrorSurfaces(bool suppress)
        {
            for (int i = 0; i < mirrors.Length; i++)
            {
                var surfaces = mirrors[i]?.surfaces; var enabled = i < priorEnabled.Length ? priorEnabled[i] : null;
                for (int j = 0; surfaces != null && j < surfaces.Length; j++) if (surfaces[j])
                { if (suppress) { if (enabled != null && j < enabled.Length) enabled[j] = surfaces[j].enabled; surfaces[j].enabled = false; } else if (enabled != null && j < enabled.Length) surfaces[j].enabled = enabled[j]; }
            }
        }
        private void Apply(int index, Renderer[] surfaces)
        {
            for (int i = 0; surfaces != null && i < surfaces.Length; i++) if (surfaces[i]) { MaterialPropertyBlock block = blocks[index][i]; surfaces[i].GetPropertyBlock(block); block.SetTexture(texturePropertyId, textures[index]); block.SetVector(textureScaleId, mirrors[index].horizontalFlip ? new Vector4(-1, 1, 1, 0) : new Vector4(1, 1, 0, 0)); surfaces[i].SetPropertyBlock(block); }
        }
        private void Release()
        {
            for (int i = 0; i < cameras.Length; i++) if (cameras[i]) DestroyOwned(cameras[i].gameObject);
            for (int i = 0; i < textures.Length; i++) if (textures[i]) { textures[i].Release(); DestroyOwned(textures[i]); }
            cameras = Array.Empty<Camera>(); textures = Array.Empty<RenderTexture>(); blocks = Array.Empty<MaterialPropertyBlock[]>(); priorEnabled = Array.Empty<bool[]>(); nextRefresh = Array.Empty<float>(); requests = Array.Empty<RenderPipeline.StandardRequest>(); supportFailureReported = Array.Empty<bool>(); built = false;
        }
        private static void DestroyOwned(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDisable() => Release();
        private void OnEnable() { if (Application.isPlaying && !built) Rebuild(); }
        private void OnDestroy() => Release();
    }
}
