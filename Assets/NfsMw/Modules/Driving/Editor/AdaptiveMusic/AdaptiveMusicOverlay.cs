#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    [Overlay(typeof(SceneView), "Adaptive Music")]
    public sealed class AdaptiveMusicOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { minWidth = 220 } };
            root.Add(new Label("Music authoring") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });
            root.Add(new Button(() => AdaptiveMusicEditorWindow.Open()) { text = "Open Adaptive Music Editor" });
            root.Add(new Button(() => AdaptiveMusicEditorWindow.Open().RunValidation()) { text = "Validate profiles" });
            root.Add(new Button(AdaptiveMusicDemoBuilder.Build) { text = "Build test scene" });
            root.Add(new Button(() => Selection.activeObject = FindProfile()) { text = "Select first profile" });
            return root;
        }

        private static SensoryMusicProfile FindProfile()
        {
            var profiles = AdaptiveMusicEditorModel.FindProfiles();
            return profiles.Count > 0 ? profiles[0] : null;
        }
    }
}
#endif
