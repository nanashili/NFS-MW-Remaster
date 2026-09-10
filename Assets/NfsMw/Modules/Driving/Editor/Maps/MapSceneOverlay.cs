using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;
namespace NfsMwRemaster.Maps.Editor
{
    [Overlay(typeof(SceneView),"NFS Map Studio")]
    public sealed class MapSceneOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root=new VisualElement();root.Add(new Label("Semantic map authoring"));
            root.Add(new Button(MapStudioWindow.Open){text="Open Map Studio"});
            root.Add(new Button(()=>{if(Selection.activeObject is MapDefinition definition){MapStudioWindow.Open();EditorGUIUtility.PingObject(definition);}else MapStudioWindow.Open();}){text="Inspect map source"});
            return root;
        }
    }
}
