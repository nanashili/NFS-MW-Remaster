using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Bounded menu-only scene. Only mesh renderers are published from the real car;
    /// driving, AI, collision, rewards and save components never run in this preview.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MostWantedShowroom : MonoBehaviour
    {
        public const int PreviewLayer = 31;
        private const int StillWidth = 1536, StillHeight = 992;
        private const int MovingWidth = 768, MovingHeight = 496;
        private const int TemporalSettleFrames = 8;
        public static Action<Camera, Transform> ConfigureRendering;
        private readonly List<Renderer> paintRenderers = new List<Renderer>();
        private GameObject stage;
        private Camera view;
        private RenderTexture target;
        private Transform car;
        private Vector3 focus;
        private Vector3 desiredPosition;
        private Quaternion desiredRotation;
        private float desiredFieldOfView;
        private MostWantedShowroomDefinition.CameraShot currentShot, fromShot, toShot;
        private float shotStartedAt, shotSeconds;
        private bool entering;
        private MostWantedFrontendPage page;
        private bool visible;
        private bool initialized;
        private int framesToSettle, renderedQuality;
        private float lastMotionAt = float.NegativeInfinity;
        private float orbitYaw = 145, orbitPitch = 13, orbitDistance = 6.9f;
        private float previousTime;
        private MostWantedShowroomDefinition publication;
        public RenderTexture Target => target;
        public bool HasPublishedVehicle => publication != null && publication.parts.Length > 0;
        public Camera PreviewCamera => view;
        public GameObject Stage => stage;

        public void Configure(MostWantedShowroomDefinition definition)
        {
            if (initialized && publication != definition)
                throw new InvalidOperationException("Configure showroom content before showing it.");
            publication = definition;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ConfigureRendering = null;

        public void Show(bool show, MostWantedFrontendPage nextPage)
        {
            if (!show && !initialized) return;
            if (!initialized) Initialize();
            bool becameVisible = show && !visible;
            visible = show;
            if (!show && view != null) view.enabled = false;
            if (stage != null) stage.SetActive(show);
            if (becameVisible) InvalidateImage();
            if (page == nextPage) return;
            page = nextPage;
            SelectShot(nextPage);
        }

        private void Initialize()
        {
            initialized = true;
            if (publication == null) publication = Resources.Load<MostWantedShowroomDefinition>("MostWantedUI/Showroom");
            stage = new GameObject("Most Wanted renderer-only showroom") { layer = PreviewLayer };
            stage.transform.SetParent(transform, false);
            // Outside authored gameplay bounds; the preview camera additionally culls to its own layer.
            stage.transform.localPosition = new Vector3(30000, 0, 30000);
            var cameraObject = Child("Showroom camera");
            view = cameraObject.AddComponent<Camera>();
            view.cullingMask = 1 << PreviewLayer;
            view.nearClipPlane = .05f;
            view.farClipPlane = 300;
            view.fieldOfView = 51;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.045f, .058f, .047f);
            view.allowHDR = true;
            view.allowMSAA = false;
            view.depth = -20;
            target = new RenderTexture(StillWidth, StillHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "Most Wanted Frontend",
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();
            view.targetTexture = target;
            BuildVehicle();
            BuildEnvironment();
            ConfigureRendering?.Invoke(view, stage.transform);
            toShot = publication != null ? publication.ShotFor(MostWantedFrontendPage.MainMenu) : DefaultShot();
            entering = publication != null && publication.entranceShot.fieldOfView > 0;
            currentShot = entering ? publication.entranceShot : toShot;
            fromShot = currentShot;
            shotStartedAt = Time.unscaledTime;
            shotSeconds = entering ? publication.entranceSeconds : 0;
            ApplyShot(currentShot);
            previousTime = Time.unscaledTime;
            renderedQuality = QualitySettings.GetQualityLevel();
            InvalidateImage();
        }

        private GameObject Child(string objectName)
        {
            var child = new GameObject(objectName) { layer = PreviewLayer };
            child.transform.SetParent(stage.transform, false);
            return child;
        }

        private void BuildVehicle()
        {
            car = Child("Published vehicle meshes").transform;
            if (!HasPublishedVehicle)
            {
                focus = stage.transform.position + Vector3.up;
                return;
            }
            foreach (var part in publication.parts)
            {
                if (part == null || part.mesh == null) continue;
                var node = new GameObject(part.name) { layer = PreviewLayer };
                node.transform.SetParent(car, false);
                node.transform.localPosition = part.position;
                node.transform.localRotation = part.rotation;
                node.transform.localScale = part.scale;
                node.AddComponent<MeshFilter>().sharedMesh = part.mesh;
                var renderer = node.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = part.materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (part.paintable) paintRenderers.Add(renderer);
            }
            car.localPosition = new Vector3(-publication.bounds.center.x, -publication.bounds.min.y + .02f, -publication.bounds.center.z);
            car.localPosition = Quaternion.Euler(0, publication.vehicleYaw, 0) * car.localPosition + publication.vehiclePosition;
            car.localRotation = Quaternion.Euler(0, publication.vehicleYaw, 0);
            focus = stage.transform.TransformPoint(publication.vehiclePosition + Vector3.up * Mathf.Max(.5f, publication.bounds.size.y * .46f));
        }

        private void BuildEnvironment()
        {
            if (publication != null)
            {
                var environment = Child("Published warehouse meshes").transform;
                foreach (var part in publication.environmentParts)
                {
                    if (part == null || part.mesh == null) continue;
                    var node = new GameObject(part.name) { layer = PreviewLayer };
                    node.transform.SetParent(environment, false);
                    node.transform.localPosition = part.position;
                    node.transform.localRotation = part.rotation;
                    node.transform.localScale = part.scale;
                    node.AddComponent<MeshFilter>().sharedMesh = part.mesh;
                    var renderer = node.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = part.materials;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
            }
            var key = Child("Showroom key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.transform.localRotation = Quaternion.Euler(44, -34, 0);
            key.color = new Color(1f, .89f, .66f);
            key.intensity = 2.4f;
            key.shadows = LightShadows.Soft;
            key.cullingMask = 1 << PreviewLayer;
            var fill = Child("Showroom fill").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.transform.localPosition = new Vector3(4, 4, 4);
            fill.color = new Color(.61f, .76f, .68f);
            fill.intensity = 3;
            fill.range = 20;
            fill.cullingMask = 1 << PreviewLayer;
        }

        private void SelectShot(MostWantedFrontendPage selected)
        {
            if (view == null) return;
            fromShot = currentShot;
            toShot = publication != null ? publication.ShotFor(selected) : DefaultShot();
            shotStartedAt = Time.unscaledTime + .20f;
            shotSeconds = publication != null ? publication.transitionSeconds : .65f;
            entering = false;
            InvalidateImage();
        }

        private static MostWantedShowroomDefinition.CameraShot DefaultShot() =>
            new MostWantedShowroomDefinition.CameraShot { position = new Vector3(5.1f,1.9f,6.1f), target = new Vector3(0,.8f,0), roll = 6, fieldOfView = 51 };

        private void ApplyShot(MostWantedShowroomDefinition.CameraShot shot)
        {
            var rotation = Quaternion.Euler(0, publication != null ? publication.vehicleYaw : 0, 0);
            Vector3 origin = publication != null ? publication.vehiclePosition : Vector3.zero;
            desiredPosition = stage.transform.TransformPoint(origin + rotation * shot.position);
            Vector3 look = stage.transform.TransformPoint(origin + rotation * shot.target);
            desiredRotation = Quaternion.LookRotation(look - desiredPosition, Vector3.up) * Quaternion.Euler(0,0,shot.roll);
            desiredFieldOfView = Mathf.Clamp(shot.fieldOfView,15,100);
            view.transform.SetPositionAndRotation(desiredPosition,desiredRotation);
            view.fieldOfView = desiredFieldOfView;
        }

        // Polar interpolation keeps camera travel outside the car when moving from front to rear.
        public static MostWantedShowroomDefinition.CameraShot InterpolateShot(
            MostWantedShowroomDefinition.CameraShot from, MostWantedShowroomDefinition.CameraShot to, float progress)
        {
            float t = Mathf.SmoothStep(0,1,Mathf.Clamp01(progress));
            float yaw = Mathf.LerpAngle(Mathf.Atan2(from.position.x,from.position.z)*Mathf.Rad2Deg,
                Mathf.Atan2(to.position.x,to.position.z)*Mathf.Rad2Deg,t)*Mathf.Deg2Rad;
            float radius = Mathf.Lerp(new Vector2(from.position.x,from.position.z).magnitude,
                new Vector2(to.position.x,to.position.z).magnitude,t);
            return new MostWantedShowroomDefinition.CameraShot { page=to.page,
                position=new Vector3(Mathf.Sin(yaw)*radius,Mathf.Lerp(from.position.y,to.position.y,t),Mathf.Cos(yaw)*radius),
                target=Vector3.Lerp(from.target,to.target,t),roll=Mathf.LerpAngle(from.roll,to.roll,t),fieldOfView=Mathf.Lerp(from.fieldOfView,to.fieldOfView,t) };
        }

        private void LateUpdate()
        {
            if (!visible || view == null) return;
            Vector3 previousPosition = view.transform.position;
            Quaternion previousRotation = view.transform.rotation;
            float previousFieldOfView = view.fieldOfView;
            float dt = Mathf.Min(.1f, Mathf.Max(0, Time.unscaledTime - previousTime));
            previousTime = Time.unscaledTime;
            if (page == MostWantedFrontendPage.Showcase)
            {
                Vector2 delta = Vector2.zero;
                if (Mouse.current?.leftButton.isPressed == true) delta = Mouse.current.delta.ReadValue() * .14f;
                if (Gamepad.current != null) delta += Gamepad.current.rightStick.ReadValue() * (90 * dt);
                orbitYaw += delta.x;
                orbitPitch = Mathf.Clamp(orbitPitch - delta.y, 3, 55);
                if (Mouse.current != null) orbitDistance = Mathf.Clamp(orbitDistance - Mouse.current.scroll.ReadValue().y * .005f, 3.8f, 11);
                desiredPosition = focus + Quaternion.Euler(orbitPitch, orbitYaw, 0) * (Vector3.back * orbitDistance);
                desiredRotation = Quaternion.LookRotation(focus - desiredPosition, Vector3.up);
                float blend = 1 - Mathf.Exp(-6 * dt);
                view.transform.position = Vector3.Lerp(view.transform.position, desiredPosition, blend);
                view.transform.rotation = Quaternion.Slerp(view.transform.rotation, desiredRotation, blend);
            }
            else
            {
                float elapsed = Time.unscaledTime - shotStartedAt;
                // The warehouse establishing view holds briefly before the opening dolly reaches the car.
                float progress = shotSeconds <= 0 ? 1 : (elapsed - (entering ? .8f : 0)) / shotSeconds;
                currentShot = InterpolateShot(fromShot,toShot,progress);
                ApplyShot(currentShot);
            }
            bool moved = previousPosition != view.transform.position
                || Quaternion.Angle(previousRotation, view.transform.rotation) > .01f
                || !Mathf.Approximately(previousFieldOfView, view.fieldOfView);
            UpdateRendering(moved);
        }

        private void InvalidateImage()
        {
            framesToSettle = TemporalSettleFrames;
            if (view != null && visible) view.enabled = true;
        }

        private void UpdateRendering(bool moved)
        {
            if (moved) lastMotionAt = Time.unscaledTime;
            bool moving = Time.unscaledTime - lastMotionAt < .12f;
            int quality = QualitySettings.GetQualityLevel();
            if (renderedQuality != quality)
            {
                renderedQuality = quality;
                InvalidateImage();
            }
            // Keep camera motion within the preview GPU budget. UI text is a separate
            // full-resolution layer; a stationary camera resolves a sharp image once.
            int width = moving ? MovingWidth : StillWidth;
            int height = moving ? MovingHeight : StillHeight;
            if (target.width != width || target.height != height || !target.IsCreated())
            {
                target.Release();
                target.width = width;
                target.height = height;
                target.Create();
                InvalidateImage();
            }
            if (moving) InvalidateImage();
            view.enabled = framesToSettle > 0;
            if (framesToSettle > 0) framesToSettle--;
        }

        public void SetPaintPreview(Color color)
        {
            InvalidateImage();
            var block = new MaterialPropertyBlock();
            foreach (var renderer in paintRenderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
            }
        }

        public void ClearPaintPreview()
        {
            InvalidateImage();
            foreach (var renderer in paintRenderers) if (renderer != null) renderer.SetPropertyBlock(null);
        }

        private void OnDestroy()
        {
            if (view != null) view.targetTexture = null;
            if (target != null) { target.Release(); Destroy(target); }
            if (stage != null) Destroy(stage);
        }
    }
}
