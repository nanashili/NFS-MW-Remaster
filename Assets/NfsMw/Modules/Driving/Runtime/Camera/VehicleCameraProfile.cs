using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Flags]
    public enum VehicleCameraEffects
    {
        None = 0, SpeedFov = 1, SpeedDistance = 2, Acceleration = 4, Steering = 8,
        Drift = 16, Shake = 32, RoadMotion = 64, Nitrous = 128, Landing = 256,
        LookAhead = 512, Collision = 1024, MotionBlur = 2048, Peripheral = 4096,
        All = 8191
    }

    public enum VehicleCameraStyle { MostWantedInspired, Cinematic, Arcade, Realistic, FirstPerson, Drift, Custom }
    public enum VehicleCameraFovAxis { Vertical, Horizontal }

    /// <summary>Original tuning inspired by visible racing-camera effects; not recovered NFS coefficients.</summary>
    [CreateAssetMenu(menuName = "Racing Tools/Vehicles/Camera Profile")]
    public sealed class VehicleCameraProfile : ScriptableObject
    {
        public const string DefaultResource = "VehicleCamera/MostWantedInspired";
        public VehicleCameraStyle style;
        public VehicleCameraFovAxis fovAxis;
        public VehicleCameraEffects effects = VehicleCameraEffects.All & ~VehicleCameraEffects.Peripheral;
        public View chase = new View();
        public View cockpit = View.Cockpit();
        public Collision collision = new Collision();
        public PostProcessing postProcessing = new PostProcessing();

        [Serializable]
        public sealed class View
        {
            public Follow follow = new Follow();
            public Speed speed = new Speed();
            public Acceleration acceleration = new Acceleration();
            public Cornering cornering = new Cornering();
            public Drift drift = new Drift();
            public Shake shake = new Shake();
            public Road road = new Road();
            public Nitrous nitrous = new Nitrous();
            public Landing landing = new Landing();
            [Min(0)] public float maximumPositionOffset = 1.5f;
            [Range(0, 15)] public float maximumPitch = 5, maximumRoll = 2;
            public static View Cockpit()
            {
                var v = new View();
                v.speed.minimumFov = 65; v.speed.maximumFov = 82;
                v.speed.speedToFov = Curve(0, 65, 80, 67, 160, 70, 220, 73, 300, 76);
                v.speed.maximumExtraDistance = 0; v.speed.heightReduction = 0;
                v.acceleration.forwardLag = .055f; v.acceleration.brakingCompression = .045f;
                v.acceleration.lateralLag = .045f; v.acceleration.maximumOffset = .09f;
                v.acceleration.fovStrength = .32f; v.acceleration.maximumAdditionalFov = 2;
                v.acceleration.pitchStrength = .4f; v.acceleration.brakingFov = .8f;
                v.cornering.yawStrength = 1.2f; v.cornering.lateralOffset = .018f; v.cornering.roll = 0;
                v.drift.yawInfluence = .045f; v.drift.horizontalOffset = .02f; v.drift.additionalFov = .5f;
                v.shake.positionAmplitude = new Vector3(.002f, .003f, .001f);
                v.shake.rotationAmplitude = new Vector3(.10f, .06f, .04f);
                v.road.positionAmplitude = .004f; v.road.rotationAmplitude = .12f;
                v.road.suspensionInfluence = .018f;
                v.nitrous.fovBoost = 3; v.nitrous.pullback = .018f; v.nitrous.additionalLag = .015f;
                v.nitrous.shakeBoost = .2f;
                v.landing.positionImpulse = .035f; v.landing.pitchImpulse = .6f;
                v.maximumPositionOffset = .12f; v.maximumPitch = 1.8f; v.maximumRoll = .8f;
                return v;
            }
        }
        [Serializable] public sealed class Follow
        {
            [Min(.1f)] public float distance = 5.3f;
            public float height = 1.75f, targetHeight = .75f;
            [Min(.01f)] public float positionSmoothTime = .065f;
            [Min(0)] public float yawResponse = 12, airborneResponse = 2.5f;
            [Range(0, 1)] public float airborneHorizon = .9f;
            [Min(0)] public float maximumFollowLag = .65f;
            [Min(0)] public float lookAheadDistance = 10;
            public AnimationCurve lookAheadSpeed = Curve(0, 0, 80, .25f, 160, .55f, 250, .9f, 320, 1);
            [Range(0, 1)] public float velocityInfluence = .65f, forwardInfluence = .35f;
            [Min(.01f)] public float lookAheadResponse = 5;
        }
        [Serializable] public sealed class Speed
        {
            [Range(30, 110)] public float minimumFov = 65, maximumFov = 90;
            public AnimationCurve speedToFov = Curve(0, 65, 80, 68, 160, 73, 220, 78, 300, 82, 360, 84);
            [Min(.01f)] public float expansionResponse = 4, contractionResponse = 2.5f;
            [Min(0)] public float maximumExtraDistance = 1.1f;
            public AnimationCurve speedToDistance = Curve(0, 0, 80, .1f, 160, .35f, 250, .8f, 320, 1);
            [Min(.01f)] public float distanceResponse = 4;
            [Range(0, 1)] public float heightReduction = .3f;
        }
        [Serializable] public sealed class Acceleration
        {
            [Tooltip("Metres per second squared; excludes gravity and reset discontinuities.")]
            [Min(0)] public float threshold = 1.2f;
            [Min(0)] public float fovStrength = .65f, maximumAdditionalFov = 4;
            [Min(.01f)] public float attackResponse = 10, recoveryResponse = 3;
            [Tooltip("Maximum response at one g of measured acceleration.")]
            [Min(0)] public float forwardLag = .6f, lateralLag = .14f, brakingCompression = .55f;
            [Min(.01f)] public float lagRecovery = 5;
            [Min(0)] public float maximumOffset = .8f, pitchStrength = 1.7f, brakingFov = 1.5f, brakingShake = .12f;
        }
        [Serializable] public sealed class Cornering
        {
            [Tooltip("Degrees of camera yaw at the response curve maximum.")]
            [Min(0)] public float yawStrength = 3, lateralOffset = .16f;
            [Range(0, 4)] public float roll = .65f;
            [Min(.01f)] public float response = 6;
            public AnimationCurve speedResponse = Curve(0, 0, 40, .45f, 100, .8f, 180, 1, 320, .7f);
            public AnimationCurve yawRateResponse = Curve(0, 0, 10, .3f, 35, 1, 90, 1);
            public AnimationCurve steeringAngleResponse = Curve(0, 0, 10, .3f, 30, 1);
        }
        [Serializable] public sealed class Drift
        {
            [Min(0)] public float minimumSpeed = 35;
            [Range(1, 80)] public float slipThreshold = 9, fullSlipAngle = 32;
            public AnimationCurve blend = AnimationCurve.EaseInOut(0, 0, 1, 1);
            [Min(.01f)] public float attackResponse = 4, recoveryResponse = 2.5f;
            [Range(0, 1)] public float yawInfluence = .28f;
            [Min(0)] public float horizontalOffset = .5f, additionalLag = .22f, additionalFov = 1.8f;
        }
        [Serializable] public sealed class Shake
        {
            public Vector3 positionAmplitude = new Vector3(.007f, .011f, .004f);
            public Vector3 rotationAmplitude = new Vector3(.18f, .10f, .12f);
            [Min(.1f)] public float noiseFrequency = 13;
            [Range(0, 2)] public float maximumStrength = 1;
            public AnimationCurve speedCurve = Curve(0, 0, 80, 0, 160, .16f, 220, .4f, 300, .8f, 360, 1);
        }
        [Serializable] public sealed class Road
        {
            [Min(0)] public float positionAmplitude = .015f, rotationAmplitude = .22f;
            [Min(0)] public float suspensionInfluence = .065f;
            [Min(.1f)] public float frequency = 9;
            public AnimationCurve speedCurve = Curve(0, 0, 30, .2f, 100, .65f, 220, 1);
            [Range(0, 1)] public float normalInfluence = .12f;
        }
        [Serializable] public sealed class Nitrous
        {
            [Min(0)] public float fovBoost = 7, pullback = .45f, additionalLag = .15f, shakeBoost = .35f;
            [Min(.01f)] public float attackResponse = 12, recoveryResponse = 3;
        }
        [Serializable] public sealed class Landing
        {
            [Min(0)] public float minimumAirTime = .12f, minimumImpactSpeed = 1.5f, fullImpactSpeed = 12;
            [Min(0)] public float positionImpulse = .15f, pitchImpulse = 1.5f;
            [Min(.01f)] public float recoveryResponse = 10;
        }
        [Serializable] public sealed class Collision
        {
            public LayerMask mask = ~0;
            [Min(.01f)] public float radius = .22f, padding = .08f;
            [Min(.01f)] public float recoveryResponse = 8;
            public bool cockpit = true;
        }
        [Serializable] public sealed class PostProcessing
        {
            [Min(0)] public float speedThreshold = 100;
            [Range(0, .4f)] public float maximumMotionBlur = .18f, nitrousAdditionalBlur = .08f;
            public AnimationCurve speedResponse = Curve(0, 0, 100, 0, 160, .25f, 250, .75f, 320, 1);
            [Range(0, .1f)] public float peripheralChromatic = .025f;
            [Range(0, 1)] public float cockpitScale = .45f;
        }
        internal static AnimationCurve Curve(params float[] pairs)
        {
            var keys = new Keyframe[pairs.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1]);
            return new AnimationCurve(keys);
        }
    }
}
