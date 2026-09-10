using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Scene-view entry point for progression authoring. Career definitions do
    /// not have world-space gizmo semantics, so the overlay deliberately
    /// provides navigation only and does not draw fake world markers.
    /// </summary>
    [Overlay(typeof(SceneView), "Career Graph")]
    public sealed class CareerGraphVisualizerOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { minWidth = 220f } };
            root.Add(new Label("Career progression")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4f }
            });
            root.Add(new Button(() => CareerGraphVisualizerWindow.Open())
            {
                text = "Open career workbench"
            });
            root.Add(new Button(CareerGraphVisualizerWindow.SelectActiveDefinition)
            {
                text = "Use selected career asset"
            });
            return root;
        }
    }
}
