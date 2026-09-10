using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NfsMwRemaster.Driving.AudioAnalysis;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    /// <summary>
    /// Small, stateful IMGUI table used by the analysis window. The rows are rebuilt from
    /// the presenter, while TreeView owns paging, keyboard navigation and column sorting.
    /// </summary>
    internal sealed class BlackBoxTableView : TreeView
    {
        private readonly Dictionary<int, BlackBoxTableRow> rowsById = new Dictionary<int, BlackBoxTableRow>();
        private List<BlackBoxTableRow> rows = new List<BlackBoxTableRow>();
        private string rowsSignature = string.Empty;
        private readonly Action<BlackBoxTableRow> selectionChanged;

        public BlackBoxTableView(TreeViewState state, MultiColumnHeader header, Action<BlackBoxTableRow> selectionChanged)
            : base(state, header)
        {
            this.selectionChanged = selectionChanged;
            showBorder = true;
            rowHeight = 20f;
            // Unity 6 validates this against the header even for a flat table. Column zero
            // is harmless because these rows have no children and therefore draw no foldout.
            columnIndexForTreeFoldouts = 0;
            useScrollView = true;
            header.sortingChanged += _ => Reload();
        }

        public void SetRows(IEnumerable<BlackBoxTableRow> values)
        {
            var next = values == null ? new List<BlackBoxTableRow>() : values.ToList();
            string signature = string.Join("\n", next.Select(row => row.id + "|" + string.Join("|", row.values ?? Array.Empty<string>())));
            if (signature == rowsSignature) return;
            rowsSignature = signature;
            rows = new List<BlackBoxTableRow>(next.Count);
            rowsById.Clear();
            foreach (var sourceRow in next)
            {
                var row = sourceRow;
                int id = row.id <= 0 ? 1 : row.id;
                while (rowsById.ContainsKey(id)) id = id == int.MaxValue ? 1 : id + 1;
                if (id != row.id) row = row.WithId(id);
                rows.Add(row);
                rowsById[id] = row;
            }
            Reload();
        }

        public void ResetColumns(float[] widths)
        {
            if (multiColumnHeader == null || multiColumnHeader.state == null || widths == null) return;
            var columns = multiColumnHeader.state.columns;
            for (int i = 0; i < columns.Length; i++)
            {
                columns[i].width = i < widths.Length ? widths[i] : columns[i].width;
                columns[i].autoResize = false;
            }
            multiColumnHeader.ResizeToFit();
            Reload();
        }

        public BlackBoxTableRow SelectedRow
        {
            get
            {
                int id = GetSelection().FirstOrDefault();
                BlackBoxTableRow row;
                return rowsById.TryGetValue(id, out row) ? row : null;
            }
        }

        protected override TreeViewItem BuildRoot()
        {
            return new TreeViewItem { id = 0, depth = -1, displayName = "root" };
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            IEnumerable<BlackBoxTableRow> ordered = rows;
            int sortColumn = multiColumnHeader == null ? -1 : multiColumnHeader.sortedColumnIndex;
            if (sortColumn >= 0)
            {
                bool ascending = multiColumnHeader.IsSortedAscending(sortColumn);
                var comparer = Comparer<BlackBoxTableRow>.Create((left, right) => CompareRows(left, right, sortColumn));
                ordered = ascending
                    ? ordered.OrderBy(row => row, comparer).ThenBy(row => row.id)
                    : ordered.OrderByDescending(row => row, comparer).ThenBy(row => row.id);
            }
            var result = new List<TreeViewItem>();
            foreach (var row in ordered)
            {
                var item = new BlackBoxTableItem(row.id, row);
                result.Add(item);
            }
            root.children = result;
            return result;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item as BlackBoxTableItem;
            if (item == null) return;
            for (int visible = 0; visible < args.GetNumVisibleColumns(); visible++)
            {
                int column = args.GetColumn(visible);
                Rect cell = args.GetCellRect(visible);
                string value = item.row.values != null && column < item.row.values.Length ? item.row.values[column] ?? string.Empty : string.Empty;
                GUI.Label(cell, new GUIContent(value, value), column == 0 ? EditorStyles.label : EditorStyles.miniLabel);
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectionChanged == null || selectedIds == null || selectedIds.Count == 0) return;
            BlackBoxTableRow row;
            if (rowsById.TryGetValue(selectedIds[0], out row)) selectionChanged(row);
        }

        private static int CompareRows(BlackBoxTableRow left, BlackBoxTableRow right, int column)
        {
            string leftValue = GetValue(left, column);
            string rightValue = GetValue(right, column);
            long leftNumber, rightNumber;
            if (TryParseSortableNumber(leftValue, out leftNumber) && TryParseSortableNumber(rightValue, out rightNumber))
            {
                int numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0) return numeric;
            }
            return StringComparer.OrdinalIgnoreCase.Compare(leftValue, rightValue);
        }

        private static string GetValue(BlackBoxTableRow row, int column)
        {
            return row != null && row.values != null && column >= 0 && column < row.values.Length
                ? row.values[column] ?? string.Empty
                : string.Empty;
        }

        private static bool TryParseSortableNumber(string value, out long number)
        {
            value = (value ?? string.Empty).Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out number);
            return long.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number);
        }

        private sealed class BlackBoxTableItem : TreeViewItem
        {
            public readonly BlackBoxTableRow row;
            public BlackBoxTableItem(int id, BlackBoxTableRow row) : base(id, 0, row.values != null && row.values.Length > 0 ? row.values[0] : string.Empty) { this.row = row; }
        }
    }

    internal sealed class BlackBoxTableRow
    {
        public int id;
        public int index;
        public string[] values;
        public string details;
        public ByteRange range;

        public BlackBoxTableRow WithId(int nextId)
        {
            return new BlackBoxTableRow { id = nextId, index = index, values = values, details = details, range = range };
        }
    }

    public sealed partial class BlackBoxInspectorWindow
    {
        private static readonly string[] WorkflowTabs = { "1  Recordings", "2  Mapping", "3  Preview", "4  Apply to vehicle" };
        [SerializeField] private float leftPaneWidth = 240f, rightPaneWidth = 280f;
        [SerializeField] private bool showDetails;
        [SerializeField] private bool technicalView;
        [SerializeField] private string sourceTableSearch = string.Empty;
        [SerializeField] private string tableSearch = string.Empty;
        [SerializeField] private TreeViewState sourceTableState = new TreeViewState();
        [SerializeField] private TreeViewState structureTableState = new TreeViewState();
        [SerializeField] private MultiColumnHeaderState sourceHeaderState;
        [SerializeField] private MultiColumnHeaderState structureHeaderState;
        private SearchField sourceSearchField, structureSearchField;
        private BlackBoxTableView sourceTable, structureTable;
        private int activeSplitter;
        private float splitterStartX, splitterStartWidth;
        private GUIStyle centerStyle, emptyStateStyle;

        private int WorkflowIndex => tab == 2 ? 1 : tab == 4 ? 2 : tab == 5 ? 3 : 0;

        private void DrawWorkspaceShell()
        {
            if (presenter == null) return;
            DrawWorkspaceToolbar();
            if (session == null)
            {
                DrawWorkspaceEmptyState();
                return;
            }
            DrawWorkflowToolbar();
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                DrawLeftPane();
                DrawPaneSplitter(1);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                    try
                    {
                        if (centerStyle == null) centerStyle = new GUIStyle { padding = new RectOffset(12, 12, 0, 0) };
                        using (new EditorGUILayout.VerticalScope(centerStyle, GUILayout.ExpandWidth(true)))
                        {
                            GUILayout.Space(12);
                            if (presenter.Error.Length > 0) EditorGUILayout.HelpBox(presenter.Error, MessageType.Error);
                            DrawWorkflowCenter();
                            GUILayout.Space(12);
                        }
                    }
                    finally { EditorGUILayout.EndScrollView(); }
                }
                if (showDetails && position.width >= 1100) { DrawPaneSplitter(2); DrawRightPane(); }
            }
            DrawWorkspaceFooter();
        }

        private void DrawWorkspaceToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("BLACK BOX AUDIO", EditorStyles.boldLabel, GUILayout.Width(122));
                var next = (BlackBoxSession)EditorGUILayout.ObjectField(session == null ? null : session, typeof(BlackBoxSession), false, GUILayout.MinWidth(150));
                if (next != session) SelectSession(next);
                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(48))) CreateSession();
                using (new EditorGUI.DisabledScope(session == null))
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(48))) Run(SaveSession);
                if (presenter.Busy)
                {
                    GUILayout.Label("Working", EditorStyles.toolbarButton, GUILayout.Width(62));
                    if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(56))) presenter.Cancel();
                }
                GUILayout.FlexibleSpace();
                var state = presenter.Busy ? "JOB RUNNING" : session == null ? "NO SESSION" : sourceLabels.Length == 0 ? "ADD SOURCE" : "READY";
                GUILayout.Label(state, EditorStyles.toolbarButton, GUILayout.Width(86));
                if (GUILayout.Button("Stop preview", EditorStyles.toolbarButton, GUILayout.Width(82))) audition.Stop();
            }
        }

        private void DrawWorkspaceEmptyState()
        {
            if (emptyStateStyle == null) emptyStateStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(16, 16, 16, 16) };
            GUILayout.Space(20);
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(emptyStateStyle, GUILayout.Width(Mathf.Min(620, position.width - 40)), GUILayout.ExpandHeight(false)))
                {
                    GUILayout.Label("Start an evidence session", EditorStyles.boldLabel);
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("Create a session, then add an approved original folder or file. The window keeps source identity, decoded PCM, authored overlays and validation actions in one reviewable scope.", EditorStyles.wordWrappedLabel);
                    GUILayout.Space(12);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Create session", GUILayout.Height(28))) CreateSession();
                        if (GUILayout.Button("Open saved session", GUILayout.Height(28)))
                        {
                            var asset = Selection.activeObject as BlackBoxSession;
                            if (asset != null) SelectSession(asset);
                        }
                    }
                    GUILayout.Space(8);
                    EditorGUILayout.HelpBox("No files are scanned until you explicitly choose a source root. WAV-only evidence cannot restore discarded original tables or controllers.", MessageType.Info);
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.FlexibleSpace();
            GUILayout.Space(20);
        }

        private void DrawWorkflowToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                int next = GUILayout.Toolbar(WorkflowIndex, WorkflowTabs, EditorStyles.toolbarButton);
                if (EditorGUI.EndChangeCheck())
                {
                    technicalView = false;
                    ShowView(next == 0 ? 0 : next == 1 ? 2 : next == 2 ? 4 : 5);
                }
                GUILayout.FlexibleSpace();
                if (position.width >= 1100) showDetails = GUILayout.Toggle(showDetails, "Details", EditorStyles.toolbarButton, GUILayout.Width(54));
                if (GUILayout.Button(technicalView ? "Close technical view" : "Structure / bytes", EditorStyles.toolbarButton, GUILayout.Width(128)))
                {
                    technicalView = !technicalView;
                    if (technicalView) tab = 1;
                    else if (tab == 1) tab = 0;
                    scroll = Vector2.zero;
                }
            }
            if (technicalView)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("Technical view", EditorStyles.boldLabel, GUILayout.Width(95));
                    GUILayout.Label("Paged fields, raw bytes and unresolved spans are linked to the selected source. Use the Sources workflow to return to the evidence summary.", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawLeftPane()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(leftPaneWidth), GUILayout.ExpandHeight(true)))
            {
                GUILayout.Label("Source files", EditorStyles.boldLabel);
                DrawSearchToolbar();
                EnsureSourceTable();
                if (sourceTable != null && sourceLabels.Length > 0)
                {
                    Rect tableRect = GUILayoutUtility.GetRect(80, 170, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    sourceTable.OnGUI(tableRect);
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("No source evidence loaded", EditorStyles.centeredGreyMiniLabel);
                    GUILayout.FlexibleSpace();
                }
                using (new EditorGUI.DisabledScope(presenter.Busy))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Add file", GUILayout.Height(22))) Run(() => { string path = EditorUtility.OpenFilePanel("Select original audio or companion configuration", "", ""); if (path.Length > 0) presenter.AttachFile(path); });
                        if (GUILayout.Button("Add folder", GUILayout.Height(22))) Run(() => { string root = EditorUtility.OpenFolderPanel("Scan only this folder for source evidence", "", ""); if (root.Length > 0) presenter.AttachFolder(root); });
                    }
                    if (GUILayout.Button("Reinspect revisions", GUILayout.Height(22))) Run(presenter.Reinspect);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Copy selected", EditorStyles.miniButtonLeft)) CopySelectedDetails();
                    if (GUILayout.Button("Reset columns", EditorStyles.miniButtonRight)) sourceTable?.ResetColumns(new[] { 155f, 65f });
                }
            }
        }

        private void DrawSearchToolbar()
        {
            if (sourceSearchField == null) sourceSearchField = new SearchField();
            string next = sourceSearchField.OnToolbarGUI(sourceTableSearch, GUILayout.ExpandWidth(true));
            if (next != sourceTableSearch) { sourceTableSearch = next; Repaint(); }
        }

        private void DrawWorkflowCenter()
        {
            if (technicalView || tab == 1) { DrawStructure(); return; }
            switch (tab)
            {
                case 0: DrawSources(); break;
                case 2: DrawRpm(); break;
                case 3: DrawBanks(); break;
                case 4: DrawTrace(); break;
                case 5: DrawNative(); break;
                default: DrawSources(); break;
            }
        }

        private void DrawRightPane()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(rightPaneWidth), GUILayout.ExpandHeight(true)))
            {
                GUILayout.Label("Selection details", EditorStyles.boldLabel);
                var document = Document;
                if (document == null)
                {
                    EditorGUILayout.LabelField("Select a source to inspect its identity, recording list and evidence chain.", EditorStyles.wordWrappedMiniLabel);
                    GUILayout.FlexibleSpace();
                }
                else
                {
                    EditorGUILayout.LabelField(document.Attachment.relativePath, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(document.Report.Variant + " · " + document.Bytes.Length + " bytes", EditorStyles.miniLabel);
                    DrawDetail("SHA-256", document.Report.Source.Sha256);
                    DrawDetail("Parser", document.Report.ParserVersion);
                    var recording = Recording;
                    if (recording != null)
                    {
                        EditorGUILayout.Space(4);
                        GUILayout.Label("Linked recording", EditorStyles.boldLabel);
                        DrawDetail("ID", recording.Id);
                        DrawDetail("Format", recording.SampleRate + " Hz · " + recording.Channels + " ch · " + recording.ValidFrames + " frames");
                        DrawDetail("Codec", recording.Codec + (recording.Decoded ? " · decoded" : " · unavailable"));
                    }
                    if (selectedName.Length > 0)
                    {
                        EditorGUILayout.Space(4);
                        GUILayout.Label("Linked byte selection", EditorStyles.boldLabel);
                        DrawDetail("Field", selectedName);
                        DrawDetail("Range", "0x" + selectedOffset.ToString("X") + " +" + selectedLength + " bytes");
                        if (GUILayout.Button("Copy byte evidence", EditorStyles.miniButton)) CopySelectedDetails();
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.HelpBox("Selection is shared by the navigator, source tables, plots and raw bytes. A changed source hash invalidates authored regions until reviewed.", MessageType.Info);
                }
                GUILayout.Label("Next action", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(NextActionText(), EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawDetail(string label, string value)
        {
            EditorGUILayout.LabelField(label, value ?? string.Empty, EditorStyles.miniLabel);
        }

        private string NextActionText()
        {
            if (presenter.Busy) return "Wait for the current background job or cancel it. Previous evidence remains available.";
            if (sourceLabels.Length == 0) return "Add an approved source file or folder.";
            if (Document == null) return "Select a source in the navigator.";
            if (technicalView) return "Select a row or byte range, then copy its evidence chain if needed.";
            switch (tab)
            {
                case 0: return "Confirm source identity, then choose a decoded recording in RPM mapping.";
                case 2: return "Review an explicit interval and assumptions before adding an authored region.";
                case 3: return "Follow each dependency edge; unresolved controllers remain blocked.";
                case 4: return render == null ? "Render a reviewed overlay or existing native profile." : "Inspect trace decisions, then apply the mapping to your vehicle.";
                case 5: return presenter.Plan == null ? "Build a dry run and review losses before applying." : "Review the planned assets and apply only an explicitly reviewed mapping.";
                default: return "Select a workflow.";
            }
        }

        private void DrawWorkspaceFooter()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(new GUIContent(presenter.Status ?? string.Empty, presenter.Status ?? string.Empty), EditorStyles.miniLabel, GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));
                GUILayout.Label("Volume", EditorStyles.miniLabel, GUILayout.Width(43));
                EditorGUI.BeginChangeCheck(); monitorGain = EditorGUILayout.Slider(monitorGain, 0f, 1f, GUILayout.Width(115));
                if (EditorGUI.EndChangeCheck()) audition.SetGain(monitorGain);
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(42))) audition.Stop();
            }
        }

        private void DrawPaneSplitter(int side)
        {
            Rect rect = GUILayoutUtility.GetRect(5, 1, GUILayout.Width(5), GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
            {
                activeSplitter = side; splitterStartX = evt.mousePosition.x; splitterStartWidth = side == 1 ? leftPaneWidth : rightPaneWidth; evt.Use();
            }
            else if (activeSplitter == side && evt.type == EventType.MouseDrag)
            {
                float delta = evt.mousePosition.x - splitterStartX;
                if (side == 1) leftPaneWidth = Mathf.Clamp(splitterStartWidth + delta, 190f, Mathf.Max(220f, position.width - rightPaneWidth - 420f));
                else rightPaneWidth = Mathf.Clamp(splitterStartWidth - delta, 240f, Mathf.Max(260f, position.width - leftPaneWidth - 420f));
                evt.Use(); Repaint();
            }
            else if (activeSplitter == side && (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp))
            { activeSplitter = 0; evt.Use(); }
        }

        private void EnsureSourceTable()
        {
            if (sourceHeaderState == null || sourceHeaderState.columns == null || sourceHeaderState.columns.Length != 2)
            {
                sourceHeaderState = new MultiColumnHeaderState(new[]
                {
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Source"), width = 155, minWidth = 95, canSort = true, autoResize = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Audio"), width = 65, minWidth = 55, canSort = true, autoResize = false }
                });
            }
            if (sourceTable == null)
            {
                if (sourceTableState == null) sourceTableState = new TreeViewState();
                sourceTable = new BlackBoxTableView(sourceTableState, new MultiColumnHeader(sourceHeaderState), row => SelectSource(row.index));
            }
            var rows = presenter.Documents.Select((doc, index) => new BlackBoxTableRow
            {
                id = StableTableId(doc.Report.Source.Sha256 + index), index = index,
                values = new[] { Path.GetFileName(doc.Attachment.relativePath), doc.IsStale ? "Changed" : doc.Report.Recordings.Count(r => r.Decoded) + " decoded" },
                details = doc.Attachment.relativePath + "\n" + doc.Report.Source.Sha256
            }).Where(row => sourceTableSearch.Length == 0 || string.Join(" ", row.values).IndexOf(sourceTableSearch, StringComparison.OrdinalIgnoreCase) >= 0);
            sourceTable.SetRows(rows);
            if (Document != null) sourceTable.SetSelection(new[] { StableTableId(Document.Report.Source.Sha256 + sourceIndex) });
        }

        private void DrawStructureTableView()
        {
            if (structureSearchField == null) structureSearchField = new SearchField();
            string next = structureSearchField.OnToolbarGUI(tableSearch, GUILayout.ExpandWidth(true));
            if (next != tableSearch) { tableSearch = next; search = next; rowsDirty = true; page = 0; }
            if (structureHeaderState == null || structureHeaderState.columns == null || structureHeaderState.columns.Length != 4)
            {
                structureHeaderState = new MultiColumnHeaderState(new[]
                {
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Field / entry"), width = 180, minWidth = 100, canSort = true, autoResize = false },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Offset"), width = 90, minWidth = 60, canSort = true, autoResize = false },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Value"), width = 180, minWidth = 100, canSort = true, autoResize = false },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Evidence"), width = 220, minWidth = 120, canSort = true, autoResize = false }
                });
            }
            if (structureTable == null) structureTable = new BlackBoxTableView(structureTableState ?? new TreeViewState(), new MultiColumnHeader(structureHeaderState), row => SelectRange(row.values[0], row.range, row.details));
            var visible = rows.Where(row => tableSearch.Length == 0 || (row.name + " " + row.raw + " " + row.value + " " + row.notes).IndexOf(tableSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(100000).Select((row, index) => new BlackBoxTableRow { id = StableTableId(row.name + row.range.Offset + index), index = index, values = new[] { row.name, "0x" + row.range.Offset.ToString("X"), row.value, row.notes }, details = row.notes, range = row.range });
            structureTable.SetRows(visible);
            Rect tableRect = GUILayoutUtility.GetRect(180, 260, GUILayout.ExpandWidth(true));
            structureTable.OnGUI(tableRect);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(rows.Length + " rows · click a row to link details and bytes", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset columns", EditorStyles.miniButton)) structureTable.ResetColumns(new[] { 180f, 90f, 180f, 220f });
            }
        }

        private void CopySelectedDetails()
        {
            if (Document == null) return;
            string text = selectedName.Length > 0
                ? selectedName + "\n0x" + selectedOffset.ToString("X") + " +" + selectedLength + " bytes\n" + selectedNotes
                : Document.Attachment.relativePath + "\n" + Document.Report.Source.Sha256;
            EditorGUIUtility.systemCopyBuffer = text;
            presenter.SetStatus("Copied the selected evidence chain to the system clipboard.");
        }

        private static int StableTableId(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in value ?? string.Empty) { hash ^= c; hash *= 16777619u; }
                return (int)(hash & 0x7fffffff) + 1;
            }
        }
    }
}
