using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class VehiclePhysicsLabTrackAnchor : MonoBehaviour
    {
        public VehiclePhysicsLabTrack track;
        public bool drawLabels = true;

        private void OnDrawGizmosSelected()
        {
            if (track == null || !track.IsValid(out _))
            {
                return;
            }

            Gizmos.color = new Color(0.15f, 0.65f, 1f, 0.8f);
            VehiclePhysicsLabTrackSample previous = track.Sample(0f);
            int count = Mathf.Clamp(Mathf.CeilToInt(track.length / 4f), 2, 256);
            for (int i = 1; i <= count; i++)
            {
                VehiclePhysicsLabTrackSample next = track.Sample(track.length * i / count);
                Gizmos.DrawLine(transform.TransformPoint(previous.position), transform.TransformPoint(next.position));
                Gizmos.DrawLine(transform.TransformPoint(previous.position + previous.left * previous.width * 0.5f),
                    transform.TransformPoint(next.position + next.left * next.width * 0.5f));
                Gizmos.DrawLine(transform.TransformPoint(previous.position - previous.left * previous.width * 0.5f),
                    transform.TransformPoint(next.position - next.left * next.width * 0.5f));
                previous = next;
            }
        }
    }
}
