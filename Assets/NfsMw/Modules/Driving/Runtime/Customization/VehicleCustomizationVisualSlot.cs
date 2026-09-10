using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>A semantic mount. Prepare owns temporary resources; Commit publishes an already built visual.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleCustomizationVisualSlot : MonoBehaviour
    {
        [SerializeField] private VehicleCustomizationCategory category;
        [SerializeField] private string slotId = string.Empty;
        [SerializeField] private Transform visualMount;
        [SerializeField] private Renderer[] targetRenderers = Array.Empty<Renderer>();
        [SerializeField] private bool hideTargetRenderersForReplacement;
        private GameObject spawnedVisual;
        private Material[][] instanceMaterials;
        private StockRenderer[] stock;
        private bool ownsColor, ownsTexture;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorProperty = Shader.PropertyToID("_Color"), BaseMap = Shader.PropertyToID("_BaseColorMap"), MainTex = Shader.PropertyToID("_MainTex");
        private sealed class StockRenderer
        {
            public Material[] materials;
            public bool enabled;
            public Color baseColor, color;
            public Texture baseMap, mainTex;
        }
        internal sealed class PreparedVisual : IDisposable
        {
            internal GameObject replacement;
            internal Material[][] materials;
            internal VehicleCustomizationVisualPayload payload;
            public void Dispose() { if (replacement) DestroyOwned(replacement); DestroyMaterials(materials); replacement = null; materials = null; }
        }
        public VehicleCustomizationCategory Category => category;
        public string SlotId => slotId;
        public GameObject SpawnedVisual => spawnedVisual;
        public void ConfigureSlot(string id, Transform mount, Renderer[] renderers, bool hide)
        { slotId = id ?? string.Empty; Configure(category, mount, renderers, hide); }
        public void Configure(VehicleCustomizationCategory configuredCategory, Transform configuredMount, Renderer[] configuredRenderers, bool configuredHideTargetRenderers)
        {
            if (stock != null) RestoreStock();
            category = configuredCategory; visualMount = configuredMount; targetRenderers = configuredRenderers ?? Array.Empty<Renderer>(); hideTargetRenderersForReplacement = configuredHideTargetRenderers; stock = null;
        }
        public void Apply(IVehicleCustomizationItem item, VehicleCustomizationVisualPayload payloadOverride = null)
        { using var prepared = Prepare(item, payloadOverride); Commit(prepared); }
        internal PreparedVisual Prepare(IVehicleCustomizationItem item, VehicleCustomizationVisualPayload payloadOverride)
        {
            CaptureStock();
            // Read user-provided metadata before changing a renderer or discarding the current visual.
            var source = item as IVehicleCustomizationVisualSource;
            var payload = item == null || source == null ? null : payloadOverride ?? source.Visual;
            var result = new PreparedVisual { payload = payload };
            try
            {
                if (payload?.ReplacementPrefab)
                {
                    result.replacement = Instantiate(payload.ReplacementPrefab, visualMount ? visualMount : transform);
                    result.replacement.SetActive(false);
                    result.replacement.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    result.replacement.transform.localScale = Vector3.one;
                }
                var overrides = payload?.MaterialOverrides;
                if (overrides != null && overrides.Length > 0)
                {
                    for (int j = 0; j < overrides.Length; j++) if (!overrides[j]) throw new ArgumentException("Slot " + slotId + ": material override " + j + " is missing.");
                    result.materials = new Material[targetRenderers.Length][];
                    for (int i = 0; i < targetRenderers.Length; i++) if (targetRenderers[i])
                    {
                        result.materials[i] = new Material[overrides.Length];
                        for (int j = 0; j < overrides.Length; j++) result.materials[i][j] = new Material(overrides[j]);
                    }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        internal void Commit(PreparedVisual prepared)
        {
            RestoreStock();
            spawnedVisual = prepared.replacement; instanceMaterials = prepared.materials;
            prepared.replacement = null; prepared.materials = null;
            var payload = prepared.payload;
            ownsColor = payload != null && payload.OverrideColor;
            ownsTexture = payload != null && payload.TextureOverride;
            for (int i = 0; i < targetRenderers.Length; i++) if (targetRenderers[i])
            {
                var renderer = targetRenderers[i];
                if (instanceMaterials != null && instanceMaterials[i] != null) renderer.sharedMaterials = instanceMaterials[i];
                if (spawnedVisual && hideTargetRenderersForReplacement) renderer.enabled = false;
                if (!ownsColor && !ownsTexture) continue;
                renderer.GetPropertyBlock(block);
                if (ownsColor) { Color color = payload.Color; color.a *= payload.Opacity; block.SetColor(BaseColor, color); block.SetColor(ColorProperty, color); }
                if (ownsTexture) { block.SetTexture(BaseMap, payload.TextureOverride); block.SetTexture(MainTex, payload.TextureOverride); }
                renderer.SetPropertyBlock(block);
            }
            if (spawnedVisual) spawnedVisual.SetActive(true);
        }
        private void CaptureStock()
        {
            if (stock != null) return;
            if (targetRenderers == null || targetRenderers.Length == 0) targetRenderers = GetComponentsInChildren<Renderer>(true);
            block ??= new MaterialPropertyBlock(); stock = new StockRenderer[targetRenderers.Length];
            for (int i = 0; i < targetRenderers.Length; i++) if (targetRenderers[i])
            {
                var renderer = targetRenderers[i]; var material = renderer.sharedMaterial; renderer.GetPropertyBlock(block);
                stock[i] = new StockRenderer { materials = renderer.sharedMaterials, enabled = renderer.enabled,
                    baseColor = ReadColor(block, material, BaseColor), color = ReadColor(block, material, ColorProperty),
                    baseMap = ReadTexture(block, material, BaseMap), mainTex = ReadTexture(block, material, MainTex) };
            }
        }
        private static Color ReadColor(MaterialPropertyBlock block, Material material, int id) => block.HasColor(id) ? block.GetColor(id) : material && material.HasProperty(id) ? material.GetColor(id) : Color.white;
        private static Texture ReadTexture(MaterialPropertyBlock block, Material material, int id) => block.HasTexture(id) ? block.GetTexture(id) : material && material.HasProperty(id) ? material.GetTexture(id) : null;
        private void RestoreStock()
        {
            if (spawnedVisual) { spawnedVisual.SetActive(false); DestroyOwned(spawnedVisual); spawnedVisual = null; }
            if (stock != null) for (int i = 0; i < targetRenderers.Length; i++) if (targetRenderers[i] && stock[i] != null)
            {
                var renderer = targetRenderers[i]; var original = stock[i]; renderer.sharedMaterials = original.materials; renderer.enabled = original.enabled;
                if (!ownsColor && !ownsTexture) continue;
                renderer.GetPropertyBlock(block);
                if (ownsColor) { block.SetColor(BaseColor, original.baseColor); block.SetColor(ColorProperty, original.color); }
                if (ownsTexture) { block.SetTexture(BaseMap, original.baseMap ? original.baseMap : Texture2D.whiteTexture); block.SetTexture(MainTex, original.mainTex ? original.mainTex : Texture2D.whiteTexture); }
                renderer.SetPropertyBlock(block);
            }
            ownsColor = ownsTexture = false; DestroyMaterials(instanceMaterials); instanceMaterials = null;
        }
        private static void DestroyMaterials(Material[][] materials)
        { if (materials != null) foreach (var row in materials) if (row != null) foreach (var material in row) if (material) DestroyOwned(material); }
        private static void DestroyOwned(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDestroy() => RestoreStock();
    }
}
