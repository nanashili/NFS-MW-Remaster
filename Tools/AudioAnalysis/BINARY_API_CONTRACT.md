# Binary evidence API contract (M1/M3)

The pure C# instance entry point is `NfsMwRemaster.Driving.AudioAnalysis.BinaryAnalysisParser.Parse(byte[] data, string sourcePath)`. It never mutates input and returns a bounded partial report on malformed input. `BinaryAnalysisParser.ParseBytes` is the static convenience wrapper over a default parser.

The report keeps source identity (`SourceEvidence`: path, SHA-256, byte length), variant, raw field evidence (`FieldEvidence`: byte range, raw hex, decoded value), structural tables, logical references, physical recordings, gaps and independent capabilities. A recording is decoded at most once and carries source payload offset, sample rate, channel count, valid frame count and interleaved float PCM. GIN table values are raw u32 plus optional IEEE-754 view; no RPM meaning is assigned by this layer.

Supported evidence is deliberately narrow: Gnsu/Octn GIN structure and EA-XAS v0 mono PCM; ABKC module/player/sample traversal with LE/BE table selection; observed BNKl v5 PT00 mono EA-XA v2 PCM/ADPCM recordings; and observed S10A v0 codec 4 mono RAM EA-XAS v1 recordings. Direct BNKl, BNKb, and S10A signatures are detected explicitly; direct BNKb is reported unsupported rather than unknown. BNKl revisions/codecs outside that observed v5/PT00/EA-XA-v2 shape, EA SNR/SNS variants outside the observed S10A subset, AST/SCHl control evaluation, RPM semantics and native profile conversion remain explicit gaps.

Limits are public constants and are enforced before allocation/traversal. Parse errors become gaps with byte ranges when known; successful structural parsing never implies playback/control support.
