# Event Placement Studio

Open **Tools → NFS MW Remaster → Event Placement Studio**. The included scene is `Assets/NfsMw/Modules/Driving/Examples/EventPlacement/EventPlacement.unity`.

1. **Catalog:** create or choose a reusable definition. Its inspector offers race, service and mission adapters. Other types show their missing-owner diagnostics.
2. Use **Place at Scene pivot** or the **Surface placement brush**. Escape exits the brush. Use the activity tool to move a world anchor; lane/city/socket anchors require explicit binding changes.
3. **Placement:** set the interaction offset, 3D trigger, maximum speed, approach angle and dwell. Keep the icon offset independent. Multi-selected placements share serialized editing and Undo.
4. **Access:** select a road publication and explicit lane, enter a valid station, review and accept its revision. Load collision geometry. Set the largest entrant size, staging and ordered service exits. Press **Validate loaded geometry and dependencies**.
5. **Availability:** edit the existing Career requirement JSON and supply synthetic facts to inspect explanations. This is an offline preview. Runtime gated activities require `FreeRoamSession.SetActivityCareerFacts`; mission owners also need their own facts/catalog configuration.
6. **Map:** inspect GPS, entrance, staging and icon separately; set district, physical level and streaming-cell metadata. Cell metadata does not load a scene or mark content discovered.
7. **Publish:** save a new revision. The command revalidates and configures the existing race/service adapter automatically. No manual addition to the legacy session event array is needed. Export/import uses GUID references and fresh placement identity.

For unloaded map icons, create a marker catalog from the Publish view and keep that object in a persistent bootstrap scene. Publish and catalog revisions must match. Map clicks route to the road access coordinate.

The sample is a graybox garage with a floating map icon, separate road entrance and two ordered exits. Select its source, choose a `RacingVehicleSetup`, then **Run isolated service owner test**. The test uses the production controller and free-roam owner in a disposable physics scene. It intentionally replaces the storefront with an empty garage and attaches no wallet/profile; it does not test purchases or real traffic.

**Batch** previews evenly spaced placements along an explicitly selected lane interval. Committing validates every candidate and rolls back on failure. Existing hand-placed events are retained.

Use **Duplicate with fresh ID** for reusable placements. Unique definitions reject duplication. A standard Unity duplicate retains serialized IDs and will fail validation until deliberately assigned a new identity. For duplicated definition assets, use their inspector's **Assign new definition identity** button only when creating genuinely new content.

Publication assets remain after Undo so old references and rollback history survive. Do not delete an asset that is still referenced by a scene or marker catalog. Import within the same asset GUID domain resolves automatically; unresolved references require deliberate repair before import.

Supported access validation is conservative straight-corridor validation with loaded colliders. Complex curved driveways, traffic reservations, persistent discovery/completion presentation and collectible/encounter adapters require the integrations listed in `EVENT_PLACEMENT_ARCHITECTURE.md`.
