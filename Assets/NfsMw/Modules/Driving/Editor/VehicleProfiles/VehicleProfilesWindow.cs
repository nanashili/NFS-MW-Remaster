using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Native, demand-repaint IMGUI surface. Authoring and publication do not depend on window lifetime.</summary>
    public sealed class VehicleProfilesWindow : EditorWindow
    {
        [SerializeField] private VehicleIdentityCatalog catalog;
        [SerializeField] private VehicleProfileDraft draft;
        [SerializeField] private string query = "", brandId = "", modelId = "", variantId = "", newLabel = "", generation = "";
        [SerializeField] private int year, addYear = 2000, tab;
        [SerializeField] private Vector2 listScroll, detailScroll;
        private VehicleProfileDraft[] drafts = Array.Empty<VehicleProfileDraft>();
        private VehicleProfileDraft[] filtered = Array.Empty<VehicleProfileDraft>();
        private SerializedObject draftObject, catalogObject;
        private VehicleProfileReport report;
        private VehicleProfilePublication.Plan plan;
        private string failure = "", status = "Draft — not validated";
        private bool refreshPending;
        private VehicleProfileMeshPreview preview;
        private readonly VehicleFrameworkPanel frameworkPanel = new VehicleFrameworkPanel();
        private readonly VehicleCameraPanel cameraPanel = new VehicleCameraPanel();
        [SerializeField] private GameObject previewSource;
        [SerializeField] private int previewLod;

        [MenuItem("Racing Tools/Vehicles/Vehicle Profiles")]
        public static void Open() => GetWindow<VehicleProfilesWindow>("Vehicle Profiles");
        [MenuItem("Racing Tools/Vehicles/Camera")]
        public static void OpenCamera()
        {
            var window = GetWindow<VehicleProfilesWindow>("Vehicle Profiles"); window.tab = 5;
            var selected = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<VehicleCameraRig>() : null;
            window.cameraPanel.SelectCamera(selected ? selected : UnityEngine.Object.FindFirstObjectByType<VehicleCameraRig>());
            window.Show();
        }
        private void OnInspectorUpdate() { if (tab == 5 && Application.isPlaying) Repaint(); }
        public void OpenDraft(VehicleProfileDraft selected) { Select(selected); Show(); }

        private void OnEnable()
        {
            minSize = new Vector2(420, 340);
            EditorApplication.projectChanged += QueueRefresh;
            Undo.undoRedoPerformed += Changed;
            QueueRefresh();
        }

        private void OnDisable()
        {
            frameworkPanel.Close();
            cameraPanel.Close();
            preview?.Dispose(); preview = null;
            EditorApplication.projectChanged -= QueueRefresh;
            Undo.undoRedoPerformed -= Changed;
            EditorApplication.delayCall -= Refresh;
            refreshPending = false;
            draftObject?.Dispose(); draftObject = null;
            catalogObject?.Dispose(); catalogObject = null;
        }

        private void QueueRefresh()
        {
            if (refreshPending) return;
            refreshPending = true;
            EditorApplication.delayCall += Refresh;
        }

        private void Refresh()
        {
            refreshPending = false;
            if (this == null) return;
            drafts = AssetDatabase.FindAssets("t:VehicleProfileDraft")
                .Select(g => AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p != null).OrderBy(p => p.DisplayLabel, StringComparer.OrdinalIgnoreCase).ToArray();
            Filter(); Changed();
        }

        private void Filter() => filtered = drafts.Where(p => p.DisplayLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        private void Changed() { preview?.Dispose(); preview = null; report = null; plan = null; status = draft != null && draft.GeneratedProduct != null ? "Published listing — revalidate to check freshness" : "Draft — not validated"; Repaint(); }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New Catalogue", EditorStyles.toolbarButton)) Run(() => CreateAsset<VehicleIdentityCatalog>("Identity Catalogue", c => { catalog = c; Changed(); }));
                if (GUILayout.Button("New Draft", EditorStyles.toolbarButton)) Run(() => CreateAsset<VehicleProfileDraft>("Vehicle Draft", d => { d.catalog = catalog; EditorUtility.SetDirty(d); Select(d); }));
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) QueueRefresh();
                GUILayout.FlexibleSpace();
            }
            if (!string.IsNullOrEmpty(failure)) EditorGUILayout.HelpBox(failure, MessageType.Error);
            bool wide = position.width >= 850;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (wide) using (new EditorGUILayout.VerticalScope(GUILayout.Width(260))) DrawList();
                using (new EditorGUILayout.VerticalScope())
                {
                    if (!wide)
                    {
                        var selected = (VehicleProfileDraft)EditorGUILayout.ObjectField("Draft", draft, typeof(VehicleProfileDraft), false);
                        if (selected != draft) Select(selected);
                    }
                    detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                    try
                    {
                        tab = GUILayout.Toolbar(tab, new[] { "Identity", "Bindings", "Framework", "Budgets", "Build", "Camera" });
                        if (tab == 5) cameraPanel.Draw();
                        else if (tab == 0) DrawIdentity();
                        else if (draft == null) EditorGUILayout.HelpBox("Create or select a draft. Identity authoring is available with an empty catalogue.", MessageType.Info);
                        else DrawDraft();
                    }
                    finally { EditorGUILayout.EndScrollView(); }
                }
            }
        }

        private void DrawList()
        {
            EditorGUI.BeginChangeCheck();
            query = EditorGUILayout.TextField("Find Vehicle", query);
            if (EditorGUI.EndChangeCheck()) Filter();
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            try
            {
                if (filtered.Length == 0) EditorGUILayout.HelpBox("No matching drafts. Create a catalogue, then a vehicle draft.", MessageType.Info);
                foreach (var profile in filtered)
                {
                    if (profile == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var model = profile.catalog != null ? profile.catalog.FindModel(profile.modelId) : null;
                        var brand = model != null ? profile.catalog.FindBrand(model.brandId) : null;
                        Rect icon = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32));
                        if (brand?.logo != null) GUI.DrawTexture(icon, brand.logo, ScaleMode.ScaleToFit);
                        else GUI.Label(icon, brand != null && brand.displayName.Length > 0 ? brand.displayName.Substring(0, Math.Min(2, brand.displayName.Length)).ToUpperInvariant() : "?", EditorStyles.centeredGreyMiniLabel);
                        if (GUILayout.Toggle(profile == draft, profile.DisplayLabel, "Button", GUILayout.MinHeight(32)) && profile != draft) Select(profile);
                    }
                }
            }
            finally { EditorGUILayout.EndScrollView(); }
        }

        private void DrawIdentity()
        {
            var selected = (VehicleIdentityCatalog)EditorGUILayout.ObjectField("Catalogue", catalog, typeof(VehicleIdentityCatalog), false);
            if (selected != catalog) { catalog = selected; brandId = modelId = variantId = ""; year = 0; Changed(); }
            if (catalog == null) { EditorGUILayout.HelpBox("Create a catalogue to add approved manufacturers and model years. No real-world specification is inferred from asset filenames.", MessageType.Info); return; }
            var brands = catalog.Brands.Where(b => b != null).ToArray();
            string nextBrand = Pick("Manufacturer", brandId, brands.Select(b => b.Id).ToArray(), brands.Select(b => b.displayName).ToArray());
            if (nextBrand != brandId) { brandId = nextBrand; modelId = variantId = ""; year = 0; }
            var models = catalog.Models.Where(m => m != null && m.brandId == brandId).ToArray();
            string nextModel = Pick("Model / Generation", modelId, models.Select(m => m.Id).ToArray(), models.Select(m => m.displayName + " " + m.generation).ToArray());
            if (nextModel != modelId) { modelId = nextModel; variantId = ""; year = 0; }
            var model = catalog.FindModel(modelId);
            int[] years = model?.years.ToArray() ?? Array.Empty<int>();
            string selectedYear = Pick("Model Year", year.ToString(), years.Select(y => y.ToString()).ToArray(), years.Select(y => y.ToString()).ToArray());
            int.TryParse(selectedYear, out int nextYear);
            if (year != nextYear) { year = nextYear; variantId = ""; }
            var variants = model?.variants.Where(v => v != null && v.year == year).ToArray() ?? Array.Empty<VehicleVariantRecord>();
            variantId = Pick("Variant", variantId, variants.Select(v => v.Id).ToArray(), variants.Select(v => v.displayName).ToArray());
            using (new EditorGUI.DisabledScope(draft == null || string.IsNullOrEmpty(variantId)))
                if (GUILayout.Button("Assign Identity to Draft")) Run(() =>
                {
                    Undo.RecordObject(draft, "Assign Vehicle Identity"); draft.catalog = catalog; draft.modelId = modelId; draft.modelYear = year; draft.variantId = variantId;
                    EditorUtility.SetDirty(draft); Changed(); Filter();
                });
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add Approved Catalogue Data", EditorStyles.boldLabel);
            newLabel = EditorGUILayout.TextField("Display Name", newLabel);
            generation = EditorGUILayout.TextField("Generation / Chassis", generation);
            addYear = EditorGUILayout.IntField("Model Year to Add", addYear);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Brand")) EditCatalog(() => brandId = catalog.AddBrand(newLabel).Id);
                if (GUILayout.Button("Add Model")) EditCatalog(() => modelId = catalog.AddModel(brandId, newLabel, generation).Id);
                if (GUILayout.Button("Add Year")) EditCatalog(() => { catalog.AddYear(modelId, addYear); year = addYear; });
                if (GUILayout.Button("Add Variant")) EditCatalog(() => variantId = catalog.AddVariant(modelId, year, newLabel).Id);
            }
            EditorGUILayout.HelpBox("Model year and production-year range are separate. Edit labels, aliases, approved logos and provenance below. Immutable IDs are hidden; deleting referenced data causes validation errors.", MessageType.Info);
            if (catalogObject == null || catalogObject.targetObject != catalog) { catalogObject?.Dispose(); catalogObject = new SerializedObject(catalog); }
            catalogObject.Update();
            EditorGUILayout.PropertyField(catalogObject.FindProperty("brands"), true);
            EditorGUILayout.PropertyField(catalogObject.FindProperty("models"), true);
            if (catalogObject.ApplyModifiedProperties()) Changed();
            if (GUILayout.Button("Save Catalogue")) Run(() => AssetDatabase.SaveAssetIfDirty(catalog));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export Catalogue")) Run(() =>
                {
                    if (EditorUtility.IsDirty(catalog)) throw new InvalidOperationException("Save the catalogue first.");
                    string path = EditorUtility.SaveFilePanel("Export Catalogue and Referenced Logos", "", catalog.name, "unitypackage");
                    if (!string.IsNullOrEmpty(path)) AssetDatabase.ExportPackage(AssetDatabase.GetAssetPath(catalog), path, ExportPackageOptions.IncludeDependencies);
                });
                if (GUILayout.Button("Import Catalogue")) Run(() =>
                {
                    string path = EditorUtility.OpenFilePanel("Review a Trusted Catalogue Package", "", "unitypackage");
                    if (!string.IsNullOrEmpty(path)) AssetDatabase.ImportPackage(path, true);
                });
            }
        }

        private void DrawDraft()
        {
            EditorGUILayout.LabelField(draft.DisplayLabel, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("State", status);
            if (draftObject == null || draftObject.targetObject != draft) { draftObject?.Dispose(); draftObject = new SerializedObject(draft); }
            draftObject.Update();
            if (tab == 1)
            {
                EditorGUILayout.HelpBox("Bind body geometry, wheels, presentation and mounting sockets below. Create a production prefab, or update an existing prefab's runtime configuration. Geometry updates use Prefab Mode to preserve authored references. Skinned rig assembly requires an existing authored prefab.", MessageType.Info);
                foreach (string field in new[] { "vehiclePrefab", "tuning", "audio", "customization", "performance", "thumbnail", "localizationKey", "tags", "description", "provenance" }) Field(field);
                Field("assembly");
                if (GUILayout.Button("Create Assembled Prefab")) Run(() =>
                {
                    draftObject.ApplyModifiedProperties();
                    string[] problems = VehicleProfileAssembly.Validate(draft);
                    if (problems.Length != 0) throw new InvalidOperationException(string.Join("\n", problems));
                    string path = EditorUtility.SaveFilePanelInProject("Create New Assembled Vehicle", "Vehicle", "prefab", "First-create only. Existing prefabs are never overwritten.");
                    if (string.IsNullOrEmpty(path)) return;
                    if (!EditorUtility.DisplayDialog("Create Assembled Prefab?", "Create " + path + " from the explicit mappings. Reference shared meshes/materials. Add the existing controller, four wheels and chassis collider. Do not copy source scripts or register a listing.", "Create Prefab", "Cancel")) return;
                    var prefab = VehicleProfileAssembly.CreatePrefab(draft, path);
                    Undo.RecordObject(draft, "Bind Assembled Vehicle"); draft.vehiclePrefab = prefab; EditorUtility.SetDirty(draft); draftObject.Update(); Changed();
                });
                if (GUILayout.Button("Select Prefab for Authoring") && draft.vehiclePrefab != null) Selection.activeObject = draft.vehiclePrefab;
                using (new EditorGUI.DisabledScope(draft.vehiclePrefab == null))
                    if (GUILayout.Button("Update Prefab Runtime Configuration")) Run(() => { draftObject.ApplyModifiedProperties(); VehicleProfileAssembly.UpdatePrefabConfiguration(draft); Changed(); });
                if (GUILayout.Button("Select Authoritative Tuning") && draft.tuning != null) Selection.activeObject = draft.tuning;
                if (GUILayout.Button("Select Engine Audio Profile") && draft.audio != null) Selection.activeObject = draft.audio;
                if (GUILayout.Button("Engine audio · Inspect source / Debug playback")) AudioAnalysis.BlackBoxInspectorWindow.Open(draft, draft.audio);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Static Geometry Preview", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Render-only inspection with shared materials. No source scripts, audio or physics run. Skinned meshes show bind pose. Left drag orbits, other-button drag pans, wheel zooms.", MessageType.Info);
                previewSource = (GameObject)EditorGUILayout.ObjectField("Preview Asset", previewSource, typeof(GameObject), false);
                previewLod = Mathf.Max(0, EditorGUILayout.IntField("LOD Level", previewLod));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Load Preview")) Run(() =>
                    { preview?.Dispose(); preview = null; preview = new VehicleProfileMeshPreview(previewSource != null ? previewSource : draft.vehiclePrefab, previewLod); });
                    if (GUILayout.Button("Stop Preview")) { preview?.Dispose(); preview = null; }
                }
                if (preview != null && !preview.IsDisposed)
                {
                    Rect area = GUILayoutUtility.GetRect(100, 280, GUILayout.ExpandWidth(true));
                    try { preview.Draw(area, Repaint); }
                    catch (ExitGUIException) { throw; }
                    catch (Exception exception) { failure = exception.Message; preview.Dispose(); preview = null; }
                }
            }
            else if (tab == 2)
            {
                frameworkPanel.Draw(draft, draftObject);
            }
            else if (tab == 3)
            {
                EditorGUILayout.HelpBox("Initial test target: M1 Pro MacBook Pro, provisional 60 FPS. Thresholds below are editable content budgets, not measured hardware guarantees. Estimates include shared dependencies once per asset; actual residency depends on import/load settings.", MessageType.Info);
                Field("budget");
            }
            else { Field("storeCatalog"); Field("price"); Field("availableInStore"); }
            if (draftObject.ApplyModifiedProperties()) Changed();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Draft")) Run(() => AssetDatabase.SaveAssetIfDirty(draft));
                if (GUILayout.Button("Validate")) Run(Validate);
            }
            if (tab == 4)
            {
                EditorGUILayout.HelpBox("Publish Listing references the supplied prefab in the existing car store. It does not certify factory spawning or acquire the car for a player. An interrupted commit blocks further publication and retains recovery backups.", MessageType.Info);
                if (GUILayout.Button("Preview Listing Changes")) Run(() =>
                {
                    string path = draft.GeneratedProduct != null ? AssetDatabase.GetAssetPath(draft.GeneratedProduct)
                        : EditorUtility.SaveFilePanelInProject("Listing Destination", "VehicleListing", "asset", "Choose an existing runtime content folder.");
                    if (!string.IsNullOrEmpty(path)) plan = VehicleProfilePublication.Prepare(draft, path);
                });
                if (plan != null)
                {
                    EditorGUILayout.HelpBox(plan.Description, MessageType.Info);
                    if (GUILayout.Button(plan.Unchanged ? "Verify No Changes" : "Publish Listing")) Run(() =>
                    { VehicleProfilePublication.Apply(plan); plan = null; Validate(); });
                }
                if (GUILayout.Button("Open Recovery Folder")) Run(() =>
                { System.IO.Directory.CreateDirectory(VehicleProfilePublication.RecoveryDirectory); EditorUtility.RevealInFinder(VehicleProfilePublication.RecoveryDirectory); });
            }
            if (report != null)
            {
                EditorGUILayout.LabelField($"LOD0: {report.Lod0Triangles:N0} triangles · {report.MaterialSlots:N0} material slots");
                EditorGUILayout.LabelField($"Largest texture: {report.MaximumTextureDimension}px · float PCM estimate: {report.EstimatedDecodedAudioBytes / 1048576.0:F1} MiB");
                foreach (var issue in report.Issues)
                {
                    EditorGUILayout.HelpBox(issue.Code + " · " + issue.Field + "\n" + issue.Message, issue.Severity == VehicleProfileSeverity.Error ? MessageType.Error : MessageType.Warning);
                    if (GUILayout.Button("Select Affected Asset: " + issue.Code)) { Selection.activeObject = issue.Target; EditorGUIUtility.PingObject(issue.Target); }
                }
            }
        }

        private void Validate()
        {
            report = VehicleProfileValidation.Inspect(draft, drafts);
            status = !report.CanPublish ? "Validation failed" : draft.GeneratedProduct == null ? "Ready to publish listing"
                : VehicleProfileValidation.Fingerprint(draft) == draft.GeneratedFingerprint ? "Published listing" : "Out of date";
        }

        private void Field(string name) => EditorGUILayout.PropertyField(draftObject.FindProperty(name), true);
        private void EditCatalog(Action edit) => Run(() => { Undo.RecordObject(catalog, "Edit Vehicle Catalogue"); edit(); EditorUtility.SetDirty(catalog); Changed(); });
        private void Select(VehicleProfileDraft selected)
        {
            draft = selected;
            if (draft != null)
            {
                catalog = draft.catalog; modelId = draft.modelId; variantId = draft.variantId; year = draft.modelYear;
                brandId = catalog != null ? catalog.FindModel(modelId)?.brandId ?? "" : "";
            }
            Changed();
        }
        private void Run(Action command)
        {
            try { failure = ""; command(); }
            catch (ExitGUIException) { throw; }
            catch (Exception exception) { failure = exception.Message; }
        }
        private static string Pick(string label, string selected, string[] ids, string[] names)
        {
            int index = Array.IndexOf(ids, selected) + 1;
            int next = EditorGUILayout.Popup(label, index, new[] { "Select…" }.Concat(names).ToArray());
            return next > 0 ? ids[next - 1] : "";
        }
        private static void CreateAsset<T>(string title, Action<T> after) where T : ScriptableObject
        {
            string path = EditorUtility.SaveFilePanelInProject(title, title.Replace(" ", ""), "asset", "Save this editor-only authoring asset.");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("The destination already exists.");
            var asset = CreateInstance<T>();
            try { AssetDatabase.CreateAsset(asset, path); }
            catch { if (!AssetDatabase.Contains(asset)) DestroyImmediate(asset); throw; }
            after(asset);
        }
    }
}
