using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor.Workspace
{
    public static class RacingWorkspacePreferences
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Racing Tools",SettingsScope.User)
            {
                label="Racing Tools",
                activateHandler=(_,root)=>
                {
                    var title=new TextField("Workspace Title"){value=EditorPrefs.GetString("RacingTools.Title","Racing Tools"),maxLength=64};
                    title.RegisterValueChangedCallback(e=>
                    {
                        string value=string.IsNullOrWhiteSpace(e.newValue)?"Racing Tools":e.newValue.Trim();
                        EditorPrefs.SetString("RacingTools.Title",value);
                        foreach(var window in Resources.FindObjectsOfTypeAll<RacingWorkspaceWindow>())window.titleContent=new GUIContent(value);
                    });
                    root.Add(title);
                    root.Add(new HelpBox("These are user workspace preferences. They do not modify game content, runtime settings or save data.",HelpBoxMessageType.Info));
                }
            };
        }
    }
}
