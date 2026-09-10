using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
namespace NfsMwRemaster.Lighting.Editor
{
    [CustomEditor(typeof(AtmosphereZone))]
    public sealed class AtmosphereZoneInspector : UnityEditor.Editor
    {
        readonly BoxBoundsHandle handle=new BoxBoundsHandle();
        public override void OnInspectorGUI(){DrawDefaultInspector();EditorGUILayout.HelpBox("Transform scales the box. Blend distances are world metres; categories compose independently. Higher priority then ordinal ID wins.",MessageType.Info);}
        void OnSceneGUI()
        {
            var zone=(AtmosphereZone)target;
            using(new Handles.DrawingScope(new Color(.2f,.8f,1),zone.transform.localToWorldMatrix))
            {handle.center=Vector3.zero;handle.size=zone.size;EditorGUI.BeginChangeCheck();handle.DrawHandle();if(EditorGUI.EndChangeCheck())LightingCommands.Edit(zone,"Resize atmosphere zone",()=>zone.size=Vector3.Max(handle.size,Vector3.one*.01f));}
            Handles.Label(zone.transform.position,zone.name+" / priority "+zone.priority);
        }
    }
    [CustomEditor(typeof(AtmosphereController))]
    public sealed class AtmosphereControllerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI(){DrawDefaultInspector();if(GUILayout.Button("Open Lighting Studio"))LightingStudioWindow.Open();}
        void OnSceneGUI()
        {
            var owner=(AtmosphereController)target;foreach(var binding in owner.probes)if(binding&&binding.probe){Handles.color=Color.cyan;Handles.DrawWireCube(binding.probe.transform.position+binding.probe.center,binding.probe.size);}
            foreach(var binding in owner.fixtures)if(binding&&binding.source){Handles.color=binding.Critical?Color.yellow:new Color(1,.5f,.1f,.3f);Handles.DrawWireDisc(binding.source.transform.position,Vector3.up,binding.source.range);}
        }
    }
    [Overlay(typeof(SceneView),"NFS Lighting Inspector")]
    public sealed class LightingDebugOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root=new VisualElement();var label=new Label("Open Lighting Studio and select an owner."){style={maxWidth=340,whiteSpace=WhiteSpace.Normal}};root.Add(label);
            root.schedule.Execute(()=>
            {
                var window=LightingStudioWindow.Active;if(!window||!window.Owner){label.text="Open Lighting Studio and select an owner.";return;}
                var owner=window.Owner;var contributors=new System.Collections.Generic.List<string>();
                try{var look=Application.isPlaying?owner.Effective:AtmosphereResolver.Resolve(owner.fallback,owner.zones,window.InspectionPoint,contributors);label.text=$"{owner.name} · {owner.quality}\nExposure {look.exposure:F2} EV · Fog {look.fogDensity:F4}\n{owner.fixtures.Length} fixtures / {owner.probes.Length} probes\n"+string.Join("\n",Application.isPlaying?owner.Contributors:contributors);}
                catch(System.Exception ex){label.text=ex.Message;}
            }).Every(250);return root;
        }
    }
}
