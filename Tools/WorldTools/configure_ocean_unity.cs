using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new IOException("Open RockportMap");
        foreach(var root in scene.GetRootGameObjects())if(root.name=="Rockport Ocean - Original Coastline")throw new IOException("Ocean already exists; inspect before replacing");
        const string folder="Assets/NfsMw/Content/World/Maps/Rockport/Ocean/";AssetDatabase.Refresh();
        var mesh=new UnityEngine.Mesh{name="Original Rockport ocean - 32m wave grid",indexFormat=IndexFormat.UInt32};
        using(var reader=new BinaryReader(File.OpenRead(folder+"OceanSurface.bytes")))
        {
            int vertexCount=reader.ReadInt32(),indexCount=reader.ReadInt32();var vertices=new Vector3[vertexCount];var normals=new Vector3[vertexCount];var uv=new Vector2[vertexCount];var indices=new int[indexCount];
            for(int i=0;i<vertexCount;i++){vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());normals[i]=Vector3.up;uv[i]=new Vector2(vertices[i].x,vertices[i].z)/500;}
            for(int i=0;i<indexCount;i++)indices[i]=reader.ReadInt32();mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateBounds();
        }
        AssetDatabase.CreateAsset(mesh,folder+"OceanSurface.asset");
        int profiles=0;
        foreach(string guid in AssetDatabase.FindAssets("t:HDRenderPipelineAsset",new[]{"Assets/NfsMw/Settings/Rendering/HDRP"}))
        {
            var asset=AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));var serialized=new SerializedObject(asset);var settings=serialized.FindProperty("m_RenderPipelineSettings");
            settings.FindPropertyRelative("supportWater").boolValue=true;
            settings.FindPropertyRelative("waterScriptInteractionsMode").intValue=(int)WaterScriptInteractionsMode.CPUSimulation;
            settings.FindPropertyRelative("waterSimulationResolution").intValue=(int)WaterSimulationResolution.Medium128;
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);profiles++;
        }
        var global=AssetDatabase.LoadMainAssetAtPath("Assets/NfsMw/Settings/Rendering/HDRPDefaultResources/HDRenderPipelineGlobalSettings.asset");
        var globalSerialized=new SerializedObject(global);var list=globalSerialized.FindProperty("m_Settings.m_SettingsList.m_List");int frameSets=0;
        for(int i=0;i<list.arraySize;i++)
        {
            var entry=list.GetArrayElementAtIndex(i);if(!entry.managedReferenceFullTypename.Contains("RenderingPathFrameSettings"))continue;
            foreach(string field in new[]{"m_Camera","m_CustomOrBakedReflection","m_RealtimeReflection"})
            {
                var bits=entry.FindPropertyRelative(field+".bitDatas.data2");bits.ulongValue|=1UL<<((int)FrameSettingsField.Water-64);frameSets++;
            }
        }
        if(frameSets!=3||profiles!=5)throw new IOException("Unexpected HDRP quality/frame settings "+profiles+" / "+frameSets);
        globalSerialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(global);
        var oceanObject=new GameObject("Rockport Ocean - Original Coastline");result.RegisterObjectCreation(oceanObject);
        var water=oceanObject.AddComponent<WaterSurface>();water.surfaceType=WaterSurfaceType.OceanSeaLake;water.geometryType=WaterGeometryType.Custom;
        water.scriptInteractions=true;water.cpuEvaluateRipples=true;water.largeWindSpeed=24;water.largeOrientationValue=35;water.largeChaos=.55f;
        water.repetitionSize=500;water.largeBand0Multiplier=.55f;water.largeBand1Multiplier=.45f;
        water.largeCurrentSpeedValue=.72f;water.ripples=true;water.ripplesWindSpeed=8;
        water.foam=true;water.simulationFoamAmount=.4f;water.foamPersistenceMultiplier=.35f;
        water.refractionColor=new Color(.12f,.30f,.34f);water.scatteringColor=new Color(.025f,.10f,.12f);water.absorptionDistance=18;
        water.startSmoothness=.96f;water.endSmoothness=.90f;water.maxTessellationFactor=3;
        water.underWater=true;water.volumeDepth=250;
        var shape=new GameObject("Original ocean surface mesh");shape.transform.SetParent(oceanObject.transform,false);
        shape.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=shape.AddComponent<MeshRenderer>();renderer.enabled=false;water.meshRenderers.Add(renderer);
        var ocean=oceanObject.AddComponent<RockportOcean>();ocean.Configure(AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"OceanFootprint.bytes"));
        var trigger=oceanObject.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.center=mesh.bounds.center+Vector3.down*245;trigger.size=new Vector3(mesh.bounds.size.x,510,mesh.bounds.size.z);water.volumeBounds=trigger;
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="Rockport Ocean Rendering";profile.Add<WaterRendering>(true).enable.Override(true);
        AssetDatabase.CreateAsset(profile,folder+"OceanVolume.asset");foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);
        var volume=oceanObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=50;volume.sharedProfile=profile;
        int buoyantTrees=0;foreach(var root in scene.GetRootGameObjects())if(root.name=="Rockport Breakable Trees")foreach(var body in root.GetComponentsInChildren<OceanBuoyantBody>(true)){body.SetOcean(ocean);EditorUtility.SetDirty(body);buoyantTrees++;}
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("Art/RockportOcean/Source");string json="{\"status\":\"PASS\",\"sourceFootprintTriangles\":"+ocean.TriangleCount+",\"renderTriangles\":"+mesh.triangles.Length/3+",\"meanSeaLevel\":0,\"hdrpProfilesEnabled\":"+profiles+",\"frameSettingsEnabled\":"+frameSets+",\"buoyantTrees\":"+buoyantTrees+",\"physics\":\"HDRP CPU spectral waves, source footprint containment, eight-point displaced-volume buoyancy and quadratic drag, dynamic rigidbody trigger enrollment\"}";
        File.WriteAllText("Art/RockportOcean/Source/ocean-unity-setup.json",json);result.Log(json);
    }
}
