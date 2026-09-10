using System;
using System.Linq;
using NfsMwRemaster.Driving.Editor.WorldValidation;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class WorldValidationCoreTests
    {
        private static WorldValidationRuleDescriptor Descriptor(string id = "test.rule")
        {
            return new WorldValidationRuleDescriptor
            {
                id = id,
                version = 1,
                displayName = id,
                ownerModule = "Tests",
                category = WorldValidationCategory.Identity,
                supportedScopes = (WorldValidationScopeKind[])Enum.GetValues(typeof(WorldValidationScopeKind))
            };
        }

        [Test]
        public void BlockingStateDependsOnStatusAndSeverity()
        {
            WorldValidationRuleDescriptor descriptor = Descriptor();
            Assert.That(WorldValidationResult.Failed(descriptor, "E", "Error", "bad"), Is.Not.Null);
            Assert.That(WorldValidationResult.Failed(descriptor, "E", "Error", "bad").IsBlocking, Is.True);
            Assert.That(WorldValidationResult.Failed(descriptor, "W", "Review", "review", WorldValidationSeverity.Warning).IsBlocking, Is.False);
            Assert.That(WorldValidationResult.Unsupported(descriptor, "U", "not available").IsBlocking, Is.False);
            Assert.That(WorldValidationResult.Create(descriptor, WorldValidationStatus.ErrorRunning,
                WorldValidationSeverity.Error, "X", "runner", "failed").IsBlocking, Is.True);
        }

        [Test]
        public void ReportNormalizationIsDeterministicAndCountsStatuses()
        {
            WorldValidationRuleDescriptor first = Descriptor("test.z");
            WorldValidationRuleDescriptor second = Descriptor("test.a");
            var report = new WorldValidationReport
            {
                results = new[]
                {
                    WorldValidationResult.Warning(first, "WARN", "Review", "review"),
                    WorldValidationResult.Passed(second, "clean"),
                    WorldValidationResult.NotEvaluated(first, "SKIP", "not run")
                }
            };

            report.NormalizeAndSummarize();

            Assert.That(report.results, Has.Length.EqualTo(3));
            Assert.That(report.passed, Is.EqualTo(1));
            Assert.That(report.warnings, Is.EqualTo(1));
            Assert.That(report.notEvaluated, Is.EqualTo(1));
            // The production report orders category, descending severity, status,
            // then stable rule identity. Alphabetical rule order is not the primary key.
            Assert.That(report.results[0].ruleId, Is.EqualTo("test.z"));
            Assert.That(report.results[0].status, Is.EqualTo(WorldValidationStatus.Warning));
            string[] ordered=report.results.Select(result=>result.Key+"|"+result.status).ToArray();
            report.results=report.results.Reverse().ToArray();
            report.NormalizeAndSummarize();
            Assert.That(report.results.Select(result=>result.Key+"|"+result.status),Is.EqualTo(ordered));
            Assert.That(report.coverage, Has.Some.Matches<WorldValidationCoverage>(entry =>
                entry.category == WorldValidationCategory.Identity && entry.evaluated == 2 && entry.notEvaluated == 1));
        }

        [Test]
        public void SuppressionHidesOnlyTheTargetedFinding()
        {
            var policy = ScriptableObject.CreateInstance<WorldValidationPolicy>();
            try
            {
                WorldValidationRuleDescriptor descriptor = Descriptor();
                var finding = WorldValidationResult.Failed(descriptor, "MISSING", "Missing", "bad");
                finding.sourceRevision = "rev-1";
                finding.assetPath = "Assets/Test.asset";
                finding.AddAffectedId("owner.one");
                policy.AddSuppression(finding, "Assets/Test.asset", "Intentional fixture gap", "test");
                var report = new WorldValidationReport { results = new[] { finding } };

                policy.Apply(report);

                Assert.That(report.results[0].status, Is.EqualTo(WorldValidationStatus.Suppressed));
                Assert.That(report.results[0].originalStatus, Is.EqualTo(nameof(WorldValidationStatus.Failed)));
                Assert.That(report.results[0].IsBlocking, Is.False);
                var unrelated=finding.Clone();unrelated.assetPath="Assets/Other.asset";
                var unrelatedReport=new WorldValidationReport{results=new[]{unrelated}};
                policy.Apply(unrelatedReport);
                Assert.That(unrelatedReport.results[0].status,Is.EqualTo(WorldValidationStatus.Failed),"A suppression cannot cross its authored path scope.");
            }
            finally { UnityEngine.Object.DestroyImmediate(policy); }
        }

        [Test]
        public void BaselineKeepsDebtVisibleAndRevisionChangesBecomeNew()
        {
            var policy = ScriptableObject.CreateInstance<WorldValidationPolicy>();
            try
            {
                WorldValidationRuleDescriptor descriptor = Descriptor();
                var existing = WorldValidationResult.Failed(descriptor, "MISSING", "Missing", "old debt");
                existing.sourceRevision = "rev-1";
                policy.AddBaseline(existing, "Tracked legacy debt");

                var report = new WorldValidationReport { results = new[] { existing.Clone() } };
                policy.Apply(report);
                Assert.That(report.results[0].baselineState, Is.EqualTo(WorldValidationBaselineState.ExistingDebt));
                Assert.That(report.results[0].status, Is.EqualTo(WorldValidationStatus.Failed));
                Assert.That(report.results[0].IsBlocking, Is.True);

                var changed = existing.Clone();
                changed.sourceRevision = "rev-2";
                report = new WorldValidationReport { results = new[] { changed } };
                policy.Apply(report);
                Assert.That(report.results[0].baselineState, Is.EqualTo(WorldValidationBaselineState.New));
            }
            finally { UnityEngine.Object.DestroyImmediate(policy); }
        }

        [Test]
        public void BatchParserPreservesExplicitPolicyAndFlags()
        {
            var request = WorldValidationBatch.ParseRequest(new[]
            {
                "Unity", "-worldValidationScope", "ExplicitScenes",
                "-worldValidationScenes", "Assets/A.unity;Assets/B.unity",
                "-worldValidationCategories", "Roads,WorldArt",
                "-worldValidationTimeout", "3.5",
                "-worldValidationExitPolicy", "AllRequestedRulesEvaluated",
                "-worldValidationIncremental", "-worldValidationExpensive", "-worldValidationNoInfo"
            });

            Assert.That(request.scope, Is.EqualTo(WorldValidationScopeKind.ExplicitScenes));
            Assert.That(request.explicitScenePaths, Is.EqualTo(new[] { "Assets/A.unity", "Assets/B.unity" }));
            Assert.That(request.categories, Is.EqualTo(new[] { WorldValidationCategory.Roads, WorldValidationCategory.WorldArt }));
            Assert.That(request.timeoutSeconds, Is.EqualTo(3.5f));
            Assert.That(request.exitPolicy, Is.EqualTo(WorldValidationExitPolicy.AllRequestedRulesEvaluated));
            Assert.That(request.incremental, Is.True);
            Assert.That(request.includeExpensive, Is.True);
            Assert.That(request.includeInfo, Is.False);
        }

        [Test]
        public void BatchParserPreservesDistrictCellScope()
        {
            var request = WorldValidationBatch.ParseRequest(new[]
            {
                "Unity", "-worldValidationScope", "DistrictCell",
                "-worldValidationDistrictId", "district-01",
                "-worldValidationCells", "0,0;1,-1;district-01|2,3"
            });

            Assert.That(request.scope, Is.EqualTo(WorldValidationScopeKind.DistrictCell));
            Assert.That(request.districtId, Is.EqualTo("district-01"));
            Assert.That(request.cellIds, Is.EqualTo(new[] { "0,0", "1,-1", "district-01|2,3" }));
        }

        [Test]
        public void CellKeysRemainStableAndExplicitlyNamespaced()
        {
            Assert.That(WorldValidationContext.CellKey("district-01", new Vector2Int(-1, 2)), Is.EqualTo("district-01|-1,2"));
            Assert.That(WorldValidationScopeDiscovery.TryParseCellToken("-1,2", "district-01", out string districtId,
                out Vector2Int cell, out string failure), Is.True, failure);
            Assert.That(districtId, Is.EqualTo("district-01"));
            Assert.That(cell, Is.EqualTo(new Vector2Int(-1, 2)));
        }

        [Test]
        public void ExitPoliciesRejectTheirConfiguredNonSuccessStates()
        {
            WorldValidationRuleDescriptor descriptor = Descriptor();
            var blocking = new WorldValidationReport
            {
                results = new[] { WorldValidationResult.Failed(descriptor, "E", "Error", "bad") }
            };
            blocking.NormalizeAndSummarize();
            Assert.That(WorldValidationBatch.ExitCode(blocking, WorldValidationExitPolicy.NoBlockers), Is.EqualTo(1));

            var incomplete = new WorldValidationReport
            {
                results = new[] { WorldValidationResult.NotEvaluated(descriptor, "SKIP", "not run") }
            };
            incomplete.NormalizeAndSummarize();
            Assert.That(WorldValidationBatch.ExitCode(incomplete, WorldValidationExitPolicy.AllRequestedRulesEvaluated), Is.EqualTo(1));
            Assert.That(WorldValidationBatch.ExitCode(incomplete, WorldValidationExitPolicy.NoBlockers), Is.EqualTo(0));

            var registrationFailure = new WorldValidationReport
            {
                registrationErrors = new[] { "Provider assembly failed to load." }
            };
            Assert.That(WorldValidationBatch.ExitCode(registrationFailure, WorldValidationExitPolicy.NoErrors), Is.EqualTo(1));
            Assert.That(WorldValidationBatch.ExitCode(registrationFailure, WorldValidationExitPolicy.AllRequestedRulesEvaluated), Is.EqualTo(1));
            Assert.That(WorldValidationBatch.ExitCode(registrationFailure, WorldValidationExitPolicy.NoBlockers), Is.EqualTo(1));
        }

        [Test]
        public void IncrementalScopeReportsOmittedInputsAsPartial()
        {
            var snapshot = new WorldValidationScopeSnapshot(WorldValidationScopeKind.ChangedAssets);
            snapshot.AddAsset("Assets/World/Changed.asset");
            snapshot.AddAsset("Assets/World/Unchanged.asset");
            snapshot.RetainChanged(new[] { "Assets/World/Changed.asset" });
            snapshot.FinalizeSnapshot();

            Assert.That(snapshot.IsPartial, Is.True);
            Assert.That(snapshot.ToRecord().omittedAssets, Is.EqualTo(new[] { "Assets/World/Unchanged.asset" }));

            var complete = new WorldValidationScopeSnapshot(WorldValidationScopeKind.ChangedAssets);
            complete.AddAsset("Assets/World/Changed.asset");
            complete.RetainChanged(new[] { "Assets/World/Changed.asset" });
            complete.FinalizeSnapshot();
            Assert.That(snapshot.Fingerprint, Is.Not.EqualTo(complete.Fingerprint));
        }

        [Test]
        public void ReportSerializationIsStableForTheSameNormalizedData()
        {
            WorldValidationRuleDescriptor descriptor = Descriptor();
            var report = new WorldValidationReport
            {
                runId = "stable",
                requestedScope = WorldValidationScopeKind.Project,
                results = new[]
                {
                    WorldValidationResult.Passed(descriptor, "clean")
                }
            };
            string first = WorldValidationReportSerializer.ToJson(report);
            string second = WorldValidationReportSerializer.ToJson(report);
            Assert.That(first, Is.EqualTo(second));
            StringAssert.Contains("World Validation Report", WorldValidationReportSerializer.ToMarkdown(report));
        }
    }
}
