using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NfsMwRemaster.Driving;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Builds the editor projection from the same CareerDefinitionAsset that
    /// runtime code compiles. This module owns no progression state and never
    /// writes career, wallet or save data.
    /// </summary>
    public static class CareerGraphVisualizerModel
    {
        private const int ExternalAssetBudget = 5000;

        public static CareerGraphProjection Build(CareerDefinitionAsset asset)
        {
            var projection = new CareerGraphProjection { Asset = asset };
            if (asset == null)
            {
                projection.Findings.Add(new CareerGraphFinding(
                    "CAREER_ASSET_MISSING",
                    CareerGraphFindingSeverity.Error,
                    "No career definition selected",
                    "Select a CareerDefinitionAsset before building a progression projection."));
                return projection;
            }

            projection.AssetPath = AssetDatabase.GetAssetPath(asset) ?? string.Empty;
            projection.AssetGuid = string.IsNullOrEmpty(projection.AssetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(projection.AssetPath);

            try
            {
                projection.Source = asset.Definition();
                if (projection.Source == null)
                {
                    throw new ArgumentException("Career definition source is null.");
                }

                string sourceJson = JsonUtility.ToJson(projection.Source);
                projection.SourceFingerprint = Hash128.Compute(sourceJson).ToString();
            }
            catch (Exception exception)
            {
                projection.CompileError = exception.Message;
                projection.Findings.Add(new CareerGraphFinding(
                    "CAREER_SOURCE_READ",
                    CareerGraphFindingSeverity.Error,
                    "Career source could not be read",
                    exception.Message,
                    sourcePath: projection.AssetPath));
                return projection;
            }

            IndexSource(projection);
            ProjectRequirements(projection);
            TryCompile(projection);
            AddExternalReferences(projection);
            Analyze(projection);
            return projection;
        }

        public static CareerGraphComparison Compare(
            CareerGraphProjection left,
            CareerGraphProjection right)
        {
            var comparison = new CareerGraphComparison
            {
                LeftFingerprint = left == null ? string.Empty : left.SourceFingerprint,
                RightFingerprint = right == null ? string.Empty : right.SourceFingerprint
            };
            if (left == null || right == null || left.Source == null || right.Source == null)
            {
                return comparison;
            }

            var leftIds = new HashSet<string>(
                left.ContentById.Keys,
                StringComparer.Ordinal);
            var rightIds = new HashSet<string>(
                right.ContentById.Keys,
                StringComparer.Ordinal);
            comparison.Added.AddRange(rightIds.Except(leftIds, StringComparer.Ordinal).OrderBy(value => value));
            comparison.Removed.AddRange(leftIds.Except(rightIds, StringComparer.Ordinal).OrderBy(value => value));
            foreach (string id in leftIds.Intersect(rightIds, StringComparer.Ordinal).OrderBy(value => value))
            {
                CareerContentDefinition a = left.ContentById[id];
                CareerContentDefinition b = right.ContentById[id];
                if (JsonUtility.ToJson(a.requirement) != JsonUtility.ToJson(b.requirement)
                    || a.kind != b.kind
                    || a.tease != b.tease
                    || !string.Equals(a.name, b.name, StringComparison.Ordinal))
                {
                    comparison.ChangedRequirements.Add(id);
                }
            }

            var leftExternal = new HashSet<string>(
                left.ExternalReferences.Select(reference => reference.Kind + "|" + reference.Id + "|" + reference.AssetPath),
                StringComparer.Ordinal);
            var rightExternal = new HashSet<string>(
                right.ExternalReferences.Select(reference => reference.Kind + "|" + reference.Id + "|" + reference.AssetPath),
                StringComparer.Ordinal);
            comparison.ChangedExternalLinks.AddRange(
                leftExternal.Except(rightExternal, StringComparer.Ordinal).OrderBy(value => value));
            comparison.ChangedExternalLinks.AddRange(
                rightExternal.Except(leftExternal, StringComparer.Ordinal).OrderBy(value => value));
            return comparison;
        }

        private static void IndexSource(CareerGraphProjection projection)
        {
            CareerGraphDefinition source = projection.Source;
            if (source.content == null)
            {
                projection.Findings.Add(new CareerGraphFinding(
                    "CAREER_CONTENT_NULL",
                    CareerGraphFindingSeverity.Error,
                    "Career content collection is null",
                    "The authored CareerDefinition must contain a non-null content collection.",
                    sourcePath: projection.AssetPath));
            }
            else
            {
                for (int i = 0; i < source.content.Length; i++)
                {
                    CareerContentDefinition content = source.content[i];
                    if (content == null)
                    {
                        projection.Findings.Add(new CareerGraphFinding(
                            "CAREER_CONTENT_NULL_ENTRY",
                            CareerGraphFindingSeverity.Error,
                            "Career contains a null content entry",
                            "Remove the null entry or author a typed content definition.",
                            sourcePath: projection.AssetPath));
                        continue;
                    }

                    if (!projection.ContentById.TryAdd(content.id ?? string.Empty, content))
                    {
                        projection.Findings.Add(new CareerGraphFinding(
                            "CAREER_DUPLICATE_ID",
                            CareerGraphFindingSeverity.Error,
                            "Duplicate career content ID",
                            "Stable IDs must be unique; references cannot be resolved deterministically.",
                            content.id,
                            projection.AssetPath,
                            CareerGraphProjection.ContentKey(content.id)));
                        continue;
                    }

                    string key = CareerGraphProjection.ContentKey(content.id);
                    var node = new CareerGraphNodeModel
                    {
                        Key = key,
                        Id = content.id ?? string.Empty,
                        Label = string.IsNullOrWhiteSpace(content.name) ? content.id : content.name,
                        TierLabel = TierLabel(content.id),
                        ProjectedKind = CareerGraphProjectedNodeKind.Content,
                        ContentKind = content.kind,
                        Teased = content.tease,
                        ContentIndex = i,
                        DefaultPosition = DefaultPosition(projection.Nodes.Count, content.kind),
                        Position = DefaultPosition(projection.Nodes.Count, content.kind),
                        SourcePath = projection.AssetPath,
                        SourceGuid = projection.AssetGuid
                    };
                    AddNode(projection, node);
                }
            }

            if (source.groups == null)
            {
                projection.Findings.Add(new CareerGraphFinding(
                    "CAREER_GROUPS_NULL",
                    CareerGraphFindingSeverity.Error,
                    "Career event-group collection is null",
                    "The authored CareerDefinition must contain a non-null groups collection.",
                    sourcePath: projection.AssetPath));
            }
            else
            {
                for (int i = 0; i < source.groups.Length; i++)
                {
                    CareerEventGroupDefinition group = source.groups[i];
                    if (group == null)
                    {
                        projection.Findings.Add(new CareerGraphFinding(
                            "CAREER_GROUP_NULL_ENTRY",
                            CareerGraphFindingSeverity.Error,
                            "Career contains a null event group",
                            "Remove the null group or author a typed event group.",
                            sourcePath: projection.AssetPath));
                        continue;
                    }

                    if (!projection.GroupsById.TryAdd(group.id ?? string.Empty, group))
                    {
                        projection.Findings.Add(new CareerGraphFinding(
                            "CAREER_DUPLICATE_GROUP_ID",
                            CareerGraphFindingSeverity.Error,
                            "Duplicate career event-group ID",
                            "Stable group IDs must be unique.",
                            group.id,
                            projection.AssetPath,
                            CareerGraphProjection.GroupKey(group.id)));
                        continue;
                    }

                    string groupKey = CareerGraphProjection.GroupKey(group.id);
                    var node = new CareerGraphNodeModel
                    {
                        Key = groupKey,
                        Id = group.id ?? string.Empty,
                        Label = "Event group / " + (group.id ?? string.Empty),
                        TierLabel = "Event groups",
                        ProjectedKind = CareerGraphProjectedNodeKind.EventGroup,
                        GroupIndex = i,
                        DefaultPosition = new Vector2(80f + (i % 4) * 300f, 760f + (i / 4) * 190f),
                        Position = new Vector2(80f + (i % 4) * 300f, 760f + (i / 4) * 190f),
                        SourcePath = projection.AssetPath,
                        SourceGuid = projection.AssetGuid
                    };
                    AddNode(projection, node);

                    if (group.eventIds == null)
                    {
                        projection.Findings.Add(new CareerGraphFinding(
                            "CAREER_GROUP_MEMBERS_NULL",
                            CareerGraphFindingSeverity.Error,
                            "Event group has no member collection",
                            "Event group membership must be explicitly authored.",
                            group.id,
                            projection.AssetPath,
                            groupKey));
                        continue;
                    }

                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string eventId in group.eventIds)
                    {
                        if (!seen.Add(eventId ?? string.Empty))
                        {
                            projection.Findings.Add(new CareerGraphFinding(
                                "CAREER_GROUP_DUPLICATE_MEMBER",
                                CareerGraphFindingSeverity.Error,
                                "Event group repeats a member",
                                "Distinct-event requirements count each event ID once.",
                                group.id,
                                projection.AssetPath,
                                groupKey));
                            continue;
                        }

                        string memberKey = CareerGraphProjection.ContentKey(eventId);
                        if (!projection.NodesByKey.ContainsKey(memberKey))
                        {
                            memberKey = AddMissingContentNode(projection, eventId, groupKey);
                        }
                        AddEdge(projection, memberKey, groupKey, CareerGraphRelationKind.GroupMember,
                            "member of group", "group." + group.id, false);
                    }
                }
            }
        }

        private static void ProjectRequirements(CareerGraphProjection projection)
        {
            foreach (CareerContentDefinition content in projection.ContentById.Values)
            {
                string key = CareerGraphProjection.ContentKey(content.id);
                CareerGraphRequirementModel tree = ProjectRequirement(
                    projection,
                    content.requirement,
                    key,
                    "requirement",
                    false);
                projection.RequirementsByNodeKey[key] = tree;
            }
        }

        private static CareerGraphRequirementModel ProjectRequirement(
            CareerGraphProjection projection,
            CareerRequirementDefinition source,
            string targetKey,
            string path,
            bool negative)
        {
            if (source == null)
            {
                projection.Findings.Add(new CareerGraphFinding(
                    "CAREER_REQUIREMENT_NULL",
                    CareerGraphFindingSeverity.Error,
                    "Content has no requirement expression",
                    "Author an explicit ALL expression for an unconditional node or a typed requirement tree.",
                    targetKey,
                    projection.AssetPath,
                    targetKey));
                return new CareerGraphRequirementModel
                {
                    Kind = CareerRequirementKind.All,
                    Description = "Missing requirement"
                };
            }

            var result = new CareerGraphRequirementModel
            {
                Kind = source.kind,
                Fact = source.fact,
                SubjectId = source.subjectId ?? string.Empty,
                Description = string.IsNullOrWhiteSpace(source.description)
                    ? source.kind.ToString()
                    : source.description,
                Required = source.required,
                Negative = negative
            };
            CareerRequirementDefinition[] children = source.children ?? Array.Empty<CareerRequirementDefinition>();
            if (source.kind == CareerRequirementKind.Fact)
            {
                string fromKey = ResolveFactNode(projection, source.fact, source.subjectId, targetKey);
                CareerGraphRelationKind relation = negative
                    ? CareerGraphRelationKind.RequiresNot
                    : CareerGraphRelationKind.Requires;
                AddEdge(projection, fromKey, targetKey, relation, FactLabel(source), path, negative);
                return result;
            }

            bool childNegative = source.kind == CareerRequirementKind.Not ? !negative : negative;
            for (int i = 0; i < children.Length; i++)
            {
                string childPath = path + "/" + source.kind.ToString().ToLowerInvariant() + "[" + i + "]";
                CareerGraphRequirementModel child = ProjectRequirement(
                    projection,
                    children[i],
                    targetKey,
                    childPath,
                    childNegative);
                result.Children.Add(child);

                if (children[i] != null && children[i].kind == CareerRequirementKind.Fact
                    && source.kind == CareerRequirementKind.Any && !childNegative)
                {
                    string fromKey = ResolveFactNode(projection, children[i].fact, children[i].subjectId, targetKey);
                    UpdateRelation(projection, fromKey, targetKey, CareerGraphRelationKind.RequiresAny, childPath);
                }
            }
            return result;
        }

        private static string ResolveFactNode(
            CareerGraphProjection projection,
            CareerFactKind fact,
            string subjectId,
            string targetKey)
        {
            string subject = subjectId ?? string.Empty;
            if (fact == CareerFactKind.EventGroupWins)
            {
                if (projection.GroupsById.ContainsKey(subject))
                {
                    return CareerGraphProjection.GroupKey(subject);
                }

                return AddMissingFactNode(projection, fact, subject, targetKey);
            }

            bool keyed = fact >= CareerFactKind.EventCompleted;
            if (keyed && projection.ContentById.ContainsKey(subject))
            {
                return CareerGraphProjection.ContentKey(subject);
            }

            if (keyed)
            {
                return AddMissingFactNode(projection, fact, subject, targetKey);
            }

            string key = CareerGraphProjection.FactKey(fact, subject);
            if (!projection.NodesByKey.ContainsKey(key))
            {
                AddNode(projection, new CareerGraphNodeModel
                {
                    Key = key,
                    Id = subject,
                    Label = FactLabel(fact, subject),
                    TierLabel = "Runtime facts",
                    ProjectedKind = CareerGraphProjectedNodeKind.Fact,
                    DefaultPosition = new Vector2(1110f, 100f + projection.Nodes.Count * 34f),
                    Position = new Vector2(1110f, 100f + projection.Nodes.Count * 34f),
                    SourcePath = projection.AssetPath,
                    SourceGuid = projection.AssetGuid
                });
            }
            return key;
        }

        private static string AddMissingFactNode(
            CareerGraphProjection projection,
            CareerFactKind fact,
            string subject,
            string targetKey)
        {
            string key = CareerGraphProjection.MissingKey(fact, subject);
            if (!projection.NodesByKey.ContainsKey(key))
            {
                AddNode(projection, new CareerGraphNodeModel
                {
                    Key = key,
                    Id = subject,
                    Label = "Missing / " + FactLabel(fact, subject),
                    TierLabel = "Missing references",
                    ProjectedKind = CareerGraphProjectedNodeKind.MissingReference,
                    Missing = true,
                    DefaultPosition = new Vector2(1110f, 100f + projection.Nodes.Count * 34f),
                    Position = new Vector2(1110f, 100f + projection.Nodes.Count * 34f),
                    SourcePath = projection.AssetPath,
                    SourceGuid = projection.AssetGuid
                });
                projection.Findings.Add(new CareerGraphFinding(
                    "CAREER_MISSING_REFERENCE",
                    CareerGraphFindingSeverity.Error,
                    "Requirement references missing content",
                    "The requirement cannot be followed to an authored content or event group ID.",
                    subject,
                    projection.AssetPath,
                    targetKey));
            }
            return key;
        }

        private static string AddMissingContentNode(
            CareerGraphProjection projection,
            string eventId,
            string ownerKey)
        {
            string key = CareerGraphProjection.ContentKey(eventId);
            if (projection.NodesByKey.ContainsKey(key))
            {
                return key;
            }

            AddNode(projection, new CareerGraphNodeModel
            {
                Key = key,
                Id = eventId ?? string.Empty,
                Label = "Missing / " + (eventId ?? string.Empty),
                TierLabel = "Missing references",
                ProjectedKind = CareerGraphProjectedNodeKind.MissingReference,
                Missing = true,
                DefaultPosition = new Vector2(1110f, 100f + projection.Nodes.Count * 34f),
                Position = new Vector2(1110f, 100f + projection.Nodes.Count * 34f),
                SourcePath = projection.AssetPath,
                SourceGuid = projection.AssetGuid
            });
            projection.Findings.Add(new CareerGraphFinding(
                "CAREER_GROUP_MISSING_EVENT",
                CareerGraphFindingSeverity.Error,
                "Event group references missing event",
                "An event group must contain only typed Career Event content IDs.",
                eventId,
                projection.AssetPath,
                ownerKey));
            return key;
        }

        private static void TryCompile(CareerGraphProjection projection)
        {
            try
            {
                projection.Compiled = projection.Asset.Compile();
                projection.CompileError = string.Empty;
            }
            catch (Exception exception)
            {
                projection.CompileError = exception.Message;
                projection.Findings.Add(new CareerGraphFinding(
                    "CAREER_COMPILE",
                    CareerGraphFindingSeverity.Error,
                    "Career definition does not compile",
                    exception.Message,
                    sourcePath: projection.AssetPath));
            }
        }

        private static void AddExternalReferences(CareerGraphProjection projection)
        {
            IReadOnlyList<WorldActivityDefinition> activities = FindAssets<WorldActivityDefinition>();
            IReadOnlyList<MissionDefinitionAsset> missions = FindAssets<MissionDefinitionAsset>();
            IReadOnlyList<EconomyDefinitionAsset> economies = FindAssets<EconomyDefinitionAsset>();

            foreach (WorldActivityDefinition activity in activities)
            {
                if (activity == null || string.IsNullOrEmpty(activity.id)
                    || !projection.ContentById.ContainsKey(activity.id))
                {
                    continue;
                }

                CareerContentDefinition content = projection.ContentById[activity.id];
                if (content.kind != CareerContentKind.Event)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(activity);
                string key = "activity:" + path + ":" + activity.id;
                AddExternalNode(projection, key, activity.id, activity.displayName, "Activity", path,
                    CareerGraphProjectedNodeKind.ExternalActivity, content.kind, content.id);
                AddEdge(projection, key, CareerGraphProjection.ContentKey(activity.id),
                    CareerGraphRelationKind.AuthoringReference, "activity definition", "external.activity", false);
                projection.ExternalReferences.Add(new CareerGraphExternalReference
                {
                    Kind = "WorldActivityDefinition",
                    Id = activity.id,
                    Label = activity.displayName,
                    AssetPath = path,
                    AssetGuid = AssetDatabase.AssetPathToGUID(path),
                    Status = "Career event ID matched",
                    Asset = activity
                });
                try
                {
                    CareerRequirementDefinition availability = activity.availability;
                    if (availability != null)
                    {
                        ProjectExternalRequirement(projection, availability, key, "activity.availability", false);
                    }
                }
                catch (Exception exception)
                {
                    projection.Findings.Add(new CareerGraphFinding(
                        "CAREER_ACTIVITY_REQUIREMENT",
                        CareerGraphFindingSeverity.Warning,
                        "Activity availability could not be projected",
                        exception.Message,
                        activity.id,
                        path,
                        key));
                }
            }

            foreach (MissionDefinitionAsset mission in missions)
            {
                if (mission == null)
                {
                    continue;
                }

                try
                {
                    MissionDefinition definition = mission.Compile().Definition();
                    if (definition == null || string.IsNullOrEmpty(definition.id)
                        || !projection.ContentById.ContainsKey(definition.id))
                    {
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(mission);
                    string key = "mission:" + path + ":" + definition.id;
                    AddExternalNode(projection, key, definition.id, definition.title, "Mission", path,
                        CareerGraphProjectedNodeKind.ExternalMission,
                        projection.ContentById[definition.id].kind,
                        definition.id);
                    AddEdge(projection, key, CareerGraphProjection.ContentKey(definition.id),
                        CareerGraphRelationKind.AuthoringReference, "mission definition", "external.mission", false);
                    projection.ExternalReferences.Add(new CareerGraphExternalReference
                    {
                        Kind = "MissionDefinitionAsset",
                        Id = definition.id,
                        Label = definition.title,
                        AssetPath = path,
                        AssetGuid = AssetDatabase.AssetPathToGUID(path),
                        Status = "Mission ID matched",
                        Asset = mission
                    });
                    if (definition.availability != null)
                    {
                        ProjectExternalRequirement(projection, definition.availability, key, "mission.availability", false);
                    }
                }
                catch (Exception exception)
                {
                    projection.ExternalReferences.Add(new CareerGraphExternalReference
                    {
                        Kind = "MissionDefinitionAsset",
                        Id = string.Empty,
                        Label = mission.name,
                        AssetPath = AssetDatabase.GetAssetPath(mission),
                        AssetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mission)),
                        Status = "Mission failed to compile: " + exception.Message,
                        Asset = mission
                    });
                }
            }

            var economyMatches = new Dictionary<string, List<CareerGraphEconomyMatch>>(StringComparer.Ordinal);
            foreach (EconomyDefinitionAsset economy in economies)
            {
                if (economy == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(economy);
                try
                {
                    EconomyDefinition definition = economy.Definition();
                    foreach (EconomyItemDefinition item in definition.items ?? Array.Empty<EconomyItemDefinition>())
                    {
                        if (item == null || !projection.ContentById.ContainsKey(item.id))
                        {
                            continue;
                        }

                        string itemKey = "economy:" + path + ":" + item.id;
                        AddExternalNode(projection, itemKey, item.id, item.name, "Economy item", path,
                            CareerGraphProjectedNodeKind.EconomyItem,
                            projection.ContentById[item.id].kind,
                            item.id);
                        AddEdge(projection, itemKey, CareerGraphProjection.ContentKey(item.id),
                            CareerGraphRelationKind.PriceReference, "read-only catalog price", "external.economy", false);
                        var match = new CareerGraphEconomyMatch
                        {
                            ItemId = item.id,
                            Name = item.name,
                            Kind = item.kind,
                            Price = Mathf.Max(0, item.price),
                            InitiallyUnlocked = item.initiallyUnlocked,
                            AssetPath = path,
                            CompatibleVehicleIds = item.compatibleVehicleIds ?? Array.Empty<string>(),
                            PerformanceIds = item.performanceIds ?? Array.Empty<string>(),
                            CustomizationIds = item.customizationIds ?? Array.Empty<string>()
                        };
                        if (!economyMatches.TryGetValue(item.id, out List<CareerGraphEconomyMatch> matches))
                        {
                            economyMatches.Add(item.id, matches = new List<CareerGraphEconomyMatch>());
                        }
                        matches.Add(match);
                        projection.ExternalReferences.Add(new CareerGraphExternalReference
                        {
                            Kind = "EconomyItemDefinition",
                            Id = item.id,
                            Label = item.name,
                            AssetPath = path,
                            AssetGuid = AssetDatabase.AssetPathToGUID(path),
                            Status = "Career content ID matched; price is read-only",
                            Asset = economy
                        });
                    }
                }
                catch (Exception exception)
                {
                    projection.Findings.Add(new CareerGraphFinding(
                        "CAREER_ECONOMY_READ",
                        CareerGraphFindingSeverity.Warning,
                        "Economy catalog could not be inspected",
                        exception.Message,
                        sourcePath: path));
                }
            }

            foreach (List<CareerGraphEconomyMatch> matches in economyMatches.Values)
            {
                projection.EconomyMatches.AddRange(matches);
            }

            foreach (CareerContentDefinition content in projection.ContentById.Values)
            {
                if (content.kind != CareerContentKind.Event)
                {
                    continue;
                }

                bool matchedActivity = projection.ExternalReferences.Any(reference =>
                    reference.Kind == "WorldActivityDefinition"
                    && string.Equals(reference.Id, content.id, StringComparison.Ordinal));
                if (!matchedActivity)
                {
                    projection.ExternalReferences.Add(new CareerGraphExternalReference
                    {
                        Kind = "WorldActivityDefinition",
                        Id = content.id,
                        Label = content.name,
                        Status = "No matching WorldActivityDefinition found; event placement is not evaluated by this tool.",
                        AssetPath = string.Empty
                    });
                }
            }
        }

        private static void ProjectExternalRequirement(
            CareerGraphProjection projection,
            CareerRequirementDefinition requirement,
            string targetKey,
            string path,
            bool negative)
        {
            ProjectRequirement(projection, requirement, targetKey, path, negative);
        }

        private static void Analyze(CareerGraphProjection projection)
        {
            AnalyzeGroupCounts(projection);
            AnalyzeSimpleContradictions(projection);
            AnalyzeCycles(projection);
            AnalyzeOrphans(projection);
            if (projection.ContentById.Values.Any(content =>
                content.kind == CareerContentKind.Rival
                || content.kind == CareerContentKind.Vehicle
                || content.kind == CareerContentKind.Upgrade
                || content.kind == CareerContentKind.Milestone))
            {
                projection.Findings.Add(new CareerGraphFinding(
                    "CAREER_EFFECTS_UNSUPPORTED",
                    CareerGraphFindingSeverity.Unsupported,
                    "Reward/effect projection is not available in the current career schema",
                    "This workbench can inspect requirements and economy catalog matches, but no authored Career reward/effect contract is currently exposed. It will not infer grants, ownership or settlement consequences.",
                    assumptionDependent: true));
            }
        }

        private static void AnalyzeGroupCounts(CareerGraphProjection projection)
        {
            foreach (CareerContentDefinition content in projection.ContentById.Values)
            {
                VisitRequirements(content.requirement, requirement =>
                {
                    if (requirement.kind != CareerRequirementKind.Fact
                        || requirement.fact != CareerFactKind.EventGroupWins)
                    {
                        return;
                    }

                    if (projection.GroupsById.TryGetValue(requirement.subjectId ?? string.Empty, out CareerEventGroupDefinition group)
                        && (group.eventIds == null || requirement.required > group.eventIds.Length))
                    {
                        projection.Findings.Add(new CareerGraphFinding(
                            "CAREER_GROUP_COUNT_UNREACHABLE",
                            CareerGraphFindingSeverity.Error,
                            "Distinct-event requirement exceeds its event group",
                            "The requirement asks for more distinct wins than the authored eligible group can provide; repeating one event must not satisfy it.",
                            content.id,
                            projection.AssetPath,
                            CareerGraphProjection.ContentKey(content.id)));
                    }
                });
            }
        }

        private static void AnalyzeSimpleContradictions(CareerGraphProjection projection)
        {
            foreach (CareerContentDefinition content in projection.ContentById.Values)
            {
                FindContradiction(content.requirement, content.id, projection);
            }
        }

        private static void FindContradiction(
            CareerRequirementDefinition requirement,
            string contentId,
            CareerGraphProjection projection)
        {
            if (requirement == null)
            {
                return;
            }

            if (requirement.kind == CareerRequirementKind.All)
            {
                var positives = new HashSet<string>(StringComparer.Ordinal);
                var negatives = new HashSet<string>(StringComparer.Ordinal);
                foreach (CareerRequirementDefinition child in requirement.children ?? Array.Empty<CareerRequirementDefinition>())
                {
                    CollectSimpleFacts(child, false, positives, negatives);
                }
                foreach (string key in positives.Intersect(negatives, StringComparer.Ordinal))
                {
                    projection.Findings.Add(new CareerGraphFinding(
                        "CAREER_CONTRADICTORY_REQUIREMENT",
                        CareerGraphFindingSeverity.Warning,
                        "ALL requirement contains a positive and negative form of the same fact",
                        "This is unsatisfiable when both forms refer to the same value. Confirm that the contradiction is intentional.",
                        contentId,
                        projection.AssetPath,
                        CareerGraphProjection.ContentKey(contentId),
                        assumptionDependent: true));
                }
            }

            foreach (CareerRequirementDefinition child in requirement.children ?? Array.Empty<CareerRequirementDefinition>())
            {
                FindContradiction(child, contentId, projection);
            }
        }

        private static void CollectSimpleFacts(
            CareerRequirementDefinition requirement,
            bool negative,
            HashSet<string> positives,
            HashSet<string> negatives)
        {
            if (requirement == null)
            {
                return;
            }

            if (requirement.kind == CareerRequirementKind.Fact)
            {
                string key = requirement.fact + "|" + (requirement.subjectId ?? string.Empty) + "|" + requirement.required;
                (negative ? negatives : positives).Add(key);
                return;
            }

            bool childNegative = requirement.kind == CareerRequirementKind.Not ? !negative : negative;
            foreach (CareerRequirementDefinition child in requirement.children ?? Array.Empty<CareerRequirementDefinition>())
            {
                CollectSimpleFacts(child, childNegative, positives, negatives);
            }
        }

        private static void AnalyzeCycles(CareerGraphProjection projection)
        {
            var adjacency = new Dictionary<string, List<CareerGraphEdgeModel>>(StringComparer.Ordinal);
            foreach (CareerGraphEdgeModel edge in projection.Edges.Where(IsDependencyEdge))
            {
                if (!projection.NodesByKey.ContainsKey(edge.FromKey)
                    || !projection.NodesByKey.ContainsKey(edge.ToKey))
                {
                    continue;
                }
                if (!adjacency.TryGetValue(edge.FromKey, out List<CareerGraphEdgeModel> edges))
                {
                    adjacency.Add(edge.FromKey, edges = new List<CareerGraphEdgeModel>());
                }
                edges.Add(edge);
            }

            var colors = new Dictionary<string, int>(StringComparer.Ordinal);
            var stack = new List<string>();
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in adjacency.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                FindCycles(key, adjacency, colors, stack, reported, projection);
            }
        }

        private static void FindCycles(
            string key,
            Dictionary<string, List<CareerGraphEdgeModel>> adjacency,
            Dictionary<string, int> colors,
            List<string> stack,
            HashSet<string> reported,
            CareerGraphProjection projection)
        {
            if (colors.TryGetValue(key, out int color))
            {
                if (color == 2)
                {
                    return;
                }

                int start = stack.IndexOf(key);
                if (start >= 0)
                {
                    string cycle = string.Join(" -> ", stack.Skip(start).Concat(new[] { key }));
                    string cycleId = string.Join("|", stack.Skip(start).OrderBy(value => value, StringComparer.Ordinal));
                    if (reported.Add(cycleId))
                    {
                        bool negative = false;
                        for (int i = start; i < stack.Count; i++)
                        {
                            string from = stack[i];
                            string to = i + 1 < stack.Count ? stack[i + 1] : key;
                            if (adjacency.TryGetValue(from, out List<CareerGraphEdgeModel> edges))
                            {
                                CareerGraphEdgeModel edge = edges.FirstOrDefault(item => item.ToKey == to);
                                negative |= edge != null && edge.Negative;
                            }
                        }
                        projection.Findings.Add(new CareerGraphFinding(
                            "CAREER_DEPENDENCY_CYCLE",
                            negative ? CareerGraphFindingSeverity.Warning : CareerGraphFindingSeverity.Error,
                            negative ? "Dependency cycle includes NOT polarity" : "Positive career dependency cycle",
                            (negative
                                ? "The cycle is conservatively flagged; NOT polarity can make static reachability non-trivial. Runtime/scenario evidence is required."
                                : "A positive prerequisite cycle has no authored entry point and can strand progression.")
                            + " Cycle: " + cycle,
                            nodeKey: key,
                            assumptionDependent: negative));
                    }
                }
                return;
            }

            colors[key] = 1;
            stack.Add(key);
            if (adjacency.TryGetValue(key, out List<CareerGraphEdgeModel> edgesForKey))
            {
                foreach (CareerGraphEdgeModel edge in edgesForKey)
                {
                    FindCycles(edge.ToKey, adjacency, colors, stack, reported, projection);
                }
            }
            stack.RemoveAt(stack.Count - 1);
            colors[key] = 2;
        }

        private static void AnalyzeOrphans(CareerGraphProjection projection)
        {
            foreach (CareerGraphNodeModel node in projection.Nodes.Where(node => node.IsContent))
            {
                bool connected = projection.Edges.Any(edge =>
                    string.Equals(edge.FromKey, node.Key, StringComparison.Ordinal)
                    || string.Equals(edge.ToKey, node.Key, StringComparison.Ordinal));
                if (!connected)
                {
                    projection.Findings.Add(new CareerGraphFinding(
                        "CAREER_ORPHAN_CONTENT",
                        CareerGraphFindingSeverity.Warning,
                        "Career content is disconnected",
                        "No requirement, group membership or projected source link reaches this content. Confirm it is an intentional root or author its progression relationship.",
                        node.Id,
                        projection.AssetPath,
                        node.Key,
                        assumptionDependent: true));
                }
            }
        }

        private static bool IsDependencyEdge(CareerGraphEdgeModel edge)
        {
            return edge.Relation == CareerGraphRelationKind.Requires
                || edge.Relation == CareerGraphRelationKind.RequiresAny
                || edge.Relation == CareerGraphRelationKind.RequiresNot;
        }

        private static void VisitRequirements(
            CareerRequirementDefinition requirement,
            Action<CareerRequirementDefinition> visitor)
        {
            if (requirement == null)
            {
                return;
            }

            visitor(requirement);
            foreach (CareerRequirementDefinition child in requirement.children ?? Array.Empty<CareerRequirementDefinition>())
            {
                VisitRequirements(child, visitor);
            }
        }

        private static void AddExternalNode(
            CareerGraphProjection projection,
            string key,
            string id,
            string label,
            string tier,
            string path,
            CareerGraphProjectedNodeKind kind,
            CareerContentKind contentKind,
            string sourceId)
        {
            if (projection.NodesByKey.ContainsKey(key))
            {
                return;
            }

            AddNode(projection, new CareerGraphNodeModel
            {
                Key = key,
                Id = id ?? string.Empty,
                Label = string.IsNullOrWhiteSpace(label) ? id : label,
                TierLabel = tier,
                ProjectedKind = kind,
                ContentKind = contentKind,
                DefaultPosition = new Vector2(80f + projection.Nodes.Count * 28f, 1120f),
                Position = new Vector2(80f + projection.Nodes.Count * 28f, 1120f),
                SourcePath = path ?? string.Empty,
                SourceGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path),
                Notes = sourceId ?? string.Empty
            });
        }

        private static void AddNode(CareerGraphProjection projection, CareerGraphNodeModel node)
        {
            if (node == null || string.IsNullOrEmpty(node.Key) || projection.NodesByKey.ContainsKey(node.Key))
            {
                return;
            }

            projection.Nodes.Add(node);
            projection.NodesByKey.Add(node.Key, node);
        }

        private static void AddEdge(
            CareerGraphProjection projection,
            string fromKey,
            string toKey,
            CareerGraphRelationKind relation,
            string label,
            string path,
            bool negative)
        {
            if (string.IsNullOrEmpty(fromKey) || string.IsNullOrEmpty(toKey))
            {
                return;
            }

            string key = fromKey + "|" + toKey + "|" + relation + "|" + (path ?? string.Empty);
            if (projection.Edges.Any(edge => string.Equals(edge.Key, key, StringComparison.Ordinal)))
            {
                return;
            }

            projection.Edges.Add(new CareerGraphEdgeModel
            {
                Key = key,
                FromKey = fromKey,
                ToKey = toKey,
                Relation = relation,
                Label = label ?? string.Empty,
                RequirementPath = path ?? string.Empty,
                Negative = negative
            });
        }

        private static void UpdateRelation(
            CareerGraphProjection projection,
            string fromKey,
            string toKey,
            CareerGraphRelationKind relation,
            string path)
        {
            CareerGraphEdgeModel edge = projection.Edges.FirstOrDefault(item =>
                item.FromKey == fromKey && item.ToKey == toKey && item.RequirementPath == path);
            if (edge != null)
            {
                edge.Relation = relation;
            }
        }

        private static Vector2 DefaultPosition(int index, CareerContentKind kind)
        {
            int kindIndex = (int)kind;
            int lane = kindIndex % 5;
            int row = Math.Max(0, index / 5);
            return new Vector2(70f + lane * 245f, 70f + row * 175f);
        }

        private static string TierLabel(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "Unscoped";
            }

            string[] parts = id.Split('.');
            if (parts.Length >= 2
                && (string.Equals(parts[0], "tier", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parts[0], "chapter", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parts[0], "blacklist", StringComparison.OrdinalIgnoreCase)))
            {
                return parts[0] + "." + parts[1];
            }

            return "Namespace: " + parts[0];
        }

        private static string FactLabel(CareerRequirementDefinition definition)
        {
            return FactLabel(definition.fact, definition.subjectId, definition.required);
        }

        private static string FactLabel(CareerFactKind fact, string subject, long required = 0)
        {
            string suffix = string.IsNullOrEmpty(subject) ? string.Empty : " / " + subject;
            if (fact == CareerFactKind.BlacklistRank)
            {
                return fact + suffix + " (1 is higher rank)";
            }

            if (fact == CareerFactKind.EventGroupWins || fact == CareerFactKind.RaceWins
                || fact == CareerFactKind.MilestoneCount || fact == CareerFactKind.Bounty
                || fact == CareerFactKind.Reputation)
            {
                return fact + suffix + " >= " + required;
            }

            return fact + suffix;
        }

        private static IReadOnlyList<T> FindAssets<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            int count = Math.Min(guids.Length, ExternalAssetBudget);
            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    result.Add(asset);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Isolated, synthetic profile state used only by the editor sandbox. It is
    /// copied on reset and has no storage adapter or connection to live saves.
    /// </summary>
    public sealed class CareerGraphSandbox
    {
        private readonly CareerGraph graph;
        private readonly CareerProfileData initial;
        private readonly Dictionary<string, long> overrides = new Dictionary<string, long>(StringComparer.Ordinal);

        public CareerProfileData Profile { get; private set; }
        public int Revision { get; private set; }
        public CareerGraphSandbox(CareerGraph configuredGraph)
        {
            graph = configuredGraph;
            initial = CareerProfileData.Create("career-visualizer-sandbox", "Synthetic designer profile");
            Reset();
        }

        public CareerSandboxFacts Facts => new CareerSandboxFacts(this);
        public IReadOnlyDictionary<string, long> Overrides => overrides;

        public void Reset()
        {
            Profile = JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(initial));
            Profile.Normalize();
            overrides.Clear();
            Revision++;
        }

        public bool SetCash(long value, out string failure)
        {
            if (value < 0 || value > int.MaxValue)
            {
                failure = "Synthetic cash must be between 0 and Int32.MaxValue.";
                return false;
            }
            Profile.wallet.balance = (int)value;
            Revision++;
            failure = string.Empty;
            return true;
        }

        public bool SetReputation(long value, out string failure)
        {
            if (value < 0)
            {
                failure = "Synthetic reputation cannot be negative.";
                return false;
            }
            Profile.economy.reputation = value;
            Revision++;
            failure = string.Empty;
            return true;
        }

        public bool SetBounty(long value, out string failure)
        {
            if (value < 0 || value > int.MaxValue)
            {
                failure = "Synthetic bounty must be between 0 and Int32.MaxValue.";
                return false;
            }
            Profile.bounty.totalBounty = (int)value;
            Revision++;
            failure = string.Empty;
            return true;
        }

        public bool SetFact(CareerFactKind kind, string subjectId, long value, out string failure)
        {
            if (value < 0)
            {
                failure = "Synthetic fact values cannot be negative.";
                return false;
            }
            if (kind == CareerFactKind.BlacklistRank && (value < 1 || value > 16))
            {
                failure = "Blacklist rank must be between 1 and 16.";
                return false;
            }
            overrides[FactKey(kind, subjectId)] = value;
            Revision++;
            failure = string.Empty;
            return true;
        }

        public bool CompleteNode(CareerGraphNodeModel node, out string failure)
        {
            if (node == null || !node.IsContent)
            {
                failure = "Select an authored career content node first.";
                return false;
            }

            switch (node.ContentKind)
            {
                case CareerContentKind.Event:
                    if (!Profile.freeRoam.completedEventIds.Contains(node.Id))
                    {
                        Profile.freeRoam.completedEventIds.Add(node.Id);
                        Profile.economy.firstWinEventIds.Add(node.Id);
                        Profile.statistics.racesWon = checked(Profile.statistics.racesWon + 1);
                        Profile.statistics.eventsCompleted = checked(Profile.statistics.eventsCompleted + 1);
                    }
                    break;
                case CareerContentKind.Milestone:
                    SetFact(CareerFactKind.MilestoneCompleted, node.Id, 1, out failure);
                    return string.IsNullOrEmpty(failure);
                case CareerContentKind.Rival:
                    SetFact(CareerFactKind.RivalDefeated, node.Id, 1, out failure);
                    return string.IsNullOrEmpty(failure);
                case CareerContentKind.Vehicle:
                    SetFact(CareerFactKind.VehicleUnlocked, node.Id, 1, out failure);
                    return string.IsNullOrEmpty(failure);
                case CareerContentKind.Upgrade:
                    SetFact(CareerFactKind.UpgradeUnlocked, node.Id, 1, out failure);
                    return string.IsNullOrEmpty(failure);
                case CareerContentKind.District:
                    SetFact(CareerFactKind.DistrictUnlocked, node.Id, 1, out failure);
                    return string.IsNullOrEmpty(failure);
                case CareerContentKind.Challenge:
                    SetFact(CareerFactKind.ChallengeCompleted, node.Id, 1, out failure);
                    return string.IsNullOrEmpty(failure);
                default:
                    failure = "This content kind has no synthetic completion command in the current profile contract.";
                    return false;
            }

            Revision++;
            failure = string.Empty;
            return true;
        }

        public bool GrantOwnership(CareerGraphNodeModel node, out string failure)
        {
            if (node == null || !node.IsContent)
            {
                failure = "Select an authored career content node first.";
                return false;
            }
            switch (node.ContentKind)
            {
                case CareerContentKind.Vehicle:
                    if (!Profile.store.ownedVehicleIds.Contains(node.Id)) Profile.store.ownedVehicleIds.Add(node.Id);
                    Profile.GetOrCreateVehicle(node.Id);
                    SetFact(CareerFactKind.VehicleOwned, node.Id, 1, out failure);
                    return string.IsNullOrEmpty(failure);
                case CareerContentKind.Upgrade:
                    if (!Profile.store.ownedProductIds.Contains(node.Id)) Profile.store.ownedProductIds.Add(node.Id);
                    SetFact(CareerFactKind.UpgradeUnlocked, node.Id, 1, out failure);
                    return string.IsNullOrEmpty(failure);
                case CareerContentKind.Customization:
                    if (!Profile.store.ownedProductIds.Contains(node.Id)) Profile.store.ownedProductIds.Add(node.Id);
                    Revision++;
                    failure = string.Empty;
                    return true;
                default:
                    failure = "Only vehicle, upgrade and customization content can be granted in the sandbox.";
                    return false;
            }
        }

        internal long Read(CareerFactKind kind, string subjectId)
        {
            string key = FactKey(kind, subjectId);
            if (overrides.TryGetValue(key, out long value))
            {
                return value;
            }

            string subject = subjectId ?? string.Empty;
            switch (kind)
            {
                case CareerFactKind.Reputation: return Math.Max(0, Profile.economy.reputation);
                case CareerFactKind.Bounty: return Math.Max(0, Profile.bounty.totalBounty);
                case CareerFactKind.BlacklistRank: return 16;
                case CareerFactKind.RaceWins: return Math.Max(0, Profile.statistics.racesWon);
                case CareerFactKind.EventCompleted:
                    return Profile.freeRoam.completedEventIds.Contains(subject) ? 1 : 0;
                case CareerFactKind.EventWon:
                    return Profile.economy.firstWinEventIds.Contains(subject)
                        || Profile.freeRoam.completedEventIds.Contains(subject) ? 1 : 0;
                case CareerFactKind.EventGroupWins:
                    if (graph == null) return 0;
                    try
                    {
                        return graph.GetEventGroup(subject).Count(id => Profile.economy.firstWinEventIds.Contains(id)
                            || Profile.freeRoam.completedEventIds.Contains(id));
                    }
                    catch (ArgumentException)
                    {
                        return 0;
                    }
                case CareerFactKind.VehicleUnlocked:
                    return Profile.store.ownedVehicleIds.Contains(subject)
                        || Profile.economy.unlocks.Any(grant => grant != null && grant.kind == EconomyItemKind.Vehicle && grant.itemId == subject)
                        ? 1 : 0;
                case CareerFactKind.VehicleOwned:
                    return Profile.store.ownedVehicleIds.Contains(subject) || Profile.FindVehicle(subject) != null ? 1 : 0;
                case CareerFactKind.UpgradeUnlocked:
                    return Profile.store.ownedProductIds.Contains(subject)
                        || Profile.economy.unlocks.Any(grant => grant != null && grant.kind == EconomyItemKind.Upgrade && grant.itemId == subject)
                        ? 1 : 0;
                case CareerFactKind.DistrictUnlocked:
                    return Profile.economy.unlocks.Any(grant => grant != null && grant.kind == EconomyItemKind.District && grant.itemId == subject)
                        ? 1 : 0;
                case CareerFactKind.MilestoneCount:
                case CareerFactKind.MilestoneCompleted:
                case CareerFactKind.RivalDefeated:
                case CareerFactKind.StoryState:
                case CareerFactKind.ChallengeCompleted:
                default:
                    return 0;
            }
        }

        internal bool IsKnown(CareerFactKind kind, string subjectId)
        {
            if (overrides.ContainsKey(FactKey(kind, subjectId)))
            {
                return true;
            }

            return kind == CareerFactKind.Reputation
                || kind == CareerFactKind.Bounty
                || kind == CareerFactKind.BlacklistRank
                || kind == CareerFactKind.RaceWins
                || kind == CareerFactKind.EventCompleted
                || kind == CareerFactKind.EventWon
                || kind == CareerFactKind.EventGroupWins
                || kind == CareerFactKind.VehicleUnlocked
                || kind == CareerFactKind.VehicleOwned
                || kind == CareerFactKind.UpgradeUnlocked
                || kind == CareerFactKind.DistrictUnlocked;
        }

        private static string FactKey(CareerFactKind kind, string subjectId)
            => kind + "|" + (subjectId ?? string.Empty);
    }

    public sealed class CareerSandboxFacts : ICareerFacts
    {
        private readonly CareerGraphSandbox sandbox;
        internal CareerSandboxFacts(CareerGraphSandbox configuredSandbox) { sandbox = configuredSandbox; }
        public long Read(CareerFactKind kind, string subjectId) => sandbox.Read(kind, subjectId);
        public bool IsKnown(CareerFactKind kind, string subjectId) => sandbox.IsKnown(kind, subjectId);
    }
}
