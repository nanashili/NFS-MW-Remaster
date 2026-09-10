# Career progression implementation status

## Available now

Create an asset with **Assets > Create > Driving > Career > Progression Definition**.
Set its stable ID (for example `career.rockport`), version and content nodes.
Use lowercase letters, digits, dots, underscores and hyphens for IDs.
Select **Validate and inspect dependencies** in its Inspector. Builds reject
invalid career assets, including duplicate IDs and missing/wrong-type references.

`CareerDefinitionAsset.Compile()` produces an immutable `CareerGraph`.
`graph.Get(id).Requirement.Evaluate(facts)` answers eligibility without allocation.
`Explain(facts)` returns the full logical tree with values and completion status.
`AffectedBy(fact, subjectId)` identifies direct consumers for event-driven invalidation.
`GetEventGroup(id)` exposes immutable, distinct event membership.

Implement `ICareerFacts` against an authoritative snapshot. Do not use the
authoring DTO as mutable runtime state. Boolean facts return 0/1; numeric values
must be nonnegative. Blacklist rank is 16 before entering the ladder and decreases
with confirmed rival victories. Group wins count distinct winning event IDs.
The interface is shared by runtime and simulation adapters.

Availability keeps discovery separate from eligibility. The caller supplies
authoritative discovery/completion facts; the graph does not infer achievements.
Completed content remains completed even when a prerequisite later changes.
Unknown content throws rather than becoming accidentally available.

## Not implemented yet

This is the requirement/catalog foundation, **not the complete attached specification**.
There is no live career-state adapter, reward transaction processor, rival-series
authority, milestone tracker, marker selection, map/shop binding, save migration,
career debugger, feasibility solver or cohort simulator yet. The Inspector's
dependency listing is not a graphical career editor. Build validation checks
syntax/types/references, not impossible predicates, dependency cycles, economic
dead ends or complete-path feasibility. No original-game threshold tables are
installed. Existing scenes, saves and payouts are unchanged.

Before runtime integration, implement aggregate durable commits and result
receipts so wallet, ownership, rank and claims cannot diverge. Do not wire this
graph into shop visibility while purchases still bypass the same gate.

## Verification

`CareerProgressionTests` covers thresholds, rank direction, nested ALL/ANY/NOT,
explanations, immutable compilation, malformed/cyclic expression trees, dependency
polarity, typed references, groups and discovery/availability separation.
The complete EditMode suite is the regression gate. These tests do not establish
gameplay feel, reward durability or #15 to #1 career balance.

See `CareerProgressionDesign.md` for research, architecture, evidence limitations
and the twelve incremental acceptance gates.
