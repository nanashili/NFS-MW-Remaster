using UnityEngine;
using UnityEngine.UIElements;
using static NfsMwRemaster.Driving.MostWantedFrontendStyle;

namespace NfsMwRemaster.Driving
{
    public sealed partial class GameFlowScreen
    {
        private void RenderTitle()
        {
            var splash = MostWantedFrontendArt.Find("MostWantedUI/Images/Branding/Game/MostWantedSplashBackgroundWidescreen");
            if (splash != null)
            {
                var backdrop = Image(Root, splash, 0, 0, Width, Height, null, ScaleMode.ScaleAndCrop);
                backdrop.style.width = backdrop.style.height = Length.Percent(100);
                backdrop.SendToBack();
            }
            var logo = MostWantedFrontendArt.Find("MostWantedUI/Images/Branding/Game/MostWantedLogo");
            if (logo != null) Image(content, logo, 581, 58, 812, 190);
            Text(content, "CLICK to continue", 380, 781, 776, 70, 37, Color.white, TextAnchor.MiddleCenter);
            Text(content, "© 2005 Electronic Arts Inc. All Rights Reserved.", 283, 859, 970, 51, 27, Color.white, TextAnchor.MiddleCenter);
            var start = Place(new Button(ContinueTitle) { name = "title-continue", tooltip = "Continue" }, 0, 0, Width, Height);
            start.style.backgroundColor = Color.clear;
            start.style.borderTopWidth = start.style.borderBottomWidth = start.style.borderLeftWidth = start.style.borderRightWidth = 0;
            content.Add(start);
            primaryButtons.Add(start);
        }

        private void ContinueTitle()
        {
            navigation.Reset(MostWantedFrontendPage.AliasPrompt);
            navigation.Current.FocusName = "alias-no";
            Render();
        }

        private void RenderAliasPrompt()
        {
            Panel(content, 212, 287, 1115, 425);
            var warning = MostWantedFrontendArt.Find("MostWantedUI/Images/Shared/Icons/GenericAlert");
            if (warning != null) Image(content, warning, 227, 304, 83, 83);
            content.Add(Place(new MostWantedTitleText("save/load", 805, 88, 88), 317, 295, 805, 88));
            Text(content, "Create a new alias? You can also continue with your current alias and choose a career from the main menu.", 267, 417, 1003, 139, 33);
            var yes = Button(content, "Yes", () => OpenPage(MostWantedFrontendPage.AliasEntry), "alias-yes", 607, 611, 322, 70);
            var no = Button(content, "No", () => runtime.Flow.CompleteBoot(), "alias-no", 963, 611, 322, 70);
            yes.style.backgroundColor = no.style.backgroundColor = new Color(.05f,.05f,.05f,1);
        }

        private void RenderAliasEntry(bool career, bool create)
        {
            Header(career ? create ? "start career" : "load career" : "create alias");
            Panel(content, 308, 293, 920, 393);
            Text(content, "Alias", 353, 389, 185, 56, 32, Khaki);
            var field = Place(new TextField { value = alias, name = "alias", maxLength = 64 }, 539, 390, 638, 56);
            field.style.fontSize = 30;
            field.RegisterValueChangedCallback(change => alias = change.newValue);
            content.Add(field);
            Button(content, career ? create ? "Start Career" : "Load Career" : "Continue", () =>
            {
                if (career)
                {
                    if (create) Confirm("Create this career? Existing profiles are never overwritten.", () => runtime.Queue(runtime.EnterCareerAsync(alias, true)));
                    else runtime.Queue(runtime.EnterCareerAsync(alias, false));
                    return;
                }
                preferences.BeginEdit();
                if (preferences.Draft == null) { feedback = preferences.Status; Render(); return; }
                preferences.Draft.playerAlias = alias;
                if (!preferences.TryApply(out string failure)) { feedback = failure; Render(); return; }
                alias = preferences.Current.playerAlias;
                if(runtime.Flow.State==GameFlowState.Boot)runtime.Flow.CompleteBoot();
                else GoBack();
            }, career ? create ? "new-career" : "resume-career" : "alias-accept", 539, 518, 638, 69);
            Footer("Back", GoBack);
        }
    }
}
