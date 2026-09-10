# Existing vehicle and save migration

This is an additive framework change within Unity 6000.6.0f1 / HDRP 17.6.0. It does not upgrade Unity, replace the render pipeline or convert all existing car art. Existing scene, tuning, model, audio and prefab `.meta` identities remain authoritative. The delivered example assets have their own new GUIDs.

## Existing prefabs

1. Preserve a copy of the prefab and its `.meta` before an intentional authoring migration. Keep the existing `VehicleController`, wheels, audio profile, catalogs and career components.
2. In Vehicle Profiles, assign the existing factory tuning to a `VehicleDefinition` and choose stable vehicle/variant IDs. The definition ID identifies a factory variant; it is distinct from a garage instance ID. Add supported slots/capabilities only for features actually authored on this model.
3. Assign that definition to the draft and use **Update Prefab Runtime Configuration**. This adds/configures the shared configuration modules without rebuilding model geometry. Bind optional lights, cockpit, mirrors, glass and sockets in Prefab Mode. Scene-specific camera/audio/effects references are prefab overrides and must be recorded by editor code.
4. Validate the draft and open an instance. Install/remove a compatible part, preview/cancel a body part, drive, then save/load through the existing career system. Use **Send configuration to Physics Lab** to compare effective values and driving behavior.

A prefab without `VehicleDefinition` continues using its existing tuning and wheel driven flags. `VehicleDriveLayout.LegacyBindings` remains serialized value **0**. Explicit FWD/RWD/AWD are opt-in, so old wheel flags are not silently reinterpreted. Existing controller input references remain supported; new assembled player vehicles use `VehicleInputAuthority`.

## Save compatibility and failure handling

The existing profile codec/repository owns storage and migration. Per-vehicle schema **2** adds factory definition ID, variant ID and tuning adjustments alongside the existing performance/customization ID arrays. Legacy vehicle records may omit definition/variant identity and adjustments. Their `vehicleId` remains the garage instance ID; it is never substituted for the factory ID.

Restore validates the complete saved candidate against current catalogs, dependencies, exclusions, fitment and tuning limits. The visual preparation step completes before performance IDs and adjustments are published. The career restore transaction can roll back other participants if a later participant fails. Unknown schema versions, identity mismatches, non-finite/out-of-range tuning, missing parts and incompatible parts return an explicit failure and retain the existing live/committed state.

For a missing part, restore the catalog entry with its original stable ID or deliberately migrate the saved ID through the existing save workflow. For a definition mismatch, spawn the saved definition before restoring it. For tuning on a legacy prefab without a definition, assign/validate a definition first. Do not clear invalid IDs, partially apply a rejected save, edit the wallet directly, or overwrite the previous valid save to hide a validation problem.

Existing profile-level versions and legacy candidate-file handling remain owned by `CareerSaveCodec` / `CareerSaveRepository`. Legacy files are not implicitly deleted or archived by this framework migration. Tested legacy vehicle data is stamped with current identity on the next successful capture.

## Authoring and asset identities

`VehicleProfileDraft` schema 1 advances to schema 2 on validation while retaining the same draft ID, tuning, audio and prefab references. New runtime-definition and assembly fields remain explicit authoring choices. Existing Physics Lab parameter enum values are retained for saved tuning; new enum members are appended rather than renumbered.

Prefab configuration updates preserve the asset GUID and existing model binding local IDs. Creating a prefab is first-create only. Geometry updates stay in Prefab Mode; the framework does not overwrite a canonical source model from a temporary preview. Preserve stable IDs when renaming labels or moving assets. Renaming a socket or changing a part ID is a semantic migration even if its Unity GUID does not change.

Configuration, installed parts and preview state are separate. Editor Undo/Redo invalidates cached builds and rebuilds from serialized installed IDs; closing or changing the active authoring context cancels preview. Runtime changes resolve into a per-instance tuning copy and do not mutate a shared ScriptableObject.

See [VALIDATION.md](VALIDATION.md) for the migration, rollback, Undo/Redo, pool reset and scene-reload checks actually executed.
