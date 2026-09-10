# World Validation Dashboard

The dashboard gives one editor workflow for checking roads, routes, event
placement, missions/career, traffic/police authoring, world art, maps,
lighting, audio zones, adaptive music, and runtime-scenario coverage. It
coordinates existing owner validators; it does not become a second gameplay or
world-authoring system.

## Open it

Use:

`NFS MW Remaster > Validation > World Validation Dashboard`

The window is dockable like other Unity editor windows. The Scene view also
contains a `NFS World Validation` overlay with the latest status, a dashboard
button, and a shortcut to the first blocking result. The overlay's Handles are
navigation feedback only and never edit content.

Use `NFS MW Remaster > Validation > Create Default Policy` to create the
reviewable policy asset at:

`Assets/NfsMw/Modules/Driving/Data/WorldValidation/WorldValidationPolicy.asset`

## Run a scan

1. Select a scope.
2. Optionally choose one category, enable `Incremental`, enable `Expensive`,
   or turn off `Info` messages.
3. For `ExplicitScenes`, enter semicolon-separated `Assets/.../*.unity` paths.
4. For `DistrictCell`, select a `CityDistrict` for the full district, select a
   `CityGeneratedInstance` for its derived cell, or enter a stable `District ID`
   and semicolon-separated `Cells` such as `0,0;0,1`.
5. Select `Run validation`.
6. Use the Diagnostics tab to search/filter, select a finding, inspect its
   evidence and source revision, then navigate to the owner.

The Dashboard tab shows the summary and per-category coverage. The Dependencies
tab shows exactly what was scanned, omitted, unavailable, and which rules were
unsupported. A green result means only that the provider evaluated its
discovered inputs; it is not a claim that unloaded or omitted content is clean.

## Rules and evidence

Rules have stable IDs and are owned by the module that understands their
semantics. Current important IDs include:

- `world.identity.references`
- `roads.authority`
- `races.routes`
- `events.placements`
- `missions.career.compile`
- `missions.graph.authoring`
- `traffic.police.authoring`
- `world.art.budgets`
- `world.city.publication`
- `maps.publication`
- `lighting.atmosphere.audit`
- `audio.zones.authoring`
- `audio.music.arrangement`
- `runtime.scenario.evidence`

Each result records the rule/version, status, severity, code, owner module,
asset or scene target, source revision, stable affected IDs, evidence, and
whether it is heuristic, empirical, stale, navigable, or fixable.

Static texture and mesh budgets are marked as heuristic estimates. Runtime
fairness, dynamic spawn behavior, render quality and audio timing are not
declared valid by static metadata alone.

The city adapter delegates to `CityBuildGuard.Validate` and does not rebuild
city generation semantics. A district scope reports source, publication,
ownership and cell-budget diagnostics for the complete district. A cell scope
filters diagnostics with a resolvable cell or parcel/output owner while keeping
district-wide source errors visible. The report records the selected district
and canonical cell keys so a filtered pass cannot be mistaken for a full-city
pass.

## Policies, suppressions, and baselines

Policies hold budgets plus targeted suppressions and baselines. A suppression
matches the rule ID and can additionally match an affected ID, source revision,
and asset/scene scope. Give every suppression a reason; optional expiry and
author fields support review. A baseline matches a stable result key and
records existing debt. Baseline debt remains visible and does not become a
pass. When the source revision changes, the finding is classified as new so an
old waiver cannot hide a new copy of the defect.

The current dashboard intentionally has no generic “fix everything” button.
Domain owners can register an `IWorldValidationFixProvider` with a preview,
risk, affected IDs, and rollback description. When a result advertises one,
the Diagnostics details view renders that provider-owned preview without
mutating content. Applying a repair remains a domain-owner operation; a future
fix must validate its post-state and report failure without claiming success.

## Batchmode / CI

From the project root, run Unity with the checked-in editor and execute:

```text
Unity -batchmode -nographics -projectPath "." -executeMethod NfsMwRemaster.Driving.Editor.WorldValidation.WorldValidationBatch.Run -worldValidationScope BuildContent -worldValidationOutput "Reports/world-validation.json" -worldValidationMarkdown "Reports/world-validation.md" -worldValidationExitPolicy NoBlockers -quit
```

Supported arguments:

```text
-worldValidationScope OpenScenes|SelectedObjects|DistrictCell|ExplicitScenes|ChangedAssets|BuildContent|Project
-worldValidationCategories comma-separated category enum names
-worldValidationRules comma-separated stable rule IDs
-worldValidationScenes semicolon/comma-separated Assets scene paths
-worldValidationDistrictId stable CityDistrict ID (DistrictCell scope)
-worldValidationCells semicolon-separated x,z or districtId|x,z tokens (DistrictCell scope)
-worldValidationPolicy Assets/.../WorldValidationPolicy.asset
-worldValidationTimeout seconds
-worldValidationExitPolicy NoBlockers|NoErrors|AllRequestedRulesEvaluated
-worldValidationExpensive
-worldValidationIncremental
-worldValidationNoInfo
-worldValidationOutput path.json
-worldValidationMarkdown path.md
```

Exit codes are `0` for the selected policy, `1` for a policy failure, and `2`
for report/export/runner failure. Reports are deterministically ordered and
include partial/stale/omitted coverage. Headless completion does not substitute
for a graphics/audio-capable test when the report says those capabilities were
unsupported or not evaluated.

## Dependency index and history

Every completed run updates:

`Library/NfsMwRemaster/WorldValidation/dependency-index.json`

and writes a local history report under:

`Library/NfsMwRemaster/WorldValidation/history/`

These are derived editor artifacts and should not be treated as runtime save
data. Rebuild the index from the Dependencies tab after importing a large set
of assets or changing the project's dependency layout.

## Extending it from a module

Reference `NfsMwRemaster.Driving.Editor` from an editor-only module assembly,
derive from `WorldValidationRuleBase`, declare a stable descriptor, implement
`Evaluate(WorldValidationContext, WorldValidationResultSink)`, and register the
rule from an `[InitializeOnLoad]` static constructor. Use the owner module's
validator and immutable/read-only inputs. Emit `NotEvaluated` or `Unsupported`
when the owner cannot inspect a requested scope. Add stable affected IDs and
evidence instead of encoding meaning only in a log string.

The provider must be resilient to one rule throwing: the scheduler captures it
as `ErrorRunning` and continues independent rules. Long checks should call
`context.ThrowIfCancellationRequested()` inside their loops. Avoid saving
scenes, mutating runtime state, or repairing ambiguous references during a
read-only scan.

## Troubleshooting

- `Not evaluated`: inspect the result message and Dependencies tab; enable
  `Expensive` or provide the missing owner input.
- `Unsupported`: the owner adapter does not claim that scope. Use the scope it
  documents instead of assuming an unloaded-scene check ran.
- `Partial`: a scene is unsaved/unavailable, an incremental input was omitted,
  or a rule was not runnable. Resolve the scope evidence before using a clean
  result as a gate.
- `Stale`: source revisions changed during the run. Rerun after imports finish.
- No changed assets: seed/rebuild the dependency index, then rerun incremental
  validation.
- A missing adapter: check that the module editor assembly references
  `NfsMwRemaster.Driving.Editor` and that its registration class is loaded.
