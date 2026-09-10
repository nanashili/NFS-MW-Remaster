using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName="NFS MW Remaster/Frontend/Content")]
    public sealed class MostWantedFrontendContent : ScriptableObject
    {
        [Serializable] public sealed class Rival
        {
            public string id="", displayName="", fullName="", carName="", strength="", portraitTexture="", backgroundTexture="";
            [Range(1,15)] public int rank=1;
            [TextArea(4,10)] public string biography="";
            public string[] challengeEventIds=Array.Empty<string>();
        }
        public Rival[] rivals=Array.Empty<Rival>();
        public SensoryMusicProfile soundtrack;
        public SensoryMixProfile audioMix;
        [TextArea(4,20)] public string credits="NFS MW Remaster\n\nFrontend implementation\n\nOriginal game and artwork\nElectronic Arts / EA Black Box\n\nSee the project's asset provenance and notices.";
    }
}
