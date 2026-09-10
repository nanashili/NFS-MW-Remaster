using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>Exercises real installed Input System devices and the existing vehicle input component.</summary>
    public sealed class MostWantedFrontendInputTests
    {
        private static readonly FieldInfo RuntimeField = typeof(MostWantedFrontendPreferences).GetField("runtime", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo OwnersField = typeof(MapInputFocus).GetField("owners", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo ReleasedFrameField = typeof(MapInputFocus).GetField("releasedFrame", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo UpdateInput = typeof(PlayerVehicleInput).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        private Keyboard keyboard, previousKeyboard;
        private Gamepad gamepad, previousGamepad;
        private GameObject root;
        private PlayerVehicleInput input;
        private MostWantedFrontendPreferences preferences, previousRuntime;
        private FrontendMemoryStore store;
        private FrontendTestBackend backend;
        private object[] previousOwners;
        private int previousReleasedFrame;
        private HashSet<object> Owners => (HashSet<object>)OwnersField.GetValue(null);

        [SetUp]
        public void SetUp()
        {
            Assert.That(RuntimeField, Is.Not.Null); Assert.That(OwnersField, Is.Not.Null);
            Assert.That(ReleasedFrameField, Is.Not.Null); Assert.That(UpdateInput, Is.Not.Null);
            previousRuntime = (MostWantedFrontendPreferences)RuntimeField.GetValue(null);
            previousOwners = new object[Owners.Count]; Owners.CopyTo(previousOwners); Owners.Clear();
            previousReleasedFrame = (int)ReleasedFrameField.GetValue(null); ReleasedFrameField.SetValue(null, -1);
            previousKeyboard = Keyboard.current; previousGamepad = Gamepad.current;
            keyboard = InputSystem.AddDevice<Keyboard>(); gamepad = InputSystem.AddDevice<Gamepad>();
            keyboard.MakeCurrent(); gamepad.MakeCurrent();
            var initial = new MostWantedFrontendSettings();
            store = new FrontendMemoryStore(); backend = new FrontendTestBackend(initial);
            preferences = new MostWantedFrontendPreferences(initial, store, backend);
            RuntimeField.SetValue(null, preferences);
            root = new GameObject("Frontend input integration fixture"); input = root.AddComponent<PlayerVehicleInput>();
            SetKeyboard(); SetGamepad(default);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            preferences?.Dispose(); RuntimeField.SetValue(null, previousRuntime);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (previousGamepad != null && previousGamepad.added) previousGamepad.MakeCurrent();
            Owners.Clear(); if (previousOwners != null) foreach (object owner in previousOwners) Owners.Add(owner);
            ReleasedFrameField.SetValue(null, previousReleasedFrame);
        }

        [Test]
        public void ACommittedBindingDrivesTheExistingPlayerVehicleInputReader()
        {
            RebindThrottle();
            SetKeyboard(Key.I); Sample(); Assert.That(input.Current.Throttle, Is.EqualTo(1));
            SetKeyboard(Key.W); Sample(); Assert.That(input.Current.Throttle, Is.Zero, "The replaced primary key must no longer accelerate.");
            SetKeyboard(Key.UpArrow); Sample(); Assert.That(input.Current.Throttle, Is.EqualTo(1), "The secondary binding must remain functional.");
        }

        [Test]
        public void StagingAndCancellingARebindCannotChangeDrivingControls()
        {
            preferences.BeginEdit();
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Throttle, 0, Key.I, out _), Is.True);
            SetKeyboard(Key.I); Sample(); Assert.That(input.Current.Throttle, Is.Zero);
            SetKeyboard(Key.W); Sample(); Assert.That(input.Current.Throttle, Is.EqualTo(1));
            preferences.Cancel();
            SetKeyboard(Key.I); Sample(); Assert.That(input.Current.Throttle, Is.Zero);
        }

        [Test]
        public void SavedBindingsSurviveReloadAndKeyboardReplacement()
        {
            RebindThrottle(); preferences.Dispose();
            preferences = new MostWantedFrontendPreferences(new MostWantedFrontendSettings(), store, backend);
            RuntimeField.SetValue(null, preferences);
            InputSystem.RemoveDevice(keyboard); keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            SetKeyboard(Key.I); Sample(); Assert.That(input.Current.Throttle, Is.EqualTo(1));
            SetKeyboard(Key.W); Sample(); Assert.That(input.Current.Throttle, Is.Zero);
        }

        [Test]
        public void MapOwnershipSuppressesReboundControlsIncludingTheReleaseFrame()
        {
            RebindThrottle(); SetKeyboard(Key.I); Sample(); Assert.That(input.Current.Throttle, Is.EqualTo(1));
            object owner = new object(); MapInputFocus.Acquire(owner);
            Sample(); Assert.That(input.Current.Throttle, Is.Zero);
            MapInputFocus.Release(owner); Sample();
            Assert.That(input.Current.Throttle, Is.Zero, "The key that closes a modal must not leak into driving on that frame.");
        }

        [Test]
        public void GamepadSteeringStillUsesExactlyTheExistingSingleDeadZone()
        {
            SetGamepad(new GamepadState { leftStick = new Vector2(.5f, 0), rightTrigger = .65f, leftTrigger = .2f });
            Sample();
            Assert.That(input.Current.Steering, Is.EqualTo((.5f - .08f) / (1 - .08f)).Within(.0001f));
            Assert.That(input.Current.Throttle, Is.EqualTo(.65f).Within(.0001f));
            Assert.That(input.Current.Brake, Is.EqualTo(.2f).Within(.0001f));
        }

        [Test]
        public void HandbrakeAndNitrousBindingsReachTheExistingReader()
        {
            preferences.BeginEdit();
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Handbrake, 0, Key.H, out _), Is.True);
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Nitrous, 0, Key.J, out _), Is.True);
            Assert.That(preferences.TryApply(out string failure), Is.True, failure);
            SetKeyboard(Key.H, Key.J); Sample();
            Assert.That(input.Current.Handbrake, Is.True); Assert.That(input.Current.Nitrous, Is.True);
            SetKeyboard(Key.Space, Key.LeftShift); Sample();
            Assert.That(input.Current.Handbrake, Is.False); Assert.That(input.Current.Nitrous, Is.False);
        }

        [Test]
        public void CancellingCaptureReleasesOnlyTheServicesOwnFocusLease()
        {
            object independentOwner = new object(); MapInputFocus.Acquire(independentOwner);
            preferences.BeginEdit();
            Assert.That(preferences.BeginRebind(MostWantedFrontendAction.Throttle, 0, out string failure), Is.True, failure);
            Assert.That(Owners.Contains(preferences), Is.True);
            Assert.That(preferences.CancelRebinding(), Is.True);
            Assert.That(preferences.CancelRebinding(), Is.True, "A second callback on the same input frame must not navigate away.");
            Assert.That(preferences.RebindingConsumesInput, Is.True);
            Assert.That(Owners.Contains(preferences), Is.False);
            Assert.That(Owners.Contains(independentOwner), Is.True);
            Assert.That(preferences.Current.keyboard.Get(MostWantedFrontendAction.Throttle), Is.EqualTo(Key.W));
            Assert.That(preferences.IsDirty, Is.False);
        }

        [Test]
        public void CaptureTimeoutReleasesItsLeaseWithoutSavingOrChangingBindings()
        {
            preferences.BeginEdit();
            Assert.That(preferences.BeginRebind(MostWantedFrontendAction.Throttle, 0, out _), Is.True);
            backend.Now += MostWantedFrontendPreferences.RebindingDuration + .1;
            preferences.Tick();
            Assert.That(preferences.IsRebinding, Is.False);
            Assert.That(Owners.Contains(preferences), Is.False);
            Assert.That(preferences.IsDirty, Is.False);
            Assert.That(store.SaveCount, Is.Zero);
        }

        private void RebindThrottle()
        {
            preferences.BeginEdit();
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Throttle, 0, Key.I, out string failure), Is.True, failure);
            Assert.That(preferences.TryApply(out failure), Is.True, failure);
        }
        private void Sample() => UpdateInput.Invoke(input, null);
        private void SetKeyboard(params Key[] keys)
        {
            keyboard.MakeCurrent(); InputSystem.Update();
            InputState.Change(keyboard, new KeyboardState(keys), InputState.currentUpdateType);
        }
        private void SetGamepad(GamepadState state)
        {
            gamepad.MakeCurrent(); InputSystem.Update();
            InputState.Change(gamepad, state, InputState.currentUpdateType);
        }
    }
}
