using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Authoring surface for the single production VehicleAudio component.</summary>
    [CustomEditor(typeof(VehicleAudio))]
    public sealed class VehicleAudioEditor : UnityEditor.Editor
    {
        private SerializedProperty profile, source, world, camera, effects, player;
        private SerializedProperty mix, sounds, bankOverride, useBankOverride, manualInput;

        private void OnEnable()
        {
            profile = serializedObject.FindProperty("profile");
            source = serializedObject.FindProperty("sourceComponent");
            world = serializedObject.FindProperty("world");
            camera = serializedObject.FindProperty("cameraRig");
            effects = serializedObject.FindProperty("effects");
            player = serializedObject.FindProperty("player");
            mix = serializedObject.FindProperty("mix");
            sounds = serializedObject.FindProperty("sounds");
            bankOverride = serializedObject.FindProperty("bankOverride");
            useBankOverride = serializedObject.FindProperty("useBankOverride");
            manualInput = serializedObject.FindProperty("manualInput");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("VehicleAudio owns telemetry routing, recovered content, authored mix controls and rehearsal input.", MessageType.Info);
            DrawSetup();
            DrawMix();
            DrawSounds();
            serializedObject.ApplyModifiedProperties();
            DrawValidation();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", ((VehicleAudio)target).RuntimeStatus ?? "");
            EditorGUILayout.LabelField("Active layers", ((VehicleAudio)target).ActiveLayers.ToString());
            EditorGUILayout.LabelField("Control ticks", ((VehicleAudio)target).ControlTicks.ToString());
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button("Rebuild audio content")) ((VehicleAudio)target).Rebuild();
            if (GUILayout.Button("Open VehicleAudio Rehearsal")) VehicleAudioRehearsalWindow.Open((VehicleAudio)target);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSetup()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Vehicle source", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(profile, new GUIContent("Decoded profile"));
                EditorGUILayout.PropertyField(useBankOverride, new GUIContent("Use explicit bank override"));
                if (useBankOverride != null && useBankOverride.boolValue) EditorGUILayout.PropertyField(bankOverride, new GUIContent("Most Wanted bank"), true);
                EditorGUILayout.PropertyField(source, new GUIContent("Telemetry source"));
                EditorGUILayout.PropertyField(world, new GUIContent("Audio world"));
                EditorGUILayout.PropertyField(player, new GUIContent("Player vehicle"));
                EditorGUILayout.PropertyField(camera, new GUIContent("Camera rig"));
                EditorGUILayout.PropertyField(effects, new GUIContent("Effects world"));
                var audio = (VehicleAudio)target;
                bool manual = EditorGUILayout.Toggle("Manual rehearsal input", audio.ManualInput);
                if (manual != audio.ManualInput)
                {
                    serializedObject.ApplyModifiedProperties(); Undo.RecordObject(audio, "Change audio input");
                    audio.ManualInput = manual; EditorUtility.SetDirty(audio); serializedObject.Update();
                }
            }
        }

        private void DrawMix()
        {
            if (mix == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                mix.isExpanded = EditorGUILayout.Foldout(mix.isExpanded, "Author mix", true);
                if (mix.isExpanded)
                {
                    EditorGUILayout.PropertyField(mix.FindPropertyRelative("mute"));
                    EditorGUILayout.PropertyField(mix.FindPropertyRelative("gain"));
                    EditorGUILayout.PropertyField(mix.FindPropertyRelative("pitch"), new GUIContent("Pitch", "Playback pitch multiplier; 1 is source pitch."));
                    EditorGUILayout.PropertyField(mix.FindPropertyRelative("tempo"), new GUIContent("Tempo", "Playback time multiplier; 1 is source tempo."));
                    EditorGUILayout.PropertyField(mix.FindPropertyRelative("channels"), new GUIContent("Per-channel controls"), true);
                }
            }
        }

        private void DrawSounds()
        {
            if (sounds == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                sounds.isExpanded = EditorGUILayout.Foldout(sounds.isExpanded, "Additional sounds and interfaces", true);
                if (sounds.isExpanded)
                {
                    EditorGUILayout.PropertyField(sounds, new GUIContent("Sounds"), true);
                    for (int i = 0; i < sounds.arraySize; i++) SyncBankParameters(sounds.GetArrayElementAtIndex(i));
                }
            }
        }

        private static void SyncBankParameters(SerializedProperty item)
        {
            var bank = item.FindPropertyRelative("bank")?.objectReferenceValue as AemsAudioBank;
            var name = item.FindPropertyRelative("interfaceName");
            if (bank == null || name == null || bank.programs == null || bank.programs.Length == 0) return;
            var names = new List<string>();
            foreach (var candidate in bank.programs) if (candidate != null) names.Add(candidate.interfaceName);
            if (names.Count == 0) return;
            int selected = names.IndexOf(name.stringValue);
            int next = EditorGUILayout.Popup("Recovered interface", selected, names.ToArray());
            bool changed = next >= 0 && next != selected;
            if (changed) name.stringValue = names[next];
            var program = Array.Find(bank.programs, p => p != null && p.interfaceName == name.stringValue);
            var parameters = item.FindPropertyRelative("parameters");
            if (program == null || parameters == null || !changed && parameters.arraySize == program.parameterCount) return;
            int previousCount = parameters.arraySize;
            parameters.arraySize = program.parameterCount;
            for (int i = changed ? 0 : previousCount; i < parameters.arraySize; i++) parameters.GetArrayElementAtIndex(i).intValue = 0;
        }

        private void DrawValidation()
        {
            var errors = new List<string>();
            if (useBankOverride != null && useBankOverride.boolValue && bankOverride != null)
            {
                var schema = bankOverride.FindPropertyRelative("schema");
                if (schema != null && schema.intValue != 1)
                    errors.Add("The explicit Most Wanted bank override needs schema 1 and acceleration/deceleration mappings.");
                else if (((VehicleAudio)target).Banks == null) errors.Add("Assign a Most Wanted bank override.");
                else if (!((VehicleAudio)target).Banks.Validate(out string bankFailure)) errors.Add("Most Wanted bank override is incomplete: " + bankFailure);
            }
            if (source != null && source.objectReferenceValue != null && !(source.objectReferenceValue is VehicleController) && !(source.objectReferenceValue is IVehicleFeedbackSource))
                errors.Add("Telemetry source must be a VehicleController or implement IVehicleFeedbackSource.");
            if (profile != null && profile.objectReferenceValue == null && !(useBankOverride != null && useBankOverride.boolValue) && sounds.arraySize == 0)
                errors.Add("Assign an decoded VehicleSensoryProfile or enable an explicit bank override.");
            if (profile != null && profile.objectReferenceValue is VehicleSensoryProfile selectedProfile && !selectedProfile.Validate(out string profileFailure))
                errors.Add("Decoded profile is invalid: " + profileFailure);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (sounds != null && sounds.arraySize > 0)
                for (int i = 0; i < sounds.arraySize; i++)
                {
                    var item = sounds.GetArrayElementAtIndex(i);
                    var id = item.FindPropertyRelative("id");
                    if (id != null && !ids.Add(id.stringValue)) errors.Add("Additional sound IDs must be unique: " + id.stringValue);
                    var clip = item.FindPropertyRelative("clip");
                    var bank = item.FindPropertyRelative("bank");
                    var iface = item.FindPropertyRelative("interfaceName");
                    var parameters = item.FindPropertyRelative("parameters");
                    var bindings = item.FindPropertyRelative("bindings");
                    if (string.IsNullOrWhiteSpace(id.stringValue)) errors.Add("Additional sound IDs cannot be empty.");
                    if (clip != null && bank != null && clip.objectReferenceValue != null && bank.objectReferenceValue != null)
                        errors.Add("Sound " + id.stringValue + " must use either a custom clip or a decoded bank.");
                    if (clip != null && bank != null && clip.objectReferenceValue == null && bank.objectReferenceValue == null)
                        errors.Add("Sound " + (id == null ? i.ToString() : id.stringValue) + " needs a custom clip or decoded bank.");
                    if (bank != null && bank.objectReferenceValue is AemsAudioBank audioBank)
                    {
                        var program = Array.Find(audioBank.programs ?? Array.Empty<AemsProgram>(), p => p != null && p.interfaceName == iface.stringValue);
                        if (program == null) errors.Add("Bank-backed sound " + id.stringValue + " needs an interface name from its decoded bank.");
                        else if (parameters != null && parameters.arraySize != program.parameterCount)
                            errors.Add("Bank-backed sound " + id.stringValue + " has " + parameters.arraySize + " parameters; the selected interface requires " + program.parameterCount + ".");
                        if (program != null && bindings != null)
                            for (int b = 0; b < bindings.arraySize; b++)
                            {
                                int slot = bindings.GetArrayElementAtIndex(b).FindPropertyRelative("parameter").intValue;
                                if (slot < 0 || slot >= program.parameterCount) errors.Add("Binding for " + id.stringValue + " refers to an invalid parameter slot.");
                            }
                    }
                }
            if (errors.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Warning);
            else EditorGUILayout.HelpBox("VehicleAudio authoring checks passed.", MessageType.None);
        }
    }
}
