using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    [Overlay(typeof(SceneView), "NFS World Validation", true)]
    public sealed class WorldValidationOverlay : Overlay
    {
        private Label status;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { minWidth = 230 } };
            root.Add(new Label("World validation"));
            status = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            root.Add(status);
            root.Add(new Button(WorldValidationWindow.Open) { text = "Open dashboard" });
            root.Add(new Button(SelectFirstBlocking) { text = "Select first blocker" });
            WorldValidationService.ReportChanged += Refresh;
            root.RegisterCallback<DetachFromPanelEvent>(_ => WorldValidationService.ReportChanged -= Refresh);
            Refresh(WorldValidationService.LastReport);
            return root;
        }

        private void Refresh(WorldValidationReport report)
        {
            if (status == null) return;
            status.text = report == null
                ? "No report."
                : report.runStatus + "\n" + report.failed + " failed · " + report.warnings + " warnings\n"
                    + report.notEvaluated + " not evaluated · partial=" + report.partial;
        }

        private static void SelectFirstBlocking()
        {
            WorldValidationResult result = (WorldValidationService.LastReport?.results ?? Array.Empty<WorldValidationResult>())
                .FirstOrDefault(value => value != null && value.IsBlocking);
            if (result != null && !WorldValidationNavigation.TrySelect(result, out string failure))
                Debug.LogWarning("World validation navigation: " + failure);
        }
    }

    [InitializeOnLoad]
    internal static class WorldValidationSceneGizmos
    {
        static WorldValidationSceneGizmos()
        {
            SceneView.duringSceneGui += Draw;
        }

        private static void Draw(SceneView sceneView)
        {
            WorldValidationReport report = WorldValidationService.LastReport;
            if (report == null || report.results == null) return;
            int drawn = 0;
            foreach (WorldValidationResult result in report.results)
            {
                if (result == null || !result.hasWorldPosition || result.status == WorldValidationStatus.Passed || drawn++ >= 256) continue;
                Color previous = Handles.color;
                Handles.color = result.IsBlocking ? new Color(1, .18f, .1f, .95f) : new Color(1, .75f, .1f, .9f);
                float size = HandleUtility.GetHandleSize(result.worldPosition) * .08f;
                Handles.SphereHandleCap(0, result.worldPosition, Quaternion.identity, size, EventType.Repaint);
                if (result.status == WorldValidationStatus.Failed || result.status == WorldValidationStatus.ErrorRunning)
                    Handles.Label(result.worldPosition + Vector3.up * size, result.diagnosticCode);
                Handles.color = previous;
            }
        }
    }
}
