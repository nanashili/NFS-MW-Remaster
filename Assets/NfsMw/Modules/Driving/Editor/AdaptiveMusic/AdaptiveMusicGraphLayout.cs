#if UNITY_EDITOR
using System;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Editor-only layout data. Musical identity and runtime behavior live in
    /// SensoryMusicProfile; this asset only remembers how the graph is arranged.
    /// </summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Adaptive Music Graph Layout")]
    public sealed class AdaptiveMusicGraphLayout : ScriptableObject
    {
        [Serializable]
        public sealed class Node
        {
            public string sectionId = string.Empty;
            public Vector2 position;
            public bool collapsed;
        }

        public string profileId = string.Empty;
        public int layoutVersion = 1;
        public Node[] nodes = Array.Empty<Node>();

        public Vector2 GetPosition(string sectionId, Vector2 fallback)
        {
            if (nodes != null)
                for (int i = 0; i < nodes.Length; i++)
                    if (nodes[i] != null && string.Equals(nodes[i].sectionId, sectionId, StringComparison.Ordinal))
                        return nodes[i].position;
            return fallback;
        }

        public bool IsCollapsed(string sectionId)
        {
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i] != null && string.Equals(nodes[i].sectionId, sectionId, StringComparison.Ordinal))
                    return nodes[i].collapsed;
            return false;
        }

        public void SetPosition(string sectionId, Vector2 position, bool collapsed = false)
        {
            if (string.IsNullOrWhiteSpace(sectionId)) return;
            if (nodes == null) nodes = Array.Empty<Node>();
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null || !string.Equals(nodes[i].sectionId, sectionId, StringComparison.Ordinal)) continue;
                nodes[i].position = position;
                nodes[i].collapsed = collapsed;
                return;
            }
            var next = new Node[nodes.Length + 1];
            Array.Copy(nodes, next, nodes.Length);
            next[next.Length - 1] = new Node { sectionId = sectionId, position = position, collapsed = collapsed };
            nodes = next;
        }

        public bool RemoveUnknownNodes(SensoryMusicProfile profile)
        {
            if (nodes == null || profile == null) return false;
            int count = 0;
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i] != null && profile.FindSection(nodes[i].sectionId) != null) count++;
            if (count == nodes.Length) return false;
            var kept = new Node[count]; int index = 0;
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i] != null && profile.FindSection(nodes[i].sectionId) != null) kept[index++] = nodes[i];
            nodes = kept;
            return true;
        }
    }
}
#endif
