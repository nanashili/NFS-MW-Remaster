using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Editor.Workspace
{
    // Both hosts construct the same module view. Native Unity owns docking and layout.
    public abstract class RacingFocusedWindow : EditorWindow
    {
        protected abstract string ModuleId { get; }
        [SerializeField] private RacingDocumentLink document;
        [SerializeField] private bool pinned;
        [Serializable] private sealed class SavedView{public string key,json;}
        [SerializeField] private List<SavedView> viewStates=new List<SavedView>();
        [SerializeField] private RacingNavigationHistory history=new RacingNavigationHistory();
        private IRacingModuleView view;
        private string viewModule;
        private VisualElement body;
        private Label breadcrumb, message;
        private DropdownField modulePicker;
        private string loadedKey;
        protected virtual bool IsWorkspace => false;
        protected virtual void OnEnable(){minSize=new Vector2(275,300);Selection.selectionChanged+=FollowSelection;RacingModuleRegistry.Changed+=Rebuild;}
        protected virtual void OnDisable(){Selection.selectionChanged-=FollowSelection;RacingModuleRegistry.Changed-=Rebuild;ReleaseView();}
        public void CreateGUI()=>Rebuild();
        private void SaveView()
        {
            if(view is IRacingViewState state&&loadedKey!=null)
            {
                var saved=viewStates.FirstOrDefault(s=>s.key==loadedKey);
                if(saved==null){saved=new SavedView{key=loadedKey};viewStates.Add(saved);}
                saved.json=state.CaptureViewState();
                if(viewStates.Count>64)viewStates.RemoveAt(0);
            }
        }
        private void ReleaseView(){SaveView();var old=view;view=null;viewModule=null;loadedKey=null;old?.Dispose();}
        private void Rebuild()
        {
            ReleaseView();rootVisualElement.Clear();
            var toolbar=new Toolbar();toolbar.style.flexWrap=Wrap.Wrap;toolbar.style.height=StyleKeyword.Auto;
            toolbar.Add(new ToolbarButton(()=>{if(history.CanBack)Navigate(history.Back(),false);}){text="Back"});
            toolbar.Add(new ToolbarButton(()=>{if(history.CanForward)Navigate(history.Forward(),false);}){text="Forward"});
            var pin=new ToolbarToggle{text="Pin",value=pinned,tooltip="Keep this document while Unity selection changes. Explicit navigation still opens its destination."};
            pin.RegisterValueChangedCallback(e=>{pinned=e.newValue;ApplyContext();});toolbar.Add(pin);
            toolbar.Add(new ToolbarButton(()=>{pinned=false;pin.SetValueWithoutNotify(false);FollowSelection();}){text="Use Selection"});
            toolbar.Add(new ToolbarButton(()=>EditorGUIUtility.systemCopyBuffer=JsonUtility.ToJson(document)){text="Copy Link"});
            toolbar.Add(new ToolbarButton(PasteLink){text="Open Link"});
            toolbar.Add(new ToolbarButton(()=>{if(document!=null)RacingModuleRegistry.Find(document.moduleId)?.OpenFocused?.Invoke(document.Resolve());}){text="Focused Window"});
            rootVisualElement.Add(toolbar);
            if(IsWorkspace)
            {
                var modules=RacingModuleRegistry.All.ToArray();
                var labels=modules.Select(m=>m.Group+" / "+m.Label).ToList();
                var picker=new DropdownField("Tool",labels,Mathf.Max(0,Array.FindIndex(modules,m=>m.Id==document?.moduleId)));
                modulePicker=picker;
                picker.RegisterValueChangedCallback(e=>{var module=modules[labels.IndexOf(e.newValue)];Navigate(RacingDocumentLink.For(module.Id,module.Compatible(document?.Resolve())??module.Compatible(Selection.activeObject)));});
                rootVisualElement.Add(picker);
                var search=new ToolbarSearchField{tooltip="Search registered tools by name, group or alias. Enter opens the first matching tool."};
                search.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode!=KeyCode.Return||string.IsNullOrWhiteSpace(search.value))return;var match=modules.FirstOrDefault(m=>(m.Label+" "+m.Group+" "+m.Aliases).IndexOf(search.value,StringComparison.OrdinalIgnoreCase)>=0);if(match!=null){picker.value=labels[Array.IndexOf(modules,match)];e.StopPropagation();}});
                rootVisualElement.Add(search);
            }
            breadcrumb=new Label();breadcrumb.style.whiteSpace=WhiteSpace.Normal;rootVisualElement.Add(breadcrumb);
            body=new VisualElement{style={flexGrow=1}};rootVisualElement.Add(body);
            message=new Label();message.style.whiteSpace=WhiteSpace.Normal;rootVisualElement.Add(message);
            if(document==null)document=RacingDocumentLink.For(ModuleId,RacingModuleRegistry.Find(ModuleId)?.Compatible(Selection.activeObject));
            Navigate(document,false);
        }
        private void PasteLink()
        {
            try
            {
                var text=EditorGUIUtility.systemCopyBuffer;
                if(text.Length>16384)throw new InvalidOperationException("The link is too large.");
                var link=JsonUtility.FromJson<RacingDocumentLink>(text);
                if(link==null||RacingModuleRegistry.Find(link.moduleId)==null)throw new InvalidOperationException("Clipboard does not contain a registered Racing Tools document link.");
                if(!IsWorkspace&&link.moduleId!=ModuleId){RacingWorkspaceWindow.OpenDocument(link);return;}
                Navigate(link);
            }catch(Exception e){message.text=e.Message;}
        }
        public void OpenDocument(Object target)=>Navigate(RacingDocumentLink.For(ModuleId,target));
        public void Navigate(RacingDocumentLink link,bool record=true)
        {
            if(link==null)return;
            if(!IsWorkspace&&link.moduleId!=ModuleId){RacingWorkspaceWindow.OpenDocument(link);return;}
            document=link.Copy();if(record||history.Count==0)history.Push(document);
            if(body==null)return;
            var module=RacingModuleRegistry.Find(document.moduleId);
            if(module==null){ReleaseView();body.Clear();message.text="This module has not registered an editing surface: "+document.moduleId;return;}
            var target=document.Resolve();
            modulePicker?.SetValueWithoutNotify(module.Group+" / "+module.Label);
            breadcrumb.text=module.Group+" / "+module.Label+" / "+(target?target.name:string.IsNullOrEmpty(document.objectId)?"Choose a Document":"Missing Document");
            if(!string.IsNullOrEmpty(document.objectId)&&!target)
            {ReleaseView();body.Clear();message.text="The exact document is unavailable. Load its saved scene or restore the asset, then open the link again. No replacement was selected.";return;}
            message.text=module.IntegrationStatus;
            if(target&&module.SourceTypes.Length>0&&module.Compatible(target)!=target)
            {ReleaseView();body.Clear();message.text="This document type is not supported by "+module.Label+". Select a compatible source explicitly.";return;}
            if(view==null||viewModule!=module.Id)
            {
                ReleaseView();body.Clear();
                try{view=module.CreateView();viewModule=module.Id;if(view is RacingModuleView shared){shared.Navigate=l=>{var owner=view;rootVisualElement.schedule.Execute(()=>{if(view==owner)Navigate(l);});};shared.RepaintRequested=Repaint;}body.Add(view.Root);}
                catch(Exception e){ReleaseView();message.text="Unable to open tool: "+e.Message;Debug.LogException(e);return;}
            }
            string key=document.moduleId+"|"+document.objectId;
            if(loadedKey!=key)
            {
                SaveView();loadedKey=key;
                ApplyContext();
                if(string.IsNullOrEmpty(document.elementId)&&view is IRacingViewState state)
                {var saved=viewStates.FirstOrDefault(s=>s.key==key);if(saved!=null)try{state.RestoreViewState(saved.json);}catch(Exception e){message.text="Saved view settings could not be restored: "+e.Message;}}
            }
            else ApplyContext();
        }
        private void ApplyContext()=>view?.SetContext(new RacingEditingContext{Document=document,Pinned=pinned,UnitySelection=Selection.activeObject,Stage=UnityEditor.SceneManagement.StageUtility.GetCurrentStageHandle().ToString()});
        private void FollowSelection()
        {
            if(pinned)return;var module=RacingModuleRegistry.Find(document?.moduleId??ModuleId);
            var target=module?.Compatible(Selection.activeObject);
            if(target)Navigate(RacingDocumentLink.For(module.Id,target));
        }
    }
    public sealed class RacingWorkspaceWindow : RacingFocusedWindow
    {
        protected override string ModuleId=>"race-routes";
        protected override bool IsWorkspace=>true;
        [MenuItem("Window/Racing Tools/Workspace",false,0)]
        public static void Open()=>GetWindow<RacingWorkspaceWindow>(EditorPrefs.GetString("RacingTools.Title","Racing Tools"));
        public static void OpenDocument(RacingDocumentLink link){var window=GetWindow<RacingWorkspaceWindow>(EditorPrefs.GetString("RacingTools.Title","Racing Tools"));window.Navigate(link);window.Show();}
    }
}
