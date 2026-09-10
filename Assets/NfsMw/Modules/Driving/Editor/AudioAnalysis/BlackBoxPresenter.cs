using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.AudioAnalysis.Analysis;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed class BlackBoxAnalysisResult
    {
        public string sourceHash, recordingId;
        public WaveformSummary summary;
        public float[] minimum, maximum;
        public SpectrogramResult spectrogram;
        public IReadOnlyList<FrequencyCandidate> candidates;
        public GinMappingInvestigation mapping;
        public BlackBoxTablePlot[] tables;
        public int candidateStartFrame, candidateFrameCount;
    }

    /// <summary>Shared presenter for every entry route; bounded work stays outside OnGUI and audio callbacks.</summary>
    public sealed class BlackBoxPresenter : IDisposable
    {
        public BlackBoxSession Session { get; private set; }
        public BlackBoxDocument[] Documents { get; private set; } = Array.Empty<BlackBoxDocument>();
        public BlackBoxAnalysisResult Analysis { get; private set; }
        public BlackBoxMappingPlan Plan { get; set; }
        public string Status { get; private set; } = "Attach original sources to inspect their evidence.";
        public string Error { get; private set; } = "";
        public bool Busy => job != null;
        private CancellationTokenSource cancel;
        private Task job;
        private Action complete;
        private int generation;
        private bool disposed;

        public BlackBoxPresenter(BlackBoxSession session) { Session = session; }
        public void SetSession(BlackBoxSession session)
        { Cancel(); Session = session; Documents = Array.Empty<BlackBoxDocument>(); Analysis = null; Plan = null; }

        public void AttachFolder(string root)
        {
            var previous = CopySources();
            Start(c => BlackBoxInspection.Inspect(Merge(previous, BlackBoxSourceAccess.Discover(root, c)), c), AcceptInspection);
        }
        public void AttachFile(string path)
        {
            var selected = new BlackBoxSource { root = Path.GetDirectoryName(Path.GetFullPath(path)), relativePath = Path.GetFileName(path) };
            var sources = Merge(CopySources(), new[] { selected });
            Start(c => BlackBoxInspection.Inspect(sources, c), AcceptInspection);
        }
        public void Reinspect()
        { var sources = CopySources(); Start(c => BlackBoxInspection.Inspect(sources, c), AcceptInspection); }

        private BlackBoxSource[] CopySources()
        {
            if (Session == null) throw new InvalidOperationException("Create or select an analysis session first.");
            Session.ValidateSchema();
            return Session.sources.Select(s => new BlackBoxSource { root = s.root, relativePath = s.relativePath, hash = s.hash, declaredContext = s.declaredContext }).ToArray();
        }
        private static BlackBoxSource[] Merge(BlackBoxSource[] existing, BlackBoxSource[] added)
            => existing.Concat(added).GroupBy(s => s.root + "/" + s.relativePath, StringComparer.Ordinal).Select(g => g.First()).ToArray();

        private void AcceptInspection(BlackBoxDocument[] documents)
        {
            Undo.RecordObject(Session, "Attach Black Box source evidence");
            foreach (var document in documents) if (string.IsNullOrEmpty(document.Attachment.hash)) document.Attachment.hash = document.Report.Source.Sha256;
            Session.sources = documents.Select(d => d.Attachment).ToList(); Session.revision++;
            EditorUtility.SetDirty(Session); Documents = documents; Plan = null; Analysis = null;
            Status = documents.Length + " sources inspected. Capabilities are independent; source order and unknown fields are preserved.";
        }

        public void ReviewRevision(BlackBoxDocument document)
        {
            Undo.RecordObject(Session, "Review changed audio source");
            string old = document.Attachment.hash;
            document.Attachment.hash = document.Report.Source.Sha256;
            foreach (var region in Session.regions) if (region.sourceHash == old) region.reviewed = false;
            Touch(); Status = "New source hash accepted. Old regions remain linked to the old hash and require explicit remapping.";
        }

        public void Analyze(BlackBoxDocument document, Recording recording, double rpmPerHz, int candidateStart = 0, int candidateEnd = 0)
        {
            if (recording == null || !recording.Decoded || recording.Pcm == null) throw new InvalidOperationException("Select decoded PCM first.");
            if (candidateEnd == 0) candidateEnd = recording.ValidFrames;
            if (candidateStart < 0 || candidateEnd <= candidateStart || candidateEnd > recording.ValidFrames) throw new InvalidOperationException("Choose a valid source frame interval for harmonic analysis.");
            Start(c =>
            {
                int bins = Math.Min(1024, recording.ValidFrames);
                var result = new BlackBoxAnalysisResult { sourceHash = document.Report.Source.Sha256, recordingId = recording.Id,
                    summary = SignalAnalysis.Summarize(recording.Pcm, recording.SampleRate, recording.Channels, c),
                    minimum = new float[bins], maximum = new float[bins], mapping = GinMappingInvestigator.Inspect(document.Report) };
                for (int b = 0; b < bins; b++)
                {
                    c.ThrowIfCancellationRequested(); float low = float.MaxValue, high = float.MinValue;
                    int start = (int)((long)b * recording.ValidFrames / bins), end = (int)((long)(b + 1) * recording.ValidFrames / bins);
                    for (int i = start * recording.Channels; i < end * recording.Channels; i++) { low = Math.Min(low, recording.Pcm[i]); high = Math.Max(high, recording.Pcm[i]); }
                    result.minimum[b] = low; result.maximum[b] = high;
                }
                result.spectrogram = SignalAnalysis.Spectrogram(recording.Pcm, recording.SampleRate, recording.Channels,
                    new SpectrogramParameters { WindowSize = 1024, HopSize = Math.Max(256, recording.ValidFrames / 1024), MaxColumns = 1024, EngineOrder = "Explicit " + rpmPerHz + " RPM/Hz; alternatives retained" }, c);
                result.tables = document.Report.Tables.Take(8).Select(t => BlackBoxTablePlot.Build(t, c)).ToArray();
                int count = Math.Min(4096, candidateEnd - candidateStart); var mono = new float[count];
                result.candidateStartFrame = candidateStart; result.candidateFrameCount = count;
                for (int i = 0; i < count; i++) mono[i] = recording.Pcm[(candidateStart + i) * recording.Channels];
                if (count >= 16) result.candidates = SignalAnalysis.FrequencyCandidates(mono, recording.SampleRate, EngineOrderModel.Explicit, rpmPerHz, 12, c);
                return result;
            }, result => { Analysis = result; Status = "Analysis cached for this source hash. Frequency candidates are inferences; spectrum uses source channel 0."; });
        }

        public void Start<T>(Func<CancellationToken, T> work, Action<T> accept)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BlackBoxPresenter));
            if (job != null) throw new InvalidOperationException("A window-owned job is already running; finish or cancel it first.");
            cancel = new CancellationTokenSource(); int expected = ++generation; var source = Session; int revision = source == null ? 0 : source.revision;
            var token = cancel.Token;
            Task<T> task = Task.Run(() => work(token), token); job = task;
            complete = () =>
            {
                var result = task.GetAwaiter().GetResult();
                if (disposed || expected != generation || Session != source || source != null && source.revision != revision)
                    throw new InvalidOperationException("Obsolete source/session revision rejected. Run the action again.");
                accept(result);
            };
            SetStatus("Working in the background; cancellation preserves prior results.");
        }
        public bool Tick()
        {
            if (job == null || !job.IsCompleted) return false;
            try { complete(); }
            catch (OperationCanceledException) { Status = "Cancelled; prior evidence and assets preserved."; }
            catch (Exception error) { SetError(error.GetBaseException().Message); }
            finally { job = null; complete = null; cancel?.Dispose(); cancel = null; }
            return true;
        }
        public void Cancel() { generation++; cancel?.Cancel(); }
        public void Touch() { if (Session != null) { Session.revision++; EditorUtility.SetDirty(Session); } Plan = null; }
        public void SetStatus(string value) { Status = value; Error = ""; }
        public void SetError(string value) { Status = value; Error = value; }
        public void Dispose() { if (disposed) return; disposed = true; Cancel(); }
    }
}
