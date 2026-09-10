using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NfsMwRemaster.Driving
{
    public enum MissionState { Active, Suspended, Succeeded, Failed, Aborted }
    public enum ObjectiveState { Inactive, Active, Succeeded, Failed, Skipped, Cancelled }
    public enum MissionClock { Mission, Game, Real }
    public enum MissionConditionKind { All, Any, Not, FactAtLeast, FactEquals, ObjectiveIs, ProgressAtLeast, RemainingAtMost, Career }

    [Serializable] public sealed class MissionCondition
    {
        public MissionConditionKind kind;
        public string key = "";
        public double value = 1;
        public ObjectiveState state = ObjectiveState.Succeeded;
        public MissionCondition[] children = Array.Empty<MissionCondition>();
        public CareerRequirementDefinition career;
        public static MissionCondition Done(string id) => new MissionCondition { kind = MissionConditionKind.ObjectiveIs, key = id };
        public static MissionCondition Fact(string key, double value = 1) => new MissionCondition { kind = MissionConditionKind.FactAtLeast, key = key, value = value };
        public static MissionCondition All(params MissionCondition[] children) => new MissionCondition { kind = MissionConditionKind.All, children = children };
        public static MissionCondition Any(params MissionCondition[] children) => new MissionCondition { kind = MissionConditionKind.Any, children = children };
        public static MissionCondition Not(MissionCondition child) => new MissionCondition { kind = MissionConditionKind.Not, children = new[] { child } };
    }

    [Serializable] public sealed class MissionObjective
    {
        public string id = "", title = "", parent = "", marker = "";
        public string kind = "event", eventType = "", target = "";
        public string[] dependencies = Array.Empty<string>();
        public string[] sequence = Array.Empty<string>();
        public string branchGroup = "", branchChoice = "";
        public bool optional, uniqueTargets, accumulate, resetWhenFalse;
        public double required = 1, duration;
        public MissionClock clock;
        public double[] warnings = Array.Empty<double>();
        public MissionCondition activate = MissionCondition.All(), success, failure;
        public string failureReason = "ObjectiveFailed";
        public MissionAction[] actions = Array.Empty<MissionAction>();
    }

    [Serializable] public sealed class MissionAction
    {
        public string id = "", kind = "", binding = "", value = "";
    }
    [Serializable] public sealed class MissionCheckpoint
    {
        public string id = "";
        public MissionCondition when = MissionCondition.All();
    }
    [Serializable] public sealed class MissionReward
    {
        public string id = "";
        public int cash;
        public long reputation;
        public EconomyGrant[] grants = Array.Empty<EconomyGrant>();
        public MissionCondition when = MissionCondition.All();
    }
    [Serializable] public sealed class MissionDefinition
    {
        public string id = "", title = "";
        public int version = 1;
        public CareerRequirementDefinition availability;
        public MissionObjective[] objectives = Array.Empty<MissionObjective>();
        public MissionCheckpoint[] checkpoints = Array.Empty<MissionCheckpoint>();
        public MissionReward[] rewards = Array.Empty<MissionReward>();
        public MissionCondition success, failure;
        public string failureReason = "MissionFailed";
    }

    // Immutable value inputs. A step is the transaction boundary; IDs order simultaneous facts.
    [Serializable] public struct MissionEvent
    {
        public string id, type, target, fact;
        public double value;
        public MissionEvent(string id, string type, string target = "", double value = 1, string fact = "")
        { this.id = id; this.type = type; this.target = target; this.value = value; this.fact = fact; }
    }
    [Serializable] public sealed class MissionObjectiveSnapshot
    {
        public string id = "";
        public ObjectiveState state;
        public double progress, elapsed, completedAt;
        public string failure = "";
        public List<string> seen = new List<string>();
        public List<double> warned = new List<double>();
    }
    [Serializable] public sealed class MissionValue { public string key = ""; public double value; }
    [Serializable] public sealed class MissionBranch { public string group = "", choice = ""; }
    [Serializable] public sealed class MissionResult
    {
        public string missionId = "", claimId = "", failure = "";
        public MissionState outcome;
        public double duration;
        public int attempts, cash;
        public long reputation;
        public List<EconomyGrant> grants = new List<EconomyGrant>();
        public List<string> completed = new List<string>(), failed = new List<string>(), bonuses = new List<string>();
    }
    [Serializable] public sealed class MissionSnapshot
    {
        public int schema = 1, contentVersion = 1, attempt = 1;
        public string missionId = "", claimId = "", checkpointId = "", checkpointJson = "";
        public string definitionHash = "";
        public MissionState state;
        public double elapsed;
        public long step;
        public List<MissionObjectiveSnapshot> objectives = new List<MissionObjectiveSnapshot>();
        public List<MissionValue> facts = new List<MissionValue>();
        public List<MissionBranch> branches = new List<MissionBranch>();
        public List<string> checkpoints = new List<string>();
        public List<string> consumedEvents = new List<string>();
        public MissionResult result;
        public bool hasResult;
    }
    [Serializable] public sealed class MissionClaim
    {
        public string claimId = "", missionId = "", fingerprint = "";
        public int cash;
    }
    [Serializable] public sealed class CareerMissionData
    {
        public int version = 1;
        public List<MissionSnapshot> instances = new List<MissionSnapshot>();
        public List<MissionClaim> claims = new List<MissionClaim>();
    }

    public interface IMissionActions
    {
        // Idempotent desired state. Must remove obsolete mission-owned bindings before restoring.
        // Implementations must throw on missing bindings, never silently skip commands.
        void Reconcile(string claimId, int attempt, IReadOnlyList<MissionAction> desired);
    }
    public interface IMissionPrimitive
    {
        void Validate(MissionObjective definition);
        double Apply(MissionObjective definition, double progress, MissionEvent input);
    }
    public interface IMissionMigration
    {
        MissionSnapshot Migrate(MissionSnapshot oldState, int targetVersion);
    }
    internal static class MissionData
    {
        public static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static void Id(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value != value.Trim()) throw new ArgumentException("Invalid stable mission ID: " + value);
            foreach (char c in value) if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == ':')) throw new ArgumentException("Invalid mission ID character: " + value);
        }
    }
}
