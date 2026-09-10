using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class RoadMeshChunk
    {
        public readonly Mesh Mesh;
        public readonly Vector3 Origin;
        public readonly int BandIndex;
        public readonly float Start, End;
        public readonly IReadOnlyList<float> Stations;
        internal RoadMeshChunk(Mesh mesh, Vector3 origin, int bandIndex, float start, float end, List<float> stations)
        { Mesh = mesh; Origin = origin; BandIndex = bandIndex; Start = start; End = end; Stations = stations.AsReadOnly(); }
    }

    public sealed class RoadMeshBuild : IDisposable
    {
        private readonly List<RoadMeshChunk> chunks = new List<RoadMeshChunk>();
        public readonly RoadEvaluation Geometry;
        public IReadOnlyList<RoadMeshChunk> Chunks => chunks;
        internal RoadMeshBuild(RoadEvaluation geometry) { Geometry = geometry; }
        internal void Add(RoadMeshChunk chunk) => chunks.Add(chunk);
        public void Dispose()
        {
            foreach (var chunk in chunks) if (chunk.Mesh != null) UnityEngine.Object.DestroyImmediate(chunk.Mesh);
            chunks.Clear();
        }
    }

    public static class RoadMeshBuilder
    {
        public static RoadMeshBuild Build(RoadAuthoring road)
        {
            var geometry = RoadGeometry.Evaluate(road);
            ValidateBands(road);
            var result = new RoadMeshBuild(geometry);
            try
            {
                int count = Mathf.CeilToInt(geometry.Length / road.chunkLength);
                if (count > 10000) throw new ArgumentException("ROAD_CHUNK_BUDGET: split this road or increase its chunk length.");
                for (int chunk = 0; chunk < count; chunk++)
                {
                    float start = chunk * road.chunkLength, end = Mathf.Min(geometry.Length, (chunk + 1) * road.chunkLength);
                    var stations = Stations(road, geometry, start, end);
                    Vector3 origin = geometry.Sample(start).Position;
                    for (int band = 0; band < road.Bands.Length; band++)
                        result.Add(new RoadMeshChunk(BuildBand(road, geometry, band, stations, origin), origin, band, start, end, stations));
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        public static Vector3 BandPoint(RoadAuthoring road, RoadEvaluation geometry, int band, float station, float fraction, float shapeHeight = 0)
        {
            float total = 0, before = 0;
            for (int i = 0; i < road.Bands.Length; i++) { float width = Width(road, i, station); total += width; if (i < band) before += width; }
            var definition = road.Profile.bands[band];
            float distance = Width(road, band, station) * fraction;
            return geometry.Sample(station).At(total * 0.5f + road.Profile.lateralOffset - before - distance,
                definition.height + definition.crossfall * distance + shapeHeight);
        }
        public static float Width(RoadAuthoring road, int band, float station)
        {
            var binding = road.Bands[band];
            return binding.overrideWidth ? binding.width.Evaluate(station) : road.Profile.bands[band].width;
        }

        private static Mesh BuildBand(RoadAuthoring road, RoadEvaluation geometry, int bandIndex, List<float> stations, Vector3 origin)
        {
            var band = road.Profile.bands[bandIndex];
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var indices = new List<int>();
            for (int strip = 0; strip < band.shape.Length - 1; strip++)
            {
                int offset = vertices.Count;
                var left = band.shape[strip]; var right = band.shape[strip + 1];
                for (int row = 0; row < stations.Count; row++)
                {
                    float station = stations[row];
                    vertices.Add(BandPoint(road, geometry, bandIndex, station, left.fraction, left.height) - origin);
                    vertices.Add(BandPoint(road, geometry, bandIndex, station, right.fraction, right.height) - origin);
                    var across = vertices[vertices.Count - 1] - vertices[vertices.Count - 2];
                    normals.Add(SurfaceNormal(road, geometry, bandIndex, station, left, across));
                    normals.Add(SurfaceNormal(road, geometry, bandIndex, station, right, across));
                    float width = Width(road, bandIndex, station), repeat = road.Profile.textureMetres;
                    uvs.Add(new Vector2(left.fraction * width / repeat, station / repeat));
                    uvs.Add(new Vector2(right.fraction * width / repeat, station / repeat));
                    if (row == 0) continue;
                    int a = offset + (row - 1) * 2, b = a + 2;
                    var normal = Vector3.Cross(vertices[b] - vertices[a], vertices[a + 1] - vertices[a]);
                    if (normal.sqrMagnitude < 0.0000000001f)
                        throw new ArgumentException("ROAD_MESH: degenerate cross-section surface.");
                    if (right.fraction > left.fraction && Vector3.Dot(normal.normalized, geometry.Sample(station).Up) <= 0)
                        throw new ArgumentException("ROAD_FOLD: a band folds over itself; reduce width or increase turn radius.");
                    indices.Add(a); indices.Add(b); indices.Add(a + 1);
                    indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
                }
            }
            if (vertices.Count > 200000) throw new ArgumentException("ROAD_MESH_BUDGET: reduce chunk length or profile detail.");
            var mesh = new Mesh { name = "Road " + road.Id + " " + road.Bands[bandIndex].id,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(indices, 0);
                mesh.SetNormals(normals); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                return mesh;
            }
            catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }

        private static Vector3 SurfaceNormal(RoadAuthoring road, RoadEvaluation geometry, int band, float station, RoadProfilePoint point, Vector3 across)
        {
            const float delta = 0.01f;
            float a = Mathf.Max(0, station - delta), b = Mathf.Min(geometry.Length, station + delta);
            var along = BandPoint(road, geometry, band, b, point.fraction, point.height) - BandPoint(road, geometry, band, a, point.fraction, point.height);
            // Evaluate on the whole road so both chunks use exactly the same derivative at a seam.
            return Vector3.Cross(along, across).normalized;
        }

        private static List<float> Stations(RoadAuthoring road, RoadEvaluation geometry, float start, float end)
        {
            var boundaries = new SortedSet<float> { start, end };
            foreach (var frame in geometry.Frames) if (frame.Station > start && frame.Station < end) boundaries.Add(frame.Station);
            foreach (var key in road.bankRadians.keys) if (key.time > start && key.time < end) boundaries.Add(key.time);
            foreach (var band in road.Bands)
                if (band.overrideWidth) foreach (var key in band.width.keys) if (key.time > start && key.time < end) boundaries.Add(key.time);
            var result = new List<float> { start };
            float previous = start;
            foreach (float station in boundaries)
            {
                if (station <= start) continue;
                SubdivideSurface(road, geometry, previous, station, 0, result); previous = station;
            }
            return result;
        }
        private static void SubdivideSurface(RoadAuthoring road, RoadEvaluation geometry, float a, float b, int depth, List<float> result)
        {
            float error = 0;
            for (int band = 0; band < road.Bands.Length; band++)
                foreach (var point in road.Profile.bands[band].shape)
                {
                    var first = BandPoint(road, geometry, band, a, point.fraction, point.height);
                    var last = BandPoint(road, geometry, band, b, point.fraction, point.height);
                    for (int q = 1; q < 4; q++)
                        error = Mathf.Max(error, RoadGeometry.DistanceToSegment(
                            BandPoint(road, geometry, band, Mathf.Lerp(a, b, q * 0.25f), point.fraction, point.height), first, last));
                }
            if (error > road.chordTolerance)
            {
                if (depth >= 16 || result.Count > 50000) throw new ArgumentException("ROAD_SURFACE_BUDGET: bank/width changes cannot meet the requested tolerance.");
                float mid = (a + b) * 0.5f;
                SubdivideSurface(road, geometry, a, mid, depth + 1, result); SubdivideSurface(road, geometry, mid, b, depth + 1, result);
            }
            else
            {
                if (result.Count >= 50000) throw new ArgumentException("ROAD_SURFACE_BUDGET: chunk exceeds the surface sample limit.");
                result.Add(b);
            }
        }

        internal static void ValidateBands(RoadAuthoring road)
        {
            ValidateCurve(road.bankRadians, -1.4f, 1.4f, "bank");
            for (int i = 0; i < road.Bands.Length; i++)
            {
                var band = road.Profile.bands[i]; var binding = road.Bands[i];
                if (binding.overrideWidth) ValidateCurve(binding.width, 0.001f, 1000, "width");
                if (band.shape == null || band.shape.Length < 2 || band.shape.Length > 64 || band.shape[0].fraction != 0 || band.shape[band.shape.Length - 1].fraction != 1)
                    throw new ArgumentException("ROAD_PROFILE: a band shape must run from fraction 0 to 1 with 2–64 points.");
                for (int p = 0; p < band.shape.Length; p++)
                {
                    var point = band.shape[p];
                    if (!RoadGeometry.Finite(point.fraction) || !RoadGeometry.Finite(point.height) || point.fraction < 0 || point.fraction > 1)
                        throw new ArgumentException("ROAD_PROFILE: nonfinite or out-of-range shape point.");
                    if (p > 0 && (point.fraction < band.shape[p - 1].fraction || (point.fraction == band.shape[p - 1].fraction && point.height == band.shape[p - 1].height)))
                        throw new ArgumentException("ROAD_PROFILE: shape points must progress left to right without coincident vertices.");
                }
            }
        }

        // Check cubic extrema, not only key values. Weighted tangents need a different polynomial contract.
        private static void ValidateCurve(AnimationCurve curve, float minimum, float maximum, string label)
        {
            if (curve == null || curve.length == 0) throw new ArgumentException("ROAD_CURVE: missing " + label + " keys.");
            if (curve.preWrapMode == WrapMode.Loop || curve.preWrapMode == WrapMode.PingPong
                || curve.postWrapMode == WrapMode.Loop || curve.postWrapMode == WrapMode.PingPong)
                throw new ArgumentException("ROAD_CURVE: use clamped " + label + " keys; repeat wrapping is unsupported.");
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (!RoadGeometry.Finite(key.time) || !RoadGeometry.Finite(key.value) || !RoadGeometry.Finite(key.inTangent) || !RoadGeometry.Finite(key.outTangent) || key.weightedMode != WeightedMode.None)
                    throw new ArgumentException("ROAD_CURVE: use finite unweighted " + label + " keys.");
                CheckValue(key.value, minimum, maximum, label);
                if (i == 0) continue;
                var prior = keys[i - 1]; float dt = key.time - prior.time;
                if (dt <= 0) throw new ArgumentException("ROAD_CURVE: key stations must increase.");
                float m0 = prior.outTangent * dt, m1 = key.inTangent * dt;
                float a = 2 * prior.value - 2 * key.value + m0 + m1;
                float b = -3 * prior.value + 3 * key.value - 2 * m0 - m1;
                float c = m0, discriminant = 4 * b * b - 12 * a * c;
                if (Mathf.Abs(a) < 0.0000001f) { if (Mathf.Abs(b) > 0.0000001f) CheckExtremum(-c / (2 * b), a, b, c, prior.value, minimum, maximum, label); }
                else if (discriminant >= 0)
                {
                    float root = Mathf.Sqrt(discriminant);
                    CheckExtremum((-2 * b + root) / (6 * a), a, b, c, prior.value, minimum, maximum, label);
                    CheckExtremum((-2 * b - root) / (6 * a), a, b, c, prior.value, minimum, maximum, label);
                }
            }
        }
        private static void CheckExtremum(float t, float a, float b, float c, float d, float minimum, float maximum, string label)
        { if (t > 0 && t < 1) CheckValue(((a * t + b) * t + c) * t + d, minimum, maximum, label); }
        private static void CheckValue(float value, float minimum, float maximum, string label)
        { if (!RoadGeometry.Finite(value) || value < minimum || value > maximum) throw new ArgumentException("ROAD_CURVE: " + label + " exceeds its limits between or at keys."); }
    }
}
