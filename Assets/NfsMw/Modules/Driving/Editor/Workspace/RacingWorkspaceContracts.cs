using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Editor.Workspace
{
    public enum RacingToolGroup { World, Gameplay, Vehicles, Audio, Diagnostics }
    public enum RacingSelectionKind { Document, Element, Spatial, RecordedSample }

    // Persistent navigation stores stable references only. Preview handles and live
    // entities deliberately do not share this representation.
    [Serializable]
    public sealed class RacingDocumentLink
    {
        public string moduleId = "", objectId = "", assetPath = "", elementId = "";
        public string sourceRevision = "";
        public RacingSelectionKind kind;
        public Vector3 location;
        public double time;
        [NonSerialized] private Object sessionObject;
        public static RacingDocumentLink For(string module, Object target, string element = "")
        {
            return new RacingDocumentLink
            {
                moduleId = module, elementId = element ?? "", sessionObject = target,
                objectId = target ? StableIdentity(target) : "",
                assetPath = target ? AssetDatabase.GetAssetPath(target) : "",
                kind = string.IsNullOrEmpty(element) ? RacingSelectionKind.Document : RacingSelectionKind.Element
            };
        }
        private static string StableIdentity(Object target)
        {
            bool saved=EditorUtility.IsPersistent(target);
            if(target is Component component)saved=!string.IsNullOrEmpty(component.gameObject.scene.path);
            if(target is GameObject gameObject)saved=!string.IsNullOrEmpty(gameObject.scene.path);
            return saved?GlobalObjectId.GetGlobalObjectIdSlow(target).ToString():"session:"+target.GetEntityId();
        }
        public Object Resolve()
        {
            if(sessionObject)return sessionObject;
            // Do not fall back to a path/name: a replacement asset is a different document.
            return GlobalObjectId.TryParse(objectId, out var id)
                ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null;
        }
        public RacingDocumentLink Copy() => (RacingDocumentLink)MemberwiseClone();
        public bool SameTarget(RacingDocumentLink other) => other != null && moduleId == other.moduleId
            && objectId == other.objectId && elementId == other.elementId && kind == other.kind
            && location == other.location && time.Equals(other.time);
    }

    public sealed class RacingEditingContext
    {
        public RacingDocumentLink Document { get; internal set; }
        public bool Pinned { get; internal set; }
        public string SelectionOrigin { get; internal set; } = "workspace";
        public Object UnitySelection { get; internal set; }
        public Object TestTarget { get; set; }
        public string Stage { get; internal set; } = "";
        public Object ResolveDocument() => Document?.Resolve();
    }

    [Serializable]
    public sealed class RacingNavigationHistory
    {
        [SerializeField] private List<RacingDocumentLink> links = new List<RacingDocumentLink>();
        [SerializeField] private int cursor = -1;
        public int Count => links.Count;
        public bool CanBack => cursor > 0;
        public bool CanForward => cursor + 1 < links.Count;
        public RacingDocumentLink Current => cursor >= 0 ? links[cursor].Copy() : null;
        public void Push(RacingDocumentLink link)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (cursor >= 0 && links[cursor].SameTarget(link)) return;
            if (CanForward) links.RemoveRange(cursor + 1, links.Count - cursor - 1);
            links.Add(link.Copy());
            if (links.Count > 64) links.RemoveAt(0);
            cursor = links.Count - 1;
        }
        public RacingDocumentLink Back() { if (CanBack) cursor--; return Current; }
        public RacingDocumentLink Forward() { if (CanForward) cursor++; return Current; }
    }

    public interface IRacingModuleView : IDisposable
    {
        VisualElement Root { get; }
        void SetContext(RacingEditingContext context);
    }
    public interface IRacingViewState
    {
        string CaptureViewState();
        void RestoreViewState(string state);
    }

    // View lifetime belongs to a real dockable host. This is not a hidden EditorWindow.
    public abstract class RacingModuleView : IRacingModuleView
    {
        protected readonly VisualElement rootVisualElement = new VisualElement { style = { flexGrow = 1 } };
        public VisualElement Root => rootVisualElement;
        public Action<RacingDocumentLink> Navigate { get; set; }
        public Action RepaintRequested { get; set; }
        protected Rect position => new Rect(0, 0, Root.resolvedStyle.width, Root.resolvedStyle.height);
        protected void Repaint() { Root.MarkDirtyRepaint(); RepaintRequested?.Invoke(); }
        public abstract void SetContext(RacingEditingContext context);
        public abstract void Dispose();
    }

    public sealed class RacingModuleDescriptor
    {
        public string Id { get; }
        public string Label { get; }
        public string Aliases { get; }
        public RacingToolGroup Group { get; }
        public Type[] SourceTypes { get; }
        public Func<IRacingModuleView> CreateView { get; }
        public Action<Object> OpenFocused { get; }
        public string IntegrationStatus { get; }
        public RacingModuleDescriptor(string id, string label, RacingToolGroup group, string aliases,
            Type[] sourceTypes, Func<IRacingModuleView> createView, Action<Object> openFocused,
            string integrationStatus)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label) || createView == null)
                throw new ArgumentException("Module identity, label and view factory are required.");
            Id = id; Label = label; Group = group; Aliases = aliases ?? "";
            SourceTypes = sourceTypes?.ToArray() ?? Array.Empty<Type>(); CreateView = createView;
            OpenFocused = openFocused; IntegrationStatus = integrationStatus ?? "";
        }
        public Object Compatible(Object selected)
        {
            if (!selected) return null;
            if (SourceTypes.Any(t => t.IsInstanceOfType(selected))) return selected;
            if (selected is GameObject go)
                foreach (var type in SourceTypes.Where(t => typeof(Component).IsAssignableFrom(t)))
                { var component = go.GetComponent(type); if (component) return component; }
            return null;
        }
    }

    public static class RacingModuleRegistry
    {
        private static readonly Dictionary<string, RacingModuleDescriptor> modules = new Dictionary<string, RacingModuleDescriptor>(StringComparer.Ordinal);
        public static IEnumerable<RacingModuleDescriptor> All => modules.Values.OrderBy(m => m.Group).ThenBy(m => m.Label);
        public static event Action Changed;
        public static void Register(RacingModuleDescriptor module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (modules.ContainsKey(module.Id)) throw new InvalidOperationException("Duplicate racing module: " + module.Id);
            modules.Add(module.Id, module); Changed?.Invoke();
        }
        public static RacingModuleDescriptor Find(string id) => id != null && modules.TryGetValue(id, out var module) ? module : null;
    }

    public sealed class RacingCommand
    {
        public string Id { get; }
        public string Label { get; }
        public string Effects { get; }
        public string Recovery { get; }
        private readonly Func<RacingEditingContext, string> validate;
        private readonly Action<RacingEditingContext> execute;
        public RacingCommand(string id, string label, string effects, string recovery,
            Func<RacingEditingContext, string> validate, Action<RacingEditingContext> execute)
        {
            if (string.IsNullOrWhiteSpace(id) || validate == null || execute == null)
                throw new ArgumentException("Command identity, validation and execution are required.");
            Id = id; Label = label; Effects = effects; Recovery = recovery;
            this.validate = validate; this.execute = execute;
        }
        public string UnavailableReason(RacingEditingContext context) => validate(context) ?? "";
        public void Execute(RacingEditingContext context)
        {
            string failure = UnavailableReason(context);
            if (failure.Length > 0) throw new InvalidOperationException(failure);
            execute(context);
        }
    }

    public static class RacingEditGuard
    {
        public static string Reason(Object target)
        {
            if (!target) return "Select an existing source document.";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Return to Edit mode before changing authored content.";
            string path = AssetDatabase.GetAssetPath(target);
            if (target is Component component) path = component.gameObject.scene.path;
            if (path.StartsWith("Packages/", StringComparison.Ordinal)) return "Package content is read-only in this workflow.";
            if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.ForceUpdate))
                return "The source is read-only. Make it editable through the project's source-control workflow.";
            return "";
        }
        public static void Require(Object target)
        { var reason = Reason(target); if (reason.Length > 0) throw new InvalidOperationException(reason); }
    }
}
