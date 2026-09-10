using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Bounded bridge from Unity particle collision events to a short-lived splash
    /// particle system. Unity reports contacts in batches, so one component can cap
    /// the cost without adding a script or GameObject per raindrop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RainCollisionImpactEmitter : MonoBehaviour
    {
        public ParticleSystem source;
        public ParticleSystem impacts;
        [Min(1)] public int maximumImpactsPerFrame = 48;

        readonly List<ParticleCollisionEvent> collisionEvents = new List<ParticleCollisionEvent>(64);
        float probability;
        float selectionAccumulator;
        int emissionFrame = -1;
        int emittedThisFrame;

        public float Probability => probability;
        public int LastCollisionCount { get; private set; }

        void Awake()
        {
            if (!source) source = GetComponent<ParticleSystem>();
        }

        public void SetProbability(float value)
        {
            probability = Mathf.Clamp01(value);
        }

        void OnParticleCollision(GameObject other)
        {
            if (!source || !impacts || probability <= .001f || !other) return;
            int count = source.GetCollisionEvents(other, collisionEvents);
            LastCollisionCount = count;
            if (emissionFrame != Time.frameCount)
            {
                emissionFrame = Time.frameCount;
                emittedThisFrame = 0;
            }

            int frameBudget = Mathf.Max(1, maximumImpactsPerFrame);
            for (int i = 0; i < count && emittedThisFrame < frameBudget; i++)
            {
                selectionAccumulator += probability;
                if (selectionAccumulator < 1) continue;
                selectionAccumulator -= 1;

                ParticleCollisionEvent contact = collisionEvents[i];
                var emit = new ParticleSystem.EmitParams
                {
                    position = contact.intersection + contact.normal * .012f,
                    velocity = contact.normal * .32f,
                    applyShapeToPosition = false
                };
                impacts.Emit(emit, 1);
                emittedThisFrame++;
            }
        }

        public void ResetPresentation()
        {
            probability = 0;
            selectionAccumulator = 0;
            LastCollisionCount = 0;
            emissionFrame = -1;
            emittedThisFrame = 0;
            collisionEvents.Clear();
            if (impacts) impacts.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDisable()
        {
            ResetPresentation();
        }
    }
}
