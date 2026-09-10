using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class CityPreview : IDisposable
    {
        private GameObject root;
        private CityDistrict district;
        private CityPlan plan;
        private int next;
        private readonly List<GameObject> hidden=new List<GameObject>();
        public bool Running => root!=null && next<plan.instances.Count;
        public float Progress => plan==null?0:(float)next/Math.Max(1,plan.instances.Count);
        public CityPreview(CityDistrict source,CityPlan result)
        {
            if(!result.Valid) throw new ArgumentException("Resolve generation errors before previewing.");
            district=source; plan=result;
            root=new GameObject("CITY PREVIEW — not saved") { hideFlags=HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,source.gameObject.scene);
            root.transform.position=source.transform.position;
            foreach(var item in CityCommands.Existing(source).Values)
                if(item.state==CityOwnership.Generated && !SceneVisibilityManager.instance.IsHidden(item.gameObject))
                { hidden.Add(item.gameObject); SceneVisibilityManager.instance.Hide(item.gameObject,true); }
            EditorApplication.update+=Tick; AssemblyReloadEvents.beforeAssemblyReload+=Dispose;
            EditorApplication.playModeStateChanged+=PlayChanged; EditorSceneManager.sceneClosing+=SceneClosing;
        }
        private void PlayChanged(PlayModeStateChange state) { if(state==PlayModeStateChange.ExitingEditMode) Dispose(); }
        private void SceneClosing(UnityEngine.SceneManagement.Scene scene,bool removing) { if(district==null||district.gameObject.scene==scene) Dispose(); }
        private void Tick()
        {
            if(district==null || root==null) { Dispose(); return; }
            try
            {
                int end=Math.Min(next+32,plan.instances.Count);
                for(;next<end;next++)
                {
                    var item=plan.instances[next];
                    var state=district.overrides.Find(o=>o.key==item.key);
                    if(state!=null && state.state!=CityOwnership.Generated) continue;
                    CityCommands.Instantiate(item,root.transform,district,true);
                }
                SceneView.RepaintAll(); if(next==plan.instances.Count) EditorApplication.update-=Tick;
            }
            catch(Exception e) { Debug.LogException(e); Dispose(); }
        }
        public void Dispose()
        {
            EditorApplication.update-=Tick; AssemblyReloadEvents.beforeAssemblyReload-=Dispose;
            EditorApplication.playModeStateChanged-=PlayChanged; EditorSceneManager.sceneClosing-=SceneClosing;
            foreach(var go in hidden) if(go!=null) SceneVisibilityManager.instance.Show(go,true);
            hidden.Clear(); if(root!=null) UnityEngine.Object.DestroyImmediate(root); root=null;
        }
    }
}
