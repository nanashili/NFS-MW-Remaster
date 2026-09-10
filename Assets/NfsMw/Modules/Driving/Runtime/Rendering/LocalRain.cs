using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// One bounded precipitation volume for the primary view. The deterministic
    /// weather model supplies intent; this component owns particles, shelter,
    /// collision impacts, camera cuts, fades, and presentation budgets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalRain : MonoBehaviour
    {
        [Header("Follow volume")]
        public Transform view;
        [Min(4)] public float regionRadius = 90;
        [Min(1)] public float teleportDistance = 60;
        public LayerMask shelterLayers = ~0;

        [Header("Weatherade source profile")]
        [Tooltip("Primary emitter footprint from the Weatherade rain sample.")]
        [Min(1)] public float emitterFootprint = 50;
        [Tooltip("Camera follow offset from the Weatherade rain sample.")]
        [Min(1)] public float emitterHeight = 20;
        public Vector2 lateralVelocityX = new Vector2(-1, 0);
        public Vector2 lateralVelocityZ = new Vector2(-2, 0);
        [Tooltip("Minimum and maximum fall-speed multipliers. At 10 m/s these reproduce Weatherade's -10 to -7 m/s range.")]
        public Vector2 verticalVelocityScale = new Vector2(.7f, 1);
        [Min(.01f)] public float swayFrequency = 5;

        [Header("Rain particle layers")]
        [Tooltip("Middle-distance rain sheet. Kept as 'particles' for existing scene and prefab compatibility.")]
        public ParticleSystem particles;
        [Tooltip("Short-lived foreground streaks. This is also the collision source for local impact splashes.")]
        public ParticleSystem nearField;
        [Tooltip("Sparse long-lived rain that fills the distance around the camera.")]
        public ParticleSystem farField;
        [Tooltip("Short-lived splash particles emitted at near-field collision contacts.")]
        public ParticleSystem impactSplashes;
        [HideInInspector] public RainCollisionImpactEmitter collisionImpacts;
        [Min(0)] public float dropsPerSecond = 4200;
        [Min(1)] public float fallSpeedMps = 10;
        [Min(1)] public float nearFieldFallSpeed = 10;
        [Min(1)] public float farFieldFallSpeed = 10;
        [Range(0, 2)] public float nearFieldEmission = .34f;
        [Range(0, 2)] public float farFieldEmission = .16f;
        [Range(0, 1)] public float turbulence = .3f;
        [Min(0)] public float emissionFadeInSeconds = .28f;
        [Min(0)] public float emissionFadeOutSeconds = .7f;
        [Min(0)] public float prewarmSeconds = .35f;

        [Header("Road spray")]
        [Tooltip("Low suspended spray. This remains separate from the rain streak budget.")]
        public ParticleSystem surfaceMist;
        [Tooltip("Optional vehicle source. Without one, mist remains a bounded camera-local effect.")]
        public Transform sprayAnchor;
        [Range(0, 1)] public float surfaceMistEmission = .032f;
        [Min(1)] public float surfaceMistDistance = 19;

        // Retain scenes and prefabs authored before the explicit layer roles.
        [HideInInspector] public ParticleSystem[] additionalLayers = Array.Empty<ParticleSystem>();
        [HideInInspector] public float[] additionalLayerEmission = { .34f, .16f };
        [HideInInspector] public float[] additionalLayerFallSpeed = { 49, 23 };

        [Range(0, 1)] public float intensity;

        WeatherSnapshot snapshot;
        WeatherPresentationQuality presentationQuality = WeatherPresentationQuality.High;
        bool hasSnapshot;
        bool qualityConfigured;
        bool positioned;
        bool sheltered;
        bool wasSheltered;
        float nextShelterCheck;
        float gustPhase;
        float middleSignal;
        float nearSignal;
        float farSignal;
        float impactSignal;
        float mistSignal;
        float qualityDensity = 1;
        float qualityDistance = 180;
        float appliedQualityDensity = -1;
        float appliedQualityDistance = -1;
        Vector3 smoothWind;
        Vector3 lastViewPosition;
        Quaternion lastViewRotation;
        Transform lastView;

        ParticleSystem cachedMiddle;
        ParticleSystem cachedNear;
        ParticleSystem cachedFar;
        ParticleSystem cachedImpacts;
        ParticleSystem cachedMist;
        int middleBudget;
        int nearBudget;
        int farBudget;
        int impactBudget;
        int mistBudget;

        public float VisibleIntensity => middleSignal;
        public bool IsSheltered => sheltered;
        public bool HasCompleteLayerSet => particles && nearField && farField
            && impactSplashes && collisionImpacts && surfaceMist;

        void Awake()
        {
            // This phase is cosmetic and never participates in gameplay or persistence.
            gustPhase = (GetHashCode() & 0x7fff) * .0137f;
            ResolveLegacyLayers();
            CacheLayerBudgets();
            qualityDistance = Mathf.Max(8, regionRadius * 2);
        }

        void ResolveLegacyLayers()
        {
            if (!nearField && additionalLayers != null && additionalLayers.Length > 0)
                nearField = additionalLayers[0];
            if (!farField && additionalLayers != null && additionalLayers.Length > 1)
                farField = additionalLayers[1];
            if (additionalLayerEmission != null)
            {
                if (additionalLayerEmission.Length > 0) nearFieldEmission = Mathf.Max(0, additionalLayerEmission[0]);
                if (additionalLayerEmission.Length > 1) farFieldEmission = Mathf.Max(0, additionalLayerEmission[1]);
            }
            if (additionalLayerFallSpeed != null)
            {
                if (additionalLayerFallSpeed.Length > 0) nearFieldFallSpeed = Mathf.Max(1, additionalLayerFallSpeed[0]);
                if (additionalLayerFallSpeed.Length > 1) farFieldFallSpeed = Mathf.Max(1, additionalLayerFallSpeed[1]);
            }
        }

        void CacheLayerBudgets()
        {
            ResolveLegacyLayers();
            CacheBudget(particles, ref cachedMiddle, ref middleBudget, 32);
            CacheBudget(nearField, ref cachedNear, ref nearBudget, 32);
            CacheBudget(farField, ref cachedFar, ref farBudget, 32);
            CacheBudget(impactSplashes, ref cachedImpacts, ref impactBudget, 16);
            CacheBudget(surfaceMist, ref cachedMist, ref mistBudget, 16);
            ParticleSystem source = nearField ? nearField : particles;
            if (!collisionImpacts && source) collisionImpacts = source.GetComponent<RainCollisionImpactEmitter>();
            if (collisionImpacts)
            {
                if (!collisionImpacts.source) collisionImpacts.source = source;
                if (!collisionImpacts.impacts) collisionImpacts.impacts = impactSplashes;
            }
        }

        static void CacheBudget(ParticleSystem system, ref ParticleSystem cached, ref int budget, int floor)
        {
            if (cached == system && budget > 0) return;
            cached = system;
            budget = system ? Mathf.Max(floor, system.main.maxParticles) : floor;
        }

        public void ApplyQuality(float density, float distance)
        {
            float safeDensity = Mathf.Clamp01(density);
            float safeDistance = Mathf.Max(8, distance);
            if (qualityConfigured
                && Mathf.Approximately(appliedQualityDensity, safeDensity)
                && Mathf.Approximately(appliedQualityDistance, safeDistance)) return;

            CacheLayerBudgets();
            qualityDensity = safeDensity;
            qualityDistance = safeDistance;
            regionRadius = Mathf.Clamp(qualityDistance * .5f, 4, 240);
            ApplyLayerBudget(particles, middleBudget, safeDensity, 1);
            ApplyLayerBudget(nearField, nearBudget, safeDensity, .8f);
            ApplyLayerBudget(farField, farBudget, safeDensity, .7f);
            ApplyLayerBudget(impactSplashes, impactBudget, safeDensity, .7f);
            ApplyLayerBudget(surfaceMist, mistBudget, Mathf.Lerp(.35f, 1, safeDensity), 1);
            ApplyRainShape(particles, 1);
            ApplyRainShape(nearField, .72f);
            ApplyRainShape(farField, 1.3f);
            appliedQualityDensity = safeDensity;
            appliedQualityDistance = safeDistance;
            qualityConfigured = true;
        }

        static void ApplyLayerBudget(ParticleSystem system, int authoredBudget, float density, float multiplier)
        {
            if (!system) return;
            var main = system.main;
            main.maxParticles = Mathf.Max(16, Mathf.RoundToInt(authoredBudget * Mathf.Clamp01(density) * multiplier));
        }

        void ApplyRainShape(ParticleSystem system, float radiusMultiplier)
        {
            if (!system) return;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            float footprint = Mathf.Min(Mathf.Max(1, emitterFootprint), qualityDistance) * radiusMultiplier;
            shape.scale = new Vector3(footprint, .1f, footprint);
        }

        public void ApplyWeather(WeatherSnapshot value, WeatherPresentationQuality quality, Camera primaryCamera)
        {
            if (primaryCamera && (!view || view == transform)) view = primaryCamera.transform;
            snapshot = value;
            presentationQuality = quality;
            hasSnapshot = true;
            intensity = value.precipitationType == WeatherPrecipitationType.None
                ? 0
                : Mathf.Clamp01(value.precipitationIntensity);
            if (!qualityConfigured)
                ApplyQuality(WeatherPresentationModel.QualityDensity(quality), Mathf.Max(8, qualityDistance));
        }

        void LateUpdate()
        {
            ResolveLegacyLayers();
            if (!view || (!particles && !nearField && !farField) || !hasSnapshot) return;
            if (lastView && lastView != view) NotifyCameraCut();

            UpdateVolumePosition();
            UpdateShelter();

            WeatherPrecipitationPresentation target = WeatherPresentationModel.Precipitation(
                snapshot,
                presentationQuality,
                sheltered);
            float delta = Mathf.Max(0, Time.unscaledDeltaTime);
            float response = target.IsEmitting ? emissionFadeInSeconds : emissionFadeOutSeconds;
            middleSignal = Damp(middleSignal, target.middleEmission, response, delta);
            nearSignal = Damp(nearSignal, target.nearEmission, response, delta);
            farSignal = Damp(farSignal, target.farEmission, response, delta);
            impactSignal = Damp(impactSignal, target.impactEmission, response, delta);
            mistSignal = Damp(mistSignal, target.surfaceMistEmission, response, delta);

            float gust = 1 + Mathf.Sin(Time.unscaledTime * .47f + gustPhase) * snapshot.gustStrength * .22f;
            Vector3 targetWind = target.horizontalVelocity * gust;
            smoothWind = Vector3.Lerp(smoothWind, targetWind, 1 - Mathf.Exp(-delta * 2.4f));

            ApplyRainLayer(particles, middleSignal, 1, fallSpeedMps, 1);
            ApplyRainLayer(nearField, nearSignal, nearFieldEmission, nearFieldFallSpeed, .7f);
            ApplyRainLayer(farField, farSignal, farFieldEmission, farFieldFallSpeed, .55f);
            ApplyCollision(target.collisionEnabled);
            ApplyImpactProbability(impactSignal);
            UpdateSurfaceMist();
        }

        void UpdateVolumePosition()
        {
            Vector3 windDirection = snapshot.windSpeedMps > .001f
                ? Quaternion.Euler(0, snapshot.windDirectionDegrees, 0) * Vector3.forward
                : Vector3.zero;
            float upwindDistance = Mathf.Clamp(snapshot.windSpeedMps * 1.75f, 0, regionRadius * .45f);
            Vector3 desired = view.position + Vector3.up * Mathf.Max(1, emitterHeight) - windDirection * upwindDistance;

            if (!positioned)
            {
                SetRainLayerPosition(desired);
                lastViewPosition = view.position;
                lastViewRotation = view.rotation;
                lastView = view;
                positioned = true;
                ClearRainLayers();
                return;
            }

            float movement = (view.position - lastViewPosition).magnitude;
            float rotationDelta = Quaternion.Angle(view.rotation, lastViewRotation);
            bool cut = movement >= teleportDistance
                || rotationDelta > 75
                || (MiddleTransformPosition() - view.position).sqrMagnitude > regionRadius * regionRadius;
            if (cut)
            {
                SetRainLayerPosition(desired);
                ClearRainLayers();
                nextShelterCheck = 0;
            }
            else
            {
                // World-space particles remain behind while the bounded emitter follows.
                // The lag avoids a camera-locked rain box and naturally replenishes the
                // volume as the car advances.
                float follow = 1 - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.05f, regionRadius / 360));
                MoveRainLayer(particles, desired, follow);
                MoveRainLayer(nearField, desired, follow);
                MoveRainLayer(farField, desired, follow);
            }
            lastViewPosition = view.position;
            lastViewRotation = view.rotation;
            lastView = view;
        }

        Vector3 MiddleTransformPosition()
        {
            if (particles) return particles.transform.position;
            if (nearField) return nearField.transform.position;
            return farField ? farField.transform.position : transform.position;
        }

        static void MoveRainLayer(ParticleSystem system, Vector3 position, float follow)
        {
            if (system) system.transform.position = Vector3.Lerp(system.transform.position, position, follow);
        }

        void UpdateShelter()
        {
            if (Time.unscaledTime < nextShelterCheck) return;
            sheltered = WeatherShelterVolume.IsSheltered(view.position)
                || Physics.Raycast(
                    view.position + Vector3.up * .5f,
                    Vector3.up,
                    30,
                    shelterLayers,
                    QueryTriggerInteraction.Ignore);
            nextShelterCheck = Time.unscaledTime + .2f;
            if (sheltered && !wasSheltered) ClearAllParticles();
            wasSheltered = sheltered;
        }

        void ApplyRainLayer(ParticleSystem system, float signal, float emissionMultiplier, float fallSpeed, float noiseMultiplier)
        {
            if (!system) return;
            var emission = system.emission;
            emission.rateOverTime = dropsPerSecond * Mathf.Max(0, signal) * Mathf.Max(0, emissionMultiplier);
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            float xMin = Mathf.Min(lateralVelocityX.x, lateralVelocityX.y);
            float xMax = Mathf.Max(lateralVelocityX.x, lateralVelocityX.y);
            float zMin = Mathf.Min(lateralVelocityZ.x, lateralVelocityZ.y);
            float zMax = Mathf.Max(lateralVelocityZ.x, lateralVelocityZ.y);
            float verticalMin = Mathf.Clamp(Mathf.Min(verticalVelocityScale.x, verticalVelocityScale.y), .05f, 2);
            float verticalMax = Mathf.Clamp(Mathf.Max(verticalVelocityScale.x, verticalVelocityScale.y), verticalMin, 2);
            velocity.x = new ParticleSystem.MinMaxCurve(smoothWind.x + xMin, smoothWind.x + xMax);
            velocity.y = new ParticleSystem.MinMaxCurve(-Mathf.Max(1, fallSpeed) * verticalMax, -Mathf.Max(1, fallSpeed) * verticalMin);
            velocity.z = new ParticleSystem.MinMaxCurve(smoothWind.z + zMin, smoothWind.z + zMax);
            var noise = system.noise;
            noise.enabled = turbulence > .001f;
            noise.strength = turbulence * noiseMultiplier;
            noise.frequency = Mathf.Max(.01f, swayFrequency);
            noise.scrollSpeed = .14f;
            noise.damping = true;
            SetPlaying(system, signal > .001f);
        }

        void SetPlaying(ParticleSystem system, bool shouldEmit)
        {
            bool includeCollisionChild = system == nearField
                && impactSplashes
                && impactSplashes.transform.IsChildOf(system.transform);
            if (shouldEmit && !system.isPlaying)
            {
                if (prewarmSeconds > .001f)
                    system.Simulate(
                        Mathf.Min(prewarmSeconds, Mathf.Max(.01f, system.main.startLifetime.constantMax)),
                        includeCollisionChild,
                        true,
                        true);
                system.Play(includeCollisionChild);
            }
            else if (!shouldEmit && system.isPlaying)
            {
                system.Stop(includeCollisionChild, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        void ApplyCollision(bool enabledForQuality)
        {
            ParticleSystem collisionSource = nearField ? nearField : particles;
            if (!collisionSource) return;
            var collision = collisionSource.collision;
            collision.enabled = enabledForQuality && impactSplashes;
            if (!collision.enabled) return;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = shelterLayers;
            // Avoid an all-zero post-collision state so Unity still reports the
            // contact to the bounded impact adapter. The remaining motion is tiny,
            // and lifetime loss removes the streak immediately after its splash.
            collision.dampen = .92f;
            collision.bounce = .02f;
            collision.lifetimeLoss = .72f;
            collision.radiusScale = .25f;
            collision.maxCollisionShapes = 64;
            collision.enableDynamicColliders = false;
            collision.sendCollisionMessages = true;
        }

        void ApplyImpactProbability(float probability)
        {
            if (collisionImpacts)
            {
                collisionImpacts.SetProbability(probability);
                return;
            }

            // Compatibility for an older prefab that authored a native collision
            // sub-emitter before the bounded collision adapter was introduced.
            ParticleSystem collisionSource = nearField ? nearField : particles;
            if (!collisionSource || !impactSplashes) return;
            var subEmitters = collisionSource.subEmitters;
            for (int i = 0; i < subEmitters.subEmittersCount; i++)
            {
                if (subEmitters.GetSubEmitterSystem(i) == impactSplashes)
                    subEmitters.SetSubEmitterEmitProbability(i, Mathf.Clamp01(probability));
            }
        }

        void UpdateSurfaceMist()
        {
            if (!surfaceMist || !view) return;
            Transform sourceTransform = sprayAnchor ? sprayAnchor : view;
            Vector3 flatForward = Vector3.ProjectOnPlane(sourceTransform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < .001f) flatForward = Vector3.forward;
            flatForward.Normalize();
            float forwardOffset = sprayAnchor ? -1.35f : Mathf.Clamp(surfaceMistDistance, 6, regionRadius * .45f);
            Vector3 target = sourceTransform.position + flatForward * forwardOffset + Vector3.up * .32f;
            float follow = 1 - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.05f, regionRadius / 260));
            surfaceMist.transform.position = Vector3.Lerp(surfaceMist.transform.position, target, follow);
            surfaceMist.transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);

            var emission = surfaceMist.emission;
            emission.rateOverTime = dropsPerSecond * Mathf.Max(0, mistSignal) * surfaceMistEmission;
            var velocity = surfaceMist.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(smoothWind.x * .22f);
            velocity.y = new ParticleSystem.MinMaxCurve(.25f);
            velocity.z = new ParticleSystem.MinMaxCurve(smoothWind.z * .22f + .35f);
            SetPlaying(surfaceMist, mistSignal > .005f);
        }

        static float Damp(float current, float target, float seconds, float delta)
        {
            if (seconds <= .001f) return target;
            return Mathf.Lerp(current, target, 1 - Mathf.Exp(-delta / seconds));
        }

        void SetRainLayerPosition(Vector3 position)
        {
            if (particles) particles.transform.position = position;
            if (nearField) nearField.transform.position = position;
            if (farField) farField.transform.position = position;
        }

        void ClearRainLayers()
        {
            if (particles) particles.Clear(false);
            if (nearField) nearField.Clear(false);
            if (farField) farField.Clear(false);
        }

        void ClearAllParticles()
        {
            ClearRainLayers();
            if (impactSplashes) impactSplashes.Clear(false);
            if (surfaceMist) surfaceMist.Clear(false);
        }

        public void NotifyCameraCut()
        {
            positioned = false;
            if (collisionImpacts) collisionImpacts.ResetPresentation();
            StopAndClearAll();
        }

        public void ResetWeatherPresentation()
        {
            intensity = 0;
            hasSnapshot = false;
            sheltered = false;
            wasSheltered = false;
            nextShelterCheck = 0;
            middleSignal = nearSignal = farSignal = impactSignal = mistSignal = 0;
            smoothWind = Vector3.zero;
            positioned = false;
            StopAndClearAll();
        }

        void StopAndClearAll()
        {
            StopAndClear(particles);
            StopAndClear(nearField);
            StopAndClear(farField);
            StopAndClear(impactSplashes);
            StopAndClear(surfaceMist);
        }

        static void StopAndClear(ParticleSystem system)
        {
            if (system) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDisable()
        {
            ResetWeatherPresentation();
        }
    }

}
