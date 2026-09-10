using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Low-level input tracking only. The caller owns tactical choice, pacing and permissions.
    public sealed class RacingLineTracker
    {
        public const string Revision = "trajectory-input.pure-pursuit.1";
        private readonly RacingTrajectoryReader trajectory;
        private readonly RacingControllerSettings settings;
        private readonly float wheelbase;
        private float integral, cueTime;
        private int nextCue;
        private RacingControlCue activeCue;
        public float Station { get; private set; }
        public float LateralError { get; private set; }
        public float HeadingError { get; private set; }
        public float TargetSpeed { get; private set; }
        public bool Finished => Station >= trajectory.Length - 1;
        public RacingLineTracker(RacingTrajectoryReader trajectory, RacingControllerSettings settings, float wheelbase)
        { this.trajectory = trajectory; this.settings = RacingLineSnapshot.Clone(settings); this.wheelbase = wheelbase; }

        public void Reset(float station = 0)
        {
            Station = Mathf.Clamp(station, 0, trajectory.Length); integral = cueTime = 0; nextCue = 0; activeCue = default;
            while (nextCue < trajectory.CueCount && trajectory.Cue(nextCue).station < Station) nextCue++;
        }

        public VehicleInputState Step(Transform pose, Vector3 velocity, Vector3 angularVelocity, VehicleTuning tuning, float dt, float pacing = 1)
        {
            if (!RacingLineSnapshot.Range(dt, 0.0001f, 0.05f) || !RacingLineSnapshot.Finite(velocity))
                return new VehicleInputState { Handbrake = true };
            float speed = Mathf.Max(0, Vector3.Dot(velocity, pose.forward));
            Station = trajectory.Project(pose.position, Station, Mathf.Max(15, speed * 2), out float error);
            LateralError = error;
            var here = trajectory.Sample(Station);
            TargetSpeed = Finished ? 0 : here.targetSpeed * Mathf.Clamp01(pacing);
            if (!Finished && Station < 1 && TargetSpeed < 0.1f)
                TargetSpeed = trajectory.Sample(1).targetSpeed * Mathf.Clamp01(pacing);
            Vector3 aim = pose.InverseTransformPoint(trajectory.Sample(Station + settings.minimumLookahead + speed * settings.lookaheadSeconds).position);
            float angle = Mathf.Atan2(2 * wheelbase * aim.x, Mathf.Max(1, aim.x * aim.x + aim.z * aim.z)) * Mathf.Rad2Deg;
            HeadingError = Vector3.SignedAngle(pose.forward, here.tangent, here.normal);
            float desired = angle / Mathf.Max(1, tuning.controls.maxSteerAngle) - Vector3.Dot(angularVelocity, here.normal) * settings.yawDamping;
            // Invert the shared speed/exponent input shaping, not the chassis physics.
            float scale = Mathf.Lerp(1, tuning.controls.highSpeedSteerScale,
                Mathf.InverseLerp(0, tuning.controls.highSpeedSteerKph, speed * 3.6f));
            float steering = Mathf.Sign(desired) * Mathf.Pow(Mathf.Clamp01(Mathf.Abs(desired) / Mathf.Max(0.1f, scale)),
                1f / Mathf.Max(0.1f, tuning.controls.steeringExponent));
            float speedError = TargetSpeed - speed;
            integral = Mathf.Clamp(integral + speedError * dt, -2, 2);
            float demand = speedError * settings.speedGain + integral * settings.integralGain;
            var input = new VehicleInputState { Steering = steering, Throttle = Mathf.Clamp01(demand), Brake = Mathf.Clamp01(-demand) };
            if (nextCue < trajectory.CueCount && Station >= trajectory.Cue(nextCue).station)
            { activeCue = trajectory.Cue(nextCue++); cueTime = activeCue.seconds; }
            if (cueTime > 0)
            { cueTime -= dt; input.Throttle = 0.25f; input.Brake = activeCue.brake; input.Steering = activeCue.steering; }
            if (Finished || TargetSpeed < 0.1f)
                return new VehicleInputState { Brake = speed > 1.2f ? 1 : 0, Handbrake = speed <= 1.2f };
            if (input.Brake > 0 && speed < 1.2f) { input.Brake = 0; input.Handbrake = true; }
            return input.Clamped();
        }
    }
}
