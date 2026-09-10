# Most Wanted reference-mode scene rollout

This rollout applies the reference system to the existing saved scene content. It does not rebuild scenes or replace cars, cameras, lighting, terrain, routes, audio, customization, career components or prefab bindings.

## Coverage

The migration scans **all 51 scene assets**, not only enabled build scenes. **21 driving scenes contain 216 vehicles**, including inactive traffic/police pool members and prefab instances. Their effective tuning is checked in Unity, including definition priority and scene-embedded ScriptableObjects.

The other **28 regular scenes** have no vehicle physics to migrate. They remain unchanged; a menu, editor demonstration, empty workspace or streamed terrain chunk does not need a duplicate vehicle controller. Recovery snapshots under `Assets/_Recovery`, when present, are excluded from migration and preserved.

The driving scenes are DrivingDemo, FreeRoam, WeatherDemo, PursuitTest, SensoryTest, ShopTest, all nine traffic scenarios, the three road-driving/authoring examples, CompoundCorner, and the hatch/coupe framework examples.

**20 tuning profiles** are migrated: 15 shared `.asset` profiles and five scene-embedded profiles. Existing tuning GUIDs and definition IDs remain unchanged, so vehicle definitions, drafts, Physics Lab setups, prefabs, spawning and pooled instances continue resolving the same assets. Three Vehicle Definitions receive updated provenance text. The data rollout changes **23 existing files** in total: 15 tuning assets, three definitions and five scenes.

## Source import versus authored adaptation

The shared street-racer tuning, BMW framework tuning and five embedded player profiles use the captured BMW first-linked source scalars. The selected indexes are explicit data choices, not a claim about the original game's active upgrade state.

Imported tyre dimensions are **not** applied as mesh resizing. Authored wheel radius/width, wheel anchors, mesh scale, center of mass, suspension rest length and travel are retained. In particular, the BMW framework's `0.46782207 m` authored radius is preserved instead of replacing it with the decoded `0.3433 m` radius.

Anonymous police, civilian and demonstration profiles use a clearly labelled **reference-mode adaptation**. They retain their own mass, peak torque, gear ratios, drive layout, brakes, springs, contact dimensions and vehicle identity. Donor torque/braking curve shapes are used on the authored RPM domain; there is no claim that a van or generic coupe has become an accurately decoded BMW or GTI. Civilian and police controllers retain their existing AI steering shaping and shift thresholds instead of receiving gamepad input-history steering on top of their control law.

The exact import/adaptation policy and field provenance are stored per profile in `migration.json`. General PC physics reconstruction boundaries remain in `REVERSE_ENGINEERING.md`: complete tyre/contact/clutch fidelity and original-game driving parity are not established by this rollout.

## Idle-braking correction

Post-migration testing exposed a defect in the reference-mode Unity adapter: negative engine torque could act as a reverse drive motor at rest. Runtime revision `shared-vehicle.mw-reference.6` routes negative engine torque through the wheel solver's dissipative braking channel. The integrator cannot reverse a stationary wheel using engine braking, while positive combustion torque in reverse remains propulsion. This is an explicit Unity solver adaptation; the scalar source torque/braking equations are unchanged. Authored-mode behavior is unchanged.

## Preservation and publication

Migration runs first in a closed, task-owned Unity project copy. Shared tuning assets are migrated in place to preserve GUIDs. Embedded tuning is identified through each owning controller's serialized local reference, because embedded ScriptableObjects can return a zero GlobalObjectId before saving.

After Unity serializes an embedded candidate, only that tuning object's YAML payload is transplanted into the original scene text. All scene headers/local IDs and every non-tuning byte are retained. The publisher independently verifies this invariant before applying anything to the live project.

`publish-scene-migration.py` requires passing migration tests, unchanged source-capture hashes, matching original-file backups, matching staged output hashes and matching staged/live C# sources. A concurrent edit fails preflight instead of being overwritten. Publication uses atomic file replacement and records exact pre/post hashes. Original bytes are retained under the publication evidence directory's `Before/Assets/...` tree. Models, prefabs, `.meta` files and build settings are not publication targets.

No open user scene is force-saved or reloaded. **Stop and restart Play Mode after the rollout. Reopen any already-open scene containing embedded tuning to load its updated on-disk values; preserve unrelated unsaved scene edits first.**

## Validation and reruns

The final test counts, publication directory and repeat-application result are recorded in `SCENE_MIGRATION_VALIDATION.md`. Coverage includes saved scenes, candidate isolation, authored geometry, source statistics, prefab/definition precedence, configuration changes, career restore and pool reuse, in addition to the existing vehicle regressions.

`MostWantedSceneMigration.ApplyInValidationCopy` is the explicit batch migration entry point. It requires the `.mw-driving-validation-source` marker and a new `MW_SCENE_MIGRATION_OUTPUT` directory. Reapplying the migration skips profiles already in reference mode rather than recalibrating user changes.

Do not run the old `validate.sh --workspace ...` preparation step over an unpublished migrated copy: preparation synchronizes original Assets and would replace staged data. For the migration phase, synchronize source code only, run Unity tests directly against the staged copy, then publish the hash-verified changes. After publication, the normal validation script can again copy the current project safely.
