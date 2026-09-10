#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    [Overlay(typeof(SceneView), "Audio Zones")]
    public sealed class AudioZoneOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { minWidth = 190 } };
            root.Add(new Label("Acoustic authoring") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });
            root.Add(new Button(() => AudioZoneEditorWindow.Open()) { text = "Open Audio Zone Editor" });
            root.Add(new Button(() => Create(AudioZoneShape.Box)) { text = "Create box zone" });
            root.Add(new Button(() => Create(AudioZoneShape.Sphere)) { text = "Create sphere zone" });
            root.Add(new Button(() => Create(AudioZoneShape.Capsule)) { text = "Create capsule zone" });
            root.Add(new Button(() => AudioZoneEditorWindow.Open().RunValidation()) { text = "Validate loaded zones" });
            root.Add(new Button(FrameSelected) { text = "Frame selected" });
            return root;
        }

        private static void Create(AudioZoneShape shape)
        {
            var view = SceneView.lastActiveSceneView;
            Vector3 position = view != null ? view.camera.transform.position + view.camera.transform.forward * 12 : Vector3.zero;
            AudioZoneEditorModel.CreateZone(shape, null, position);
            SceneView.RepaintAll();
        }

        private static void FrameSelected()
        {
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        }
    }
}
#endif
