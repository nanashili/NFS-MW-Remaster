using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving
{
    public enum MostWantedFrontendPreferenceSection { All, Audio, Video, Gameplay, Player, Controls }
    public enum MostWantedFrontendAction { SteerLeft, SteerRight, Throttle, Brake, Handbrake, Nitrous, Camera }
    public enum MostWantedFrontendMusicOrder { Sequential, Random }

    /// <summary>Local presentation preferences. Career progress and vehicle tuning never belong here.</summary>
    [Serializable]
    public sealed class MostWantedFrontendSettings
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        // Persisted through the existing sensory.preferences.v1 key, not the frontend document.
        public SensoryPreferences audio = new SensoryPreferences();
        public int width = 1920, height = 1080;
        public FullScreenMode fullScreenMode = FullScreenMode.Windowed;
        public int qualityLevel, vSyncCount = 1, targetFrameRate = -1;
        public bool pauseOnFocusLoss = true;
        public bool hoodCamera;
        public float menuMusic = .7f;
        public bool eaTraxEnabled = true;
        public MostWantedFrontendMusicOrder eaTraxOrder = MostWantedFrontendMusicOrder.Sequential;
        public string playerAlias = "free_roam_profile";
        public MostWantedFrontendKeyboardBindings keyboard = new MostWantedFrontendKeyboardBindings();

        public MostWantedFrontendSettings Clone()
        {
            var copy = (MostWantedFrontendSettings)MemberwiseClone();
            copy.audio = CloneAudio(audio);
            copy.keyboard = keyboard?.Clone();
            return copy;
        }

        public static SensoryPreferences CloneAudio(SensoryPreferences source) => source == null ? null : new SensoryPreferences
        {
            master = source.master, vehicle = source.vehicle, effects = source.effects, music = source.music,
            police = source.police, cameraMotion = source.cameraMotion, haptics = source.haptics,
            flashes = source.flashes, subtitles = source.subtitles
        };

        public bool TryValidate(out string failure)
        {
            if (version != CurrentVersion) return Fail("Unsupported frontend preferences version.", out failure);
            if (audio == null || !Unit(audio.master) || !Unit(audio.vehicle) || !Unit(audio.effects)
                || !Unit(audio.music) || !Unit(audio.police) || !Unit(menuMusic)) return Fail("Audio levels must be finite values between zero and one.", out failure);
            if (!Enum.IsDefined(typeof(MostWantedFrontendMusicOrder), eaTraxOrder))
                return Fail("Unknown EA TRAX order.", out failure);
            if (width < 320 || width > 16384 || height < 200 || height > 16384)
                return Fail("Display dimensions are outside the supported range.", out failure);
            if (!Enum.IsDefined(typeof(FullScreenMode), fullScreenMode)) return Fail("Unknown display mode.", out failure);
            if (qualityLevel < 0) return Fail("Unknown quality preset.", out failure);
            if (vSyncCount < 0 || vSyncCount > 4) return Fail("VSync must be between zero and four refresh intervals.", out failure);
            if (targetFrameRate != -1 && (targetFrameRate < 30 || targetFrameRate > 360))
                return Fail("The frame limit must be unlimited or between 30 and 360 FPS.", out failure);
            if (!TryValidateAlias(playerAlias, out failure)) return false;
            if (keyboard == null) return Fail("Keyboard bindings are missing.", out failure);
            return keyboard.TryValidate(out failure);
        }

        public static bool TryValidateAlias(string alias, out string failure)
        {
            if (string.IsNullOrWhiteSpace(alias) || alias.Length > 64 || alias != alias.Trim())
                return Fail("Use a career alias of 1–64 letters, numbers, hyphens or underscores.", out failure);
            foreach (char character in alias)
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_')
                    return Fail("Career aliases allow letters, numbers, hyphens and underscores.", out failure);
            failure = string.Empty; return true;
        }

        public bool SameDisplay(MostWantedFrontendSettings other) => other != null && width == other.width
            && height == other.height && fullScreenMode == other.fullScreenMode;

        public bool EquivalentTo(MostWantedFrontendSettings other)
        {
            if (other == null || version != other.version || !SameDisplay(other) || qualityLevel != other.qualityLevel
                || vSyncCount != other.vSyncCount || targetFrameRate != other.targetFrameRate
                || pauseOnFocusLoss != other.pauseOnFocusLoss || hoodCamera != other.hoodCamera || menuMusic != other.menuMusic
                || eaTraxEnabled != other.eaTraxEnabled || eaTraxOrder != other.eaTraxOrder || playerAlias != other.playerAlias)
                return false;
            if (audio == null || other.audio == null) { if (!ReferenceEquals(audio, other.audio)) return false; }
            else if (audio.master != other.audio.master || audio.vehicle != other.audio.vehicle || audio.effects != other.audio.effects
                || audio.music != other.audio.music || audio.police != other.audio.police || audio.flashes != other.audio.flashes)
                return false;
            return keyboard == null ? other.keyboard == null : keyboard.EquivalentTo(other.keyboard);
        }

        private static bool Unit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
        private static bool Fail(string reason, out string failure) { failure = reason; return false; }
    }

    /// <summary>Bindings for the existing keyboard reader; does not replace the vehicle input source.</summary>
    [Serializable]
    public sealed class MostWantedFrontendKeyboardBindings
    {
        public const int ActionCount = 7;
        public Key[] primary = { Key.A, Key.D, Key.W, Key.S, Key.Space, Key.LeftShift, Key.C };
        public Key[] secondary = { Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow, Key.None, Key.None, Key.None };

        public MostWantedFrontendKeyboardBindings Clone() => new MostWantedFrontendKeyboardBindings
        { primary = primary == null ? null : (Key[])primary.Clone(), secondary = secondary == null ? null : (Key[])secondary.Clone() };

        public Key Get(MostWantedFrontendAction action, int slot = 0)
        {
            ValidateIndex(action, slot);
            var keys = slot == 0 ? primary : secondary;
            return keys != null && keys.Length == ActionCount ? keys[(int)action] : Key.None;
        }

        public bool TrySet(MostWantedFrontendAction action, int slot, Key key, out string failure)
        {
            if ((int)action < 0 || (int)action >= ActionCount || slot < 0 || slot > 1)
            { failure = "Unknown control or binding slot."; return false; }
            var candidate = Clone();
            if (candidate.primary == null || candidate.secondary == null || candidate.primary.Length != ActionCount || candidate.secondary.Length != ActionCount)
            { failure = "The keyboard binding table is malformed."; return false; }
            (slot == 0 ? candidate.primary : candidate.secondary)[(int)action] = key;
            if (!candidate.TryValidate(out failure)) return false;
            primary = candidate.primary; secondary = candidate.secondary; return true;
        }

        public bool TryValidate(out string failure)
        {
            if (primary == null || secondary == null || primary.Length != ActionCount || secondary.Length != ActionCount)
            { failure = "The keyboard binding table is malformed."; return false; }
            for (int action = 0; action < ActionCount; action++)
            {
                if (primary[action] == Key.None && secondary[action] == Key.None)
                { failure = Label((MostWantedFrontendAction)action) + " must have at least one binding."; return false; }
                for (int slot = 0; slot < 2; slot++)
                {
                    Key key = slot == 0 ? primary[action] : secondary[action];
                    if (key == Key.None) continue;
                    if (!IsBindable(key)) { failure = key + " is reserved by an existing game/menu command or is unsupported."; return false; }
                    for (int other = 0; other < ActionCount; other++)
                        for (int otherSlot = 0; otherSlot < 2; otherSlot++)
                            if ((other != action || otherSlot != slot) && key == (otherSlot == 0 ? primary[other] : secondary[other]))
                            { failure = key + " is already assigned to " + Label((MostWantedFrontendAction)other) + "."; return false; }
                }
            }
            failure = string.Empty; return true;
        }

        public static bool IsBindable(Key key)
        {
            // The installed Input System's physical key range before F1 is contiguous. Avoid
            // Enum.IsDefined boxing in the per-frame driving reader; all tables are also validated on load/apply.
            if ((int)key <= 0 || (int)key >= (int)Key.F1) return false;
            switch (key)
            {
                // Keep unmigrated commands, OS shortcuts and menu escape routes reachable.
                case Key.Escape: case Key.Enter: case Key.NumpadEnter: case Key.Tab:
                case Key.LeftAlt: case Key.RightAlt: case Key.LeftMeta: case Key.RightMeta:
                case Key.B: case Key.X: case Key.E: case Key.Q: case Key.N: case Key.R: case Key.M:
                case Key.PrintScreen: case Key.Pause: case Key.ContextMenu:
                    return false;
                default: return true;
            }
        }

        public bool IsPressed(MostWantedFrontendAction action, Keyboard device)
            => Pressed(device, Get(action), false) || Pressed(device, Get(action, 1), false);
        public bool WasPressedThisFrame(MostWantedFrontendAction action, Keyboard device)
            => Pressed(device, Get(action), true) || Pressed(device, Get(action, 1), true);
        public string DisplayName(MostWantedFrontendAction action, int slot = 0)
        {
            Key key = Get(action, slot);
            if (key == Key.None) return "Not assigned";
            string label = Keyboard.current != null ? Keyboard.current[key].displayName : null;
            return string.IsNullOrWhiteSpace(label) ? key.ToString() : label;
        }

        public bool EquivalentTo(MostWantedFrontendKeyboardBindings other)
        {
            if (other == null || primary == null || secondary == null || other.primary == null || other.secondary == null
                || primary.Length != ActionCount || secondary.Length != ActionCount
                || other.primary.Length != ActionCount || other.secondary.Length != ActionCount) return false;
            for (int i = 0; i < ActionCount; i++) if (primary[i] != other.primary[i] || secondary[i] != other.secondary[i]) return false;
            return true;
        }

        public static string Label(MostWantedFrontendAction action)
        {
            switch (action)
            {
                case MostWantedFrontendAction.SteerLeft: return "Steer left";
                case MostWantedFrontendAction.SteerRight: return "Steer right";
                case MostWantedFrontendAction.Throttle: return "Accelerate";
                case MostWantedFrontendAction.Brake: return "Brake / reverse";
                case MostWantedFrontendAction.Handbrake: return "Handbrake";
                case MostWantedFrontendAction.Nitrous: return "Nitrous";
                case MostWantedFrontendAction.Camera: return "Change camera";
                default: return "Unknown control";
            }
        }

        private static bool Pressed(Keyboard device, Key key, bool edge) => device != null && IsBindable(key)
            && (edge ? device[key].wasPressedThisFrame : device[key].isPressed);
        private static void ValidateIndex(MostWantedFrontendAction action, int slot)
        {
            if ((int)action < 0 || (int)action >= ActionCount) throw new ArgumentOutOfRangeException(nameof(action));
            if (slot < 0 || slot > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}
