using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before changing the population.");
        const string path = "Assets/NfsMw/Scenes/World/RockportMap.unity";
        const string evidence = "Art/RockportPopulation/Disabled-20260910";
        Scene scene = SceneManager.GetSceneByPath(path);
        if (scene.IsValid() && scene.isLoaded && scene.isDirty)
            throw new InvalidOperationException("RockportMap has unsaved changes.");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        Directory.CreateDirectory(evidence);
        if (!File.Exists(evidence + "/RockportMap.before.unity"))
            File.Copy(path, evidence + "/RockportMap.before.unity", false);
        int traffic = 0, police = 0, pedestrians = 0, hazards = 0, controllers = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                bool actor = false;
                if (component is RoadVehicleMotor) { traffic++; actor = true; }
                else if (component is VehiclePoliceUnit) { police++; actor = true; }
                else if (component is AmbientPedestrian) { pedestrians++; actor = true; }
                else if (component is PoliceRoadHazard) { hazards++; actor = true; }
                if (actor)
                {
                    result.RegisterObjectModification(component.gameObject);
                    component.gameObject.SetActive(false);
                }
                if (component is FreeRoamTraffic || component is TrafficWorldDirector)
                {
                    controllers++;
                    result.RegisterObjectModification(component);
                    component.enabled = false;
                }
            }
        }
        if (traffic == 0 || police == 0 || pedestrians == 0 || controllers == 0)
            throw new InvalidOperationException("Expected population inventory was not found.");
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Failed to save RockportMap.");
        if (opened) EditorSceneManager.CloseScene(scene, true);
        string report = "{\n  \"status\": \"PASS\",\n  \"trafficVehiclesInactive\": " + traffic
            + ",\n  \"policeVehiclesInactive\": " + police + ",\n  \"pedestriansInactive\": " + pedestrians
            + ",\n  \"policeHazardsInactive\": " + hazards + ",\n  \"populationControllersDisabled\": " + controllers + "\n}\n";
        File.WriteAllText(evidence + "/removal-report.json", report);
        result.Log(report);
    }
}
