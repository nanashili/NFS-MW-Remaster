using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Occurrence-local 3D projection and finite body bounds, shared by planning and measured telemetry.
    public static class RacingCorridorGeometry
    {
        public static RacingCorridorSample Sample(RacingCorridorSample[] samples, float station)
        {
            int i = Index(samples, station); var a = samples[i]; var b = samples[i + 1];
            float t = Mathf.InverseLerp(a.station, b.station, station);
            a.position = Vector3.Lerp(a.position, b.position, t); a.width = Mathf.Lerp(a.width, b.width, t);
            a.left = Vector3.Slerp(a.left, b.left, t).normalized; a.forward = Vector3.Slerp(a.forward, b.forward, t).normalized;
            a.up = Vector3.Slerp(a.up, b.up, t).normalized; a.station = Mathf.Lerp(a.station, b.station, t); return a;
        }
        public static float FootprintClearance(RacingLineSnapshot input, Vector3 position, Quaternion orientation, float station)
        {
            float minimum = float.PositiveInfinity; Vector3 right = orientation * Vector3.right, forward = orientation * Vector3.forward;
            float halfLength = input.Dimensions.z * 0.5f, halfWidth = input.Dimensions.x * 0.5f;
            for (int longitudinal = -1; longitudinal <= 1; longitudinal += 2)
                for (int lateral = -1; lateral <= 1; lateral += 2)
                {
                    Vector3 corner = position + forward * (longitudinal * halfLength) + right * (lateral * halfWidth);
                    var c = Project(input.Corridor, corner, station, input.Dimensions.z + input.Settings.spacing * 2);
                    minimum = Mathf.Min(minimum, c.width * 0.5f - Mathf.Abs(Vector3.Dot(corner - c.position, c.left)));
                }
            var centre = Sample(input.Corridor, station);
            float offset = Vector3.Dot(position - centre.position, centre.left);
            float extent = Mathf.Abs(Vector3.Dot(right, centre.left)) * halfWidth + Mathf.Abs(Vector3.Dot(forward, centre.left)) * halfLength;
            foreach (var exclusion in input.Exclusions)
                if (station + halfLength >= exclusion.start && station - halfLength <= exclusion.end)
                    minimum = Mathf.Min(minimum, Mathf.Max(exclusion.minimumLateral - (offset + extent), offset - extent - exclusion.maximumLateral));
            return minimum;
        }
        private static RacingCorridorSample Project(RacingCorridorSample[] samples, Vector3 position, float station, float range)
        {
            int start = Index(samples, station - range), end = Index(samples, station + range);
            float best = float.PositiveInfinity, result = station;
            for (int i = start; i <= end; i++)
            {
                var a = samples[i]; var b = samples[i + 1]; Vector3 edge = b.position - a.position;
                float t = Mathf.Clamp01(Vector3.Dot(position - a.position, edge) / Mathf.Max(0.0001f, edge.sqrMagnitude));
                float square = (position - a.position - edge * t).sqrMagnitude;
                if (square >= best) continue; best = square; result = Mathf.Lerp(a.station, b.station, t);
            }
            return Sample(samples, result);
        }
        private static int Index(RacingCorridorSample[] samples, float station)
        {
            int low = 0, high = samples.Length - 2;
            while (low < high) { int middle = (low + high + 1) / 2; if (samples[middle].station <= station) low = middle; else high = middle - 1; }
            return low;
        }
    }
}
