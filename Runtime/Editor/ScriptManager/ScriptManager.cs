using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.IO;
using System.Linq;
using System.Data;
using ExcelDataReader;
using System.Collections.Generic;

public class ScriptManagerWindow : EditorWindow
{
    private ListView scriptList;
    private MultiColumnListView previewTable;
    private Label statusLabel;

    private List<FileInfo> excelFiles = new List<FileInfo>();
    private string excelFolderPath;

    private FileInfo selectedFile;

    [MenuItem("VNovelizer/📜 剧本管理器 (Script Manager)", false, 22)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<ScriptManagerWindow>();
        wnd.titleContent = new GUIContent("剧本管理器");
        wnd.minSize = new Vector2(1000, 600);
    }

    public void CreateGUI()
    {
        // 1. 初始化路径
        var config = VNProjectConfig.Instance;
        if (config == null || config.ExcelSourceFolder == null)
        {
            var error = new Label("请先在 VNProjectConfig 中配置 Excel 源文件夹！")
            {
                style = { color = Color.red, fontSize = 16, unityTextAlign = TextAnchor.MiddleCenter, paddingTop = 50 }
            };
            rootVisualElement.Add(error);
            return;
        }

        excelFolderPath = Path.GetFullPath(AssetDatabase.GetAssetPath(config.ExcelSourceFolder));

        // --- 根布局 ---
        var root = rootVisualElement;
        root.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);

        var splitView = new TwoPaneSplitView(0, 350, TwoPaneSplitViewOrientation.Horizontal);
        root.Add(splitView);

        // ==========================
        //        左侧：文件列表
        // ==========================
        var leftPane = new VisualElement();

        // 工具栏
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.paddingTop = 5;
        toolbar.style.paddingBottom = 5;
        toolbar.style.paddingLeft = 5;
        toolbar.style.paddingRight = 5;

        var createBtn = new Button(CreateNewScript) { text = "➕ 新建", style = { flexGrow = 1 } };
        var convertBtn = new Button(ConvertScripts) { text = "🔄 转换", style = { width = 60, backgroundColor = new Color(0.2f, 0.5f, 0.2f) } };
        var refreshBtn = new Button(RefreshList) { text = "刷新", style = { width = 50 } };

        toolbar.Add(createBtn);
        toolbar.Add(convertBtn);
        toolbar.Add(refreshBtn);
        leftPane.Add(toolbar);

        // 列表
        scriptList = new ListView();
        scriptList.fixedItemHeight = 30;
        scriptList.makeItem = () =>
        {
            var row = new VisualElement() { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 5, height = 30 } };

            var icon = new Image() { style = { width = 16, height = 16, marginRight = 5 } };
            icon.image = EditorGUIUtility.IconContent("TextAsset Icon").image;

            var nameLabel = new Label() { name = "Name", style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } };

            var btnContainer = new VisualElement() { style = { flexDirection = FlexDirection.Row } };
            var renameBtn = new Button() { text = "✎", name = "Rename", style = { width = 25, height = 20 } };
            var playBtn = new Button() { text = "▶", name = "Play", style = { width = 25, height = 20, backgroundColor = new Color(0.2f, 0.2f, 0.5f) } };
            var delBtn = new Button() { text = "×", name = "Delete", style = { width = 25, height = 20, backgroundColor = new Color(0.6f, 0.2f, 0.2f) } };

            btnContainer.Add(renameBtn);
            btnContainer.Add(playBtn);
            btnContainer.Add(delBtn);

            row.Add(icon);
            row.Add(nameLabel);
            row.Add(btnContainer);
            return row;
        };

        scriptList.bindItem = (element, index) =>
        {
            if (index >= excelFiles.Count) return;
            var file = excelFiles[index];
            element.Q<Label>("Name").text = file.Name;

            element.Q<Button>("Rename").clickable = new Clickable(() => RenameScript(file));
            element.Q<Button>("Play").clickable = new Clickable(() => QuickPlay(file));
            element.Q<Button>("Delete").clickable = new Clickable(() => DeleteScript(file));

            // 双击打开
            element.UnregisterCallback<MouseDownEvent>(OnItemMouseDown);
            element.RegisterCallback<MouseDownEvent>(OnItemMouseDown);

            void OnItemMouseDown(MouseDownEvent evt)
            {
                if (evt.clickCount == 2)
                {
                    Application.OpenURL(file.FullName);
                }
            }
        };

        scriptList.itemsSource = excelFiles;
        scriptList.style.flexGrow = 1;
        scriptList.selectionType = SelectionType.Single;
        scriptList.selectionChanged += OnSelectionChanged;

        leftPane.Add(scriptList);
        splitView.Add(leftPane);

        // ==========================
        //        右侧：内容预览
        // ==========================
        var rightPane = new VisualElement();
        rightPane.style.paddingLeft = 10;
        rightPane.style.paddingTop = 10;
        rightPane.style.paddingRight = 10;
        rightPane.style.paddingBottom = 10;

        var previewLabel = new Label("预览区域 (只读)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 5 } };
        rightPane.Add(previewLabel);

        statusLabel = new Label("就绪") { style = { height = 20, color = Color.green } };
        rightPane.Add(statusLabel);

        previewTable = new MultiColumnListView();
        previewTable.style.flexGrow = 1;
        previewTable.showAlternatingRowBackgrounds = AlternatingRowBackground.All;

        rightPane.Add(previewTable);
        splitView.Add(rightPane);

        RefreshList();
    }

    private void RefreshList()
    {
        if (!Directory.Exists(excelFolderPath)) return;

        var dir = new DirectoryInfo(excelFolderPath);
        excelFiles = dir.GetFiles("*.*")
            .Where(f => (f.Extension == ".xlsx" || f.Extension == ".xls") && !f.Name.StartsWith("~$"))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        scriptList.itemsSource = excelFiles;
        scriptList.Rebuild();
        statusLabel.text = $"刷新完成，共 {excelFiles.Count} 个剧本";
    }

    private void OnSelectionChanged(IEnumerable<object> selection)
    {
        foreach (var item in selection)
        {
            selectedFile = item as FileInfo;
            if (selectedFile != null) LoadPreview(selectedFile);
            break;
        }
    }

    private void LoadPreview(FileInfo file)
    {
        previewTable.columns.Clear();
        previewTable.itemsSource = null;

        try
        {
            using (var stream = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    List<List<string>> tableData = new List<List<string>>();
                    List<string> headers = new List<string>();
                    bool isFirstRow = true;

                    while (reader.Read())
                    {
                        var rowList = new List<string>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            object val = reader.GetValue(i);
                            rowList.Add(val != null ? val.ToString() : "");
                        }

                        if (isFirstRow)
                        {
                            headers = rowList;
                            isFirstRow = false;
                        }
                        else
                        {
                            tableData.Add(rowList);
                        }
                    }

                    if (headers.Count == 0) return;

                    for (int c = 0; c < headers.Count; c++)
                    {
                        string headerName = headers[c];
                        if (string.IsNullOrEmpty(headerName)) headerName = $"Col {c}";

                        var col = new Column { name = headerName, title = headerName, width = 100 };

                        int colIndex = c;
                        col.makeCell = () => new Label();
                        col.bindCell = (e, i) =>
                        {
                            if (previewTable.itemsSource == null || i >= previewTable.itemsSource.Count) return;
                            var rowData = (List<string>)previewTable.itemsSource[i];
                            if (colIndex < rowData.Count)
                                (e as Label).text = rowData[colIndex];
                        };
                        previewTable.columns.Add(col);
                    }

                    previewTable.itemsSource = tableData;
                    previewTable.Rebuild();
                    statusLabel.text = $"已加载预览：{file.Name}";
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"预览失败: {e.Message}");
            statusLabel.text = "预览失败：文件被占用";
        }
    }

    private void CreateNewScript()
    {
        string templatePath = "Assets/Resources/VNovelizerRes/ExcelVNScripts/Templates/ScriptTemplate.xlsx";
        if (!File.Exists(templatePath))
        {
            EditorUtility.DisplayDialog("错误", $"找不到模板文件：{templatePath}\n请创建模板。", "确定");
            return;
        }

        string path = EditorUtility.SaveFilePanel("新建剧本", excelFolderPath, "NewChapter", "xlsx");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            File.Copy(templatePath, path);
            RefreshList();
            statusLabel.text = $"已创建：{Path.GetFileName(path)}";
            Application.OpenURL(path);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"创建失败: {e.Message}");
        }
    }

    private void RenameScript(FileInfo file)
    {
        RenamePopup.Show(file.Name, (newName) =>
        {
            if (string.IsNullOrEmpty(newName)) return;
            if (!newName.EndsWith(".xlsx")) newName += ".xlsx";

            string newPath = Path.Combine(file.DirectoryName, newName);
            if (File.Exists(newPath))
            {
                EditorUtility.DisplayDialog("错误", "文件名已存在！", "确定");
                return;
            }

            try
            {
                file.MoveTo(newPath);
                RefreshList();
            }
            catch (IOException) { EditorUtility.DisplayDialog("错误", "文件被占用，无法重命名。", "确定"); }
        });
    }

    private void DeleteScript(FileInfo file)
    {
        if (EditorUtility.DisplayDialog("删除剧本", $"确定要删除 {file.Name} 吗？\n此操作将同时删除对应的 CSV 文件。\n此操作无法撤销！", "删除", "取消"))
        {
            // 1. 获取 CSV 文件路径
            string csvFileName = Path.GetFileNameWithoutExtension(file.Name) + ".csv";
            var config = VNProjectConfig.Instance;

            // 确保 Config 存在且路径已配置
            if (config != null && config.CsvOutputFolder != null)
            {
                string csvFolderPath = Path.GetFullPath(AssetDatabase.GetAssetPath(config.CsvOutputFolder));
                string csvPath = Path.Combine(csvFolderPath, csvFileName);

                // 2. 如果 CSV 存在，删除它
                if (File.Exists(csvPath))
                {
                    try
                    {
                        File.Delete(csvPath);
                        File.Delete(csvPath + ".meta"); // 顺便删掉 meta 文件，保持 Unity 干净
                        Debug.Log($"[ScriptManager] 已同步删除 CSV: {csvFileName}");
                    }
                    catch (IOException e)
                    {
                        Debug.LogWarning($"[ScriptManager] 无法删除 CSV 文件: {e.Message}");
                    }
                }
            }

            // 3. 删除 Excel 文件
            try
            {
                file.Delete();
                // 尝试删除 meta 文件 (Excel 的 meta)
                string metaPath = file.FullName + ".meta";
                if (File.Exists(metaPath)) File.Delete(metaPath);
            }
            catch (IOException e)
            {
                Debug.LogError($"删除 Excel 失败: {e.Message}");
            }

            // 4. 刷新 Unity 资源数据库 (让 Unity 知道文件没了)
            AssetDatabase.Refresh();

            // 5. 刷新列表
            RefreshList();
        }
    }

    private void ConvertScripts()
    {
        statusLabel.text = "正在转换...";
        ExcelToCsvConverter.ConvertAllExcelFiles();
        statusLabel.text = "转换完成！";
        RefreshList();
    }

    private void QuickPlay(FileInfo file)
    {
        string scriptName = Path.GetFileNameWithoutExtension(file.Name);

        PlayerPrefs.SetString("Debug_ScriptName", scriptName);
        PlayerPrefs.SetString("Debug_LineID", "");
        PlayerPrefs.SetInt("Debug_Mode", 1);
        PlayerPrefs.Save();

        if (!EditorApplication.isPlaying)
        {
            string scenePath = "Assets/Scenes/VNDebugScene.unity";
            if (System.IO.File.Exists(scenePath))
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
                EditorApplication.isPlaying = true;
            }
            else
            {
                Debug.LogError($"找不到 DebugScene，路径错误：{scenePath}");
            }
        }
    }
}

public class RenamePopup : EditorWindow
{
    private string fileName;
    private System.Action<string> onConfirm;

    public static void Show(string currentName, System.Action<string> callback)
    {
        var win = GetWindow<RenamePopup>(true, "重命名", true);
        win.fileName = currentName;
        win.onConfirm = callback;
        win.minSize = new Vector2(300, 80);
        win.maxSize = new Vector2(300, 80);
        win.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        fileName = EditorGUILayout.TextField("新文件名:", fileName);
        EditorGUILayout.Space(10);
        if (GUILayout.Button("确定"))
        {
            onConfirm?.Invoke(fileName);
            Close();
        }
    }
}