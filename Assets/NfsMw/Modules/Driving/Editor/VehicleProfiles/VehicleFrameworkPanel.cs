using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Shared IMGUI editor; pending edits belong to the view, never to a factory asset.</summary>
    internal sealed class VehicleFrameworkPanel
    {
        private VehicleProfileDraft activeDraft;
        private VehicleConfiguration live;
        private readonly List<VehicleTuningAdjustment> pending = new List<VehicleTuningAdjustment>();
        private string failure = "", search = "";
        private int section;
        private bool advanced;
        public void Close() { if (live != null) live.GetComponent<VehicleCustomizationSystem>()?.CancelPreview(); live = null; }
        public void Draw(VehicleProfileDraft draft, SerializedObject serialized)
        {
            if (draft == null) { EditorGUILayout.HelpBox("Select a vehicle draft first.", MessageType.Info); return; }
            if (activeDraft != draft) { Close(); activeDraft = draft; pending.Clear(); failure = ""; }
            EditorGUILayout.HelpBox("Choose identity → bind the model → configure handling and parts → validate → preview → build. Factory assets are shared; live instance edits are saved through the career system.", MessageType.Info);
            Field(serialized, "runtimeDefinition"); serialized.ApplyModifiedProperties();
            section = GUILayout.Toolbar(section, new[] { "Overview", "Driving", "Parts", "Diagnostics" });
            advanced = EditorGUILayout.ToggleLeft("Advanced settings", advanced);
            if (section == 0) DrawOverview(draft, serialized);
            if (section == 1) DrawDriving(draft);
            if (section == 2) DrawParts(draft);
            if (section == 3) DrawDiagnostics(draft);
            if (!string.IsNullOrEmpty(failure)) EditorGUILayout.HelpBox(failure, MessageType.Error);
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Send configuration to Physics Lab")) TryHandoff(draft, live, true, out failure);
                if (GUILayout.Button("Engine audio")) AudioAnalysis.BlackBoxInspectorWindow.Open(draft, draft.audio);
                if (GUILayout.Button("Most Wanted mechanics")) DrivingMechanics.MostWantedDrivingWindow.Open(draft.tuning);
            }
        }
        private void DrawOverview(VehicleProfileDraft draft, SerializedObject serialized)
        {
            if (draft.runtimeDefinition == null)
            {
                EditorGUILayout.HelpBox("Assign a Vehicle Definition above, or create one from the draft's identity and factory tuning.", MessageType.Warning);
                if (GUILayout.Button("Create definition")) CreateDefinition(draft);
            }
            else
            {
                using var obj = new SerializedObject(draft.runtimeDefinition); obj.Update();
                foreach (string field in new[] { "manufacturer", "model", "year", "variantId", "factoryTuning", "capabilities" }) Field(obj, field);
                if (advanced) foreach (string field in new[] { "vehicleId", "supportedSlots", "tuningLimits", "referenceEvidence" }) Field(obj, field);
                obj.ApplyModifiedProperties();
            }
            foreach (string field in new[] { "vehiclePrefab", "tuning", "performance", "customization", "physicsLabSetup", "physicsLabExperiment" }) Field(serialized, field);
            EditorGUILayout.HelpBox("Model Bindings holds explicit wheel, lamp, glass, cockpit, mirror and socket mappings. Generate or update the production prefab in Build.", MessageType.None);
            DrawLiveSelector(draft);
        }
        private void DrawLiveSelector(VehicleProfileDraft draft)
        {
            var selected = (VehicleConfiguration)EditorGUILayout.ObjectField(new GUIContent("Live vehicle", "Optional scene instance with this draft's definition. Changes never mutate the factory tuning."), live, typeof(VehicleConfiguration), true);
            if (selected != live)
            {
                if (selected != null && (EditorUtility.IsPersistent(selected) || selected.Definition != draft.runtimeDefinition)) failure = "Choose a scene instance with this draft's Vehicle Definition.";
                else { Close(); live = selected; ReloadPending(); failure = ""; }
            }
            var selection = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<VehicleConfiguration>() : null;
            if (selection != null && selection != live && selection.Definition == draft.runtimeDefinition && !EditorUtility.IsPersistent(selection)
                && GUILayout.Button("Use selected scene vehicle")) { Close(); live = selection; ReloadPending(); }
        }
        private void ReloadPending() { pending.Clear(); if (live != null) pending.AddRange(live.Adjustments); }
        private void DrawDriving(VehicleProfileDraft draft)
        {
            DrawLiveSelector(draft);
            if (live != null)
            {
                EditorGUILayout.LabelField("Instance tuning adjustments", EditorStyles.boldLabel);
                int remove = -1;
                for (int i = 0; i < pending.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var value = pending[i]; value.parameter = (VehiclePhysicsLabTuningParameter)EditorGUILayout.EnumPopup(value.parameter);
                        value.value = EditorGUILayout.FloatField(value.value); pending[i] = value;
                        GUILayout.Label(VehicleConfigurationResolver.Unit(value.parameter), GUILayout.Width(45));
                        if (GUILayout.Button("−", GUILayout.Width(24))) remove = i;
                    }
                }
                if (remove >= 0) pending.RemoveAt(remove);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add adjustment")) pending.Add(new VehicleTuningAdjustment { parameter = VehiclePhysicsLabTuningParameter.Mass, value = live.Definition.factoryTuning.chassis.mass });
                    if (GUILayout.Button("Reload instance")) ReloadPending();
                    if (GUILayout.Button("Reset adjustments")) pending.Clear();
                    if (GUILayout.Button("Apply"))
                    {
                        Undo.RecordObject(live, "Apply vehicle tuning");
                        if (live.TrySetAdjustments(pending.ToArray(), out failure)) { EditorUtility.SetDirty(live); PrefabUtility.RecordPrefabInstancePropertyModifications(live); }
                    }
                }
            }
            var tuning = draft.runtimeDefinition != null ? draft.runtimeDefinition.factoryTuning : draft.tuning;
            if (tuning != null)
            {
                EditorGUILayout.LabelField("Factory handling (shared asset)", EditorStyles.boldLabel);
                using var obj = new SerializedObject(tuning); obj.Update();
                foreach (string field in new[] { "driveLayout", "transmissionMode", "differential", "speedGovernor" }) Field(obj, field);
                Field(obj, "simulationModel");
                if (tuning.UsesMostWantedReference) Field(obj, "mostWanted");
                foreach (string field in new[] { "chassis", "engine", "tires", "controls" }) Field(obj, field);
                if (advanced) foreach (string field in new[] { "assists", "handling", "aero" }) Field(obj, field);
                obj.ApplyModifiedProperties();
            }
            DrawResolved(draft);
        }
        private void DrawParts(VehicleProfileDraft draft)
        {
            DrawLiveSelector(draft);
            search = EditorGUILayout.TextField("Find part", search);
            var performance = live != null ? live.GetComponent<VehiclePerformanceSystem>() : null;
            var customization = live != null ? live.GetComponent<VehicleCustomizationSystem>() : null;
            if (live == null) EditorGUILayout.HelpBox("Choose a live vehicle to preview and install parts. Catalogues below are the authored choices for this vehicle.", MessageType.Info);
            var perfCatalog = draft.runtimeDefinition != null ? draft.runtimeDefinition.performanceCatalog : draft.performance;
            if (perfCatalog != null) foreach (var part in perfCatalog.Upgrades)
            {
                if (part == null || !Matches(part.DisplayName)) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(part.DisplayName, part, typeof(VehiclePerformanceUpgradeDefinition), false);
                    using (new EditorGUI.DisabledScope(performance == null))
                    {
                        bool installed = performance != null && performance.Build.Installed.Any(p => p.UpgradeId == part.UpgradeId);
                        if (GUILayout.Button(installed ? "Remove" : "Install", GUILayout.Width(65)))
                        { Undo.RecordObject(performance, "Change vehicle part"); if (installed) performance.TryRemove(part.Category, out failure); else performance.TryInstall(part, out failure); EditorUtility.SetDirty(performance); PrefabUtility.RecordPrefabInstancePropertyModifications(performance); }
                    }
                }
            }
            var customCatalog = draft.runtimeDefinition != null ? draft.runtimeDefinition.customizationCatalog : draft.customization;
            if (customCatalog != null) foreach (var part in customCatalog.Items)
            {
                if (part == null || !Matches(part.DisplayName)) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(part.DisplayName, part, typeof(VehicleCustomizationDefinition), false);
                    using (new EditorGUI.DisabledScope(customization == null))
                    { if (GUILayout.Button("Preview", GUILayout.Width(65))) customization.BeginPreview(part, out failure); }
                }
            }
            if (customization != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!customization.IsPreviewing))
                    {
                        if (GUILayout.Button("Apply preview")) { Undo.RecordObject(customization, "Commit body parts"); customization.TryApplyPreview(out failure); EditorUtility.SetDirty(customization); PrefabUtility.RecordPrefabInstancePropertyModifications(customization); }
                        if (GUILayout.Button("Cancel preview")) customization.CancelPreview();
                    }
                    if (GUILayout.Button("Reset preview")) customization.ResetPreview();
                }
            }
            DrawResolved(draft);
        }
        private bool Matches(string label) => string.IsNullOrEmpty(search) || label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        private void DrawDiagnostics(VehicleProfileDraft draft)
        {
            if (draft.runtimeDefinition != null && !draft.runtimeDefinition.Validate(out var reason)) EditorGUILayout.HelpBox(reason, MessageType.Error);
            foreach (string issue in VehicleProfileAssembly.Validate(draft)) EditorGUILayout.HelpBox(issue, MessageType.Warning);
            if (draft.assembly.presentationSource != null)
            { var issues = new List<string>(); draft.assembly.presentationSource.ValidateBindings(issues); foreach (string issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Warning); }
            DrawResolved(draft);
        }
        private void DrawResolved(VehicleProfileDraft draft)
        {
            EditorGUILayout.LabelField("Factory → parts → adjustment → resolved", EditorStyles.boldLabel);
            try
            {
                var root = live != null ? live.gameObject : draft.vehiclePrefab;
                var perf = root != null ? root.GetComponent<VehiclePerformanceSystem>() : null;
                var custom = root != null ? root.GetComponent<VehicleCustomizationSystem>() : null;
                var configuration = root != null ? root.GetComponent<VehicleConfiguration>() : null;
                var factory = draft.runtimeDefinition != null ? draft.runtimeDefinition.factoryTuning : draft.tuning;
                if (factory == null) return;
                using var resolved = VehicleConfigurationResolver.Resolve(factory, perf?.Build, custom?.EffectiveBuild, configuration?.Adjustments, draft.runtimeDefinition);
                foreach (var row in resolved.Breakdown)
                {
                    if (!Matches(row.parameter.ToString())) continue;
                    EditorGUILayout.LabelField(row.parameter.ToString(), $"{row.factoryValue:0.###} → {row.finalValue:0.###} {row.unit}");
                    if (advanced) foreach (var c in row.contributions) EditorGUILayout.LabelField("  " + c.source, $"{c.before:0.###} → {c.after:0.###}");
                }
                VehicleTopSpeedEstimate estimate = VehicleTopSpeedEstimator.Estimate(resolved.Tuning);
                EditorGUILayout.HelpBox($"Configured target {resolved.Tuning.chassis.maxSpeedKph:0} km/h. Engineering estimate {estimate.estimatedKph:0} km/h (gearing ceiling {estimate.gearingCeilingKph:0} km/h; flat-road full throttle, ideal gear selection; no automatic shift logic/slip/transients/assists/nitrous). Measured results come from a completed Physics Lab run. Governor {(resolved.Tuning.speedGovernor ? "enabled" : "disabled")}.", MessageType.None);
            }
            catch (ArgumentException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
        }
        internal static bool TryHandoff(VehicleProfileDraft draft, out string failure) => TryHandoff(draft, null, true, out failure);
        internal static bool TryHandoff(VehicleProfileDraft draft, VehicleConfiguration live, bool open, out string failure)
        {
            failure = "";
            if (draft == null || draft.physicsLabSetup == null) { failure = "Assign a Physics Lab setup in Overview."; return false; }
            if (draft.runtimeDefinition == null) { failure = "Assign a Vehicle Definition first."; return false; }
            if (!draft.runtimeDefinition.Validate(out failure)) return false;
            if (live != null && live.Definition != draft.runtimeDefinition) { failure = "The live vehicle belongs to a different definition."; return false; }
            var root = live != null ? live.gameObject : draft.vehiclePrefab;
            var perf = root != null ? root.GetComponent<VehiclePerformanceSystem>() : null;
            var custom = root != null ? root.GetComponent<VehicleCustomizationSystem>() : null;
            var configuration = root != null ? root.GetComponent<VehicleConfiguration>() : null;
            var setup = draft.physicsLabSetup;
            Undo.RecordObject(setup, "Send vehicle configuration to Physics Lab");
            setup.definition = draft.runtimeDefinition; setup.tuning = draft.runtimeDefinition.factoryTuning;
            setup.upgrades = perf?.Build.Installed.OfType<VehiclePerformanceUpgradeDefinition>().ToArray() ?? Array.Empty<VehiclePerformanceUpgradeDefinition>();
            setup.bodyParts = custom?.EffectiveBuild.Installed.OfType<VehicleCustomizationDefinition>().ToArray() ?? Array.Empty<VehicleCustomizationDefinition>();
            setup.tuningAdjustments = configuration?.Adjustments ?? Array.Empty<VehicleTuningAdjustment>();
            EditorUtility.SetDirty(setup); AssetDatabase.SaveAssetIfDirty(setup);
            if (draft.physicsLabExperiment != null)
            {
                Undo.RecordObject(draft.physicsLabExperiment, "Bind Physics Lab vehicle"); draft.physicsLabExperiment.vehicle = setup;
                EditorUtility.SetDirty(draft.physicsLabExperiment); AssetDatabase.SaveAssetIfDirty(draft.physicsLabExperiment);
                if (open) VehiclePhysicsLabWindow.Open().SelectDefinition(draft.physicsLabExperiment);
            }
            return true;
        }
        private static void Field(SerializedObject obj, string field) { var p = obj.FindProperty(field); if (p != null) EditorGUILayout.PropertyField(p, true); }
        private static void CreateDefinition(VehicleProfileDraft draft)
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Vehicle Definition", draft.name + "Definition", "asset", "Save the canonical definition."); if (string.IsNullOrEmpty(path)) return;
            var definition = ScriptableObject.CreateInstance<VehicleDefinition>();
            var model = draft.catalog?.FindModel(draft.modelId); var brand = model != null ? draft.catalog.FindBrand(model.brandId) : null;
            definition.vehicleId = string.IsNullOrEmpty(draft.modelId) ? draft.Id : draft.modelId; definition.variantId = string.IsNullOrEmpty(draft.variantId) ? "stock" : draft.variantId;
            definition.manufacturer = brand?.displayName ?? ""; definition.model = model?.displayName ?? draft.name; definition.year = draft.modelYear;
            definition.factoryTuning = draft.tuning; definition.prefab = draft.vehiclePrefab != null ? draft.vehiclePrefab.GetComponent<VehicleController>() : null;
            definition.performanceCatalog = draft.performance; definition.customizationCatalog = draft.customization;
            AssetDatabase.CreateAsset(definition, AssetDatabase.GenerateUniqueAssetPath(path)); Undo.RecordObject(draft, "Assign Vehicle Definition"); draft.runtimeDefinition = definition; EditorUtility.SetDirty(draft); AssetDatabase.SaveAssets();
        }
    }
}
