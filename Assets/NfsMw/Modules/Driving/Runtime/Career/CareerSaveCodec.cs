using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NfsMwRemaster.Driving
{
    public enum SaveError { Missing, InvalidData, UnsupportedVersion, Conflict, StorageFailure }

    public sealed class SaveException : IOException
    {
        public SaveError Error { get; }
        public SaveException(SaveError error, string message) : base(message) { Error = error; }
    }

    /// <summary>Strict staging validation, deliberately separate from runtime Normalize().</summary>
    public static class CareerSaveCodec
    {
        public const int MaximumBytes = 16 * 1024 * 1024;
        public const int MaximumCollection = 100000;
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static JObject Parse(string json)
        {
            if (json == null || json.Length > MaximumBytes || Utf8.GetByteCount(json) > MaximumBytes)
                throw Invalid("Save exceeds the 16 MiB payload limit.");
            try
            {
                using (var input = new StringReader(json))
                using (var reader = new JsonTextReader(input) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
                {
                    var value = JObject.Load(reader, new JsonLoadSettings
                    { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Ignore });
                    if (reader.Read()) throw Invalid("Unexpected data after the JSON object.");
                    CheckBounds(value);
                    return value;
                }
            }
            catch (JsonException exception) { throw Invalid("Malformed save JSON: " + exception.Message); }
        }

        public static int Validate(string slot, string json)
            => Validate(slot, json, out _);

        public static int Validate(string slot, string json, out string displayName)
        {
            JObject root = Parse(json);
            int version = Integer(root, "saveVersion", 1, int.MaxValue);
            if (version > CareerProfileData.CurrentVersion)
                throw new SaveException(SaveError.UnsupportedVersion, "Profile schema is newer than this build; no downgrade is allowed.");
            if (Text(root, "profileId") != slot) throw Invalid("Profile identity does not match the slot.");
            Text(root, "activeVehicleId");
            displayName = (string)Require(root, "playerName", JTokenType.String);
            var wallet = Object(root, "wallet"); Integer(wallet, "balance", 0, int.MaxValue);
            var store = Object(root, "store"); Ids(store, "ownedProductIds"); Ids(store, "ownedVehicleIds");
            var bounty = Object(root, "bounty");
            foreach (string key in new[] { "totalBounty", "currentPursuitBounty", "heatLevel", "awardedPursuitSeconds",
                "policeVehiclesDisabled", "roadblocksDodged", "spikeStripsDodged", "propertyDamageEvents",
                "costToState", "tradePaintEvents", "trafficInfractions", "pursuitsEscaped", "pursuitsBusted" })
                Integer(bounty, key, 0, int.MaxValue);
            Require(bounty, "pursuitActive", JTokenType.Boolean);
            JToken duration = bounty["pursuitDurationSeconds"];
            if (!TryFiniteNumber(duration, out double seconds)
                || seconds < 0 || seconds > float.MaxValue)
                throw Invalid("Invalid pursuit duration.");
            var statistics = Object(root, "statistics");
            foreach (string key in new[] { "racesWon", "racesLost", "eventsCompleted", "totalCostToState" })
                Integer(statistics, key, 0, int.MaxValue);
            var roam = Object(root, "freeRoam"); Ids(roam, "discoveredLocationIds"); Ids(roam, "completedEventIds");
            Require(roam, "safehouseId", JTokenType.String);
            var vehicles = (JArray)Require(root, "vehicles", JTokenType.Array);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken token in vehicles)
            {
                if (!(token is JObject vehicle) || !seen.Add(Text(vehicle, "vehicleId"))) throw Invalid("Invalid or duplicate vehicle.");
                Ids(vehicle, "performanceUpgradeIds"); Ids(vehicle, "customizationIds");
                if (vehicle["schemaVersion"] != null) Integer(vehicle, "schemaVersion", 1, 2);
                OptionalId(vehicle, "vehicleDefinitionId");
                OptionalId(vehicle, "vehicleVariantId");
                if (vehicle["tuningAdjustments"] != null)
                {
                    var adjustments = (JArray)Require(vehicle, "tuningAdjustments", JTokenType.Array);
                    if (adjustments.Count > MaximumCollection) throw Invalid("Vehicle tuning adjustment collection is too large.");
                    foreach (JToken adjustment in adjustments)
                    {
                        if (!(adjustment is JObject record)) throw Invalid("Invalid vehicle tuning adjustment.");
                        Integer(record, "parameter", 0, int.MaxValue);
                        var value = record["value"];
                        if (!TryFiniteNumber(value, out double number) || Math.Abs(number) > 1000000) throw Invalid("Invalid vehicle tuning adjustment value.");
                    }
                }
                foreach (string key in new[] { "paintId", "paintFinishId", "rimFinishId", "glassTintId" })
                    OptionalId(vehicle, key);
            }
            if (version >= 2) ValidateEconomy(slot, Object(root, "economy"), wallet, bounty);
            else if (root["economy"] is JObject legacyEconomy)
            {
                JToken initialized = legacyEconomy["initialized"];
                if (initialized != null && initialized.Type != JTokenType.Boolean) throw Invalid("Invalid legacy economy flag.");
                if (initialized != null && (bool)initialized || legacyEconomy["receipts"] is JArray oldReceipts && oldReceipts.Count != 0)
                    throw Invalid("Version-one profile unexpectedly contains a committed economy journal.");
            }
            if (version >= 4)
            {
                var police = Object(root, "police");
                Require(police, "pendingWorldOutcomeId", JTokenType.String);
                if (Integer(police, "version", 1, int.MaxValue) != 1) throw new SaveException(SaveError.UnsupportedVersion, "Police save schema is newer than this build.");
                var settlements = (JArray)Require(police, "settlements", JTokenType.Array);
                foreach (var token in settlements)
                {
                    if (!(token is JObject record)) throw Invalid("Invalid police settlement record.");
                    Integer(record, "cashPaid", 0, int.MaxValue);
                    var outcome = Object(record, "outcome"); Text(outcome, "encounterId"); Text(outcome, "targetId");
                    Integer(outcome, "kind", 0, 2); Integer(outcome, "engagementLevel", 1, 5);
                    foreach (string key in new[] { "assessedFine", "reputation", "historicalBounty" }) Integer(outcome, key, 0, int.MaxValue);
                }
                try { police.ToObject<CareerPoliceData>().Validate(); }
                catch (ArgumentException exception) { throw Invalid(exception.Message); }
            }
            if (version >= 3)
            {
                var missions = Object(root, "missions");
                if (Integer(missions, "version", 1, int.MaxValue) != 1) throw new SaveException(SaveError.UnsupportedVersion, "Mission save schema is newer than this build.");
                Require(missions, "instances", JTokenType.Array); Require(missions, "claims", JTokenType.Array);
                try { MissionPersistence.Validate(missions.ToObject<CareerMissionData>()); }
                catch (ArgumentException exception) { throw Invalid(exception.Message); }
                if (roam["missionResume"] != null && roam["missionResume"].Type != JTokenType.Null)
                {
                    var resume = Object(roam, "missionResume");
                    if (!(bool)Require(resume, "active", JTokenType.Boolean)) return version;
                    Text(resume, "eventId"); Text(resume, "claimId");
                    foreach (var pair in new[] { ("position", new[] { "x", "y", "z" }), ("rotation", new[] { "x", "y", "z", "w" }) })
                        foreach (string axis in pair.Item2)
                        {
                            var component = Object(resume, pair.Item1)[axis];
                            if (component == null || (component.Type != JTokenType.Float && component.Type != JTokenType.Integer)
                                || !MissionData.Finite((double)component) || Math.Abs((double)component) > 1000000) throw Invalid("Invalid mission resume pose.");
                        }
                    var countdown = resume["countdown"];
                    if (countdown == null || (countdown.Type != JTokenType.Float && countdown.Type != JTokenType.Integer)
                        || !MissionData.Finite((double)countdown) || (double)countdown < 0 || (double)countdown > 3) throw Invalid("Invalid mission countdown.");
                }
            }
            if (root["weather"] != null && root["weather"].Type != JTokenType.Null)
                ValidateWeather(Object(root, "weather"));
            return version;
        }

        private static void ValidateWeather(JObject weather)
        {
            int schema = Integer(weather, "schemaVersion", 1, int.MaxValue);
            if (schema > 1) throw new SaveException(SaveError.UnsupportedVersion, "Weather save schema is newer than this build.");
            Integer(weather, "seed", 0, int.MaxValue);
            Unsigned(weather, "simulationRandomState");
            Unsigned(weather, "cosmeticRandomState");
            if (weather["lightningSequence"] != null) Unsigned(weather, "lightningSequence");
            Float(weather, "simulationSeconds", 0, 604800000);
            Float(weather, "timeOfDaySeconds", 0, 86400);
            Float(weather, "fixedAccumulator", 0, 10);
            Float(weather, "clockTimeScale", 0, 100);
            Float(weather, "weatherTimeScale", 0, 1000);
            Text(weather, "currentPresetId"); Text(weather, "targetPresetId");
            ValidateWeatherValues(Object(weather, "currentValues"));
            ValidateWeatherValues(Object(weather, "transitionStartValues"));
            ValidateWeatherValues(Object(weather, "targetValues"));
            Float(weather, "transitionElapsed", 0, 604800);
            Float(weather, "transitionDuration", 0, 604800);
            Float(weather, "settledRemaining", 0, 604800);
            Float(weather, "surfaceWetness", 0, 1); Float(weather, "standingWater", 0, 1);
            Float(weather, "nextLightningSimulationSeconds", -1, 604800000);
            Require(weather, "automaticWeather", JTokenType.Boolean); Require(weather, "paused", JTokenType.Boolean);
            if (weather["recentHistoryCount"] != null)
                Integer(weather, "recentHistoryCount", 0, 8);
            if (weather["recentHistory"] is JArray history)
            {
                if (history.Count > 8) throw Invalid("Weather history exceeds its bound.");
                foreach (JToken id in history)
                    if (id.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)id) || ((string)id).Length > 200)
                        throw Invalid("Invalid weather history entry.");
            }
            if (weather["transitionCooldowns"] is JArray cooldowns)
            {
                if (cooldowns.Count > 256) throw Invalid("Weather transition cooldowns exceed their bound.");
                foreach (JToken cooldown in cooldowns)
                    if (cooldown.Type != JTokenType.Float && cooldown.Type != JTokenType.Integer)
                        throw Invalid("Invalid weather transition cooldown.");
                    else if (!TryFiniteNumber(cooldown, out double value)
                        || value < 0 || value > 604800)
                        throw Invalid("Invalid weather transition cooldown.");
            }
            JArray pending = (JArray)Require(weather, "pendingLightning", JTokenType.Array);
            if (pending.Count > 32) throw Invalid("Weather lightning queue exceeds its bound.");
            foreach (JToken item in pending)
            {
                JObject strike = item as JObject ?? throw Invalid("Invalid weather lightning event.");
                Integer(strike, "sequence", 0, int.MaxValue); Float(strike, "scheduledSimulationSeconds", 0, 604800000);
                Float(strike, "distanceMeters", 1, 10000); Float(strike, "thunderDelaySeconds", 0, 60); Float(strike, "flashDurationSeconds", .01f, 1);
            }
        }

        private static void ValidateWeatherValues(JObject values)
        {
            Float(values, "temperatureC", -80, 80); Float(values, "humidity", 0, 1); Float(values, "cloudCover", 0, 1);
            Integer(values, "precipitationType", 0, 2); Float(values, "precipitationIntensity", 0, 1);
            Float(values, "windDirectionDegrees", 0, 360); Float(values, "windSpeedMps", 0, 100); Float(values, "gustStrength", 0, 1);
            Float(values, "visibilityMeters", 1, 1000000); Float(values, "fogDensity", 0, 1); Float(values, "electricalActivity", 0, 1);
            Integer(values, "condition", 0, 14);
        }

        private static float Float(JObject value, string key, float min, float max)
        {
            JToken token = value[key];
            if (token == null) throw Invalid("Missing or invalid field: " + key);
            if (!TryFiniteNumber(token, out double number) || number < min || number > max)
                throw Invalid("Out of range: " + key);
            return (float)number;
        }

        private static bool TryFiniteNumber(JToken token, out double number)
        {
            number = 0;
            if (token == null || token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
                return false;
            try
            {
                number = token.Value<double>();
                return !double.IsNaN(number) && !double.IsInfinity(number);
            }
            catch (Exception exception) when (exception is FormatException
                || exception is InvalidCastException
                || exception is OverflowException)
            {
                return false;
            }
        }

        private static uint Unsigned(JObject value, string key)
        {
            JToken token = Require(value, key, JTokenType.Integer);
            if (!ulong.TryParse(token.ToString(), out ulong number) || number > uint.MaxValue) throw Invalid("Invalid unsigned field: " + key);
            return (uint)number;
        }

        public static string Migrate(string slot, string json)
        {
            int version = Validate(slot, json);
            if (version == CareerProfileData.CurrentVersion) return json;
            JObject root = Parse(json);
            // Explicit v1 -> v2. No runtime state, rewards or clocks consulted.
            if (version == 1)
            {
                root["economy"] = JObject.FromObject(new CareerEconomyData());
                root["saveVersion"] = 2;
            }
            // Explicit v2 -> v3: no historical payouts inferred.
            if (version < 3) root["missions"] = JObject.FromObject(new CareerMissionData());
            // Explicit v3 -> v4: preserve missions and historical bounty, infer no encounters or rewards.
            root["police"] = JObject.FromObject(new CareerPoliceData());
            root["saveVersion"] = 4;
            string migrated = root.ToString(Formatting.None);
            Validate(slot, migrated);
            return migrated;
        }

        private static void ValidateEconomy(string slot, JObject economy, JObject wallet, JObject bounty)
        {
            int version = Integer(economy, "version", 1, int.MaxValue);
            if (version > 1) throw new SaveException(SaveError.UnsupportedVersion, "Economy schema is newer than this build.");
            long revision = Long(economy, "revision"); Long(economy, "reputation");
            bool initialized = (bool)Require(economy, "initialized", JTokenType.Boolean);
            long cash = Integer(economy, "openingCash", 0, int.MaxValue);
            long reputation = Long(economy, "openingReputation");
            long banked = Integer(economy, "openingBounty", 0, int.MaxValue);
            Ids(economy, "firstWinEventIds");
            Require(economy, "unlocks", JTokenType.Array); Require(economy, "activities", JTokenType.Array);
            var receipts = (JArray)Require(economy, "receipts", JTokenType.Array);
            long sequence = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken token in receipts)
            {
                if (!(token is JObject r) || !ids.Add(Text(r, "transactionId"))) throw Invalid("Invalid or duplicate economy receipt.");
                if (Text(r, "profileId") != slot) throw Invalid("Receipt belongs to a different profile.");
                Require(r, "fingerprint", JTokenType.String); Long(r, "utcSeconds");
                long next = Long(r, "sequence");
                if (next <= sequence || next > revision || Long(r, "previousCash") != cash
                    || Long(r, "previousReputation") != reputation || Long(r, "previousBounty") != banked)
                    throw Invalid("Broken economy balance chain.");
                long cashDelta = 0, repDelta = 0, bountyDelta = 0;
                foreach (JToken line in (JArray)Require(r, "lines", JTokenType.Array))
                {
                    if (!(line is JObject item)) throw Invalid("Invalid receipt line.");
                    int kind = Integer(item, "kind", 0, (int)EconomyLineKind.Forfeiture);
                    Integer(item, "itemKind", 0, (int)EconomyItemKind.Safehouse);
                    JToken amount = Require(item, "amount", JTokenType.Integer);
                    if (!long.TryParse(amount.ToString(), out long delta)) throw Invalid("Receipt amount overflows.");
                    if (kind == (int)EconomyLineKind.Fine && delta > 0 || kind != (int)EconomyLineKind.Fine
                        && kind != (int)EconomyLineKind.Cash && delta < 0) throw Invalid("Invalid receipt amount sign.");
                    try
                    {
                        if (kind == (int)EconomyLineKind.Cash || kind == (int)EconomyLineKind.Fine) cashDelta = checked(cashDelta + delta);
                        if (kind == (int)EconomyLineKind.Reputation) repDelta = checked(repDelta + delta);
                        if (kind == (int)EconomyLineKind.Bounty) bountyDelta = checked(bountyDelta + delta);
                    }
                    catch (OverflowException) { throw Invalid("Receipt totals overflow."); }
                }
                try
                {
                    if (checked(cash + cashDelta) != Long(r, "newCash") || checked(reputation + repDelta) != Long(r, "newReputation")
                        || checked(banked + bountyDelta) != Long(r, "newBounty")) throw Invalid("Receipt lines do not reconcile.");
                }
                catch (OverflowException) { throw Invalid("Receipt balances overflow."); }
                cash = Integer(r, "newCash", 0, int.MaxValue); reputation = Long(r, "newReputation");
                banked = Integer(r, "newBounty", 0, int.MaxValue); sequence = next;
            }
            if (initialized && (cash != (long)wallet["balance"] || reputation != Long(economy, "reputation")
                || banked != (long)bounty["totalBounty"])) throw Invalid("Profile balances disagree with its economy journal.");
            if (!initialized && (revision != 0 || receipts.Count != 0)) throw Invalid("Uninitialized economy has committed receipts.");
        }

        private static void CheckBounds(JToken token)
        {
            if (token is JContainer container)
            {
                if (container.Count > MaximumCollection) throw Invalid("Save collection exceeds its safety limit.");
                foreach (JToken child in container.Children()) CheckBounds(child);
            }
            else if (token.Type == JTokenType.String && ((string)token).Length > 65536)
                throw Invalid("Save string exceeds its safety limit.");
        }
        private static JToken Require(JObject value, string key, JTokenType type)
        {
            JToken token = value[key];
            if (token == null || token.Type != type) throw Invalid("Missing or invalid field: " + key);
            return token;
        }
        private static JObject Object(JObject value, string key) => (JObject)Require(value, key, JTokenType.Object);
        private static string Text(JObject value, string key)
        {
            string text = (string)Require(value, key, JTokenType.String);
            if (string.IsNullOrWhiteSpace(text) || text.Length > 200) throw Invalid("Invalid identifier: " + key);
            return text;
        }
        private static void OptionalId(JObject value, string key)
        {
            if (value[key] == null) return;
            string text = (string)Require(value, key, JTokenType.String);
            if (text.Length > 200 || text.Length > 0 && string.IsNullOrWhiteSpace(text)) throw Invalid("Invalid optional identifier: " + key);
        }
        private static long Long(JObject value, string key)
        {
            JToken token = Require(value, key, JTokenType.Integer);
            if (!long.TryParse(token.ToString(), out long number) || number < 0) throw Invalid("Invalid nonnegative integer: " + key);
            return number;
        }
        private static int Integer(JObject value, string key, int min, int max)
        { long number = Long(value, key); if (number < min || number > max) throw Invalid("Out of range: " + key); return (int)number; }
        private static void Ids(JObject value, string key)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken token in (JArray)Require(value, key, JTokenType.Array))
                if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token)
                    || ((string)token).Length > 200 || !seen.Add((string)token)) throw Invalid("Invalid or duplicate ID in " + key);
        }
        internal static SaveException Invalid(string message) => new SaveException(SaveError.InvalidData, message);
    }
}
