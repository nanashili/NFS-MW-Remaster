using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Diagnostics
{
    [DefaultExecutionOrder(20000), DisallowMultipleComponent]
    public sealed class DiagnosticOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private bool drawWorld;
        [SerializeField,Range(50,2000)] private float drawDistance=300;
        private IDisposable subscription, profilerLease;
        private DiagnosticProfiler profiler;
        private string selected="", search="";
        private Vector2 scroll;
        private int epoch=-1;
        private readonly List<DiagnosticChannel> choices=new List<DiagnosticChannel>();
        private bool focused;
        public bool Visible=>visible;
        public int LinesDrawn {get;private set;}
        private void OnEnable(){if(!DiagnosticBuild.Enabled){enabled=false;return;}Bind();SetVisible(visible);}
        private void Bind()
        {
            subscription?.Dispose();subscription=null;profilerLease?.Dispose();profiler?.Dispose();
            epoch=DiagnosticSession.Epoch;
            if(DiagnosticSession.Hub.Find("unity.performance")==null){profiler=new DiagnosticProfiler();profilerLease=DiagnosticSession.Hub.Register(profiler);}
            selected="";
        }
        public void SetVisible(bool value)
        {
            visible=DiagnosticBuild.Enabled&&value;
            if(visible){MapInputFocus.Acquire(this);focused=true;}
            else {MapInputFocus.Release(this);focused=false;subscription?.Dispose();subscription=null;selected="";}
        }
        private void Update()
        {
            if(epoch!=DiagnosticSession.Epoch)Bind();
            var keyboard=Keyboard.current;var pad=Gamepad.current;
            if(keyboard?.f10Key.wasPressedThisFrame==true || (pad?.selectButton.isPressed==true && pad.startButton.wasPressedThisFrame))SetVisible(!visible);
            if(!visible)return;
            if(keyboard?.escapeKey.wasPressedThisFrame==true || pad?.buttonEast.wasPressedThisFrame==true){SetVisible(false);return;}
            choices.Clear();foreach(var c in DiagnosticSession.Hub.Channels)if(Matches(c))choices.Add(c);
            if(choices.Count>0 && (string.IsNullOrEmpty(selected)||DiagnosticSession.Hub.Find(selected)==null))Select(choices[0].Provider.Id);
            if(choices.Count>0 && (keyboard?.tabKey.wasPressedThisFrame==true || pad?.dpad.down.wasPressedThisFrame==true || pad?.dpad.up.wasPressedThisFrame==true))
            {
                int index=choices.FindIndex(c=>c.Provider.Id==selected);int direction=pad?.dpad.up.wasPressedThisFrame==true?-1:1;
                Select(choices[(index+direction+choices.Count)%choices.Count].Provider.Id);
            }
        }
        private bool Matches(DiagnosticChannel c)=>(c.Provider.Category+" "+c.Provider.Label).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0;
        private void Select(string id){subscription?.Dispose();selected=id;subscription=DiagnosticSession.Hub.Subscribe(id,drawWorld);}
        private void LateUpdate(){DiagnosticSession.Hub.Tick(DiagnosticClock.Now(SamplePhase.LateUpdate));}
        private void OnGUI()
        {
            if(!DiagnosticBuild.Enabled || !visible)return;
            GUILayout.BeginArea(new Rect(12,12,Mathf.Min(650,Screen.width-24),Mathf.Max(150,Screen.height-24)),GUI.skin.box);
            GUILayout.Label("DIAGNOSTICS • live / F10 close • Tab / D-pad select");
            search=GUILayout.TextField(search,96);
            scroll=GUILayout.BeginScrollView(scroll);
            foreach(var c in choices)if(GUILayout.Button(c.Provider.Category+" / "+c.Provider.Label))Select(c.Provider.Id);
            bool world=GUILayout.Toggle(drawWorld,"World geometry (selected provider)");if(world!=drawWorld){drawWorld=world;if(selected.Length>0)Select(selected);}
            var channel=DiagnosticSession.Hub.Find(selected);var snapshot=channel?.Current;
            if(channel!=null)GUILayout.Label(channel.Error+"  | sample "+channel.SampleMilliseconds.ToString("F3")+" ms");
            if(snapshot!=null)
            {
                GUILayout.Label($"Generation {snapshot.Generation} / {snapshot.Clock.phase} / age {Time.realtimeSinceStartupAsDouble-snapshot.Clock.realtime:F2}s");
                for(int i=0;i<snapshot.Count;i++)GUILayout.Label(snapshot[i].id+": "+snapshot[i]);
            }
            GUILayout.Label($"Events {DiagnosticSession.Hub.Events.Count} / overwritten {DiagnosticSession.Hub.Events.Overwritten} / rejected {DiagnosticSession.Hub.RejectedEvents}");
            GUILayout.EndScrollView();GUILayout.EndArea();DrawLines(snapshot);
        }
        private void DrawLines(DiagnosticSnapshot s)
        {
            LinesDrawn=0;if(!drawWorld || s==null || Time.realtimeSinceStartupAsDouble-s.Clock.realtime>2 || Event.current.type!=EventType.Repaint)return;
            Camera camera=Camera.main;if(camera==null)return;
            for(int i=0;i<s.LineCount && LinesDrawn<128;i++)
            {
                var line=s.Line(i);if(Vector3.Distance(camera.transform.position,line.from)>drawDistance)continue;
                var a=camera.WorldToScreenPoint(line.from);var b=camera.WorldToScreenPoint(line.to);if(a.z<=0 || b.z<=0)continue;
                a.y=Screen.height-a.y;b.y=Screen.height-b.y;Vector2 delta=b-a;
                var matrix=GUI.matrix;var color=GUI.color;
                GUI.color=line.color;GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,a);
                GUI.DrawTexture(new Rect(a.x,a.y,delta.magnitude,2),Texture2D.whiteTexture);GUI.matrix=matrix;GUI.color=color;LinesDrawn++;
            }
        }
        private void OnDisable(){if(focused)MapInputFocus.Release(this);focused=false;subscription?.Dispose();subscription=null;profilerLease?.Dispose();profilerLease=null;profiler?.Dispose();profiler=null;}
    }
}
