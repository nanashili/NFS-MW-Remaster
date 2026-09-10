using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Surface")]
    public sealed class SensorySurfaceProfile : ScriptableObject
    {
        public SensorySurface surface = SensorySurface.AsphaltDry;
        [Min(0)] public float grip = 1, rollingResistance = 1;
        [Tooltip("Camera vibration roughness; suspension motion is added independently.")]
        [Range(0, 1)] public float cameraRoughness = .08f;
        public AudioClip rolling, tireSlip, impact, scrape, destruction;
        public ParticleSystem impactEffect;
        public ParticleSystem contactEffect;
        public bool leavesSkid = true;
        public Color skidColor = new Color(0.035f, 0.035f, 0.035f, 0.6f);
        [Range(0, 2)] public float particleIntensity = 1;
    }
}
