# Mission / Objective Graph Editor

This is the editor workflow for authoring and debugging missions against the
existing NFS MW Remaster mission runtime. It is an authoring/debugging surface,
not a replacement mission engine.

## Scope and distinction

The supplied `05_Mission_Objective_Graph_Editor.md` is the project-specific feature
brief. It asks for a production-oriented graph workflow, typed links, validation,
simulation, trace inspection, copy/paste, layout metadata, recovery-safe editing
and a representative branch mission.

The Unity links supplied with that request are implementation references for editor
windows, UI Toolkit, overlays, handles and graph tooling. They are not gameplay
requirements. The current project does not install Graph Toolkit, so the production
path uses a custom canvas with a persistent model that can be moved to another
visual framework later without migrating mission saves.

## Quick start

1. Open **NFS MW Remaster > Driving > Mission Objective Graph**.
2. Use **Create Demo** to create the synthetic
   `mission.demo.branching-pursuit` asset, or use **New** to create an empty event
   mission.
3. Select the mission in the left catalog. The source asset is the semantic data;
   the adjacent `<mission-name>.layout.asset` file is editor-only layout state.
4. On **Graph**, use **Add node** or right-click the canvas. Drag from a blue
   control output port to another node's blue input port to add a dependency.
   Condition/reference links are shown in orange and do not define execution order.
5. Select a node and edit it on **Inspector**. Every commit is candidate-compiled
   and recorded with Undo. Stable IDs are intentionally read-only in the normal
   inspector.
6. Open **Validation**, filter diagnostics, and click **Go** on a diagnostic to
   select its node. Resolve errors before publishing.
7. Use **Simulation** for an isolated preview. Start/reset the disposable runtime,
   inject a semantic event, and step one bounded evaluation cycle. The preview
   actions panel records requested commands but does not spawn gameplay entities,
   write a wallet/save, or settle rewards.
8. Use **Trace** to inspect active/succeeded/failed/cancelled state and the last
   transition reasons. A live `MissionHost` can be inspected read-only while the
   editor is open.
9. Use **Publish** to inspect the JSON, content hash and compile status. **Validate
   and save** writes the authored asset and layout sidecar. **Duplicate fresh IDs**
   creates a new mission identity and remaps internal references.

The Scene view **Mission Graph** overlay provides a small shortcut to open the
workbench or use the selected `MissionDefinitionAsset`.

## Window tabs

### Graph

The canvas supports node search, selection, multi-selection, pan/zoom, keyboard
navigation, node dragging, connection creation/removal, grouping, comments,
bookmarks, reroute points, minimap, collapsed groups, framing and clipboard
operations. Node positions and visual metadata are stored in the layout sidecar,
not in mission JSON.

The built-in palette maps to current runtime primitives: event, sequence, condition,
hold, timer and deadline. A node's runtime `kind` remains the authoritative
primitive identifier; adding a visual node never invents an executable method.

### Inspector

The inspector exposes the fields supported by the existing mission schema:

- objective title, kind, parent and optional/required policy;
- event type, target/marker, required count, duration, clock and failure policy;
- dependencies, sequence tokens and branch group;
- activation, success and failure conditions;
- typed action entries and their parameters;
- mission success/failure conditions, career availability, checkpoints and reward
  references.

The **Raw JSON** foldout is an intentional migration escape hatch. It can apply
structurally parseable drafts that the semantic validator will display as errors;
it does not bypass `MissionGraph` at publish time.

### Validation

Validation is classified as error, warning or informational evidence. It checks
stable IDs, titles/kinds, duplicate references, parent/dependency integrity,
condition references, action fields, branch metadata, stale layout entries and
compile failures. Reachability and solvability analysis is deliberately bounded;
the UI does not claim a mathematical proof of all player behavior.

### Variables

The Variables tab inventories channels inferred from event types, targets, markers,
branch keys, action payloads, conditions, career requirements, checkpoints and
rewards. Notes are stored in the layout sidecar for designer communication. This
panel does not create a second variable store or silently promote mission-local
facts to persistent career values.

### Simulation and Trace

The edit-mode simulator constructs `MissionGraph` and `MissionRuntime` from the
current definition. It supports event ID/type/target/fact/value input, game-time
and real-time deltas, one-cycle stepping, reset, selected-node event use and
desired-action inspection. The injected event is scoped to the preview attempt.

The runtime trace reports mission state, current result, objective status/progress,
last transition reason, condition explanation and action requests. Live attach is
read-only; it never rewinds a scene or silently executes a debug command.

### Publish

Publish shows the source revision/content hash, raw JSON and validation summary. It
also owns reload, copy-to-clipboard, layout creation, safe duplicate and save
operations. A content hash changes with semantic mission data, not with graph
positions, comments or bookmarks.

## Architecture and dependency map

```text
MissionDefinitionAsset (authored JSON)
        |
        v
MissionGraph (schema, validation, hash, dependencies)
        |
        +--> MissionRuntime (events, clocks, transitions, snapshots, results)
        |        |
        |        +--> IMissionActions / MissionWorldActions
        |        +--> MissionTrace / MissionHost inspection
        |
        +--> Career/Event references, settlement and save services

MissionObjectiveGraphWindow (editor-only)
        |\
        | +--> MissionGraphEditorModel (diagnostics, typed semantic edits, clipboard)
        | +--> MissionGraphLayoutAsset (positions, groups, comments, bookmarks)
        | +--> disposable MissionRuntime + PreviewActions (edit-mode preview)
        | +--> MissionHost (read-only live inspection)
        |
        +--> MissionDefinitionAsset through validated Undo/serialization commits
```

The editor assembly is `NfsMwRemaster.Driving.Missions.Editor` and references only
the runtime driving assembly. The test assembly references it for focused editor
model tests. Runtime assemblies contain no `UnityEditor` types.

## Authoring and runtime schemas

### Semantic mission source

The source asset remains the existing JSON-backed `MissionDefinition`. Its stable
semantic data includes mission ID/version/title, objective nodes, dependencies,
parent/branch metadata, conditions, actions, checkpoints, rewards and career
requirements. `MissionGraph` parses and validates it; the editor never writes a
visual graph object into player save data.

### Editor layout sidecar

`MissionGraphLayoutAsset` stores schema version, source asset GUID/mission ID/hash,
pan/zoom, node positions and collapsed/pinned flags, groups, comments, bookmarks,
reroute points and variable notes. It is intentionally separate so a visual edit
cannot migrate a mission instance or change settlement identity.

### Clipboard

Clipboard payloads contain selected objective definitions and their editor positions.
Paste allocates fresh objective/action IDs and remaps internal dependency, parent,
condition, branch, checkpoint and reward references. External references remain
visible for validation rather than being guessed.

### Runtime instance

Attempt identity, active node state, counters, chosen branches, timers, checkpoint
snapshots and committed outcomes remain runtime/save concerns. The editor does not
serialize runtime subscriptions, delegates, scene pointers or preview receipts.

## Invariants and safety rules

- Stable content IDs survive reordering and layout changes.
- Semantic edits validate a candidate `MissionGraph` before modifying the asset.
- Control-flow links are dependency edges and are cycle-checked.
- Condition/reference edges are typed and visible; they are not silently converted
  into execution order.
- Unknown or malformed source is kept visible as a diagnostic rather than replaced
  with fabricated valid-looking data.
- Layout, comments and bookmarks do not contribute to the semantic content hash.
- Preview uses dry-run actions and an isolated runtime; it cannot mutate live save,
  wallet, career, authored tuning or production world state.
- Runtime attach is read-only by default.
- Undo covers asset semantic changes; sidecar writes are explicit asset operations.
- Window close, play-mode transition and assembly reload dispose preview state and
  unsubscribe editor callbacks.
- The editor references authoritative settlement, persistence, entity binding and
  streaming services instead of reproducing them.

## Milestone status

| Milestone | Status in this tool |
| --- | --- |
| M01 Runtime contract and graph schema | Implemented against the existing `MissionGraph` / `MissionRuntime` contract; layout sidecar added |
| M02 Graph canvas vertical slice | Implemented: palette, typed control links, inspector, Undo, save/reload and clipboard |
| M03 Composition and condition library | Implemented for current sequence/condition/branch/optional fields and shared condition shapes |
| M04 Timers and adjudication | Reuses the authoritative runtime; editor exposes clock/duration/deadline fields and preview stepping |
| M05 Bindings and recovery | Reuses current runtime checkpoint/action/binding contracts; full streaming reconstruction remains upstream |
| M06 Settlement and persistence | References existing services; the editor does not settle rewards or change save ownership |
| M07 Simulation and live debugging | Implemented: isolated event injection, stepping, diagnostics, trace and read-only host attach |
| M08 Validation and hardening | Focused editor tests, compile checks and diagnostics implemented; large-graph benchmark and licensed Unity runner remain release gates |

## Testing and evidence

Focused editor tests are in
`Assets/NfsMw/Modules/Driving/Tests/Editor/MissionObjectiveGraphEditorTests.cs`. They cover:

- cycle rejection and valid control-link insertion;
- clipboard fresh-ID/remap behavior and layout offsets;
- missing-reference diagnostics without throwing;
- mission duplication including checkpoint/reward condition remapping;
- layout normalization and semantic edge enumeration.

The implementation was compiled with Unity `6000.6.0f1` generated response files:

```text
dotnet csc @Library/Bee/artifacts/200b0aE.dag/NfsMwRemaster.Driving.rsp ...
dotnet csc @Library/Bee/artifacts/200b0aE.dag/NfsMwRemaster.Driving.Missions.Editor.rsp ...
dotnet csc @Library/Bee/artifacts/200b0aE.dag/NfsMwRemaster.Driving.Tests.rsp ...
```

Those current-source compile checks succeeded with only existing deprecation
warnings. They are not a claim that the Unity Test Runner or a full project build
passed. The broad legacy editor assembly still has its pre-existing
`RockportMapSurface` error and is outside this feature's isolated assembly.

## Limitations and next integrations

- Add a registered extension catalog when new runtime objective/action/condition
  primitives need custom inspectors; the current palette intentionally exposes the
  primitives already present in the runtime.
- Add a first-class declared variable schema once the shared mission contract owns
  variable type/default/ownership/migration rules. The current Variables panel
  remains informational notes and inferred channels.
- Add entity-registry and streaming fixtures for unloaded-versus-destroyed target
  behavior, pooled-object rebinding, checkpoint reconstruction and retry cleanup.
- Add settlement/save fixtures for pending settlement, idempotency and active-save
  migration.
- Add large-graph timing captures on named Mac/PC target hardware before setting
  performance budgets or claiming virtualization is sufficient.
- If Graph Toolkit becomes an approved installed dependency, the semantic/layout
  boundary allows the canvas layer to be replaced without changing mission JSON or
  runtime instances.

## Troubleshooting

**The menu is missing.** Allow Unity to finish script compilation and inspect the
Console. The menu belongs to the editor-only Missions assembly; player builds do
not contain it.

**The graph is empty.** Select a `MissionDefinitionAsset` in the catalog or use
**Create Demo**. If the source JSON is malformed, the window keeps the asset
selected and shows the source error in Validation/Publish instead of silently
creating content.

**A connection is rejected.** Only output-to-input control ports create execution
dependencies. The candidate graph is cycle-checked and compiled before the edit is
committed; use a sequence/branch field when the desired relation is not a control
dependency.

**The layout is stale.** Refresh the layout sidecar or recreate it from Publish.
Stale visual entries are diagnostic; they do not alter the source mission.

**Preview actions appear but nothing spawns.** This is intentional. Preview actions
are dry-run requests. Test real actions through an isolated test scene and the
authoritative world-action services.

## Unity editor references

- [Extending the Unity Editor](https://docs.unity3d.com/6000.6/Documentation/Manual/extending-the-editor.html)
- [Graph Toolkit](https://docs.unity3d.com/6000.6/Documentation/Manual/gtk/gtk-index.html)
- [Customizing the Unity workspace](https://docs.unity3d.com/6000.6/Documentation/Manual/CustomizingYourWorkspace.html)
- [Using custom Editor tools](https://docs.unity3d.com/6000.6/Documentation/Manual/UsingCustomEditorTools.html)
- [Custom overlays](https://docs.unity3d.com/6000.6/Documentation/Manual/overlays-custom.html)
- [Gizmos and Handles](https://docs.unity3d.com/6000.6/Documentation/Manual/gizmos-handles-programming.html)
- [Defining tool context](https://docs.unity3d.com/6000.6/Documentation/Manual/define-context-for-aop.html)

