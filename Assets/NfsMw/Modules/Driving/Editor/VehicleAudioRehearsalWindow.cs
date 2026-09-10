using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Small play-mode harness that drives the production component with editable telemetry.</summary>
    public sealed class VehicleAudioRehearsalWindow : EditorWindow
    {
        private VehicleAudio target;
        private float rpm = 2500, load = .35f, speed = 18, slip, nitrous;
        private float throttle = .8f, brake;
        private int gear = 2;
        private bool horn, siren, grounded = true;
        private SensorySurface surface = SensorySurface.AsphaltDry;
        private int sequence;
        private double lastTick;
        private bool ownsManualInput;
        private bool previousManualInput;
        private VehicleFeedbackFrame previousFrame;
        private bool hasPreviousFrame;

        [MenuItem("NFS MW Remaster/Audio/Vehicle Rehearsal")]
        private static void OpenFromMenu() => Open();

        public static void Open(VehicleAudio vehicle = null)
        {
            var window = GetWindow<VehicleAudioRehearsalWindow>("VehicleAudio Rehearsal");
            window.SetTarget(vehicle != null ? vehicle : Selection.activeGameObject?.GetComponentInParent<VehicleAudio>());
            window.Show();
        }

        private void OnEnable() { EditorApplication.update += Tick; }
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            ReleaseTarget();
        }

        private void SetTarget(VehicleAudio next)
        {
            if (target == next) return;
            ReleaseTarget();
            target = next;
            hasPreviousFrame = false;
            if (target != null && EditorApplication.isPlaying)
            {
                previousManualInput = target.ManualInput;
                target.ManualInput = true;
                ownsManualInput = true;
                lastTick = EditorApplication.timeSinceStartup;
            }
        }

        private void ReleaseTarget()
        {
            if (target == null || !ownsManualInput) return;
            target.SetHorn(false);
            target.SetSiren(false);
            target.ManualInput = previousManualInput;
            ownsManualInput = false;
        }

        private void OnGUI()
        {
            var next = (VehicleAudio)EditorGUILayout.ObjectField("VehicleAudio", target, typeof(VehicleAudio), true);
            SetTarget(next);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Select a saved VehicleAudio, enter Play Mode, then adjust telemetry and use Send frame/Submit impact. The component discovers its controller and audio world on startup.", MessageType.Info);
                return;
            }
            if (target == null) { EditorGUILayout.HelpBox("Select a VehicleAudio component.", MessageType.Warning); return; }
            using (var serialized = new SerializedObject(target))
            {
                serialized.Update();
                var mix = serialized.FindProperty("mix");
                if (mix != null)
                {
                    mix.isExpanded = EditorGUILayout.Foldout(mix.isExpanded, "Live author mix", true);
                    if (mix.isExpanded)
                    {
                        EditorGUILayout.PropertyField(mix.FindPropertyRelative("mute"));
                        EditorGUILayout.PropertyField(mix.FindPropertyRelative("gain"));
                        EditorGUILayout.PropertyField(mix.FindPropertyRelative("pitch"));
                        EditorGUILayout.PropertyField(mix.FindPropertyRelative("tempo"));
                        EditorGUILayout.PropertyField(mix.FindPropertyRelative("channels"), new GUIContent("Per-channel controls"), true);
                    }
                }
                serialized.ApplyModifiedProperties();
            }
            rpm = EditorGUILayout.Slider("RPM", rpm, 0, 10000);
            load = EditorGUILayout.Slider("Engine load", load, 0, 1);
            speed = EditorGUILayout.Slider("Speed", speed, 0, 90);
            gear = EditorGUILayout.IntSlider("Gear", gear, -1, 8);
            slip = EditorGUILayout.Slider("Wheel slip", slip, 0, 1);
            throttle = EditorGUILayout.Slider("Throttle", throttle, 0, 1);
            brake = EditorGUILayout.Slider("Brake", brake, 0, 1);
            nitrous = EditorGUILayout.Slider("Nitrous", nitrous, 0, 1);
            grounded = EditorGUILayout.Toggle("Wheels grounded", grounded);
            surface = (SensorySurface)EditorGUILayout.EnumPopup("Surface", surface);
            horn = EditorGUILayout.Toggle("Horn", horn);
            siren = EditorGUILayout.Toggle("Siren", siren);
            if (GUILayout.Button("Send frame now")) SendFrame(0.02f);
            if (GUILayout.Button("Submit impact")) target.SubmitImpact(new FeedbackImpact { Sequence = ++sequence, Time = Time.timeAsDouble,
                Point = target.transform.position, Normal = Vector3.up, Severity = .7f, NormalSpeed = speed, Material = surface, BodyMaterial = SensorySurface.Metal });
        }

        private void Tick()
        {
            if (EditorApplication.isPlaying && target != null)
            {
                if (!EditorApplication.isPaused)
                {
                    if (!ownsManualInput) { previousManualInput = target.ManualInput; target.ManualInput = true; ownsManualInput = true; lastTick = EditorApplication.timeSinceStartup; }
                    double now = EditorApplication.timeSinceStartup;
                    float dt = Mathf.Clamp((float)(now - lastTick), .001f, .1f); lastTick = now;
                    SendFrame(dt);
                }
            }
            Repaint();
        }

        private void SendFrame(float dt)
        {
            var frame = target.Frame;
            bool running = rpm > 0, nitroActive = nitrous > .01f;
            var edges = FeedbackEdges.None;
            if (running && (!hasPreviousFrame || !previousFrame.EngineRunning)) edges |= FeedbackEdges.Startup;
            if (gear != previousFrame.Gear && hasPreviousFrame) edges |= gear > previousFrame.Gear ? FeedbackEdges.Upshift : FeedbackEdges.Downshift;
            if (rpm >= 7900 && (!hasPreviousFrame || previousFrame.EngineRpm < 7900)) edges |= FeedbackEdges.Limiter;
            if (nitroActive && (!hasPreviousFrame || previousFrame.NitroIntensity <= .01f)) edges |= FeedbackEdges.NitroOn;
            if (!nitroActive && hasPreviousFrame && previousFrame.NitroIntensity > .01f) edges |= FeedbackEdges.NitroOff;
            frame.Sequence = ++sequence; frame.Epoch = 1; frame.Time = Time.timeAsDouble;
            frame.Position = target.transform.position; frame.Rotation = target.transform.rotation;
            frame.EngineRunning = running; frame.EngineRpm = rpm; frame.NormalizedRpm = Mathf.Clamp01(rpm / 8000); frame.EngineLoad = load;
            frame.Edges = edges;
            frame.Speed = speed; frame.SpeedIntensity = Mathf.Clamp01(speed / 90); frame.Gear = gear; frame.WheelCount = 4;
            frame.Throttle = throttle; frame.Brake = brake; frame.NitroIntensity = nitrous; frame.NitrousFlow = nitrous; frame.FrontSlip = slip; frame.RearSlip = slip;
            frame.Capabilities = FeedbackCapabilities.Engine | FeedbackCapabilities.Wheels | FeedbackCapabilities.Nitrous;
            for (int i = 0; i < 4; i++) frame.SetWheel(i, new WheelFeedback { Grounded = grounded, Driven = i >= 2, Front = i < 2,
                Surface = surface, Load = grounded ? 700 : 0, Slip = slip, Point = target.transform.position + Vector3.up * .25f });
            target.SubmitFrame(frame);
            target.SetHorn(horn);
            target.SetSiren(siren);
            target.Advance(dt);
            previousFrame = frame; hasPreviousFrame = true;
        }
    }
}
