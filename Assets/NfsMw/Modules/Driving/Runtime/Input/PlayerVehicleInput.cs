using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Default keyboard/gamepad adapter. It is intentionally separate from
    /// VehicleController so AI, replay, networking, or a steering wheel can
    /// provide the same IVehicleInputSource contract later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVehicleInput : MonoBehaviour, IVehicleInputSource, IVehicleDiscreteInputSource
    {
        [SerializeField, Range(0f, 0.5f)] private float stickDeadZone = 0.08f;

        private VehicleInputState current;
        private bool resetRequested;
        private bool cameraToggleRequested;
        private VehicleInputState pendingActions;

        public VehicleInputState Current
        {
            get { return MapInputFocus.Captured ? VehicleInputState.Neutral : current; }
        }

        private void Update()
        {
            if (MapInputFocus.Captured) { current = VehicleInputState.Neutral; resetRequested = cameraToggleRequested = false; return; }
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;

            float keyboardSteering = 0f;
            float keyboardThrottle = 0f;
            float keyboardBrake = 0f;

            if (keyboard != null)
            {
                keyboardSteering = (MostWantedFrontendBindings.IsPressed(keyboard, MostWantedFrontendAction.SteerRight) ? 1f : 0f)
                    - (MostWantedFrontendBindings.IsPressed(keyboard, MostWantedFrontendAction.SteerLeft) ? 1f : 0f);
                keyboardThrottle = MostWantedFrontendBindings.IsPressed(keyboard, MostWantedFrontendAction.Throttle) ? 1f : 0f;
                keyboardBrake = MostWantedFrontendBindings.IsPressed(keyboard, MostWantedFrontendAction.Brake) ? 1f : 0f;

                if (keyboard.bKey.wasPressedThisFrame)
                {
                    resetRequested = true;
                }

                if (MostWantedFrontendBindings.WasPressedThisFrame(keyboard, MostWantedFrontendAction.Camera))
                {
                    cameraToggleRequested = true;
                }

                if (keyboard.xKey.wasPressedThisFrame && GetComponent<FreeRoamSession>()?.CanDrive != false)
                {
                    var vehicle = GetComponent<VehicleController>();
                    if (vehicle != null) vehicle.TrySetEngineRunning(!vehicle.EngineRunning);
                }
            }

            float steering = keyboardSteering;
            float throttle = keyboardThrottle;
            float brake = keyboardBrake;
            bool handbrake = keyboard != null && MostWantedFrontendBindings.IsPressed(keyboard, MostWantedFrontendAction.Handbrake);
            bool nitrous = keyboard != null && MostWantedFrontendBindings.IsPressed(keyboard, MostWantedFrontendAction.Nitrous);
            bool gearUp = keyboard != null && keyboard.eKey.wasPressedThisFrame;
            bool gearDown = keyboard != null && keyboard.qKey.wasPressedThisFrame;
            bool gearNeutral = keyboard != null && keyboard.nKey.isPressed;
            bool gearReverse = keyboard != null && keyboard.rKey.isPressed;
            pendingActions.GearUp |= gearUp; pendingActions.GearDown |= gearDown;

            if (gamepad != null)
            {
                // Own steering dead-zone shaping here. ReadValue() already carries Input System
                // processors, which would apply another axis/stick dead zone before ours and
                // compress the useful analogue range twice.
                float padSteering = ApplyDeadZone(gamepad.leftStick.x.ReadUnprocessedValue());
                float padThrottle = gamepad.rightTrigger.ReadValue();
                float padBrake = gamepad.leftTrigger.ReadValue();

                if (Mathf.Abs(padSteering) > Mathf.Abs(steering))
                {
                    steering = padSteering;
                }

                throttle = Mathf.Max(throttle, padThrottle);
                brake = Mathf.Max(brake, padBrake);
                handbrake |= gamepad.aButton.isPressed;
                nitrous |= gamepad.leftShoulder.isPressed;

                if (gamepad.startButton.wasPressedThisFrame)
                {
                    resetRequested = true;
                }

                if (gamepad.rightShoulder.wasPressedThisFrame)
                {
                    cameraToggleRequested = true;
                }
            }

            current = new VehicleInputState
            {
                Steering = Mathf.Clamp(steering, -1f, 1f),
                Throttle = Mathf.Clamp01(throttle),
                Brake = Mathf.Clamp01(brake),
                Handbrake = handbrake,
                Nitrous = nitrous,
                GearUp = gearUp,
                GearDown = gearDown,
                GearNeutral = gearNeutral,
                GearReverse = gearReverse
            };
        }

        public VehicleInputState ConsumeDiscreteActions()
        {
            VehicleInputState result = pendingActions;
            pendingActions.GearUp = pendingActions.GearDown = false;
            return result;
        }

        public bool ConsumeResetRequest()
        {
            bool result = !MapInputFocus.Captured && resetRequested;
            resetRequested = false;
            return result;
        }

        public bool ConsumeCameraToggleRequest()
        {
            bool result = !MapInputFocus.Captured && cameraToggleRequested;
            cameraToggleRequested = false;
            return result;
        }

        private float ApplyDeadZone(float value)
        {
            float magnitude = Mathf.Abs(value);
            if (magnitude <= stickDeadZone)
            {
                return 0f;
            }

            float remapped = Mathf.InverseLerp(stickDeadZone, 1f, magnitude);
            return Mathf.Sign(value) * remapped;
        }
    }
}
