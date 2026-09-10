#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static partial class DrivingDemoBuilder
    {
        internal const string FreeRoamScenePath = RockportStreamingMigration.EntryScenePath;

        [MenuItem("NFS MW Remaster/Build Free Roam Scene")]
        public static void BuildFreeRoamScene() => RockportStreamingMigration.BuildOrRefresh();

        [MenuItem("NFS MW Remaster/Apply Rockport World Build to Current Free Roam")]
        public static void ApplyRockportWorldBuildToCurrentFreeRoam() => RockportStreamingMigration.BuildOrRefresh();

        [MenuItem("NFS MW Remaster/Build Rockport Streaming Scenes")]
        public static void BuildRockportStreamingScenes() => RockportStreamingMigration.BuildOrRefresh();

        private static Material FreeRoamMaterial(string name, Color color) =>
            GetOrCreateMaterial("FreeRoam" + name + ".mat", color, 0.05f, 0.65f);
    }
}
#endif
