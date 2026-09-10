using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using NfsMwRemaster.Driving;
internal class CommandScript : IRunCommand
{
 public void Execute(ExecutionResult result)
 {
  foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
  {
   result.Log(root.name+" children="+root.transform.childCount);
   if(root.name!="Rockport Breakable Trees")continue;
   foreach(var t in root.GetComponentsInChildren<Transform>(true))
   {
    string line=t.name;foreach(var c in t.GetComponents<Component>())line+=" "+(c?c.GetType().Name:"MISSING");result.Log(line);
   }
  }
  result.Log("Types "+typeof(OceanBuoyantBody).AssemblyQualifiedName);
 }
}
