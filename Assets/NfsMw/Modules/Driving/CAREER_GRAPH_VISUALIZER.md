# Career Graph Visualizer

The Career Graph Visualizer is a dockable editor workbench for authoring and
debugging the progression graph behind the existing career runtime. It answers
the questions designers need while building a Most Wanted-style ladder:

- What does this rival, event, milestone, car or upgrade depend on?
- Why is an event locked, and which facts would unlock it?
- Which content depends on a race, group win, bounty threshold or rival defeat?
- Are typed references missing, contradictory, cyclic or economically difficult?
- Does a synthetic profile satisfy the requirement without touching a real save?

Open it from **NFS MW Remaster > Driving > Career Graph Visualizer**. The
**Create Demo** action creates a small, valid career asset under
`Assets/NfsMw/Modules/Driving/Examples/Career/` so the tool can be evaluated before production
career content exists. Existing `CareerDefinitionAsset` files are discovered in
the Career library panel.

## Ownership and safety

The tool is an editor projection over these authoritative owners:

| Concern | Authoritative owner | Visualizer behavior |
| --- | --- | --- |
| Career IDs, content kinds and requirements | `CareerDefinitionAsset` / `CareerGraph` | Reads an isolated copy and compiles the same typed graph |
| Requirement evaluation and explanations | `CareerRequirement` | Uses the compiled requirement tree and exposes ALL/ANY/NOT/facts |
| Event group membership | `CareerGraph` | Shows distinct typed group members and count requirements |
| Mission/activity references | Existing mission and world activity assets | Displays matched links and missing-link diagnostics |
| Prices, unlock catalog and settlement scenarios | `EconomyDefinitionAsset` / existing economy owners | Shows read-only matches and runs the existing bounded simulation |
| Wallet, ownership and saves | `CareerProfileData` and save/settlement systems | Never writes live profile, wallet, ownership or save data |

The graph is not a second career engine. `CareerDefinitionAsset.Definition()` is
an isolated editor copy; `CareerDefinitionAsset.Compile()` and `CareerGraph` remain
the runtime compilation path. Source edits use Unity `SerializedObject` and
therefore participate in Undo. Canvas state is separate and never changes the
career source fingerprint.

## Graph workflow

The Graph tab provides:

- Overview, forward-dependency and reverse-dependent views.
- Search over stable key, ID, label, kind and tier.
- Filters for external references, missing references, runtime facts and tiers.
- Generated tier groups that can be collapsed or focused from the right panel.
- Pan, wheel zoom, node dragging, pinning, automatic layout and Fit.
- Colored relation edges: required, ANY, NOT, group membership, authoring link and
  read-only economy price link.
- A node panel with requirement, dependency, external-link and economy summaries.

Tier groups are derived from stable ID namespaces such as `tier.1`, `chapter.2`
and `blacklist.15`. Other IDs are placed into a namespace tier. Group collapse is
visual only: it does not disable or change runtime requirements.

Bookmarks store a camera position, zoom and selected node. Notes are draggable
canvas annotations with editable text and color. Both are stored in a sidecar
named `<CareerAsset>.CareerGraphLayout.asset`; they can be saved independently
from the career definition. Unsaved sidecar state is transient and is destroyed
on editor reload.

## Inspector and validation

The Inspector tab edits content and event-group authoring through the serialized
career asset. The validation tab reports:

- Duplicate IDs, null collections and compile errors.
- Missing or wrong-type typed references.
- Event-group duplicate members and unreachable distinct-win counts.
- Simple positive/negative contradictions.
- Positive dependency cycles and conservative NOT-polarity cycle warnings.
- Disconnected authored content.
- Unsupported reward/effect surfaces that are not present in the current runtime
  career schema.

The analyzer is conservative. A warning is not a proof that a career is
impossible, and the visualizer does not invent reward transactions, rival defeat
records, map markers or settlement rules. Unsupported findings are deliberately
visible so missing runtime contracts cannot be mistaken for completed tooling.

## Sandbox and simulation

The Sandbox tab creates a synthetic `CareerProfileData` and evaluates the current
compiled graph through `ICareerFacts`. It can set cash, reputation, bounty and
arbitrary fact overrides, complete supported content kinds and grant synthetic
vehicle/upgrade/customization ownership. It is resettable and never connects to
the active profile or save repository.

The Simulation tab invokes the existing bounded `EconomySimulation` over an
assigned `EconomyDefinitionAsset`. It reports attempts, wins, pursuits, income,
spending, fines and final cash. It does not claim to simulate race skill,
opponent AI, complete career rewards or authored thresholds that are not exposed
by the current runtime contracts.

## Reports and revision comparison

The Export menu writes a Markdown or JSON report containing:

- Career asset path, career ID, source fingerprint and compile status.
- Projected nodes and typed edges.
- Validation findings and external references.
- Optional synthetic sandbox and economy simulation summaries.
- Optional comparison against another career asset.

Compare is source-fingerprint-aware and lists added/removed content, changed
requirements or kinds, and changed external links. It is an inspection tool; it
does not merge or rewrite career definitions.

## Current schema boundaries

The current career runtime exposes requirements and typed content kinds, but it
does not yet expose a durable career reward/effect contract. Rival rewards,
marker choices, configured vehicle grants, milestone settlement and atomic
progression receipts therefore remain owned by future career integration work.
The visualizer reports that boundary instead of fabricating a parallel wallet or
save model. Mission objectives remain owned by the mission framework; world
activities remain owned by their activity definitions.

## Troubleshooting

**The library is empty.** Create or select a
`Driving > Career > Progression Definition` asset. The project may contain the
runtime contract without any authored career asset yet.

**The graph reports a missing activity or mission.** Add a
`WorldActivityDefinition` or `MissionDefinitionAsset` with the exact stable ID.
The visualizer does not infer placement from a career ID.

**The economy tab is empty.** Assign an `EconomyDefinitionAsset`; only its
read-only catalog matches and authored bounded scenarios are inspected.

**A layout is not saved.** The source asset must be inside the Unity project.
Click **Save Layout** or **Create / select layout sidecar**. A transient layout is
intentional for unsaved assets.

**Graph Toolkit is unavailable.** The supplied Unity documentation describes
Graph Toolkit as a separate editor graph package. It is not installed in this
project, so this implementation uses a UI Toolkit `EditorWindow` with an
`IMGUIContainer` canvas, standard editor controls and a Scene view overlay. The
semantic model does not depend on Graph Toolkit and can be migrated later if the
package becomes an approved project dependency.

## Unity references

- [Extending the Unity Editor](https://docs.unity3d.com/6000.6/Documentation/Manual/extending-the-editor.html)
- [Graph Toolkit](https://docs.unity3d.com/6000.6/Documentation/Manual/gtk/gtk-index.html)
- [Customizing the Unity workspace](https://docs.unity3d.com/6000.6/Documentation/Manual/CustomizingYourWorkspace.html)
- [Using custom Editor tools](https://docs.unity3d.com/6000.6/Documentation/Manual/UsingCustomEditorTools.html)
- [Creating custom overlays](https://docs.unity3d.com/6000.6/Documentation/Manual/overlays-custom.html)
- [Programming gizmos and Handles](https://docs.unity3d.com/6000.6/Documentation/Manual/gizmos-handles-programming.html)
- [Defining context for editor tools](https://docs.unity3d.com/6000.6/Documentation/Manual/define-context-for-aop.html)
