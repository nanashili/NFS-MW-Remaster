using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Input fixture used only by AdaptiveMusicTest.unity. It injects semantic
    /// snapshots into the runtime director; it does not own game flow, heat or
    /// pursuit state.
    /// </summary>
    public sealed class AdaptiveMusicTestHarness : MonoBehaviour
    {
        [SerializeField] private AdaptiveMusic director;
        [SerializeField] private bool showOnScreenGuide = true;
        private string lastAction = "Waiting for a context key.";

        private void Awake()
        {
            if (director == null) director = GetComponent<AdaptiveMusic>();
            if (director == null) director = FindAnyObjectByType<AdaptiveMusic>();
        }

        private void Update()
        {
            if (director == null || Keyboard.current == null) return;
            var keyboard = Keyboard.current;
            if (keyboard.digit0Key.wasPressedThisFrame) { director.ClearGameplaySnapshot(); lastAction = "Returned to runtime flow."; }
            else if (keyboard.digit1Key.wasPressedThisFrame) Set(AdaptiveMusicContext.Frontend, 0.12f);
            else if (keyboard.digit2Key.wasPressedThisFrame) Set(AdaptiveMusicContext.FreeRoam, 0.08f);
            else if (keyboard.digit3Key.wasPressedThisFrame) Set(AdaptiveMusicContext.Race, 0.55f);
            else if (keyboard.digit4Key.wasPressedThisFrame) Set(AdaptiveMusicContext.Pursuit, 0.82f);
            else if (keyboard.digit5Key.wasPressedThisFrame) Set(AdaptiveMusicContext.Cooldown, 0.38f);
            else if (keyboard.digit6Key.wasPressedThisFrame) { Set(AdaptiveMusicContext.Escape, 0.45f); director.RequestStinger("outcome.escaped", "fixture.input"); }
            else if (keyboard.digit7Key.wasPressedThisFrame) { Set(AdaptiveMusicContext.Results, 0.18f); director.RequestStinger("outcome.paid-fine", "fixture.input"); }
            else if (keyboard.fKey.wasPressedThisFrame) { Set(AdaptiveMusicContext.Failure, 0.24f); director.RequestStinger("outcome.arrested", "fixture.input"); }
            else if (keyboard.pKey.wasPressedThisFrame) { Set(AdaptiveMusicContext.Pause, 0.12f); lastAction = "Injected pause context."; }
        }

        private void Set(AdaptiveMusicContext context, float intensity)
        {
            director.SetGameplaySnapshot(MusicGameplaySnapshot.ForContext(context, intensity, "fixture.input"));
            lastAction = "Injected " + context + " at " + intensity.ToString("0.00") + ".";
        }

        private void OnGUI()
        {
            if (!showOnScreenGuide || director == null) return;
            var transport = director.Transport;
            GUILayout.BeginArea(new Rect(16, 16, 420, 152), GUI.skin.box);
            GUILayout.Label("ADAPTIVE MUSIC FIXTURE");
            GUILayout.Label("1 Frontend  2 Roam  3 Race  4 Pursuit  5 Cooldown");
            GUILayout.Label("6 Escape + stinger  7 Results + stinger  F Arrested  P Pause  0 Runtime");
            GUILayout.Label("Context: " + transport.context + "   Section: " + transport.activeSectionId);
            GUILayout.Label("Beat " + transport.beat.ToString("0.00") + "   Bar " + transport.bar + "   " + lastAction);
            GUILayout.EndArea();
        }
    }
}
