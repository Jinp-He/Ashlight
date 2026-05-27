using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DataTableManager
{
    public class DataTableManagerWindow : EditorWindow
    {
        const int PreviewMaxRows = 500;
        const float LeftWidth = 320f;
        const float ColWidth = 150f;
        const float RowNumWidth = 40f;

        [MenuItem("Tools/DataTable Manager")]
        public static void Open()
        {
            var w = GetWindow<DataTableManagerWindow>("DataTable Manager");
            w.minSize = new Vector2(960, 520);
            w.Show();
        }

        string _projectRoot;
        string _datasRoot;
        string _genScript;
        List<string> _files = new List<string>();
        string _search = "";
        string _selected;
        WorkbookData _loaded;
        string _loadError;
        int _sheetIndex;
        string[] _sheetNames = Array.Empty<string>();
        Vector2 _leftScroll, _previewScroll;

        void OnEnable()
        {
            _projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _datasRoot = Path.Combine(_projectRoot, "DataTables", "Datas");
            var scriptName = Application.platform == RuntimePlatform.WindowsEditor ? "gen.bat" : "gen.sh";
            _genScript = Path.Combine(_projectRoot, "DataTables", scriptName);
            RefreshFiles();
        }

        void RefreshFiles()
        {
            _files.Clear();
            if (!Directory.Exists(_datasRoot)) return;
            foreach (var p in Directory.GetFiles(_datasRoot, "*.xlsx", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(p);
                if (name.StartsWith("~$") || name.StartsWith("~")) continue;
                _files.Add(p);
            }
            _files.Sort(StringComparer.OrdinalIgnoreCase);
        }

        void OnGUI()
        {
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeft();
                DrawRight();
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    RefreshFiles();
                if (GUILayout.Button("Run gen", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    RunGen();
                if (GUILayout.Button("Refresh Assets", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    AssetDatabase.Refresh();
                if (GUILayout.Button("Open DataTables", EditorStyles.toolbarButton, GUILayout.Width(120)))
                    EditorUtility.RevealInFinder(_datasRoot + Path.DirectorySeparatorChar);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Search", GUILayout.Width(48));
                _search = GUILayout.TextField(_search ?? "", EditorStyles.toolbarTextField, GUILayout.Width(220));
                if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20)))
                    _search = "";
            }
        }

        void DrawLeft()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftWidth)))
            {
                using (var sv = new EditorGUILayout.ScrollViewScope(_leftScroll))
                {
                    _leftScroll = sv.scrollPosition;
                    string curDir = null;
                    bool any = false;
                    foreach (var f in _files)
                    {
                        if (!string.IsNullOrEmpty(_search) &&
                            f.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        any = true;
                        var rel = GetRelative(f);
                        var relDir = Path.GetDirectoryName(rel);
                        if (relDir != curDir)
                        {
                            curDir = relDir;
                            EditorGUILayout.LabelField(
                                string.IsNullOrEmpty(relDir) ? "(root)" : relDir,
                                EditorStyles.boldLabel);
                        }
                        DrawFileRow(f);
                    }
                    if (!any)
                        EditorGUILayout.HelpBox(
                            _files.Count == 0
                                ? $"No .xlsx under:\n{_datasRoot}"
                                : "No match for search.",
                            MessageType.Info);
                }
            }
        }

        void DrawFileRow(string fullPath)
        {
            var name = Path.GetFileName(fullPath);
            bool selected = fullPath == _selected;
            var prevColor = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.35f, 0.55f, 0.9f, 1f);
            var content = new GUIContent("  " + name, fullPath);
            if (GUILayout.Button(content, selected ? EditorStyles.toolbarButton : EditorStyles.label))
            {
                _selected = fullPath;
                LoadSelected();
            }
            GUI.backgroundColor = prevColor;
        }

        string GetRelative(string full)
        {
            if (!full.StartsWith(_datasRoot, StringComparison.OrdinalIgnoreCase)) return full;
            return full.Substring(_datasRoot.Length).TrimStart(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        void LoadSelected()
        {
            _loaded = null;
            _loadError = null;
            _sheetIndex = 0;
            _sheetNames = Array.Empty<string>();
            if (string.IsNullOrEmpty(_selected) || !File.Exists(_selected)) return;
            try
            {
                _loaded = XlsxReader.Read(_selected, PreviewMaxRows);
                _sheetNames = _loaded.Sheets.Select(s => s.Name).ToArray();
            }
            catch (Exception e)
            {
                _loadError = e.Message;
                Debug.LogError($"[DataTableManager] Failed reading {_selected}: {e}");
            }
        }

        void DrawRight()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (string.IsNullOrEmpty(_selected))
                {
                    EditorGUILayout.HelpBox("从左边选择一个 .xlsx 文件查看预览。", MessageType.Info);
                    return;
                }
                DrawDetailToolbar();
                DrawFileHeader();
                if (!string.IsNullOrEmpty(_loadError))
                {
                    EditorGUILayout.HelpBox("读取失败: " + _loadError, MessageType.Error);
                    return;
                }
                DrawPreview();
            }
        }

        void DrawDetailToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Open in Excel", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    OpenExternal(_selected);
                if (GUILayout.Button("Reveal", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    EditorUtility.RevealInFinder(_selected);
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    LoadSelected();
                GUILayout.FlexibleSpace();
                if (_sheetNames.Length > 0)
                {
                    GUILayout.Label("Sheet", GUILayout.Width(40));
                    _sheetIndex = EditorGUILayout.Popup(
                        _sheetIndex, _sheetNames,
                        EditorStyles.toolbarPopup, GUILayout.Width(200));
                }
            }
        }

        void DrawFileHeader()
        {
            var info = new FileInfo(_selected);
            EditorGUILayout.LabelField(GetRelative(_selected), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Size: {info.Length / 1024f:F1} KB    Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm}",
                EditorStyles.miniLabel);
        }

        void DrawPreview()
        {
            if (_loaded == null || _loaded.Sheets.Count == 0)
            {
                EditorGUILayout.HelpBox("表无数据或未加载。", MessageType.None);
                return;
            }
            _sheetIndex = Mathf.Clamp(_sheetIndex, 0, _loaded.Sheets.Count - 1);
            var sheet = _loaded.Sheets[_sheetIndex];
            int cols = sheet.ColumnCount;
            int rows = sheet.Rows.Count;
            if (cols == 0 || rows == 0)
            {
                EditorGUILayout.HelpBox("空表。", MessageType.None);
                return;
            }

            if (sheet.Truncated)
                EditorGUILayout.HelpBox(
                    $"预览已截断至前 {PreviewMaxRows} 行。完整内容请用 Open in Excel。",
                    MessageType.Warning);

            var lineH = EditorGUIUtility.singleLineHeight;
            var headerStyle = EditorStyles.miniBoldLabel;
            var lubanHeaderStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };

            using (var sv = new EditorGUILayout.ScrollViewScope(_previewScroll))
            {
                _previewScroll = sv.scrollPosition;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("#", headerStyle, GUILayout.Width(RowNumWidth));
                    for (int c = 0; c < cols; c++)
                        GUILayout.Label(ColToLetter(c + 1), headerStyle, GUILayout.Width(ColWidth));
                }
                for (int r = 0; r < rows; r++)
                {
                    var row = sheet.Rows[r];
                    bool isLubanHeader = row.Count > 0 && !string.IsNullOrEmpty(row[0]) && row[0].StartsWith("##");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label((r + 1).ToString(), EditorStyles.miniLabel,
                            GUILayout.Width(RowNumWidth), GUILayout.Height(lineH));
                        for (int c = 0; c < cols; c++)
                        {
                            string val = c < row.Count ? row[c] : "";
                            if (isLubanHeader)
                                GUILayout.Label(val, lubanHeaderStyle,
                                    GUILayout.Width(ColWidth), GUILayout.Height(lineH));
                            else
                                EditorGUILayout.SelectableLabel(val, EditorStyles.label,
                                    GUILayout.Width(ColWidth), GUILayout.Height(lineH));
                        }
                    }
                }
            }
        }

        static string ColToLetter(int col)
        {
            string s = "";
            while (col > 0)
            {
                int r = (col - 1) % 26;
                s = (char)('A' + r) + s;
                col = (col - 1) / 26;
            }
            return s;
        }

        void OpenExternal(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataTableManager] Open failed: {e.Message}");
            }
        }

        void RunGen()
        {
            if (!File.Exists(_genScript))
            {
                EditorUtility.DisplayDialog(
                    "gen script missing",
                    $"找不到生成脚本:\n{_genScript}",
                    "OK");
                return;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _genScript,
                    WorkingDirectory = Path.GetDirectoryName(_genScript) ?? _projectRoot,
                    UseShellExecute = true,
                };
                Process.Start(psi);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataTableManager] Run gen failed: {e.Message}");
                EditorUtility.DisplayDialog("Run gen failed", e.Message, "OK");
            }
        }
    }
}
