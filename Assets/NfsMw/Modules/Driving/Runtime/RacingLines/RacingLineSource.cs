using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Racing Lines/Studio Document", fileName = "RacingLine")]
    public sealed class RacingLineSource : ScriptableObject
    {
        public const int CurrentSchema = 2;
        [HideInInspector] public int schema = CurrentSchema;
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        public RacingLineRoute route;
        public RacingVehicleSetup vehicle;
        public RacingCapabilityProfile capability;
        public RacingPlannerSettings planner = new RacingPlannerSettings();
        public RacingVerificationSettings verification = new RacingVerificationSettings();
        public RacingLineHint[] hints = Array.Empty<RacingLineHint>();
        public RacingLineExclusion[] exclusions = Array.Empty<RacingLineExclusion>();
        [Tooltip("Last verified publication. Regeneration never overwrites this asset.")]
        public RacingLineArtifact published;
        [TextArea] public string referenceNotes;
#if UNITY_EDITOR
        public RacingReferenceCapture[] referenceCaptures = Array.Empty<RacingReferenceCapture>();
#endif
    }
#if UNITY_EDITOR
    [Serializable]
    public sealed class RacingReferenceCapture
    {
        public Texture2D image;
        public string sourceAndPermission, gameBuild, carAndSetup;
        [Min(0)] public float captureSeconds, routeStation;
        [Tooltip("-1 means not visible/unknown. Never infer precise input or tire force from a screenshot.")]
        public float observedSpeedMps = -1;
        [Min(0)] public float speedUncertaintyMps;
        public bool stationIsInferred = true;
        [TextArea] public string observation;
    }
#endif
}
