using UnityEngine;

namespace NfsMwRemaster.Driving
{
    internal sealed class CameraFollowModule
    {
        private bool initialized;
        private Vector3 position, previousTarget, positionVelocity, lookAhead;
        private Quaternion orbit, rotation;
        public void Reset() { initialized = false; positionVelocity = lookAhead = Vector3.zero; }
        public VehicleCameraPose Resolve(VehicleCameraProfile.View p, in VehicleCameraFrame f, bool cockpit,
            bool lookEnabled, float dt, in CameraContributions c)
        {
            Quaternion horizon = Quaternion.Euler(0, f.rotation.eulerAngles.y, 0);
            Quaternion desiredOrbit = horizon * Quaternion.Euler(0, c.angles.y, 0);
            if (!initialized) { orbit = desiredOrbit; previousTarget = f.position; }
            orbit = Quaternion.Slerp(orbit, desiredOrbit, CameraResponse.Blend(f.grounded ? p.follow.yawResponse : p.follow.airborneResponse, dt));
            Vector3 direction = horizon * Vector3.forward;
            Vector3 travel = Vector3.ProjectOnPlane(f.velocity, Vector3.up);
            if (travel.sqrMagnitude > .01f) travel.Normalize(); else travel = direction;
            Vector3 aim = direction * p.follow.forwardInfluence + travel * p.follow.velocityInfluence;
            if (aim.sqrMagnitude > .001f) aim.Normalize(); else aim = direction;
            float ahead = lookEnabled ? p.follow.lookAheadDistance * Mathf.Clamp01(CameraResponse.Curve(p.follow.lookAheadSpeed, f.speedKph)) : 0;
            lookAhead = Vector3.Lerp(lookAhead, aim * ahead, CameraResponse.Blend(p.follow.lookAheadResponse, dt));
            Vector3 origin = f.position + Vector3.up * p.follow.targetHeight;
            Vector3 offset = Vector3.ClampMagnitude(c.offset, Mathf.Max(0, p.maximumPositionOffset));
            Vector3 desired = cockpit ? f.cockpitPosition + f.cockpitRotation * offset :
                f.position + orbit * new Vector3(offset.x, p.follow.height - c.heightReduction + offset.y, -p.follow.distance - c.extraDistance + offset.z);
            if (!initialized) position = desired;
            // Translation feed-forward avoids several metres of accidental follow lag at motorway speeds.
            if (!cockpit)
            {
                position += f.position - previousTarget;
                position = Vector3.SmoothDamp(position, desired, ref positionVelocity, Mathf.Max(.01f, p.follow.positionSmoothTime), Mathf.Infinity, dt);
                position = desired + Vector3.ClampMagnitude(position - desired, Mathf.Max(0, p.follow.maximumFollowLag));
            }
            else position = desired;
            Quaternion desiredRotation;
            if (cockpit)
            {
                Quaternion anchor = f.cockpitRotation;
                if (!f.grounded) anchor = Quaternion.Slerp(anchor, Quaternion.Euler(0, anchor.eulerAngles.y, 0), p.follow.airborneHorizon);
                desiredRotation = anchor * Quaternion.Euler(0, c.angles.y, 0);
            }
            else desiredRotation = Quaternion.LookRotation(origin + direction * 3.2f + lookAhead - position, Vector3.up);
            if (!initialized) rotation = desiredRotation;
            rotation = Quaternion.Slerp(rotation, desiredRotation, CameraResponse.Blend(f.grounded ? p.follow.yawResponse : p.follow.airborneResponse, dt));
            Quaternion finalRotation = rotation * Quaternion.Euler(Mathf.Clamp(c.angles.x, -p.maximumPitch, p.maximumPitch),
                cockpit ? 0 : c.angles.y * .15f, Mathf.Clamp(c.angles.z, -p.maximumRoll, p.maximumRoll));
            initialized = true; previousTarget = f.position;
            return new VehicleCameraPose { position = position, rotation = finalRotation, collisionOrigin = cockpit ? f.cockpitPosition : origin,
                debug = new VehicleCameraDiagnostics { distance = Vector3.Distance(position, origin), lag = offset.magnitude + Vector3.Distance(position, desired), lookAhead = lookAhead.magnitude } };
        }
    }
}
