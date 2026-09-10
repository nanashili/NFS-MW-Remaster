using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    internal static class RoadFingerprint
    {
        // Explicit fields and persistent asset IDs: no instance IDs, dictionary order or runtime hash codes.
        internal static string Compute(RoadNetworkAuthoring network)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write("RoadBake/4"); writer.Write(Application.unityVersion); writer.Write(network.Id.ToString());
                var roads = network.Roads.OrderBy(road => road.Id.ToString(), StringComparer.Ordinal).ToArray(); writer.Write(roads.Length);
                foreach (var road in roads)
                {
                    writer.Write(road.Id.ToString()); writer.Write(road.SchemaVersion);
                    writer.Write(road.chordTolerance); writer.Write(road.maximumSampleSpacing); writer.Write(road.chunkLength);
                    var matrix = road.Reference.transform.localToWorldMatrix;
                    for (int i = 0; i < 16; i++) writer.Write(matrix[i]);
                    var spline = road.Reference.Spline; writer.Write(spline.Closed); writer.Write(spline.Count);
                    foreach (var knot in spline)
                    {
                        Vector(writer, knot.Position); Vector(writer, knot.TangentIn); Vector(writer, knot.TangentOut);
                        var r = knot.Rotation.value; writer.Write(r.x); writer.Write(r.y); writer.Write(r.z); writer.Write(r.w);
                    }
                    Curve(writer, road.bankRadians);
                    var profile = road.Profile; Asset(writer, profile);
                    writer.Write((int)profile.roadClass); writer.Write(profile.speedLimit); writer.Write(profile.textureMetres); writer.Write(profile.lateralOffset);
                    writer.Write(profile.bands.Length);
                    for (int i = 0; i < profile.bands.Length; i++)
                    {
                        var band = profile.bands[i]; var binding = road.Bands[i];
                        writer.Write(band.id.ToString()); writer.Write(binding.id.ToString()); writer.Write(binding.overrideWidth);
                        if (binding.overrideWidth) Curve(writer, binding.width);
                        writer.Write((int)band.kind); writer.Write((int)band.direction); writer.Write(band.width); writer.Write(band.height);
                        writer.Write(band.crossfall); writer.Write(band.collision); Asset(writer, band.material); Asset(writer, band.surface);
                        if (band.surface != null) { writer.Write((int)band.surface.surface); writer.Write(band.surface.grip); writer.Write(band.surface.rollingResistance); }
                        writer.Write(band.shape.Length);
                        foreach (var point in band.shape) { writer.Write(point.fraction); writer.Write(point.height); }
                    }
                }
                var links = network.connections.OrderBy(link => link.from.ToString(), StringComparer.Ordinal).ThenBy(link => link.to.ToString(), StringComparer.Ordinal).ToArray();
                writer.Write(links.Length); foreach (var link in links) { writer.Write(link.from.ToString()); writer.Write(link.to.ToString()); }
                writer.Flush();
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
        private static void Vector(BinaryWriter writer, Vector3 vector) { writer.Write(vector.x); writer.Write(vector.y); writer.Write(vector.z); }
        private static void Asset(BinaryWriter writer, UnityEngine.Object asset)
        {
            if (asset == null) { writer.Write(""); writer.Write(0L); return; }
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long local)) throw new ArgumentException("ROAD_ASSET: save referenced profiles and materials as assets before baking.");
            writer.Write(guid); writer.Write(local);
        }
        private static void Curve(BinaryWriter writer, AnimationCurve curve)
        {
            writer.Write((int)curve.preWrapMode); writer.Write((int)curve.postWrapMode); writer.Write(curve.length);
            foreach (var key in curve.keys)
            {
                writer.Write(key.time); writer.Write(key.value); writer.Write(key.inTangent); writer.Write(key.outTangent);
                writer.Write(key.inWeight); writer.Write(key.outWeight); writer.Write((int)key.weightedMode);
            }
        }
    }
}
