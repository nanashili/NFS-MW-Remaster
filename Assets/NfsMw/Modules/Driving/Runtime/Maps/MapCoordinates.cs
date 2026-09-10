using System;
using UnityEngine;

namespace NfsMwRemaster.Maps
{
    [Serializable] public struct MapPoint
    {
        public double x, y;
        public MapPoint(double x, double y) { this.x = x; this.y = y; }
        public static MapPoint operator +(MapPoint a, MapPoint b) => new MapPoint(a.x + b.x, a.y + b.y);
        public static MapPoint operator -(MapPoint a, MapPoint b) => new MapPoint(a.x - b.x, a.y - b.y);
        public static MapPoint operator *(MapPoint a, double s) => new MapPoint(a.x * s, a.y * s);
        public double Square => x * x + y * y;
        public bool Finite => !double.IsNaN(x) && !double.IsNaN(y) && !double.IsInfinity(x) && !double.IsInfinity(y);
    }
    [Serializable] public struct LogicalOrigin { public double x, y, z; }
    [Serializable] public sealed class MapFrame
    {
        public LogicalOrigin origin;
        public double unitsPerMeter = 1;
        public double yawDegrees;
        public bool mirrorEast;
        public void Validate()
        {
            if (!new MapPoint(origin.x, origin.z).Finite || double.IsNaN(origin.y) || double.IsInfinity(origin.y)
                || !new MapPoint(unitsPerMeter, yawDegrees).Finite || unitsPerMeter <= 0) throw new ArgumentException("Invalid map coordinate frame.");
        }
        // Source world XZ is right-handed in map space (east, north). UI flips north down.
        public MapPoint Source(double x, double z)
        {
            double c = Math.Cos(yawDegrees * Math.PI / 180), s = Math.Sin(yawDegrees * Math.PI / 180);
            x -= origin.x; z -= origin.z;
            return new MapPoint((x * c - z * s) * unitsPerMeter * (mirrorEast ? -1 : 1), (x * s + z * c) * unitsPerMeter);
        }
        public MapPoint Source(Vector3 source) => Source(source.x, source.z);
        public MapPoint Shifted(Vector3 unity, LogicalOrigin shift) => Source(unity.x + shift.x, unity.z + shift.z);
        public MapPoint ToSource(MapPoint map)
        {
            double c = Math.Cos(yawDegrees * Math.PI / 180), s = Math.Sin(yawDegrees * Math.PI / 180);
            double x = map.x / unitsPerMeter * (mirrorEast ? -1 : 1), z = map.y / unitsPerMeter;
            return new MapPoint(x * c + z * s + origin.x, -x * s + z * c + origin.z);
        }
        public Vector3 ToShifted(MapPoint map, double sourceHeight, LogicalOrigin shift)
        { var p = ToSource(map); return new Vector3((float)(p.x - shift.x), (float)(sourceHeight - shift.y), (float)(p.y - shift.z)); }
        public float Heading(Vector3 forward)
        {
            double c = Math.Cos(yawDegrees * Math.PI / 180), s = Math.Sin(yawDegrees * Math.PI / 180);
            return (float)(Math.Atan2((forward.x * c - forward.z * s) * (mirrorEast ? -1 : 1), forward.x * s + forward.z * c) * 180 / Math.PI);
        }
        public MapFrame Copy() => JsonUtility.FromJson<MapFrame>(JsonUtility.ToJson(this));
    }
    [Serializable] public sealed class MapViewport
    {
        public MapPoint center;
        public float pixelsPerUnit = 1, rotation;
        public Vector2 anchor = new Vector2(.5f, .5f);
        public Vector2 ToUI(MapPoint p, Rect rect)
        {
            var d = p - center; double a = rotation * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return new Vector2(rect.x + rect.width * anchor.x + (float)((d.x * c - d.y * s) * pixelsPerUnit),
                rect.y + rect.height * anchor.y - (float)((d.x * s + d.y * c) * pixelsPerUnit));
        }
        public MapPoint FromUI(Vector2 p, Rect rect)
        {
            double x = (p.x - rect.x - rect.width * anchor.x) / pixelsPerUnit, y = -(p.y - rect.y - rect.height * anchor.y) / pixelsPerUnit;
            double a = rotation * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return center + new MapPoint(x * c + y * s, -x * s + y * c);
        }
        public void Zoom(float factor, Vector2 pivot, Rect rect)
        { var before = FromUI(pivot, rect); pixelsPerUnit = Mathf.Clamp(pixelsPerUnit * factor, .01f, 20); center += before - FromUI(pivot, rect); }
        public void Pan(Vector2 delta, Rect rect) => center += FromUI(Vector2.zero, rect) - FromUI(delta, rect);
    }
}
