using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum TrafficDriverArchetype { Cautious, Normal, Assertive, Aggressive }

    [Serializable]
    public sealed class TrafficDriverProfile
    {
        [Range(0.7f, 1.2f)] public float speedMultiplier = 1;
        public TrafficDriverArchetype archetype = TrafficDriverArchetype.Normal;
        [Range(0.8f, 3)] public float followingSeconds = 1.8f;
        [Range(1, 4)] public float acceleration = 2;
        [Range(2, 6)] public float comfortableBraking = 3;
        [Range(1, 4)] public float standstillGap = 2;
        [Range(1, 8)] public float jerk = 3;
        [Range(0.1f, 1.5f)] public float reactionSeconds = 0.65f;
        [Range(4, 14)] public float emergencyBraking = 12;
        [Range(0, 1)] public float politeness = 0.4f;
        [Range(2, 8)] public float intersectionGap = 5.5f;
        [Range(5, 60)] public float patienceSeconds = 25;
        [Range(-0.3f, 0.3f)] public float lateralPreference;
        [Range(0, 0.5f)] public float actuatorSeconds;

        public static TrafficDriverProfile FromSeed(int seed)
        {
            var profile = new TrafficDriverProfile(); profile.ApplySeed(seed); return profile;
        }

        public void ApplySeed(int seed)
        {
            var random = new TrafficRandom(seed);
            ApplyTemperament(random.Unit(), (random.Unit() - 0.5f) * 0.4f);
        }
        public void ApplyTemperament(float temperament, float lateral = 0)
        {
            temperament = Mathf.Clamp01(temperament);
            emergencyBraking = 12; actuatorSeconds = 0;
            archetype = temperament < 0.2f ? TrafficDriverArchetype.Cautious : temperament < 0.8f
                ? TrafficDriverArchetype.Normal : temperament < 0.97f ? TrafficDriverArchetype.Assertive : TrafficDriverArchetype.Aggressive;
            speedMultiplier = Mathf.Lerp(0.82f, 1.08f, temperament);
            followingSeconds = Mathf.Lerp(2.3f, 1.35f, temperament);
            acceleration = Mathf.Lerp(1.5f, 3, temperament);
            comfortableBraking = Mathf.Lerp(2.5f, 4, temperament);
            standstillGap = Mathf.Lerp(3, 1.8f, temperament);
            jerk = Mathf.Lerp(2, 4, temperament);
            reactionSeconds = Mathf.Lerp(0.9f, 0.35f, temperament);
            politeness = Mathf.Lerp(0.8f, 0.1f, temperament);
            intersectionGap = Mathf.Lerp(6.5f, 3.8f, temperament);
            patienceSeconds = Mathf.Lerp(35, 15, temperament);
            lateralPreference = Mathf.Clamp(lateral, -0.3f, 0.3f);
        }
        public void CopyFrom(TrafficDriverProfile newValues)
        {
            if (newValues == null) throw new ArgumentNullException(nameof(newValues));
            archetype = newValues.archetype; speedMultiplier = newValues.speedMultiplier;
            followingSeconds = newValues.followingSeconds; acceleration = newValues.acceleration;
            comfortableBraking = newValues.comfortableBraking; standstillGap = newValues.standstillGap;
            jerk = newValues.jerk; reactionSeconds = newValues.reactionSeconds; emergencyBraking = newValues.emergencyBraking;
            politeness = newValues.politeness; intersectionGap = newValues.intersectionGap;
            patienceSeconds = newValues.patienceSeconds; lateralPreference = newValues.lateralPreference; actuatorSeconds = 0;
        }
        /* Population sampling intentionally correlates the behavioral dimensions. */
        public void Vary(float amount)
        {
            speedMultiplier = Mathf.Clamp(speedMultiplier * (1 + amount), 0.7f, 1.2f);
            followingSeconds = Mathf.Clamp(followingSeconds * (1 - amount), 0.8f, 3);
            acceleration = Mathf.Clamp(acceleration * (1 + amount), 1, 4);
            comfortableBraking = Mathf.Clamp(comfortableBraking * (1 + amount), 2, 6);
            reactionSeconds = Mathf.Clamp(reactionSeconds * (1 - amount), 0.1f, 1.5f);
            politeness = Mathf.Clamp01(politeness - amount);
            intersectionGap = Mathf.Clamp(intersectionGap * (1 - amount), 2, 8);
        }
    }

    public enum TrafficDriverState { Cruising, Following, Waiting, Yielding, Recovering, Disabled }

    // Physics supplies observations; the driver owns smooth longitudinal decisions.
    // Distances are bumper clearance, speeds metres/second, time seconds.
    public sealed class TrafficDriver
    {
        private readonly TrafficDriverProfile profile;
        private float acceleration;
        private float shockRemaining;
        private struct Observation { public float time, speed, target, gap, leader; }
        private readonly Observation[] history = new Observation[64];
        private int historyCount, historyHead;
        private float clock, nextObservation;
        public float DesiredAcceleration { get; private set; }
        public float SafetyAcceleration { get; private set; }
        public float FinalAcceleration => acceleration;
        public float DesiredGap { get; private set; }
        public float TimeToCollision { get; private set; }
        public TrafficDriverState State { get; private set; }
        public bool Braking { get; private set; }
        public TrafficDriver(TrafficDriverProfile settings) { profile = settings ?? new TrafficDriverProfile(); }
        public void Reset()
        {
            acceleration = shockRemaining = clock = nextObservation = 0; historyCount = historyHead = 0;
            DesiredAcceleration = SafetyAcceleration = DesiredGap = 0; TimeToCollision = float.PositiveInfinity;
            State = TrafficDriverState.Cruising; Braking = false;
        }
        public void NotifyCollision(float impactSpeed)
        { if (impactSpeed >= 3) shockRemaining = Mathf.Max(shockRemaining, Mathf.Clamp(impactSpeed * 0.35f, 1.5f, 6)); }

        public float Step(float dt, float speed, float desiredSpeed, float gap, float leadSpeed, bool emergencyNearby)
        {
            speed = Finite(speed) ? Mathf.Max(0, speed) : 0;
            if (!Finite(dt) || dt <= 0) return speed;
            dt = Mathf.Min(dt, 0.1f);
            desiredSpeed = Finite(desiredSpeed) ? Mathf.Max(0, desiredSpeed) : 0;
            gap = float.IsPositiveInfinity(gap) ? gap : Finite(gap) ? Mathf.Max(0, gap) : 0;
            leadSpeed = Finite(leadSpeed) ? Mathf.Max(0, leadSpeed) : 0;
            clock += dt;
            shockRemaining = Mathf.Max(0, shockRemaining - dt);
            State = shockRemaining > 0 ? TrafficDriverState.Recovering
                : emergencyNearby ? TrafficDriverState.Yielding
                : gap < 60 ? (speed < 0.4f && leadSpeed < 0.4f ? TrafficDriverState.Waiting : TrafficDriverState.Following)
                : TrafficDriverState.Cruising;
            if (shockRemaining > 0) desiredSpeed = 0;
            else if (emergencyNearby) desiredSpeed = Mathf.Min(desiredSpeed, 3);
            var current = new Observation { time = clock, speed = speed, target = desiredSpeed, gap = gap, leader = leadSpeed };
            if (historyCount == 0 || clock >= nextObservation)
            {
                history[historyHead] = current; historyHead = (historyHead + 1) % history.Length;
                historyCount = Mathf.Min(history.Length, historyCount + 1); nextObservation = clock + 0.05f;
            }
            int oldest = (historyHead - historyCount + history.Length) % history.Length;
            Observation perceived = history[oldest];
            float perceiveAt = clock - Safe(profile.reactionSeconds, 0.1f, 1.5f, 0.65f);
            for (int i = 1; i < historyCount; i++)
            {
                Observation sample = history[(oldest + i) % history.Length];
                if (sample.time > perceiveAt) break;
                perceived = sample;
            }
            float comfortable = Safe(profile.comfortableBraking, 1, 6, 3);
            float maxAcceleration = Safe(profile.acceleration, 0.5f, 4, 2);
            float maximumBrake = Safe(profile.emergencyBraking, 4, 14, 12);
            DesiredAcceleration = CarFollowingAcceleration(perceived.speed, perceived.target, perceived.gap, perceived.leader, profile);
            if (desiredSpeed < 0.01f) DesiredAcceleration = -comfortable;
            float requested = Mathf.Clamp(DesiredAcceleration, -comfortable, maxAcceleration);
            float smooth = Mathf.MoveTowards(acceleration, requested, Safe(profile.jerk, 1, 8, 3) * dt);
            // Fresh safety observations bypass normal reaction delay and comfort jerk.
            // Assumes a bounded leader deceleration; this cannot prevent an already unavoidable impact.
            float safeSpeed = SafeFollowingSpeed(gap, leadSpeed, maximumBrake, 14, dt + Safe(profile.actuatorSeconds, 0, 0.5f, 0),
                Safe(profile.standstillGap, 1, 4, 2));
            SafetyAcceleration = float.IsPositiveInfinity(safeSpeed) ? maxAcceleration : (safeSpeed - speed) / dt;
            acceleration = Mathf.Clamp(Mathf.Min(smooth, SafetyAcceleration), -maximumBrake, maxAcceleration);
            float closing = speed - leadSpeed;
            TimeToCollision = closing > 0.01f ? gap / closing : float.PositiveInfinity;
            DesiredGap = Safe(profile.standstillGap, 1, 4, 2) + speed * Safe(profile.followingSeconds, 0.8f, 3, 1.8f);
            Braking = acceleration < -0.3f || desiredSpeed < 0.1f;
            return Mathf.Max(0, speed + acceleration * dt);
        }

        public static float CarFollowingAcceleration(float speed, float target, float gap, float leader, TrafficDriverProfile driver)
        {
            driver = driver ?? new TrafficDriverProfile();
            float a = Safe(driver.acceleration, 0.5f, 4, 2), b = Safe(driver.comfortableBraking, 1, 6, 3);
            if (!Finite(speed) || !Finite(target) || !Finite(leader) || float.IsNaN(gap)) return -b;
            speed = Mathf.Clamp(speed, 0, 200); leader = Mathf.Clamp(leader, 0, 200);
            if (target < 0.01f) return -b;
            float desired = Safe(driver.standstillGap, 1, 4, 2) + Mathf.Max(0,
                speed * Safe(driver.followingSeconds, 0.8f, 3, 1.8f) + speed * (speed - leader) / (2 * Mathf.Sqrt(a * b)));
            float interaction = float.IsPositiveInfinity(gap) ? 0 : Mathf.Pow(desired / Mathf.Max(0.1f, gap), 2);
            return a * (1 - Mathf.Pow(speed / Mathf.Max(0.5f, target), 4) - interaction);
        }

        public static float SafeFollowingSpeed(float gap, float leaderSpeed, float followerBrake, float leaderBrake, float reaction, float standstill)
        {
            if (float.IsPositiveInfinity(gap)) return float.PositiveInfinity;
            if (!Finite(gap) || !Finite(leaderSpeed)) return 0;
            float b = Safe(followerBrake, 0.1f, 30, 4), leadB = Safe(leaderBrake, 0.1f, 30, 12);
            float delay = Safe(reaction, 0, 3, 0.1f), lead = Mathf.Max(0, leaderSpeed);
            float reserve = Mathf.Max(0, gap - Safe(standstill, 0, 10, 2));
            return Mathf.Max(0, Mathf.Sqrt(b * b * delay * delay + b * lead * lead / leadB + 2 * b * reserve) - b * delay);
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static float Safe(float value, float min, float max, float fallback) => Finite(value) ? Mathf.Clamp(value, min, max) : fallback;
    }
}
