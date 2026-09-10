using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving.AudioAnalysis.Analysis
{
    /// <summary>Ordered source table to RPM/frame mapping. The input order is evidence and is never sorted.</summary>
    public sealed class RpmMapping
    {
        readonly RpmAnchor[] anchors;
        public readonly MappingLookupPolicy LookupPolicy;
        public readonly bool IsMonotonic;
        public readonly bool IsAscending;
        public readonly string Validation;
        public IReadOnlyList<RpmAnchor> Anchors { get { return anchors; } }

        public RpmMapping(IList<RpmAnchor> orderedAnchors, MappingLookupPolicy lookupPolicy = MappingLookupPolicy.PiecewiseLinear)
        {
            if (orderedAnchors == null || orderedAnchors.Count == 0) throw new ArgumentException("At least one ordered anchor is required.");
            anchors = new RpmAnchor[orderedAnchors.Count];
            for (int i = 0; i < anchors.Length; i++)
            {
                if (double.IsNaN(orderedAnchors[i].Rpm) || double.IsInfinity(orderedAnchors[i].Rpm) || double.IsNaN(orderedAnchors[i].TableCoordinate) || double.IsInfinity(orderedAnchors[i].TableCoordinate) || double.IsNaN(orderedAnchors[i].FrameCoordinate) || double.IsInfinity(orderedAnchors[i].FrameCoordinate)) throw new ArgumentException("Mapping values must be finite.");
                if (orderedAnchors[i].Frames.EndExclusive < orderedAnchors[i].Frames.Start) throw new ArgumentException("Frame ranges must be half-open.");
                anchors[i] = orderedAnchors[i];
            }
            LookupPolicy = lookupPolicy;
            bool haveDirection = false, monotonic = true, ascending = true;
            for (int i = 1; i < anchors.Length; i++)
            {
                double d = anchors[i].Rpm - anchors[i - 1].Rpm;
                if (Math.Abs(d) > 1e-9)
                {
                    if (!haveDirection) { ascending = d > 0; haveDirection = true; }
                    else if ((d > 0) != ascending) monotonic = false;
                }
            }
            IsMonotonic = monotonic; IsAscending = ascending;
            Validation = monotonic ? (haveDirection ? (ascending ? "ascending" : "descending") : "flat") : "non-monotonic/discontinuous";
        }

        public double FrameToRpm(double frame)
        {
            if (double.IsNaN(frame) || double.IsInfinity(frame)) throw new ArgumentException("Frame must be finite.", "frame");
            IReadOnlyList<double> alternatives = FrameToRpmAlternatives(frame);
            if (alternatives.Count == 0) throw new ArgumentOutOfRangeException("frame", "Frame is outside the source mapping; extrapolation is not implicit.");
            if (alternatives.Count > 1) throw new InvalidOperationException("Frame has ambiguous source mapping; use FrameToRpmAlternatives.");
            return alternatives[0];
        }

        public IReadOnlyList<double> FrameToRpmAlternatives(double frame)
        {
            var result = new List<double>();
            if (double.IsNaN(frame) || double.IsInfinity(frame)) return result;
            if (LookupPolicy == MappingLookupPolicy.ExactOnly)
            {
                for (int i = 0; i < anchors.Length; i++)
                    if (Math.Abs(frame - anchors[i].FrameCoordinate) < 1e-9) AddDistinct(result, anchors[i].Rpm);
                return result;
            }
            for (int i = 0; i < anchors.Length - 1; i++)
            {
                double lo = Math.Min(anchors[i].FrameCoordinate, anchors[i + 1].FrameCoordinate), hi = Math.Max(anchors[i].FrameCoordinate, anchors[i + 1].FrameCoordinate);
                if (frame < lo || frame > hi) continue;
                if (Math.Abs(anchors[i + 1].FrameCoordinate - anchors[i].FrameCoordinate) < 1e-12)
                {
                    // A vertical inverse segment has two source RPMs at one frame.
                    AddDistinct(result, anchors[i].Rpm); AddDistinct(result, anchors[i + 1].Rpm);
                }
                else AddDistinct(result, Interpolate(anchors[i].FrameCoordinate, anchors[i + 1].FrameCoordinate, anchors[i].Rpm, anchors[i + 1].Rpm, frame));
            }
            if (anchors.Length == 1 && Math.Abs(frame - anchors[0].FrameCoordinate) < 1e-9) AddDistinct(result, anchors[0].Rpm);
            return result;
        }

        public bool TryFrameToRpm(double frame, out double rpm)
        {
            rpm = 0;
            if (double.IsNaN(frame) || double.IsInfinity(frame)) return false;
            IReadOnlyList<double> alternatives = FrameToRpmAlternatives(frame);
            if (alternatives.Count != 1) return false;
            rpm = alternatives[0]; return true;
        }

        /// <summary>Returns every matching segment. Duplicate RPMs and non-monotonic inverses remain alternatives.</summary>
        public IReadOnlyList<MappingInterval> RpmToFrames(double rpm)
        {
            var result = new List<MappingInterval>();
            if (double.IsNaN(rpm) || double.IsInfinity(rpm)) return result;
            if (LookupPolicy == MappingLookupPolicy.ExactOnly)
            {
                for (int i = 0; i < anchors.Length; i++) if (Math.Abs(rpm - anchors[i].Rpm) < 1e-9)
                    result.Add(new MappingInterval(rpm, rpm, anchors[i].FrameCoordinate, anchors[i].FrameCoordinate, anchors[i].TableIndex, anchors[i].TableIndex));
                return result;
            }
            if (LookupPolicy == MappingLookupPolicy.LowerBound || LookupPolicy == MappingLookupPolicy.UpperBound)
            {
                bool lower = LookupPolicy == MappingLookupPolicy.LowerBound;
                double bound = lower ? double.NegativeInfinity : double.PositiveInfinity;
                for (int i = 0; i < anchors.Length; i++)
                {
                    double value = anchors[i].Rpm;
                    if ((lower && value <= rpm + 1e-9 && value > bound) || (!lower && value >= rpm - 1e-9 && value < bound)) bound = value;
                }
                if (double.IsInfinity(bound)) return result;
                for (int i = 0; i < anchors.Length; i++)
                    if (Math.Abs(anchors[i].Rpm - bound) < 1e-9)
                        result.Add(new MappingInterval(bound, bound, anchors[i].FrameCoordinate, anchors[i].FrameCoordinate, anchors[i].TableIndex, anchors[i].TableIndex));
                return result;
            }
            if (anchors.Length == 1)
            {
                if (Math.Abs(rpm - anchors[0].Rpm) < 1e-9) result.Add(new MappingInterval(rpm, rpm, anchors[0].FrameCoordinate, anchors[0].FrameCoordinate, anchors[0].TableIndex, anchors[0].TableIndex));
                return result;
            }
            for (int i = 0; i < anchors.Length - 1; i++)
            {
                double a = anchors[i].Rpm, b = anchors[i + 1].Rpm;
                double low = Math.Min(a, b), high = Math.Max(a, b);
                if (rpm < low - 1e-9 || rpm > high + 1e-9) continue;
                double frameStart = anchors[i].FrameCoordinate, frameEnd = anchors[i + 1].FrameCoordinate;
                if (Math.Abs(b - a) >= 1e-9) { frameStart = Interpolate(a, b, anchors[i].FrameCoordinate, anchors[i + 1].FrameCoordinate, rpm); frameEnd = frameStart; }
                result.Add(new MappingInterval(low, high, frameStart, frameEnd, anchors[i].TableIndex, anchors[i + 1].TableIndex));
            }
            return result;
        }

        public double TableCoordinateToRpm(double coordinate)
        {
            double rpm;
            if (!TryTableCoordinateToRpm(coordinate, out rpm)) throw new ArgumentOutOfRangeException("coordinate", "Coordinate is outside the source mapping; extrapolation is not implicit.");
            return rpm;
        }
        public bool TryTableCoordinateToRpm(double coordinate, out double rpm)
        {
            rpm = 0;
            if (double.IsNaN(coordinate) || double.IsInfinity(coordinate)) return false;
            if (LookupPolicy == MappingLookupPolicy.ExactOnly)
            {
                bool found = false;
                for (int i = 0; i < anchors.Length; i++) if (Math.Abs(coordinate - anchors[i].TableCoordinate) < 1e-9)
                {
                    if (found && Math.Abs(rpm - anchors[i].Rpm) > 1e-9) return false;
                    rpm = anchors[i].Rpm; found = true;
                }
                return found;
            }
            if (LookupPolicy == MappingLookupPolicy.LowerBound || LookupPolicy == MappingLookupPolicy.UpperBound)
            {
                bool lower = LookupPolicy == MappingLookupPolicy.LowerBound;
                double bound = lower ? double.NegativeInfinity : double.PositiveInfinity;
                for (int i = 0; i < anchors.Length; i++)
                {
                    double value = anchors[i].TableCoordinate;
                    if ((lower && value <= coordinate + 1e-9 && value > bound) || (!lower && value >= coordinate - 1e-9 && value < bound)) bound = value;
                }
                if (double.IsInfinity(bound)) return false;
                bool found = false;
                for (int i = 0; i < anchors.Length; i++) if (Math.Abs(anchors[i].TableCoordinate - bound) < 1e-9)
                {
                    if (found && Math.Abs(rpm - anchors[i].Rpm) > 1e-9) return false;
                    rpm = anchors[i].Rpm; found = true;
                }
                return found;
            }
            if (anchors.Length == 1) { if (Math.Abs(coordinate - anchors[0].TableCoordinate) > 1e-9) return false; rpm = anchors[0].Rpm; return true; }
            int segment = FindSegmentForTableCoordinate(coordinate);
            if (segment < 0) return false;
            rpm = Interpolate(anchors[segment].TableCoordinate, anchors[segment + 1].TableCoordinate, anchors[segment].Rpm, anchors[segment + 1].Rpm, coordinate);
            return true;
        }

        static double Interpolate(double x0, double x1, double y0, double y1, double x)
        { return Math.Abs(x1 - x0) < 1e-12 ? y0 : y0 + (y1 - y0) * ((x - x0) / (x1 - x0)); }
        static void AddDistinct(List<double> values, double value)
        { for (int i = 0; i < values.Count; i++) if (Math.Abs(values[i] - value) < 1e-9) return; values.Add(value); }
        int FindSegmentForTableCoordinate(double coordinate)
        {
            for (int i = 0; i < anchors.Length - 1; i++)
            {
                double lo = Math.Min(anchors[i].TableCoordinate, anchors[i + 1].TableCoordinate), hi = Math.Max(anchors[i].TableCoordinate, anchors[i + 1].TableCoordinate);
                if (coordinate >= lo && coordinate <= hi) return i;
            }
            return -1;
        }
    }

    public readonly struct FrameTransform
    {
        public readonly int SourceRate, OutputRate;
        public FrameTransform(int sourceRate, int outputRate)
        { if (sourceRate <= 0 || outputRate <= 0) throw new ArgumentOutOfRangeException(); SourceRate = sourceRate; OutputRate = outputRate; }
        public double SourceToOutput(double sourceFrame) { return sourceFrame * OutputRate / SourceRate; }
        public double OutputToSource(double outputFrame) { return outputFrame * SourceRate / OutputRate; }
        public FrameRange SourceToOutput(FrameRange source)
        {
            long start = (long)Math.Floor(SourceToOutput(source.Start));
            long end = (long)Math.Ceiling(SourceToOutput(source.EndExclusive));
            return new FrameRange(start, Math.Max(start, end));
        }
        public FrameRange OutputToSource(FrameRange output)
        {
            long start = (long)Math.Floor(OutputToSource(output.Start));
            long end = (long)Math.Ceiling(OutputToSource(output.EndExclusive));
            return new FrameRange(start, Math.Max(start, end));
        }
    }
}
