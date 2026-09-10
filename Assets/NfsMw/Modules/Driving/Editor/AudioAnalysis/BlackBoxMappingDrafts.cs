using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.AudioAnalysis.Analysis;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public static class BlackBoxMappingDrafts
    {
        public const int MaximumTableAnchors = 16384;
        public static BlackBoxRegionReview FromEndpoints(BlackBoxDocument document, Recording recording, int start, int end, float rpm0, float rpm1)
        {
            if (document == null || recording == null || !recording.Decoded || recording.Pcm == null || start < 0 || end <= start + 1 || end > recording.ValidFrames)
                throw new InvalidOperationException("Choose at least two decoded source frames.");
            if (float.IsNaN(rpm0) || float.IsInfinity(rpm0) || rpm0 <= 0 || float.IsNaN(rpm1) || float.IsInfinity(rpm1) || rpm1 <= 0)
                throw new InvalidOperationException("Enter positive, finite RPM endpoints.");
            return new BlackBoxRegionReview { sourceHash = document.Report.Source.Sha256, recordingId = recording.Id,
                label = System.IO.Path.GetFileNameWithoutExtension(document.Attachment.relativePath) + " / " + recording.Id,
                startFrame = start, endFrame = end, startRpm = rpm0, endRpm = rpm1,
                evidence = document.Report.ParserVersion + " · decoded physical recording " + recording.Id,
                assumptions = "User-authored RPM endpoints and load range. Linear frame interpolation; original game controller and recording role are unverified." };
        }

        public static BlackBoxRegionReview FromGinTable(BlackBoxDocument document, Recording recording)
        {
            var hypothesis = GinMappingInvestigator.Inspect(document.Report);
            var table = document.Report.Tables.FirstOrDefault(t => t.Name == "table_a");
            if (table == null || table.Entries.Count < 2 || !hypothesis.Endpoint0.HasValue || !hypothesis.Endpoint1.HasValue)
                throw new InvalidOperationException("This source has no usable GIN table. Use your own RPM endpoints.");
            if (table.Entries.Count > MaximumTableAnchors)
                throw new InvalidOperationException("This table exceeds the native draft anchor budget. Use explicit endpoints; the full source table remains in exported evidence.");
            var draft = FromEndpoints(document, recording, 0, recording.ValidFrames, (float)hypothesis.Endpoint0.Value, (float)hypothesis.Endpoint1.Value);
            var anchors = new List<EngineRpmAnchor>();
            for (int i = 0; i < table.Entries.Count; i++)
            {
                long frame = table.Entries[i].RawValue;
                if (i > 0 && frame <= table.Entries[i - 1].RawValue)
                    throw new InvalidOperationException("Table frame positions overlap or reverse. Choose a reviewed interval with explicit RPM endpoints.");
                float rpm = draft.startRpm + (draft.endRpm - draft.startRpm) * i / (table.Entries.Count - 1);
                if (frame < recording.ValidFrames) anchors.Add(new EngineRpmAnchor(rpm, (int)frame));
                else
                {
                    if (anchors.Count > 0 && anchors[anchors.Count - 1].sourceFrame < recording.ValidFrames - 1)
                    {
                        var previous = anchors[anchors.Count - 1];
                        float t = (recording.ValidFrames - 1 - previous.sourceFrame) / (float)(frame - previous.sourceFrame);
                        anchors.Add(new EngineRpmAnchor(previous.rpm + (rpm - previous.rpm) * t, recording.ValidFrames - 1));
                    }
                    break;
                }
            }
            if (anchors.Count < 2) throw new InvalidOperationException("Not enough table positions fall inside decoded audio. Use explicit endpoints.");
            draft.rpmAnchors = anchors.ToArray(); draft.startFrame = anchors[0].sourceFrame; draft.endFrame = anchors[anchors.Count - 1].sourceFrame + 1;
            draft.startRpm = anchors[0].rpm; draft.endRpm = anchors[anchors.Count - 1].rpm;
            draft.lookupPolicy = "Piecewise linear between preserved table A frame positions and uniformly interpolated candidate RPMs; bounded to decoded frames";
            draft.assumptions = "GIN hypothesis: interpret candidate endpoints as RPM and spread them uniformly over table A entry order. Preserve nonlinear frame positions. Trim to decoded frames; original lookup and controller behavior remain unverified.";
            return draft;
        }
    }
}
