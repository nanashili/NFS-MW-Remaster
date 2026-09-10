using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public enum CareerGraphProjectedNodeKind
    {
        Content,
        EventGroup,
        Fact,
        MissingReference,
        ExternalActivity,
        ExternalMission,
        EconomyItem
    }

    public enum CareerGraphRelationKind
    {
        Requires,
        RequiresAny,
        RequiresNot,
        GroupMember,
        AuthoringReference,
        PriceReference
    }

    public enum CareerGraphFindingSeverity
    {
        Info,
        Warning,
        Error,
        Unsupported
    }

    public enum CareerGraphViewMode
    {
        Overview,
        ForwardDependencies,
        ReverseDependencies
    }

    /// <summary>
    /// Editor projection of one career node. It is intentionally not a runtime
    /// state object and is rebuilt from the authoritative CareerDefinitionAsset.
    /// </summary>
    public sealed class CareerGraphNodeModel
    {
        public string Key = string.Empty;
        public string Id = string.Empty;
        public string Label = string.Empty;
        public string TierLabel = string.Empty;
        public string SourcePath = string.Empty;
        public string SourceGuid = string.Empty;
        public string Notes = string.Empty;
        public CareerGraphProjectedNodeKind ProjectedKind;
        public CareerContentKind ContentKind;
        public bool Missing;
        public bool Teased;
        public int ContentIndex = -1;
        public int GroupIndex = -1;
        public Vector2 DefaultPosition;
        public Vector2 Position;

        public bool IsContent => ProjectedKind == CareerGraphProjectedNodeKind.Content;
        public bool IsExternal => ProjectedKind == CareerGraphProjectedNodeKind.ExternalActivity
            || ProjectedKind == CareerGraphProjectedNodeKind.ExternalMission
            || ProjectedKind == CareerGraphProjectedNodeKind.EconomyItem;

        public string DisplayKind
        {
            get
            {
                if (IsContent)
                {
                    return ContentKind.ToString();
                }

                return ProjectedKind.ToString();
            }
        }
    }

    public sealed class CareerGraphEdgeModel
    {
        public string Key = string.Empty;
        public string FromKey = string.Empty;
        public string ToKey = string.Empty;
        public string Label = string.Empty;
        public string RequirementPath = string.Empty;
        public CareerGraphRelationKind Relation;
        public bool Negative;
    }

    public sealed class CareerGraphRequirementModel
    {
        public CareerRequirementKind Kind;
        public CareerFactKind Fact;
        public string SubjectId = string.Empty;
        public string Description = string.Empty;
        public long Required;
        public bool Negative;
        public int SourceIndex = -1;
        public readonly List<CareerGraphRequirementModel> Children = new List<CareerGraphRequirementModel>();

        public string Summary
        {
            get
            {
                if (Kind == CareerRequirementKind.Fact)
                {
                    string subject = string.IsNullOrEmpty(SubjectId) ? string.Empty : ": " + SubjectId;
                    return Fact + subject + " >= " + Required;
                }

                return Kind.ToString();
            }
        }
    }

    public sealed class CareerGraphFinding
    {
        public string Code = string.Empty;
        public CareerGraphFindingSeverity Severity;
        public string Title = string.Empty;
        public string Message = string.Empty;
        public string SourceId = string.Empty;
        public string SourcePath = string.Empty;
        public string NodeKey = string.Empty;
        public bool AssumptionDependent;

        public CareerGraphFinding()
        {
        }

        public CareerGraphFinding(
            string code,
            CareerGraphFindingSeverity severity,
            string title,
            string message,
            string sourceId = "",
            string sourcePath = "",
            string nodeKey = "",
            bool assumptionDependent = false)
        {
            Code = code ?? string.Empty;
            Severity = severity;
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            NodeKey = nodeKey ?? string.Empty;
            AssumptionDependent = assumptionDependent;
        }
    }

    public sealed class CareerGraphExternalReference
    {
        public string Kind = string.Empty;
        public string Id = string.Empty;
        public string Label = string.Empty;
        public string AssetPath = string.Empty;
        public string AssetGuid = string.Empty;
        public string Status = string.Empty;
        public UnityEngine.Object Asset;
    }

    public sealed class CareerGraphEconomyMatch
    {
        public string ItemId = string.Empty;
        public string Name = string.Empty;
        public EconomyItemKind Kind;
        public int Price;
        public bool InitiallyUnlocked;
        public string AssetPath = string.Empty;
        public string[] CompatibleVehicleIds = Array.Empty<string>();
        public string[] PerformanceIds = Array.Empty<string>();
        public string[] CustomizationIds = Array.Empty<string>();
    }

    public sealed class CareerGraphProjection
    {
        public CareerDefinitionAsset Asset;
        public CareerGraphDefinition Source;
        public CareerGraph Compiled;
        public string AssetPath = string.Empty;
        public string AssetGuid = string.Empty;
        public string SourceFingerprint = string.Empty;
        public string CompileError = string.Empty;
        public bool IsCompiled => Compiled != null && string.IsNullOrEmpty(CompileError);
        public readonly List<CareerGraphNodeModel> Nodes = new List<CareerGraphNodeModel>();
        public readonly List<CareerGraphEdgeModel> Edges = new List<CareerGraphEdgeModel>();
        public readonly List<CareerGraphFinding> Findings = new List<CareerGraphFinding>();
        public readonly List<CareerGraphExternalReference> ExternalReferences = new List<CareerGraphExternalReference>();
        public readonly List<CareerGraphEconomyMatch> EconomyMatches = new List<CareerGraphEconomyMatch>();
        public readonly Dictionary<string, CareerGraphNodeModel> NodesByKey = new Dictionary<string, CareerGraphNodeModel>(StringComparer.Ordinal);
        public readonly Dictionary<string, CareerGraphRequirementModel> RequirementsByNodeKey = new Dictionary<string, CareerGraphRequirementModel>(StringComparer.Ordinal);
        public readonly Dictionary<string, CareerContentDefinition> ContentById = new Dictionary<string, CareerContentDefinition>(StringComparer.Ordinal);
        public readonly Dictionary<string, CareerEventGroupDefinition> GroupsById = new Dictionary<string, CareerEventGroupDefinition>(StringComparer.Ordinal);

        public CareerGraphNodeModel FindNode(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return NodesByKey.TryGetValue(key, out CareerGraphNodeModel node) ? node : null;
        }

        public CareerGraphNodeModel FindContent(string id)
        {
            return FindNode(ContentKey(id));
        }

        public IEnumerable<CareerGraphEdgeModel> Incoming(string key)
        {
            return Edges.Where(edge => string.Equals(edge.ToKey, key, StringComparison.Ordinal));
        }

        public IEnumerable<CareerGraphEdgeModel> Outgoing(string key)
        {
            return Edges.Where(edge => string.Equals(edge.FromKey, key, StringComparison.Ordinal));
        }

        public static string ContentKey(string id) => "content:" + (id ?? string.Empty);
        public static string GroupKey(string id) => "group:" + (id ?? string.Empty);
        public static string FactKey(CareerFactKind fact, string subjectId)
            => "fact:" + fact + ":" + (subjectId ?? string.Empty);
        public static string MissingKey(CareerFactKind fact, string subjectId)
            => "missing:" + fact + ":" + (subjectId ?? string.Empty);
    }

    [Serializable]
    public sealed class CareerGraphNodeLayout
    {
        public string key = string.Empty;
        public Vector2 position;
        public bool collapsed;
        public bool pinned;
    }

    [Serializable]
    public sealed class CareerGraphGroupLayout
    {
        public string id = string.Empty;
        public string title = "Career group";
        public Rect rect = new Rect(40, 40, 640, 360);
        public Color color = new Color(0.12f, 0.28f, 0.38f, 0.22f);
        public bool collapsed;
        public List<string> nodeKeys = new List<string>();
    }

    [Serializable]
    public sealed class CareerGraphBookmarkLayout
    {
        public string id = string.Empty;
        public string title = "Bookmark";
        public Vector2 pan;
        public float zoom = 1f;
        public List<string> nodeKeys = new List<string>();
    }

    [Serializable]
    public sealed class CareerGraphCommentLayout
    {
        public string id = string.Empty;
        public string text = "Comment";
        public Rect rect = new Rect(80, 80, 280, 100);
        public Color color = new Color(0.22f, 0.18f, 0.08f, 0.85f);
    }

    /// <summary>
    /// Editor-only canvas metadata. It is kept in a separate asset so moving a
    /// node, adding a comment or changing a bookmark never changes progression
    /// semantics, save compatibility or the career source fingerprint.
    /// </summary>
    public sealed class CareerGraphLayoutAsset : ScriptableObject
    {
        public const int CurrentSchema = 1;

        [SerializeField] private int schema = CurrentSchema;
        [SerializeField] private string definitionGuid = string.Empty;
        [SerializeField] private string definitionId = string.Empty;
        [SerializeField] private string sourceFingerprint = string.Empty;
        [SerializeField] private Vector2 pan = new Vector2(50, 50);
        [SerializeField] private float zoom = 1f;
        [SerializeField] private List<CareerGraphNodeLayout> nodes = new List<CareerGraphNodeLayout>();
        [SerializeField] private List<CareerGraphGroupLayout> groups = new List<CareerGraphGroupLayout>();
        [SerializeField] private List<CareerGraphBookmarkLayout> bookmarks = new List<CareerGraphBookmarkLayout>();
        [SerializeField] private List<CareerGraphCommentLayout> comments = new List<CareerGraphCommentLayout>();

        public int Schema => schema;
        public string DefinitionGuid { get => definitionGuid; set => definitionGuid = value ?? string.Empty; }
        public string DefinitionId { get => definitionId; set => definitionId = value ?? string.Empty; }
        public string SourceFingerprint { get => sourceFingerprint; set => sourceFingerprint = value ?? string.Empty; }
        public Vector2 Pan { get => pan; set => pan = value; }
        public float Zoom { get => zoom; set => zoom = Mathf.Clamp(value, 0.25f, 2.5f); }
        public List<CareerGraphNodeLayout> Nodes => nodes;
        public List<CareerGraphGroupLayout> Groups => groups;
        public List<CareerGraphBookmarkLayout> Bookmarks => bookmarks;
        public List<CareerGraphCommentLayout> Comments => comments;

        public CareerGraphNodeLayout FindNode(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return nodes.FirstOrDefault(node => node != null && string.Equals(node.key, key, StringComparison.Ordinal));
        }

        public CareerGraphNodeLayout EnsureNode(string key, Vector2 defaultPosition)
        {
            CareerGraphNodeLayout existing = FindNode(key);
            if (existing != null)
            {
                return existing;
            }

            var created = new CareerGraphNodeLayout { key = key, position = defaultPosition };
            nodes.Add(created);
            return created;
        }

        public CareerGraphGroupLayout FindGroup(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return groups.FirstOrDefault(group => group != null
                && string.Equals(group.id, id, StringComparison.Ordinal));
        }

        public CareerGraphGroupLayout EnsureGroup(
            string id,
            string title,
            Rect defaultRect,
            IEnumerable<string> nodeKeys = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            CareerGraphGroupLayout existing = FindGroup(id);
            if (existing != null)
            {
                if (!string.IsNullOrEmpty(title))
                {
                    existing.title = title;
                }

                if (existing.nodeKeys == null)
                {
                    existing.nodeKeys = new List<string>();
                }

                return existing;
            }

            var created = new CareerGraphGroupLayout
            {
                id = id,
                title = string.IsNullOrEmpty(title) ? id : title,
                rect = defaultRect,
                nodeKeys = nodeKeys == null
                    ? new List<string>()
                    : nodeKeys.Where(key => !string.IsNullOrEmpty(key))
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
            };
            groups.Add(created);
            return created;
        }

        public void Normalize()
        {
            schema = CurrentSchema;
            pan = new Vector2(
                float.IsFinite(pan.x) ? pan.x : 50f,
                float.IsFinite(pan.y) ? pan.y : 50f);
            zoom = Mathf.Clamp(float.IsFinite(zoom) ? zoom : 1f, 0.25f, 2.5f);
            if (nodes == null) nodes = new List<CareerGraphNodeLayout>();
            if (groups == null) groups = new List<CareerGraphGroupLayout>();
            if (bookmarks == null) bookmarks = new List<CareerGraphBookmarkLayout>();
            if (comments == null) comments = new List<CareerGraphCommentLayout>();

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            nodes.RemoveAll(node => node == null
                || string.IsNullOrEmpty(node.key)
                || !nodeIds.Add(node.key));
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            groups.RemoveAll(group => group == null
                || string.IsNullOrEmpty(group.id)
                || !groupIds.Add(group.id));
            var bookmarkIds = new HashSet<string>(StringComparer.Ordinal);
            bookmarks.RemoveAll(bookmark => bookmark == null
                || string.IsNullOrEmpty(bookmark.id)
                || !bookmarkIds.Add(bookmark.id));
            var commentIds = new HashSet<string>(StringComparer.Ordinal);
            comments.RemoveAll(comment => comment == null
                || string.IsNullOrEmpty(comment.id)
                || !commentIds.Add(comment.id));
            foreach (CareerGraphGroupLayout group in groups)
            {
                if (group.nodeKeys == null) group.nodeKeys = new List<string>();
                var groupNodeIds = new HashSet<string>(StringComparer.Ordinal);
                group.nodeKeys.RemoveAll(key => string.IsNullOrEmpty(key) || !groupNodeIds.Add(key));
            }
            foreach (CareerGraphBookmarkLayout bookmark in bookmarks)
            {
                if (bookmark.nodeKeys == null) bookmark.nodeKeys = new List<string>();
                var bookmarkNodeIds = new HashSet<string>(StringComparer.Ordinal);
                bookmark.nodeKeys.RemoveAll(key => string.IsNullOrEmpty(key) || !bookmarkNodeIds.Add(key));
                bookmark.zoom = Mathf.Clamp(float.IsFinite(bookmark.zoom) ? bookmark.zoom : 1f, 0.25f, 2.5f);
            }
        }
    }

    public sealed class CareerGraphComparison
    {
        public string LeftFingerprint = string.Empty;
        public string RightFingerprint = string.Empty;
        public readonly List<string> Added = new List<string>();
        public readonly List<string> Removed = new List<string>();
        public readonly List<string> ChangedRequirements = new List<string>();
        public readonly List<string> ChangedExternalLinks = new List<string>();
    }
}
