# Vehicle framework: start here

The framework is integrated into the existing vehicle runtime, Vehicle Profiles, Physics Lab, Racing Workspace, workshop, career saves and engine audio. Unity **6000.6.0f1**, HDRP **17.6.0**. The two supplied vehicles use original blockout geometry so their bindings are easy to inspect; replace that geometry with authored models for final art.

## Try the supplied vehicles

1. Open `Assets/NfsMw/Modules/Driving/Examples/VehicleFramework/hatch-fwd/Driving.unity` and press Play. The RWD counterpart is `coupe-rwd/Driving.unity`.
2. Drive with **WASD/arrows**, **Space** for handbrake, **Left Shift** for nitrous, and **C** for cockpit/chase camera. **B** resets the vehicle; **X** toggles the engine when allowed. Manual mode uses **E/Q** for shifts, **N** for neutral, and **R** for reverse selection at a safe speed.
3. Open **Racing Tools → Vehicles → Vehicle Profiles**, select the matching `Profile.asset`, then **Framework**. Assign the scene car under **Live vehicle**. **Driving** applies instance tuning; **Parts** installs performance parts or previews body parts. **Advanced settings** shows modifier contributions and detailed assists.
4. Preview the sport bumper kit or graphite paint, then **Apply preview** or **Cancel preview**. Closing the tool or changing the active vehicle cancels a temporary preview. The scene's workshop uses the existing wallet, ownership and purchase rules. Studio installation is an authoring operation.
5. Use the workshop's **SAVE PROFILE / LOAD PROFILE**, or send the selected configuration to the **Physics Lab** and run its experiment. The handoff includes installed parts and instance tuning. Each example uses its own career profile and garage instance ID.

The **Build Framework Examples** menu creates missing examples and validates their HDRP materials. It preserves existing definitions, profiles, prefab GUIDs and scenes. Copy an example folder before experimenting with a different car, then assign new stable IDs to the copied definition and catalog parts.

| Example | Layout | Factory mass | Peak torque setting | Final drive | Lateral grip setting |
| --- | --- | ---: | ---: | ---: | ---: |
| Compact 180 (`hatch-fwd`) | FWD | 1120 kg | 180 N m | 4.10 | 1.30 |
| Coupe 390 (`coupe-rwd`) | RWD | 1450 kg | 390 N m | 3.42 | 1.18 |

These are authored inputs, not measured performance claims. Six-second launch comparisons and the upgrade experiment are reported in [VALIDATION.md](VALIDATION.md).

## Author another vehicle

In Vehicle Profiles, create/select a draft and use the existing identity catalog to select manufacturer, model, year and variant. Assign its tuning and audio profiles under **Bindings**. Under **Framework → Overview**, create or assign a `VehicleDefinition`; keep its ID stable after saves or parts reference it. Capabilities describe actual supported features.

Set the **Assembly** body source, four wheel sources and suspension anchors. Map presentation from an explicit `VehiclePresentationBindings` component on the body source, and mirrors from `VehicleMirrorRenderer`. Set the chassis collider dimensions and semantic sockets. Source MonoBehaviours are not copied or run during assembly; known presentation data and shared mesh/material references are remapped into the generated hierarchy. Skinned rigs use an existing authored prefab; static geometry assembly rejects unsupported mappings with a repair message.

Use **Create Assembled Prefab** for first creation. Use **Update Prefab Runtime Configuration** when changing definitions, tuning, catalogs or audio on an existing prefab. Edit geometry in Prefab Mode to retain scene overrides and local object IDs. The **Build** tab publishes a store listing from the assembled prefab through the existing publication workflow.

| Authoring area | Bind or configure | Check |
| --- | --- | --- |
| Driving / Powertrain | Layout, torque curve, inertia, throttle, automatic/manual ratios, clutch, differential, limiter, induction and nitrous | Physics Lab launch, shifts and coast-down; target, estimate and measurement remain distinct. |
| Wheels / Suspension | Four anchors, radius, travel, spring/damper, wheelbase, track, mass and center of mass | Contact channels over flat ground, ramps and kerbs. Caliper bindings follow steering/suspension without wheel spin. |
| Lamps | Independent low/high, DRL, tail, brake, reverse, indicator and fog `Light[]` plus emissive `Renderer[]` | HDRP lights use the light's authored units; examples use lumens. Emission intensity is separate. Shared tail/brake lamps take the stronger active state. |
| Lighting control | `headlights.mode`, automatic lighting, indicator/hazard/fog state on the binding | Automatic lighting reads the existing atmosphere; manual low/high/off overrides it. Brake/reverse/dashboard state comes from telemetry. |
| Glass | Separate windscreen, rear and side renderers; compatible tint/finish materials | Property blocks preserve unrelated material values. Wetness, dirt and cracks are applied only to declared shader properties. |
| Windows / Wipers | Side-window pivots, opening axis/distance; wiper pivots, sweep axis and neutral poses | Rain uses precipitation; dry weather parks wipers. Pooling restores authored poses. |
| Mirrors | Rear-facing view transforms, surfaces, UV flip, FOV, clips, culling, resolution and refresh | Use a validated HDRP material and the matching texture property, e.g. HDRP/Unlit with `_UnlitColorMap`. Camera and surface must both show the rear scene. |
| Cockpit | Steering pivot/axis/neutral/ratio/range, speed/RPM needles, gear text/lever, pedals and optional hand targets | Steering follows road-wheel angles. Bind the driver camera anchor to the existing camera rig; its feedback compositor applies motion once. |
| Parts / Kits | Stable IDs, variant compatibility, dependencies, exclusions, claimed slots, visual payload and explicit physical modifiers | A complete candidate is validated and visuals prepared before publication; rejection preserves the previous build. |
| Audio / Effects | Existing `VehicleSensoryProfile`, `VehicleAudio` and effects pipeline | RPM/load/gears/boost/slip follow production telemetry. Keep decoded audio assets local. |
| Diagnostics / Budgets | Authoring warnings, Player/Opponent/Traffic/Parked/Garage role, HDRP quality preset | Unsupported bindings remain disabled; live mirrors are limited to eligible player/garage vehicles. |

## Parts and tuning rules

Author performance upgrades with the existing `TunedVehiclePerformanceUpgrade` and catalogs. Modifiers can address engine, induction, transmission, clutch/differential, tyres, brakes, suspension, mass, aerodynamics and nitrous. The resolver rebuilds from factory data in a deterministic order, then applies bounded absolute tuning adjustments. The advanced breakdown shows each contributor, before/after values and units. Removing a part restores the correct baseline without repeated multiplication.

Use `AssetVehicleCustomization` for individual visual parts or kits. A kit can claim multiple semantic slots; its dependencies, exclusions and vehicle/variant compatibility are checked together. Wheel radius/offset/track/clearance metadata must satisfy fitment bounds. A visual change affects physics only through explicit physical metadata/modifiers. Paint, finishes, texture/livery changes and unsupported camber/width effects remain cosmetic.

Use the existing store products for price, unlock, ownership and purchase behavior. Preview never grants ownership or changes a career save. Persistent changes go through `CareerProfileSystem`; a bad saved part or tuning value returns a clear validation failure instead of silently loading part of a build.

See [INTEGRATION.md](INTEGRATION.md) for ownership and module order, [MIGRATION.md](MIGRATION.md) for existing vehicles/saves, and [REFERENCE.md](REFERENCE.md) for reference confidence.
