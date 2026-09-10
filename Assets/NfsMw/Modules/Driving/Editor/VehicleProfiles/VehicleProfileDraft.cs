using System;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Persistent authoring document. Never referenced by generated runtime content.
    /// Physics, sensory content and store ownership remain in their existing modules.
    /// </summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Vehicle Profiles/Draft")]
    public sealed class VehicleProfileDraft : ScriptableObject
    {
        public const int CurrentSchema = 2;
        [SerializeField, HideInInspector] private int schema = CurrentSchema;
        [SerializeField, HideInInspector] private string id = Guid.NewGuid().ToString("N");
        public int Schema => schema;
        public string Id => id;
        public VehicleIdentityCatalog catalog;
        [HideInInspector] public string modelId = "", variantId = "";
        [HideInInspector] public int modelYear;
        public string localizationKey = "";
        public string[] tags = Array.Empty<string>();
        [TextArea] public string description = "", provenance = "";
        [Tooltip("Existing, assembled production prefab. Source models alone are not driveable vehicles.")]
        public GameObject vehiclePrefab;
        [Tooltip("Must be the tuning already bound on the prefab; this document does not own a copy.")]
        public VehicleTuning tuning;
        [Tooltip("Canonical runtime definition shared by gameplay, previews and the Physics Lab.")]
        public VehicleDefinition runtimeDefinition;
        [Tooltip("Canonical Physics Lab setup receiving this profile's definition and bindings.")]
        public RacingVehicleSetup physicsLabSetup;
        public VehiclePhysicsLabDefinition physicsLabExperiment;
        [Tooltip("Optional; when assigned it must already be bound to a VehicleAudio on the prefab.")]
        public VehicleSensoryProfile audio;
        [Tooltip("Editor-only analysis sources and reviewed native engine-audio overlays.")]
        public AudioAnalysis.BlackBoxSession audioAnalysis;
        public VehicleCustomizationCatalog customization;
        public VehiclePerformanceCatalog performance;
        public Sprite thumbnail;
        [Min(0)] public int price;
        public bool availableInStore;
        public VehicleStoreCatalog storeCatalog;
        public VehicleContentBudget budget = new VehicleContentBudget();
        public VehicleAssemblyDefinition assembly = new VehicleAssemblyDefinition();
        [SerializeField, HideInInspector] private AssetVehicleCarStoreProduct generatedProduct;
        [SerializeField, HideInInspector] private string generatedFingerprint = "";
        [SerializeField, HideInInspector] private string generatedOutputHash = "";
        public AssetVehicleCarStoreProduct GeneratedProduct => generatedProduct;
        public string GeneratedFingerprint => generatedFingerprint;
        public string GeneratedOutputHash => generatedOutputHash;

        private void OnValidate()
        {
            MigrateLegacySchema();
        }

        internal void MigrateLegacySchema()
        { if (schema == 1) schema = CurrentSchema; }

        public string DisplayLabel
        {
            get
            {
                var model = catalog != null ? catalog.FindModel(modelId) : null;
                var variant = model?.variants.Find(v => v != null && v.Id == variantId);
                var brand = model != null ? catalog.FindBrand(model.brandId) : null;
                return model == null || variant == null ? name + " (incomplete identity)"
                    : $"{brand?.displayName} {model.displayName} {model.generation} {modelYear} {variant.displayName}".Trim();
            }
        }

        internal void RecordPublication(AssetVehicleCarStoreProduct product, string fingerprint, string outputHash)
        { generatedProduct = product; generatedFingerprint = fingerprint; generatedOutputHash = outputHash; }
    }

    /// <summary>Authoring gates, not measured FPS predictions. Zero is not an unlimited budget.</summary>
    [Serializable]
    public sealed class VehicleContentBudget
    {
        [Min(1)] public int maximumLod0Triangles = 120000;
        [Min(1)] public int maximumMaterialSlots = 32;
        [Min(1)] public int maximumTextureDimension = 4096;
        [Min(1)] public int maximumAudioMiB = 64;
        public bool requireLods = true;
        public bool enforceAsErrors;
    }
}
