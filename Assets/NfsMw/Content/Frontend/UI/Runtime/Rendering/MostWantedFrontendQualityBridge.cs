using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Keeps the frontend's quality transaction connected to the existing HDRP preset implementation.</summary>
    public static class MostWantedFrontendQualityBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            MostWantedFrontendPreferences.ConfigureQualityAdapter(level => HdrpQualityRuntime.SetQuality(level));
        }
    }
}
