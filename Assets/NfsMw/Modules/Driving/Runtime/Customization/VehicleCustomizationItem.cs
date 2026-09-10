using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Visual shop slots based on the 2005-style vehicle customisation menu.
    /// Vinyl is intentionally one slot; its Style identifies the vinyl family.
    /// </summary>
    public enum VehicleCustomizationCategory
    {
        BodyKit,
        Spoiler,
        Rims,
        Hood,
        RoofScoop,
        Paint,
        RimPaint,
        WindowTint,
        Vinyl,
        Decals,
        Numbers,
        CustomGauges
        ,Bumper
        ,Fender
        ,SideSkirt
        ,Mirror
        ,Exhaust
        ,Tires
        ,Interior
    }

    /// <summary>
    /// Optional family metadata for filtering and shop presentation. New
    /// families can be added without changing build or vehicle orchestration.
    /// </summary>
    public enum VehicleCustomizationStyle
    {
        Standard,
        Sport,
        Tuner,
        SportCarbon,
        TunerCarbon,
        Flame,
        Tribal,
        Stripe,
        RaceFlag,
        NationalFlag,
        Body,
        Unique,
        ContestWinner
    }

    /// <summary>
    /// Asset references are deliberately optional. A definition can be added
    /// to a catalogue now and receive its prefab/material/texture later.
    /// </summary>
    [Serializable]
    public sealed class VehicleCustomizationVisualPayload
    {
        [SerializeField] private GameObject replacementPrefab = null!;
        [SerializeField] private Material[] materialOverrides =
            Array.Empty<Material>();
        [SerializeField] private Texture2D textureOverride = null!;
        [SerializeField] private Sprite gaugeSprite = null!;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private bool overrideColor;
        [SerializeField, Range(0f, 1f)] private float opacity = 1f;

        public VehicleCustomizationVisualPayload() { }
        public VehicleCustomizationVisualPayload(Material[] materials)
        { materialOverrides = materials ?? Array.Empty<Material>(); }
        public VehicleCustomizationVisualPayload(GameObject prefab, Material[] materials)
        { replacementPrefab = prefab; materialOverrides = materials ?? Array.Empty<Material>(); }

        public GameObject ReplacementPrefab
        {
            get { return replacementPrefab; }
        }

        public Material[] MaterialOverrides
        {
            get { return materialOverrides ?? Array.Empty<Material>(); }
        }

        public Texture2D TextureOverride
        {
            get { return textureOverride; }
        }

        public Sprite GaugeSprite
        {
            get { return gaugeSprite; }
        }

        public Color Color
        {
            get { return color; }
        }

        public bool OverrideColor
        {
            get { return overrideColor; }
        }

        public float Opacity
        {
            get { return Mathf.Clamp01(opacity); }
        }

        public void Configure(
            GameObject configuredPrefab,
            Material[] configuredMaterials,
            Texture2D configuredTexture,
            Sprite configuredGaugeSprite,
            Color configuredColor,
            bool configuredOverrideColor,
            float configuredOpacity)
        {
            replacementPrefab = configuredPrefab;
            materialOverrides = configuredMaterials ?? Array.Empty<Material>();
            textureOverride = configuredTexture;
            gaugeSprite = configuredGaugeSprite;
            color = configuredColor;
            overrideColor = configuredOverrideColor;
            opacity = Mathf.Clamp01(configuredOpacity);
        }
    }

    [Serializable]
    public sealed class VehiclePartMount
    {
        [SerializeField] private string slotId = string.Empty;
        [SerializeField] private VehicleCustomizationVisualPayload visual = new VehicleCustomizationVisualPayload();
        public string SlotId => slotId;
        public VehicleCustomizationVisualPayload Visual => visual;
        public void Configure(string id, VehicleCustomizationVisualPayload payload) { slotId = id ?? string.Empty; visual = payload ?? new VehicleCustomizationVisualPayload(); }
    }

    /// <summary>
    /// Small public contract for shop items. Items can come from Unity assets,
    /// career rewards, downloadable content, or a test without changing the
    /// customization system.
    /// </summary>
    public interface IVehicleCustomizationItem
    {
        string CustomizationId { get; }

        string DisplayName { get; }

        VehicleCustomizationCategory Category { get; }

        VehicleCustomizationStyle Style { get; }

        int Price { get; }

        bool IsStock { get; }
    }

    /// <summary>Optional part metadata. Existing item implementations need not implement this seam.</summary>
    public interface IVehicleCustomizationPartMetadata
    {
        IReadOnlyList<string> SupportedVehicleIds { get; }
        IReadOnlyList<string> RequiredSlotIds { get; }
        IReadOnlyList<string> RequiredPartIds { get; }
        IReadOnlyList<string> ConflictingPartIds { get; }
        IReadOnlyList<string> MountSlotIds { get; }
        float WheelRadiusDelta { get; }
        float WheelOffsetDelta { get; }
        float TrackWidthDelta { get; }
        float ClearanceDelta { get; }
    }

    /// <summary>Optional variant-level compatibility metadata; legacy parts remain valid.</summary>
    public interface IVehicleCustomizationVariantMetadata
    {
        IReadOnlyList<string> SupportedVariantIds { get; }
    }

    public interface IVehicleCustomizationPhysicalEffect
    {
        void ApplyPhysicalEffects(VehicleTuning tuning);
    }

    /// <summary>
    /// Optional visual data seam. A custom item can omit this and provide its
    /// own visual adapter, while asset-backed items use the built-in payload.
    /// </summary>
    public interface IVehicleCustomizationVisualSource
    {
        VehicleCustomizationVisualPayload Visual { get; }
    }

    /// <summary>
    /// Base class for content assets. The visual payload remains empty until
    /// the corresponding vehicle mesh/material/texture assets are available.
    /// </summary>
    public abstract class VehicleCustomizationDefinition :
        ScriptableObject,
        IVehicleCustomizationItem,
        IVehicleCustomizationVisualSource,
        IVehicleStoreProduct,
        IVehicleCustomizationPartMetadata,
        IVehicleCustomizationVariantMetadata,
        IVehicleCustomizationPhysicalEffect
    {
        [SerializeField] private string customizationId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private VehicleCustomizationCategory category;
        [SerializeField] private VehicleCustomizationStyle style;
        [SerializeField, Min(0)] private int price;
        [SerializeField] private bool stock;
        [SerializeField] private VehicleCustomizationVisualPayload visual =
            new VehicleCustomizationVisualPayload();
        [SerializeField] private string[] supportedVehicleIds = Array.Empty<string>();
        [SerializeField] private string[] supportedVariantIds = Array.Empty<string>();
        [SerializeField] private string[] requiredSlotIds = Array.Empty<string>();
        [SerializeField] private string[] requiredPartIds = Array.Empty<string>();
        [SerializeField] private string[] conflictingPartIds = Array.Empty<string>();
        [SerializeField] private string[] mountSlotIds = Array.Empty<string>();
        [SerializeField] private float wheelRadiusDelta;
        [SerializeField] private float wheelOffsetDelta;
        [SerializeField] private float trackWidthDelta;
        [SerializeField] private float clearanceDelta;
        [SerializeField] private VehiclePerformanceModifier physicalModifier = new VehiclePerformanceModifier();
        [SerializeField] private VehiclePartMount[] mounts = Array.Empty<VehiclePartMount>();

        public string CustomizationId
        {
            get
            {
                return string.IsNullOrWhiteSpace(customizationId)
                    ? name
                    : customizationId;
            }
        }

        public string DisplayName
        {
            get
            {
                return string.IsNullOrWhiteSpace(displayName)
                    ? Category + " " + Style
                    : displayName;
            }
        }

        public VehicleCustomizationCategory Category
        {
            get { return category; }
        }

        public VehicleCustomizationStyle Style
        {
            get { return style; }
        }

        public int Price
        {
            get { return Mathf.Max(0, price); }
        }

        public string ProductId
        {
            get { return CustomizationId; }
        }

        public VehicleStoreCategory StoreCategory
        {
            get { return VehicleStoreCategory.BodyShop; }
        }

        public VehicleStoreProductKind ProductKind
        {
            get { return VehicleStoreProductKind.Customization; }
        }

        public bool IsAvailable
        {
            get { return !IsStock; }
        }

        public bool IsStock
        {
            get { return stock; }
        }

        public IReadOnlyList<string> SupportedVehicleIds => supportedVehicleIds ?? Array.Empty<string>();
        public IReadOnlyList<string> SupportedVariantIds => supportedVariantIds ?? Array.Empty<string>();
        public IReadOnlyList<string> RequiredSlotIds => requiredSlotIds ?? Array.Empty<string>();
        public IReadOnlyList<string> RequiredPartIds => requiredPartIds ?? Array.Empty<string>();
        public IReadOnlyList<string> ConflictingPartIds => conflictingPartIds ?? Array.Empty<string>();
        public IReadOnlyList<string> MountSlotIds => mountSlotIds ?? Array.Empty<string>();
        public float WheelRadiusDelta => wheelRadiusDelta;
        public float WheelOffsetDelta => wheelOffsetDelta;
        public float TrackWidthDelta => trackWidthDelta;
        public float ClearanceDelta => clearanceDelta;
        public VehiclePerformanceModifier PhysicalModifier => physicalModifier;
        public IReadOnlyList<VehiclePartMount> Mounts => mounts ?? Array.Empty<VehiclePartMount>();

        public virtual void ApplyPhysicalEffects(VehicleTuning tuning)
        {
            if (tuning == null || tuning.tires == null) throw new ArgumentNullException(nameof(tuning));
            if (!float.IsFinite(wheelRadiusDelta) || !float.IsFinite(wheelOffsetDelta)
                || !float.IsFinite(trackWidthDelta) || !float.IsFinite(clearanceDelta))
                throw new ArgumentException("Customization wheel fitment values must be finite.", nameof(tuning));

            float radius = tuning.tires.wheelRadius + wheelRadiusDelta;
            float clearance = tuning.tires.suspensionRestLength + clearanceDelta;
            float lateralOffset = tuning.tires.wheelLateralOffset + trackWidthDelta * 0.5f + wheelOffsetDelta;
            if (!float.IsFinite(radius) || radius < 0.05f || !float.IsFinite(clearance) || clearance < 0.05f
                || !float.IsFinite(lateralOffset) || lateralOffset < 0f)
                throw new ArgumentException("Customization wheel fitment would produce an invalid tire geometry.", nameof(tuning));

            tuning.tires.wheelRadius = radius;
            tuning.tires.wheelLateralOffset = lateralOffset;
            tuning.tires.suspensionRestLength = clearance;
            physicalModifier?.ApplyTo(tuning);
        }

        public VehicleCustomizationVisualPayload Visual
        {
            get
            {
                if (visual == null)
                {
                    visual = new VehicleCustomizationVisualPayload();
                }

                return visual;
            }
        }

        public void ConfigureMetadata(
            string configuredId,
            string configuredDisplayName,
            VehicleCustomizationCategory configuredCategory,
            VehicleCustomizationStyle configuredStyle,
            int configuredPrice,
            bool configuredStock)
        {
            customizationId = configuredId;
            displayName = configuredDisplayName;
            category = configuredCategory;
            style = configuredStyle;
            price = Mathf.Max(0, configuredPrice);
            stock = configuredStock;
        }

        public void ConfigureCompatibility(string[] vehicleIds, string[] variantIds)
        {
            supportedVehicleIds = vehicleIds ?? Array.Empty<string>();
            supportedVariantIds = variantIds ?? Array.Empty<string>();
        }

        public void ConfigurePhysicalFitment(float configuredWheelRadiusDelta, float configuredWheelOffsetDelta,
            float configuredTrackWidthDelta, float configuredClearanceDelta)
        {
            wheelRadiusDelta = configuredWheelRadiusDelta;
            wheelOffsetDelta = configuredWheelOffsetDelta;
            trackWidthDelta = configuredTrackWidthDelta;
            clearanceDelta = configuredClearanceDelta;
        }

        public void ConfigureVisual(VehicleCustomizationVisualPayload configuredVisual)
        {
            visual = configuredVisual ?? new VehicleCustomizationVisualPayload();
        }
    }

}
