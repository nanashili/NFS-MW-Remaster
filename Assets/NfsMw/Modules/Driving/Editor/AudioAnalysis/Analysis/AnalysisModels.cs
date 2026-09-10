using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving.AudioAnalysis.Analysis
{
    public enum MappingEvidenceStatus { Unknown, Candidate, Verified }
    public enum MappingLookupPolicy { PiecewiseLinear, LowerBound, UpperBound, ExactOnly }
    public enum FrameBoundary { Inclusive, Exclusive }
    public enum EngineOrderModel { Unknown, CrankshaftOneX, CombustionOrder, Explicit }

    public readonly struct FrameRange
    {
        public readonly long Start;
        public readonly long EndExclusive;
        public FrameRange(long start, long endExclusive)
        {
            if (start < 0 || endExclusive < start) throw new ArgumentOutOfRangeException();
            Start = start; EndExclusive = endExclusive;
        }
        public long Length { get { return EndExclusive - Start; } }
        public override string ToString() { return "[" + Start + ", " + EndExclusive + ")"; }
    }

    public sealed class SourceEvidence
    {
        public readonly string SourceId;
        public readonly string ContentHash;
        public readonly string RecordPath;
        public readonly string RawRepresentation;
        public readonly string ParserVersion;
        public readonly string Notes;
        public SourceEvidence(string sourceId, string contentHash = "", string recordPath = "", string rawRepresentation = "", string parserVersion = "", string notes = "")
        { SourceId = sourceId ?? ""; ContentHash = contentHash ?? ""; RecordPath = recordPath ?? ""; RawRepresentation = rawRepresentation ?? ""; ParserVersion = parserVersion ?? ""; Notes = notes ?? ""; }
    }

    public readonly struct RpmAnchor
    {
        public readonly int TableIndex;
        public readonly double TableCoordinate;
        public readonly double Rpm;
        public readonly double FrameCoordinate;
        public readonly FrameRange Frames;
        public readonly MappingEvidenceStatus Status;
        public readonly SourceEvidence Evidence;
        public RpmAnchor(int tableIndex, double tableCoordinate, double rpm, FrameRange frames, MappingEvidenceStatus status, SourceEvidence evidence)
        { TableIndex = tableIndex; TableCoordinate = tableCoordinate; Rpm = rpm; Frames = frames; FrameCoordinate = frames.Start; Status = status; Evidence = evidence; }
        public RpmAnchor(int tableIndex, double tableCoordinate, double frameCoordinate, double rpm, FrameRange frames, MappingEvidenceStatus status, SourceEvidence evidence)
        { TableIndex = tableIndex; TableCoordinate = tableCoordinate; Rpm = rpm; Frames = frames; FrameCoordinate = frameCoordinate; Status = status; Evidence = evidence; }
    }

    public readonly struct MappingInterval
    {
        public readonly double InputStart;
        public readonly double InputEnd;
        public readonly double OutputStart;
        public readonly double OutputEnd;
        public readonly int FirstTableIndex;
        public readonly int SecondTableIndex;
        public MappingInterval(double inputStart, double inputEnd, double outputStart, double outputEnd, int firstTableIndex, int secondTableIndex)
        { InputStart = inputStart; InputEnd = inputEnd; OutputStart = outputStart; OutputEnd = outputEnd; FirstTableIndex = firstTableIndex; SecondTableIndex = secondTableIndex; }
        public bool IsPoint { get { return Math.Abs(OutputEnd - OutputStart) < 1e-9; } }
    }

    public readonly struct FrequencyCandidate
    {
        public readonly double FrequencyHz;
        public readonly double Rpm;
        public readonly double Strength;
        public readonly int Harmonic;
        public readonly EngineOrderModel Model;
        public readonly string Assumption;
        public FrequencyCandidate(double frequencyHz, double rpm, double strength, int harmonic, EngineOrderModel model, string assumption)
        { FrequencyHz = frequencyHz; Rpm = rpm; Strength = strength; Harmonic = harmonic; Model = model; Assumption = assumption ?? ""; }
    }

    public sealed class WaveformSummary
    {
        public readonly int SampleRate;
        public readonly int Channels;
        public readonly long FrameCount;
        public readonly double DurationSeconds;
        public readonly float Peak;
        public readonly double Rms;
        public readonly string AlgorithmVersion;
        public WaveformSummary(int sampleRate, int channels, long frameCount, float peak, double rms, string algorithmVersion)
        { SampleRate = sampleRate; Channels = channels; FrameCount = frameCount; DurationSeconds = sampleRate > 0 ? (double)frameCount / sampleRate : 0; Peak = peak; Rms = rms; AlgorithmVersion = algorithmVersion ?? ""; }
    }

    public sealed class SpectrogramResult
    {
        public readonly int SampleRate, WindowSize, HopSize, BinCount, ColumnCount;
        public readonly float[] Magnitudes;
        public readonly string ParametersHash;
        public readonly string AlgorithmVersion;
        public SpectrogramResult(int sampleRate, int windowSize, int hopSize, int binCount, int columnCount, float[] magnitudes, string parametersHash, string algorithmVersion)
        { SampleRate = sampleRate; WindowSize = windowSize; HopSize = hopSize; BinCount = binCount; ColumnCount = columnCount; Magnitudes = magnitudes ?? Array.Empty<float>(); ParametersHash = parametersHash ?? ""; AlgorithmVersion = algorithmVersion ?? ""; }
        public float At(int column, int bin) { return Magnitudes[column * BinCount + bin]; }
    }

    public sealed class SignalComparison
    {
        public readonly long DeclaredLatencyFrames;
        public readonly long ComparedFrames;
        public readonly double PeakDifference;
        public readonly double RmsDifference;
        public readonly double RmsA, RmsB;
        public readonly bool LengthsMatch;
        public SignalComparison(long latency, long compared, double peakDifference, double rmsDifference, double rmsA, double rmsB, bool lengthsMatch)
        { DeclaredLatencyFrames = latency; ComparedFrames = compared; PeakDifference = peakDifference; RmsDifference = rmsDifference; RmsA = rmsA; RmsB = rmsB; LengthsMatch = lengthsMatch; }
    }

    public sealed class GinMappingInvestigation
    {
        public readonly string Status;
        public readonly string SourceHash;
        public readonly string ParserVersion;
        public readonly double? Endpoint0;
        public readonly double? Endpoint1;
        public readonly int TableACount;
        public readonly int TableBCount;
        public readonly int DeclaredFrames;
        public readonly string Observation;
        public readonly RpmMapping Candidate;
        public GinMappingInvestigation(string status, string sourceHash, string parserVersion, double? endpoint0, double? endpoint1, int tableACount, int tableBCount, int declaredFrames, string observation, RpmMapping candidate)
        { Status = status ?? "unknown"; SourceHash = sourceHash ?? ""; ParserVersion = parserVersion ?? ""; Endpoint0 = endpoint0; Endpoint1 = endpoint1; TableACount = tableACount; TableBCount = tableBCount; DeclaredFrames = declaredFrames; Observation = observation ?? ""; Candidate = candidate; }
    }
}
