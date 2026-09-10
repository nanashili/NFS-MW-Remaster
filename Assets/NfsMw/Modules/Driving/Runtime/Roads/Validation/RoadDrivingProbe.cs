#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    // An explicitly invoked development-player check. The existing vehicle and wheel simulation apply all forces.
    public sealed class RoadDrivingProbe : MonoBehaviour, IVehicleInputSource
    {
        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string error, unityVersion, networkId, fingerprint;
            public float simulatedSeconds, forwardMetres, maximumLateralError, finalSpeed;
            public int groundedFrames, physicsFrames, surfaceMismatches, authoringComponents;
        }
        private string reportPath;
        private bool driving;
        public VehicleInputState Current => new VehicleInputState { Throttle = driving ? 0.5f : 0 };
        public bool ConsumeResetRequest() => false;
        public bool ConsumeCameraToggleRequest() => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BeginIfRequested()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--road-validation");
            if (at < 0 || at + 1 >= args.Length) return;
            var root = new GameObject("Road driving validation");
            Application.runInBackground = true;
            DontDestroyOnLoad(root);
            var probe = root.AddComponent<RoadDrivingProbe>(); probe.reportPath = args[at + 1];
            probe.StartCoroutine(probe.Run());
        }

        private IEnumerator Run()
        {
            var report = new Report { unityVersion = Application.unityVersion };
            yield return SceneManager.LoadSceneAsync("RoadDrivingValidation", LoadSceneMode.Single);
            yield return null;
            var vehicle = FindAnyObjectByType<VehicleController>();
            var network = FindAnyObjectByType<RoadNetwork>();
            if (vehicle == null || network == null || !network.UsesBakedData)
            { Finish(report, "Missing shared vehicle or published road network."); yield break; }
            report.networkId = network.Publication.NetworkId.ToString(); report.fingerprint = network.Publication.Fingerprint;
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                string name = component.GetType().FullName;
                if (name == "NfsMwRemaster.Driving.RoadAuthoring" || name == "NfsMwRemaster.Driving.RoadNetworkAuthoring"
                    || name == "UnityEngine.Splines.SplineContainer") report.authoringComponents++;
            }
            vehicle.SetInputSource(this);
            var fixedStep = new WaitForFixedUpdate();
            for (int i = 0; i < 50; i++) yield return fixedStep;
            Vector3 start = vehicle.Body.position;
            driving = true;
            for (int i = 0; i < 400; i++)
            {
                yield return fixedStep;
                report.physicsFrames++; report.simulatedSeconds += Time.fixedDeltaTime;
                bool grounded = vehicle.Wheels.Length == 4;
                foreach (var wheel in vehicle.Wheels)
                {
                    grounded &= wheel.Grounded;
                    if (wheel.Grounded && wheel.SurfaceKind != SensorySurface.AsphaltDry) report.surfaceMismatches++;
                }
                if (grounded) report.groundedFrames++;
                report.maximumLateralError = Mathf.Max(report.maximumLateralError, Mathf.Abs(vehicle.Body.position.x - start.x));
            }
            driving = false;
            report.forwardMetres = vehicle.Body.position.z - start.z;
            report.finalSpeed = vehicle.Body.linearVelocity.magnitude;
            bool pass = report.forwardMetres > 15 && report.forwardMetres < 190 && report.maximumLateralError < 0.5f
                && report.groundedFrames >= 390 && report.surfaceMismatches == 0 && report.authoringComponents == 0
                && vehicle.Body.position.y > 0 && vehicle.Body.position.y < 2;
            var args = Environment.GetCommandLineArgs(); int captureAt = Array.IndexOf(args, "--road-capture");
            if (captureAt >= 0 && captureAt + 1 < args.Length)
            {
                string capture = Path.GetFullPath(args[captureAt + 1]); Directory.CreateDirectory(Path.GetDirectoryName(capture));
                ScreenCapture.CaptureScreenshot(capture);
                for (int frame = 0; frame < 120 && !File.Exists(capture); frame++) yield return null;
                if (!File.Exists(capture)) { Finish(report, "Requested player screenshot was not written."); yield break; }
            }
            Finish(report, pass ? null : "Driving, wheel contact, surface binding or authoring stripping gate failed.");
        }

        private void Finish(Report report, string error)
        {
            report.passed = error == null; report.error = error;
            try
            {
                string path = Path.GetFullPath(reportPath); Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log(report.passed ? "ROAD_DRIVING_VALIDATION_PASSED" : "ROAD_DRIVING_VALIDATION_FAILED: " + error);
            }
            catch (Exception exception) { Debug.LogException(exception); report.passed = false; }
            Application.Quit(report.passed ? 0 : 1);
        }
    }
}
#endif
