#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public enum AudioZoneDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public sealed class AudioZoneDiagnostic
    {
        public AudioZoneDiagnosticSeverity severity;
        public string code = string.Empty;
        public string message = string.Empty;
        public UnityEngine.Object target;
        public string property = string.Empty;

        public AudioZoneDiagnostic(AudioZoneDiagnosticSeverity severity, string code, string message,
            UnityEngine.Object target = null, string property = "")
        {
            this.severity = severity;
            this.code = code ?? string.Empty;
            this.message = message ?? string.Empty;
            this.target = target;
            this.property = property ?? string.Empty;
        }
    }

    /// <summary>Editor-only catalog, validation and reversible authoring operations.</summary>
    public static class AudioZoneEditorModel
    {
        public const int MaximumLoadedZones = 4096;
        public const int MaximumLoadedPortals = 2048;

        public static List<AudioZone> FindZones()
        {
            var result = new List<AudioZone>(UnityEngine.Object.FindObjectsByType<AudioZone>(FindObjectsInactive.Include));
            result.RemoveAll(zone => zone == null || !zone.gameObject.scene.IsValid() || !zone.gameObject.scene.isLoaded);
            result.Sort((a, b) => string.Compare(a.StableId, b.StableId, StringComparison.Ordinal));
            return result;
        }

        public static List<AudioZonePortal> FindPortals()
        {
            var result = new List<AudioZonePortal>(UnityEngine.Object.FindObjectsByType<AudioZonePortal>(FindObjectsInactive.Include));
            result.RemoveAll(portal => portal == null || !portal.gameObject.scene.IsValid() || !portal.gameObject.scene.isLoaded);
            result.Sort((a, b) => string.Compare(a.StableId, b.StableId, StringComparison.Ordinal));
            return result;
        }

        public static List<AudioZoneProfile> FindProfiles()
        {
            var result = new List<AudioZoneProfile>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioZoneProfile"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<AudioZoneProfile>(path);
                if (profile != null) result.Add(profile);
            }
            result.Sort((a, b) => string.Compare(a.DisplayName + "|" + a.StableId, b.DisplayName + "|" + b.StableId, StringComparison.Ordinal));
            return result;
        }

        public static List<AudioZoneDiagnostic> ValidateAll()
        {
            var diagnostics = new List<AudioZoneDiagnostic>();
            var zones = FindZones();
            var portals = FindPortals();
            var profiles = FindProfiles();
            if (zones.Count == 0) diagnostics.Add(new AudioZoneDiagnostic(AudioZoneDiagnosticSeverity.Info, "ZONE_NONE", "No AudioZone components are loaded in the open scenes."));
            if (zones.Count > MaximumLoadedZones) diagnostics.Add(new AudioZoneDiagnostic(AudioZoneDiagnosticSeverity.Error, "ZONE_BUDGET", "Loaded zone count exceeds the editor/runtime budget of " + MaximumLoadedZones + "."));
            if (portals.Count > MaximumLoadedPortals) diagnostics.Add(new AudioZoneDiagnostic(AudioZoneDiagnosticSeverity.Error, "PORTAL_BUDGET", "Loaded portal count exceeds the editor/runtime budget of " + MaximumLoadedPortals + "."));

            var zoneIds = new Dictionary<string, AudioZone>(StringComparer.Ordinal);
            foreach (var zone in zones)
            {
                if (string.IsNullOrWhiteSpace(zone.StableId)) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "ZONE_ID", "Zone stable ID is empty.", zone, "stableId");
                else if (zoneIds.TryGetValue(zone.StableId, out var other)) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "ZONE_ID_DUPLICATE", "Stable ID is shared with " + other.name + ". Duplicate authoring IDs make runtime tie-breaking unsafe.", zone, "stableId");
                else zoneIds.Add(zone.StableId, zone);
                if (zone.Profile == null) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "ZONE_PROFILE", "Zone has no acoustic profile.", zone, "profile");
                else if (!zone.Profile.Validate(out string failure)) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "PROFILE_INVALID", failure, zone.Profile);
                if (!FiniteBounds(zone.WorldBounds)) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "ZONE_BOUNDS", "Zone bounds contain a non-finite or empty value.", zone);
                if (zone.Shape == AudioZoneShape.Convex && zone.ConvexCollider == null && zone.ConvexMesh == null)
                    Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "CONVEX_SOURCE", "Convex zone needs an authored Collider or Mesh.", zone, "convexMesh");
                if (!zone.EnabledForRuntime) Add(diagnostics, AudioZoneDiagnosticSeverity.Info, "ZONE_DISABLED", "Zone is disabled for runtime evaluation.", zone, "enabledForRuntime");
            }

            var profileIds = new Dictionary<string, AudioZoneProfile>(StringComparer.Ordinal);
            foreach (var profile in profiles)
            {
                if (!string.IsNullOrWhiteSpace(profile.StableId) && profileIds.TryGetValue(profile.StableId, out var other))
                    Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "PROFILE_ID_DUPLICATE", "Stable ID is shared with " + other.name + ".", profile, "stableId");
                else if (!string.IsNullOrWhiteSpace(profile.StableId)) profileIds.Add(profile.StableId, profile);
                if (!profile.Validate(out string failure)) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "PROFILE_INVALID", failure, profile);
                ValidateProfileAudio(diagnostics, profile);
            }

            var portalIds = new Dictionary<string, AudioZonePortal>(StringComparer.Ordinal);
            var endpointKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var portal in portals)
            {
                if (string.IsNullOrWhiteSpace(portal.StableId)) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "PORTAL_ID", "Portal stable ID is empty.", portal, "stableId");
                else if (portalIds.TryGetValue(portal.StableId, out var other)) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "PORTAL_ID_DUPLICATE", "Stable ID is shared with " + other.name + ".", portal, "stableId");
                else portalIds.Add(portal.StableId, portal);
                if (portal.SourceZone == null || portal.TargetZone == null) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "PORTAL_ENDPOINT", "Portal must reference both a source and target zone.", portal);
                else if (portal.SourceZone == portal.TargetZone) Add(diagnostics, AudioZoneDiagnosticSeverity.Error, "PORTAL_SELF", "Portal cannot connect a zone to itself.", portal);
                else
                {
                    string key = portal.SourceZone.StableId + "|" + portal.TargetZone.StableId + "|" + (portal.Bidirectional ? "b" : "o");
                    if (!endpointKeys.Add(key)) Add(diagnostics, AudioZoneDiagnosticSeverity.Warning, "PORTAL_DUPLICATE_ENDPOINT", "Another portal already connects this endpoint pair.", portal);
                    if (!portal.SourceZone.WorldBounds.Intersects(portal.TargetZone.WorldBounds))
                        Add(diagnostics, AudioZoneDiagnosticSeverity.Warning, "PORTAL_DISCONNECTED", "Portal endpoint volumes do not overlap; this can be intentional for a door or tunnel mouth, but verify the connection in Scene view.", portal);
                }
            }

            var worlds = new List<AudioZoneWorld>(UnityEngine.Object.FindObjectsByType<AudioZoneWorld>(FindObjectsInactive.Include));
            if (zones.Count > 0 && worlds.Count == 0) Add(diagnostics, AudioZoneDiagnosticSeverity.Warning, "WORLD_MISSING", "Loaded zones have no AudioZoneWorld. Runtime membership and arbitration will not run.");
            if (worlds.Count > 1) Add(diagnostics, AudioZoneDiagnosticSeverity.Warning, "WORLD_MULTIPLE", "Multiple AudioZoneWorld components are loaded. Use one owner per acoustic runtime context.");
            if (worlds.Count > 0 && worlds[0].AudioWorld == null) Add(diagnostics, AudioZoneDiagnosticSeverity.Warning, "AUDIO_WORLD_MISSING", "AudioZoneWorld has no SensoryAudioWorld reference; ambience and environment filters will be unavailable.", worlds[0], "audioWorld");
            int listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length;
            if (listeners > 1) Add(diagnostics, AudioZoneDiagnosticSeverity.Warning, "LISTENER_MULTIPLE", "More than one AudioListener is loaded. Runtime should have one active listener; preview manages its own reversible listener state.");
            if (zones.Count > 0 && worlds.Count > 0 && worlds[0].FallbackProfile == null)
                Add(diagnostics, AudioZoneDiagnosticSeverity.Info, "FALLBACK_NONE", "No fallback profile is assigned. Outside authored zones the runtime will use neutral filters and no fallback ambience.", worlds[0], "fallbackProfile");

            ValidateOverlaps(diagnostics, zones);
            return diagnostics;
        }

        public static string ComputeRevision(IList<AudioZone> zones, IList<AudioZonePortal> portals)
        {
            var text = new System.Text.StringBuilder();
            foreach (var zone in zones ?? Array.Empty<AudioZone>())
            {
                if (zone == null) continue;
                text.Append(zone.StableId).Append('|').Append(zone.Profile != null ? zone.Profile.StableId : string.Empty).Append('|')
                    .Append(zone.Shape).Append('|');
                AppendVector(text, zone.transform.position);
                AppendQuaternion(text, zone.transform.rotation);
                AppendVector(text, zone.transform.lossyScale);
                text.Append(';');
            }
            foreach (var portal in portals ?? Array.Empty<AudioZonePortal>())
            {
                if (portal == null) continue;
                text.Append(portal.StableId).Append('|').Append(portal.SourceZone != null ? portal.SourceZone.StableId : string.Empty).Append('|')
                    .Append(portal.TargetZone != null ? portal.TargetZone.StableId : string.Empty).Append('|').Append(portal.State).Append(';');
            }
            return Hash128.Compute(text.ToString()).ToString();
        }

        private static void AppendVector(System.Text.StringBuilder text, Vector3 value)
        {
            text.Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void AppendQuaternion(System.Text.StringBuilder text, Quaternion value)
        {
            text.Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.w.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        public static AudioZoneProfile CreateProfile(string folder, AudioZoneCategory category)
        {
            if (string.IsNullOrWhiteSpace(folder)) folder = "Assets/NfsMw/Modules/Driving/Data/AudioZones";
            EnsureFolder(folder);
            string id = "audio.profile." + category.ToString().ToLowerInvariant() + "." + Guid.NewGuid().ToString("N").Substring(0, 8);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + category + ".asset");
            var profile = ScriptableObject.CreateInstance<AudioZoneProfile>();
            profile.SetDefaults(category, id);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            return profile;
        }

        public static AudioZone CreateZone(AudioZoneShape shape, AudioZoneProfile profile, Vector3 position)
        {
            var go = new GameObject(shape + " Audio Zone");
            Undo.RegisterCreatedObjectUndo(go, "Create audio zone");
            go.transform.position = position;
            var zone = go.AddComponent<AudioZone>();
            zone.SetStableId("audio.zone." + Guid.NewGuid().ToString("N"));
            zone.SetShape(shape);
            zone.SetProfile(profile);
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
            return zone;
        }

        public static AudioZonePortal CreatePortal(AudioZone source, AudioZone target, Vector3 position)
        {
            var go = new GameObject("Audio Zone Portal");
            Undo.RegisterCreatedObjectUndo(go, "Create audio zone portal");
            go.transform.position = position;
            var portal = go.AddComponent<AudioZonePortal>();
            portal.SetStableId("audio.portal." + Guid.NewGuid().ToString("N"));
            portal.SetEndpoints(source, target);
            Selection.activeGameObject = go;
            return portal;
        }

        private static void ValidateProfileAudio(List<AudioZoneDiagnostic> diagnostics, AudioZoneProfile profile)
        {
            foreach (var layer in profile.Ambience)
            {
                if (layer == null || layer.clip == null)
                {
                    Add(diagnostics, AudioZoneDiagnosticSeverity.Warning, "AMBIENCE_CLIP", "Ambience layer has no AudioClip; it will be skipped at runtime.", profile, "ambience");
                    continue;
                }
                if (layer.clip.loadState == AudioDataLoadState.Unloaded)
                    Add(diagnostics, AudioZoneDiagnosticSeverity.Info, "AMBIENCE_STREAMING", "Clip is not resident in the editor. Runtime/preview loading policy must make it available before audition.", profile, "ambience");
            }
        }

        private static void ValidateOverlaps(List<AudioZoneDiagnostic> diagnostics, List<AudioZone> zones)
        {
            int pairCount = 0;
            for (int i = 0; i < zones.Count && pairCount < 10000; i++)
                for (int j = i + 1; j < zones.Count && pairCount++ < 10000; j++)
                {
                    var a = zones[i]; var b = zones[j];
                    if (a == null || b == null || !a.WorldBounds.Intersects(b.WorldBounds) || a.Profile == null || b.Profile == null) continue;
                    if (a.Profile.BlendMode == AudioZoneBlendMode.PriorityOverride && b.Profile.BlendMode == AudioZoneBlendMode.PriorityOverride
                        && a.Profile.Priority == b.Profile.Priority)
                        Add(diagnostics, AudioZoneDiagnosticSeverity.Warning, "OVERLAP_TIE", "Overlapping PriorityOverride zones share the same priority. Stable IDs resolve the tie, but verify the intended boundary.", a);
                }
        }

        private static bool FiniteBounds(Bounds bounds)
            => SensoryMath.IsFinite(bounds.center.x) && SensoryMath.IsFinite(bounds.center.y) && SensoryMath.IsFinite(bounds.center.z)
                && SensoryMath.IsFinite(bounds.size.x) && SensoryMath.IsFinite(bounds.size.y) && SensoryMath.IsFinite(bounds.size.z)
                && bounds.size.sqrMagnitude > 0;

        private static void Add(List<AudioZoneDiagnostic> diagnostics, AudioZoneDiagnosticSeverity severity, string code,
            string message, UnityEngine.Object target = null, string property = "")
            => diagnostics.Add(new AudioZoneDiagnostic(severity, code, message, target, property));

        private static void EnsureFolder(string path)
        {
            string normalized = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized)) return;
            string[] parts = normalized.Split('/');
            string current = parts[0];
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
