using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Spatial siren admitted by the shared scene budget; lightbar observes the same unit state.</summary>
    [RequireComponent(typeof(VehiclePoliceUnit))]
    public sealed class PoliceVehicleFeedback : MonoBehaviour
    {
        [SerializeField] private AudioClip sirenClip;
        [SerializeField] private SensoryAudioWorld world;
        [SerializeField] private PoliceSensoryProfile profile;
        [SerializeField] private Light[] redLights = System.Array.Empty<Light>(), blueLights = System.Array.Empty<Light>();
        [SerializeField] private Renderer redLens, blueLens;
        private VehiclePoliceUnit unit;
        private FeedbackVoiceLease siren;
        private int variant;
        private float phase;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock properties;
        public void ConfigureLightbar(Renderer red, Renderer blue) { redLens = red; blueLens = blue; }
        public void ConfigureSensory(SensoryAudioWorld audioWorld, PoliceSensoryProfile content) { world = audioWorld; profile = content; }
        private void Awake()
        {
            unit = GetComponent<VehiclePoliceUnit>();
            properties = new MaterialPropertyBlock();
            uint hash = 2166136261;
            foreach (char value in unit.UnitId ?? name) hash = (hash ^ value) * 16777619;
            variant = (int)(hash % 1009); phase = variant / 1009f;
        }
        private void Update()
        {
            bool emergency = unit != null && !unit.IsDisabled && (unit.State == VehiclePoliceUnitState.Engaged || unit.State == VehiclePoliceUnitState.Responding);
            if (world != null)
            {
                AudioClip clip = profile != null && profile.sirens.Length > 0 ? profile.sirens[variant % profile.sirens.Length] : sirenClip;
                if (emergency && !world.Owns(siren)) siren = world.Play(clip, SensoryCategory.Sirens, transform, Vector3.up, 0.7f, 1, 70, true, phase: phase);
                if (emergency) world.UpdateVoice(siren, 0.7f); else world.Release(siren);
            }
            bool canFlash = world == null || world.Preferences.flashes;
            bool flash = Mathf.Repeat(Time.time + phase, 0.5f) < 0.25f;
            emergency &= canFlash;
            Set(redLights, emergency && flash); Set(blueLights, emergency && !flash);
            SetLens(redLens, emergency && flash ? Color.red : new Color(0.12f, 0.01f, 0.01f),emergency&&flash?5000:0);
            SetLens(blueLens, emergency && !flash ? Color.cyan : new Color(0.01f, 0.02f, 0.12f),emergency&&!flash?5000:0);
        }
        private void SetLens(Renderer lens, Color color,float luminance=0)
        {
            if (lens == null || properties == null) return;
            lens.GetPropertyBlock(properties); properties.SetColor(BaseColor, color); properties.SetColor(ColorId, color);
            properties.SetColor("_EmissiveColor",color*luminance);
            lens.SetPropertyBlock(properties);
        }
        private static void Set(Light[] lights, bool enabled) { if (lights != null) foreach (var light in lights) if (light != null) light.enabled = enabled; }
        private void OnDisable() { if (world != null) world.Release(siren); Set(redLights, false); Set(blueLights, false); SetLens(redLens, new Color(0.12f, 0.01f, 0.01f)); SetLens(blueLens, new Color(0.01f, 0.02f, 0.12f)); }
    }
}
