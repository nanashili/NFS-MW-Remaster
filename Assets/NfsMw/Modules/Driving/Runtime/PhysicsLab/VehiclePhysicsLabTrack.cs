using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Driving/Vehicle Physics Lab Track", fileName = "PhysicsLabTrack")]
    public sealed class VehiclePhysicsLabTrack : ScriptableObject
    {
        public const int CurrentSchema = 1;

        [HideInInspector] public int schema = CurrentSchema;
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        public string displayName = "Flat vehicle physics fixture";
        public VehiclePhysicsLabTrackKind kind = VehiclePhysicsLabTrackKind.FlatStraight;
        [Min(10f)] public float length = 240f;
        [Min(2f)] public float width = 18f;
        [Min(5f)] public float radius = 35f;
        [Min(0.1f)] public float slalomAmplitude = 4f;
        [Min(1f)] public float slalomWavelength = 30f;
        [Min(0f)] public float rampHeight = 2f;
        [Min(1f)] public float rampLength = 20f;
        public string surfaceId = "AsphaltDry";
        [Min(0f)] public float surfaceGrip = 1f;
        [Min(0f)] public float surfaceRollingResistance = 1f;

        public VehiclePhysicsLabTrackSample Sample(float distance)
        {
            float safeLength = Mathf.Max(10f, length);
            float clamped = Mathf.Clamp(distance, 0f, safeLength);
            Vector3 position = Vector3.forward * clamped;
            Vector3 forward = Vector3.forward;

            switch (kind)
            {
                case VehiclePhysicsLabTrackKind.ConstantRadius:
                {
                    float safeRadius = Mathf.Max(5f, radius);
                    float angle = clamped / safeRadius;
                    Vector3 center = Vector3.right * safeRadius;
                    position = center + new Vector3(-Mathf.Cos(angle) * safeRadius, 0f, Mathf.Sin(angle) * safeRadius);
                    forward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)).normalized;
                    break;
                }
                case VehiclePhysicsLabTrackKind.Slalom:
                case VehiclePhysicsLabTrackKind.LaneChange:
                {
                    float wavelength = Mathf.Max(1f, slalomWavelength);
                    float phase = clamped / wavelength * Mathf.PI * 2f;
                    float amplitude = Mathf.Max(0f, slalomAmplitude);
                    if (kind == VehiclePhysicsLabTrackKind.LaneChange)
                    {
                        float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clamped / safeLength));
                        position.x = Mathf.Lerp(-amplitude, amplitude, blend);
                        position.z = clamped;
                        float derivative = amplitude * 2f / safeLength;
                        forward = new Vector3(derivative, 0f, 1f).normalized;
                    }
                    else
                    {
                        position.x = Mathf.Sin(phase) * amplitude;
                        position.z = clamped;
                        float derivative = Mathf.Cos(phase) * amplitude * Mathf.PI * 2f / wavelength;
                        forward = new Vector3(derivative, 0f, 1f).normalized;
                    }
                    break;
                }
                case VehiclePhysicsLabTrackKind.Ramp:
                {
                    float rampT = Mathf.Clamp01((clamped - (safeLength - Mathf.Max(1f, rampLength))) / Mathf.Max(1f, rampLength));
                    position.y = Mathf.Max(0f, rampHeight) * rampT;
                    float derivative = rampT > 0f && rampT < 1f
                        ? Mathf.Max(0f, rampHeight) / Mathf.Max(1f, rampLength)
                        : 0f;
                    forward = new Vector3(0f, derivative, 1f).normalized;
                    break;
                }
            }

            Vector3 up = Vector3.up;
            Vector3 left = Vector3.Cross(forward, up).normalized;
            up = Vector3.Cross(left, forward).normalized;
            return new VehiclePhysicsLabTrackSample
            {
                distance = clamped,
                position = position,
                forward = forward,
                left = left,
                up = up,
                width = Mathf.Max(2f, width)
            };
        }

        public bool IsValid(out string failure)
        {
            failure = string.Empty;
            if (schema != CurrentSchema || !Guid.TryParseExact(id, "N", out _))
            {
                failure = "PHYSICS_LAB_TRACK_SCHEMA: migrate the track asset before use.";
                return false;
            }

            if (!Finite(length) || length < 10f || !Finite(width) || width < 2f || !Finite(radius) || radius < 5f
                || !Finite(slalomAmplitude) || !Finite(slalomWavelength) || slalomWavelength < 1f
                || !Finite(rampHeight) || !Finite(rampLength) || rampLength < 1f
                || !Finite(surfaceGrip) || surfaceGrip < 0f || !Finite(surfaceRollingResistance) || surfaceRollingResistance < 0f)
            {
                failure = "PHYSICS_LAB_TRACK_VALUES: length, width, geometry and surface values must be finite and positive.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
