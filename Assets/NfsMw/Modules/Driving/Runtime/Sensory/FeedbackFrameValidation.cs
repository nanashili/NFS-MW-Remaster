using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Replay ingestion validation; run once on explicit import, never reflected over a hot physics sample.</summary>
    public static class FeedbackFrameValidation
    {
        private static bool F(float value) => SensoryMath.IsFinite(value);
        private static bool V(Vector3 value) => F(value.x) && F(value.y) && F(value.z);
        public static bool Valid(VehicleFeedbackFrame f)
        {
            var q = f.Rotation;
            float norm = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
            if (f.WheelCount < 0 || f.WheelCount > 8 || double.IsNaN(f.Time) || double.IsInfinity(f.Time)) return false;
            if (!V(f.Position) || !V(f.Velocity) || !V(f.LocalVelocity) || !V(f.AccelerationG)
                || !F(norm) || Mathf.Abs(norm - 1) > 0.01f
                || !F(f.Speed) || !F(f.SlipAngle) || !F(f.YawRate) || !F(f.EngineRpm) || !F(f.NormalizedRpm) || !F(f.EngineLoad)
                || !F(f.EngineTorque) || !F(f.DrivetrainTorque) || !F(f.Throttle) || !F(f.Brake)
                || !F(f.Clutch) || !F(f.Boost) || !F(f.BodyDamage) || !F(f.WaterDepth)
                || !F(f.NitrousFlow) || !F(f.NitrousRemaining) || !F(f.FrontSlip) || !F(f.RearSlip)
                || !F(f.Wheelspin) || !F(f.BrakeLock) || !F(f.Drift) || !F(f.SpeedIntensity)
                || !F(f.NitroIntensity) || !F(f.EngineStress) || !F(f.Landing) || !F(f.Scrape)) return false;
            var impact = f.LastImpact;
            if (double.IsNaN(impact.Time) || double.IsInfinity(impact.Time) || !V(impact.Point) || !V(impact.Normal)
                || !V(impact.LocalDirection) || !F(impact.Severity) || !F(impact.NormalSpeed) || !F(impact.TangentSpeed) || !F(impact.Impulse)) return false;
            for (int i = 0; i < f.WheelCount; i++)
            {
                var wheel = f.GetWheel(i);
                if (!V(wheel.Point) || !V(wheel.Normal) || !F(wheel.AngularSpeed) || !F(wheel.LinearSpeed)
                    || !F(wheel.RoadSpeed) || !F(wheel.LongitudinalSlip) || !F(wheel.LateralSlip) || !F(wheel.Load)
                    || !F(wheel.Compression) || !F(wheel.Slip) || !F(wheel.Spin) || !F(wheel.Lock)) return false;
            }
            return true;
        }
    }
}
