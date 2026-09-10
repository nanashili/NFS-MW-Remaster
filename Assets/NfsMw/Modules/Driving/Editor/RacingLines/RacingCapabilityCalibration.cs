using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    [Serializable]
    public struct RacingCalibrationSample { public float time, speed, acceleration; public bool braking, shifting; }

    public sealed class RacingCapabilityCalibration : IDisposable
    {
        private readonly RacingVehicleSetup setup;
        private readonly Scene scene;
        private readonly RacingVehicleRig rig;
        private readonly List<RacingCalibrationSample> data = new List<RacingCalibrationSample>();
        private int steps;
        private float previousSpeed, brakeStarted = -1;
        private bool disposed;
        public string Fingerprint { get; }
        public bool IsDone { get; private set; }
        public float Time => steps * setup.fixedStep;
        public RacingCapabilityPoint[] Points { get; private set; }
        public string Evidence { get; private set; }
        public RacingCalibrationSample[] Samples => data.ToArray();

        public RacingCapabilityCalibration(RacingVehicleSetup setup)
        {
            if (Application.isPlaying) throw new ArgumentException("Exit Play mode to calibrate.");
            this.setup = setup; Fingerprint = RacingLineSnapshot.VehicleFingerprintOf(setup);
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var ground = new GameObject("Synthetic dry straight, grip 1"); SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = new Vector3(0, -0.25f, 700);
                ground.AddComponent<BoxCollider>().size = new Vector3(100, 0.5f, 1600);
                ground.AddComponent<VehicleSurface>().Configure("Calibration asphalt", 1);
                rig = new RacingVehicleRig(scene, setup, Vector3.up * RacingVehicleRig.SpawnHeight(setup), Quaternion.identity);
                Physics.SyncTransforms();
            }
            catch { Dispose(); throw; }
        }
        public void Advance(double milliseconds = 4)
        {
            if (IsDone || disposed) return;
            var watch = Stopwatch.StartNew();
            try
            {
                if (RacingLineSnapshot.VehicleFingerprintOf(setup) != Fingerprint)
                    throw new ArgumentException("LINE_CALIBRATION_STALE: vehicle changed between work slices.");
                do
                {
                    bool settling = Time < 1;
                    if (!settling && brakeStarted < 0 && (Time >= 31 || previousSpeed >= 35)) brakeStarted = Time;
                    bool braking = brakeStarted >= 0;
                    rig.Input.Current = settling ? new VehicleInputState { Handbrake = true }
                        : braking ? new VehicleInputState { Brake = previousSpeed > 1.2f ? 1 : 0, Handbrake = previousSpeed <= 1.2f }
                        : new VehicleInputState { Throttle = 1 };
                    rig.Vehicle.StepSimulation(setup.fixedStep); scene.GetPhysicsScene().Simulate(setup.fixedStep); steps++;
                    float speed = Vector3.Dot(rig.Body.linearVelocity, rig.Root.transform.forward);
                    if (!RacingLineSnapshot.Finite(speed) || Mathf.Abs(rig.Body.position.x) > 30 || rig.Body.position.y < -1)
                        throw new ArgumentException("LINE_CALIBRATION: rig left the straight fixture or produced invalid state.");
                    if (!settling) data.Add(new RacingCalibrationSample { time = Time, speed = speed, acceleration = (speed - previousSpeed) / setup.fixedStep,
                        braking = braking, shifting = rig.Vehicle.Telemetry.IsShifting });
                    previousSpeed = speed;
                    if (braking && (speed < 0.7f || Time - brakeStarted >= 15)) { Finish(); break; }
                } while (watch.Elapsed.TotalMilliseconds < milliseconds);
            }
            catch { Dispose(); throw; }
        }
        private void Finish()
        {
            float peak = 0; foreach (var sample in data) peak = Mathf.Max(peak, sample.speed);
            float covered = Mathf.Floor(peak * 0.85f);
            if (covered < 5) throw new ArgumentException("LINE_CALIBRATION: insufficient speed coverage; inspect the actual vehicle setup.");
            Points = new RacingCapabilityPoint[4];
            for (int i = 0; i < Points.Length; i++)
            {
                float speed = covered * i / (Points.Length - 1);
                var acceleration = new List<float>(); var braking = new List<float>();
                foreach (var sample in data)
                    if (Mathf.Abs(sample.speed - Mathf.Max(2, speed)) < Mathf.Max(3, covered / 5) && !sample.shifting)
                    {
                        if (sample.braking && sample.acceleration < -0.1f && sample.time - brakeStarted > 0.3f) braking.Add(-sample.acceleration);
                        else if (!sample.braking && sample.acceleration > 0.05f) acceleration.Add(sample.acceleration);
                    }
                if (acceleration.Count < 4 || braking.Count < 4)
                    throw new ArgumentException("LINE_CALIBRATION_COVERAGE: insufficient full-input samples near " + speed.ToString("F1") + " m/s.");
                acceleration.Sort(); braking.Sort();
                Points[i] = new RacingCapabilityPoint { speed = speed, acceleration = Mathf.Clamp(acceleration[acceleration.Count / 4] * 0.7f, 0.05f, 30),
                    braking = Mathf.Clamp(braking[braking.Count / 4] * 0.7f, 0.1f, 30), lateralAcceleration = 3 };
            }
            Evidence = "Measured shared-physics straight fixture, dry grip 1; " + Application.unityVersion + "; " + DateTime.UtcNow.ToString("O")
                + "; dt=" + setup.fixedStep.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + "; samples=" + data.Count + "; full throttle/service brake, non-shift positive samples, lower quartile x0.7. "
                + "Coverage 0.." + covered + " m/s. Lateral limit 3 m/s² remains a conservative assumption, NOT a measured tire envelope. "
                + "Recalibrate after tune/rig changes. Rollout validation remains mandatory.";
            IsDone = true; Dispose();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; rig?.Dispose();
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
