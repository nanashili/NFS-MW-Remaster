#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Isolated-project CLI probe. Only in-memory profile storage is used during play.</summary>
    [InitializeOnLoad]
    public static class DrivingPoliceReferenceSmoke
    {
        private const string Key = "NfsPoliceReferenceSmoke";
        private static int stage;
        private static float stageAt;
        private static double deadline;
        private static VehiclePursuitDirector director;
        private static VehiclePoliceUnit cop;
        private static CareerProfileSystem profile;
        private static PoliceSmokeStorage storage;
        private static Vector3 startPosition;
        private static GameObject wall;
        static DrivingPoliceReferenceSmoke()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.delayCall += Resume;
        }
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this probe in an isolated Unity batch project.");
            DrivingDemoBuilder.BuildFreeRoamScene();
            ValidateSceneRigs();
            DrivingDemoBuilder.BuildPursuitTestScene();
            ValidateSceneRigs();
            foreach (var p in UnityEngine.Object.FindObjectsByType<CareerProfileSystem>(FindObjectsSortMode.None))
                p.ConfigureAutomaticPersistence(false, false, false);
            SessionState.SetBool(Key, true); deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.isPlaying = true;
            Resume();
        }
        private static void ValidateSceneRigs()
        {
            int units = 0;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                {
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) == 0, "Missing script: " + node.name);
                    if (!node.TryGetComponent<VehiclePoliceUnit>(out var unit)) continue;
                    Require(unit.Vehicle.Wheels.Length == 4 && unit.GetComponent<RoadVehicleMotor>() == null, "Unmigrated police rig: " + node.name);
                    units++;
                }
            Require(units >= 6, "Missing authored police pool.");
            Require(UnityEngine.Object.FindObjectsByType<PoliceRoadHazard>(FindObjectsSortMode.None).Length >= 2, "Missing authored hazard sites.");
        }
        private static void Resume()
        {
            deadline = EditorApplication.timeSinceStartup + 120; stage = 0;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Next() { stage++; stageAt = Time.time; }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Police smoke timed out at stage " + stage);
                if (!EditorApplication.isPlaying || Time.time < 0.1f) return;
                switch (stage)
                {
                    case 0:
                        director = UnityEngine.Object.FindAnyObjectByType<VehiclePursuitDirector>();
                        Require(director != null && director.Target != null, "Missing pursuit rig.");
                        profile = director.Target.Transform.GetComponent<CareerProfileSystem>();
                        profile.ConfigureAutomaticPersistence(false, false, false);
                        storage = profile.gameObject.AddComponent<PoliceSmokeStorage>(); profile.SetStorage(storage);
                        profile.GetComponent<VehicleStoreWallet>().SetBalance(10000);
                        profile.TryCreateNewProfile(out _);
                        cop = UnityEngine.Object.FindObjectsByType<VehiclePoliceUnit>(FindObjectsSortMode.None)[0];
                        Require(cop.Vehicle != null && cop.Vehicle.Wheels.Length == 4, "Police have no production wheel rig.");
                        Require(cop.GetComponent<RoadVehicleMotor>() == null, "Legacy traffic motor remains on police.");
                        // Known-input physical straight-line approach with sufficient braking distance.
                        foreach (var other in UnityEngine.Object.FindObjectsByType<VehiclePoliceUnit>(FindObjectsSortMode.None))
                            if (other != cop) other.gameObject.SetActive(false);
                        cop.gameObject.SetActive(false);
                        Vector3 targetPosition = director.Target.Position;
                        Require(cop.PlaceWhileInactive(targetPosition - Vector3.forward * 45f + Vector3.up * 0.5f, Quaternion.identity), "Placement failed.");
                        cop.gameObject.SetActive(true); startPosition = cop.Position;
                        Require(director.TryStartPursuit(out string startFailure), startFailure);
                        Next(); break;
                    case 1:
                        if (Time.time - stageAt < 5f) return;
                        Require(Vector3.Distance(startPosition, cop.Position) > 3f, "Police failed to move under production vehicle input: " + cop.Position + ", input=" + JsonUtility.ToJson(cop.Current));
                        Require(cop.Vehicle.Body.constraints == RigidbodyConstraints.None, "Police chassis still has legacy rotation locks.");
                        Require(cop.Vehicle.Body.linearVelocity.magnitude < 100f, "Unbounded physical speed.");
                        Debug.Log("Police physical input smoke: travelled=" + Vector3.Distance(startPosition, cop.Position) + "m, speed=" + cop.Vehicle.Body.linearVelocity.magnitude + "m/s");
                        // A real occluder must prevent observation; move the test rig only while inactive.
                        cop.gameObject.SetActive(false);
                        Require(cop.PlaceWhileInactive(director.Target.Position - Vector3.forward * 18f + Vector3.up * 0.5f, Quaternion.identity), "Reposition failed.");
                        cop.gameObject.SetActive(true); cop.Vehicle.enabled = false; cop.Vehicle.Body.isKinematic = true;
                        var targetBody = director.Target.Body; targetBody.isKinematic = true;
                        director.Target.Transform.GetComponent<VehicleController>().enabled = false;
                        wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Police Smoke Occluder";
                        wall.transform.position = director.Target.Position - Vector3.forward * 8f + Vector3.up * 3f;
                        wall.transform.localScale = new Vector3(30, 7, 2); Physics.SyncTransforms();
                        storage.Fail = true; Next(); break;
                    case 2:
                        if (director.EncounterState != PoliceEncounterState.OutcomePending) return;
                        Require(director.PendingOutcome.kind == PoliceOutcomeKind.Escaped, "LOS loss did not produce escape.");
                        Require(profile.GetComponent<VehicleStoreWallet>().Balance == 10000, "Unacknowledged escape changed cash.");
                        Require(profile.CurrentProfile.police.settlements.Count == 0, "Failed save created a receipt.");
                        Require(!director.TryAcknowledgeOutcome(out _), "Failed storage unexpectedly acknowledged.");
                        storage.Fail = false;
                        Require(director.TryAcknowledgeOutcome(out string failure), failure);
                        Require(!director.IsActive && profile.CurrentProfile.economy.reputation > 0, "Escape was not settled.");
                        Require(profile.GetComponent<VehicleStoreWallet>().Balance == 10000, "Escape awarded cash.");
                        Require(profile.CurrentProfile.police.settlements.Count == 1, "Escape receipt not exactly once.");
                        Debug.Log("Police reference runtime smoke passed: physical vehicle input, LOS cooldown, save failure hold, REP-only escape, exactly-once acknowledgement.");
                        Finish(0); break;
                }
            }
            catch (Exception exception) { Debug.LogError("Police reference smoke failed: " + exception); Finish(1); }
        }
        private static void Finish(int code)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Poll;
            EditorApplication.isPlaying = false; EditorApplication.Exit(code);
        }
    }
    public sealed class PoliceSmokeStorage : MonoBehaviour, ICareerProfileStorage
    {
        public bool Fail; private string json;
        public bool TrySave(string id, string value, out string failure) { failure = Fail ? "Injected disk failure" : ""; if (!Fail) json = value; return !Fail; }
        public bool TryLoad(string id, out string value, out string failure) { value = json; failure = ""; return json != null; }
    }
}
#endif
