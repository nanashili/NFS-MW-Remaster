using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using NfsMwRemaster.Driving.Editor.Workspace;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    /// <summary>
    /// Dockable UI Toolkit dashboard. Domain rules stay in their owner modules;
    /// this window owns filtering, run control, evidence presentation and safe
    /// navigation only.
    /// </summary>
    public sealed class WorldValidationWindow : RacingFocusedWindow
    {
        protected override string ModuleId => "validation";
        [MenuItem("NFS MW Remaster/Validation/World Validation Dashboard")]
        public static void Open()=>GetWindow<WorldValidationWindow>("World Validation");
        [MenuItem("NFS MW Remaster/Validation/Create Default Policy")]
        private static void CreatePolicyMenu(){var policy=WorldValidationPolicy.CreateDefaultAsset();Selection.activeObject=policy;EditorGUIUtility.PingObject(policy);}
    }
    public sealed class WorldValidationView : RacingModuleView
    {
        private readonly List<WorldValidationResult> visibleResults = new List<WorldValidationResult>();
        private readonly List<string> historyPaths = new List<string>();
        private readonly List<string> tabs = new List<string> { "Dashboard", "Diagnostics", "Dependencies", "History", "Policy" };
        private VisualElement content;
        private Label statusLabel;
        private Label summaryLabel;
        private Label coverageLabel;
        private DropdownField scopeField;
        private DropdownField categoryField;
        private DropdownField statusField;
        private TextField searchField;
        private TextField explicitScenesField;
        private TextField districtIdField;
        private TextField cellIdsField;
        private Toggle incrementalToggle;
        private Toggle expensiveToggle;
        private Toggle infoToggle;
        private ListView diagnosticList;
        private ScrollView detailView;
        private int activeTab;
        private WorldValidationResult selectedResult;
        private WorldValidationReport report;
        private string contextObjectId;

        public WorldValidationView()
        {
            WorldValidationService.ReportChanged -= OnReportChanged;
            WorldValidationService.ReportChanged += OnReportChanged;
            report = WorldValidationService.LastReport;
            CreateGUI();
        }

        public override void Dispose()
        {
            WorldValidationService.ReportChanged -= OnReportChanged;
        }
        public override void SetContext(RacingEditingContext context)
        {
            contextObjectId=context.ResolveDocument()?context.Document.objectId:null;
            if(!string.IsNullOrEmpty(contextObjectId))activeTab=1;
            RebuildContent();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            BuildHeader();
            BuildTabs();
            content = new VisualElement { name = "world-validation-content" };
            content.style.flexGrow = 1;
            rootVisualElement.Add(content);
            RebuildContent();
        }

        private void BuildHeader()
        {
            var title = new Label("WORLD VALIDATION") { name = "world-validation-title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.marginLeft = 8;
            title.style.marginTop = 6;
            rootVisualElement.Add(title);

            var controls = new VisualElement { name = "world-validation-controls" };
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.flexWrap = Wrap.Wrap;
            controls.style.marginLeft = 6;
            controls.style.marginRight = 6;
            controls.style.marginTop = 4;

            scopeField = new DropdownField("Scope", Enum.GetNames(typeof(WorldValidationScopeKind)).ToList(), (int)WorldValidationScopeKind.OpenScenes);
            scopeField.style.minWidth = 170;
            categoryField = new DropdownField("Category", new[] { "All" }.Concat(Enum.GetNames(typeof(WorldValidationCategory))).ToList(), 0);
            categoryField.style.minWidth = 170;
            statusField = new DropdownField("Status", new[] { "All" }.Concat(Enum.GetNames(typeof(WorldValidationStatus))).ToList(), 0);
            statusField.style.minWidth = 135;
            categoryField.RegisterValueChangedCallback(_ =>
            {
                RefreshDiagnostics();
                BuildDetails();
            });
            statusField.RegisterValueChangedCallback(_ =>
            {
                RefreshDiagnostics();
                BuildDetails();
            });
            explicitScenesField = new TextField("Explicit scene paths") { tooltip = "Assets/NfsMw/Scenes/Tests/A.unity;Assets/NfsMw/Scenes/Tests/B.unity" };
            explicitScenesField.style.minWidth = 230;
            districtIdField = new TextField("District ID")
            {
                tooltip = "Optional. For DistrictCell scope, select a CityDistrict or provide its stable district ID."
            };
            districtIdField.style.minWidth = 170;
            cellIdsField = new TextField("Cells")
            {
                tooltip = "Optional x,z tokens separated by semicolons, for example 0,0;0,1. Prefix with districtId| for multiple districts."
            };
            cellIdsField.style.minWidth = 205;
            incrementalToggle = new Toggle("Incremental");
            expensiveToggle = new Toggle("Expensive");
            infoToggle = new Toggle("Info") { value = true };
            controls.Add(scopeField);
            controls.Add(categoryField);
            controls.Add(statusField);
            controls.Add(explicitScenesField);
            controls.Add(districtIdField);
            controls.Add(cellIdsField);
            controls.Add(incrementalToggle);
            controls.Add(expensiveToggle);
            controls.Add(infoToggle);
            controls.Add(new Button(StartRun) { text = "Run validation" });
            controls.Add(new Button(WorldValidationService.Cancel) { text = "Cancel" });
            controls.Add(new Button(CreatePolicy) { text = "Create policy" });
            controls.Add(new Button(OpenPolicy) { text = "Open policy" });
            rootVisualElement.Add(controls);

            statusLabel = new Label { name = "world-validation-status" };
            statusLabel.style.marginLeft = 8;
            statusLabel.style.marginBottom = 4;
            rootVisualElement.Add(statusLabel);
        }

        private void BuildTabs()
        {
            var row = new VisualElement { name = "world-validation-tabs" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginLeft = 6;
            foreach (string tab in tabs)
            {
                string captured = tab;
                var button = new Button(() =>
                {
                    activeTab = tabs.IndexOf(captured);
                    RebuildContent();
                }) { text = tab };
                button.style.marginRight = 2;
                row.Add(button);
            }
            rootVisualElement.Add(row);
        }

        private void RebuildContent()
        {
            if (content == null) return;
            content.Clear();
            report = report ?? WorldValidationService.LastReport;
            switch (activeTab)
            {
                case 1: BuildDiagnostics(); break;
                case 2: BuildDependencies(); break;
                case 3: BuildHistory(); break;
                case 4: BuildPolicy(); break;
                default: BuildDashboard(); break;
            }
            UpdateStatus();
        }

        private void BuildDashboard()
        {
            var scroll = new ScrollView { name = "world-validation-dashboard" };
            scroll.style.flexGrow = 1;
            summaryLabel = new Label { name = "world-validation-summary" };
            summaryLabel.style.whiteSpace = WhiteSpace.Normal;
            summaryLabel.style.marginLeft = 8;
            summaryLabel.style.marginTop = 8;
            scroll.Add(summaryLabel);
            coverageLabel = new Label { name = "world-validation-coverage" };
            coverageLabel.style.whiteSpace = WhiteSpace.Normal;
            coverageLabel.style.marginLeft = 8;
            coverageLabel.style.marginTop = 8;
            scroll.Add(coverageLabel);
            var help = new HelpBox("A green result covers only the discovered scope. Not evaluated, unsupported, unavailable, partial and stale states remain visible so an unloaded district cannot appear validated. For DistrictCell, select a CityDistrict for its whole authored district, select a CityGeneratedInstance for its cell, or enter a stable district ID and x,z cell tokens.", HelpBoxMessageType.Info);
            help.style.marginTop = 12;
            help.style.marginLeft = 8;
            help.style.marginRight = 8;
            scroll.Add(help);
            content.Add(scroll);
            UpdateDashboardLabels();
        }

        private void BuildDiagnostics()
        {
            var wrapper = new VisualElement { name = "world-validation-diagnostics" };
            wrapper.style.flexDirection = FlexDirection.Row;
            wrapper.style.flexWrap = Wrap.Wrap;
            wrapper.style.flexGrow = 1;

            searchField = new TextField("Search");
            searchField.RegisterValueChangedCallback(_ => RefreshDiagnostics());
            searchField.style.marginLeft = 6;
            searchField.style.marginRight = 6;
            searchField.style.minWidth = 220;
            var left = new VisualElement();
            left.style.width = 360;
            left.style.flexShrink = 1;
            left.style.maxWidth = Length.Percent(100);
            if(!string.IsNullOrEmpty(contextObjectId))
            {
                left.Add(new HelpBox("Showing findings for the exact linked source. This filter does not change the scan scope.",HelpBoxMessageType.Info));
                left.Add(new Button(()=>{contextObjectId=null;RebuildContent();}){text="Show All Sources"});
            }
            left.Add(searchField);
            diagnosticList = new ListView
            {
                name = "world-validation-result-list",
                selectionType = SelectionType.Single,
                fixedItemHeight = 48,
                makeItem = () =>
                {
                    var label = new Label { style = { whiteSpace = WhiteSpace.Normal } };
                    label.style.paddingTop = 4;
                    label.style.paddingBottom = 4;
                    return label;
                },
                bindItem = (element, index) =>
                {
                    if (element is Label label && index >= 0 && index < visibleResults.Count)
                    {
                        WorldValidationResult item = visibleResults[index];
                        label.text = "[" + item.status + "] " + item.title + "\n" + item.ruleId + " · " + item.DisplayTarget;
                    }
                }
            };
            diagnosticList.style.flexGrow = 1;
            diagnosticList.selectionChanged += selection =>
            {
                selectedResult = selection.OfType<WorldValidationResult>().FirstOrDefault();
                BuildDetails();
            };
            left.Add(diagnosticList);
            wrapper.Add(left);

            detailView = new ScrollView { name = "world-validation-result-details" };
            detailView.style.flexGrow = 1;
            detailView.style.marginLeft = 8;
            detailView.style.marginRight = 8;
            wrapper.Add(detailView);
            content.Add(wrapper);
            RefreshDiagnostics();
            BuildDetails();
        }

        private void BuildDependencies()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            var label = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            label.style.marginLeft = 8;
            label.style.marginTop = 8;
            if (report == null) label.text = "No validation report exists yet.";
            else
            {
                WorldValidationScopeRecord scope = report.scope;
                label.text = "Dependency and scope evidence\n\n"
                    + "Requested scope: " + report.requestedScope + "\n"
                    + "Scope fingerprint: " + (scope?.fingerprint ?? "") + "\n"
                    + "Scenes scanned: " + (scope?.scannedScenes?.Length ?? 0) + "\n"
                    + "Assets scanned: " + (scope?.scannedAssets?.Length ?? 0) + "\n"
                    + "Assets omitted by incremental filter: " + (scope?.omittedAssets?.Length ?? 0) + "\n"
                    + "Selected objects: " + (scope?.selectedObjects?.Length ?? 0) + "\n"
                    + "Districts: " + (scope?.districtIds?.Length ?? 0) + "\n"
                    + "Cells: " + (scope?.cellIds?.Length ?? 0) + "\n"
                    + "Partial: " + report.partial + "\n\n"
                    + "Unavailable:\n" + string.Join("\n", scope?.unavailable ?? Array.Empty<string>()) + "\n\n"
                    + "Omitted rules:\n" + string.Join("\n", scope?.omittedRules ?? Array.Empty<string>()) + "\n\n"
                    + "Incremental assets not inspected:\n" + string.Join("\n", scope?.omittedAssets ?? Array.Empty<string>());
            }
            scroll.Add(label);
            var update = new Button(() =>
            {
                if (WorldValidationDependencyCache.TryUpdate(out string failure)) RebuildContent();
                else EditorUtility.DisplayDialog("World Validation", "Dependency index update failed: " + failure, "OK");
            }) { text = "Rebuild dependency index" };
            update.style.marginLeft = 8;
            scroll.Add(update);
            content.Add(scroll);
        }

        private void BuildHistory()
        {
            var wrapper = new VisualElement();
            wrapper.style.flexGrow = 1;
            historyPaths.Clear();
            historyPaths.AddRange(WorldValidationHistoryStore.ListPaths());
            var list = new ListView(historyPaths, 28, () => new Label(), (element, index) =>
            {
                if (element is Label label && index >= 0 && index < historyPaths.Count) label.text = Path.GetFileName(historyPaths[index]);
            });
            list.style.flexGrow = 1;
            list.selectionChanged += selection =>
            {
                string path = selection.OfType<string>().FirstOrDefault();
                WorldValidationReport loaded = WorldValidationHistoryStore.Load(path);
                if (loaded != null)
                {
                    report = loaded;
                    selectedResult = null;
                    RebuildContent();
                }
            };
            wrapper.Add(list);
            var note = new Label("History is stored under Library/NfsMwRemaster/WorldValidation/history and is intentionally not imported as project content.");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginLeft = 8;
            wrapper.Add(note);
            content.Add(wrapper);
        }

        private void BuildPolicy()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            WorldValidationPolicy policy = WorldValidationPolicy.Find();
            var label = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            label.style.marginLeft = 8;
            label.style.marginTop = 8;
            label.text = policy == null
                ? "No WorldValidationPolicy asset exists. The dashboard uses conservative defaults until you create one."
                : "Policy: " + AssetDatabase.GetAssetPath(policy) + "\n"
                    + "Schema: " + policy.schema + "\n"
                    + "Texture budget: " + policy.maximumTextureMegabytes + " MB\n"
                    + "Mesh budget: " + policy.maximumMeshVertices + " vertices\n"
                    + "Scene object budget: " + policy.maximumLoadedSceneObjects + "\n"
                    + "Suppressions: " + (policy.suppressions?.Length ?? 0) + "\n"
                    + "Baselines: " + (policy.baselines?.Length ?? 0) + "\n\n"
                    + "Suppressions are exact rule/owner/revision entries. Baselines acknowledge existing debt and never turn it into a pass.";
            scroll.Add(label);
            if (policy != null)
            {
                var open = new Button(() =>
                {
                    Selection.activeObject = policy;
                    EditorGUIUtility.PingObject(policy);
                }) { text = "Select policy asset" };
                scroll.Add(open);
            }
            content.Add(scroll);
        }

        private void RefreshDiagnostics()
        {
            if (diagnosticList == null) return;
            visibleResults.Clear();
            string query = searchField?.value ?? string.Empty;
            string category = categoryField?.value ?? "All";
            string status = statusField?.value ?? "All";
            foreach (WorldValidationResult item in report?.results ?? Array.Empty<WorldValidationResult>())
            {
                if(!string.IsNullOrEmpty(contextObjectId)&&item.globalObjectId!=contextObjectId)continue;
                if (!string.Equals(category, "All", StringComparison.Ordinal)
                    && !string.Equals(item.category.ToString(), category, StringComparison.Ordinal)) continue;
                if (!string.Equals(status, "All", StringComparison.Ordinal)
                    && !string.Equals(item.status.ToString(), status, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrWhiteSpace(query)
                    && (item.title + " " + item.message + " " + item.ruleId + " " + item.DisplayTarget)
                        .IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                visibleResults.Add(item);
            }
            if (selectedResult != null && !visibleResults.Contains(selectedResult)) selectedResult = null;
            diagnosticList.itemsSource = visibleResults;
            diagnosticList.RefreshItems();
        }

        private void BuildDetails()
        {
            if (detailView == null) return;
            detailView.Clear();
            if (selectedResult == null)
            {
                detailView.Add(new HelpBox("Select a diagnostic to inspect its status, evidence, source revision, baseline state and navigation target.", HelpBoxMessageType.Info));
                return;
            }
            var title = new Label("[" + selectedResult.status + "] " + selectedResult.title);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            detailView.Add(title);
            var facts = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            facts.text = "Rule: " + selectedResult.ruleId + " v" + selectedResult.ruleVersion + "\n"
                + "Category: " + selectedResult.category + " · Severity: " + selectedResult.severity + "\n"
                + "Owner: " + selectedResult.ownerModule + "\n"
                + "Target: " + selectedResult.DisplayTarget + "\n"
                + "Source revision: " + selectedResult.sourceRevision + "\n"
                + "Baseline: " + selectedResult.baselineState + " · stale: " + selectedResult.stale + "\n"
                + "Heuristic: " + selectedResult.heuristic + " · empirical: " + selectedResult.empirical;
            detailView.Add(facts);
            detailView.Add(new Label("\n" + selectedResult.message) { style = { whiteSpace = WhiteSpace.Normal } });
            if (selectedResult.evidence != null && selectedResult.evidence.Length > 0)
            {
                var evidenceFoldout = new Foldout { text = "Evidence (" + selectedResult.evidence.Length + ")", value = true };
                foreach (WorldValidationEvidence evidence in selectedResult.evidence)
                    evidenceFoldout.Add(new Label(evidence.kind + " · " + evidence.label + ": " + evidence.value) { style = { whiteSpace = WhiteSpace.Normal } });
                detailView.Add(evidenceFoldout);
            }
            if (selectedResult.canNavigate)
                detailView.Add(new Button(() => NavigateSelected()) { text = "Select / focus source" });
            if (selectedResult.canFix)
            {
                if (WorldValidationRuleRegistry.TryPreviewFix(selectedResult,
                    out WorldValidationFixPreview preview, out string previewFailure))
                {
                    var fix = new Foldout { text = "Safe-fix preview", value = true };
                    fix.Add(new Label(preview.title) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                    fix.Add(new Label("Rationale: " + preview.rationale) { style = { whiteSpace = WhiteSpace.Normal } });
                    fix.Add(new Label("Risk: " + preview.risk) { style = { whiteSpace = WhiteSpace.Normal } });
                    fix.Add(new Label("Rollback: " + preview.rollback) { style = { whiteSpace = WhiteSpace.Normal } });
                    fix.Add(new Label("Mutates content: " + preview.mutatesContent) { style = { whiteSpace = WhiteSpace.Normal } });
                    fix.Add(new Label("Affected IDs: " + (preview.affectedIds.Length == 0
                        ? "none declared"
                        : string.Join(", ", preview.affectedIds))) { style = { whiteSpace = WhiteSpace.Normal } });
                    detailView.Add(fix);
                    detailView.Add(new HelpBox("Preview only. Applying a repair remains a domain-owner operation with its own Undo/rollback and post-fix validation contract.", HelpBoxMessageType.Info));
                }
                else
                {
                    detailView.Add(new HelpBox("Safe-fix preview unavailable: " + previewFailure, HelpBoxMessageType.Warning));
                }
            }
            WorldValidationPolicy policy = WorldValidationPolicy.Find();
            if (policy != null && selectedResult.status != WorldValidationStatus.Passed)
            {
                var actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                actions.Add(new Button(() => SuppressSelected(policy)) { text = "Suppress selected" });
                actions.Add(new Button(() => BaselineSelected(policy)) { text = "Baseline selected" });
                detailView.Add(actions);
            }
        }

        private void StartRun()
        {
            var request = WorldValidationRunRequest.Default();
            if (!Enum.TryParse(scopeField?.value, out WorldValidationScopeKind scope)) scope = WorldValidationScopeKind.OpenScenes;
            request.scope = scope;
            request.explicitScenePaths = (explicitScenesField?.value ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
            request.districtId = (districtIdField?.value ?? string.Empty).Trim();
            request.cellIds = WorldValidationScopeDiscovery.ParseCellTokens(cellIdsField?.value);
            string category = categoryField?.value ?? "All";
            if (!string.Equals(category, "All", StringComparison.Ordinal)
                && Enum.TryParse(category, out WorldValidationCategory parsedCategory)) request.categories = new[] { parsedCategory };
            request.includeExpensive = expensiveToggle != null && expensiveToggle.value;
            request.incremental = incrementalToggle != null && incrementalToggle.value;
            request.includeInfo = infoToggle == null || infoToggle.value;
            WorldValidationService.Start(request);
            report = null;
            selectedResult = null;
            RebuildContent();
        }

        private void CreatePolicy()
        {
            WorldValidationPolicy policy = WorldValidationPolicy.CreateDefaultAsset();
            Selection.activeObject = policy;
            EditorGUIUtility.PingObject(policy);
            RebuildContent();
        }

        private static void OpenPolicy()
        {
            WorldValidationPolicy policy = WorldValidationPolicy.Find();
            if (policy == null) policy = WorldValidationPolicy.CreateDefaultAsset();
            Selection.activeObject = policy;
            EditorGUIUtility.PingObject(policy);
        }

        private void NavigateSelected()
        {
            if (!WorldValidationNavigation.TrySelect(selectedResult, out string failure))
                EditorUtility.DisplayDialog("World Validation", failure, "OK");
            else
            {
                var target=Selection.activeObject;
                var module=RacingModuleRegistry.All.FirstOrDefault(m=>m.Id!="validation"&&m.Compatible(target));
                if(module!=null)Navigate?.Invoke(RacingDocumentLink.For(module.Id,module.Compatible(target)));
            }
        }

        private void SuppressSelected(WorldValidationPolicy policy)
        {
            string reason = "Intentional authoring exception reviewed in World Validation Dashboard.";
            Undo.RecordObject(policy, "Suppress world validation finding");
            policy.AddSuppression(selectedResult, selectedResult.assetPath ?? selectedResult.scenePath, reason, Environment.UserName);
            EditorUtility.SetDirty(policy);
            AssetDatabase.SaveAssetIfDirty(policy);
            report = report ?? WorldValidationService.LastReport;
            policy.Apply(report);
            RebuildContent();
        }

        private void BaselineSelected(WorldValidationPolicy policy)
        {
            Undo.RecordObject(policy, "Baseline world validation finding");
            policy.AddBaseline(selectedResult, "Existing debt acknowledged from World Validation Dashboard.");
            EditorUtility.SetDirty(policy);
            AssetDatabase.SaveAssetIfDirty(policy);
            report = report ?? WorldValidationService.LastReport;
            policy.Apply(report);
            RebuildContent();
        }

        private void OnReportChanged(WorldValidationReport changed)
        {
            if (changed != null) report = changed;
            if (rootVisualElement == null) return;
            RefreshDiagnostics();
            UpdateDashboardLabels();
            UpdateStatus();
            Repaint();
        }

        private void UpdateStatus()
        {
            if (statusLabel == null) return;
            if (WorldValidationService.IsRunning && WorldValidationService.Active != null)
            {
                WorldValidationRunSession session = WorldValidationService.Active;
                statusLabel.text = "Running " + session.EvaluatedRuleCount + "/" + session.TotalRuleCount + " rules (" + (session.Progress * 100).ToString("0") + "%)…";
                return;
            }
            if (report == null)
            {
                statusLabel.text = "No report yet. Choose a scope and run validation.";
                return;
            }
            statusLabel.text = "Last run: " + report.runStatus + " · scope=" + report.requestedScope + " · partial=" + report.partial + " · stale=" + report.stale;
        }

        private void UpdateDashboardLabels()
        {
            if (summaryLabel == null || coverageLabel == null) return;
            if (report == null)
            {
                summaryLabel.text = "No validation report exists yet.";
                coverageLabel.text = string.Empty;
                return;
            }
            summaryLabel.text = "Run " + report.runId + "\n"
                + "Passed " + report.passed + " · Failed " + report.failed + " · Warnings " + report.warnings + " · Not evaluated " + report.notEvaluated
                + " · Unsupported " + report.unsupported + " · Cancelled " + report.cancelled + " · Timed out " + report.timedOut
                + " · Execution errors " + report.executionErrors + " · Suppressed " + report.suppressed + " · Existing debt " + report.baselineDebt
                + "\nPartial: " + report.partial + " · Stale: " + report.stale;
            coverageLabel.text = "Coverage\n" + string.Join("\n", (report.coverage ?? Array.Empty<WorldValidationCoverage>())
                .Select(entry => entry.category + ": evaluated=" + entry.evaluated + ", not evaluated=" + entry.notEvaluated
                    + ", unsupported=" + entry.unsupported + ", blocking=" + entry.errors
                    + (string.IsNullOrEmpty(entry.reason) ? string.Empty : " — " + entry.reason)));
        }
    }
}
