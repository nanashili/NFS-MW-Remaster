using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Second real source adapter. It has no reference to powertrain, wallet, pursuit settlement or mission commands.</summary>
    public sealed class FeedbackReplaySource : MonoBehaviour, IVehicleFeedbackSource
    {
        [SerializeField] private FeedbackReplayAsset clip;
        [SerializeField] private bool playOnStart, loop = true;
        [SerializeField] private bool followRecordedPose = true;
        private VehicleFeedbackFrame[] frames;
        private double elapsed;
        private int cursor, epoch, sequence, lastImpact, recordedEpoch, published = -1;
        public bool Playing { get; private set; }
        public VehicleFeedbackFrame Frame { get; private set; }
        public event Action<VehicleFeedbackFrame> Sampled;
        public event Action<FeedbackImpact> Impact;
        public void Configure(FeedbackReplayAsset content, bool autoPlay) { clip = content; playOnStart = autoPlay; }
        private void Start()
        {
            if (GetComponent<Rigidbody>() != null) { Debug.LogError("Telemetry replay must not control a gameplay Rigidbody.", this); enabled = false; return; }
            if (playOnStart && clip != null) Play(clip.frames);
        }
        public void Play(VehicleFeedbackFrame[] recording)
        {
            if (GetComponentInParent<Rigidbody>() != null || GetComponentInChildren<Rigidbody>(true) != null)
                throw new InvalidOperationException("Replay requires a separate audition object without gameplay Rigidbodies.");
            if (recording == null || recording.Length < 2 || recording.Length > 6000) throw new ArgumentException("Replay requires 2..6000 samples.");
            double lastTime = double.NegativeInfinity;
            foreach (var f in recording)
            {
                if (double.IsNaN(f.Time) || double.IsInfinity(f.Time) || f.Time <= lastTime || f.WheelCount < 0 || f.WheelCount > 8
                    || !FeedbackFrameValidation.Valid(f))
                    throw new ArgumentException("Replay has nonfinite, unordered or unsupported telemetry.");
                lastTime = f.Time;
            }
            frames = (VehicleFeedbackFrame[])recording.Clone();
            elapsed = 0; cursor = 0; epoch++; lastImpact = 0; recordedEpoch = frames[0].Epoch;
            published = -1; Playing = true; Frame = default;
        }
        public void PlayAuthored() { if (clip != null) Play(clip.frames); }
        public void Stop() { Playing = false; Frame = default; lastImpact = 0; }
        private void FixedUpdate()
        {
            if (!Playing || frames == null) return;
            elapsed += Time.fixedDeltaTime;
            double duration = frames[frames.Length - 1].Time - frames[0].Time;
            if (elapsed > duration)
            {
                if (!loop) { Stop(); return; }
                elapsed %= duration; cursor = 0; epoch++; lastImpact = 0; published = -1;
            }
            while (cursor + 1 < frames.Length && frames[cursor + 1].Time - frames[0].Time <= elapsed) cursor++;
            var f = frames[cursor];
            if (published == cursor) return;
            if (f.Epoch != recordedEpoch) { recordedEpoch = f.Epoch; epoch++; lastImpact = 0; }
            published = cursor;
            double shift = Time.fixedTimeAsDouble - f.Time;
            f.Time += shift; var impact = f.LastImpact; impact.Time += shift; f.LastImpact = impact;
            f.Epoch = epoch; f.Sequence = ++sequence;
            Frame = f;
            if (followRecordedPose) transform.SetPositionAndRotation(f.Position, f.Rotation);
            Sampled?.Invoke(f);
            if (impact.Sequence != 0 && impact.Sequence != lastImpact) { lastImpact = impact.Sequence; Impact?.Invoke(impact); }
        }
        private void OnDisable() => Stop();
    }
}
