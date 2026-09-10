using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static NfsMwRemaster.Driving.MostWantedFrontendStyle;

namespace NfsMwRemaster.Driving
{
    public sealed partial class GameFlowScreen
    {
        private readonly struct MenuItem
        {
            public readonly string Label, Icon;
            public readonly Action Action;
            public MenuItem(string label,string icon,Action action){Label=label;Icon=icon;Action=action;}
        }
        private MostWantedFrontendContent frontendContent;
        private MostWantedShowroom showroom;

        private void RenderPage(MostWantedFrontendPage page)
        {
            switch(page)
            {
                case MostWantedFrontendPage.Title: RenderTitle();break;
                case MostWantedFrontendPage.AliasPrompt: RenderAliasPrompt();break;
                case MostWantedFrontendPage.AliasEntry: RenderAliasEntry(false, true);break;
                case MostWantedFrontendPage.CareerNew: RenderAliasEntry(true, true);break;
                case MostWantedFrontendPage.CareerLoad: RenderAliasEntry(true, false);break;
                case MostWantedFrontendPage.AliasManager: RenderAliasEntry(false, true);break;
                case MostWantedFrontendPage.QuickRace: RenderRaceMode("quick race");break;
                case MostWantedFrontendPage.ChallengeSeries: RenderRaceMode("challenge series");break;
                case MostWantedFrontendPage.GameStats: RenderGameStats();break;
                case MostWantedFrontendPage.MainMenu: RenderMainMenu();break;
                case MostWantedFrontendPage.Career: RenderCareer();break;
                case MostWantedFrontendPage.Safehouse: RenderSafehouse();break;
                case MostWantedFrontendPage.Options: RenderOptions();break;
                case MostWantedFrontendPage.Pause: RenderPause();break;
                case MostWantedFrontendPage.Results: RenderResults();break;
                case MostWantedFrontendPage.Loading: RenderLoading();break;
                case MostWantedFrontendPage.Faulted: RenderFaulted();break;
                case MostWantedFrontendPage.Lan: RenderLan();break;
                case MostWantedFrontendPage.Online: RenderOnline();break;
                case MostWantedFrontendPage.Credits: RenderCredits();break;
                case MostWantedFrontendPage.Music: RenderMusic();break;
                case MostWantedFrontendPage.Blacklist: RenderBlacklist();break;
                case MostWantedFrontendPage.RivalBio: RenderRivalBio();break;
                case MostWantedFrontendPage.RaceEvents: RenderActivities(false,false);break;
                case MostWantedFrontendPage.Milestones: RenderActivities(true,false);break;
                case MostWantedFrontendPage.Bounty: RenderActivities(false,true);break;
                case MostWantedFrontendPage.Garage:
                case MostWantedFrontendPage.RivalCar: RenderGarage(page==MostWantedFrontendPage.RivalCar);break;
                case MostWantedFrontendPage.Customize: RenderCustomize();break;
                case MostWantedFrontendPage.Paint: RenderPaint();break;
                case MostWantedFrontendPage.Cart: RenderCart();break;
                case MostWantedFrontendPage.Showcase: Footer("Back",GoBack);Text(content,"Drag to orbit · Scroll to zoom",950,920,440,38,22,Khaki);break;
                default: RenderPreferences(page);break;
            }
        }

        private void BuildBackdrop()
        {
            if(frontendContent==null)frontendContent=runtime.Settings.FrontendContent
                ??Resources.Load<MostWantedFrontendContent>("MostWantedUI/Content");
            if(showroom==null)
            {
                showroom=gameObject.GetComponent<MostWantedShowroom>();
                if(showroom==null)showroom=gameObject.AddComponent<MostWantedShowroom>();
                showroom.Configure(runtime.Settings.Showroom);
            }
            bool boot = navigation.Current.Page == MostWantedFrontendPage.Title || navigation.Current.Page == MostWantedFrontendPage.AliasPrompt
                || navigation.Current.Page == MostWantedFrontendPage.AliasEntry;
            surface.style.backgroundColor = Color.clear;
            showroom.Show(!boot,navigation.Current.Page);
            if (boot) return;
            if(showroom.Target!=null)
            {
                var backdrop=Image(Root,showroom.Target,0,0,Width,Height,null,ScaleMode.ScaleAndCrop);
                backdrop.name="showroom-backdrop";
                backdrop.style.width=backdrop.style.height=Length.Percent(100);
                backdrop.SendToBack();
            }
            var vignette=Box(Root,"frontend-shade",0,0,Width,Height);
            vignette.style.width=vignette.style.height=Length.Percent(100);
            vignette.PlaceBehind(surface);
            vignette.pickingMode=PickingMode.Ignore;vignette.style.backgroundColor=new Color(0,0,0,.04f);
            bool carousel = navigation.Current.Page == MostWantedFrontendPage.MainMenu || navigation.Current.Page == MostWantedFrontendPage.Career
                || navigation.Current.Page == MostWantedFrontendPage.Options || navigation.Current.Page == MostWantedFrontendPage.Safehouse;
            if (!carousel)
            {
                AddGrunge(surface,-80,-75,1670,240,"header");
                AddGrunge(surface,-120,871,1740,230,"footer");
            }
        }
        private void AddGrunge(VisualElement parent,float x,float y,float w,float h,string kind)
        {
            var texture=MostWantedFrontendArt.Find("MostWantedUI/Images/Shared/Grunge/" + (kind == "carousel" ? "Splatter01" : "GritLong"));
            if(texture!=null)Image(parent,texture,x,y,w,h,Color.black,ScaleMode.StretchToFill);
        }
        private Texture2D FindIcon(string kind)
        {
            string path = kind switch
            {
                "career" => "Menus/Main/Career", "new-career" => "Menus/Main/NewCareer", "load-career" => "Menus/Main/ProfileManagement",
                "quick-race" => "Menus/RaceModes/ChallengeSeries", "challenge" => "Menus/Main/QuickRace", "alias" => "Menus/Main/ProfileManagement",
                "car" => "Menus/Main/CarSelect", "options" => "Menus/Main/Options", "lan" => "Menus/Main/Lan",
                "online" => "Menus/Main/GoOnline", "audio" => "Menus/Options/Audio", "video" => "Menus/Options/Display",
                "gameplay" => "Menus/Options/Gameplay", "player" => "Menus/Options/Player", "controls" => "Menus/Options/Controls",
                "music" => "Menus/Options/EATrax", "credits" => "Menus/Options/Credits", "blacklist" => "Menus/Main/Top15",
                "left" => "Menus/Navigation/ArrowLeft", "right" => "Menus/Navigation/ArrowMain",
                _ => null
            };
            return path == null ? MostWantedFrontendArt.Find("ICON_"+kind.ToUpperInvariant()) : MostWantedFrontendArt.Find("MostWantedUI/Images/"+path);
        }

        private void Carousel(string title,MenuItem[] items)
        {
            bool main=navigation.Current.Page==MostWantedFrontendPage.MainMenu;
            // Original MainMenu.fng / MainMenu_Sub.fng: centered coordinates on a 480-unit canvas.
            FngGrunge("MwGrit01",main?174:196,main?226:211,665,0,Color.black);
            FngGrunge("Graffiti04",main?115:304,main?132:45,256,main?51:54,Color.white);
            FngGrunge("RogerTag3",main?277:-85,main?87:105,main?112:128,main?20:-28,Color.white);
            if(main)FngGrunge("MwGrit01",74,-268,512,-169,new Color(0,0,0,232f/255));
            content.Add(Place(new MostWantedTitleText(title,615,70,83,true),765,678,615,70));
            int selected=navigation.Select(navigation.Current.Selection,items.Length,false);
            int previous=carouselPage==navigation.Current.Page?carouselSelection:selected;
            carouselPage=navigation.Current.Page;carouselSelection=selected;
            var selection=Panel(content,967,747,144,144,false,false,Color.clear);selection.CornerColor=Amber;
            var central=Button(content,string.Empty,items[selected].Action,"carousel-selected",973,752,132,132);
            central.Q<MostWantedPanel>()?.RemoveFromHierarchy();
            Icon(central,items[selected].Icon,-5,-6,142);
            AnimateCarouselIcon(central,selected-previous,0,142,1);
            for(int relative=-2;relative<=2;relative++)
            {
                if(relative==0)continue;
                int index=selected+relative;
                float size=Mathf.Abs(relative)==1?96:62;
                float x=1039+CarouselOffset(relative)-size*.5f;
                if (index < 0 || index >= items.Length)
                {
                    float dotSize=Mathf.Abs(relative)==1?42:21;
                    var dot = Box(content,"carousel-end-"+relative,x+(size-dotSize)*.5f,819-dotSize*.5f,dotSize,dotSize);
                    dot.style.backgroundColor = new Color(1,1,1,Mathf.Abs(relative)==1?.3f:.15f);
                    dot.style.borderTopLeftRadius=dot.style.borderTopRightRadius=dot.style.borderBottomLeftRadius=dot.style.borderBottomRightRadius=30;
                    continue;
                }
                var button=Button(content,string.Empty,()=>{navigation.Select(index,items.Length,false);Render();},"carousel-neighbor-"+relative,x,819-size*.5f,size,size,false);
                Icon(button,items[index].Icon,0,0,size);
                float opacity=Mathf.Abs(relative)==1?.62f:.28f;
                button.style.opacity=opacity;
                AnimateCarouselIcon(button,index-previous,relative,size,opacity);
            }
            var left=Button(content,string.Empty,()=>Shift(-1),"carousel-left",732,782,78,76,false);CarouselArrow(left,false);left.style.display=selected==0?DisplayStyle.None:DisplayStyle.Flex;
            var right=Button(content,string.Empty,()=>Shift(1),"carousel-right",1303,782,78,76,false);CarouselArrow(right,true);right.style.display=selected==items.Length-1?DisplayStyle.None:DisplayStyle.Flex;
            Text(content,items[selected].Label,796,894,581,68,40,Color.white,TextAnchor.MiddleCenter);
            void Shift(int delta){navigation.Select(navigation.Current.Selection+delta,items.Length,false);navigation.Current.FocusName="carousel-selected";Render();}
            horizontalNavigation=Shift;
        }
        private void CarouselArrow(VisualElement parent,bool right)
        {
            var texture=MostWantedFrontendArt.Find("MostWantedUI/Images/Menus/Navigation/ArrowSkinnier");
            if(texture==null)return;
            for(int layer=0;layer<2;layer++)
            {
                var arrow=Image(parent,texture,layer*25,8,56,56,layer==(right?1:0)?Amber:Khaki);
                if(right)arrow.style.scale=new Scale(new Vector3(-1,1,1));
            }
        }
        private void FngGrunge(string textureName,float centerX,float centerY,float size,float degrees,Color tint)
            => FngGrungeRect(textureName, centerX, centerY, size, size, degrees, tint);

        private void FngGrungeRect(string textureName,float centerX,float centerY,float width,float height,float degrees,Color tint)
        {
            const float scale=Height/480f;
            var texture=MostWantedFrontendArt.Find("MostWantedUI/Images/Shared/Grunge/"+textureName);
            if(texture==null)return;
            var element=Image(content,texture,Width*.5f+(centerX-Mathf.Abs(width)*.5f)*scale,
                Height*.5f+(centerY-Mathf.Abs(height)*.5f)*scale,Mathf.Abs(width)*scale,Mathf.Abs(height)*scale,tint,ScaleMode.StretchToFill);
            element.style.scale = new Scale(new Vector3(Mathf.Sign(width), Mathf.Sign(height), 1));
            element.style.rotate=new Rotate(degrees);
        }
        private void RenderMainMenu()
        {
            Carousel("main menu",new[]{
                new MenuItem("Career","career",()=>OpenPage(MostWantedFrontendPage.Career)),
                new MenuItem("Quick Race","quick-race",()=>OpenPage(MostWantedFrontendPage.QuickRace)),
                new MenuItem("Challenge Series","challenge",()=>OpenPage(MostWantedFrontendPage.ChallengeSeries)),
                new MenuItem("My Cars","car",()=>OpenPage(MostWantedFrontendPage.Garage)),
                new MenuItem("Alias Manager","alias",()=>OpenPage(MostWantedFrontendPage.AliasManager)),
                new MenuItem("LAN Play","lan",()=>OpenPage(MostWantedFrontendPage.Lan)),
                new MenuItem("Options","options",()=>OpenPage(MostWantedFrontendPage.Options)),
                new MenuItem("Online","online",()=>OpenPage(MostWantedFrontendPage.Online))});
            var logo=MostWantedFrontendArt.Find("MostWantedUI/Images/Branding/Game/MostWantedLogo");
            if(logo!=null)Image(content,logo,715,48,635,149,null,ScaleMode.StretchToFill);
            Footer("Quit",()=>Confirm("Exit the game?",Application.Quit),"Q",Key.Q);
            Footer("Game Stats",()=>OpenPage(MostWantedFrontendPage.GameStats),"1",Key.Digit1);
        }
        private void RenderRaceMode(string title)
        {
            Header(title);Panel(content,182,185,1170,647);
            Text(content,"No events available",257,355,1020,80,40,Khaki,TextAnchor.MiddleCenter);
            Text(content,"This race mode is not available yet.",277,455,980,120,31,Color.white,TextAnchor.MiddleCenter);
            Footer("Back",GoBack);
        }
        private void RenderGameStats()
        {
            Header("game stats");Panel(content,182,185,1170,647);
            if(careerSnapshot==null)Text(content,"Load a career to view your statistics.",257,380,1020,120,36,Color.white,TextAnchor.MiddleCenter);
            else
            {
                Text(content,"Race Wins",267,345,680,70,34,Khaki);
                Text(content,(careerSnapshot.statistics.racesWon).ToString(),947,345,290,70,36,Color.white,TextAnchor.MiddleRight);
                Text(content,"Total Bounty",267,425,680,70,34,Khaki);
                Text(content,Bounty.ToString("N0"),947,425,290,70,36,Color.white,TextAnchor.MiddleRight);
            }
            Footer("Back",GoBack);
        }
        private void RenderCareer()
        {
            Carousel("career main menu", new[] {
                new MenuItem("Start Career", "new-career", () => OpenPage(MostWantedFrontendPage.CareerNew)),
                new MenuItem("Load Career", "load-career", () => OpenPage(MostWantedFrontendPage.CareerLoad)) });
            Footer("Back",GoBack);
        }
        private void RenderSafehouse()
        {
            var stats=Panel(content,156,658,510,232,false,true,new Color(0,0,0,.57f));
            Text(stats,"Career Completion:",21,60,326,46,30,Khaki);
            Text(stats,CompletionLabel(),351,60,137,46,34,Color.white,TextAnchor.MiddleRight);
            Text(stats,"Bounty:",21,116,213,46,30,Khaki);Text(stats,$"{Bounty:N0}",224,116,264,46,34,Color.white,TextAnchor.MiddleRight);
            Text(stats,"Cash:",21,171,213,46,30,Khaki);Text(stats,$"{runtime.WorldSession?.Cash??0:N0}",224,171,264,46,34,Color.white,TextAnchor.MiddleRight);
            Carousel("safe house",new[]{new MenuItem("Resume Free Roam","car",()=>Execute(GameFlowCommand.Continue)),
                new MenuItem("Blacklist","blacklist",()=>OpenPage(MostWantedFrontendPage.Blacklist)),
                new MenuItem("My Cars","car",()=>OpenPage(MostWantedFrontendPage.Garage)),
                new MenuItem("Options","options",()=>OpenPage(MostWantedFrontendPage.Options))});
            Footer("Back",GoBack);
        }
        private void RenderOptions()
        {
            Carousel("options",new[]{
                new MenuItem("Audio","audio",()=>OpenPage(MostWantedFrontendPage.Audio)),
                new MenuItem("Video","video",()=>OpenPage(MostWantedFrontendPage.Video)),
                new MenuItem("Gameplay","gameplay",()=>OpenPage(MostWantedFrontendPage.Gameplay)),
                new MenuItem("Player","player",()=>OpenPage(MostWantedFrontendPage.Player)),
                new MenuItem("Controls","controls",()=>OpenPage(MostWantedFrontendPage.Controls)),
                new MenuItem("EA™ TRAX","music",()=>OpenPage(MostWantedFrontendPage.Music)),
                new MenuItem("Credits","credits",()=>OpenPage(MostWantedFrontendPage.Credits))});
            Footer("Back",GoBack);
        }
        private void RenderPause()
        {
            Header("pause");Panel(content,386,212,766,597);
            Button(content,"Resume",()=>Execute(GameFlowCommand.Resume),"resume",430,296,678,61);
            Button(content,"Blacklist",()=>OpenPage(MostWantedFrontendPage.Blacklist),"blacklist",430,361,678,61);
            Button(content,"Options",()=>OpenPage(MostWantedFrontendPage.Options),"options",430,426,678,61);
            Button(content,"Save Career",()=>Execute(GameFlowCommand.Save),"save",430,491,678,61);
            if(runtime.WorldSession?.CanRestartRace==true)Button(content,"Restart Race",()=>Confirm("Discard this attempt and restart from the grid?",()=>Execute(GameFlowCommand.Restart)),"restart",430,556,678,61);
            Button(content,"Main Menu",()=>Confirm("Save career and leave this world?",()=>runtime.Queue(runtime.Flow.ReturnToMenuAsync())),"main-menu",430,636,678,61);
            Footer("Back",GoBack);
        }
        private void RenderLoading()
        {
            Header(runtime.Flow.State==GameFlowState.EventLoading?"race events":"loading");
            Panel(content,290,381,960,246);
            Text(content,runtime.WorldSession?.ActiveEvent?.DisplayName??"NEED FOR SPEED MOST WANTED",331,442,878,61,35,Khaki,TextAnchor.MiddleCenter);
            progress=Text(content,"Preparing game systems…",331,502,878,66,29,Color.white,TextAnchor.MiddleCenter);
            if(runtime.CanCancelLoad)
            {
                // Loading cancellation is intentionally allowed while the application command gate is busy.
                var button=new Button(runtime.CancelLoading){text="Esc  Cancel loading",name="cancel-load"};
                Place(button,0,0,350,54);footer.Add(button);primaryButtons.Add(button);
            }
        }
        private void RenderResults()
        {
            var result=runtime.WorldSession?.LastRaceResult;Header(result?.Won==true?"race complete":"race results",true);
            Panel(content,300,236,936,555);
            Text(content,result?.EventName??"Event",345,327,850,66,40,Khaki,TextAnchor.MiddleCenter);
            Text(content,result==null?"No race receipt available.":$"Time: {result.ElapsedSeconds:0.00}s\nCash awarded: {result.CashAwarded:N0}",345,416,850,119,36,Color.white,TextAnchor.MiddleCenter);
            Button(content,"Return to Free Roam",()=>Execute(GameFlowCommand.Continue),"continue",382,573,770,62);
            if(runtime.WorldSession?.CanRestartRace==true)Button(content,"Race Again",()=>Execute(GameFlowCommand.Restart),"retry",382,637,770,62);
            Footer("Back",GoBack);
        }
        private void RenderFaulted()
        {
            Header("recovery");Panel(content,290,330,956,356);
            Text(content,"WORLD RECOVERY FAILED",330,412,870,60,40,Amber,TextAnchor.MiddleCenter);
            Text(content,"Restart the application before loading another career. Your save has not been deleted.",345,483,840,109,29,Color.white,TextAnchor.MiddleCenter);
            Footer("Quit",()=>Confirm("Exit the game?",Application.Quit));
        }
        private void RenderLan()
        {
            var panel = Panel(content, 214, 44, 1118, 827, true, false); panel.Corners = false;
            var heading = Panel(panel, 0, 0, 1118, 65, true, false); heading.Corners = false; heading.StripeOpacity = .96f;
            heading.Add(Place(new MostWantedTitleText("lan", 260, 65, 88), 0, -4, 260, 65));
            var server = Panel(panel, 0, 73, 1118, 68, false, false); server.Corners = false;
            Text(server,"LAN Server Select",0,0,1118,68,36,Color.white,TextAnchor.MiddleCenter);
            var line = Box(panel, "server-heading-rule", 0, 142, 1118, 7); line.style.backgroundColor = new Color(.06f,.06f,.06f,1);
            Text(panel,"Name",16,151,702,42,30,Khaki);Text(panel,"Racers",876,151,207,42,30,Khaki);
            var bottom = Panel(panel, 0, 797, 1118, 30, true, false); bottom.Corners = false; bottom.StripeOpacity = .96f;
            Footer("Back",GoBack);Footer("Create Server",()=>{},"1",Key.None,false);Footer("Join",()=>{},"Enter",Key.None,false);
            footer.Q<Button>("command-create-server").tooltip = "LAN networking is unavailable in this build.";
            footer.Q<Button>("command-join").tooltip = "LAN networking is unavailable in this build.";
        }
        private void RenderOnline()
        {
            Header("ea login");
            AddGrunge(content, -420, 197, 1310, 892, "header");
            var panel = Panel(content, 400, 314, 744, 288); panel.CornerOutset = 15; panel.HeaderHeight = 57;
            void Unavailable() { feedback = "EA online services are unavailable in this build."; Render(); }
            Button(content,"Create New EA Account",Unavailable,"create-account",462,380,614,68);
            Button(content,"Use Existing EA Account",Unavailable,"existing-account",462,437,614,68);
            var notice = Panel(content, 352, 758, 956, 97, false, false, new Color(0,0,0,.45f)); notice.Corners = false;
            Text(notice,"ESRB NOTICE: GAME EXPERIENCE MAY CHANGE\nDURING ONLINE PLAY",0,0,956,97,30,Color.white,TextAnchor.MiddleCenter);
            Footer("Back",GoBack);
        }
        private void RenderCredits()
        {
            FngGrungeRect("MwGrit01", -235, -316, 550, 550, -172, Color.black);
            FngGrungeRect("Vignette02", 242, -209, -512, 128, 180, Color.black);
            FngGrungeRect("Vignette02", -64, 222, -512, 128, 0, Color.black);
            Header("credits");
            var panel = Panel(content, 193, 113, 1158, 762, true, true, new Color(0,0,0,.97f));
            panel.CornerOutset = 12; panel.HeaderHeight = 80;
            var scroll=Place(new ScrollView(ScrollViewMode.Vertical){name="credits-scroll"},219,218,1106,613);
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            content.Add(scroll);
            string copy = frontendContent?.credits ?? "Need for Speed™ Most Wanted\n\nGame Team\n\n\nProgramming\n\nGary Bearchell\nAndrew Brownsword";
            var credits = new MostWantedBodyText(copy, 36);
            credits.style.height = Mathf.Max(900, copy.Split('\n').Length * 57 + 140);
            credits.style.width = 1106; credits.style.whiteSpace = WhiteSpace.Normal;
            credits.style.unityTextAlign = TextAnchor.UpperCenter; credits.style.color = Color.white;
            credits.style.marginTop = 100; scroll.Add(credits);
            double start = Time.unscaledTimeAsDouble;
            scroll.schedule.Execute(() =>
            {
                if (navigation.Current.Page != MostWantedFrontendPage.Credits || Time.unscaledTimeAsDouble - start < 3) return;
                float maximum = Mathf.Max(0, scroll.contentContainer.layout.height - scroll.contentViewport.layout.height);
                scroll.scrollOffset = new Vector2(0, maximum > 0 ? (float)((Time.unscaledTimeAsDouble - start - 3) * 26) % maximum : 0);
            }).Every(33);
            Footer("Back",GoBack);
        }
        private void UpdateLiveFrontend()
        {
            if(showroom!=null)showroom.Show(IsVisible && navigation.Current.Page != MostWantedFrontendPage.Title
                && navigation.Current.Page != MostWantedFrontendPage.AliasPrompt && navigation.Current.Page != MostWantedFrontendPage.AliasEntry,navigation.Current.Page);
            UpdateMusicNotice();
        }
    }
}
