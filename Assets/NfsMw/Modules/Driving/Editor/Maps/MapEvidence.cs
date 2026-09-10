using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NfsMwRemaster.Maps.Editor
{
    public static class MapEvidence
    {
        [Serializable] private sealed class Measurement
        {
            public string unity,device,scope,definition,revision;
            public int tiles,segments,bytes,overviewSegments,overviewBytes,cacheBytes,cacheLoads,reusedTiles;
            public double firstBakeMilliseconds,reuseBakeMilliseconds,coldCacheMilliseconds,warmCacheMilliseconds;
        }
        public static void CreateAndMeasure()
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();var definition=MapDemo.Create();timer.Stop();
            string folder=File.ReadAllText("/tmp/nfs-map-demo.txt"),output="/tmp/nfs-map-evidence";Directory.CreateDirectory(output);
            MapExport.Svg(definition.publication,definition.style,output+"/SyntheticWorldMap.svg");
            MapExport.Svg(definition.publication,definition.style,output+"/Bridge.svg",1);MapExport.Svg(definition.publication,definition.style,output+"/Tunnel.svg",-1);
            var m=new Measurement{unity=Application.unityVersion,device=SystemInfo.graphicsDeviceName,scope="Synthetic six-lane fixture in Editor. Creation includes assets, import and scene save. Cache timings are Resources JSON load/parse and warm lookup, not target-build GPU timing.",definition=folder,revision=definition.publication.Fingerprint,firstBakeMilliseconds=timer.Elapsed.TotalMilliseconds};
            foreach(var tile in definition.publication.Tiles){m.tiles++;m.segments+=tile.segments;m.bytes+=tile.bytes;}
            m.overviewSegments=definition.publication.Overview.segments;m.overviewBytes=definition.publication.Overview.bytes;
            using(var cache=new MapTileCache(definition.publication))
            {
                timer.Restart();foreach(var tile in definition.publication.Tiles)if(cache.Get(tile)==null)throw new InvalidOperationException("Sample tile could not load.");timer.Stop();m.coldCacheMilliseconds=timer.Elapsed.TotalMilliseconds;
                timer.Restart();foreach(var tile in definition.publication.Tiles)cache.Get(tile);timer.Stop();m.warmCacheMilliseconds=timer.Elapsed.TotalMilliseconds;m.cacheBytes=cache.Bytes;m.cacheLoads=cache.Loads;
            }
            var result=MapBaker.Bake(definition);m.reuseBakeMilliseconds=result.milliseconds;m.reusedTiles=result.reused;
            File.WriteAllText(output+"/measurements.json",JsonUtility.ToJson(m,true));
            // Both revisions are retained; the saved sample points to the initial immutable publication.
            File.WriteAllText(output+"/sample.txt",folder+"/MapStudio.unity");AssetDatabase.SaveAssets();
        }
        public static void VerifySaved()
        {
            string folder=File.ReadAllText("/tmp/nfs-map-demo.txt");EditorSceneManager.OpenScene(folder+"/MapStudio.unity");
            var controller=UnityEngine.Object.FindAnyObjectByType<WorldMapController>();
            if(controller==null||controller.map==null||controller.style==null||controller.roads==null||controller.knowledgeProvider==null)throw new InvalidOperationException("Sample has missing references after reload.");
            if(controller.map.RoadRevision!=controller.roads.Publication.Fingerprint)throw new InvalidOperationException("Sample road revision mismatch.");
            using(var cache=new MapTileCache(controller.map))foreach(var tile in controller.map.Tiles)if(cache.Get(tile)==null)throw new InvalidOperationException("Sample tile is missing after reload.");
            File.WriteAllText("/tmp/nfs-map-evidence/reload.txt","PASS: saved sample scene, map/style/road/policy references and every tile hash survived a fresh Unity process.");
        }
    }
}
