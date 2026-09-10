using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class RoadNetworkEditorWindow : EditorWindow
    {
        [SerializeField] private RoadProfile profile;
        [SerializeField] private RoadNetworkAuthoring network;
        private readonly List<RoadAuthoring> roads = new List<RoadAuthoring>();
        private ListView list;
        private HelpBox status;
        [MenuItem("NFS MW Remaster/Roads/Road Network Editor")]
        public static void Open() => GetWindow<RoadNetworkEditorWindow>("Road Network Editor");
        private void OnEnable() { EditorApplication.hierarchyChanged += Refresh; Selection.selectionChanged += Repaint; }
        private void OnDisable() { EditorApplication.hierarchyChanged -= Refresh; Selection.selectionChanged -= Repaint; }
        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 12; root.style.paddingRight = 12; root.style.paddingTop = 12; root.style.paddingBottom = 12;
            var title = new Label("Road Network"); title.style.fontSize = 19; title.style.unityFontStyleAndWeight = FontStyle.Bold; root.Add(title);
            root.Add(new Label("Draw, shape and validate roads in the Scene view."));
            RoadDrawTool.Profile = profile; RoadDrawTool.Network = network;
            var networkField = new ObjectField("Network") { objectType = typeof(RoadNetworkAuthoring), allowSceneObjects = true, value = network };
            networkField.RegisterValueChangedCallback(evt => { network = evt.newValue as RoadNetworkAuthoring; RoadDrawTool.Network = network; }); root.Add(networkField);
            var field = new ObjectField("Profile") { objectType = typeof(RoadProfile), allowSceneObjects = false, value = profile };
            field.RegisterValueChangedCallback(evt => { profile = evt.newValue as RoadProfile; RoadDrawTool.Profile = profile; }); root.Add(field);
            root.Add(new Button(() => Run(() => { profile = RoadProfileInspector.GetDefaultProfile(); field.value = profile; })) { text = "Two lane preset" });
            root.Add(new Button(() => Run(() => { profile = RoadProfileInspector.GetLocalStreetProfile(); field.value = profile; })) { text = "Local street with curbs and sidewalks" });
            root.Add(new Button(() => Run(() =>
            {
                RequireProfile(); RoadDrawTool.Profile = profile; ToolManager.SetActiveTool<RoadDrawTool>();
                SceneView.lastActiveSceneView?.Focus();
            })) { text = "Draw road" });
            root.Add(new Button(() => Run(() =>
            {
                RequireProfile(); var road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 100 }, network);
                Selection.activeGameObject = road.gameObject; RoadPreview.Rebuild(road); SceneView.lastActiveSceneView?.FrameSelected();
            })) { text = "Create 100 m road" });
            var surface = new Toggle("Snap to surfaces") { value = RoadDrawTool.SnapToSurface };
            surface.RegisterValueChangedCallback(evt => RoadDrawTool.SnapToSurface = evt.newValue); root.Add(surface);
            var grid = new FloatField("Grid size (hold Ctrl)") { value = RoadDrawTool.Grid };
            grid.RegisterValueChangedCallback(evt => RoadDrawTool.Grid = RoadGeometry.Finite(evt.newValue) ? Mathf.Max(0, evt.newValue) : 1); root.Add(grid);
            root.Add(new Button(() => Run(() =>
            {
                var selected = Selection.GetFiltered<RoadAuthoring>(SelectionMode.Editable);
                if (selected.Length == 0) throw new ArgumentException("Select at least one road.");
                foreach (var road in selected) { using (RoadMeshBuilder.Build(road)) { } RoadPreview.Rebuild(road); }
            })) { text = "Validate and preview selected" });
            root.Add(new Button(() => Run(() =>
            {
                network = RoadAuthoringCommands.CreateNetwork(Selection.GetFiltered<RoadAuthoring>(SelectionMode.Editable));
                networkField.value = network;
                Selection.activeGameObject = network.gameObject;
            })) { text = "Create network from selected roads" });
            root.Add(new Button(() => Run(() => RoadAuthoringCommands.AddRoads(network, Selection.GetFiltered<RoadAuthoring>(SelectionMode.Editable)))) { text = "Add selected roads to network" });
            root.Add(new Button(() => Run(() => RoadAuthoringCommands.ConnectMatchingEnds(network))) { text = "Connect matching lane ends" });
            root.Add(new Button(() => Run(() =>
            {
                var selected = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponentInParent<RoadNetworkAuthoring>();
                var owner = network != null ? network : selected;
                if (owner == null) throw new ArgumentException("Choose a road network or select one of its roads.");
                RoadNetworkAuthoringInspector.Bake(owner);
            })) { text = "Bake selected network" });
            list = new ListView(roads, 24, () => new Label(), (element, index) => ((Label)element).text = roads[index] == null ? "" : roads[index].name);
            list.style.flexGrow = 1;
            list.selectionChanged += selected => { foreach (var item in selected) if (item is RoadAuthoring road) Selection.activeGameObject = road.gameObject; };
            root.Add(list);
            status = new HelpBox("Choose a profile, then draw. Enter finishes a road; Escape cancels the gesture. Native Spline tools edit tangents.", HelpBoxMessageType.Info);
            root.Add(status); Refresh();
        }
        private void RequireProfile() { if (profile == null) throw new ArgumentException("Choose a profile or create the two lane preset."); }
        private void Run(Action action)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { status.text = "Exit Play mode before editing roads."; return; }
            try { action(); status.text = "Ready."; status.messageType = HelpBoxMessageType.Info; Refresh(); }
            catch (ArgumentException exception) { status.text = exception.Message; status.messageType = HelpBoxMessageType.Error; }
        }
        private void Refresh()
        {
            if (list == null) return;
            roads.Clear(); roads.AddRange(UnityEngine.Object.FindObjectsByType<RoadAuthoring>(FindObjectsInactive.Include));
            roads.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal)); list.Rebuild();
        }
    }
}
