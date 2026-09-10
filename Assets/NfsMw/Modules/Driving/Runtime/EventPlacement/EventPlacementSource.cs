using System;
using UnityEngine;
namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class EventPlacementSource : MonoBehaviour
    {
        public int schema = 1;
        public string id = Guid.NewGuid().ToString("N");
        public WorldActivityDefinition definition;
        public ActivityAnchor anchor = new ActivityAnchor();
        public ActivityAnchor access = new ActivityAnchor { kind = ActivityAnchorKind.Lane };
        public Vector3 interactionOffset, stagingOffset, iconOffset = Vector3.up * 5, cinematicOffset;
        public Vector3 triggerSize = new Vector3(8, 4, 10);
        public Vector3 vehicleSize = new Vector3(2.5f, 3.2f, 5);
        [Range(0, 180)] public float approachAngle = 75;
        [Range(0, 2.9f)] public float maximumSpeed = 2;
        [Range(0, 10)] public float dwellSeconds = .5f;
        [Range(0, 45)] public float maximumGrade = 15;
        [Min(0)] public float maximumAccessLength = 30;
        public LayerMask obstructionMask = ~0;
        public string district, level = "ground", streamingCell;
        public Vector3[] serviceExitOffsets = { Vector3.zero };
        [TextArea] public string notes;
        [HideInInspector] public EventPlacementPublication published;
    }
}
