#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Builds a small synthetic traversal fixture without depending on production road or vehicle content.</summary>
    public static class AudioZoneDemoBuilder
    {
        private const string Folder = "Assets/NfsMw/Modules/Driving/Data/AudioZones";
        private const string ScenePath = "Assets/NfsMw/Scenes/Tests/AudioZoneTest.unity";
        private const string DiagnosticFolder = "Assets/NfsMw/Modules/Driving/Audio/Diagnostic";

        [MenuItem("NFS MW Remaster/Driving/Audio Zones/Build Audio Zone Test Scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder("Assets/NfsMw/Modules/Driving/Data"); EnsureFolder(Folder); EnsureFolder("Assets/NfsMw/Scenes");
            var open = GetOrCreateProfile("OpenStreet.asset", AudioZoneCategory.OpenStreet);
            var underpass = GetOrCreateProfile("Underpass.asset", AudioZoneCategory.Underpass);
            var industrial = GetOrCreateProfile("IndustrialInterior.asset", AudioZoneCategory.IndustrialInterior);
            var garage = GetOrCreateProfile("Garage.asset", AudioZoneCategory.Garage);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientLight = new Color(0.12f, 0.14f, 0.18f);
            CreateCube("Road bed", Vector3.zero, new Vector3(150, 0.2f, 180));
            CreateCube("Underpass roof", new Vector3(0, 9, 10), new Vector3(28, 0.4f, 48));
            CreateCube("Underpass wall L", new Vector3(-14, 4, 10), new Vector3(0.4f, 8, 48));
            CreateCube("Underpass wall R", new Vector3(14, 4, 10), new Vector3(0.4f, 8, 48));
            CreateCube("Industrial roof", new Vector3(43, 11, 4), new Vector3(38, 0.4f, 38));
            CreateCube("Garage roof", new Vector3(0, 8, -52), new Vector3(30, 0.4f, 28));
            CreateCube("Garage back wall", new Vector3(0, 4, -66), new Vector3(30, 8, 0.4f));

            var openZone = CreateZone("Open Arterial", "audio.demo.open", AudioZoneShape.Box, open, Vector3.zero, new Vector3(145, 8, 175));
            var underpassZone = CreateZone("Underpass", "audio.demo.underpass", AudioZoneShape.Box, underpass, new Vector3(0, 0, 10), new Vector3(26, 8, 46));
            var industrialZone = CreateZone("Industrial Yard", "audio.demo.industrial", AudioZoneShape.Box, industrial, new Vector3(43, 0, 4), new Vector3(36, 10, 36));
            var garageZone = CreateZone("Garage", "audio.demo.garage", AudioZoneShape.Box, garage, new Vector3(0, 0, -52), new Vector3(28, 7, 26));
            CreatePortal("Underpass Mouth", underpassZone, openZone, new Vector3(0, 0, -14));
            CreatePortal("Industrial Gate", industrialZone, openZone, new Vector3(24, 0, 4));
            CreatePortal("Garage Door", garageZone, openZone, new Vector3(0, 0, -39));

            var cameraObject = new GameObject("Traversal Camera + AudioListener");
            cameraObject.transform.position = new Vector3(0, 3.2f, -105);
            cameraObject.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            var camera = cameraObject.AddComponent<Camera>(); NfsMwRemaster.Driving.Editor.Rendering.HdrpSceneDefaults.Camera(camera); camera.fieldOfView = 62; camera.nearClipPlane = 0.1f; camera.farClipPlane = 500;
            cameraObject.AddComponent<AudioListener>();
            var systems = new GameObject("Audio Zone Runtime Systems");
            var audio = systems.AddComponent<SensoryAudioWorld>(); audio.Configure(cameraObject.transform, null, null);
            var world = systems.AddComponent<AudioZoneWorld>(); world.Configure(audio, AudioZoneListenerPolicy.RenderedAudioListener, null, null, open);
            var reverb = systems.AddComponent<AudioReverbZone>(); reverb.enabled = false; world.SetNativeReverbZone(reverb);

            Selection.activeGameObject = cameraObject;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Built synthetic Audio Zone test scene at " + ScenePath + ". Use NFS MW Remaster/Driving/Audio Zone Editor and Preview to traverse the four spaces.");
        }

        private static AudioZoneProfile GetOrCreateProfile(string fileName, AudioZoneCategory category)
        {
            string path = Folder + "/" + fileName;
            var profile = AssetDatabase.LoadAssetAtPath<AudioZoneProfile>(path);
            bool created = profile == null;
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<AudioZoneProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            // Demo content is intentionally additive. Never reset a profile that
            // a developer has already authored at the canonical demo path.
            if (!created) return profile;
            profile.SetDefaults(category, "audio.demo.profile." + category.ToString().ToLowerInvariant());
            profile.SetDisplayName("Demo " + category);
            profile.SetBlendMode(category == AudioZoneCategory.OpenStreet ? AudioZoneBlendMode.WeightedBlend : AudioZoneBlendMode.PriorityOverride);
            profile.SetPriority(category == AudioZoneCategory.OpenStreet ? 0 : 20);
            profile.SetAcoustics(0, category == AudioZoneCategory.OpenStreet ? 22000 : category == AudioZoneCategory.Garage ? 7000 : 10500,
                20, category == AudioZoneCategory.OpenStreet ? 0 : 0.35f, category == AudioZoneCategory.Garage ? 1.6f : 2.2f,
                category == AudioZoneCategory.Underpass ? AudioReverbPreset.Cave : category == AudioZoneCategory.Garage ? AudioReverbPreset.ParkingLot : AudioReverbPreset.Off);
            var wind = Clip("wind.wav"); var traffic = Clip("traffic.wav");
            if (category == AudioZoneCategory.OpenStreet) profile.SetAmbience(new[] {
                new AudioZoneAmbienceLayer { id = "wind", clip = wind, category = SensoryCategory.Environment, gain = 0.25f, priority = 210, loop = true, maxDistance = 100 },
                new AudioZoneAmbienceLayer { id = "traffic-bed", clip = traffic, category = SensoryCategory.Environment, gain = 0.2f, priority = 211, loop = true, maxDistance = 100 }
            });
            else if (category == AudioZoneCategory.Underpass) profile.SetAmbience(new[] {
                new AudioZoneAmbienceLayer { id = "underpass-air", clip = wind, category = SensoryCategory.Environment, gain = 0.32f, pitch = 0.82f, priority = 212, loop = true, maxDistance = 70 }
            });
            else if (category == AudioZoneCategory.IndustrialInterior) profile.SetAmbience(new[] {
                new AudioZoneAmbienceLayer { id = "industrial-bed", clip = traffic, category = SensoryCategory.Environment, gain = 0.28f, pitch = 0.72f, priority = 213, loop = true, maxDistance = 65 }
            });
            else profile.SetAmbience(new[] {
                new AudioZoneAmbienceLayer { id = "garage-air", clip = wind, category = SensoryCategory.Environment, gain = 0.22f, pitch = 0.6f, priority = 214, loop = true, maxDistance = 50 }
            });
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static AudioClip Clip(string fileName) => AssetDatabase.LoadAssetAtPath<AudioClip>(DiagnosticFolder + "/" + fileName);

        private static AudioZone CreateZone(string name, string id, AudioZoneShape shape, AudioZoneProfile profile, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name); go.transform.position = position;
            var zone = go.AddComponent<AudioZone>(); zone.SetStableId(id); zone.SetShape(shape); zone.SetProfile(profile); zone.SetCenter(Vector3.zero); zone.SetSize(size);
            return zone;
        }

        private static AudioZonePortal CreatePortal(string name, AudioZone source, AudioZone target, Vector3 position)
        {
            var go = new GameObject(name); go.transform.position = position;
            var portal = go.AddComponent<AudioZonePortal>(); portal.SetStableId("audio.demo.portal." + name.Replace(' ', '-').ToLowerInvariant()); portal.SetEndpoints(source, target); portal.SetState(AudioZonePortalState.Open);
            return portal;
        }

        private static void CreateCube(string name, Vector3 position, Vector3 scale)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = name; cube.transform.position = position; cube.transform.localScale = scale;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
