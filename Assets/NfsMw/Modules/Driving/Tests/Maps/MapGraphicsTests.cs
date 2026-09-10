using System;
using System.Linq;
using System.Collections;
using System.IO;
using System.Reflection;
using NfsMwRemaster.Maps.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
namespace NfsMwRemaster.Maps.Tests
{
    public sealed class MapGraphicsTests
    {
        [UnityTest] public IEnumerator EveryWorkspaceViewRendersWithPublishedTiles()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires a graphics device; run without -nographics.");
            string folder="Assets/MapGraphics_"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var roads=MapDemo.Fixture();AssetDatabase.CreateAsset(roads,folder+"/Roads.asset");
            var definition=ScriptableObject.CreateInstance<MapDefinition>();definition.roads=roads;definition.levels=roads.Lanes.Select((l,i)=>new MapLaneLevel{lane=l.Id,level=i>3?i-3:0}).ToArray();
            var style=ScriptableObject.CreateInstance<MapStyle>();AssetDatabase.CreateAsset(style,folder+"/Style.asset");definition.style=style;AssetDatabase.CreateAsset(definition,folder+"/Map.asset");
            var bake=MapBaker.Bake(definition);
            var window=EditorWindow.GetWindow<MapStudioWindow>(true,"Map graphics validation",false);window.position=new Rect(50,50,1100,700);
            try
            {
                typeof(MapStudioWindow).GetField("definition",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,definition);
                for(int page=0;page<9;page++)
                {
                    typeof(MapStudioWindow).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,page);
                    window.Repaint();yield return null;yield return null;
                }
            }
            finally{window.Close();AssetDatabase.DeleteAsset(Path.GetDirectoryName(AssetDatabase.GetAssetPath(bake.publication)));AssetDatabase.DeleteAsset(folder);}
            LogAssert.NoUnexpectedReceived();
        }
    }
}
