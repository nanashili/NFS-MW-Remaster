using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class CityTerrainPreview : IDisposable
    {
        private TerrainData copy;
        private GameObject root;
        private Terrain original;
        private bool hidden;
        public double CutCubicMetres { get; private set; }
        public double FillCubicMetres { get; private set; }
        public CityTerrainPreview(CityDistrict d,Terrain terrain,Func<float,bool> cancel=null)
        {
            if(terrain==null||terrain.terrainData==null) throw new ArgumentException("Choose a Unity Terrain. Mesh-terrain grading requires an upstream adapter.");
            original=terrain; var data=terrain.terrainData;
            if(data.heightmapResolution>1025) throw new ArgumentException("Preview budget is 1025 height samples per axis; use a smaller terrain tile.");
            try
            {
                copy=UnityEngine.Object.Instantiate(data); copy.hideFlags=HideFlags.HideAndDontSave;
                int n=data.heightmapResolution; var heights=data.GetHeights(0,0,n,n);
                var roads=CityGeometry.Polygons(CityGeometry.RoadFootprints(d));
                float cellArea=data.size.x*data.size.z/((n-1)*(n-1));
                for(int z=0;z<n;z++)
                {
                    if(cancel!=null && cancel((float)z/n)) throw new OperationCanceledException();
                    for(int x=0;x<n;x++)
                    {
                        var world=terrain.transform.position+new Vector3((float)x/(n-1)*data.size.x,0,(float)z/(n-1)*data.size.z);
                        var local=d.ToLocal(world);
                        if(roads.Any(r=>CityGeometry.Contains(r,local)) || d.reservations.Any(r=>CityGeometry.Contains(r.polygon,local))) continue;
                        var parcel=d.parcels.FirstOrDefault(p=>CityGeometry.Contains(p.polygon,local)); if(parcel==null) continue;
                        float target=(d.transform.position.y+parcel.padHeight-terrain.transform.position.y)/data.size.y;
                        if(target<0||target>1) throw new ArgumentException("Parcel pad height leaves the terrain's vertical range.");
                        float delta=(target-heights[z,x])*data.size.y; if(delta>0) FillCubicMetres+=delta*cellArea; else CutCubicMetres-=delta*cellArea;
                        heights[z,x]=target;
                    }
                }
                copy.SetHeights(0,0,heights); root=Terrain.CreateTerrainGameObject(copy); root.name="CITY TERRAIN PREVIEW — not committed";
                root.hideFlags=HideFlags.HideAndDontSave; root.transform.position=terrain.transform.position;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,terrain.gameObject.scene);
                root.GetComponent<Terrain>().materialTemplate=terrain.materialTemplate;
                UnityEngine.Object.DestroyImmediate(root.GetComponent<TerrainCollider>());
                hidden=!SceneVisibilityManager.instance.IsHidden(terrain.gameObject); if(hidden) SceneVisibilityManager.instance.Hide(terrain.gameObject,true);
                AssemblyReloadEvents.beforeAssemblyReload+=Dispose; EditorApplication.playModeStateChanged+=Play;
            }
            catch { Dispose(); throw; }
        }
        private void Play(PlayModeStateChange state) { if(state==PlayModeStateChange.ExitingEditMode) Dispose(); }
        public void Dispose()
        {
            AssemblyReloadEvents.beforeAssemblyReload-=Dispose; EditorApplication.playModeStateChanged-=Play;
            if(hidden && original!=null) SceneVisibilityManager.instance.Show(original.gameObject,true); hidden=false;
            if(root!=null) UnityEngine.Object.DestroyImmediate(root); if(copy!=null) UnityEngine.Object.DestroyImmediate(copy);
            root=null;copy=null;
        }
    }
}
