using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NfsMwRemaster.Driving;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Diagnostics.Editor
{
    public sealed class DiagnosticStudioWindow : EditorWindow
    {
        private static readonly string[] Tabs={"Live", "Charts", "Timeline", "World", "Providers", "Capture", "Replay", "Validation", "Setup"};
        [SerializeField] private int tab;
        [SerializeField] private string search="", selected="", pinned="";
        [SerializeField] private bool paused, includeText;
        private Vector2 scroll;
        private IDisposable subscription;
        private int epoch=-1;
        private bool geometry;
        private DiagnosticCapture replay;
        private int replayIndex;
        private string status="";
        private CancellationTokenSource cancel;
        private Task export;
        private double repaintAt;
        private bool recording;
        private readonly Dictionary<string,IDisposable> captureSubscriptions=new Dictionary<string,IDisposable>();
        [MenuItem("Tools/NFS MW Remaster/Debug Overlay Studio")]
        public static void Open()=>GetWindow<DiagnosticStudioWindow>("Debug Overlay Studio");
        private void OnEnable(){minSize=new Vector2(700,430);EditorApplication.update+=UpdateView;EditorApplication.playModeStateChanged+=PlayChanged;SceneView.duringSceneGui+=DrawScene;}
        private void OnDisable(){EditorApplication.update-=UpdateView;EditorApplication.playModeStateChanged-=PlayChanged;SceneView.duringSceneGui-=DrawScene;Release();StopRecording();cancel?.Cancel();cancel?.Dispose();cancel=null;}
        private void PlayChanged(PlayModeStateChange state){Release();StopRecording();selected="";}
        private void StopRecording(){recording=false;foreach(var lease in captureSubscriptions.Values)lease.Dispose();captureSubscriptions.Clear();}
        private void Release(){subscription?.Dispose();subscription=null;}
        private void UpdateView()
        {
            if(export!=null && export.IsCompleted){status=export.IsCanceled?"Export cancelled":export.IsFaulted?"Export failed: "+export.Exception.GetBaseException().GetType().Name:"Capture exported";export=null;cancel?.Dispose();cancel=null;}
            if(epoch!=DiagnosticSession.Epoch){epoch=DiagnosticSession.Epoch;Release();StopRecording();selected="";}
            if(recording && EditorApplication.isPlaying)
            {
                var removed=new List<string>();foreach(var id in captureSubscriptions.Keys)if(DiagnosticSession.Hub.Find(id)==null)removed.Add(id);
                foreach(var id in removed){captureSubscriptions[id].Dispose();captureSubscriptions.Remove(id);}
                foreach(var c in DiagnosticSession.Hub.Channels)if(!captureSubscriptions.ContainsKey(c.Provider.Id))captureSubscriptions.Add(c.Provider.Id,DiagnosticSession.Hub.Subscribe(c.Provider.Id));
            }
            bool wants=EditorApplication.isPlaying && !paused && tab<=3;
            bool wantsGeometry=tab==3;
            if(!wants){Release();}else if(DiagnosticSession.Hub.Find(selected)!=null && (subscription==null || geometry!=wantsGeometry))
            {Release();geometry=wantsGeometry;subscription=DiagnosticSession.Hub.Subscribe(selected,geometry);}
            if(EditorApplication.timeSinceStartup>=repaintAt){repaintAt=EditorApplication.timeSinceStartup+0.1;Repaint();if(tab==3)SceneView.RepaintAll();}
        }
        private void OnGUI()
        {
            GUILayout.Label("DEBUG OVERLAY STUDIO",EditorStyles.boldLabel);
            GUILayout.Label("Authoritative owner samples · bounded traces · development only",EditorStyles.miniLabel);
            if (GUILayout.Button("Vehicle audio trace · selected vehicle"))
            {
                var vehicle = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponentInParent<VehicleAudio>();
                NfsMwRemaster.Driving.Editor.AudioAnalysis.BlackBoxInspectorWindow.Open(null, vehicle == null ? null : vehicle.Profile, vehicle);
            }
            tab=GUILayout.Toolbar(tab,Tabs);GUILayout.Space(8);
            using(new EditorGUILayout.HorizontalScope())
            {
                search=EditorGUILayout.TextField("Search",search);paused=GUILayout.Toggle(paused,"Freeze subscriptions",GUILayout.Width(145));
                if(GUILayout.Button("Save layout",GUILayout.Width(90)))EditorPrefs.SetString("NFS.Diagnostics.Layout",JsonUtility.ToJson(new Layout{tab=tab,search=search,pinned=pinned}));
                if(GUILayout.Button("Load layout",GUILayout.Width(90))){var l=JsonUtility.FromJson<Layout>(EditorPrefs.GetString("NFS.Diagnostics.Layout","{}"));tab=Mathf.Clamp(l.tab,0,8);search=l.search??"";pinned=l.pinned??"";}
            }
            scroll=EditorGUILayout.BeginScrollView(scroll);
            if(tab<=4)DrawProviders();
            var channel=DiagnosticSession.Hub.Find(selected);
            switch(tab)
            {
                case 0:DrawSnapshot(channel?.Current,channel);break;
                case 1:DrawChart(channel);break;
                case 2:DrawTimeline();break;
                case 3:EditorGUILayout.HelpBox("Selected provider geometry appears in Scene View. Capped at 128 lines and 300 m; yellow pursuit markers are last-known knowledge. No geometry means the owner has not supplied it.",MessageType.Info);DrawSnapshot(channel?.Current,channel);break;
                case 4:DrawContracts(channel);break;
                case 5:DrawCapture();break;
                case 6:DrawReplay();break;
                case 7:DrawValidation();break;
                case 8:DrawSetup();break;
            }
            if(status.Length>0)EditorGUILayout.HelpBox(status,MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        private void DrawProviders()
        {
            if(!EditorApplication.isPlaying)EditorGUILayout.HelpBox("Enter Play Mode with a Diagnostic Overlay and explicit source bindings. No gameplay is simulated by this window.",MessageType.Info);
            foreach(var c in DiagnosticSession.Hub.Channels)
            {
                if((c.Provider.Category+" "+c.Provider.Label+" "+c.Provider.Id).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                using(new EditorGUILayout.HorizontalScope())
                {if(GUILayout.Toggle(selected==c.Provider.Id,c.Provider.Category+" / "+c.Provider.Label,"Button") && selected!=c.Provider.Id){Release();selected=c.Provider.Id;}
                    GUILayout.Label(c.Consumers+" subscribers",GUILayout.Width(110));}
            }
        }
        private void DrawSnapshot(DiagnosticSnapshot s,DiagnosticChannel channel=null)
        {
            if(channel!=null){EditorGUILayout.LabelField("Provider health",channel.Error.Length==0?"Ready":channel.Error);EditorGUILayout.LabelField("Sampling cost (last)",channel.SampleMilliseconds.ToString("F3")+" ms");}
            if(s==null){EditorGUILayout.HelpBox("Waiting for an owner sample. Unknown is not zero.",MessageType.Info);return;}
            EditorGUILayout.LabelField("Entity / generation",s.EntityId+" / "+s.Generation);EditorGUILayout.LabelField("Revision",s.Revision);
            double age=Time.realtimeSinceStartupAsDouble-s.Clock.realtime;
            EditorGUILayout.LabelField("Sample phase / age",s.Clock.phase+" / "+age.ToString("F2")+" s"+(age>2?" (STALE)":""));
            EditorGUILayout.LabelField("Clock mapping",$"real {s.Clock.realtime:F3} / game {s.Clock.game:F3} / DSP {s.Clock.dsp:F3} / owner tick {s.Clock.tick}");
            for(int i=0;i<s.Count;i++)
            {var m=s[i];using(new EditorGUILayout.HorizontalScope()){if(GUILayout.Button(pinned==m.id?"★":"☆",GUILayout.Width(26)))pinned=m.id;EditorGUILayout.LabelField(m.id,m.ToString());}}
        }
        private void DrawChart(DiagnosticChannel channel)
        {
            EditorGUILayout.LabelField("Pinned metric",pinned.Length==0?"Choose a star in Live":pinned);
            if(channel==null || channel.History.Count<2)return;
            var values=new List<Vector2>();double min=double.PositiveInfinity,max=double.NegativeInfinity;
            for(int i=0;i<channel.History.Count;i++){var s=channel.History[i];for(int j=0;j<s.Count;j++)if(s[j].id==pinned && s[j].kind!=MetricKind.State && s[j].validity==MetricValidity.Valid)
                {double v=s[j].value;min=Math.Min(min,v);max=Math.Max(max,v);values.Add(new Vector2(i,(float)v));break;}}
            Rect rect=GUILayoutUtility.GetRect(100,220,GUILayout.ExpandWidth(true));EditorGUI.DrawRect(rect,new Color(0.08f,0.1f,0.12f));
            if(values.Count<2)return;
            Handles.BeginGUI();Handles.color=Color.cyan;
            for(int i=1;i<values.Count;i++){if(values[i].x-values[i-1].x>1)continue;Handles.DrawLine(Point(values[i-1]),Point(values[i]));}Handles.EndGUI();
            EditorGUILayout.LabelField($"Observed min {min:G5} / max {max:G5} • {channel.History.Count} samples / overwritten {channel.History.Overwritten}");
            Vector3 Point(Vector2 v)=>new Vector3(rect.x+v.x/(channel.History.Count-1)*rect.width,rect.yMax-(float)((v.y-min)/Math.Max(0.000001,max-min))*rect.height);
        }
        private void DrawTimeline()
        {
            var hub=DiagnosticSession.Hub;EditorGUILayout.LabelField($"Events {hub.Events.Count} / overwritten {hub.Events.Overwritten} / rate rejected {hub.RejectedEvents}");
            for(int i=hub.Events.Count-1;i>=Math.Max(0,hub.Events.Count-150);i--){var e=hub.Events[i];EditorGUILayout.LabelField($"{e.clock.realtime:F3} [{e.severity}] {e.provider}",e.code);}
        }
        private void DrawContracts(DiagnosticChannel c)
        {
            EditorGUILayout.HelpBox("Provider IDs are stable binding IDs plus runtime instance identity. Entity generations isolate pooling. Histories hold 128 samples, snapshots 64 metrics and 128 lines. No command is callable through the provider interface.",MessageType.Info);
            if(c!=null){EditorGUILayout.LabelField("Provider ID",c.Provider.Id);EditorGUILayout.LabelField("Minimum interval",c.Provider.Interval+" s");}
        }
        private void DrawCapture()
        {
            using(new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {if(GUILayout.Button(recording?"Stop multi-provider recording":"Record all registered providers (bounded rolling histories)")){if(recording)StopRecording();else recording=true;}}
            EditorGUILayout.LabelField("Recording subscriptions",captureSubscriptions.Count.ToString());
            EditorGUILayout.HelpBox("Export a frozen copy of retained histories and events. Default excludes state/event text and aliases entity/provider identities. No save data, network upload or scene paths are collected. Review optional text before sharing.",MessageType.Info);
            includeText=EditorGUILayout.Toggle("Include redacted state/event text",includeText);
            using(new EditorGUI.DisabledScope(export!=null))if(GUILayout.Button("Export bounded JSON capture…"))
            {
                string path=EditorUtility.SaveFilePanel("Export diagnostics","","diagnostics-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".json","json");
                if(path.Length>0)try{var capture=DiagnosticCapture.Create(DiagnosticSession.Hub,Application.unityVersion,includeText);cancel=new CancellationTokenSource();export=DiagnosticCapture.ExportAsync(path,JsonUtility.ToJson(capture,true),cancel.Token);status="Exporting staged capture…";}
                catch(Exception){cancel?.Dispose();cancel=null;status="Capture could not be exported: size budget or path validation failed. No file published.";}
            }
            if(export!=null && GUILayout.Button("Cancel export"))cancel.Cancel();
        }
        private void DrawReplay()
        {
            EditorGUILayout.HelpBox("RECORDED DATA • scrubbing does not rewind or drive the simulation.",MessageType.Info);
            if(GUILayout.Button("Open capture…")){string path=EditorUtility.OpenFilePanel("Open diagnostics","","json");if(path.Length>0)try{if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException();replay=DiagnosticCapture.Parse(File.ReadAllText(path));replayIndex=0;status="Capture loaded";}catch(Exception){status="Capture rejected: invalid schema, contents, size or I/O.";}}
            if(replay==null)return;
            EditorGUILayout.LabelField($"Schema {replay.schema} / {replay.records.Length} records / truncated {replay.truncated} / lost {replay.overwritten+replay.rejected}");
            if(replay.records.Length>0){replayIndex=EditorGUILayout.IntSlider("Record",replayIndex,0,replay.records.Length-1);var r=replay.records[replayIndex];
                EditorGUILayout.LabelField(r.provider+" / "+r.entity+" / real "+r.clock.realtime.ToString("F3"));foreach(var m in r.metrics)EditorGUILayout.LabelField(m.id,m.ToString());}
        }
        private void DrawValidation()
        {
            EditorGUILayout.HelpBox("Configuration checks are read-only and do not require a running overlay. These checks can be called by a future World Validation Dashboard adapter.",MessageType.Info);
            if(GUILayout.Button("Validate loaded scene bindings"))status=DiagnosticSetup.ValidateScenes();
            EditorGUILayout.LabelField("Shipping", "Runtime entry and command execution disabled outside Editor / Development Build.");
            EditorGUILayout.LabelField("Commands", "No gameplay mutation commands installed. Sandbox API is separately gated.");
        }
        private void DrawSetup()
        {
            EditorGUILayout.HelpBox("Select an owner component in the Inspector, then attach a read-only source binding. Supported: VehicleController, TrafficWorldDirector, VehiclePoliceUnit, VehiclePursuitDirector, MissionHost, SensoryAudioWorld, SensoryEffectsWorld. Traffic slot -1 summarizes the world; a slot >= 0 follows that trip generation.",MessageType.Info);
            if(GUILayout.Button("Add overlay to active scene (Undo)"))DiagnosticSetup.AddOverlay();
            if(GUILayout.Button("Bind supported owners on selected GameObjects (Undo)"))status=DiagnosticSetup.BindSelection();
            if(GUILayout.Button("Create synthetic diagnostic sample…"))DiagnosticSetup.CreateSample();
            EditorGUILayout.HelpBox("F10 or controller View + Start opens the runtime overlay. Escape/B closes; Tab/D-pad changes provider. Gameplay input is captured while open, with closing-frame suppression. Editor-only viewing does not capture game input.",MessageType.None);
        }
        private void DrawScene(SceneView view)
        {
            if(tab!=3 || paused || !EditorApplication.isPlaying)return;var s=DiagnosticSession.Hub.Find(selected)?.Current;if(s==null || Time.realtimeSinceStartupAsDouble-s.Clock.realtime>2)return;
            using(new Handles.DrawingScope(Matrix4x4.identity))for(int i=0;i<s.LineCount;i++){var l=s.Line(i);if(Vector3.Distance(view.camera.transform.position,l.from)>300)continue;Handles.color=l.color;Handles.DrawLine(l.from,l.to,2);}
        }
        [Serializable] private sealed class Layout {public int tab;public string search,pinned;}
    }
}
