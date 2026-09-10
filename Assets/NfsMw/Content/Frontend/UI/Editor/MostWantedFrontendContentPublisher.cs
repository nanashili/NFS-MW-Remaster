using System;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class MostWantedFrontendContentPublisher
    {
        private const string ContentPath = "Assets/NfsMw/Content/Frontend/UI/Data/FrontendContent.asset";
        private const string SoundtrackPath = "Assets/NfsMw/Modules/Driving/Data/AdaptiveMusic/AdaptiveMusicSoundtrack.asset";

        [MenuItem("NFS MW Remaster/Frontend/Publish Submenu Content")]
        public static void Publish()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before publishing frontend content.");
            var soundtrack = AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>(SoundtrackPath);
            if (soundtrack == null) throw new InvalidOperationException("The published soundtrack is missing.");
            var settings = AssetDatabase.LoadAssetAtPath<GameFlowSettings>(DrivingGameFlowBuilder.SettingsPath);
            if (settings == null) throw new InvalidOperationException("The Boot game flow settings are missing.");
            var content = AssetDatabase.LoadAssetAtPath<MostWantedFrontendContent>(ContentPath);
            if (content == null)
            {
                content = ScriptableObject.CreateInstance<MostWantedFrontendContent>();
                // Opening credits transcribed from the supplied original-game reference.
                content.credits = "Need for Speed™ Most Wanted\n\nGame Team\n\n\nProgramming\n\nGary Bearchell\nAndrew Brownsword"
                    + "\n\n\nOriginal Game and Artwork\n\nElectronic Arts / EA Black Box\n\n\nNFS MW Remaster";
                AssetDatabase.CreateAsset(content, ContentPath);
            }
            content.soundtrack = soundtrack;
            content.audioMix = AssetDatabase.LoadAssetAtPath<SensoryMixProfile>(DrivingGameFlowBuilder.SensoryMixPath);
            EditorUtility.SetDirty(content);
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("frontendContent").objectReferenceValue = content;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(content);
            AssetDatabase.SaveAssetIfDirty(settings);
        }
    }
}
