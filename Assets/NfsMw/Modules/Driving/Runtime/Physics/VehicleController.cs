using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Thin orchestration layer. Every major concern is a replaceable module:
    /// input, powertrain, wheel/tire, assists, nitrous, and camera can evolve
    /// independently without turning the vehicle into one monolithic script.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(VehiclePowertrain))]
    [RequireComponent(typeof(VehicleAssists))]
    [RequireComponent(typeof(VehicleModuleHost))]
    public sealed class VehicleController : MonoBehaviour
    {
        public const string SimulationRevision = "shared-vehicle.mw-reference.7";
        private const float AutomaticDirectionChangeSpeedKph = 1f;
        private const float AutomaticDirectionIntentDeadZone = 0.05f;
        private enum AutomaticDriveDirection
        {
            Forward,
            BrakingToReverse,
            Reverse
        }
        private bool manualSimulation;
        public bool ManualSimulation => manualSimulation;
        public bool UsesBuiltInAero => useBuiltInAero;
        // Authoring/preview adapters must reject serialized physics references outside this chassis before cloning.
        public bool HasLocalPhysicsBindings => (powertrain == null || powertrain.gameObject == gameObject)
            && (assists == null || assists.gameObject == gameObject)
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
        [SerializeField] private VehicleAssists assists;
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
        private float smoothedThrottle;
        private float smoothedBrake;
        private AutomaticDriveDirection automaticDriveDirection;
        private bool configured;
        private readonly MostWantedSteeringModel referenceSteering = new MostWantedSteeringModel();
        private float referenceFrontAxle = 1.3f, referenceRearAxle = -1.3f, referenceFrontTrack = 1.5f;

        public VehicleTelemetry Telemetry { get; private set; }
        public VehicleTuning FactoryTuning => stockTuning;
        public event System.Action<float> PhysicsSampled;
        public event System.Action PoseReset;
        public void NotifyPoseReset() { referenceSteering.Reset(); PoseReset?.Invoke(); }
        public void ResetSimulationForPool()
        {
            if (gameObject.activeSelf) throw new System.InvalidOperationException("Pool reset requires an inactive vehicle.");
            if (!configured) ConfigureModules();
            smoothedSteering = smoothedThrottle = smoothedBrake = 0;
            automaticDriveDirection = AutomaticDriveDirection.Forward;
            Telemetry = default;
            for (int i = 0; i < wheels.Length; i++) { wheels[i].Configure(body, tuning); wheels[i].ResetSimulation(); }
            powertrain.Configure(tuning, wheels); powertrain.ResetPowertrain(); powertrain.SetIgnition(true);
            if (nitrous != null) nitrous.ResetForSpawn();
            assists.Configure(body, tuning, wheels);
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
            assists.BeginStep(deltaTime);
            float shapedSteering;
            float steeringAngle;
            if (tuning.UsesMostWantedReference && tuning.mostWanted.useSteeringTables)
            {
                float rearSlipDegrees = GetReferenceRearSlipDegrees(rawInput.Steering);
                steeringAngle = referenceSteering.Step(tuning.mostWanted, rawInput, forwardSpeed,
                    rearSlipDegrees, deltaTime);
                // The recovered steering model already outputs a physical wheel angle. Do not
                // run it through the authored max-angle/smoothing path a second time. This also
                // keeps source-backed player steering independent from generic authored caps;
                // NPC/reference-adaptation profiles opt out of these tables entirely.
                smoothedSteering = Mathf.Clamp(steeringAngle / MostWantedSteeringModel.AbsoluteMaximumDegrees, -1f, 1f);
                shapedSteering = smoothedSteering;
            }
            else
            {
                shapedSteering = VehicleMath.ShapeSteering(rawInput.Steering, speedKph, tuning.controls);
                shapedSteering = assists.ResolveSteering(shapedSteering, forwardSpeed, rawInput.Handbrake);
                smoothedSteering = VehicleMath.SmoothInput(
                    smoothedSteering,
                    shapedSteering,
                    tuning.controls.steeringResponse,
                    deltaTime);
                steeringAngle = smoothedSteering * tuning.controls.maxSteerAngle;
            }
            smoothedThrottle = VehicleMath.SmoothInput(
                smoothedThrottle,
                rawInput.Throttle,
                tuning.controls.throttleResponse,
                deltaTime);
            smoothedBrake = VehicleMath.SmoothInput(
                smoothedBrake,
                rawInput.Brake,
                tuning.controls.brakeResponse,
                deltaTime);

            bool reverseRequested;
            float driveThrottle;
            float serviceBrake;
            ResolveDriveIntent(forwardSpeed, rawInput, out driveThrottle, out serviceBrake, out reverseRequested);

            bool nitrousActive = nitrous != null
                && nitrous.Tick(deltaTime, EngineRunning && rawInput.Nitrous && driveThrottle > 0.01f, speedKph, powertrain.Gear);
            powertrain.SetManualRequests(rawInput.GearUp, rawInput.GearDown, rawInput.GearNeutral, rawInput.GearReverse);
            VehiclePowertrainOutput powertrainOutput = powertrain.Simulate(
                deltaTime,
                forwardSpeed,
                driveThrottle,
                reverseRequested,
                nitrousActive);

            int drivenWheelCount = CountDrivenWheels();
            float forwardBrakeTorque = serviceBrake
                * tuning.controls.serviceBrakeTorque
                * tuning.controls.frontBrakeBias;
            float rearBrakeTorque = serviceBrake
                * tuning.controls.serviceBrakeTorque
                * (1f - tuning.controls.frontBrakeBias);

            for (int i = 0; i < wheels.Length; i++)
            {
                VehicleWheel wheel = wheels[i];
                float driveShare = powertrain.GetDriveTorqueShare(wheel, wheels);
                float driveTorque = driveShare > 0f
                    ? powertrainOutput.WheelTorque * driveShare
                    : 0f;
                // The shared Unity adapter has no original clutch/road-reaction solve.
                // Deliver negative engine torque as dissipative wheel braking, not as a
                // reverse drive motor. The wheel integrator then cannot cross zero angular
                // momentum and propel an idle car backward. Positive torque in reverse is
                // still propulsion; engine torque, not wheel-torque sign, distinguishes it.
                float engineBrakeTorque = 0f;
                if (tuning.UsesMostWantedReference && powertrainOutput.EngineTorque < 0f)
                {
                    engineBrakeTorque = Mathf.Abs(driveTorque);
                    driveTorque = 0f;
                }
                if (rawInput.Handbrake && wheel.IsHandbrakeWheel)
                {
                    driveTorque = 0f;
                }

                driveTorque *= assists.GetDriveTorqueMultiplier(wheel);
                float brakeTorque = wheel.IsFrontWheel ? forwardBrakeTorque : rearBrakeTorque;
                brakeTorque *= assists.GetBrakeTorqueMultiplier(wheel, brakeTorque > 0.01f);
                brakeTorque += engineBrakeTorque; // Service-brake ABS does not govern engine braking.
                if (rawInput.Handbrake && wheel.IsHandbrakeWheel)
                {
                    brakeTorque += tuning.controls.handbrakeTorque;
                }

                float lateralGripMultiplier = rawInput.Handbrake
                    && wheel.IsHandbrakeWheel
                    ? tuning.tires.handbrakeRearGrip
                    : 1f;

                float wheelSteering = steeringAngle;
                if (tuning.UsesMostWantedReference && tuning.mostWanted.useSteeringTables && wheel.IsFrontWheel)
                {
                    float side = transform.InverseTransformPoint(wheel.transform.position).x;
                    if (side * steeringAngle < 0f)
                        wheelSteering = MostWantedVehicleMath.OutsideSteerDegrees(steeringAngle,
                            referenceFrontAxle - referenceRearAxle, referenceFrontTrack);
                }
                wheel.Simulate(deltaTime, new VehicleWheelCommand
                {
                    SteerAngle = wheel.IsSteeringWheel ? wheelSteering : 0f,
                    DriveTorque = driveTorque,
                    BrakeTorque = brakeTorque,
                    LongitudinalGripMultiplier = 1f,
                    LateralGripMultiplier = lateralGripMultiplier
                });
            }

            if (useBuiltInAero)
            {
                ApplyAeroForces(driveThrottle);
            }

            VehicleInputState handlingInput = rawInput;
            handlingInput.Steering = smoothedSteering;
            assists.ApplyPostWheelForces(forwardSpeed, handlingInput, deltaTime);
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

            if (assists == null)
            {
                assists = GetComponent<VehicleAssists>();
            }

            if (nitrous == null)
            {
                nitrous = GetComponent<VehicleNitrous>();
            }

            if (wheels == null || wheels.Length == 0)
            {
                wheels = GetComponentsInChildren<VehicleWheel>();
            }

            if (moduleHost != null)
            {
                moduleHost.Configure(this, body, tuning, wheels);
                tuning = moduleHost.Context.Tuning;
            }

            body.mass = tuning.chassis.mass;
            body.centerOfMass = tuning.chassis.centerOfMass;
            body.linearDamping = 0.02f;
            body.angularDamping = 0.05f;
            body.maxAngularVelocity = 25f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i].Configure(body, tuning);
            }
            if (tuning.UsesMostWantedReference)
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
            automaticDriveDirection = tuning.transmissionMode == VehicleTransmissionMode.Automatic && powertrain.Gear < 0
                ? AutomaticDriveDirection.Reverse
                : AutomaticDriveDirection.Forward;
            assists.Configure(body, tuning, wheels);
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

        private void ResolveDriveIntent(
            float forwardSpeed,
            VehicleInputState rawInput,
            out float driveThrottle,
            out float serviceBrake,
            out bool reverseRequested)
        {
            driveThrottle = smoothedThrottle;
            serviceBrake = smoothedBrake;
            reverseRequested = false;

            if (tuning.transmissionMode == VehicleTransmissionMode.Automatic)
            {
                float directionChangeSpeed = VehicleMath.KphToMetersPerSecond(AutomaticDirectionChangeSpeedKph);
                bool nearDirectionChange = Mathf.Abs(forwardSpeed) <= directionChangeSpeed;
                bool wantsReverse = rawInput.GearReverse
                    || rawInput.Brake > rawInput.Throttle + AutomaticDirectionIntentDeadZone;
                bool wantsForward = !rawInput.GearReverse
                    && rawInput.Throttle > rawInput.Brake + AutomaticDirectionIntentDeadZone;

                switch (automaticDriveDirection)
                {
                    case AutomaticDriveDirection.Forward:
                        if (wantsReverse)
                            automaticDriveDirection = nearDirectionChange
                                ? AutomaticDriveDirection.Reverse
                                : AutomaticDriveDirection.BrakingToReverse;
                        break;
                    case AutomaticDriveDirection.BrakingToReverse:
                        if (!wantsReverse || wantsForward)
                            automaticDriveDirection = AutomaticDriveDirection.Forward;
                        else if (nearDirectionChange)
                            automaticDriveDirection = AutomaticDriveDirection.Reverse;
                        break;
                    case AutomaticDriveDirection.Reverse:
                        if (wantsForward && nearDirectionChange)
                            automaticDriveDirection = AutomaticDriveDirection.Forward;
                        break;
                }

                if (automaticDriveDirection == AutomaticDriveDirection.BrakingToReverse)
                {
                    driveThrottle = 0f;
                    serviceBrake = smoothedBrake;
                }
                else if (automaticDriveDirection == AutomaticDriveDirection.Reverse)
                {
                    reverseRequested = true;
                    if (wantsForward)
                    {
                        driveThrottle = 0f;
                        serviceBrake = smoothedThrottle;
                    }
                    else
                    {
                        driveThrottle = smoothedBrake;
                        serviceBrake = 0f;
                    }
                }
            }
            else automaticDriveDirection = AutomaticDriveDirection.Forward;

            if (rawInput.Handbrake && serviceBrake < 0.1f)
            {
                serviceBrake = 0f;
            }
        }

        private float GetReferenceRearSlipDegrees(float steeringInput)
        {
            if (wheels == null || Mathf.Abs(steeringInput) < 0.0001f) return 0f;
            VehicleWheel fallback = null;
            for (int i = 0; i < wheels.Length; i++)
            {
                VehicleWheel wheel = wheels[i];
                if (wheel == null || wheel.IsFrontWheel || !wheel.Grounded) continue;
                fallback ??= wheel;
                float side = transform.InverseTransformPoint(wheel.transform.position).x;
                if (steeringInput > 0f && side > 0f || steeringInput < 0f && side < 0f)
                    return wheel.LateralSlipRadians * Mathf.Rad2Deg;
            }
            return fallback != null ? fallback.LateralSlipRadians * Mathf.Rad2Deg : 0f;
        }

        private void ApplyAeroForces(float throttle)
        {
            VehicleTuning.AeroSettings aero = tuning.aero;
            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed > 0.05f)
            {
                float dragMagnitude = 0.5f
                    * aero.airDensity
                    * aero.dragCoefficient
                    * aero.frontalArea
                    * speed
                    * speed;
                if (tuning.UsesMostWantedReference)
                {
                    int contacts = 0; foreach (var wheel in wheels) if (wheel.Grounded) contacts++;
                    Vector3 center = body.centerOfMass;
                    if (contacts >= 2) center.y -= .1f * (1f - Mathf.Clamp01(throttle));
                    body.AddForceAtPosition(-velocity.normalized * dragMagnitude * (2f - Mathf.Clamp01(throttle)), transform.TransformPoint(center), ForceMode.Force);
                }
                else
                {
                    body.AddForce(-velocity.normalized * dragMagnitude, ForceMode.Force);
                    body.AddForce(-velocity.normalized * tuning.chassis.rollingResistance * tuning.chassis.mass * Physics.gravity.magnitude, ForceMode.Force);
                }
            }

            float speedSquared = speed * speed;
            float downforceMagnitude = aero.downforceCoefficient * speedSquared;
            if (tuning.UsesMostWantedReference)
            {
                int contacts = 0; foreach (var wheel in wheels) if (wheel.Grounded) contacts++;
                float forwardness = speed > .0001f ? Vector3.Dot(velocity / speed, transform.forward) : 1f;
                float force = MostWantedVehicleMath.ReferenceDownforce(speed, tuning.mostWanted.linearDownforceCoefficient,
                    forwardness, transform.up.y, contacts);
                Vector3 center = body.centerOfMass;
                if (contacts > 0) center.z = Mathf.Lerp(referenceRearAxle, referenceFrontAxle, aero.downforceBalance);
                body.AddForceAtPosition(-transform.up * force, transform.TransformPoint(center), ForceMode.Force);
            }
            else if (downforceMagnitude > 0f)
            {
                Vector3 downforce = -transform.up * downforceMagnitude;
                Vector3 frontPoint = transform.position + transform.forward * 1.2f;
                Vector3 rearPoint = transform.position - transform.forward * 1.2f;
                body.AddForceAtPosition(downforce * aero.downforceBalance, frontPoint, ForceMode.Force);
                body.AddForceAtPosition(downforce * (1f - aero.downforceBalance), rearPoint, ForceMode.Force);
            }

            float maxSpeed = VehicleMath.KphToMetersPerSecond(tuning.chassis.maxSpeedKph);
            if (tuning.speedGovernor && speed > maxSpeed && Vector3.Dot(velocity, transform.forward) > 0f)
            {
                body.AddForce(
                    -velocity.normalized * (speed - maxSpeed) * 4f,
                    ForceMode.Acceleration);
            }
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
                HandlingMode = assists == null ? VehicleHandlingMode.Grip : assists.HandlingMode,
                AssistYawTorque = assists == null ? 0f : assists.AppliedYawTorque,
                AverageLateralSlipRadians = wheels.Length == 0 ? 0f : AverageLateralSlip(),
                AverageFrictionUtilization = wheels.Length == 0 ? 0f : AverageFrictionUtilization(),
                TractionControlReduction = assists == null ? 0f : assists.LastTractionControlReduction,
                AbsReduction = assists == null ? 0f : assists.LastAbsReduction,
                CountersteeringContribution = assists == null ? 0f : assists.LastCountersteering,
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

        private int CountDrivenWheels()
        {
            int count = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i].IsDrivenWheel)
                {
                    count++;
                }
            }

            return count;
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
            if (assists != null) assists.ResetHandling();
            if (powertrain != null)
            {
                powertrain.ResetPowertrain();
            }
            automaticDriveDirection = AutomaticDriveDirection.Forward;

            if (nitrous != null)
            {
                nitrous.Refill();
            }
            NotifyPoseReset();
        }
    }
}
