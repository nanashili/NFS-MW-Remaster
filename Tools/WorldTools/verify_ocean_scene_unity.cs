using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlaying||scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity"||scene.isDirty)throw new InvalidOperationException("Expected saved RockportMap in edit mode.");
        var roots=scene.GetRootGameObjects();if(roots.Length!=7)throw new InvalidOperationException("Unexpected root count "+roots.Length);
        RockportOcean ocean=null;int trees=0;
        foreach(var root in roots){if(root.name.StartsWith("TEMP"))throw new InvalidOperationException("Temporary fixture retained");if(root.TryGetComponent<RockportOcean>(out var candidate))ocean=candidate;trees+=root.GetComponentsInChildren<DestructibleProp>(true).Length;}
        if(!ocean||!ocean.isActiveAndEnabled||ocean.TriangleCount!=608||trees!=481)throw new InvalidOperationException("Missing ocean or physical tree contents");
        var water=ocean.Surface;var mesh=water.meshRenderers[0].GetComponent<MeshFilter>().sharedMesh;
        if(!water.scriptInteractions||water.geometryType!=WaterGeometryType.Custom||water.surfaceType!=WaterSurfaceType.OceanSeaLake||mesh.vertexCount!=112199||mesh.triangles.Length!=666432)throw new InvalidOperationException("Ocean mesh or simulation settings changed");
        if(!ocean.GetComponent<Volume>().sharedProfile.TryGet<WaterRendering>(out var rendering)||!rendering.enable.value||!ocean.GetComponent<BoxCollider>().isTrigger)throw new InvalidOperationException("Water rendering or trigger disabled");
        string json="{\"status\":\"PASS\",\"scene\":\""+scene.path+"\",\"saved\":true,\"editMode\":true,\"sceneRoots\":7,\"temporaryFixtures\":0,\"physicalTrees\":481,\"sourceOceanTriangles\":608,\"renderVertices\":112199,\"renderTriangles\":222144,\"hdrpScriptInteractions\":true}";
        File.WriteAllText("Art/RockportOcean/Source/ocean-saved-scene-verification.json",json);result.Log(json);
    }
}
