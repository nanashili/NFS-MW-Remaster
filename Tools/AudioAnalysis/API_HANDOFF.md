# Integration handoff

Use `new NfsMwRemaster.Driving.AudioAnalysis.BinaryAnalysisParser().Parse(bytes, sourcePath)` (or `BinaryAnalysisParser.ParseBytes`) from the scanner/presenter. The report's stable top-level properties are `Source`, `Variant`, `DetectedByteOrder`, `Fields`, `Tables`, `Recordings`, `References`, `Gaps`, `Capabilities`, `IsPartial`, and `ParserVersion`.

`Recording.Pcm` is interleaved float PCM (mono for verified GIN), normalized only by the codec's signed 16-bit scale; no resampling/downmix/trim. `Recording.Payload` is the encoded source byte range. `ValidFrames` excludes incomplete physical EA-XAS blocks. `TableEntry.RawValue` and `FieldEvidence.RawHex` are the lossless values to use for any future semantic investigation. `LogicalReference` is deliberately separate from `Recording`.

Treat `CapabilityStatus.Supported` as format-subset support only. Original RPM mapping, control evaluation, external SNR/SNS and SCHl/AST playback remain blocked. The observed S10A RAM subset decodes through EA-XAS v1; this is not general EAAC support. Native conversion belongs to `BlackBoxNativeMapping`, which supports reviewed authored approximations and blocks verified original-control conversion. `AnalysisGap.NextExperiment` supplies actionable follow-up context for the UI.
