using System;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
namespace NfsMwRemaster.Driving.Editor
{
    [EditorTool("Paint surface dressing")]
    public sealed class GrimePainterTool : EditorTool
    {
        static GrimeStroke draft; static GrimeCanvas owner; static string layer;
        public static void Discard(){draft=null;owner=null;layer=null;SceneView.RepaintAll();}
        public override GUIContent toolbarIcon=>new GUIContent("Grime","Paint on the explicit receiver");
        public override void OnWillBeDeactivated()=>Discard();
        public override void OnToolGUI(EditorWindow window)
        {
            var w=GrimePainterWindow.Active; if(!(window is SceneView)||!w||!w.Canvas||EditorApplication.isPlayingOrWillChangePlaymode){Discard();return;}
            var e=Event.current;
            if(e.type==EventType.MouseLeaveWindow){Discard();return;}
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){Discard();ToolManager.RestorePreviousTool();e.Use();return;}
            if(e.type==EventType.KeyDown&&(e.keyCode==KeyCode.LeftBracket||e.keyCode==KeyCode.RightBracket)){w.Width*=e.keyCode==KeyCode.LeftBracket?.9f:1.1f;if(draft!=null){draft.width=w.Width;draft.rotation=w.Rotation;}e.Use();w.Repaint();}
            if(e.type==EventType.ScrollWheel&&e.shift){w.Rotation+=e.delta.y*5;if(draft!=null){draft.width=w.Width;draft.rotation=w.Rotation;}e.Use();w.Repaint();}
            if(draft!=null&&(owner!=w.Canvas||layer!=w.Layer?.id||draft.brush!=w.Brush)){Discard();}
            if(e.type==EventType.Layout)HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            bool hitSurface=false;RaycastHit hit=default; string reason="Choose an approved receiver.";
            if(w.Receiver&&GrimeGeometry.ReceiverAllowed(w.Canvas,w.Receiver,out reason))hitSurface=w.Receiver.surface.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition),out hit,100000);
            if(hitSurface)
            {
                bool slope=Vector3.Angle(hit.normal,Vector3.up)<=w.Canvas.maximumSlope;Handles.color=slope?Color.green:Color.red;Handles.DrawWireDisc(hit.point,hit.normal,w.Width/2);Handles.DrawLine(hit.point,hit.point+hit.normal*w.Width*.5f);Handles.Label(hit.point,w.Receiver.name+(slope?"":" • slope excluded"));
                if(!e.alt&&e.button==0&&(e.type==EventType.MouseDown||e.type==EventType.MouseDrag)&&slope)
                {
                    try
                    {
                        if(w.Layer==null||w.Layer.locked||!w.Layer.enabled||!w.Brush)throw new InvalidOperationException("Choose a brush and enabled unlocked layer.");
                        if(w.Erase)
                        {
                            if(e.type==EventType.MouseDown)GrimeCommands.Edit(w.Canvas,"Erase grime strokes",()=>w.Canvas.strokes.RemoveAll(s=>s.layerId==w.Layer.id&&s.samples.Any(a=>a.receiver==w.Receiver&&Vector3.Distance(a.position,hit.point)<w.Width/2)));
                        }
                        else
                        {
                            if(e.type==EventType.MouseDown){Discard();draft=w.NewStroke();owner=w.Canvas;layer=w.Layer.id;}
                            if(draft!=null&&(draft.samples.Count==0||Vector3.Distance(draft.samples[draft.samples.Count-1].position,hit.point)>.02f))
                            {
                                if(draft.samples.Count>=20000)throw new InvalidOperationException("Stroke source limit reached. Release to commit.");
                                draft.samples.Add(GrimeGeometry.Capture(w.Receiver,hit,Vector3.ProjectOnPlane(((SceneView)window).camera.transform.up,hit.normal)));
                            }
                        }
                    }
                    catch(Exception ex){w.Status(ex.Message);Discard();}
                    e.Use();
                }
            }
            else if(!string.IsNullOrEmpty(reason))w.Status(reason);
            if(e.type==EventType.MouseUp&&e.button==0&&draft!=null)
            {
                try{if(draft.samples.Count>0)GrimeCommands.Add(owner,draft);w.Status("Stroke committed. Validate / preview to inspect conforming geometry.");}catch(Exception ex){w.Status(ex.Message);}finally{Discard();}e.Use();
            }
            if(draft!=null&&draft.samples.Count>1){Handles.color=Color.yellow;Handles.DrawAAPolyLine(draft.samples.Select(a=>a.position).ToArray());}
            window.Repaint();
        }
    }
    [CustomEditor(typeof(GrimeCanvas)),CanEditMultipleObjects]
    public sealed class GrimeCanvasInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI(){if(GUILayout.Button("Open Grime Painter"))GrimePainterWindow.Open();serializedObject.Update();DrawPropertiesExcluding(serializedObject,"m_Script","id","schema","published","generatedRoot");serializedObject.ApplyModifiedProperties();}
    }
    [CustomEditor(typeof(GrimeReceiver)),CanEditMultipleObjects]
    public sealed class GrimeReceiverInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI(){serializedObject.Update();DrawPropertiesExcluding(serializedObject,"m_Script","id");serializedObject.ApplyModifiedProperties();if(GUILayout.Button("Assign fresh receiver IDs (invalidates old anchors)")){Undo.RecordObjects(targets,"Assign receiver identity");foreach(GrimeReceiver receiver in targets){receiver.id=Guid.NewGuid().ToString("N");EditorUtility.SetDirty(receiver);PrefabUtility.RecordPrefabInstancePropertyModifications(receiver);}}EditorGUILayout.HelpBox("Collider and MeshRenderer must share the same static mesh. Mixed submeshes require approval; transparent materials are always rejected.",MessageType.Info);}
    }
    [Overlay(typeof(SceneView),"Surface dressing")]
    public sealed class GrimePainterOverlay:Overlay
    {
        public override VisualElement CreatePanelContent(){var root=new VisualElement();root.Add(new Button(GrimePainterWindow.Open){text="Grime Painter"});root.Add(new Button(()=>ToolManager.SetActiveTool<GrimePainterTool>()){text="Paint approved receiver"});root.Add(new Button(()=>{GrimePainterTool.Discard();ToolManager.RestorePreviousTool();}){text="Discard / leave brush"});return root;}
    }
}
