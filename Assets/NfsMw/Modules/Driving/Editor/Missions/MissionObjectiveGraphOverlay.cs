using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Scene view entry point for mission authoring. The overlay intentionally
    /// contains navigation/actions only; mission semantics remain in the graph
    /// window and MissionRuntime.
    /// </summary>
    [Overlay(typeof(SceneView), "Mission Graph")]
    public sealed class MissionObjectiveGraphOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { minWidth = 180 } };
            root.Add(new Label("Mission authoring"));
            root.Add(new Button(MissionObjectiveGraphWindow.Open) { text = "Open graph workbench" });
            root.Add(new Button(SelectActiveMission) { text = "Use selected mission asset" });
            return root;
        }

        private static void SelectActiveMission()
        {
            var asset = Selection.activeObject as MissionDefinitionAsset;
            if (asset == null) return;
            MissionObjectiveGraphWindow.SelectDefinition(asset);
        }
    }
}
