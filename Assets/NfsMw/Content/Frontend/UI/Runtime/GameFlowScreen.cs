using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static NfsMwRemaster.Driving.MostWantedFrontendStyle;

namespace NfsMwRemaster.Driving
{
    /// <summary>Most Wanted frontend presentation over the existing application and career services.</summary>
    [DisallowMultipleComponent]
    public sealed partial class GameFlowScreen : MonoBehaviour
    {
        private GameFlowRuntime runtime;
        private UIDocument document;
        private PanelSettings panel;
        private VisualElement surface, content, footer;
        private Label progress;
        private string alias, feedback;
        private Action confirmation;
        private string confirmationText;
        private GameFlowState renderedState = (GameFlowState)(-1);
        private bool locationVisible;
        private int backFrame = -1;
        private float nextRefresh;
        private readonly Dictionary<Key, Action> shortcuts = new Dictionary<Key, Action>();
        private readonly MostWantedFrontendNavigation navigation = new MostWantedFrontendNavigation();
        private Action<int> horizontalNavigation;
        private readonly List<VisualElement> primaryButtons = new List<VisualElement>();
        private bool rendering;
        private MostWantedFrontendPage lastRenderedPage = (MostWantedFrontendPage)(-1);
        public VisualElement Root => document?.rootVisualElement;
        public MostWantedFrontendNavigation Navigation => navigation;
        public bool IsVisible => Root != null && Root.style.display.value != DisplayStyle.None;
        public bool OwnsLocation => runtime?.WorldSession?.State == FreeRoamState.Location;
        public string CurrentPage => navigation.Current.Page.ToString();

        public void Initialize(GameFlowRuntime application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            runtime = application; alias = runtime.Settings.DefaultAlias;
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "Most Wanted Frontend Panel";
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("GameFlowTheme");
            if (panel.themeStyleSheet == null) throw new InvalidOperationException("GameFlowTheme is missing from Resources.");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int((int)Width, (int)Height);
            panel.screenMatchMode = PanelScreenMatchMode.Expand;
            panel.sortingOrder = 1000;
            document = gameObject.AddComponent<UIDocument>(); document.panelSettings = panel;
            Root.RegisterCallback<NavigationCancelEvent>(evt => { if(TryHandleBack()) evt.StopImmediatePropagation(); },TrickleDown.TrickleDown);
            Root.RegisterCallback<NavigationMoveEvent>(OnNavigate,TrickleDown.TrickleDown);
            Root.RegisterCallback<GeometryChangedEvent>(_ => UpdateViewportAnchors());
            InitializePreferences();
            Render();
        }

        public void Render()
        {
            if (rendering || Root == null || runtime?.Flow == null) return;
            rendering = true;
            try
            {
                SyncApplicationState();
                bool pageChanged = lastRenderedPage != navigation.Current.Page;
                var outgoingContent = pageChanged ? content : null;
                var outgoingFooter = pageChanged ? footer : null;
                string focusName = (Root.focusController?.focusedElement as VisualElement)?.name;
                if (lastRenderedPage == navigation.Current.Page && !string.IsNullOrEmpty(focusName)) navigation.Current.FocusName = focusName;
                lastRenderedPage = navigation.Current.Page;
                Root.Clear(); progress=null;shortcuts.Clear();primaryButtons.Clear();horizontalNavigation=null;
                Root.style.position=Position.Absolute;Root.style.left=Root.style.right=Root.style.top=Root.style.bottom=0;
                Root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                Root.style.backgroundColor=Color.black;
                bool visible = OwnsLocation || (runtime.Flow.State != GameFlowState.FreeRoam && runtime.Flow.State != GameFlowState.RaceActive);
                Root.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
                if (visible) MapInputFocus.Acquire(this); else MapInputFocus.Release(this);
                if(!visible){confirmation=null;showroom?.Show(false,navigation.Current.Page);StopMusicPreview();return;}
                // Keep the authored UI scale; edge-anchored groups use the extra viewport width.
                surface=Box(Root,"mw-frontend",0,0,Width,Height);
                surface.style.left=Length.Percent(50);surface.style.top=Length.Percent(50);
                surface.style.translate=new Translate(Length.Percent(-50),Length.Percent(-50));
                surface.style.overflow=Overflow.Visible;
                Root.style.overflow=Overflow.Hidden;
                BuildBackdrop();
                content=Box(surface,"mw-content",0,0,Width,Height);
                footer=Box(surface,"mw-command-bar",156,912,1230,62);
                RenderPage(navigation.Current.Page);
                UpdateViewportAnchors();
                BeginPageAnimation(outgoingContent,outgoingFooter,pageChanged);
                string message=!string.IsNullOrWhiteSpace(feedback)?feedback:runtime.Flow.Failure;
                if(!string.IsNullOrEmpty(message))
                {
                    var status=Panel(surface,156,850,1215,48,false,false,new Color(0,0,0,.92f));
                    Text(status,message,15,1,1185,46,22,Khaki);
                }
                if(confirmation!=null) RenderConfirmation();
                RenderPreferenceOverlay();
                string target=navigation.Current.FocusName;
                Root.schedule.Execute(()=>
                {
                    if(Root==null||!IsVisible)return;
                    var previous=string.IsNullOrEmpty(target)?null:Root.Q<VisualElement>(target);
                    if(previous?.focusable==true && previous.enabledInHierarchy) previous.Focus();
                    else if(primaryButtons.Count>0)primaryButtons[0].Focus();
                });
            }
            finally { rendering=false; }
        }

        private void UpdateViewportAnchors()
        {
            if (Root == null || content == null || footer == null) return;
            float viewportWidth = Root.contentRect.width;
            if (float.IsNaN(viewportWidth) || viewportWidth <= 0) return;
            float sideSpace = Mathf.Max(0, (viewportWidth - Width) * .5f);
            bool carousel = navigation.Current.Page == MostWantedFrontendPage.MainMenu
                || navigation.Current.Page == MostWantedFrontendPage.Career
                || navigation.Current.Page == MostWantedFrontendPage.Options
                || navigation.Current.Page == MostWantedFrontendPage.Safehouse;
            content.style.left = carousel ? sideSpace : 0;
            footer.style.left = 156 - sideSpace;
        }

        private void SyncApplicationState()
        {
            bool location=OwnsLocation;
            if(runtime.Flow.State==renderedState&&location==locationVisible)return;
            CancelCustomizationPreview();
            CancelPreferences();
            StopMusicPreview();
            confirmation=null;feedback=null;
            renderedState=runtime.Flow.State;locationVisible=location;
            var page = location ? runtime.WorldSession.ActiveLocation?.Kind==WorldLocationKind.BodyShop
                || runtime.WorldSession.ActiveLocation?.Kind==WorldLocationKind.PerformanceShop ? MostWantedFrontendPage.Customize : MostWantedFrontendPage.Safehouse
                : renderedState==GameFlowState.Boot && runtime.Settings.RequireTitleConfirmation ? MostWantedFrontendPage.Title
                : renderedState==GameFlowState.MainMenu ? MostWantedFrontendPage.MainMenu
                : renderedState==GameFlowState.Paused ? MostWantedFrontendPage.Pause
                : renderedState==GameFlowState.Results ? MostWantedFrontendPage.Results
                : renderedState==GameFlowState.Faulted ? MostWantedFrontendPage.Faulted : MostWantedFrontendPage.Loading;
            navigation.Reset(page);
            RefreshCareerSnapshot();
        }

        public void OpenPage(MostWantedFrontendPage page)
        {
            if(runtime.Flow.IsBusy||confirmation!=null||PreferencesConsumeInput||preferences?.HasPendingVideoChange==true)return;
            navigation.Current.FocusName=(Root?.focusController?.focusedElement as VisualElement)?.name??string.Empty;
            if (navigation.Current.Page == MostWantedFrontendPage.Music && page != MostWantedFrontendPage.Music) StopMusicPreview();
            if (MostWantedFrontendNavigation.IsSettings(navigation.Current.Page) && !MostWantedFrontendNavigation.IsSettings(page)) CancelPreferences();
            navigation.Push(page); feedback=null;
            if(MostWantedFrontendNavigation.IsSettings(page)) BeginPreferences(page);
            RefreshCareerSnapshot();Render();
        }
        public bool DismissConfirmation()
        {if(confirmation==null)return false;confirmation=null;Render();return true;}
        public bool TryHandleBack()
        {
            if(!IsVisible)return false;
            if(backFrame==Time.frameCount)return true;
            backFrame=Time.frameCount;
            if(CancelRebinding())return true;
            if(DismissConfirmation())return true;
            if(runtime.Flow.IsBusy){runtime.CancelLoading();return true;}
            if (preferences?.HasPendingVideoChange == true)
            {
                CancelPreferences();
                BeginPreferences(navigation.Current.Page);
                Render();
                return true;
            }
            if (navigation.Current.Page == MostWantedFrontendPage.Controls && preferences?.IsDirty == true && CanInteractWithPreferences)
            { AcceptPreferences(); return true; }
            if(MostWantedFrontendNavigation.IsSettings(navigation.Current.Page)
                && navigation.Current.Page != MostWantedFrontendPage.AdvancedVideo) CancelPreferences();
            if(navigation.Current.Page==MostWantedFrontendPage.Music)StopMusicPreview();
            if(navigation.Current.Page==MostWantedFrontendPage.Paint||navigation.Current.Page==MostWantedFrontendPage.Cart)CancelCustomizationPreview();
            if(navigation.Back()){feedback=null;Render();return true;}
            if(OwnsLocation){Execute(GameFlowCommand.Continue);return true;}
            if(runtime.Flow.State==GameFlowState.Paused){Execute(GameFlowCommand.Resume);return true;}
            if(runtime.Flow.State==GameFlowState.Results){Execute(GameFlowCommand.Continue);return true;}
            if(runtime.Flow.State==GameFlowState.MainMenu){Confirm("Exit the game?",Application.Quit);return true;}
            return true;
        }
        private void GoBack() { backFrame=-1;TryHandleBack(); }
        private void Confirm(string message,Action action)
        {confirmationText=message;confirmation=action;navigation.Current.FocusName="confirm";Render();}
        private void Execute(GameFlowCommand command)
        {
            var result=runtime.Flow.Execute(command);if(!result.Succeeded)feedback=result.Message;
            RefreshCareerSnapshot();Render();
        }
        private void OnNavigate(NavigationMoveEvent evt)
        {
            if(PreferencesConsumeInput){evt.StopImmediatePropagation();return;}
            if (TryNavigatePreferenceRows(evt)) return;
            if(confirmation!=null||horizontalNavigation==null)return;
            if(evt.direction!=NavigationMoveEvent.Direction.Left&&evt.direction!=NavigationMoveEvent.Direction.Right)return;
            if(evt.target is TextField || (evt.target as VisualElement)?.GetFirstAncestorOfType<TextField>()!=null)return;
            horizontalNavigation(evt.direction==NavigationMoveEvent.Direction.Left?-1:1);evt.StopImmediatePropagation();
        }

        private void Update()
        {
            if(runtime==null)return;
            if(locationVisible!=OwnsLocation)Render();
            TickFrontendMusic();
            TickMusicPreview();
            TickFrontendAnimations();
            if(!IsVisible)return;
            TickPreferences();
            if(progress!=null)progress.text=runtime.Flow.State==GameFlowState.Boot?"Initializing game systems…"
                :runtime.CanCancelLoad?$"Loading world — {runtime.Flow.LoadProgress:P0}":"Preparing game systems…";
            var keyboard=Keyboard.current;
            bool typing=Root.focusController?.focusedElement is TextField || (Root.focusController?.focusedElement as VisualElement)?.GetFirstAncestorOfType<TextField>()!=null;
            if(!typing&&confirmation==null&&keyboard!=null&&!PreferencesConsumeInput
                &&preferences?.HasPendingVideoChange!=true&&preferences?.IsRestoringDisplay!=true)
            {
                // Numeric hints are presentation shortcuts only; driving action bindings remain untouched.
                Action invoke=null;
                foreach(var binding in shortcuts)if(keyboard[binding.Key].wasPressedThisFrame){invoke=binding.Value;break;}
                invoke?.Invoke();
            }
            if(Time.unscaledTime>=nextRefresh)
            {
                nextRefresh=Time.unscaledTime+.5f;
                UpdateLiveFrontend();
            }
        }

        private MostWantedPanel Panel(VisualElement parent,float x,float y,float w,float h,bool stripes=true,bool bands=true,Color? fill=null)
        {
            var panelElement=Place(new MostWantedPanel{Stripes=stripes,HeaderBands=bands,Fill=fill??Color.black},x,y,w,h);
            parent.Add(panelElement);return panelElement;
        }
        private Button Button(VisualElement parent,string text,Action action,string name,float x,float y,float w,float h,bool primary=true)
        {
            var button=Place(new Button(()=>{if(!runtime.Flow.IsBusy)action();}){name=name},x,y,w,h);
            button.AddToClassList("mw-button");button.style.backgroundColor=Color.clear;
            button.style.borderLeftWidth=button.style.borderRightWidth=button.style.borderTopWidth=button.style.borderBottomWidth=0;
            button.style.paddingLeft=button.style.paddingRight=button.style.paddingTop=button.style.paddingBottom=0;
            button.style.marginLeft=button.style.marginRight=button.style.marginTop=button.style.marginBottom=0;
            button.style.borderTopLeftRadius=button.style.borderTopRightRadius=button.style.borderBottomLeftRadius=button.style.borderBottomRightRadius=0;
            var corners=Place(new MostWantedPanel{Fill=Color.clear,CornerColor=Amber},0,0,w,h);corners.style.display=DisplayStyle.None;button.Add(corners);
            var label=Text(button,text,22,0,w-44,h,30,Khaki,TextAnchor.MiddleCenter);
            void Selected(bool selected){corners.style.display=selected?DisplayStyle.Flex:DisplayStyle.None;label.style.color=selected?Amber:Khaki;}
            button.RegisterCallback<FocusInEvent>(_=>{Selected(true);navigation.Current.FocusName=name;});
            button.RegisterCallback<FocusOutEvent>(_=>Selected(false));
            button.RegisterCallback<PointerEnterEvent>(_=>Selected(true));
            button.RegisterCallback<PointerLeaveEvent>(_=>Selected(ReferenceEquals(Root.focusController?.focusedElement,button)));
            parent.Add(button);if(primary)primaryButtons.Add(button);return button;
        }
        private float footerX;
        private static void StyleScrollbar(ScrollView scroll)
        {
            var bar = scroll.verticalScroller;
            bar.style.width = 28; bar.style.backgroundColor = Color.clear;
            var slider = bar.Q<Slider>("unity-slider");
            var tracker = bar.Q("unity-tracker");
            var thumb = bar.Q("unity-dragger");
            var border = bar.Q("unity-dragger-border");
            if (slider != null) slider.style.backgroundColor = Color.clear;
            if (border != null) border.style.display = DisplayStyle.None;
            foreach (var part in new[] { tracker, thumb })
            {
                if (part == null) continue;
                part.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                part.style.borderTopWidth = part.style.borderRightWidth = part.style.borderBottomWidth = part.style.borderLeftWidth = 0;
                part.style.borderTopLeftRadius = part.style.borderTopRightRadius = part.style.borderBottomLeftRadius = part.style.borderBottomRightRadius = 0;
                part.style.width = 10;
                part.style.backgroundColor = part == thumb ? new Color(.28f,.24f,.1f,1) : new Color(.045f,.045f,.04f,1);
            }
            foreach (var button in new[] { bar.lowButton, bar.highButton })
            {
                button.style.width = button.style.height = 28;
                button.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                button.style.backgroundColor = Color.clear;
                button.style.borderTopWidth = button.style.borderRightWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = 0;
                var arrow = Image(button, MostWantedFrontendArt.Find("MostWantedUI/Images/Menus/Navigation/ArrowSkinnier"), 2, 2, 24, 24, Khaki);
                arrow.style.rotate = new Rotate(button == bar.lowButton ? 90 : -90);
            }
        }

        private void Footer(string label,Action action,string key="Esc",Key shortcut=Key.None,bool enabled=true)
        {
            if(footer.childCount==0)footerX=0;
            float width=label == "Defaults" || label == "Re-Order" || label == "Preview" ? 280
                : label == "Accept" ? 225 : Mathf.Clamp(86+label.Length*16,195,395);
            var button=Button(footer,string.Empty,action,"command-"+label.Replace(' ','-').ToLowerInvariant(),footerX,0,width,54,false);
            button.Insert(0,Place(new MostWantedPanel{Stripes=true,Corners=false,StripeOpacity=1.4f,Fill=new Color(0,0,0,.86f)},0,0,width,54));
            var keyArt = MostWantedFrontendArt.Find("MostWantedUI/Images/Controls/Keyboard/Keys/" + (key == "↵" ? "Enter" : key));
            if (keyArt != null) Image(button, keyArt, -2, -5, 85, 64);
            else
            {
                var cap=Box(button,"keycap",10,8,54,38);cap.style.borderBottomWidth=cap.style.borderTopWidth=cap.style.borderLeftWidth=cap.style.borderRightWidth=3;
                cap.style.borderBottomColor=cap.style.borderTopColor=cap.style.borderLeftColor=cap.style.borderRightColor=Color.white;
                cap.style.borderTopLeftRadius=cap.style.borderTopRightRadius=cap.style.borderBottomLeftRadius=cap.style.borderBottomRightRadius=6;
                Text(cap,key,0,0,54,35,21,Color.white,TextAnchor.MiddleCenter);
            }
            var caption=Text(button,label,78,0,width-85,54,28);caption.style.whiteSpace=WhiteSpace.NoWrap;
            button.SetEnabled(enabled);if(!enabled)button.style.opacity=.35f;
            if(shortcut!=Key.None&&enabled)shortcuts[shortcut]=action;
            footerX+=width+14;
        }
        private void Header(string title,bool cash=false)
        {
            AddGrunge(content,0,0,610,190,"header");
            content.Add(Place(new MostWantedTitleText(title.ToLowerInvariant(),900,91,90),190,36,900,91));
            if(cash)
            {
                var cashPanel=Panel(content,1010,14,372,85,true,false,new Color(0,0,0,.78f));cashPanel.Corners=false;
                Text(cashPanel,$"Cash: {runtime.WorldSession?.Cash??0:N0}",15,10,345,65,30,Khaki,TextAnchor.MiddleRight);
            }
        }
        private void Icon(VisualElement parent,string kind,float x,float y,float size,Color? tint=null)
        {
            var texture=FindIcon(kind);
            if(texture!=null)
            {
                var image = Image(parent,texture,x,y,size,size,tint??Color.white);
                if (kind == "right") image.style.scale = new Scale(new Vector3(-1,1,1));
            }
            else parent.Add(Place(new MostWantedIcon(kind,tint??Color.white),x,y,size,size));
        }
        private void RenderConfirmation()
        {
            content.SetEnabled(false);footer.SetEnabled(false);primaryButtons.Clear();
            var dim=Box(surface,"modal-scrim",0,0,Width,Height);dim.style.backgroundColor=new Color(0,0,0,.62f);
            var modal=Box(surface,"confirmation",386,300,766,320);Panel(modal,0,0,766,320);
            Text(modal,confirmationText,45,73,676,97,29,Color.white,TextAnchor.MiddleCenter);
            Button(modal,"Accept",()=>{var action=confirmation;confirmation=null;action?.Invoke();Render();},"confirm",45,185,676,52);
            Button(modal,"Back",()=>DismissConfirmation(),"cancel",45,238,676,52);
        }
        private void OnDestroy()
        {
            StopFrontendMusic();DisposePreferences();CancelCustomizationPreview();
            DisposeMusicPreview();MapInputFocus.Release(this);
            if(panel!=null)Destroy(panel);
        }

        private void OnDisable()
        {
            StopFrontendMusic();CancelPreferences();StopMusicPreview();MapInputFocus.Release(this);
            showroom?.Show(false,navigation.Current.Page);
        }

        private void OnEnable()
        {
            if(runtime!=null&&Root!=null)Render();
        }
    }
}
