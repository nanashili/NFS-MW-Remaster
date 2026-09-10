using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public enum MissionGraphPortKind
    {
        Control,
        Condition,
        Event,
        Action,
        Data
    }

    [Serializable]
    public sealed class MissionGraphNodeLayout
    {
        public string id = string.Empty;
        public Vector2 position;
        public bool collapsed;
        public bool pinned;
    }

    [Serializable]
    public sealed class MissionGraphGroupLayout
    {
        public string id = string.Empty;
        public string title = "Stage group";
        public Rect rect = new Rect(40, 40, 600, 360);
        public Color color = new Color(0.12f, 0.28f, 0.38f, 0.22f);
        public bool collapsed;
        public List<string> nodeIds = new List<string>();
    }

    [Serializable]
    public sealed class MissionGraphCommentLayout
    {
        public string id = string.Empty;
        public string text = "Comment";
        public Rect rect = new Rect(80, 80, 260, 90);
        public Color color = new Color(0.22f, 0.18f, 0.08f, 0.85f);
    }

    [Serializable]
    public sealed class MissionGraphBookmarkLayout
    {
        public string id = string.Empty;
        public string title = "Bookmark";
        public Vector2 pan;
        public float zoom = 1f;
        public List<string> nodeIds = new List<string>();
    }

    [Serializable]
    public sealed class MissionGraphRerouteLayout
    {
        public string edgeKey = string.Empty;
        public List<Vector2> points = new List<Vector2>();
    }

    [Serializable]
    public sealed class MissionGraphVariableNote
    {
        public string key = string.Empty;
        public string type = "number";
        public string ownership = "authoritative fact";
        public string notes = string.Empty;
    }

    /// <summary>
    /// Editor-only layout and review metadata. It is deliberately separate from
    /// MissionDefinitionAsset so graph movement/comments never alter a runtime
    /// content hash or a saved MissionSnapshot.
    /// </summary>
    public sealed class MissionGraphLayoutAsset : ScriptableObject
    {
        public const int CurrentSchema = 1;

        [SerializeField] private int schema = CurrentSchema;
        [SerializeField] private string definitionGuid = string.Empty;
        [SerializeField] private string definitionId = string.Empty;
        [SerializeField] private string sourceContentHash = string.Empty;
        [SerializeField] private Vector2 pan = new Vector2(40, 40);
        [SerializeField] private float zoom = 1f;
        [SerializeField] private List<MissionGraphNodeLayout> nodes = new List<MissionGraphNodeLayout>();
        [SerializeField] private List<MissionGraphGroupLayout> groups = new List<MissionGraphGroupLayout>();
        [SerializeField] private List<MissionGraphCommentLayout> comments = new List<MissionGraphCommentLayout>();
        [SerializeField] private List<MissionGraphBookmarkLayout> bookmarks = new List<MissionGraphBookmarkLayout>();
        [SerializeField] private List<MissionGraphRerouteLayout> reroutes = new List<MissionGraphRerouteLayout>();
        [SerializeField] private List<MissionGraphVariableNote> variables = new List<MissionGraphVariableNote>();

        public int Schema => schema;
        public string DefinitionGuid { get => definitionGuid; set => definitionGuid = value ?? string.Empty; }
        public string DefinitionId { get => definitionId; set => definitionId = value ?? string.Empty; }
        public string SourceContentHash { get => sourceContentHash; set => sourceContentHash = value ?? string.Empty; }
        public Vector2 Pan { get => pan; set => pan = value; }
        public float Zoom { get => zoom; set => zoom = Mathf.Clamp(value, 0.25f, 2.5f); }
        public List<MissionGraphNodeLayout> Nodes => nodes;
        public List<MissionGraphGroupLayout> Groups => groups;
        public List<MissionGraphCommentLayout> Comments => comments;
        public List<MissionGraphBookmarkLayout> Bookmarks => bookmarks;
        public List<MissionGraphRerouteLayout> Reroutes => reroutes;
        public List<MissionGraphVariableNote> Variables => variables;

        public MissionGraphNodeLayout FindNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && nodes[i].id == id) return nodes[i];
            return null;
        }

        public MissionGraphNodeLayout EnsureNode(string id, Vector2 defaultPosition)
        {
            var existing = FindNode(id);
            if (existing != null) return existing;
            var created = new MissionGraphNodeLayout { id = id, position = defaultPosition };
            nodes.Add(created);
            return created;
        }

        public void RemoveNode(string id)
        {
            nodes.RemoveAll(x => x == null || x.id == id);
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                if (groups[i] == null) { groups.RemoveAt(i); continue; }
                groups[i].nodeIds.RemoveAll(x => x == id);
            }
            for (int i = bookmarks.Count - 1; i >= 0; i--)
            {
                if (bookmarks[i] == null) { bookmarks.RemoveAt(i); continue; }
                bookmarks[i].nodeIds.RemoveAll(x => x == id);
            }
        }

        public MissionGraphRerouteLayout FindReroute(string edgeKey)
        {
            for (int i = 0; i < reroutes.Count; i++)
                if (reroutes[i] != null && reroutes[i].edgeKey == edgeKey) return reroutes[i];
            return null;
        }

        public void RemoveReroute(string edgeKey) => reroutes.RemoveAll(x => x == null || x.edgeKey == edgeKey);

        public void Normalize()
        {
            schema = CurrentSchema;
            zoom = Mathf.Clamp(float.IsFinite(zoom) ? zoom : 1f, 0.25f, 2.5f);
            if (nodes == null) nodes = new List<MissionGraphNodeLayout>();
            if (groups == null) groups = new List<MissionGraphGroupLayout>();
            if (comments == null) comments = new List<MissionGraphCommentLayout>();
            if (bookmarks == null) bookmarks = new List<MissionGraphBookmarkLayout>();
            if (reroutes == null) reroutes = new List<MissionGraphRerouteLayout>();
            if (variables == null) variables = new List<MissionGraphVariableNote>();
            foreach (var group in groups)
            {
                if (group == null) continue;
                if (group.nodeIds == null) group.nodeIds = new List<string>();
                group.nodeIds.RemoveAll(string.IsNullOrEmpty);
            }
            foreach (var bookmark in bookmarks)
            {
                if (bookmark == null) continue;
                if (bookmark.nodeIds == null) bookmark.nodeIds = new List<string>();
                bookmark.nodeIds.RemoveAll(string.IsNullOrEmpty);
                bookmark.zoom = Mathf.Clamp(float.IsFinite(bookmark.zoom) ? bookmark.zoom : 1f, 0.25f, 2.5f);
            }
            foreach (var reroute in reroutes)
                if (reroute != null && reroute.points == null) reroute.points = new List<Vector2>();
        }
    }
}
