# Reproducible binary validation

`run-harness.sh` compiles the checked-in `BinaryAnalysis.cs` together with a headless .NET 8 entry point using the Unity-bundled Roslyn compiler. It does not copy or commit input binaries. The report defaults to `Library/BlackBoxAudio/audio-analysis-report.json` and includes the parser source SHA-256, every input SHA-256, variant, detected byte order, bounded field/table/recording ranges, unknown spans, gaps, capabilities, parse time, and per-recording decode time.

Run a structural/decode report against private fixtures:

```bash
Tools/AudioAnalysis/run-harness.sh /private/fixtures/M3GTR_cutl.gin /private/fixtures/car_696_exh_mb.abk /private/fixtures/car_tranny.abk
```

To compare each parsed recording against optional pinned `vgmstream-cli` WAV output, set a tool path and a private reference directory. Reference names are `<input-stem>.<stream-number>.wav`, `<input-stem>-<stream-number>.wav`, or `<input-stem>.wav`; stream numbers are one-based. With strict mode, missing references or any PCM mismatch fail the command:

```bash
BLACKBOX_AUDIO_VGMSTREAM=/private/tools/vgmstream-cli \
BLACKBOX_AUDIO_REFERENCE_DIR=/private/reference-wav \
BLACKBOX_AUDIO_STRICT_REFERENCE=1 \
Tools/AudioAnalysis/run-harness.sh /private/fixtures/car_tranny.abk
```

The comparison records sample rate/channel metadata, compared frames, missing reference tails, missing parser frames, maximum absolute normalized PCM error, and mismatch count separately. A reference WAV may be signed 16-bit or IEEE float, mono or multichannel; the first channel is compared, while strict mode requires sample rate and channel count to match. The vgmstream process is invoked once per parser recording with `-i -s N -o output.wav input`; `-i` disables loop repetition so frame counts represent one physical recording.

The harness accepts direct `Gnsu`/`Octn`, `ABKC`, `BNKl`, `BNKb`, and `S10A` inputs. Direct `BNKb` is detected and explicitly reported as unsupported; it is never classified as an unknown signature. Generated reports are local evidence and should remain outside source control when they contain private fixture paths.

If Unity is installed elsewhere, set `UNITY_ROOT` to the editor `Contents/Resources` directory. The script expects its bundled .NET 8 runtime and Roslyn compiler; no system `dotnet` SDK is required.
