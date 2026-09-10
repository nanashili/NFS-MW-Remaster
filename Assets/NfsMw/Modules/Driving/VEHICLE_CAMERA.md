# Vehicle sense-of-speed camera

Open **Racing Tools → Vehicles → Camera** (the Camera tab in Vehicle Profiles). The current scene camera and its assigned profile open together. Expand Chase or Cockpit to tune curves and strengths. Duplicate a preset as Custom before making a personal variant. During Play mode, live diagnostics show measured acceleration, slip, FOV contributions, lag, drift, vibration, look-ahead and landing response. Temporary Enabled Effects isolates individual modules without modifying the asset. Motion Blur is an independent user switch on the rig and in this panel.

The default is `Resources/VehicleCamera/MostWantedInspired.asset`. Cinematic, Arcade, Realistic, FirstPerson, Drift and Custom assets are also supplied. The FirstPerson preset tunes cockpit comfort; camera-mode switching remains the existing driving control. Existing scene rigs and the scene builder use the default profile.

## Resolution and ownership

`VehicleCameraRig` remains the only camera controller. It caches the target's controller, rigidbody, wheels and colliders when acquiring a vehicle. `VehicleCameraTelemetrySampler` reads `PhysicsSampled` snapshots and derives filtered acceleration from velocity change. `VehicleCameraPipeline` combines follow, speed FOV/distance, acceleration, steering, drift, nitrous, shake, suspension/road and landing modules. `CameraCollisionModule` corrects the result before the rig's single transform/FOV write. The HDRP adapter consumes the resolved diagnostics and never writes the pose.

The camera never changes vehicle velocity, steering, power or wheel forces. Translation feed-forward removes the old camera's speed-dependent world-space smoothing delay; measured acceleration supplies a separate bounded lag. Steering uses actual yaw rate and wheel steering angle. Drift requires speed and chassis/travel slip angle. Nitrous reads the simulation's actual active state. Landing requires sustained air time and uses the last descending velocity before contact, not button input or the largest earlier fall speed.

A grounded vehicle below 0.5 km/h with negligible yaw settles after 0.35 seconds. Its presentation anchor then ignores millimetre-scale suspension jitter; noise and acceleration input are suppressed. Movement, displacement over 12 cm, a significant rotation, camera-mode change, target change or vehicle reset releases/resets stabilization. Pausing freezes the result. Cockpit motion has independent settings and a 12 cm total translation limit by default; airborne head rotation is damped toward the horizon.

## Default tuning

FOV uses vertical degrees by default: 65° at rest, 68° at 80 km/h, 73° at 160, 78° at 220 and 82° at 300, with smooth attack/recovery. The editable curve reaches about 82.5° at 320. Measured hard acceleration can add 4°, drift 1.8°, and active nitrous 7°, subject to the 90° total ceiling. Braking compresses FOV and moves the camera forward. Cockpit contributions and the 82° ceiling are smaller.

The chase camera starts 5.3 m behind the vehicle origin, 1.75 m high, with up to 1.1 m of deliberate speed pull-back and 0.3 m of lowering. These distances are original tuning, not recovered coefficients. Asphalt defaults to low procedural roughness; gravel, dirt, grass and concrete assets carry distinct `cameraRoughness` values. Wheel suspension movement contributes separately. Custom old-asphalt or cobblestone surface profiles can author their own roughness without changing vehicle physics.

## HDRP and collision

The optional HDRP adapter creates an owned local volume on **Vehicle Camera Effects**, layer 26. It adds that volume layer to the observing camera and restores its prior membership when disabled. Mirror cameras exclude the layer. Shared weather profiles are untouched. Keep layer 26 reserved for this use. The adapter follows an existing custom volume anchor when present.

Very Low/Low apply zero speed blur; Medium uses 25%, High 70% and Ultra 100% of the configured amount. Sample quality selects HDRP's configured Low/Medium/High quality levels respectively. Default maximum regular blur is 0.18, with up to 0.08 additional nitrous blur; cockpit uses 45% of those amounts. The rig's user switch and preset permission are respected, and disabled HDRP frame settings are never forced on. Peripheral chromatic aberration is restrained and disabled in the default profile. Full-screen radial blur and speed-line rendering were omitted to preserve road/traffic readability and avoid an extra rendering pass.

Collision sweeps a sphere large enough to cover the near-plane footprint, ignores the vehicle's cached colliders and triggers, retracts immediately, and restores distance gradually. Overlap recovery handles the camera starting inside geometry. Buffers and probe state are reused. Static effect resolution and collision queries have allocation tests; the editor's live text display intentionally allocates outside gameplay code.

## Reference and evidence boundaries

The visual reference is [Most Wanted 2005 gameplay recorded by Reiji](https://www.youtube.com/watch?v=edOE_DosQdY&t=960s), particularly the BMW race around 16:00–17:40. Observed frames show a prominent car low in the image, a readable forward route, strong environmental blur near the sides and orientation changes through corners. Those observations guided composition and restrained blur. They do not establish exact dynamic FOV, spring, nitrous or landing coefficients. Those responses are original, configurable tuning based on the requested perceived effects.

[Habib Zargarpour's contemporary interview about ICE](https://www.awn.com/vfxworld/new-technology-pushing-what-possible) describes live lens and camera-position editing used in Most Wanted cinematics. It provides context for configurable camera authoring, not evidence of hidden gameplay coefficients.

## Repeatable verification

Run Edit Mode tests matching `VehicleCamera` for speed steps at 0, 50, 100, 150, 200, 250 and 320 km/h, parked jitter, acceleration/braking, nitrous, drift/corners, landing severity, suspension/surface response, cockpit limits, pause/reset, horizontal FOV conversion, 30/120 Hz consistency and steady-state allocations. Collision tests cover a wall, tunnel roof, initial overlap, obstruction recovery and allocations.

Play Mode `VehicleCameraScenePlayTests` checks live rig stopping/resuming without changing rigidbody velocity and HDRP quality/user-toggle/cleanup behavior. Set `CAMERA_SCENE_EVIDENCE` to a writable folder to run its matched-trajectory replay in an isolated copy of the project. It loads WeatherDemo's BMW, disables simulation only inside the test, and replays identical positions and velocities through the preserved old camera equations and the new pipeline. It exports CSV, paired PNGs and 24 fps frame sequences at 80/160/250 km/h. A temporary daylight corridor makes framing comparable. The replay deliberately disables the HDRP adapter on both cameras to isolate pose/FOV changes; HDRP integration is checked separately. These synthetic replays do not measure player steering precision or comfort.

Set `BMW_SCENE_EVIDENCE` to run `BmwVehicleScenePlayTests` against normal FixedUpdate driving, cockpit anchors and both mirrors. Do not run a second Unity process against a project already open in the editor; use a separate validation copy.
