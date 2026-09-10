using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [RequireComponent(typeof(RoadVehicleMotor))]
    public sealed class TrafficVehicleLights : MonoBehaviour
    {
        [SerializeField] private Renderer[] brakeLights;
        [SerializeField] private Renderer[] leftIndicators;
        [SerializeField] private Renderer[] rightIndicators;
        private RoadVehicleMotor motor;
        private MaterialPropertyBlock properties;
        public void Configure(Renderer[] brakes, Renderer[] left, Renderer[] right)
        { brakeLights = brakes; leftIndicators = left; rightIndicators = right; }
        private void Awake() { motor = GetComponent<RoadVehicleMotor>(); properties = new MaterialPropertyBlock(); }
        private void LateUpdate()
        {
            bool flash = Mathf.Repeat(Time.time, 0.8f) < 0.4f;
            bool hazards = motor.Immobilized || motor.DriverState == TrafficDriverState.Recovering;
            Set(brakeLights, motor.BrakeLights ? Color.red : new Color(0.18f, 0.01f, 0.01f),motor.BrakeLights?1500:50);
            Set(leftIndicators, flash && (hazards || motor.TurnSignal < 0) ? new Color(1, 0.55f, 0) : Color.black,3000);
            Set(rightIndicators, flash && (hazards || motor.TurnSignal > 0) ? new Color(1, 0.55f, 0) : Color.black,3000);
        }
        private void Set(Renderer[] lights, Color color,float luminance)
        {
            if (lights == null) return;
            properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
            properties.SetColor("_EmissiveColor",color*luminance);
            foreach (Renderer light in lights) if (light != null) light.SetPropertyBlock(properties);
        }
    }
}
