#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Editor-only authoring adapter pinned to the installed Unity version. Runtime uses public mixer APIs.</summary>
    public static class SensoryMixerBuilder
    {
        internal static Type EditorType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            { var type = assembly.GetType("UnityEditor.Audio." + name); if (type != null) return type; }
            throw new NotSupportedException("Unity editor mixer authoring API unavailable: " + name);
        }
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static object Call(object target, string name, params object[] args)
        {
            var type = target as Type ?? target.GetType();
            var method = type.GetMethod(name, Flags) ?? throw new NotSupportedException("Mixer authoring method missing: " + name);
            return method.Invoke(target is Type ? null : target, args);
        }
        public static SensoryMixProfile GetOrCreate(string folder)
        {
            string path = folder + "/SensoryMix.asset";
            var saved = AssetDatabase.LoadAssetAtPath<SensoryMixProfile>(path);
            if (saved != null && saved.mixer != null && saved.routes.Length == 9 && saved.snapshots.Length == 6
                && Array.TrueForAll(saved.routes, route => route != null && route.group != null)
                && Array.TrueForAll(saved.snapshots, snapshot => snapshot != null && snapshot.snapshot != null)) return saved;
            string mixerPath = folder + "/Sensory.mixer";
            var type = EditorType("AudioMixerController");
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(mixerPath) ?? (AudioMixer)Call(type, "CreateMixerControllerAtPath", mixerPath);
            var master = type.GetProperty("masterGroup", Flags).GetValue(mixer);
            var groups = new System.Collections.Generic.Dictionary<string, object> { ["Master"] = master };
            object Group(string name, string parent)
            {
                object value = null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(mixerPath)) if (asset is AudioMixerGroup && asset.name == name) { value = asset; break; }
                if (value == null) value = Call(mixer, "CreateNewGroup", name, false);
                var serialized = new SerializedObject((UnityEngine.Object)groups[parent]); var children = serialized.FindProperty("m_Children");
                bool linked = false;
                for (int i = 0; i < children.arraySize; i++) if (children.GetArrayElementAtIndex(i).objectReferenceValue == (UnityEngine.Object)value) linked = true;
                if (!linked) Call(mixer, "AddChildToParent", value, groups[parent]);
                groups[name] = value; return value;
            }
            Group("Vehicle", "Master"); Group("Effects", "Master"); Group("Police", "Master"); Group("Music", "Master");
            var profile = saved != null ? saved : ScriptableObject.CreateInstance<SensoryMixProfile>(); profile.mixer = mixer;
            profile.routes = new SensoryMixRoute[9];
            foreach (SensoryCategory category in Enum.GetValues(typeof(SensoryCategory)))
            {
                string parent = category == SensoryCategory.Player || category == SensoryCategory.OtherVehicle ? "Vehicle"
                    : category == SensoryCategory.Radio || category == SensoryCategory.Sirens ? "Police"
                    : category == SensoryCategory.Music ? "Music" : "Effects";
                var leaf = Group(category + " Output", parent);
                profile.routes[(int)category] = new SensoryMixRoute { category = category, group = (AudioMixerGroup)leaf };
            }
            var target = type.GetProperty("TargetSnapshot", Flags);
            var first = (AudioMixerSnapshot)target.GetValue(mixer); first.name = "FreeRoam";
            type.GetProperty("startSnapshot", Flags).SetValue(mixer, first);
            profile.snapshots = new SensorySnapshot[6];
            foreach (SensoryMixState state in Enum.GetValues(typeof(SensoryMixState)))
            {
                if (state != SensoryMixState.FreeRoam) Call(mixer, "CloneNewSnapshotFromTarget", false);
                var snapshot = (AudioMixerSnapshot)target.GetValue(mixer); snapshot.name = state.ToString();
                profile.snapshots[(int)state] = new SensorySnapshot { state = state, snapshot = snapshot };
                foreach (var route in profile.routes)
                {
                    float db = route.category == SensoryCategory.OtherVehicle ? -6 : 0;
                    if (state == SensoryMixState.Paused) db -= 6;
                    if (state == SensoryMixState.Crash && route.category != SensoryCategory.Impacts) db -= 6;
                    if (state == SensoryMixState.Pursuit && route.category == SensoryCategory.Environment) db -= 3;
                    Call(route.group, "SetValueForVolume", mixer, snapshot, db);
                }
            }
            target.SetValue(mixer, first);
            var parameterType = EditorType("ExposedAudioParameter");
            var fields = parameterType.GetFields(Flags);
            var guidField = Array.Find(fields, field => field.FieldType.Name == "GUID");
            var nameField = Array.Find(fields, field => field.FieldType == typeof(string));
            if (guidField == null || nameField == null) throw new NotSupportedException("Mixer parameter schema changed.");
            var parameters = Array.CreateInstance(parameterType, 5); int index = 0;
            foreach (string name in new[] { "Master", "Vehicle", "Effects", "Music", "Police" })
            {
                object parameter = Activator.CreateInstance(parameterType);
                guidField.SetValue(parameter, Call(groups[name], "GetGUIDForVolume")); nameField.SetValue(parameter, name + "Volume");
                parameters.SetValue(parameter, index++);
            }
            type.GetProperty("exposedParameters", Flags).SetValue(mixer, parameters);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(mixerPath)) EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(mixer); EditorUtility.SetDirty(profile);
            if (saved == null) AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            return profile;
        }
        public static void Inspect()
        {
            foreach (string name in new[] { "AudioMixerController", "AudioMixerGroupController", "AudioMixerSnapshotController", "AudioParameterPath" })
            {
                var type = EditorType(name);
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    if (method.Name.Contains("Group") || method.Name.Contains("Snapshot") || method.Name.Contains("Exposed") || method.Name.Contains("Volume") || method.Name.Contains("Parameter"))
                        Debug.Log(name + " " + method);
                foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) Debug.Log(name + " CTOR " + constructor);
            }
            EditorApplication.Exit(0);
        }
    }
}
#endif
