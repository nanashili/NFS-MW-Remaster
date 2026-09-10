#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static partial class DrivingDemoBuilder
    {
        private static void ConfigurePoliceRig(VehiclePoliceUnit unit, Material tire)
        {
            const string path = "Assets/NfsMw/Modules/Driving/Data/PoliceVehicleTuning.asset";
            var tuning = AssetDatabase.LoadAssetAtPath<VehicleTuning>(path);
            if (tuning == null)
            {
                tuning = VehicleTuning.CreateStreetRacer(); tuning.displayName = "Provisional Ventura Bay Police";
                tuning.chassis.mass = 1550f; tuning.engine.maxTorqueNewtonMeters = 410f;
                tuning.handling.driftBias = 0f; tuning.handling.brakeToDrift = false;
                AssetDatabase.CreateAsset(tuning, path);
            }
            var body = unit.GetComponent<Rigidbody>(); body.constraints = RigidbodyConstraints.None;
            var wheels = new VehicleWheel[4]; int index = 0;
            foreach (int axle in new[] { 1, -1 }) foreach (int side in new[] { -1, 1 })
                wheels[index++] = CreateWheel(unit.transform, "Police Wheel " + axle + " " + side,
                    new Vector3(side * 0.88f, 0f, axle * 1.38f), axle == 1 ? VehicleAxle.Front : VehicleAxle.Rear,
                    axle == 1, axle == -1, axle == -1, tire, tuning);
            unit.GetComponent<VehicleController>().ConfigureForRuntime(tuning, unit, wheels);
            unit.gameObject.AddComponent<PoliceVehicleFeedback>().ConfigureLightbar(
                unit.transform.Find("Emergency Light Red")?.GetComponent<Renderer>(),
                unit.transform.Find("Emergency Light Blue")?.GetComponent<Renderer>());
        }
    }
}
#endif
