using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NfsMwRemaster.Driving.Editor;

namespace NfsMwRemaster.Driving.Editor.Workspace
{
    internal sealed class VehicleFrameworkWorkspaceView : RacingModuleView, IRacingViewState
    {
        private VehicleProfileDraft draft;
        private SerializedObject serialized;
        private IMGUIContainer imgui;
        private readonly VehicleFrameworkPanel panel = new VehicleFrameworkPanel();
        public override void SetContext(RacingEditingContext context)
        {
            var selected = context != null ? context.ResolveDocument() as VehicleProfileDraft : null;
            if (selected == null && context != null) selected = context.UnitySelection as VehicleProfileDraft;
            if (selected == null) selected = draft;
            if (selected == draft && serialized != null) { Repaint(); return; }
            serialized?.Dispose(); serialized = selected != null ? new SerializedObject(selected) : null; draft = selected;
            if (imgui == null)
            {
                imgui = new IMGUIContainer(Draw) { style = { flexGrow = 1 } };
                rootVisualElement.Add(imgui);
            }
            Repaint();
        }
        private void Draw()
        {
            if (draft == null || serialized == null) { EditorGUILayout.HelpBox("Select a Vehicle Profile Draft in the workspace.", MessageType.Info); return; }
            serialized.Update(); panel.Draw(draft, serialized); if (serialized.ApplyModifiedProperties()) Repaint();
        }
        public string CaptureViewState()
        {
            return draft != null ? GlobalObjectId.GetGlobalObjectIdSlow(draft).ToString() : "";
        }
        public void RestoreViewState(string state)
        {
            if (string.IsNullOrWhiteSpace(state) || !GlobalObjectId.TryParse(state, out var id)) return;
            var restored = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as VehicleProfileDraft;
            if (restored == null || restored == draft) return;
            serialized?.Dispose(); draft = restored; serialized = new SerializedObject(restored); Repaint();
            if (imgui == null) { imgui = new IMGUIContainer(Draw); rootVisualElement.Add(imgui); }
        }
        public override void Dispose() { panel.Close(); serialized?.Dispose(); serialized = null; }
    }
}
