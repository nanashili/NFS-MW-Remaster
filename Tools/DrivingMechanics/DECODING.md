# Read-only Most Wanted handling decoding

## Entry points

`MostWantedHandlingReader.Decode(gameFolder, vehicleNames = null)` is a pure C# read-only API in `NfsMwRemaster.Driving.Editor.DrivingMechanics`. It returns a serializable `MostWantedHandlingReport`; it does not write a report, create assets, assign tuning, launch Unity, or execute the game. Null vehicle selection means every `pvehicle` row, including hash-only and template rows. Explicit selection uses exact, case-sensitive VLT collection names or `0xXXXXXXXX` keys. An empty selection is rejected.

`DecodeSources(IReadOnlyList<MostWantedHandlingSourceInput>, vehicleNames = null)` accepts named buffers for tests and approved copies. It snapshots each buffer and reports snapshot verification, **not** verification of an installation that was never opened. Neither API needs CARS models or engine-audio eligibility to admit a vehicle.

The file API reads `GLOBAL/attributes.bin`, `GLOBAL/FE_ATTRIB.bin`, and `GLOBAL/gameplay.bin` using `FileAccess.Read`. Relative source filenames, SHA-256 and byte lengths are retained. Hashes are recalculated after decoding and a mismatch aborts the operation. Executable version and physics parity are not identified by the pack format.

## Standalone validation and capture

Run from the project root:

```sh
bash Tools/DrivingMechanics/run-reader.sh --self-test
```

This compiles only the shared reader, reader, report model, synthetic tests and a small console host with the installed Unity .NET compiler. It invokes the synthetic NUnit tests directly; it does **not** start the Unity Editor or run a Unity test session. Build products are under `Library/DrivingMechanics`. The explicit installation integration test is excluded from this default run.

After tool access to the installation is explicitly permitted:

```sh
bash Tools/DrivingMechanics/run-reader.sh \
  --game "$BLACKBOX_GAME" \
  --out "$PWD/Tools/DrivingMechanics/Evidence/handling-local-unique.json" \
  --vehicle bmwm3gtre46 --vehicle gti --vehicle punto
```

Omit every `--vehicle` argument to capture all database vehicles, not just those three examples. Use a new output filename each time. The host refuses existing outputs, output under the source installation, symbolic-link output ancestors, and destinations outside `Tools/DrivingMechanics/Evidence` or `Library/DrivingMechanics`. Historical `Tools/VehicleFramework/Evidence/Baseline/reference-data.json` is not a permitted output.

Full assembly compilation remains available through the existing `bash Tools/AudioAnalysis/compile.sh`, which also does not launch Unity. Unity execution and explicit installation tests require coordination with prime.

## Captured local installation: 8 September 2026

Prime successfully captured the actual installation in `Evidence/handling-local-20260908.json`. It contains **3 vehicles (`bmwm3gtre46`, `gti`, `punto`), 39 reached records and 49 explicit `unsupported-field` issues**. All three source files were hashed before parsing and after decoding; every hash matched. The report remains `complete=false` because unsupported fields are preserved, not because all handling fields failed. This is a completed file capture, not a synthetic snapshot or an unrun integration claim.

| Source file | Bytes | SHA-256 before and after |
| --- | ---: | --- |
| `GLOBAL/attributes.bin` | 689,728 | `3d0362f2c052b2f93576852f504cb7806b14eefbf83b8531d45dc1d430ec6337` |
| `GLOBAL/FE_ATTRIB.bin` | 113,088 | `b4f78df4a83f3d36e961b43cfd635ed1750666a051260e27620cc2f254b9bcca` |
| `GLOBAL/gameplay.bin` | 2,105,216 | `6eb317c578ab44176fa0bdd2d1b530bf4dfbae0f648980ee3af72212ae391314` |

The capture JSON SHA-256 is `47bdd24fb8ef208c0ec41f920944d388588603b404a6855b5313be8791984614`. Historical `Tools/VehicleFramework/Evidence/Baseline/reference-data.json` remains a separate, unchanged artifact with SHA-256 `17a9d3a9b68387e2fd6bb3a7c9bcaa76ee5fe4595c4b2b557126471c2bbc3ccc`. Neither is overwritten by capture or importer tests.

`MostWantedDrivingImporterTests` reads this preserved JSON without reopening the game installation. Every test deep-clones its managed evidence graph before mutation. The fixture covers explicit slot-zero BMW/GTI/Punto mapping, IDLE/MAX_RPM versus RED_LINE torque domains, forward/reverse gear and efficiency indexing, per-axle unit mapping, braking, induction, nitrous, provenance, rejection of required-field/hash/inheritance defects, captured JSON validation, and create-only save behavior. Its teardown checks both evidence-file hashes and the shared fixture graph. Asset-save tests use unique disposable `Assets/__MostWantedImporterTests_*` folders; malformed JSON uses `Library/DrivingMechanics/ImporterTests-*`.

For prime's isolated Unity test project, copy the two fixture files at the same relative paths or set `MOST_WANTED_DRIVING_TEST_ROOT` to the original project root. This changes only where these tests read their fixtures; temporary files and saved assets stay in the isolated project. Filter by `NfsMwRemaster.Driving.Tests.MostWantedDrivingImporterTests` or category `DrivingMechanicsOffline`. The fixture does not require `BLACKBOX_GAME`, networking, a running game, or access to the original installation. Tests are prepared for prime's coordinated execution; compilation alone does not establish a test pass.

## Graph and typed data contract

Each vehicle points at its `pvehicle` record. Its `links` retain every handling reference, original field name/hash, reference type, array index and stored target class/collection keys. Array indexes are zero-based; scalar references use index `-1`. Repeated references to the same target remain repeated links. Only target **records** are deduplicated. The target class in a reference is authoritative; a field called `brakes` never causes the reader to replace a different stored class with the brakes class.

Target record IDs join `vehicle.links[].targetRecordId` to `report.records[].id`. Records expose `fields[]`; field names such as `MAX_RPM`, `GEAR_EFFICIENCY`, `BRAKES`, `SECTION_WIDTH`, `RIM_SIZE`, `ASPECT_RATIO`, `PSI`, and `NOS_CAPACITY` are hash-matched labels. Unresolved labels remain hexadecimal keys instead of disappearing. All declared fields on reached engine, transmission, tires, chassis, brakes, nos, induction, rigidbodyspecs and junkman rows are emitted, not just a small list of familiar numbers.

The `kind` discriminator determines which value members apply:

| Kind | Payload |
| --- | --- |
| `float32` | `numericValue` is the exact finite float promoted to double; `floatBits` and `numberText` preserve the source representation. |
| `int8`, `int16`, `int32` | `signedValue`; no conversion through float32. |
| `uint8`, `uint16`, `uint32` | `unsignedValue`; full UInt32 precision is preserved. |
| `boolean` | `booleanValue`; noncanonical source bytes are explicitly unsupported. |
| `axle-pair` | Two components named `Front`, `Rear`, each with exact float bits/text and a finite numeric value. |
| `vector2`, `vector3`, `vector4` | Components in stored x/y/z/w order, without axis conversion. |
| `ref-spec` | `referenceClassKey`, `referenceRowKey`; the third pointer word remains in original/relocated bytes and uninterpreted words. |
| `string-key`, `text` | Decoded source text, with the original payload bytes retained. |
| `junkman-mod` | `referenceClassKey`, **definitionKey** (not a collection reference), and a Scale component. No modification is applied. |
| `upgrade-specs` | Collection key and original bytes retained; currently unsupported because the remaining layout/stage semantics have not been established by this reader's evidence. |
| `unsupported` | Original type hash, payload width and bytes retained with a reason. No reinterpretation as float. |

Array capacity, count, header bytes, element width and indexes are retained. A decoded count of zero is different from a missing field, an unsupported type or malformed storage. Type hashes **and** storage widths must match. Non-finite floats keep their exact bits and text but are unsupported; no NaN/Infinity is written into JSON numeric fields.

## Provenance and confidence

Locations contain source-relative filename, source SHA-256, vault index, segment (`bin` or `vlt`), segment start, segment-local offset, and absolute original VPAK offset. Offsets are file locations, not runtime addresses. Field definition origins, row origins, array item origins and declaring-owner origins are separate. Pointer fixups apply only to reader-owned byte copies. `rawHex` comes from the pre-fixup bytes; `relocatedHex` comes from the reader's relocated copy. The caller's source bytes remain unchanged.

Each field identifies its declaring row and the inheritance path from the requested row through that owner. The record also carries its full row lineage. A missing parent or cycle marks `inheritanceResolved=false`; only that row's own fields are retained, and they are not represented as a complete inherited result.

`report.complete` is false when **any** decoded field or graph resolution is unsupported. Because all pvehicle fields are retained, unrelated audio/FX types can make the report incomplete even when required engine fields decode. Consumers must examine each required field's `status`, each link's `status`, and the target record's `inheritanceResolved`. A missing field, empty array, null reference, missing target, unsupported target class, or unknown type must never be converted to an invented default by the decoding layer.

Source bytes are limited to 32 MiB per pack and 128 MiB total, with 1–16 named input packs. The shared reader limits vault counts, field counts, array capacity and inheritance depth. Analysis loading additionally caps 128 MiB of cumulative vault-segment bytes, 250,000 field definitions, 100,000 source rows and 1,000,000 source values, protecting against repeated segments/shared layouts expanding a small pack into excessive reader objects; these additional limits do not change legacy `Load` calls. Resolved reports additionally cap 20,000 records, 250,000 fields and 1,000,000 values. Exceeding a budget fails explicitly rather than truncating evidence.

## Structural references, not conversion authority

The existing reader's [VaultLib layout reference](https://github.com/NFSTools/VaultLib/tree/c1c9e94044ef0eae9ebc43c5a2b3a70ef5369d86) and MIT notice remain in `Tools/AudioAnalysis/THIRD_PARTY.md` and `Notices/VaultLib-LICENSE.txt`.

The [pinned reconstruction](https://github.com/dbalatoni13/nfsmw/tree/13189413c4c6e447c2225052c66b55d76863b985) supplies comparison declarations: `src/Speed/Indep/Src/Misc/MWAttribUserTypes.h:69-77` (AxlePair), `:94-99` (JunkmanMod), `src/Speed/Indep/Tools/AttribSys/Runtime/AttribSys.h:918-955` (RefSpec), and the generated `AttribSys/Classes/{pvehicle,engine,transmission,tires,chassis,brakes,nos,induction,rigidbodyspecs,junkman}.h` headers (candidate field labels). These establish structural interpretations used with the PC pack's declared type/size. They do not establish physical units, torque sample spacing, upgrade activation, or PC executable behavior.

Reference/formula evidence and the runtime mapper belong to their separate owners. This reader performs no physical conversions, curve normalization, blending, stock-stage selection or Unity tuning writes. Downloaded comparison source stays in Library and is not compiled or executed.
