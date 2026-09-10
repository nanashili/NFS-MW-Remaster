using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    internal sealed class VehiclePhysicsLabFixture : IDisposable
    {
        private readonly Scene scene;
        private readonly VehiclePhysicsLabTrack track;
        private readonly float halfExtent;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private GameObject root;
        private bool disposed;

        public VehiclePhysicsLabFixture(Scene previewScene, VehiclePhysicsLabTrack definition, float fixtureHalfExtent)
        {
            scene = previewScene;
            track = definition;
            halfExtent = Mathf.Max(100f, fixtureHalfExtent);
            root = new GameObject("Vehicle Physics Lab Fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            Build();
        }

        private void Build()
        {
            switch (track.kind)
            {
                case VehiclePhysicsLabTrackKind.SurfaceSweep:
                    BuildSurfaceSweep();
                    break;
                case VehiclePhysicsLabTrackKind.Ramp:
                    BuildRamp();
                    CreateSurfaceBox("Ramp landing", new Vector3(0f, -0.25f, track.length + halfExtent * 0.5f),
                        new Vector3(halfExtent * 2f, 0.5f, halfExtent), track.surfaceId + " landing",
                        track.surfaceGrip, track.surfaceRollingResistance);
                    break;
                default:
                    CreateSurfaceBox("Lab road", new Vector3(0f, -0.25f, track.length * 0.5f),
                        new Vector3(halfExtent * 2f, 0.5f, Mathf.Max(halfExtent * 2f, track.length + 40f)),
                        track.surfaceId, track.surfaceGrip, track.surfaceRollingResistance);
                    break;
            }

            if (track.kind == VehiclePhysicsLabTrackKind.ContactArena)
            {
                BuildContactWalls();
            }
        }

        private void BuildSurfaceSweep()
        {
            float stripLength = 10f;
            int count = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(track.length, 40f) / stripLength), 4, 256);
            for (int i = 0; i < count; i++)
            {
                float center = i * stripLength + stripLength * 0.5f;
                float grip = Mathf.Lerp(0.55f, 1.2f, i / Mathf.Max(1f, count - 1f));
                CreateSurfaceBox("Surface sweep " + i, new Vector3(0f, -0.25f, center),
                    new Vector3(halfExtent * 2f, 0.5f, stripLength + 0.02f),
                    track.surfaceId + " " + i, grip, track.surfaceRollingResistance);
            }
        }

        private void BuildRamp()
        {
            float rampLength = Mathf.Clamp(track.rampLength, 1f, Mathf.Max(1f, track.length));
            float start = Mathf.Max(0f, track.length - rampLength);
            float height = Mathf.Max(0f, track.rampHeight);
            float extent = Mathf.Max(10f, track.width * .5f + 5f);
            if (start > 0f)
                CreateSurfaceBox("Ramp approach", new Vector3(0, -.25f, start * .5f),
                    new Vector3(extent * 2f, .5f, start), track.surfaceId + " approach", track.surfaceGrip, track.surfaceRollingResistance);
            Vector3[] vertices =
            {
                new Vector3(-extent, -0.25f, start), new Vector3(extent, -0.25f, start),
                new Vector3(-extent, -0.25f, track.length), new Vector3(extent, -0.25f, track.length),
                new Vector3(-extent, 0f, start), new Vector3(extent, 0f, start),
                new Vector3(-extent, height, track.length), new Vector3(extent, height, track.length)
            };
            int[] triangles =
            {
                0, 1, 2, 1, 3, 2,
                4, 6, 5, 5, 6, 7,
                0, 6, 4, 0, 2, 6,
                1, 7, 3, 1, 5, 7,
                0, 5, 1, 0, 4, 5,
                2, 7, 6, 2, 3, 7
            };
            var mesh = new Mesh { name = "Physics Lab Ramp Fixture", vertices = vertices, triangles = triangles };
            meshes.Add(mesh);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var ramp = new GameObject("Ramp and landing fixture");
            SceneManager.MoveGameObjectToScene(ramp, scene);
            ramp.transform.SetParent(root.transform, false);
            ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
            var surface = ramp.AddComponent<VehicleSurface>();
            surface.Configure(track.surfaceId + " ramp", track.surfaceGrip, track.surfaceRollingResistance);
        }

        private void BuildContactWalls()
        {
            float wallHeight = 5f;
            float wallThickness = 0.5f;
            CreateStaticBox("Contact wall left", new Vector3(-halfExtent, wallHeight * 0.5f, track.length * 0.5f),
                new Vector3(wallThickness, wallHeight, track.length + 20f));
            CreateStaticBox("Contact wall right", new Vector3(halfExtent, wallHeight * 0.5f, track.length * 0.5f),
                new Vector3(wallThickness, wallHeight, track.length + 20f));
            CreateStaticBox("Contact wall end", new Vector3(0f, wallHeight * 0.5f, track.length + 10f),
                new Vector3(halfExtent * 2f, wallHeight, wallThickness));
        }

        private GameObject CreateSurfaceBox(string name, Vector3 position, Vector3 size, string surfaceName, float grip, float rollingResistance)
        {
            GameObject value = CreateStaticBox(name, position, size);
            VehicleSurface surface = value.AddComponent<VehicleSurface>();
            surface.Configure(string.IsNullOrWhiteSpace(surfaceName) ? "Lab surface" : surfaceName,
                Mathf.Max(0f, grip), Mathf.Max(0f, rollingResistance));
            return value;
        }

        private GameObject CreateStaticBox(string name, Vector3 position, Vector3 size)
        {
            GameObject value = new GameObject(name);
            SceneManager.MoveGameObjectToScene(value, scene);
            value.transform.SetParent(root.transform, false);
            value.transform.position = position;
            BoxCollider collider = value.AddComponent<BoxCollider>();
            collider.size = size;
            return value;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                root = null;
            }
            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] != null) UnityEngine.Object.DestroyImmediate(meshes[i]);
            }
            meshes.Clear();
        }
    }

    [DisallowMultipleComponent]
    internal sealed class VehiclePhysicsLabCollisionRecorder : MonoBehaviour
    {
        private readonly List<VehiclePhysicsLabCollisionEvent> events = new List<VehiclePhysicsLabCollisionEvent>();
        public float CurrentTime { get; set; }
        public int MaximumEvents { get; set; } = 256;
        public IReadOnlyList<VehiclePhysicsLabCollisionEvent> Events => events;

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || events.Count >= Mathf.Max(1, MaximumEvents) || collision.contactCount == 0)
            {
                return;
            }

            ContactPoint point = collision.GetContact(0);
            events.Add(new VehiclePhysicsLabCollisionEvent
            {
                time = CurrentTime,
                otherName = collision.collider == null ? "<missing>" : collision.collider.name,
                point = point.point,
                normal = point.normal,
                impulse = collision.impulse.magnitude,
                contactCount = collision.contactCount
            });
        }
    }

    internal static class VehiclePhysicsLabLiveInput
    {
        public static VehicleInputState ReadKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return VehicleInputState.Neutral;
            }

            float steering = (keyboard.aKey.isPressed ? -1f : 0f) + (keyboard.dKey.isPressed ? 1f : 0f);
            float throttle = keyboard.wKey.isPressed ? 1f : 0f;
            float brake = keyboard.sKey.isPressed ? 1f : 0f;
            return new VehicleInputState
            {
                Steering = Mathf.Clamp(steering, -1f, 1f),
                Throttle = throttle,
                Brake = brake,
                Handbrake = keyboard.spaceKey.isPressed,
                Nitrous = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
            };
        }
    }

    public sealed class VehiclePhysicsLabRunner : IDisposable
    {
        private readonly VehiclePhysicsLabDefinition definition;
        private readonly Func<VehicleInputState> liveInput;
        private readonly string definitionFingerprint;
        private readonly string vehicleFingerprint;
        private readonly List<VehiclePhysicsLabSample> samples = new List<VehiclePhysicsLabSample>();
        private readonly List<string> diagnostics = new List<string>();
        private Scene scene;
        private VehiclePhysicsLabFixture fixture;
        private RacingVehicleSetup fixtureSetup;
        private VehicleTuning effectiveTuning;
        private RacingVehicleRig rig;
        private VehiclePhysicsLabCollisionRecorder collisions;
        private VehiclePhysicsLabRunReport report;
        private Vector3 previousVelocity;
        private Vector3 latestAcceleration;
        private float elapsed;
        private float sampleAccumulator;
        private int lastRecordedActionKey = -1;
        private int ticks;
        private bool brakeTriggered;
        private bool disposed;
        private bool warmupReported;

        public VehiclePhysicsLabRunner(VehiclePhysicsLabDefinition experiment, Func<VehicleInputState> liveInputProvider = null)
        {
            definition = experiment ?? throw new ArgumentNullException(nameof(experiment));
            liveInput = liveInputProvider;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("PHYSICS_LAB_PLAY_MODE: run isolated experiments outside Play mode.");
            }

            if (!definition.IsValid(out string failure))
            {
                throw new ArgumentException(failure);
            }

            vehicleFingerprint = RacingLineSnapshot.VehicleFingerprintOf(definition.vehicle);
            definitionFingerprint = ComputeFingerprint(definition, vehicleFingerprint);
            report = new VehiclePhysicsLabRunReport
            {
                definitionId = definition.id,
                definitionFingerprint = definitionFingerprint,
                vehicleFingerprint = vehicleFingerprint,
                engineVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                experiment = definition.experiment.ToString(),
                inputMode = definition.input.mode.ToString(),
                samplePhase = VehiclePhysicsLabSamplePhase.AfterPhysicsSceneSimulation.ToString(),
                fixedStep = definition.fixedStep,
                warmupSeconds = definition.warmupSeconds,
                seed = definition.seed,
                appliedOverrides = CloneOverrides(definition.temporaryOverrides),
                status = VehiclePhysicsLabResultStatus.Running,
                configurationEvidence = BuildConfigurationEvidence()
            };

            try
            {
                effectiveTuning = definition.vehicle.CreateEffectiveTuning();
                if (!VehiclePhysicsLabTuningOverrides.Apply(effectiveTuning, definition.temporaryOverrides, out string[] overrideMessages))
                {
                    throw new ArgumentException(string.Join("; ", overrideMessages));
                }

                for (int i = 0; i < overrideMessages.Length; i++)
                {
                    diagnostics.Add(overrideMessages[i]);
                }

                fixtureSetup = UnityEngine.Object.Instantiate(definition.vehicle);
                fixtureSetup.name = definition.vehicle.name + " (Physics Lab Fixture)";
                fixtureSetup.hideFlags = HideFlags.HideAndDontSave;
                fixtureSetup.tuning = effectiveTuning;
                // This setup is already fully resolved, including temporary overrides.
                // A factory definition here would replace that result on the rig's next
                // resolution; body parts and adjustments would otherwise apply twice.
                fixtureSetup.definition = null;
                fixtureSetup.upgrades = Array.Empty<VehiclePerformanceUpgradeDefinition>();
                fixtureSetup.bodyParts = Array.Empty<VehicleCustomizationDefinition>();
                fixtureSetup.tuningAdjustments = Array.Empty<VehicleTuningAdjustment>();

                scene = EditorSceneManager.NewPreviewScene();
                fixture = new VehiclePhysicsLabFixture(scene, definition.track, definition.safety.fixtureHalfExtent);
                VehiclePhysicsLabTrackSample start = definition.track.Sample(0f);
                Vector3 spawn = start.position + start.up * RacingVehicleRig.SpawnHeight(fixtureSetup);
                rig = new RacingVehicleRig(scene, fixtureSetup, spawn, Quaternion.LookRotation(start.forward, start.up), true);
                collisions = rig.Root.AddComponent<VehiclePhysicsLabCollisionRecorder>();
                collisions.MaximumEvents = definition.capture.maximumCollisionEvents;
                SeedStartingSpeed(start.forward);
                Physics.SyncTransforms();
                previousVelocity = rig.Body.linearVelocity;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public VehiclePhysicsLabRunReport Report => report;
        public Scene PreviewScene => scene;
        public GameObject PreviewVehicle => rig == null ? null : rig.Root;
        public IReadOnlyList<VehiclePhysicsLabSample> LiveSamples => samples;
        public bool IsDone => report == null || report.status != VehiclePhysicsLabResultStatus.Running;
        public float Progress
        {
            get
            {
                return definition == null ? 1f : Mathf.Clamp01(elapsed / Mathf.Max(0.01f, definition.safety.maximumSeconds));
            }
        }

        public void Advance(double milliseconds = 5d)
        {
            if (IsDone || disposed) return;
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(milliseconds), "The editor work slice must be finite and positive.");
            }
            string currentFingerprint;
            try
            {
                currentFingerprint = ComputeFingerprint(definition, RacingLineSnapshot.VehicleFingerprintOf(definition.vehicle));
            }
            catch (Exception exception)
            {
                Complete(VehiclePhysicsLabResultStatus.Invalid, false, "STALE_INPUT", exception.Message);
                return;
            }
            if (currentFingerprint != definitionFingerprint)
            {
                Complete(VehiclePhysicsLabResultStatus.Failed, false, "STALE_INPUT",
                    "The vehicle, experiment, track or temporary overrides changed while this run was active.");
                return;
            }

            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                do
                {
                    Step();
                }
                while (!IsDone && watch.Elapsed.TotalMilliseconds < Mathf.Max(0.1f, (float)milliseconds));
            }
            catch (Exception exception)
            {
                Complete(VehiclePhysicsLabResultStatus.Invalid, false, "RUNNER_EXCEPTION", exception.Message);
            }
        }

        public void Cancel()
        {
            if (IsDone) return;
            Complete(VehiclePhysicsLabResultStatus.Cancelled, false, "CANCELLED", "The experiment was cancelled before normal termination.");
        }

        public void MarkIncomplete(string failureCode, string failureMessage)
        {
            if (IsDone) return;
            Complete(VehiclePhysicsLabResultStatus.Incomplete, false, failureCode, failureMessage);
        }

        private void Step()
        {
            if (ticks >= definition.capture.maximumSamples * 4)
            {
                Complete(VehiclePhysicsLabResultStatus.Incomplete, false, "TICK_BUDGET", "The tick budget exceeded the bounded capture budget.");
                return;
            }

            bool warming = elapsed < definition.warmupSeconds;
            if (warming && !warmupReported)
            {
                diagnostics.Add("Warmup uses a fresh isolated rig with service brake and handbrake held; warmup samples are excluded from analysis.");
                warmupReported = true;
            }

            VehicleInputState input = warming ? new VehicleInputState { Brake = 1f, Handbrake = true } : BuildInput(elapsed - definition.warmupSeconds);
            rig.Input.Current = input.Clamped();
            if (collisions != null) collisions.CurrentTime = elapsed;

            rig.Vehicle.StepSimulation(definition.fixedStep);
            scene.GetPhysicsScene().Simulate(definition.fixedStep);
            elapsed += definition.fixedStep;
            ticks++;

            Vector3 velocity = rig.Body.linearVelocity;
            latestAcceleration = (velocity - previousVelocity) / Mathf.Max(0.0001f, definition.fixedStep);
            previousVelocity = velocity;

            ValidateState();
            if (IsDone) return;

            if (!warming)
            {
                sampleAccumulator += definition.fixedStep;
                if (sampleAccumulator + 0.000001f >= definition.capture.sampleIntervalSeconds)
                {
                    sampleAccumulator -= definition.capture.sampleIntervalSeconds;
                    CaptureSample(elapsed, elapsed - definition.warmupSeconds);
                }
            }

            if (ShouldFinish())
            {
                Complete(VehiclePhysicsLabResultStatus.Passed, true, string.Empty, string.Empty);
            }
        }

        private void ValidateState()
        {
            Rigidbody body = rig.Body;
            if (!Finite(body.position) || !Finite(body.rotation) || !Finite(body.linearVelocity) || !Finite(body.angularVelocity))
            {
                Complete(VehiclePhysicsLabResultStatus.Invalid, false, "NON_FINITE_STATE", "The shared vehicle produced a non-finite pose or velocity.");
                return;
            }

            if (body.linearVelocity.magnitude > definition.safety.maximumLinearSpeedMps
                || body.angularVelocity.magnitude > definition.safety.maximumAngularSpeedRadPerSec)
            {
                Complete(VehiclePhysicsLabResultStatus.Failed, false, "SAFETY_LIMIT", "The vehicle exceeded the experiment safety velocity bound.");
                return;
            }

            Vector3 position = body.position;
            if (Mathf.Abs(position.x) > definition.safety.fixtureHalfExtent
                || Mathf.Abs(position.z) > definition.safety.fixtureHalfExtent
                || position.y < -definition.safety.fixtureHalfExtent)
            {
                Complete(VehiclePhysicsLabResultStatus.Failed, false, "FIXTURE_EXIT", "The vehicle left the disposable fixture bounds.");
                return;
            }

            float roll = Vector3.Angle(body.transform.up, Vector3.up);
            if (roll > definition.evaluation.maximumRolloverDegrees && body.linearVelocity.sqrMagnitude > 4f)
            {
                Complete(VehiclePhysicsLabResultStatus.Failed, false, "ROLLOVER", "The vehicle exceeded the configured rollover angle.");
                return;
            }

            if (samples.Count >= definition.capture.maximumSamples)
            {
                Complete(VehiclePhysicsLabResultStatus.Incomplete, false, "CAPTURE_LIMIT", "The bounded sample buffer was exhausted before the experiment ended.");
            }
        }

        private void CaptureSample(float time, float measurementTime)
        {
            VehicleController vehicle = rig.Vehicle;
            Rigidbody body = rig.Body;
            VehicleTelemetry telemetry = vehicle.Telemetry;
            Vector3 velocity = body.linearVelocity;
            Vector3 acceleration = latestAcceleration;
            Vector3 localVelocity = body.transform.InverseTransformDirection(velocity);
            float speed = velocity.magnitude;
            bool slipValid = speed >= 2f;
            float slipAngle = Mathf.Atan2(localVelocity.x, Mathf.Abs(localVelocity.z) + 0.0001f) * Mathf.Rad2Deg;
            int grounded = telemetry.GroundedWheels;
            var wheelSamples = definition.capture.captureWheelChannels
                ? new VehiclePhysicsLabWheelSample[vehicle.Wheels == null ? 0 : vehicle.Wheels.Length]
                : Array.Empty<VehiclePhysicsLabWheelSample>();

            for (int i = 0; i < wheelSamples.Length; i++)
            {
                VehicleWheel wheel = vehicle.Wheels[i];
                wheelSamples[i] = new VehiclePhysicsLabWheelSample
                {
                    wheelIndex = i,
                    grounded = wheel.Grounded,
                    front = wheel.IsFrontWheel,
                    driven = wheel.IsDrivenWheel,
                    handbrake = wheel.IsHandbrakeWheel,
                    longitudinalSlip = wheel.LongitudinalSlip,
                    lateralSlipRadians = wheel.LateralSlipRadians,
                    normalLoad = wheel.NormalLoad,
                    suspensionCompression = wheel.SuspensionCompression,
                    contactForwardSpeedMps = wheel.ContactForwardSpeed,
                    longitudinalForce = wheel.LongitudinalForce,
                    lateralForce = wheel.LateralForce,
                    contactPoint = wheel.ContactPoint,
                    contactNormal = wheel.ContactNormal,
                    surfaceName = wheel.SurfaceName,
                    surfaceGrip = wheel.SurfaceGrip
                };
            }

            samples.Add(new VehiclePhysicsLabSample
            {
                tick = ticks,
                time = time,
                measurementTime = Mathf.Max(0f, measurementTime),
                position = body.position,
                rotation = body.rotation,
                velocity = velocity,
                angularVelocity = body.angularVelocity,
                acceleration = acceleration,
                speedKph = telemetry.SpeedKph,
                forwardSpeedKph = telemetry.ForwardSpeedKph,
                lateralSpeedMps = localVelocity.x,
                yawRateRadPerSec = Vector3.Dot(body.angularVelocity, body.transform.up),
                // Remove roll-induced gravity projection; lateral acceleration is
                // measured on the vehicle's horizontal right axis.
                lateralAccelerationMps2 = Vector3.Dot(acceleration,
                    Vector3.ProjectOnPlane(body.transform.right, Vector3.up).normalized),
                slipAngleDegrees = slipAngle,
                slipAngleValid = slipValid,
                airborne = grounded < 2 || telemetry.HandlingMode == VehicleHandlingMode.Airborne,
                groundedWheels = grounded,
                engineRpm = telemetry.EngineRpm,
                engineTorque = telemetry.EngineTorque,
                drivetrainTorque = telemetry.DrivetrainTorque,
                gear = telemetry.Gear,
                shifting = telemetry.IsShifting,
                nitrousActive = telemetry.NitrousActive,
                nitrousSeconds = telemetry.NitrousSeconds,
                rawInput = definition.capture.captureRawInput ? telemetry.RawInput : VehicleInputState.Neutral,
                finalInput = definition.capture.captureFilteredInput ? telemetry.FinalInput : VehicleInputState.Neutral,
                handling = telemetry.HandlingMode,
                assistYawTorque = telemetry.AssistYawTorque,
                tractionControlReduction = telemetry.TractionControlReduction,
                absReduction = telemetry.AbsReduction,
                countersteeringContribution = telemetry.CountersteeringContribution,
                averageLongitudinalSlip = telemetry.AverageSlip,
                wheels = wheelSamples
            });
        }

        private bool ShouldFinish()
        {
            float speedKph = telemetrySpeedKph();
            if (definition.experiment == VehiclePhysicsLabExperimentKind.Braking)
            {
                if (brakeTriggered && speedKph <= definition.evaluation.targetStopSpeedKph)
                {
                    return true;
                }
            }
            else if (definition.evaluation.requireTargetSpeed && definition.evaluation.targetSpeedKph > 0f
                && speedKph >= definition.evaluation.targetSpeedKph)
            {
                report.targetReached = true;
                return true;
            }

            if (elapsed >= definition.safety.maximumSeconds)
            {
                if (definition.evaluation.requireTargetSpeed && !report.targetReached)
                {
                    Complete(VehiclePhysicsLabResultStatus.Failed, true, "TARGET_NOT_REACHED",
                        "The target was not reached within the configured time bound; no extrapolated measurement was created.");
                    return false;
                }

                if (definition.experiment == VehiclePhysicsLabExperimentKind.Braking && !brakeTriggered)
                {
                    Complete(VehiclePhysicsLabResultStatus.Failed, true, "BRAKE_TRIGGER_NOT_REACHED",
                        "The vehicle did not reach the configured braking start speed.");
                    return false;
                }

                return true;
            }

            return false;
        }

        private float telemetrySpeedKph()
        {
            return rig == null ? 0f : rig.Vehicle.Telemetry.SpeedKph;
        }

        private VehicleInputState BuildInput(float time)
        {
            if (definition.input.mode == VehiclePhysicsLabInputMode.Live)
            {
                return liveInput == null ? VehiclePhysicsLabLiveInput.ReadKeyboard() : liveInput().Clamped();
            }

            if (definition.input.mode == VehiclePhysicsLabInputMode.Recorded)
            {
                return RecordedInput(time);
            }

            VehicleInputState result = VehicleInputState.Neutral;
            float stepTime = Mathf.Max(0f, definition.input.stepTime);
            float amplitude = Mathf.Clamp01(definition.input.sineAmplitude);
            float frequency = Mathf.Max(0.01f, definition.input.sineFrequencyHz);
            float transition = Mathf.Max(0.05f, definition.input.transitionSeconds);
            float speed = telemetrySpeedKph();
            float defaultSteer = Mathf.Abs(definition.input.steering) < 0.001f ? 0.55f : definition.input.steering;
            result.Throttle = Mathf.Clamp01(definition.input.throttle);
            result.Steering = definition.input.steering;
            result.Brake = Mathf.Clamp01(definition.input.brake);
            result.Handbrake = definition.input.handbrake;
            result.Nitrous = definition.input.nitrous;

            switch (definition.experiment)
            {
                case VehiclePhysicsLabExperimentKind.CoastDown:
                    result = VehicleInputState.Neutral;
                    break;
                case VehiclePhysicsLabExperimentKind.Braking:
                    // Use the seeded rigidbody speed as the trigger source. Telemetry is
                    // published after the first simulation step and is zero during the
                    // initial calibration tick, which otherwise delays or omits braking.
                    float triggerSpeed = rig == null || rig.Body == null
                        ? speed
                        : VehicleMath.MetersPerSecondToKph(rig.Body.linearVelocity.magnitude);
                    if (!brakeTriggered && triggerSpeed >= Mathf.Max(1f, definition.brakingStartSpeedKph))
                    {
                        brakeTriggered = true;
                    }
                    result = brakeTriggered
                        ? new VehicleInputState { Brake = 1f }
                        : new VehicleInputState { Throttle = Mathf.Clamp01(definition.input.throttle) };
                    break;
                case VehiclePhysicsLabExperimentKind.RepeatedBraking:
                    result = Mathf.FloorToInt(time / transition) % 2 == 0
                        ? new VehicleInputState { Throttle = Mathf.Clamp01(definition.input.throttle) }
                        : new VehicleInputState { Brake = 1f };
                    break;
                case VehiclePhysicsLabExperimentKind.StepSteer:
                case VehiclePhysicsLabExperimentKind.LaneChange:
                    result.Steering = time >= stepTime ? defaultSteer : 0f;
                    break;
                case VehiclePhysicsLabExperimentKind.SineSteer:
                case VehiclePhysicsLabExperimentKind.Slalom:
                    result.Steering = Mathf.Sin(time * frequency * Mathf.PI * 2f) * amplitude;
                    break;
                case VehiclePhysicsLabExperimentKind.Skidpad:
                    result.Steering = defaultSteer;
                    break;
                case VehiclePhysicsLabExperimentKind.CombinedBrakingTurn:
                    result.Steering = defaultSteer;
                    result.Brake = time >= stepTime ? 1f : 0f;
                    result.Throttle = time >= stepTime ? 0f : Mathf.Clamp01(definition.input.throttle);
                    break;
                case VehiclePhysicsLabExperimentKind.BrakeToDrift:
                    result.Steering = defaultSteer;
                    if (time >= stepTime && time < stepTime + transition)
                    {
                        result.Brake = 1f;
                        result.Throttle = 0f;
                    }
                    else if (time >= stepTime + transition)
                    {
                        result.Handbrake = true;
                        result.Throttle = Mathf.Clamp01(definition.input.throttle);
                    }
                    break;
                case VehiclePhysicsLabExperimentKind.SustainedDrift:
                    result.Steering = defaultSteer;
                    result.Handbrake = time >= stepTime;
                    break;
                case VehiclePhysicsLabExperimentKind.DriftTransition:
                    result.Steering = (Mathf.FloorToInt(time / transition) % 2 == 0 ? 1f : -1f) * amplitude;
                    result.Handbrake = time >= stepTime && time < stepTime + transition;
                    break;
                case VehiclePhysicsLabExperimentKind.Nitrous:
                    result.Nitrous = time >= stepTime;
                    break;
                case VehiclePhysicsLabExperimentKind.Contact:
                    result.Steering = 0f;
                    break;
                case VehiclePhysicsLabExperimentKind.Custom:
                    result = new VehicleInputState
                    {
                        Steering = definition.input.steering,
                        Throttle = definition.input.throttle,
                        Brake = definition.input.brake,
                        Handbrake = definition.input.handbrake,
                        Nitrous = definition.input.nitrous
                    };
                    break;
            }

            return result.Clamped();
        }

        private VehicleInputState RecordedInput(float time)
        {
            VehiclePhysicsLabControlKey[] keys = definition.input.keys;
            if (keys == null || keys.Length == 0)
            {
                diagnostics.Add("RECORDED_INPUT_EMPTY: no keys were supplied; neutral input was consumed.");
                return VehicleInputState.Neutral;
            }

            int activeKey = 0;
            if (time <= keys[0].time)
            {
                activeKey = 0;
                return WithRecordedAction(keys[activeKey].input, activeKey);
            }
            for (int i = 1; i < keys.Length; i++)
            {
                if (time > keys[i].time) continue;
                activeKey = i;
                float blend = Mathf.InverseLerp(keys[i - 1].time, keys[i].time, time);
                VehicleInputState a = keys[i - 1].input;
                VehicleInputState b = keys[i].input;
                VehicleInputState result = new VehicleInputState
                {
                    Steering = Mathf.Lerp(a.Steering, b.Steering, blend),
                    Throttle = Mathf.Lerp(a.Throttle, b.Throttle, blend),
                    Brake = Mathf.Lerp(a.Brake, b.Brake, blend),
                    Handbrake = blend < 0.5f ? a.Handbrake : b.Handbrake,
                    Nitrous = blend < 0.5f ? a.Nitrous : b.Nitrous,
                    // Discrete actions belong to the active authored key; they
                    // must not be lost while analog channels are interpolated.
                    GearUp = b.GearUp,
                    GearDown = b.GearDown,
                    GearNeutral = b.GearNeutral,
                    GearReverse = b.GearReverse
                };
                return WithRecordedAction(result, activeKey);
            }

            return WithRecordedAction(keys[keys.Length - 1].input, keys.Length - 1);
        }

        private VehicleInputState WithRecordedAction(VehicleInputState input, int key)
        {
            VehicleInputState result = input.Clamped();
            bool edge = key != lastRecordedActionKey;
            lastRecordedActionKey = key;
            if (!edge)
            {
                result.GearUp = result.GearDown = false;
                result.GearNeutral = result.GearReverse = false;
            }
            return result;
        }

        private void SeedStartingSpeed(Vector3 forward)
        {
            float speed = VehicleMath.KphToMetersPerSecond(Mathf.Max(0f, definition.startingSpeedKph));
            if (speed <= 0.001f) return;
            rig.Body.linearVelocity = forward.normalized * speed;
            foreach (VehicleWheel wheel in rig.Vehicle.Wheels)
            {
                wheel.SetInitialForwardSpeed(speed);
            }
        }

        private string BuildConfigurationEvidence()
        {
            var values = new List<string>
            {
                "vehicle=" + vehicleFingerprint,
                "simulation=" + VehicleController.SimulationRevision,
                "fixedStep=" + definition.fixedStep.ToString("R", CultureInfo.InvariantCulture),
                "track=" + definition.track.id,
                "trackKind=" + definition.track.kind,
                "gravity=" + Physics.gravity.ToString("R"),
                "solverIterations=" + Physics.defaultSolverIterations,
                "solverVelocityIterations=" + Physics.defaultSolverVelocityIterations,
                "overrides=" + JsonUtility.ToJson(definition.temporaryOverrides)
            };
            return string.Join("; ", values);
        }

        private void Complete(VehiclePhysicsLabResultStatus status, bool complete, string failureCode, string failureMessage)
        {
            if (report == null || report.status != VehiclePhysicsLabResultStatus.Running) return;
            report.status = status;
            report.complete = complete;
            report.elapsedSeconds = elapsed;
            report.measuredSeconds = Mathf.Max(0f, elapsed - definition.warmupSeconds);
            report.totalTicks = ticks;
            report.failureCode = failureCode ?? string.Empty;
            report.failureMessage = failureMessage ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(failureCode)) diagnostics.Add(failureCode + ": " + failureMessage);
            report.diagnostics = diagnostics.ToArray();
            report.samples = samples.ToArray();
            report.collisions = collisions == null
                ? Array.Empty<VehiclePhysicsLabCollisionEvent>()
                : collisions.Events.ToArray();
            report.metrics = VehiclePhysicsLabAnalysis.Analyze(report, definition);
            report.droppedSamples = 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (rig != null) { rig.Dispose(); rig = null; }
            fixture?.Dispose(); fixture = null;
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            scene = default;
            DestroyTransient(fixtureSetup); fixtureSetup = null;
            DestroyTransient(effectiveTuning); effectiveTuning = null;
        }

        private static void DestroyTransient(UnityEngine.Object value)
        {
            if (value == null) return;
            UnityEngine.Object.DestroyImmediate(value);
        }

        private static string ComputeFingerprint(VehiclePhysicsLabDefinition experiment, string vehicle)
        {
            using (var digest = new RacingDigest())
            {
                digest.Add("vehicle-physics-lab.1");
                digest.Add(experiment.id);
                digest.Add(JsonUtility.ToJson(experiment));
                digest.Add(experiment.track == null ? "<null-track>" : experiment.track.id);
                digest.Add(experiment.vehicle == null ? "<null-vehicle>" : experiment.vehicle.id);
                digest.Add(vehicle);
                digest.Add(VehicleController.SimulationRevision);
                digest.Add(Application.unityVersion);
                return digest.Finish();
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);

        private static VehiclePhysicsLabTuningOverride[] CloneOverrides(VehiclePhysicsLabTuningOverride[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<VehiclePhysicsLabTuningOverride>();
            var copy = new VehiclePhysicsLabTuningOverride[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
