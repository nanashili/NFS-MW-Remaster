#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class CityValidationSmoke
    {
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run this validation in a separate Unity batch process.");
            RockportStreamingMigration.Validate();
        }

        public static void RenderSavedScene() => Run();
    }
}
#endif
