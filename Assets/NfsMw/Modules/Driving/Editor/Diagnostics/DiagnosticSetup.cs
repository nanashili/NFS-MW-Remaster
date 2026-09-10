using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
namespace NfsMwRemaster.Diagnostics.Editor
{
    public static class DiagnosticSetup
    {
        public static void AddOverlay()
        {
            var go=new GameObject("Development Diagnostics");Undo.RegisterCreatedObjectUndo(go,"Add diagnostics overlay");Undo.AddComponent<DiagnosticOverlay>(go);Selection.activeGameObject=go;
        }
        public static string BindSelection()
        {
            int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Bind diagnostic owners");int count=0;
            foreach(var go in Selection.gameObjects)
            {
                if(EditorUtility.IsPersistent(go))continue;
                if(go.GetComponent<DiagnosticSource>()!=null)continue;
                MonoBehaviour owner=null;
                foreach(var component in go.GetComponents<MonoBehaviour>())
                    if(Supported(component)){owner=component;break;}
                if(owner==null)continue;
                var binding=Undo.AddComponent<DiagnosticSource>(go);Undo.RecordObject(binding,"Configure diagnostics binding");binding.Configure(owner,Guid.NewGuid().ToString("N"));
                PrefabUtility.RecordPrefabInstancePropertyModifications(binding);EditorSceneManager.MarkSceneDirty(go.scene);count++;
            }
            Undo.CollapseUndoOperations(group);return count+" read-only owner bindings added. Select a binding in the Inspector to choose another supported owner.";
        }
        public static bool Supported(MonoBehaviour source)=>source is VehicleController || source is TrafficWorldDirector || source is VehiclePoliceUnit || source is VehiclePursuitDirector || source is MissionHost || source is SensoryAudioWorld || source is SensoryEffectsWorld || source is RacingLineInput || source is VehicleBountySystem || source is VehicleStoreWallet || source is RoadNetwork || source is RockportWorldStreamer;
        public static string ValidateScenes()
        {
            int count=0,invalid=0;var ids=new HashSet<string>();var issues=new List<string>();
            foreach(var b in UnityEngine.Object.FindObjectsByType<DiagnosticSource>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                count++;var serialized=new SerializedObject(b);string id=serialized.FindProperty("providerId").stringValue;
                string problem=b.Source==null?"Missing owner":!Supported(b.Source)?"Unsupported owner":string.IsNullOrWhiteSpace(id)||id.Length>64?"Author an identity of 1–64 characters":!ids.Add(id)?"Duplicate authored identity; use Fresh identity in Inspector":"";
                if(problem.Length>0){invalid++;issues.Add(b.gameObject.name+": "+problem);}
            }
            return $"{count} bindings inspected; {invalid} configuration issues.\n"+string.Join("\n",issues);
        }
        [MenuItem("Tools/NFS MW Remaster/Diagnostics/Create synthetic sample")]
        public static void CreateSample()
        {
            string path=EditorUtility.SaveFilePanelInProject("Save synthetic diagnostic scene","DiagnosticSample","unity","Choose a new scene path.");
            if(path.Length==0)return;if(System.IO.File.Exists(path)){Debug.LogError("Choose a new path; existing scenes are preserved.");return;}
            try{BuildSample(path);}catch(InvalidOperationException error){EditorUtility.DisplayDialog("Cannot create diagnostic sample",error.Message,"OK");}
        }
        public static void BuildSample(string path)
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path))throw new InvalidOperationException("Save the current untitled scene before creating an additive diagnostic sample. Existing work has not been changed.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root=new GameObject("SYNTHETIC diagnostics fixture");SceneManager.MoveGameObjectToScene(root,scene);root.AddComponent<DiagnosticFixture>();root.AddComponent<DiagnosticOverlay>();
                var cameraObject=new GameObject("Sample camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);cameraObject.tag="MainCamera";var camera=cameraObject.AddComponent<Camera>();camera.transform.position=new Vector3(0,10,-15);camera.transform.LookAt(Vector3.forward*4);
                NfsMwRemaster.Driving.Editor.Rendering.HdrpSceneDefaults.Camera(camera);
                if(!EditorSceneManager.SaveScene(scene,path))throw new InvalidOperationException("Scene save failed.");
            }
            catch{EditorSceneManager.CloseScene(scene,true);throw;}
            finally{if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
        }
    }
    [Overlay(typeof(SceneView),"NFS Diagnostics")]
    public sealed class DiagnosticSceneOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {var root=new VisualElement();root.Add(new Label("Read-only development diagnostics"));root.Add(new Button(DiagnosticStudioWindow.Open){text="Open Debug Overlay Studio"});return root;}
    }
}
