using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
namespace NfsMwRemaster.Lighting.Editor
{
    public sealed class LightingStudioWindow : EditorWindow
    {
        [SerializeField] AtmosphereController owner;[SerializeField] AtmosphereProfile source;
        [SerializeField] LightingCameraSet cameraSet;[SerializeField] int poseIndex;
        [SerializeField] int tab;[SerializeField] Vector3 cameraPosition=new Vector3(0,3,-25),cameraEuler=new Vector3(5,0,0);
        [SerializeField] string review="Road edges: pending\nTraffic silhouettes: pending\nSignals / brake lights: pending\nTunnel exit: pending\nVehicle paint / reflections: pending\nArtist approval: pending";
        AtmosphereProfile draft;string revision,status="Select a controller and profile to begin.";Vector2 scroll;int resolution=128;float fov=60,meanError;
        LightingBakeJob job;LightingBakeSet staged;Texture2D baseline,candidate,difference;LightingCaptureMetadata baselineMeta,candidateMeta;
        string circuit="default";float circuitIntensity=3,circuitRange=15;LightShadows circuitShadows=LightShadows.None;
        UnityEditor.Editor draftEditor;bool showDifference;
        public static LightingStudioWindow Active {get;private set;}
        public AtmosphereController Owner=>owner;
        public Vector3 InspectionPoint=>cameraPosition;
        [MenuItem("Tools/NFS MW/Lighting/Lighting & Atmosphere Studio")]
        public static void Open()=>GetWindow<LightingStudioWindow>("Lighting Studio").Show();
        void OnEnable(){Active=this;minSize=new Vector2(740,640);EditorApplication.update+=UpdateJob;AssemblyReloadEvents.beforeAssemblyReload+=Cleanup;EditorApplication.playModeStateChanged+=Play;EditorSceneManager.sceneClosing+=SceneClosing;Undo.undoRedoPerformed+=UndoChanged;}
        void OnDisable(){Cleanup();EditorApplication.update-=UpdateJob;AssemblyReloadEvents.beforeAssemblyReload-=Cleanup;EditorApplication.playModeStateChanged-=Play;EditorSceneManager.sceneClosing-=SceneClosing;Undo.undoRedoPerformed-=UndoChanged;if(Active==this)Active=null;}
        void Play(PlayModeStateChange state)=>Cleanup();void SceneClosing(Scene scene,bool removing)=>Cleanup();void UndoChanged(){CancelJob();Repaint();}
        void Cleanup(){CancelJob();if(draftEditor)DestroyImmediate(draftEditor);if(draft)DestroyImmediate(draft);draft=null;ClearImages();}
        void CancelJob(){job?.Dispose();job=null;EditorUtility.ClearProgressBar();}
        void ClearImages(){foreach(var image in new[]{baseline,candidate,difference})if(image)DestroyImmediate(image);baseline=candidate=difference=null;baselineMeta=candidateMeta=null;}
        public void CreateGUI(){rootVisualElement.Add(new Label("LIGHTING & ATMOSPHERE"){style={fontSize=21,marginLeft=12,marginTop=10}});rootVisualElement.Add(new Label("District looks • isolated audition • staged reflections"){style={marginLeft=12}});rootVisualElement.Add(new IMGUIContainer(Draw){style={flexGrow=1}});}
        void Run(Action action){try{action();}catch(Exception ex){status=ex.Message;Debug.LogWarning("Lighting Studio: "+ex.Message);}Repaint();}
        void Button(string text,Action action){if(GUILayout.Button(text))Run(action);}
        void Draw()
        {
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();var next=(AtmosphereController)EditorGUILayout.ObjectField("Scene owner",owner,typeof(AtmosphereController),true);if(EditorGUI.EndChangeCheck()){Cleanup();owner=next;source=owner?owner.fallback:null;}
                using(new EditorGUILayout.HorizontalScope()){Button("Create owner",()=>owner=LightingCommands.CreateController());Button("Use selected",()=>{Cleanup();owner=Selection.activeGameObject?Selection.activeGameObject.GetComponent<AtmosphereController>():null;source=owner?owner.fallback:null;});Button("Create synthetic demonstration",LightingDemo.Create);}
                tab=GUILayout.SelectionGrid(tab,new[]{"Profile Library","District Volumes","Lights","Probes","Atmosphere","Exposure","Bake","Comparison","Validation"},3);
                scroll=EditorGUILayout.BeginScrollView(scroll);
                if(tab==0)Library();else if(!owner)EditorGUILayout.HelpBox("Choose a scene owner.",MessageType.Info);
                else switch(tab){case 1:Zones();break;case 2:Lights();break;case 3:Probes();break;case 4:Look(false);break;case 5:Look(true);break;case 6:Bake();break;case 7:Comparison();break;case 8:Validation();break;}
                EditorGUILayout.EndScrollView();EditorGUILayout.HelpBox(status,MessageType.None);
            }
        }
        void Library()
        {
            EditorGUILayout.HelpBox(LightingAudit.Capabilities(),MessageType.Info);
            EditorGUI.BeginChangeCheck();var next=(AtmosphereProfile)EditorGUILayout.ObjectField("Source profile",source,typeof(AtmosphereProfile),false);if(EditorGUI.EndChangeCheck()){Cleanup();source=next;}
            using(new EditorGUILayout.HorizontalScope())
            {
                Button("New profile",()=>{string path=EditorUtility.SaveFilePanelInProject("Create atmosphere profile","Atmosphere","asset","Choose source asset location");if(path.Length>0){Cleanup();source=LightingCommands.CreateProfile(path);}});
                Button("Duplicate with new ID",()=>{if(!source)throw new InvalidOperationException("Select a source profile.");string path=EditorUtility.SaveFilePanelInProject("Duplicate atmosphere profile",source.name+" Copy","asset","New identity");if(path.Length>0){var original=source;Cleanup();source=LightingCommands.CreateProfile(path,original);}});
                Button("Audition draft",LoadDraft);
            }
            if(source){EditorGUILayout.LabelField("Stable ID",source.id);EditorGUILayout.LabelField("Source revision",source.Revision);EditorGUILayout.LabelField("Intent",source.intent,EditorStyles.wordWrappedLabel);}
            if(owner&&source)Button("Assign profile to this scene owner",()=>LightingCommands.Edit(owner,"Assign atmosphere fallback",()=>owner.fallback=source));
            if(draft)
            {EditorGUILayout.HelpBox("Draft is temporary. Applying modifies only: "+AssetDatabase.GetAssetPath(source)+". Existing consumers of that shared asset will see the approved values.",MessageType.Warning);DrawDraft(new[]{"intent","referenceNotes","semanticVariant","lightingSeconds","fogSeconds","exposureSeconds","reflectionSeconds","bakedScenarioId","allowRealtimeFallback","lowFixtureIntensity","lowDecorativeShadows","realtimeBudget","shadowBudget"});Button("Apply draft to named source asset",()=>{LightingCommands.ApplyDraft(source,draft,revision);revision=source.Revision;status="Source updated. Undo restores its previous values.";});}
        }
        void LoadDraft(){if(!source)throw new InvalidOperationException("Select a source profile.");Cleanup();draft=Instantiate(source);draft.name=source.name;draft.hideFlags=HideFlags.HideAndDontSave;revision=source.Revision;status="Editing an isolated draft.";}
        void DrawDraft(string[] fields){var so=new SerializedObject(draft);so.Update();foreach(string field in fields)EditorGUILayout.PropertyField(so.FindProperty(field),true);so.ApplyModifiedProperties();}
        void Look(bool exposure)
        {
            if(!draft){EditorGUILayout.HelpBox("Choose a source in Profile Library and audition a draft.",MessageType.Info);Button("Load draft",LoadDraft);return;}
            var so=new SerializedObject(draft);so.Update();var look=so.FindProperty("look");foreach(string field in exposure?new[]{"exposureEV100","automaticExposure","exposure","contrast","saturation","filter","bloom","vignette","tonemapping"}:new[]{"skyModel","sky","equator","ground","keyColor","keyIntensity","keyEuler","fog","fogMode","fogColor","fogDensity","fogStart","fogEnd","reflectionIntensity"})EditorGUILayout.PropertyField(look.FindPropertyRelative(field));so.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(exposure?"EV100 controls physical camera exposure. Automatic exposure adapts to scene luminance during gameplay; comparison captures use fixed EV100 for repeatability. Compensation offsets either mode.":"HDRP height fog uses extinction distance derived from density. Volumetric lighting is available in High and Ultra. Sky and fog change rendering; existing surface physics retain their own settings.",MessageType.Info);
            CameraFields();Button("Render draft candidate",()=>Capture(false));if(candidate)GUILayout.Label(candidate,GUILayout.Height(220));
        }
        void CameraFields(){cameraPosition=EditorGUILayout.Vector3Field("Inspection / capture position",cameraPosition);cameraEuler=EditorGUILayout.Vector3Field("Camera rotation",cameraEuler);fov=EditorGUILayout.Slider("Field of view",fov,20,100);using(new EditorGUILayout.HorizontalScope()){Button("Use Scene camera",()=>{var cam=SceneView.lastActiveSceneView?.camera;if(!cam)throw new InvalidOperationException("Open a Scene view.");cameraPosition=cam.transform.position;cameraEuler=cam.transform.eulerAngles;fov=cam.fieldOfView;});Button("Use world camera",()=>{if(!owner.worldCamera)throw new InvalidOperationException("Assign a world camera.");cameraPosition=owner.worldCamera.transform.position;cameraEuler=owner.worldCamera.transform.eulerAngles;fov=owner.worldCamera.fieldOfView;});}}
        void Zones()
        {
            Button("Add district / interior volume",()=>LightingCommands.AddZone(owner,cameraPosition));
            foreach(var zone in owner.zones)if(zone){using(new EditorGUILayout.HorizontalScope()){EditorGUILayout.ObjectField(zone,typeof(AtmosphereZone),true);Button("Inspect",()=>Selection.activeGameObject=zone.gameObject);EditorGUILayout.LabelField(zone.Weights(cameraPosition).ToString());}}
            CameraFields();var list=new List<string>();RunResolve(list);foreach(string item in list)EditorGUILayout.LabelField(item,EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox("Priority ascending, then stable ID ordinal; higher priority composes last. Four weights: lighting / fog / exposure / reflections. Scene handles edit selected zone bounds with Undo. Cell IDs are metadata; external streaming must register/unregister explicit zone references.",MessageType.Info);
        }
        void RunResolve(List<string> list){try{var value=AtmosphereResolver.Resolve(owner.fallback,owner.zones,cameraPosition,list);EditorGUILayout.LabelField($"Effective exposure {value.exposure:F2} EV / fog {value.fogDensity:F4} / key {value.keyIntensity:F2}");}catch(Exception ex){EditorGUILayout.HelpBox(ex.Message,MessageType.Error);}}
        void Lights()
        {
            Button("Register selected hierarchy's existing lights",()=>LightingCommands.RegisterSelection(owner,false,true,false));
            var so=new SerializedObject(owner);so.Update();EditorGUILayout.PropertyField(so.FindProperty("keyLight"));EditorGUILayout.PropertyField(so.FindProperty("quality"));EditorGUILayout.PropertyField(so.FindProperty("fixtures"),true);so.ApplyModifiedProperties();
            foreach(var f in owner.fixtures)if(f){using(new EditorGUILayout.HorizontalScope()){EditorGUILayout.LabelField($"{f.name} · {f.role} · {f.circuit}"+(f.Critical?" · protected":""));Button("Inspect",()=>Selection.activeGameObject=f.gameObject);}}
            EditorGUILayout.LabelField("Circuit batch edit",EditorStyles.boldLabel);circuit=EditorGUILayout.TextField("Exact circuit",circuit);circuitIntensity=EditorGUILayout.FloatField("Intensity (each light’s physical unit)",circuitIntensity);circuitRange=EditorGUILayout.FloatField("Range (m)",circuitRange);circuitShadows=(LightShadows)EditorGUILayout.EnumPopup("Shadows",circuitShadows);
            EditorGUILayout.HelpBox("Apply changes only registered, non-critical lights in this exact circuit. Unclassified fixtures are protected until given an explicit role. This changes scene Light components and records prefab overrides.",MessageType.Info);
            Button("Apply circuit light settings",()=>status="Updated "+LightingCommands.SetCircuit(owner,circuit,circuitIntensity,circuitRange,circuitShadows)+" lights; Undo restores prior values.");
            EditorGUILayout.HelpBox("Bindings reference existing fixtures. Configure role and upstream ID in the Inspector before applying quality policies. Critical fixtures and emissive visuals are never edited by the runtime quality filter. Bake type, range, color and shadow settings remain standard Light Inspector properties with Unity Undo.",MessageType.Info);
        }
        void Probes()
        {
            Button("Register selected reflection probes",()=>LightingCommands.RegisterSelection(owner,false,false,true));
            var so=new SerializedObject(owner);so.Update();EditorGUILayout.PropertyField(so.FindProperty("probes"),true);so.ApplyModifiedProperties();
            foreach(var p in owner.probes)if(p&&p.probe){EditorGUILayout.ObjectField(p.probe,typeof(ReflectionProbe),true);EditorGUILayout.LabelField($"{p.id} · cell {p.cellId} · {p.probe.mode} · {p.probe.resolution}px");}
            EditorGUILayout.HelpBox("Reflection boxes and selected influence spheres are drawn in Scene view. Legacy indirect probes remain owned by Unity's Lighting window; reflection capture does not replace an indirect-light bake. APV is disabled in the current quality assets.",MessageType.Info);
            Button("Open Unity Lighting window",()=>EditorApplication.ExecuteMenuItem("Window/Rendering/Lighting"));
        }
        void Bake()
        {
            Button("Register selected static mesh hierarchy",()=>LightingCommands.RegisterSelection(owner,true,false,false));
            var so=new SerializedObject(owner);so.Update();EditorGUILayout.PropertyField(so.FindProperty("bakeGeometry"),true);so.ApplyModifiedProperties();
            EditorGUILayout.LabelField("Current fingerprint",LightingAudit.Fingerprint(owner));EditorGUILayout.ObjectField("Approved",owner.approvedBake,typeof(LightingBakeSet),false);
            resolution=EditorGUILayout.IntPopup("Cubemap face resolution",resolution,new[]{"64","128","256"},new[]{64,128,256});
            EditorGUILayout.HelpBox($"Scope: {owner.bakeGeometry.Length} explicit static mesh references, {owner.probes.Length} registered probes, {owner.probes.Length*6} face renders. Captures realtime illumination into custom HDR cubemaps with mipmaps. No engine GI/lightmap bake or APV scenario is claimed. Review glossy filtering and seams on actual car materials.",MessageType.Info);
            using(new EditorGUI.DisabledScope(job!=null))Button("Stage reflection captures",()=>{job=new LightingBakeJob(owner,resolution);status="Capturing one face per editor update.";});
            if(job!=null){EditorGUILayout.LabelField(job.Status);Button("Cancel and release stage",CancelJob);if(job.Finished)Button("Save complete stage asset",()=>{string path=EditorUtility.SaveFilePanelInProject("Save reflection stage","Lighting Reflections","asset","Previous bake remains intact");if(path.Length>0){staged=job.SaveStage(path);CancelJob();}});}
            staged=(LightingBakeSet)EditorGUILayout.ObjectField("Candidate stage",staged,typeof(LightingBakeSet),false);
            if(staged){EditorGUILayout.HelpBox("Approve modifies this controller's approvedBake reference and custom textures/mode on its registered probes. Old stage assets are retained. Undo restores references.",MessageType.Warning);Button("Approve complete current stage",()=>LightingCommands.ApplyBake(owner,staged));}
        }
        void UpdateJob(){if(job==null||job.Finished)return;try{job.Tick();status=job.Status;}catch(Exception ex){status=ex.Message;CancelJob();}Repaint();}
        void Capture(bool isBaseline)
        {
            var profile=isBaseline?source:draft;if(!owner||!profile||!profile.IsValid)throw new InvalidOperationException("Assign owner and valid source/draft.");
            var timer=System.Diagnostics.Stopwatch.StartNew();Texture2D image;using(var preview=new LightingPreview(owner)){image=preview.Capture(profile.look,cameraPosition,Quaternion.Euler(cameraEuler),960,540,fov,owner.quality,profile.lowFixtureIntensity,profile.lowDecorativeShadows);}timer.Stop();
            var meta=LightingComparison.Metadata(owner,profile,cameraPosition,Quaternion.Euler(cameraEuler),960,540,fov,timer.Elapsed.TotalMilliseconds,"Isolated static geometry. Single look at fixed camera, not a runtime zone blend.");
            if(isBaseline){if(baseline)DestroyImmediate(baseline);baseline=image;baselineMeta=meta;}else{if(candidate)DestroyImmediate(candidate);candidate=image;candidateMeta=meta;}
            if(difference)DestroyImmediate(difference);if(baseline&&candidate)difference=LightingComparison.Difference(baseline,candidate,out meanError);
            status=$"Capture {timer.Elapsed.TotalMilliseconds:F1} ms including readback. No production scene mutated.";
        }
        void Comparison()
        {
            cameraSet=(LightingCameraSet)EditorGUILayout.ObjectField("Fixed camera set",cameraSet,typeof(LightingCameraSet),false);
            Button("Create camera review set",()=>{string path=EditorUtility.SaveFilePanelInProject("Create camera set","Lighting Cameras","asset","Exact camera poses");if(path.Length>0){cameraSet=CreateInstance<LightingCameraSet>();AssetDatabase.CreateAsset(cameraSet,path);Undo.RegisterCreatedObjectUndo(cameraSet,"Create camera set");}});
            if(cameraSet)
            {
                Button("Append current pose",()=>LightingCommands.Edit(cameraSet,"Append lighting camera",()=>cameraSet.poses=cameraSet.poses.Concat(new[]{new LightingCameraPose{name="Pose "+cameraSet.poses.Length,position=cameraPosition,euler=cameraEuler,fieldOfView=fov}}).ToArray()));
                if(cameraSet.poses.Length>0){poseIndex=EditorGUILayout.Popup("Review pose",Mathf.Clamp(poseIndex,0,cameraSet.poses.Length-1),cameraSet.poses.Select(p=>p==null?"Missing":p.name).ToArray());Button("Use review pose",()=>{var pose=cameraSet.poses[poseIndex];if(pose==null)throw new InvalidOperationException("Missing camera pose.");cameraPosition=pose.position;cameraEuler=pose.euler;fov=pose.fieldOfView;});}
                Button("Capture effective zones along camera set",()=>{string path=EditorUtility.OpenFolderPanel("Archive zone review sequence",Application.dataPath,"");if(path.Length>0)LightingSequence.Capture(owner,cameraSet,path);});
            }
            CameraFields();using(new EditorGUILayout.HorizontalScope()){Button("Capture source baseline",()=>Capture(true));Button("Capture draft candidate",()=>Capture(false));}
            showDifference=EditorGUILayout.Toggle("Show absolute pixel difference",showDifference);
            if(showDifference&&difference){GUILayout.Label(difference,GUILayout.Height(260));EditorGUILayout.LabelField($"Mean RGB difference {meanError:F5}; not an art-quality score.");}
            else using(new EditorGUILayout.HorizontalScope()){if(baseline)GUILayout.Label(baseline,GUILayout.MaxWidth(position.width*.46f),GUILayout.Height(220));if(candidate)GUILayout.Label(candidate,GUILayout.MaxWidth(position.width*.46f),GUILayout.Height(220));}
            if(baselineMeta!=null&&candidateMeta!=null&&(baselineMeta.cameraPosition!=candidateMeta.cameraPosition||baselineMeta.cameraEuler!=candidateMeta.cameraEuler||baselineMeta.fov!=candidateMeta.fov||baselineMeta.sourceFingerprint!=candidateMeta.sourceFingerprint))EditorGUILayout.HelpBox("Capture geometry, quality or camera differs. Compare metadata before interpreting pixels.",MessageType.Warning);
            review=EditorGUILayout.TextArea(review,GUILayout.MinHeight(120));Button("Archive images, metadata and review",()=>{string path=EditorUtility.OpenFolderPanel("Archive lighting comparison",Application.dataPath,"");if(path.Length>0)LightingComparison.Archive(path,baseline,candidate,difference,baselineMeta,candidateMeta,review);});
        }
        void Validation(){EditorGUILayout.HelpBox(LightingAudit.Capabilities(),MessageType.Info);CameraFields();foreach(var issue in LightingAudit.Validate(owner,cameraPosition))EditorGUILayout.HelpBox(issue,issue.StartsWith("ERROR")?MessageType.Error:issue.StartsWith("REVIEW")?MessageType.Warning:MessageType.Info);Button("New ID on selected lighting binding",()=>{var go=Selection.activeGameObject;Component target=go?(Component)go.GetComponent<AtmosphereZone>()??(Component)go.GetComponent<LightingFixture>()??go.GetComponent<LightingProbeBinding>():null;if(!target)throw new InvalidOperationException("Select a zone, fixture or probe binding. References are never guessed.");LightingCommands.NewId(target);});}
    }
}
