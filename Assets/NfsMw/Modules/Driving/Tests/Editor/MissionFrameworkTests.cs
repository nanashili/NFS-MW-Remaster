using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MissionFrameworkTests
    {
        private static MissionObjective Event(string id, string type, params string[] dependencies)
            => new MissionObjective { id = id, eventType = type, dependencies = dependencies };
        private static MissionDefinition Definition(params MissionObjective[] nodes)
            => new MissionDefinition { id = "mission.test", objectives = nodes, success = MissionCondition.Done(nodes[nodes.Length - 1].id) };
        private static void Step(MissionRuntime runtime, long step, params MissionEvent[] events) => runtime.Step(step, 1, 1, events);

        [Test] public void DefinitionIsDetachedAndStagesDoNotConsumePastEvents()
        {
            var definition = Definition(Event("first", "area.entered"), Event("second", "target.destroyed", "first"));
            var graph = new MissionGraph(definition); definition.objectives[0].required = 900;
            using var runtime = new MissionRuntime(graph);
            Step(runtime, 1, new MissionEvent("a", "area.entered"), new MissionEvent("b", "target.destroyed"));
            Assert.That(runtime.Objective("first").state, Is.EqualTo(ObjectiveState.Succeeded));
            Assert.That(runtime.Objective("second").progress, Is.Zero);
            Step(runtime, 2, new MissionEvent("c", "target.destroyed"));
            Assert.That(runtime.State, Is.EqualTo(MissionState.Succeeded)); Assert.That(runtime.SubscriptionCount, Is.Zero);
        }
        [Test] public void DuplicateTargetsAndDuplicateEventsDoNotInflateProgressAcrossLoad()
        {
            var node = Event("destroy", "destroyed"); node.required = 5; node.uniqueTargets = true;
            var graph = new MissionGraph(Definition(node)); using var runtime = new MissionRuntime(graph);
            Step(runtime, 1, new MissionEvent("a", "destroyed", "target.a"), new MissionEvent("b", "destroyed", "target.a"));
            using var loaded = new MissionRuntime(graph, restore: JsonConvert.DeserializeObject<MissionSnapshot>(JsonConvert.SerializeObject(runtime.Capture())));
            Step(loaded, 2, new MissionEvent("c", "destroyed", "target.a"), new MissionEvent("d", "destroyed", "target.b"));
            Assert.That(loaded.Objective("destroy").progress, Is.EqualTo(2));
        }
        [Test] public void ReplayedEventCannotCompleteTheNextStage()
        {
            using var runtime = new MissionRuntime(new MissionGraph(Definition(Event("first", "event"), Event("second", "event", "first"))));
            Step(runtime, 1, new MissionEvent("a", "event")); Step(runtime, 2, new MissionEvent("a", "event"));
            Assert.That(runtime.Objective("second").progress, Is.Zero);
            Step(runtime, 3, new MissionEvent("b", "event")); Assert.That(runtime.State, Is.EqualTo(MissionState.Succeeded));
        }
        [Test] public void ChangedContentAtSameVersionCannotSilentlyRestore()
        {
            var definition = Definition(Event("first", "event"));
            using var runtime = new MissionRuntime(new MissionGraph(definition));
            definition.objectives[0].required = 2;
            Assert.Throws<ArgumentException>(() => new MissionRuntime(new MissionGraph(definition), restore: runtime.Capture()));
        }
        [Test] public void ObjectiveDeclarationOrderDoesNotChangeContentIdentity()
        {
            var definition = Definition(Event("first", "event"), Event("second", "event", "first"));
            string hash = new MissionGraph(definition).ContentHash;
            Array.Reverse(definition.objectives);
            Assert.That(new MissionGraph(definition).ContentHash, Is.EqualTo(hash));
        }
        [Test] public void ActiveMissionSurvivesActualUnityProfileSerialization()
        {
            var node = Event("destroy", "destroyed"); node.required = 5; node.duration = 50;
            var graph = new MissionGraph(Definition(node)); using var runtime = new MissionRuntime(graph);
            Step(runtime, 1, new MissionEvent("a", "destroyed")); Step(runtime, 2, new MissionEvent("b", "destroyed"));
            var profile = CareerProfileData.Create("career", "Driver"); profile.missions.instances.Add(runtime.Capture());
            string json = JsonUtility.ToJson(profile); Assert.That(CareerSaveCodec.Validate("career", json), Is.EqualTo(CareerProfileData.CurrentVersion));
            using var restored = new MissionRuntime(graph, restore: JsonUtility.FromJson<CareerProfileData>(json).missions.instances[0]);
            Assert.That(restored.Progress("destroy"), Is.EqualTo(2)); Assert.That(restored.ObjectiveElapsed("destroy"), Is.EqualTo(2)); Assert.That(restored.Result, Is.Null);
        }
        [Test] public void OptionalFailureDoesNotFailPrimaryAndBonusesUseSharedConditions()
        {
            var primary = Event("evacuate", "evacuate");
            var optional = Event("bonus", "destroy"); optional.required = 5; optional.optional = true; optional.failure = MissionCondition.Fact("damage");
            var definition = Definition(optional, primary);
            definition.rewards = new[] { new MissionReward { id = "base", cash = 100 }, new MissionReward { id = "bonus", cash = 50, when = MissionCondition.Done("bonus") } };
            using var runtime = new MissionRuntime(new MissionGraph(definition));
            Step(runtime, 1, new MissionEvent("a", "damage", fact: "damage"), new MissionEvent("b", "evacuate"));
            Assert.That(runtime.State, Is.EqualTo(MissionState.Succeeded)); Assert.That(runtime.Result.cash, Is.EqualTo(100));
            Assert.That(runtime.Result.failed, Does.Contain("bonus"));
        }
        [TestCase(false)] [TestCase(true)] public void DeadlineFailureWinsRegardlessOfBatchOrdering(bool reverse)
        {
            var reach = Event("reach", "entered"); reach.duration = 1;
            using var runtime = new MissionRuntime(new MissionGraph(Definition(reach)));
            var events = new[] { new MissionEvent("a", "entered"), new MissionEvent("b", "noise") };
            if (reverse) Array.Reverse(events);
            Step(runtime, 1, events);
            Assert.That(runtime.State, Is.EqualTo(MissionState.Failed)); Assert.That(runtime.Result.failure, Is.EqualTo("TimerExpired"));
        }
        [Test] public void ParallelProtectionFailureWinsOverEvacuation()
        {
            var definition = Definition(Event("evacuate", "evacuate")); definition.failure = MissionCondition.Fact("ally.dead");
            using var runtime = new MissionRuntime(new MissionGraph(definition));
            Step(runtime, 1, new MissionEvent("a", "evacuate"), new MissionEvent("b", "destroyed", fact: "ally.dead"));
            Assert.That(runtime.State, Is.EqualTo(MissionState.Failed));
        }
        [TestCase(1)] [TestCase(2)] public void ExclusiveBranchesConvergeWithoutDuplicatedTail(int route)
        {
            var tunnel = Event("tunnel", "tunnel"); tunnel.branchGroup = "route"; tunnel.branchChoice = "tunnel";
            tunnel.activate = new MissionCondition { kind = MissionConditionKind.FactEquals, key = "route", value = 1 };
            var bridge = Event("bridge", "bridge"); bridge.branchGroup = "route"; bridge.branchChoice = "bridge";
            bridge.activate = new MissionCondition { kind = MissionConditionKind.FactEquals, key = "route", value = 2 };
            var home = Event("home", "home"); home.activate = MissionCondition.Any(MissionCondition.Done("tunnel"), MissionCondition.Done("bridge"));
            using var runtime = new MissionRuntime(new MissionGraph(Definition(tunnel, bridge, home)));
            Step(runtime, 1, new MissionEvent("a", "choice", value: route, fact: "route"));
            Step(runtime, 2, new MissionEvent("b", route == 1 ? "tunnel" : "bridge"));
            Step(runtime, 3, new MissionEvent("c", "home"));
            Assert.That(runtime.State, Is.EqualTo(MissionState.Succeeded));
            Assert.That(runtime.Objective(route == 1 ? "bridge" : "tunnel").state, Is.EqualTo(ObjectiveState.Skipped));
        }
        [Test] public void PauseAndRealClockSemanticsAreExplicit()
        {
            var timer = new MissionObjective { id = "timer", kind = "timer", duration = 10 };
            using var runtime = new MissionRuntime(new MissionGraph(Definition(timer)));
            Step(runtime, 1); runtime.Suspend(true); runtime.Step(2, 50, 50, Array.Empty<MissionEvent>());
            Assert.That(runtime.Objective("timer").elapsed, Is.EqualTo(1));
            runtime.Suspend(false); runtime.Step(3, 9, 9, Array.Empty<MissionEvent>());
            Assert.That(runtime.State, Is.EqualTo(MissionState.Succeeded));
        }
        [Test] public void RealTimeDeadlineExpiresWhileSuspended()
        {
            var node = Event("reach", "enter"); node.duration = 10; node.clock = MissionClock.Real;
            using var runtime = new MissionRuntime(new MissionGraph(Definition(node)));
            runtime.Suspend(true); runtime.Step(1, 0, 10, Array.Empty<MissionEvent>());
            Assert.That(runtime.State, Is.EqualTo(MissionState.Failed));
        }
        [Test] public void CheckpointRetryRestoresCountersClocksAndReconcilesOwnedState()
        {
            var first = Event("first", "first"); var second = Event("second", "second", "first"); second.duration = 5;
            second.actions = new[] { new MissionAction { id = "spawn", kind = "spawn", binding = "ally" } };
            var definition = Definition(first, second); definition.checkpoints = new[] { new MissionCheckpoint { id = "stage.two", when = MissionCondition.Done("first") } };
            var world = new World(); using var runtime = new MissionRuntime(new MissionGraph(definition), actions: world);
            Step(runtime, 1, new MissionEvent("a", "first"));
            string claim = runtime.Capture().claimId;
            for (int i = 0; i < 20; i++)
            {
                runtime.Step(2 + i * 2, 6, 6, Array.Empty<MissionEvent>());
                Assert.That(runtime.State, Is.EqualTo(MissionState.Failed)); Assert.That(world.Count, Is.Zero);
                runtime.RetryCheckpoint(); Assert.That(world.Count, Is.EqualTo(1));
                Assert.That(runtime.Objective("first").state, Is.EqualTo(ObjectiveState.Succeeded));
                Assert.That(runtime.Objective("second").elapsed, Is.Zero);
            }
            Step(runtime, 100, new MissionEvent("b", "second")); Assert.That(runtime.State, Is.EqualTo(MissionState.Succeeded));
            Assert.That(runtime.Capture().claimId, Is.EqualTo(claim)); Assert.That(world.Count, Is.Zero);
        }
        [Test] public void InvalidGraphReferencesCyclesAndUnknownPrimitivesAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new MissionGraph(Definition(Event("one", "event", "missing"))));
            Assert.Throws<ArgumentException>(() => new MissionGraph(Definition(Event("one", "event", "two"), Event("two", "event", "one"))));
            var unknown = Event("one", "event"); unknown.kind = "bespoke";
            Assert.Throws<ArgumentException>(() => new MissionGraph(Definition(unknown)));
        }
        [Test] public void NewerContentNeedsExplicitMigrationAndInvalidSnapshotIsRejected()
        {
            var definition = Definition(Event("one", "event")); using var runtime = new MissionRuntime(new MissionGraph(definition));
            var snapshot = runtime.Capture(); definition.version = 2;
            Assert.Throws<ArgumentException>(() => new MissionRuntime(new MissionGraph(definition), restore: snapshot));
            snapshot.objectives[0].progress = double.NaN;
            Assert.Throws<ArgumentException>(() => MissionRuntime.ValidateSnapshot(snapshot));
        }
        [Test] public void ChangedEventIdentityPayloadInSameStepIsRejectedBeforeMutation()
        {
            using var runtime = new MissionRuntime(new MissionGraph(Definition(Event("one", "event"))));
            Assert.Throws<ArgumentException>(() => Step(runtime, 1, new MissionEvent("a", "event"), new MissionEvent("a", "other")));
            Assert.That(runtime.Objective("one").progress, Is.Zero);
        }
        [Test] public void IrrelevantEventStormDoesNotDeliverToObjectives()
        {
            using var runtime = new MissionRuntime(new MissionGraph(Definition(Event("one", "event"))));
            var inputs = new MissionEvent[1000]; for (int i = 0; i < inputs.Length; i++) inputs[i] = new MissionEvent("noise." + i, "irrelevant");
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) runtime.Step(i + 1, 0, 0, inputs);
            watch.Stop(); TestContext.WriteLine("1,000,000 irrelevant events: " + watch.Elapsed.TotalMilliseconds + " ms (Editor)");
            Assert.That(runtime.EventsDelivered, Is.Zero); Assert.That(runtime.State, Is.EqualTo(MissionState.Active));
        }
        [Test] public void PursuitFactsAreConsumedWithoutSimulatingPolice()
        {
            using var course = new MissionCourse(new[] { Vector3.zero }, 1, 120, pursuit: true);
            course.Advance(20, Vector3.zero, Vector3.zero, 0, pursued: true);
            Assert.That(course.Runtime.Objective("survive").state, Is.EqualTo(ObjectiveState.Succeeded));
            course.Advance(1, Vector3.zero, Vector3.zero, 0, escaped: true);
            Assert.That(course.Outcome, Is.EqualTo(MissionState.Succeeded));
        }
        [Test] public void FailedSettlementDoesNotPayAndRetryPaysOnce()
        {
            var root = new GameObject("Mission settlement");
            try
            {
                var wallet = root.AddComponent<VehicleStoreWallet>(); wallet.SetBalance(100);
                var profile = root.AddComponent<CareerProfileSystem>(); var storage = root.AddComponent<MissionTestStorage>(); profile.SetStorage(storage);
                profile.ConfigureAutomaticPersistence(false, false, false); profile.TryCreateNewProfile(out _);
                using var course = new MissionCourse(new[] { Vector3.zero }, 1, 30, cash: 1500);
                course.Advance(1, Vector3.zero, Vector3.zero, 0);
                storage.Fail = true; Assert.That(profile.TrySettleMission(course.Runtime, out _), Is.False); Assert.That(wallet.Balance, Is.EqualTo(100));
                storage.Fail = false; Assert.That(profile.TrySettleMission(course.Runtime, out var failure), Is.True, failure);
                Assert.That(profile.TrySettleMission(course.Runtime, out failure), Is.True, failure);
                Assert.That(wallet.Balance, Is.EqualTo(1600)); Assert.That(profile.CurrentProfile.missions.claims.Count, Is.EqualTo(1));
                Assert.That(CareerSaveCodec.Validate(profile.ProfileId, storage.Json), Is.EqualTo(CareerProfileData.CurrentVersion));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test] public void VersionTwoMigrationAddsEmptyMissionSectionWithoutRewards()
        {
            var profile = CareerProfileData.Create("career", "Driver"); profile.wallet.balance = 100;
            var json = Newtonsoft.Json.Linq.JObject.Parse(JsonUtility.ToJson(profile)); json["saveVersion"] = 2; json.Remove("missions");
            var migrated = JsonUtility.FromJson<CareerProfileData>(CareerSaveCodec.Migrate("career", json.ToString()));
            Assert.That(migrated.saveVersion, Is.EqualTo(CareerProfileData.CurrentVersion)); Assert.That(migrated.missions.claims, Is.Empty); Assert.That(migrated.wallet.balance, Is.EqualTo(100));
        }
        private sealed class World : IMissionActions { public int Count; public void Reconcile(string claimId, int attempt, IReadOnlyList<MissionAction> desired) { Count = desired.Count; } }
    }
        public sealed class MissionTestStorage : MonoBehaviour, ICareerProfileStorage
        {
            public bool Fail; public string Json;
            public bool TrySave(string id, string json, out string failure) { failure = Fail ? "disk full" : ""; if (!Fail) Json = json; return !Fail; }
            public bool TryLoad(string id, out string json, out string failure) { json = Json; failure = ""; return Json != null; }
        }
}
