using System;
using UnityEngine;
namespace NfsMwRemaster.Driving
{
    [Serializable] public sealed class GrimeChunk
    {
        public string key, layerId;
        public string[] strokeIds;
        public Mesh mesh;
        public Material material;
    }
    public sealed class GrimePublication : ScriptableObject
    {
        [SerializeField] private int schema;
        [SerializeField] private string owner, fingerprint, artifactSignature;
        [SerializeField] private GrimeChunk[] chunks;
        public int Schema => schema;
        public string Owner => owner;
        public string Fingerprint => fingerprint;
        public string ArtifactSignature => artifactSignature;
        public GrimeChunk[] Chunks
        {
            get
            {
                var result = new GrimeChunk[chunks?.Length ?? 0];
                for (int i = 0; i < result.Length; i++) result[i] = new GrimeChunk { key = chunks[i].key, layerId = chunks[i].layerId, strokeIds = (string[])chunks[i].strokeIds.Clone(), mesh = chunks[i].mesh, material = chunks[i].material };
                return result;
            }
        }
        public void Initialize(string source, string revision, string signature, GrimeChunk[] value)
        {
            if (schema != 0) throw new InvalidOperationException("Create a new grime publication.");
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(revision) || value == null) throw new ArgumentException("Invalid grime publication.");
            owner = source; fingerprint = revision; artifactSignature = signature; chunks = new GrimeChunk[value.Length]; for(int i=0;i<value.Length;i++) { var c=value[i]; chunks[i]=new GrimeChunk { key=c.key, layerId=c.layerId, strokeIds=(string[])c.strokeIds.Clone(), mesh=c.mesh, material=c.material }; } schema = 1;
        }
    }
}
