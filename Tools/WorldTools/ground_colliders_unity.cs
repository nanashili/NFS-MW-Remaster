using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.Linq;
internal class CommandScript : IRunCommand {
    public void Execute(ExecutionResult result) {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new InvalidOperationException("Unexpected scene.");
        int created=0,total=0,expected=0;
        foreach(var root in scene.GetRootGameObjects()) {
            if(root.name!="Rockport Roads - Paved and Unpaved Routes"&&root.name!="Rockport Original Ground - Exact Source Meshes")continue;
            var meshes=root.GetComponentsInChildren<MeshFilter>(true);expected+=meshes.Length;
            foreach(var filter in meshes){var collider=filter.GetComponent<MeshCollider>();
                // Per-component Undo snapshots duplicate the enormous prefab
                // hierarchy. This command edits the separately saved map scene.
                if(!collider&&created<4000){collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;collider.convex=false;created++;}
                if(collider){if(collider.sharedMesh!=filter.sharedMesh||collider.convex||!collider.enabled)throw new InvalidOperationException("Source collision mesh mismatch.");total++;}
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);result.Log("Added "+created+" static ground MeshColliders; "+total+" / "+expected+" source surfaces now have collision.");
    }
}
