using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Thin orchestration layer. Every major concern is a replaceable module:
    /// input, recovered powertrain, wheel contacts, nitrous, and camera can evolve
    /// independently without turning the vehicle into one monolithic script.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(VehiclePowertrain))]
    [RequireComponent(typeof(VehicleModuleHost))]
    public sealed class VehicleController : MonoBehaviour
    {
        public const string SimulationRevision = "nfsmw.3bb4ab24.source-port.2";
        private bool gearUpHeld, gearDownHeld;
        private bool manualSimulation;
        public bool ManualSimulation => manualSimulation;
        public bool UsesBuiltInAero => useBuiltInAero;
        // Authoring/preview adapters must reject serialized physics references outside this chassis before cloning.
        public bool HasLocalPhysicsBindings => (powertrain == null || powertrain.gameObject == gameObject)
            && (nitrous == null || nitrous.gameObject == gameObject)
            && (moduleHost == null || moduleHost.gameObject == gameObject);

        public void SetManualSimulation(bool value)
        {
            if (value && gameObject.scene.GetPhysicsScene() == Physics.defaultPhysicsScene)
                throw new System.InvalidOperationException("Manual vehicle stepping requires an isolated local physics scene.");
            manualSimulation = value;
        }

        public void StepSimulation(float deltaTime)
        {
            if (!manualSimulation) throw new System.InvalidOperationException("Acquire manual simulation before stepping the vehicle.");
            if (gameObject.scene.GetPhysicsScene() == Physics.defaultPhysicsScene)
                throw new System.InvalidOperationException("A manually stepped vehicle was moved out of its isolated physics scene.");
            if (!float.IsFinite(deltaTime) || deltaTime <= 0 || deltaTime > 0.05f)
                throw new System.ArgumentOutOfRangeException(nameof(deltaTime));
            SimulatePhysics(deltaTime);
        }
        [SerializeField] private VehicleTuning stockTuning;
        [SerializeField] private MonoBehaviour inputSourceComponent;
        [SerializeField] private VehiclePowertrain powertrain;
        [SerializeField] private VehicleNitrous nitrous;
        [SerializeField] private VehicleWheel[] wheels;
        [SerializeField] private VehicleCameraRig cameraRig;
        [SerializeField] private VehicleModuleHost moduleHost;
        [SerializeField] private bool useBuiltInAero = true;

        private Rigidbody body;
        private VehicleTuning tuning;
        private IVehicleInputSource inputSource;
        private Vector3 safePosition;
        private Quaternion safeRotation;
        private float smoothedSteering;
        private bool configured;
        private readonly MostWantedChassis chassis = new MostWantedChassis();
        private readonly MostWantedTire[] tires = new MostWantedTire[4];
        private readonly bool[] grounded = new bool[4];
        private int previousGroundedWheels;
        private bool chassisContact;
        private float referenceFrontAxle = 1.3f, referenceRearAxle = -1.3f, referenceFrontTrack = 1.5f;

        public VehicleTelemetry Telemetry { get; private set; }
        public VehicleTuning FactoryTuning => stockTuning;
        public event System.Action<float> PhysicsSampled;
        public event System.Action PoseReset;
        public void NotifyPoseReset() { chassis.Reset(); gearUpHeld = gearDownHeld = false; PoseReset?.Invoke(); }
        public void ResetSimulationForPool()
        {
            if (gameObject.activeSelf) throw new System.InvalidOperationException("Pool reset requires an inactive vehicle.");
            if (!configured) ConfigureModules();
            smoothedSteering = 0;
            Telemetry = default;
            for (int i = 0; i < wheels.Length; i++) { wheels[i].Configure(body, tuning); wheels[i].ResetSimulation(); }
            powertrain.Configure(tuning, wheels); powertrain.ResetPowertrain(); powertrain.SetIgnition(true);
            if (nitrous != null) nitrous.ResetForSpawn();
            NotifyPoseReset();
        }
        public bool EngineRunning => powertrain == null || powertrain.EngineRunning;
        public VehicleCameraRig CameraRig => cameraRig;
        public bool TrySetEngineRunning(bool running)
        {
            if (body == null || powertrain == null || !running && body.linearVelocity.sqrMagnitude > 1f) return false;
            powertrain.SetIgnition(running); return true;
        }

        public Rigidbody Body
        {
            get { return body; }
        }

        public VehicleTuning Tuning
        {
            get { return tuning != null ? tuning : stockTuning; }
            set
            {
                stockTuning = value;
                tuning = null;
                configured = false;
            }
        }

        public VehicleWheel[] Wheels
        {
            get { return wheels; }
            set
            {
                wheels = value;
                configured = false;
            }
        }

        public VehicleModuleHost ModuleHost
        {
            get { return moduleHost; }
        }

        public MonoBehaviour InputSourceComponent
        {
            get { return inputSourceComponent; }
            set
            {
                inputSourceComponent = value;
                inputSource = value as IVehicleInputSource;
                gearUpHeld = gearDownHeld = false;
            }
        }

        public void SetInputSource(MonoBehaviour configuredInput)
        {
            InputSourceComponent = configuredInput;
        }

        public void ConfigureForRuntime(
            VehicleTuning configuredTuning,
            MonoBehaviour configuredInput,
            VehicleWheel[] configuredWheels,
            VehicleCameraRig configuredCameraRig = null)
        {
            stockTuning = configuredTuning;
            tuning = null;
            InputSourceComponent = configuredInput;
            wheels = configuredWheels;
            cameraRig = configuredCameraRig;
            configured = false;
            ConfigureModules();
        }

        private void Awake()
        {
            if (!configured)
            {
                ConfigureModules();
            }

            safePosition = transform.position;
            safeRotation = transform.rotation;
        }

        private void FixedUpdate()
        {
            if (!manualSimulation) SimulatePhysics(Time.fixedDeltaTime);
        }

        private void SimulatePhysics(float deltaTime)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost != null && moduleHost.ConsumeReconfigureRequest())
            {
                ConfigureModules();
            }

            VehicleInputState rawInput = inputSource != null
                ? inputSource.Current.Clamped()
                : VehicleInputState.Neutral;
            bool up = rawInput.GearUp, down = rawInput.GearDown;
            rawInput.GearUp &= !gearUpHeld;
            rawInput.GearDown &= !gearDownHeld;
            gearUpHeld = up;
            gearDownHeld = down;
            if (inputSource is IVehicleDiscreteInputSource discrete)
            {
                VehicleInputState actions = discrete.ConsumeDiscreteActions();
                rawInput.GearUp |= actions.GearUp; rawInput.GearDown |= actions.GearDown;
                rawInput.GearNeutral |= actions.GearNeutral; rawInput.GearReverse |= actions.GearReverse;
            }

            if (moduleHost != null && moduleHost.Context != null)
            {
                moduleHost.Context.Input = rawInput;
                moduleHost.RunBeforePhysics(deltaTime);
                rawInput = moduleHost.Context.Input.Clamped();
            }

            if (inputSource != null)
            {
                if (inputSource.ConsumeResetRequest())
                {
                    ResetVehicle();
                }

                if (inputSource.ConsumeCameraToggleRequest() && cameraRig != null)
                {
                    cameraRig.ToggleCameraMode();
                }
            }

            float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
            float speedKph = VehicleMath.MetersPerSecondToKph(body.linearVelocity.magnitude);
            powertrain.SetManualRequests(rawInput.GearUp, rawInput.GearDown,
                rawInput.GearNeutral, rawInput.GearReverse);
            bool playerControls = inputSource != null
                && (inputSource is not IVehicleInputControlPolicy policy || policy.ApplyPlayerControls);
            var input = playerControls ? powertrain.ApplyDriverPedals(rawInput, forwardSpeed) : rawInput;
            float driveThrottle = input.Throttle, serviceBrake = input.Brake;
            Vector3 localVelocity = transform.InverseTransformDirection(body.linearVelocity);
            Vector3 localAngular = transform.InverseTransformDirection(body.angularVelocity);
            float slipAngle = localVelocity.z < 1f ? 0f : Mathf.Atan2(localVelocity.x, localVelocity.z);
            int contacts = 0;
            float maxSlip = 0; int maxSlipWheel = 0;
            for (int i = 0; i < 4; i++)
            {
                tires[i] = wheels[i].Tire;
                grounded[i] = wheels[i].Grounded;
                if (grounded[i]) contacts++;
                if (tires[i].Slip > maxSlip) { maxSlip = tires[i].Slip; maxSlipWheel = i; }
            }
            bool nitrousActive = nitrous != null && nitrous.Tick(deltaTime,
                EngineRunning && rawInput.Nitrous && driveThrottle > .01f, speedKph, powertrain.Gear);
            var frame = new MostWantedChassis.Frame(input, deltaTime, forwardSpeed, slipAngle, localAngular.y, contacts,
                gear: powertrain.Gear, playerSteering: tuning.mostWanted.useSteeringTables,
                frontAxle: referenceFrontAxle, rearAxle: referenceRearAxle, frontTrack: referenceFrontTrack,
                localLateralSpeed: localVelocity.x, grade: transform.forward.y,
                nosBoost: 1f + (nitrous != null ? nitrous.BoostMultiplier * tuning.mostWanted.nitrousTorqueBoost : 0f),
                bodySpeed: body.linearVelocity.magnitude);
            chassis.BeginStep(frame, tires);
            Vector3 center = body.centerOfMass;
            float frontBias = contacts == 0 ? .5f : tuning.mostWanted.frontWeightBias * .01f;
            center.z = Mathf.Lerp(referenceRearAxle, referenceFrontAxle, frontBias);
            body.centerOfMass = center;
            if (useBuiltInAero) chassis.ApplyAerodynamics(body, tuning, wheels, driveThrottle, false, referenceFrontAxle, referenceRearAxle);
            VehiclePowertrainOutput powertrainOutput = powertrain.Simulate(deltaTime, forwardSpeed, driveThrottle, nitrous != null ? nitrous.BoostMultiplier : 0f);
            chassis.ApplyDriveForces(body, tuning, tires, grounded, powertrainOutput.WheelTorque, frame,
                wheels[0].IsDrivenWheel || wheels[1].IsDrivenWheel, wheels[2].IsDrivenWheel || wheels[3].IsDrivenWheel);
            var steering = chassis.SteerAndTune(frame, tuning, tires, wheels[2].LateralSlipRadians,
                wheels[3].LateralSlipRadians, maxSlip, maxSlipWheel);
            smoothedSteering = steering.SteeringDegrees / MostWantedChassis.AbsoluteMaximumDegrees;
            float shapedSteering = smoothedSteering;
            float frontSway = (wheels[0].CompressionMeters - wheels[1].CompressionMeters) * tuning.mostWanted.swaybarStiffness.x;
            float rearSway = (wheels[2].CompressionMeters - wheels[3].CompressionMeters) * tuning.mostWanted.swaybarStiffness.y;
            Vector3 totalForce = Vector3.zero, totalTorque = Vector3.zero;
            float correction = 0; int newContacts = 0;
            for (int i = 0; i < 4; i++)
            {
                float angle = i == 0 ? steering.FrontLeftDegrees : i == 1 ? steering.FrontRightDegrees : 0;
                float sway = (i < 2 ? frontSway : rearSway) * (i % 2 == 0 ? 1 : -1);
                wheels[i].Simulate(deltaTime, angle, sway);
                totalForce += wheels[i].WorldForce;
                totalTorque += Vector3.Cross(wheels[i].ForcePosition - body.worldCenterOfMass, wheels[i].WorldForce);
                correction = Mathf.Max(correction, wheels[i].BottomOutCorrection);
                if (wheels[i].Grounded) newContacts++;
            }
            float yaw = Vector3.Dot(transform.up, totalTorque);
            totalTorque += transform.up * (yaw * tuning.mostWanted.yawSpeed - yaw);
            body.AddForce(totalForce, ForceMode.Force);
            body.AddTorque(totalTorque, ForceMode.Force);
            if (correction > 0) body.position += Vector3.up * correction;
            if (newContacts != 0 && previousGroundedWheels == 0)
            {
                localAngular.y *= .5f; localAngular.z *= .5f;
                body.angularVelocity = transform.TransformDirection(localAngular);
            }
            previousGroundedWheels = newContacts;
            chassis.ApplyDriftYawTorque(body, transform.up);
            chassis.ApplyWallSteer(body, frame, newContacts, powertrain.Gear > 0);
            Vector3 groundNormal = Vector3.up;
            float altitude = 0;
            if (gameObject.scene.GetPhysicsScene().Raycast(body.position, Vector3.down, out RaycastHit groundHit,
                1000f, wheels[0].GroundMask, QueryTriggerInteraction.Ignore))
            {
                groundNormal = groundHit.normal;
                altitude = Mathf.Max(0, groundHit.distance + wheels[0].LocalArm.y);
            }
            var landingFrame = new MostWantedChassis.Frame(input, deltaTime, forwardSpeed, slipAngle,
                localAngular.y, newContacts, powertrain.Gear);
            chassis.ApplyJumpStabilizer(body, landingFrame, groundNormal, altitude, chassisContact);
            ApplySleep(input, newContacts, totalForce, totalTorque);
            chassisContact = false;
            if (moduleHost != null)
            {
                moduleHost.RunAfterPhysics(deltaTime);
            }

            UpdateSafePose(forwardSpeed, speedKph);
            UpdateTelemetry(powertrainOutput, speedKph, forwardSpeed, driveThrottle, serviceBrake,
                rawInput, shapedSteering, nitrousActive);
            if (moduleHost != null && moduleHost.Context != null)
            {
                moduleHost.Context.Telemetry = Telemetry;
            }
            PhysicsSampled?.Invoke(deltaTime);
        }

        private void OnCollisionStay(Collision collision)
        {
            chassisContact = true;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                chassis.OnWallCollision(contact.normal, contact.point, collision.relativeVelocity,
                    collision.rigidbody != null ? collision.rigidbody.linearVelocity : Vector3.zero, body);
            }
        }

        private void ApplySleep(VehicleInputState input, int contacts, Vector3 wheelForce, Vector3 wheelTorque)
        {
            float speed = body.linearVelocity.magnitude;
            if (speed >= 1f || body.angularVelocity.magnitude >= .25f || input.Throttle > 0) return;
            if (speed < .5f && contacts == 4 && (input.Brake > 0 || input.Handbrake) && !chassisContact)
            {
                // Chassis::DoSleep SS_ALL. Damp's argument is the removed fraction.
                body.linearVelocity *= speed;
                body.angularVelocity *= speed;
                foreach (var wheel in wheels) wheel.Tire.AngularVelocity = 0;
                return;
            }
            Vector3 velocity = transform.InverseTransformDirection(body.linearVelocity);
            Vector3 angular = transform.InverseTransformDirection(body.angularVelocity);
            Vector3 force = transform.InverseTransformDirection(wheelForce);
            Vector3 torque = transform.InverseTransformDirection(wheelTorque);
            velocity.x *= speed; angular.y *= speed;
            body.linearVelocity = transform.TransformDirection(velocity);
            body.angularVelocity = transform.TransformDirection(angular);
            body.AddForce(transform.right * (-force.x * (1 - speed)), ForceMode.Force);
            body.AddTorque(transform.up * (-torque.y * (1 - speed)), ForceMode.Force);
        }

        private void LateUpdate()
        {
            if (wheels == null)
            {
                return;
            }

            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i].ApplyVisualPose(Time.deltaTime);
            }

            if (moduleHost != null)
            {
                moduleHost.RunPresentation(Time.deltaTime);
            }
        }

        private void ConfigureModules()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            if (moduleHost == null)
            {
                moduleHost = GetComponent<VehicleModuleHost>();
            }

            if (stockTuning == null)
            {
                stockTuning = VehicleTuning.CreateStreetRacer();
            }

            tuning = stockTuning;

            if (inputSourceComponent == null)
            {
                VehicleInputAuthority authority = GetComponent<VehicleInputAuthority>();
                if (authority != null) inputSourceComponent = authority;
            }
            if (inputSourceComponent == null)
            {
                MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IVehicleInputSource)
                    {
                        inputSourceComponent = behaviours[i];
                        break;
                    }
                }
            }

            inputSource = inputSourceComponent as IVehicleInputSource;

            if (powertrain == null)
            {
                powertrain = GetComponent<VehiclePowertrain>();
            }

            if (nitrous == null)
            {
                nitrous = GetComponent<VehicleNitrous>();
            }

            if (wheels == null || wheels.Length == 0)
            {
                wheels = GetComponentsInChildren<VehicleWheel>();
            }

            if (wheels.Length != 4) throw new System.InvalidOperationException("MW physics requires four wheels.");
            System.Array.Sort(wheels, (a, b) =>
            {
                int axleOrder = a.Axle.CompareTo(b.Axle);
                return axleOrder != 0 ? axleOrder : transform.InverseTransformPoint(a.transform.position).x.CompareTo(transform.InverseTransformPoint(b.transform.position).x);
            });

            if (moduleHost != null)
            {
                moduleHost.Configure(this, body, tuning, wheels);
                tuning = moduleHost.Context.Tuning;
            }

            body.mass = tuning.chassis.mass;
            var box = GetComponent<BoxCollider>();
            float halfHeight = box != null ? box.size.y * .5f : .65f;
            var source = tuning.mostWanted;
            body.centerOfMass = new Vector3(0, source.rollCenter - halfHeight - Mathf.Max(source.rideHeight.x, source.rideHeight.y),
                source.frontAxle - source.wheelBase * (1 - source.frontWeightBias * .01f));
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.maxAngularVelocity = 30f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i].Configure(body, tuning);
            }
            {
                float front = 0f, rear = 0f, minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                int fronts = 0, rears = 0;
                foreach (var wheel in wheels)
                {
                    Vector3 position = transform.InverseTransformPoint(wheel.transform.position);
                    if (wheel.IsFrontWheel) { front += position.z; fronts++; minX = Mathf.Min(minX, position.x); maxX = Mathf.Max(maxX, position.x); }
                    else { rear += position.z; rears++; }
                }
                if (fronts > 0) referenceFrontAxle = front / fronts;
                if (rears > 0) referenceRearAxle = rear / rears;
                if (fronts >= 2) referenceFrontTrack = Mathf.Max(.1f, maxX - minX);
            }

            powertrain.Configure(tuning, wheels);
            if (nitrous != null)
            {
                nitrous.Configure(tuning);
            }

            // Only an explicitly assigned camera belongs to this vehicle. NPC activation must not steal the player camera.
            if (cameraRig != null)
            {
                cameraRig.SetTarget(transform, body);
            }

            configured = true;
        }

        public bool TryInstallPerformanceUpgrade(
            IVehiclePerformanceUpgrade upgrade,
            out string failure)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost == null)
            {
                failure = "No vehicle module host is attached.";
                return false;
            }

            return moduleHost.TryInstallPerformanceUpgrade(upgrade, out failure);
        }

        public bool CanInstallPerformanceUpgrade(
            IVehiclePerformanceUpgrade upgrade,
            out string failure)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost == null)
            {
                failure = "No vehicle module host is attached.";
                return false;
            }

            return moduleHost.CanInstallPerformanceUpgrade(upgrade, out failure);
        }

        public bool TryInstallPerformanceUpgradeById(
            string upgradeId,
            out string failure)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost == null)
            {
                failure = "No vehicle module host is attached.";
                return false;
            }

            return moduleHost.TryInstallPerformanceUpgradeById(upgradeId, out failure);
        }

        public bool TryRemovePerformanceUpgrade(
            VehiclePerformanceCategory category,
            out string failure)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost == null)
            {
                failure = "No vehicle module host is attached.";
                return false;
            }

            return moduleHost.TryRemovePerformanceUpgrade(category, out failure);
        }

        public bool TryInstallCustomization(
            IVehicleCustomizationItem customization,
            out string failure)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost == null)
            {
                failure = "No vehicle module host is attached.";
                return false;
            }

            return moduleHost.TryInstallCustomization(customization, out failure);
        }

        public bool CanInstallCustomization(
            IVehicleCustomizationItem customization,
            out string failure)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost == null)
            {
                failure = "No vehicle module host is attached.";
                return false;
            }

            return moduleHost.CanInstallCustomization(customization, out failure);
        }

        public bool TryInstallCustomizationById(
            string customizationId,
            out string failure)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost == null)
            {
                failure = "No vehicle module host is attached.";
                return false;
            }

            return moduleHost.TryInstallCustomizationById(customizationId, out failure);
        }

        public bool TryRemoveCustomization(
            VehicleCustomizationCategory category,
            out string failure)
        {
            if (!configured)
            {
                ConfigureModules();
            }

            if (moduleHost == null)
            {
                failure = "No vehicle module host is attached.";
                return false;
            }

            return moduleHost.TryRemoveCustomization(category, out failure);
        }

        private void UpdateSafePose(float forwardSpeed, float speedKph)
        {
            bool hasGroundedWheel = false;
            for (int i = 0; i < wheels.Length; i++)
            {
                hasGroundedWheel |= wheels[i].Grounded;
            }

            if (hasGroundedWheel
                && speedKph < 16f
                && Vector3.Dot(transform.up, Vector3.up) > 0.65f
                && Mathf.Abs(forwardSpeed) < VehicleMath.KphToMetersPerSecond(16f))
            {
                safePosition = transform.position;
                safeRotation = transform.rotation;
            }
        }

        private void UpdateTelemetry(
            VehiclePowertrainOutput powertrainOutput,
            float speedKph,
            float forwardSpeed,
            float driveThrottle,
            float serviceBrake,
            VehicleInputState rawInput,
            float shapedSteering,
            bool nitrousActive)
        {
            float slipTotal = 0f;
            int slipCount = 0;
            int groundedWheels = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                slipTotal += Mathf.Abs(wheels[i].LongitudinalSlip);
                slipCount++;
                if (wheels[i].Grounded) groundedWheels++;
            }

            VehicleInputState shapedInput = rawInput;
            shapedInput.Steering = shapedSteering;
            VehicleInputState finalInput = shapedInput;
            finalInput.Steering = smoothedSteering;
            finalInput.Throttle = driveThrottle;
            finalInput.Brake = serviceBrake;

            Telemetry = new VehicleTelemetry
            {
                SpeedKph = speedKph,
                ForwardSpeedKph = VehicleMath.MetersPerSecondToKph(forwardSpeed),
                EngineRpm = powertrainOutput.EngineRpm,
                EngineTorque = powertrainOutput.EngineTorque,
                DrivetrainTorque = powertrainOutput.WheelTorque,
                Steering = smoothedSteering,
                Throttle = driveThrottle,
                Brake = serviceBrake,
                AverageSlip = slipCount == 0 ? 0f : slipTotal / slipCount,
                RawInput = rawInput,
                ShapedInput = shapedInput,
                FinalInput = finalInput,
                HandlingMode = groundedWheels < 2 ? VehicleHandlingMode.Airborne : chassis.DriftAmount > 0 ? VehicleHandlingMode.Drifting : VehicleHandlingMode.Grip,
                AssistYawTorque = chassis.ApplyDriftYaw * body.inertiaTensor.y,
                AverageLateralSlipRadians = wheels.Length == 0 ? 0f : AverageLateralSlip(),
                AverageFrictionUtilization = wheels.Length == 0 ? 0f : AverageFrictionUtilization(),
                TractionControlReduction = 0,
                AbsReduction = 0,
                CountersteeringContribution = 0,
                BrakeLightsRequired = serviceBrake > 0.01f || forwardSpeed < -0.25f,
                GroundedWheels = groundedWheels,
                NitrousSeconds = nitrous == null ? 0f : nitrous.RemainingSeconds,
                Gear = powertrainOutput.Gear,
                NitrousActive = nitrousActive,
                IsShifting = powertrainOutput.IsShifting
            };
        }

        private float AverageLateralSlip()
        {
            float total = 0f;
            for (int i = 0; i < wheels.Length; i++) total += Mathf.Abs(wheels[i].LateralSlipRadians);
            return total / wheels.Length;
        }

        private float AverageFrictionUtilization()
        {
            float total = 0f; int count = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                float capacity = Mathf.Max(0.001f, wheels[i].NormalLoad * tuning.tires.lateralGrip);
                total += Mathf.Sqrt(wheels[i].LongitudinalForce * wheels[i].LongitudinalForce + wheels[i].LateralForce * wheels[i].LateralForce) / capacity;
                count++;
            }
            return count == 0 ? 0f : total / count;
        }

        public void ResetVehicle()
        {
            if (body == null)
            {
                return;
            }

            body.position = safePosition + Vector3.up * 0.45f;
            body.rotation = safeRotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
            chassis.Reset();
            foreach (var wheel in wheels) wheel.ResetSimulation();
            if (powertrain != null)
            {
                powertrain.ResetPowertrain();
            }

            if (nitrous != null)
            {
                nitrous.Refill();
            }
            NotifyPoseReset();
        }
    }
}
