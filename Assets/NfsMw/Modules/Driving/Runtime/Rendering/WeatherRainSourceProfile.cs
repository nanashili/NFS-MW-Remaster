using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// The authored Weatherade rain profile used by the weather presentation.
    /// Keeping the values here gives the scene builder and the runtime adapter
    /// one source of truth for the decoded drop physics.
    /// </summary>
    public static class WeatherRainSourceProfile
    {
        public const float EmitterFootprintMeters = 50f;
        public const float FollowHeightMeters = 20f;
        public const float FallSpeedMetersPerSecond = 10f;
        public const float SwayFrequency = 5f;
        public const float StretchMultiplier = 14f;
        public const float DropSizeMinMeters = .006f;
        public const float DropSizeMaxMeters = .011f;
        public const float LifetimeMinSeconds = 3f;
        public const float LifetimeMaxSeconds = 5f;
        public const float VerticalVelocityScaleMin = .7f;
        public const float VerticalVelocityScaleMax = 1f;
        public const float LateralVelocityXMin = -1f;
        public const float LateralVelocityXMax = 0f;
        public const float LateralVelocityZMin = -2f;
        public const float LateralVelocityZMax = 0f;

        public static void ApplyTo(LocalRain rain)
        {
            if (!rain) return;
            rain.emitterFootprint = EmitterFootprintMeters;
            rain.emitterHeight = FollowHeightMeters;
            rain.fallSpeedMps = FallSpeedMetersPerSecond;
            rain.swayFrequency = SwayFrequency;
            rain.lateralVelocityX = new Vector2(LateralVelocityXMin, LateralVelocityXMax);
            rain.lateralVelocityZ = new Vector2(LateralVelocityZMin, LateralVelocityZMax);
            rain.verticalVelocityScale = new Vector2(VerticalVelocityScaleMin, VerticalVelocityScaleMax);
        }

        public static bool Matches(LocalRain rain)
        {
            return rain
                && Mathf.Approximately(rain.emitterFootprint, EmitterFootprintMeters)
                && Mathf.Approximately(rain.emitterHeight, FollowHeightMeters)
                && Mathf.Approximately(rain.fallSpeedMps, FallSpeedMetersPerSecond)
                && Mathf.Approximately(rain.swayFrequency, SwayFrequency)
                && Mathf.Approximately(rain.lateralVelocityX.x, LateralVelocityXMin)
                && Mathf.Approximately(rain.lateralVelocityX.y, LateralVelocityXMax)
                && Mathf.Approximately(rain.lateralVelocityZ.x, LateralVelocityZMin)
                && Mathf.Approximately(rain.lateralVelocityZ.y, LateralVelocityZMax)
                && Mathf.Approximately(rain.verticalVelocityScale.x, VerticalVelocityScaleMin)
                && Mathf.Approximately(rain.verticalVelocityScale.y, VerticalVelocityScaleMax);
        }

        public static bool Matches(ParticleSystem system)
        {
            if (!system) return false;
            ParticleSystem.MainModule main = system.main;
            return Mathf.Approximately(main.startSize.constantMin, DropSizeMinMeters)
                && Mathf.Approximately(main.startSize.constantMax, DropSizeMaxMeters)
                && Mathf.Approximately(main.startLifetime.constantMin, LifetimeMinSeconds)
                && Mathf.Approximately(main.startLifetime.constantMax, LifetimeMaxSeconds);
        }
    }
}
