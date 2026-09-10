using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Optional surface metadata read by the wheel raycast. A collider without
    /// this component uses neutral road values, so the vehicle stays usable in
    /// ordinary Unity scenes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleSurface : MonoBehaviour
    {
        [SerializeField] private string surfaceName = "Road";
        [SerializeField] private SensorySurfaceProfile profile;
        [SerializeField] private bool useProfilePhysics = true;
        public SensorySurfaceProfile Profile => profile;
        public SensorySurface Kind => profile != null ? profile.surface : SensorySurface.AsphaltDry;
        public void SetProfile(SensorySurfaceProfile value, bool applyPhysics = true) { profile = value; useProfilePhysics = applyPhysics; }
        [SerializeField, Min(0f)] private float gripMultiplier = 1f;
        [SerializeField, Min(0f)] private float rollingResistanceMultiplier = 1f;
        [SerializeField, Range(0f, 1f)] private float wetness;
        [SerializeField, Range(0f, 1f)] private float wetGripMultiplier = 0.72f;
        public float Wetness => wetness;
        public void SetWeatherWetness(float value) { wetness = Mathf.Clamp01(value); }

        public string SurfaceName
        {
            get { return profile != null ? profile.name : surfaceName; }
        }

        public float GripMultiplier
        {
            get
            {
                float baseGrip = profile != null && useProfilePhysics ? Mathf.Max(0, profile.grip) : gripMultiplier;
                return baseGrip * Mathf.Lerp(1f, wetGripMultiplier, wetness);
            }
        }

        public float RollingResistanceMultiplier
        {
            get { return profile != null && useProfilePhysics ? Mathf.Max(0, profile.rollingResistance) : rollingResistanceMultiplier; }
        }

        public void Configure(string newName, float newGripMultiplier, float newRollingResistanceMultiplier = 1f)
        {
            profile = null;
            surfaceName = string.IsNullOrWhiteSpace(newName) ? "Surface" : newName;
            gripMultiplier = Mathf.Max(0f, newGripMultiplier);
            rollingResistanceMultiplier = Mathf.Max(0f, newRollingResistanceMultiplier);
        }
    }
}
