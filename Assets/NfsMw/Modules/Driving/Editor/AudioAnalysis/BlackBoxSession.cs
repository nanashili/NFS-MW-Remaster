using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    [Serializable]
    public sealed class BlackBoxSource
    {
        public string root = "", relativePath = "", hash = "", declaredContext = "";
        public string Id => hash;
    }

    [Serializable]
    public sealed class BlackBoxRegionReview
    {
        public string id = Guid.NewGuid().ToString("N"), sourceHash = "", recordingId = "";
        public string label = "Authored region", evidence = "", assumptions = "";
        public int startFrame, endFrame;
        public float startRpm = 1000, endRpm = 1000, minimumLoad, maximumLoad = 1, gain = 0.5f;
        public bool reviewed;
        public EngineRpmAnchor[] rpmAnchors = Array.Empty<EngineRpmAnchor>();
        // User approval deliberately cannot change this into recovered historical metadata.
        public string provenance = "Authored override";
        public string lookupPolicy = "Linear within explicitly authored endpoints; ambiguous candidates stop";
    }

    /// <summary>Mutable authoring overlays. Runtime profiles never reference this Editor asset.</summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Vehicle Profiles/Black Box Analysis")]
    public sealed class BlackBoxSession : ScriptableObject
    {
        public const int CurrentSchema = 1;
        public int schema = CurrentSchema;
        public string id = Guid.NewGuid().ToString("N");
        public VehicleProfileDraft vehicle;
        public VehicleSensoryProfile profile;
        public string targetSceneObjectId = "", targetScenePath = "";
        public string completePackFolder = "", gameInstallationFolder = "";
        public List<BlackBoxSource> sources = new List<BlackBoxSource>();
        public List<BlackBoxRegionReview> regions = new List<BlackBoxRegionReview>();
        public string outputFolder = "", generatedProfilePath = "", generatedProfileHash = "", generatedFingerprint = "";
        public string generatedManifestHash = "";
        public string referenceCaptureContext = "";
        public int revision;
        public void ValidateSchema()
        {
            if (schema != CurrentSchema || !Guid.TryParseExact(id, "N", out _) || sources == null || regions == null)
                throw new InvalidOperationException("Unknown or invalid analysis schema; preserve the asset and use a compatible reader.");
        }
    }
}
