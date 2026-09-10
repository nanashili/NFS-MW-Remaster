using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static NfsMwRemaster.Driving.MostWantedFrontendStyle;

namespace NfsMwRemaster.Driving
{
    public sealed partial class GameFlowScreen
    {
        private MostWantedFrontendPreferences preferences;
        private Label preferenceNotice, preferenceOverlayNotice;
        private int renderedPreferenceOverlay;
        private readonly List<VisualElement> preferenceRows = new List<VisualElement>();
        private readonly List<Action<int>> preferenceAdjustments = new List<Action<int>>();
        private int selectedPreferenceRow;
        private bool acceptPreferencesWhenConfirmed;
        private MostWantedFrontendAction selectedBindingAction = MostWantedFrontendAction.Throttle;
        private int selectedBindingSlot;

        // Capture consumes its terminating frame as well, so Escape cannot also navigate back.
        private bool PreferencesConsumeInput => preferences != null
            && (preferences.RebindingConsumesInput || preferences.IsRestoringDisplay);
        private bool CanEditPreferences => preferences != null && preferences.IsEditing
            && !preferences.RecoveryRequired && !preferences.HasPendingVideoChange
            && !preferences.IsRestoringDisplay && !preferences.IsRebinding;
        private bool CanInteractWithPreferences => CanEditPreferences && !preferences.RebindingConsumesInput;

        private void InitializePreferences()
        {
            preferences = runtime?.Preferences
                ?? MostWantedFrontendPreferences.Initialize(runtime != null ? runtime.Settings : null);
            alias = preferences.Current.playerAlias;
        }

        private void BeginPreferences(MostWantedFrontendPage page)
        {
            if (!MostWantedFrontendNavigation.IsSettings(page)) return;
            if (preferences == null) InitializePreferences();
            preferences.BeginEdit(); // Idempotent: Video and Advanced Video share the same draft.
        }

        private bool CancelRebinding()
        {
            if (preferences == null || !preferences.CancelRebinding()) return false;
            feedback = preferences.Status;
            Render();
            return true;
        }

        private void CancelPreferences()
        {
            preferences?.Cancel();
            acceptPreferencesWhenConfirmed = false;
            preferenceNotice = preferenceOverlayNotice = null;
        }

        private void DisposePreferences()
        {
            // GameFlowRuntime owns the shared service and any asynchronous display restoration.
            CancelPreferences();
            preferences = null;
            renderedPreferenceOverlay = 0;
        }

        private void RenderPreferences(MostWantedFrontendPage page)
        {
            preferenceNotice = null;
            preferenceRows.Clear(); preferenceAdjustments.Clear(); selectedPreferenceRow = 0;
            if (!MostWantedFrontendNavigation.IsSettings(page))
            {
                Header("options");
                Text(content, "This settings page is unavailable.", 250, 350, 1030, 100, 32, Khaki);
                Footer("Back", GoBack);
                return;
            }

            BeginPreferences(page);
            footer.style.left = 184;
            FngGrungeRect("MwGrit01", -130, 241, -555, 316, 0, Color.black);
            FngGrungeRect("Vignette02", 139, -199, -512, 138, 180, Color.black);
            FngGrungeRect("MwGrit01", -231, -285, 550, 550, -172, Color.black);
            Header(page == MostWantedFrontendPage.AdvancedVideo ? "advanced video" : page.ToString());
            var panel = Panel(content, 184, page == MostWantedFrontendPage.Controls ? 216 : 205, 1160, page == MostWantedFrontendPage.Controls ? 660 : 620);
            panel.CornerOutset = 16; panel.HeaderHeight = 80;
            if (preferences.Draft == null)
            {
                Text(content, preferences.Status, 245, 300, 1040, 190, 29, Khaki);
                Footer("Back", GoBack);
                return;
            }

            switch (page)
            {
                case MostWantedFrontendPage.Audio: RenderAudioPreferences(); break;
                case MostWantedFrontendPage.Video: RenderVideoPreferences(); break;
                case MostWantedFrontendPage.AdvancedVideo: RenderAdvancedVideoPreferences(); break;
                case MostWantedFrontendPage.Gameplay: RenderGameplayPreferences(); break;
                case MostWantedFrontendPage.Player: RenderPlayerPreferences(); break;
                case MostWantedFrontendPage.Controls: RenderControlPreferences(); break;
            }

            preferenceNotice = Text(content, PreferenceStatus(), 230, 771, 1080, 49, 21, Khaki);
            preferenceNotice.name = "preferences-status";
            preferenceNotice.style.display = DisplayStyle.None;
            Footer(page == MostWantedFrontendPage.Controls ? "Done" : "Back", page == MostWantedFrontendPage.Controls ? AcceptPreferences : GoBack);
            if (page != MostWantedFrontendPage.Controls)
            {
                Footer("Accept", AcceptPreferences, "Enter", Key.Enter, CanEditPreferences);
                footer.Q<Button>("command-accept").name = "command-apply";
            }
            else footer.Q<Button>("command-done").name = "command-apply";
            Footer("Defaults", () =>
            {
                if (!CanInteractWithPreferences) return;
                preferences.RestoreDefaults(PreferenceSection(page));
                feedback = null;
                Render();
            }, "1", Key.Digit1, CanEditPreferences);
            if (page == MostWantedFrontendPage.Controls)
                Footer("Clear", () =>
                {
                    if (!CanInteractWithPreferences) return;
                    feedback = preferences.TrySetBinding(selectedBindingAction, selectedBindingSlot, Key.None, out string failure) ? null : failure;
                    navigation.Current.FocusName = "pref-binding-" + selectedBindingAction + "-" + selectedBindingSlot;
                    Render();
                }, "2", Key.Digit2, CanEditPreferences);
            if (page == MostWantedFrontendPage.Video)
            {
                var advanced = Button(content, "2   Advanced", () => OpenPage(MostWantedFrontendPage.AdvancedVideo), "pref-advanced", 996, 211, 325, 68, false);
                advanced.SetEnabled(CanEditPreferences);
                shortcuts[Key.Digit2] = () => OpenPage(MostWantedFrontendPage.AdvancedVideo);
            }
            if (preferenceRows.Count > 0) primaryButtons.Add(preferenceRows[0]);
        }

        private void RenderAudioPreferences()
        {
            var sound = preferences.Draft.audio;
            PreferenceSlider("Sound Effects Volume", "pref-effects", 306, sound.effects, (s, value) => s.audio.effects = value);
            PreferenceSlider("Car Volume", "pref-vehicle", 356, sound.vehicle, (s, value) => s.audio.vehicle = value);
            PreferenceSlider("Speech Volume", "pref-police", 406, sound.police, (s, value) => s.audio.police = value);
            PreferenceSlider("Menu Music Volume", "pref-menu-music", 456, preferences.Draft.menuMusic, (s, value) => s.menuMusic = value);
            PreferenceSlider("Game Music Volume", "pref-music", 506, sound.music, (s, value) => s.audio.music = value);
            PreferenceFixed("Interactive Music", "pref-interactive-music", 556, "On", "Adaptive music follows the current driving context.");
            PreferenceBoolean("EA™ TRAX", "pref-ea-trax", 606, preferences.Draft.eaTraxEnabled,
                (s, value) => s.eaTraxEnabled = value);
            PreferenceChoice("EA™ TRAX Order", "pref-ea-trax-order", 656,
                new List<string> { "Sequential", "Random" }, (int)preferences.Draft.eaTraxOrder,
                (s, index) => s.eaTraxOrder = (MostWantedFrontendMusicOrder)index);
            PreferenceFixed("Audio Mode", "pref-audio-mode", 706, AudioSettings.speakerMode == AudioSpeakerMode.Mono ? "Mono" : "Stereo", "Output follows the system audio device.");
        }

        private void RenderVideoPreferences()
        {
            var draft = preferences.Draft;
            var sizes = new List<Vector2Int> { new Vector2Int(draft.width, draft.height) };
            foreach (var mode in Screen.resolutions)
            {
                var size = new Vector2Int(mode.width, mode.height);
                if (mode.width >= 320 && mode.height >= 200 && !sizes.Contains(size)) sizes.Add(size);
            }
            var resolutions = new List<string>();
            foreach (var size in sizes) resolutions.Add(size.x + " × " + size.y);
            var qualityNames = new List<string>(QualitySettings.names);
            bool validQuality = draft.qualityLevel >= 0 && draft.qualityLevel < qualityNames.Count;
            if (qualityNames.Count == 0) qualityNames.Add("Unavailable");
            PreferenceSlider("Level Of Detail", "pref-quality", 336,
                validQuality && qualityNames.Count > 1 ? (float)draft.qualityLevel / (qualityNames.Count - 1) : 1,
                (s, value) => s.qualityLevel = Mathf.RoundToInt(value * (qualityNames.Count - 1)),
                validQuality && preferences.CanChangeQuality, 1f / Mathf.Max(1, qualityNames.Count - 1));
            PreferenceChoice("Resolution", "pref-resolution", 399, resolutions, 0, (s, index) =>
            { s.width = sizes[index].x; s.height = sizes[index].y; }, preferences.CanChangeDisplay);
            Text(content, "Adjust your monitor brightness and contrast\nso that the logo on the left is visible and\nthe logo on the right is barely visible.", 308, 508, 930, 117, 30, Khaki, TextAnchor.MiddleCenter);
            Image(content, MostWantedFrontendArt.Find("MostWantedUI/Images/Menus/Options/BrightnessCalibrationVisible"), 483, 630, 193, 134);
            Image(content, MostWantedFrontendArt.Find("MostWantedUI/Images/Menus/Options/BrightnessCalibrationDark"), 838, 630, 193, 134);
        }

        private void RenderAdvancedVideoPreferences()
        {
            var draft = preferences.Draft;
            var modes = new List<FullScreenMode> { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow };
            if (Application.platform == RuntimePlatform.WindowsPlayer) modes.Add(FullScreenMode.ExclusiveFullScreen);
            if (Application.platform == RuntimePlatform.OSXPlayer) modes.Add(FullScreenMode.MaximizedWindow);
            if (!modes.Contains(draft.fullScreenMode)) modes.Add(draft.fullScreenMode);
            var modesText = new List<string>();
            foreach (var mode in modes) modesText.Add(DisplayModeLabel(mode));
            PreferenceChoice("Display Mode", "pref-display-mode", 326, modesText, modes.IndexOf(draft.fullScreenMode),
                (s, index) => s.fullScreenMode = modes[index], preferences.CanChangeDisplay);
            PreferenceChoice("Vertical Sync", "pref-vsync", 396,
                new List<string> { "Off", "Every refresh", "Every 2 refreshes", "Every 3 refreshes", "Every 4 refreshes" },
                draft.vSyncCount, (s, index) =>
                {
                    s.vSyncCount = index;
                    content.Q("pref-frame-limit")?.SetEnabled(index == 0 && CanEditPreferences);
                });
            var limits = new List<int> { -1, 30, 60, 90, 120, 144, 165, 240, 360 };
            if (!limits.Contains(draft.targetFrameRate)) { limits.Add(draft.targetFrameRate); limits.Sort(); }
            var labels = new List<string>();
            foreach (int limit in limits) labels.Add(limit == -1 ? "Unlimited" : limit + " FPS");
            PreferenceChoice("Frame Limit", "pref-frame-limit", 466, labels, limits.IndexOf(draft.targetFrameRate),
                (s, index) => s.targetFrameRate = limits[index], draft.vSyncCount == 0);
            PreferenceHelp("Display changes must be confirmed within 15 seconds.", 675);
        }

        private void RenderGameplayPreferences()
        {
            PreferenceFixed("Autosave", "pref-autosave", 326, "On", "Career progress is saved automatically.");
            PreferenceFixed("Game Moment Camera", "pref-moment-camera", 376, "Off");
            PreferenceFixed("Car Damage", "pref-car-damage", 426, "Off");
            PreferenceFixed("Rearview Mirror", "pref-rearview", 476, "Off");
            PreferenceFixed("Units", "pref-units", 526, "Metric", "Speed and distance use metric units.");
            PreferenceFixed("Free Roam Map Mode", "pref-roam-map", 576, "Rotate", "The minimap follows the vehicle heading.");
            PreferenceFixed("Race Map Mode", "pref-race-map", 626, "Rotate", "The minimap follows the vehicle heading.");
        }

        private void RenderPlayerPreferences()
        {
            PreferenceFixed("Transmission", "pref-transmission", 326, "Auto", "Transmission follows the current vehicle tuning.");
            PreferenceChoice("Camera", "pref-camera", 376, new List<string> { "Close", "Hood" },
                preferences.Draft.hoodCamera ? 1 : 0, (s, index) => s.hoodCamera = index == 1, preferences.CanChangeCameraView);
            PreferenceFixed("Gauges", "pref-gauges", 426, "On", "Driving information is shown on the HUD.");
            PreferenceFixed("Race Information", "pref-race-information", 476, "On", "Race progress is shown on the HUD.");
            PreferenceFixed("Split Time", "pref-split-time", 526, "Off");
            PreferenceFixed("Score", "pref-score", 576, "On", "Race results show the earned score.");
            PreferenceFixed("Leaderboard", "pref-leaderboard", 626, "Off");
        }

        private void RenderControlPreferences()
        {
            Text(content, "Primary", 630, 234, 320, 54, 34, Khaki, TextAnchor.MiddleCenter);
            Text(content, "Secondary", 960, 234, 323, 54, 34, Khaki, TextAnchor.MiddleCenter);
            var scroll = Place(new ScrollView(ScrollViewMode.Vertical) { name = "controls-list" }, 204, 310, 1120, 508);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            StyleScrollbar(scroll);
            content.Add(scroll);
            var actions = new MostWantedFrontendAction?[] { MostWantedFrontendAction.Throttle, MostWantedFrontendAction.Brake,
                MostWantedFrontendAction.SteerLeft, MostWantedFrontendAction.SteerRight, MostWantedFrontendAction.Handbrake,
                null, MostWantedFrontendAction.Nitrous, null, null, MostWantedFrontendAction.Camera };
            var labels = new[] { "Accelerate", "Brake/Reverse", "Steer Left", "Steer Right", "Handbrake", "Speedbreaker", "N2O", "Shift Down", "Shift Up", "Camera" };
            for (int i = 0; i < actions.Length; i++)
            {
                var row = new VisualElement(); row.style.height = 62; row.style.flexShrink = 0; scroll.Add(row);
                var rowLabel = Text(row, labels[i], 0, 0, 405, 50, 34, Khaki);
                for (int slot = 0; slot < 2; slot++)
                {
                    int bindingSlot = slot;
                    if (!actions[i].HasValue)
                    {
                        var cell = Box(row, "fixed-binding", 426 + slot * 330, 0, 320, 50);
                        cell.style.backgroundColor = new Color(.12f, .12f, .12f, 1);
                        Text(cell, slot == 0 && i == 7 ? "Q" : slot == 0 && i == 8 ? "E" : "---", 0, 0, 320, 50, 34, Khaki, TextAnchor.MiddleCenter);
                        cell.tooltip = i == 5 ? "Speedbreaker is unavailable in this build." : "Shift keys are fixed by the vehicle input system.";
                        continue;
                    }
                    var action = actions[i].Value;
                    var display = preferences.GetBindingDisplay(action, slot);
                    var button = Button(row, display == "Not assigned" ? "---" : display, () =>
                    {
                        if (!CanInteractWithPreferences) return;
                        if (!preferences.BeginRebind(action, bindingSlot, out string failure)) feedback = failure;
                        else { feedback = null; navigation.Current.FocusName = "pref-cancel-capture"; }
                        Render();
                    }, "pref-binding-" + action + "-" + slot, 426 + slot * 330, 0, 320, 50);
                    button.Q<MostWantedPanel>()?.RemoveFromHierarchy();
                    button.style.backgroundColor = new Color(.12f,.12f,.12f,1);
                    button.RegisterCallback<FocusInEvent>(_ =>
                    {
                        selectedBindingAction = action; selectedBindingSlot = bindingSlot;
                        rowLabel.style.color = Amber; button.style.backgroundColor = new Color(.22f,.09f,0,1); scroll.ScrollTo(row);
                    });
                    button.RegisterCallback<FocusOutEvent>(_ => { rowLabel.style.color = Khaki; button.style.backgroundColor = new Color(.12f,.12f,.12f,1); });
                    button.SetEnabled(CanEditPreferences && Keyboard.current != null);
                }
            }
            Text(content, "Click on a field or press ENTER to re-map this function", 219, 829, 1080, 45, 25, Khaki, TextAnchor.MiddleCenter);
        }

        private void PreferenceSlider(string label, string name, float y, float value,
            Action<MostWantedFrontendSettings, float> changed, bool available = true, float increment = .1f)
        {
            var row = PreferenceRow(label, name + "-row", y);
            var slider = Place(new Slider(0, 1) { name = name, value = value, tooltip=label }, 704, 0, 349, 50);
            // Keep the native slider's keyboard and pointer handling under the segmented artwork.
            slider.style.opacity = 0;
            var track = Box(row, name + "-track", 714, 19, 329, 12);
            track.style.backgroundColor = new Color(.12f, .12f, .12f, 1);
            track.pickingMode = PickingMode.Ignore;
            var segments = new VisualElement[10];
            var segmentTexture=MostWantedFrontendArt.Find("MostWantedUI/Images/Menus/Navigation/LEDMainSliders");
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = segmentTexture!=null
                    ? Image(row,segmentTexture,714+i*33,18,32,14,Amber,ScaleMode.StretchToFill)
                    : Box(row,name+"-segment-"+i,714+i*33,22,32,7);
                // The atlas contains two LEDs; stretch one LED into each of the ten original slots.
                if (segments[i] is Image segment) segment.sourceRect = new Rect(0, 12, 16, 7);
                segments[i].pickingMode = PickingMode.Ignore;
            }
            void UpdateSegments(float volume)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    segments[i].style.opacity = i < Mathf.RoundToInt(volume * segments.Length) ? 1 : .2f;
                    if(segmentTexture==null)segments[i].style.backgroundColor=Amber;
                }
            }
            UpdateSegments(value);
            var amount = Text(row, Mathf.RoundToInt(value * 100) + "%", 1023, 0, 91, 50, 24, Color.white, TextAnchor.MiddleRight);
            amount.style.display=DisplayStyle.None;
            slider.SetEnabled(available && CanEditPreferences);
            slider.RegisterValueChangedCallback(change =>
            {
                if (!available || !CanInteractWithPreferences) return;
                changed(preferences.Draft, change.newValue);
                amount.text = Mathf.RoundToInt(change.newValue * 100) + "%";
                UpdateSegments(change.newValue);
                UpdatePreferenceControls();
            });
            row.Add(slider);
            AddPreferenceArrows(row, name, step => slider.value = Mathf.Clamp01(slider.value + step * increment), available);
        }

        private void PreferenceBoolean(string label, string name, float y, bool value,
            Action<MostWantedFrontendSettings, bool> changed)
            => PreferenceChoice(label, name, y, new List<string> { "Off", "On" }, value ? 1 : 0,
                (settings, index) => changed(settings, index == 1));

        private void PreferenceChoice(string label, string name, float y, List<string> choices, int selected,
            Action<MostWantedFrontendSettings, int> changed, bool available = true)
        {
            var row = PreferenceRow(label, name, y);
            int index = Mathf.Clamp(selected, 0, choices.Count - 1);
            int size = choices.Exists(choice => choice.Length > 18) ? 25 : 34;
            var value = Text(row, choices[index], 711, 0, 337, 50, size, Color.white, TextAnchor.MiddleCenter);
            value.name = name + "-value"; value.style.whiteSpace = WhiteSpace.NoWrap;
            AddPreferenceArrows(row, name, step =>
            {
                index = (index + step + choices.Count) % choices.Count;
                value.text = choices[index]; changed(preferences.Draft, index); UpdatePreferenceControls();
            }, available);
            if (!available) row.tooltip = "This option is unavailable in the current session.";
        }

        private void PreferenceFixed(string label, string name, float y, string value, string reason = "This feature is not available in this build.")
        {
            var row = PreferenceRow(label, name, y);
            row.tooltip = reason;
            Text(row, value, 711, 0, 337, 50, 34, Color.white, TextAnchor.MiddleCenter);
            preferenceAdjustments.Add(null);
        }

        private VisualElement PreferenceRow(string label, string name, float y)
        {
            var row = Place(new VisualElement { name = name, focusable = true, tabIndex = 0 }, 185, y, 1156, 50);
            content.Add(row);
            var corners = Panel(row, 0, -14, 1156, 78, false, false, Color.clear);
            corners.CornerColor = Amber;
            corners.style.display = DisplayStyle.None;
            var caption = Text(row, label, 37, 0, 581, 50, 34, Khaki, TextAnchor.MiddleRight);
            caption.name = "preference-label"; caption.style.whiteSpace = WhiteSpace.NoWrap;
            int index = preferenceRows.Count;
            preferenceRows.Add(row);
            void Selected(bool selected)
            {
                corners.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
                caption.style.color = selected ? Amber : Khaki;
                foreach (var arrow in row.Query<Button>(className: "preference-arrow").ToList())
                    arrow.style.visibility = selected ? Visibility.Visible : Visibility.Hidden;
                if (selected) { selectedPreferenceRow = index; navigation.Current.FocusName = name; }
            }
            row.RegisterCallback<FocusInEvent>(_ => Selected(true));
            row.RegisterCallback<FocusOutEvent>(_ => Selected(false));
            row.RegisterCallback<PointerEnterEvent>(_ => { if (CanInteractWithPreferences) row.Focus(); });
            row.RegisterCallback<NavigationSubmitEvent>(evt => { AcceptPreferences(); evt.StopImmediatePropagation(); });
            return row;
        }

        private void AddPreferenceArrows(VisualElement row, string name, Action<int> adjust, bool available)
        {
            void Change(int direction) { if (available && row.enabledInHierarchy && CanInteractWithPreferences) adjust(direction); }
            preferenceAdjustments.Add(available ? Change : null);
            if (!available) return;
            var texture = MostWantedFrontendArt.Find("MostWantedUI/Images/Menus/Navigation/ArrowSkinnier");
            for (int direction = -1; direction <= 1; direction += 2)
            {
                int step = direction;
                var arrow = Button(row, string.Empty, () => Change(step), name + (step < 0 ? "-decrease" : "-increase"), step < 0 ? 643 : 1075, 1, 48, 48, false);
                arrow.AddToClassList("preference-arrow"); arrow.style.visibility = Visibility.Hidden;
                arrow.Q<MostWantedPanel>()?.RemoveFromHierarchy();
                arrow.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopPropagation());
                if (texture != null)
                {
                    var art = Image(arrow, texture, 0, 0, 48, 48, Amber);
                    if (step > 0) art.style.scale = new Scale(new Vector3(-1, 1, 1));
                }
            }
        }

        private bool TryNavigatePreferenceRows(NavigationMoveEvent evt)
        {
            if (!MostWantedFrontendNavigation.IsSettings(navigation.Current.Page) || preferenceRows.Count == 0 || !CanInteractWithPreferences) return false;
            if (evt.direction == NavigationMoveEvent.Direction.Up || evt.direction == NavigationMoveEvent.Direction.Down)
            {
                selectedPreferenceRow = Mathf.Clamp(selectedPreferenceRow + (evt.direction == NavigationMoveEvent.Direction.Up ? -1 : 1), 0, preferenceRows.Count - 1);
                preferenceRows[selectedPreferenceRow].Focus();
            }
            else if (evt.direction == NavigationMoveEvent.Direction.Left || evt.direction == NavigationMoveEvent.Direction.Right)
                preferenceAdjustments[selectedPreferenceRow]?.Invoke(evt.direction == NavigationMoveEvent.Direction.Left ? -1 : 1);
            else return false;
            evt.StopImmediatePropagation(); return true;
        }

        private void PreferenceHelp(string message, float y) => Text(content, message, 237, y, 1066, 87, 23, Khaki);

        private void AcceptPreferences()
        {
            if (!CanInteractWithPreferences) return;
            if (!preferences.IsDirty) { GoBack(); return; }
            acceptPreferencesWhenConfirmed = true;
            ApplyPreferences();
        }

        private void ApplyPreferences()
        {
            if (!CanInteractWithPreferences) return;
            if (!preferences.TryApply(out string failure)) feedback = failure;
            else if (preferences.HasPendingVideoChange)
            { feedback = null; navigation.Current.FocusName = "pref-confirm-video"; }
            else
            {
                alias = preferences.Current.playerAlias;
                if (acceptPreferencesWhenConfirmed)
                {
                    acceptPreferencesWhenConfirmed = false;
                    GoBack(); return;
                }
                feedback = null;
                BeginPreferences(navigation.Current.Page);
            }
            Render();
        }

        private void KeepPreferenceVideo()
        {
            if (preferences == null) return;
            if (!preferences.ConfirmVideo(out string failure)) feedback = failure;
            else
            {
                alias = preferences.Current.playerAlias; feedback = null;
                if (acceptPreferencesWhenConfirmed)
                { acceptPreferencesWhenConfirmed = false; GoBack(); return; }
            }
            if (!preferences.HasPendingVideoChange) BeginPreferences(navigation.Current.Page);
            Render();
        }

        private void RevertPreferenceVideo()
        {
            if (preferences == null) return;
            CancelPreferences(); feedback = preferences.Status;
            BeginPreferences(navigation.Current.Page);
            Render();
        }

        private int PreferenceOverlayState => preferences == null ? 0
            : preferences.IsRebinding ? 1 : preferences.HasPendingVideoChange ? 2
            : preferences.IsRestoringDisplay ? 3
            : preferences.RecoveryRequired && MostWantedFrontendNavigation.IsSettings(navigation.Current.Page) ? 4 : 0;

        private void RenderPreferenceOverlay()
        {
            preferenceOverlayNotice = null;
            renderedPreferenceOverlay = PreferenceOverlayState;
            if (renderedPreferenceOverlay == 0 || surface == null) return;
            content?.SetEnabled(false); footer?.SetEnabled(false); primaryButtons.Clear();
            var dim = Box(surface, "preferences-modal-scrim", 0, 0, Width, Height);
            dim.style.backgroundColor = new Color(0, 0, 0, .75f);
            var modal = Box(dim, "preferences-modal", 330, 277, 876, 420);
            Panel(modal, 0, 0, 876, 420);
            string title = renderedPreferenceOverlay == 1 ? "Change keyboard binding"
                : renderedPreferenceOverlay == 2 ? "Keep these display settings?"
                : renderedPreferenceOverlay == 3 ? "Restoring display settings" : "Settings recovery required";
            Text(modal, title, 37, 27, 802, 69, 33, Amber, TextAnchor.MiddleCenter);
            preferenceOverlayNotice = Text(modal, PreferenceOverlayText(), 43, 112, 790, 139, 25, Khaki, TextAnchor.MiddleCenter);
            preferenceOverlayNotice.name = "preferences-modal-status";
            modal.RegisterCallback<NavigationSubmitEvent>(evt =>
            { if (preferences?.RebindingConsumesInput == true) evt.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            if (renderedPreferenceOverlay == 1)
            {
                navigation.Current.FocusName = "pref-cancel-capture";
                Button(modal, "Cancel capture", () => CancelRebinding(), "pref-cancel-capture", 48, 300, 780, 61);
            }
            else if (renderedPreferenceOverlay == 2)
            {
                Button(modal, "Keep changes", KeepPreferenceVideo, "pref-confirm-video", 48, 274, 780, 58);
                Button(modal, "Revert", RevertPreferenceVideo, "pref-revert-video", 48, 338, 780, 58);
            }
            else if (renderedPreferenceOverlay == 4)
                Button(modal, "Back", GoBack, "pref-recovery-back", 48, 300, 780, 61);
        }

        private string PreferenceOverlayText() => preferences == null ? string.Empty
            : preferences.HasPendingVideoChange
                ? preferences.Status + "\nReverting in " + Mathf.CeilToInt(preferences.VideoConfirmationSecondsRemaining) + " seconds."
                : preferences.Status;

        private string PreferenceStatus() => preferences == null ? string.Empty
            : preferences.IsDirty ? "Unsaved changes. Apply to save, or Back to discard."
            : !string.IsNullOrEmpty(preferences.Status) ? preferences.Status : "No unsaved changes.";

        private void UpdatePreferenceControls()
        {
            if (preferenceNotice != null) preferenceNotice.text = PreferenceStatus();
            var apply = footer?.Q<Button>("command-apply");
            if (apply != null)
            {
                bool enabled = CanEditPreferences;
                apply.SetEnabled(enabled);
                apply.style.opacity = enabled ? 1 : .35f;
            }
        }

        private void TickPreferences()
        {
            // No service Tick here: the application owns deadlines even when this screen is hidden.
            if (preferences == null) return;
            if (preferenceOverlayNotice != null) preferenceOverlayNotice.text = PreferenceOverlayText();
            int state = PreferenceOverlayState;
            if (state != renderedPreferenceOverlay)
            {
                renderedPreferenceOverlay = state;
                feedback = preferences.Status;
                if (state == 0) BeginPreferences(navigation.Current.Page);
                Render();
                return;
            }
            if (MostWantedFrontendNavigation.IsSettings(navigation.Current.Page)) UpdatePreferenceControls();
        }

        private static MostWantedFrontendPreferenceSection PreferenceSection(MostWantedFrontendPage page)
        {
            switch (page)
            {
                case MostWantedFrontendPage.Audio: return MostWantedFrontendPreferenceSection.Audio;
                case MostWantedFrontendPage.Video:
                case MostWantedFrontendPage.AdvancedVideo: return MostWantedFrontendPreferenceSection.Video;
                case MostWantedFrontendPage.Gameplay: return MostWantedFrontendPreferenceSection.Gameplay;
                case MostWantedFrontendPage.Player: return MostWantedFrontendPreferenceSection.Player;
                default: return MostWantedFrontendPreferenceSection.Controls;
            }
        }

        private static string DisplayModeLabel(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.Windowed: return "Windowed";
                case FullScreenMode.FullScreenWindow: return "Borderless fullscreen";
                case FullScreenMode.ExclusiveFullScreen: return "Exclusive fullscreen";
                case FullScreenMode.MaximizedWindow: return "Maximized window";
                default: return mode.ToString();
            }
        }
    }
}
