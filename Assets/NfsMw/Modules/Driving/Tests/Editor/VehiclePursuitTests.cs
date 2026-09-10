using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehiclePursuitTests
    {
        [Test]
        public void PoliceResponseProfileEscalatesPressureAcrossHeat()
        {
            VehiclePoliceResponseDefaultProfile profile =
                new VehiclePoliceResponseDefaultProfile();
            VehiclePoliceResponseTier low = profile.GetTier(1);
            VehiclePoliceResponseTier high = profile.GetTier(5);

            Assert.That(high.MaxUnits, Is.GreaterThan(low.MaxUnits));
            Assert.That(high.Aggression, Is.GreaterThan(low.Aggression));
            Assert.That(high.TopSpeedMultiplier, Is.GreaterThan(low.TopSpeedMultiplier));
            Assert.That(high.SearchDuration, Is.GreaterThan(low.SearchDuration));
            Assert.That(high.DisengageRadius, Is.GreaterThan(low.DisengageRadius));
        }

        [Test]
        public void ExplicitExtensionInterceptorAndBoxerRolesChooseCoordinatedTactics()
        {
            VehiclePursuitDecision interceptor =
                VehiclePursuitDecisionModel.Decide(
                    new VehiclePursuitDecisionInput
                    {
                        Role = VehiclePoliceUnitRole.Interceptor,
                        AllowContactExtensions = true,
                        Phase = VehiclePursuitPhase.Engaged,
                        DistanceToTarget = 50f,
                        Aggression = 0.94f,
                        TargetVisible = true,
                        Slot = 0
                    });
            VehiclePursuitDecision boxer =
                VehiclePursuitDecisionModel.Decide(
                    new VehiclePursuitDecisionInput
                    {
                        Role = VehiclePoliceUnitRole.Boxer,
                        AllowContactExtensions = true,
                        Phase = VehiclePursuitPhase.Engaged,
                        DistanceToTarget = 20f,
                        Aggression = 0.94f,
                        TargetVisible = true,
                        Slot = 1
                    });

            Assert.That(interceptor.Tactic, Is.EqualTo(VehiclePoliceTactic.Intercept));
            Assert.That(boxer.Tactic, Is.EqualTo(VehiclePoliceTactic.BoxRight));
            Assert.That(boxer.CommitsToContact, Is.True);
        }

        [Test]
        public void ExplicitContactExtensionPursuerCommitsToCloseRam()
        {
            VehiclePursuitDecision decision =
                VehiclePursuitDecisionModel.Decide(
                    new VehiclePursuitDecisionInput
                    {
                        Role = VehiclePoliceUnitRole.Pursuer,
                        AllowContactExtensions = true,
                        Phase = VehiclePursuitPhase.Engaged,
                        DistanceToTarget = 12f,
                        Aggression = 0.94f,
                        TargetVisible = true,
                        Slot = 0
                    });

            Assert.That(decision.Tactic, Is.EqualTo(VehiclePoliceTactic.Ram));
            Assert.That(decision.CommitsToContact, Is.True);
        }

        [Test]
        public void SearchPhaseDoesNotContinueDirectContactAttack()
        {
            VehiclePursuitDecision decision =
                VehiclePursuitDecisionModel.Decide(
                    new VehiclePursuitDecisionInput
                    {
                        Role = VehiclePoliceUnitRole.Heavy,
                        Phase = VehiclePursuitPhase.Search,
                        DistanceToTarget = 5f,
                        Aggression = 0.94f,
                        TargetVisible = false,
                        Slot = 0
                    });

            Assert.That(decision.Tactic, Is.EqualTo(VehiclePoliceTactic.Search));
            Assert.That(decision.CommitsToContact, Is.False);
        }

        [Test]
        public void PursuitTargetAdapterExposesTheVehicleBodyContract()
        {
            GameObject root = new GameObject("Pursuit Target Test");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                VehiclePursuitTargetAdapter adapter =
                    root.AddComponent<VehiclePursuitTargetAdapter>();
                adapter.Configure("test_target");

                Assert.That(adapter.TargetId, Is.EqualTo("test_target"));
                Assert.That(adapter.Body, Is.SameAs(body));
                Assert.That(adapter.Transform, Is.SameAs(root.transform));
                Assert.That(adapter.IsAvailable, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
