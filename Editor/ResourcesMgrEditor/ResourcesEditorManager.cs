using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class ResourceManagerWindow : EditorWindow
{
    public enum ResType { Background, Video, BGM, SFX, Voice}

    private VisualElement rightPane;
    private ListView leftMenu;
    // 换回 TextField 以兼容旧版，或者用 ToolbarSearchField
    private TextField searchField;

    private ResType currentType = ResType.Background;
    private string searchKeyword = "";

    [MenuItem("VNovelizer/🗂️ 资源管理器 (Resource Manager)",false,23)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<ResourceManagerWindow>();
        wnd.titleContent = new GUIContent("资源管理器");
        wnd.minSize = new Vector2(1000, 600);
    }

    public void CreateGUI()
    {
        var root = rootVisualElement;
        root.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);

        var splitView = new TwoPaneSplitView(0, 200, TwoPaneSplitViewOrientation.Horizontal);
        root.Add(splitView);

        // --- 左侧：菜单 ---
        var leftPane = new VisualElement();
        leftPane.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
        leftPane.style.paddingTop = 10;

        var title = new Label("资源分类")
        {
            style = { fontSize = 14, unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 10, marginBottom = 10, color = new Color(0.7f, 0.7f, 0.7f) }
        };
        leftPane.Add(title);

        var types = System.Enum.GetValues(typeof(ResType)).Cast<ResType>().ToList();
        leftMenu = new ListView();
        leftMenu.itemsSource = types;
        leftMenu.makeItem = () => new Label() { style = { paddingLeft = 10, paddingTop = 8, paddingBottom = 8, fontSize = 13 } };
        leftMenu.bindItem = (e, i) => { (e as Label).text = GetTypeName(types[i]); };
        leftMenu.selectionType = SelectionType.Single;

        leftMenu.selectionChanged += (items) => {
            foreach (var item in items)
            {
                currentType = (ResType)item;
                RefreshRightPane();
                break;
            }
        };

        leftPane.Add(leftMenu);
        splitView.Add(leftPane);

        // --- 右侧：内容 ---
        var rightContainer = new VisualElement();
        rightContainer.style.paddingTop = 10;
        rightContainer.style.paddingLeft = 10;
        rightContainer.style.paddingRight = 10;

        // 工具栏
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.marginBottom = 10;

        // 【新增】导入按钮
        var importBtn = new Button(ImportFile) { text = "➕ 导入文件...", style = { height = 20, backgroundColor = new Color(0.2f, 0.6f, 0.2f), color = Color.white } };

        searchField = new TextField() { style = { flexGrow = 1, marginLeft = 10 } };
        // searchField.placeholder = "搜索..."; // 旧版Unity注释掉这行
        searchField.RegisterValueChangedCallback(evt => {
            searchKeyword = evt.newValue.ToLower();
            RefreshRightPane();
        });

        var refreshBtn = new Button(RefreshRightPane) { text = "刷新", style = { width = 60 } };

        toolbar.Add(importBtn);
        toolbar.Add(searchField);
        toolbar.Add(refreshBtn);
        rightContainer.Add(toolbar);

        var scrollView = new ScrollView();
        rightPane = new VisualElement();
        rightPane.style.flexDirection = FlexDirection.Row;
        rightPane.style.flexWrap = Wrap.Wrap;

        scrollView.Add(rightPane);
        rightContainer.Add(scrollView);

        splitView.Add(rightContainer);

        leftMenu.SetSelection(0);
    }

    // --- 核心功能：导入文件 ---
    private void ImportFile()
    {
        // 1. 获取目标路径 (Unity Assets 路径)
        string targetAssetPath = GetPathFromConfig(currentType);
        if (!Directory.Exists(targetAssetPath))
        {
            EditorUtility.DisplayDialog("错误", $"目标文件夹不存在：\n{targetAssetPath}\n\n请检查配置文件。", "确定");
            return;
        }

        // 2. 打开系统文件选择框
        string extensionFilter = GetExtensionFilter(currentType);
        string srcPath = EditorUtility.OpenFilePanel($"导入 {currentType}", "", extensionFilter);

        if (string.IsNullOrEmpty(srcPath)) return; // 用户取消

        // 3. 计算目标绝对路径
        string fileName = Path.GetFileName(srcPath);
        // 将 Assets/Resources/... 转换为系统绝对路径
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string destPath = Path.Combine(projectRoot, targetAssetPath, fileName);

        // 4. 检查重名
        if (File.Exists(destPath))
        {
            bool replace = EditorUtility.DisplayDialog("文件已存在", $"文件 '{fileName}' 已存在，要覆盖吗？", "覆盖", "取消");
            if (!replace) return;
        }

        // 5. 复制文件
        try
        {
            File.Copy(srcPath, destPath, true);
            AssetDatabase.Refresh(); // 强制刷新 Unity 资源数据库
            Debug.Log($"[ResourceManager] 成功导入: {fileName}");
            RefreshRightPane();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"导入失败: {e.Message}");
        }
    }

    // --- 核心功能：删除文件 ---
    private void DeleteAsset(string assetPath)
    {
        if (EditorUtility.DisplayDialog("删除确认", $"确定要删除文件吗？\n\n{assetPath}\n\n此操作无法撤销！", "删除", "取消"))
        {
            AssetDatabase.DeleteAsset(assetPath);
            RefreshRightPane();
        }
    }

    private void RefreshRightPane()
    {
        rightPane.Clear();
        string path = GetPathFromConfig(currentType);

        if (!Directory.Exists(path)) return;

        // Video 类型在 StreamingAssets 中，需要特殊处理
        if (currentType == ResType.Video)
        {
            RefreshVideoPane(path);
            return;
        }

        string filter = GetSearchFilter(currentType);
        string[] guids = AssetDatabase.FindAssets(filter, new[] { path });

        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(assetPath);

            if (!string.IsNullOrEmpty(searchKeyword) && !fileName.ToLower().Contains(searchKeyword))
                continue;

            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            CreateAssetCard(asset, fileName, assetPath);
        }
    }

    /// <summary>
    /// 刷新视频面板（StreamingAssets 中的视频文件）
    /// </summary>
    private void RefreshVideoPane(string path)
    {
        // 支持的视频文件扩展名
        string[] videoExtensions = { ".mp4", ".mov", ".webm", ".avi", ".asf", ".wmv" };

        // 获取目录下所有文件
        string[] allFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly);

        foreach (string filePath in allFiles)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            
            // 检查是否是视频文件
            if (!videoExtensions.Contains(extension))
                continue;

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string assetPath = filePath.Replace('\\', '/');

            // 过滤搜索关键词
            if (!string.IsNullOrEmpty(searchKeyword) && !fileName.ToLower().Contains(searchKeyword))
                continue;

            // 对于 StreamingAssets 中的文件，使用 DefaultAsset
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null)
            {
                // 如果无法加载为资源，创建一个占位符对象
                asset = new UnityEngine.Object();
            }
            
            CreateAssetCard(asset, fileName, assetPath);
        }
    }

    private void CreateAssetCard(Object asset, string name, string fullPath)
    {
        var card = new VisualElement();
        card.style.width = 120;
        card.style.height = 140;
        card.style.marginRight = 10;
        card.style.marginBottom = 10;
        card.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        // 简写边框，如果报错请展开写 borderTopWidth 等
        card.style.borderTopWidth = 1; card.style.borderBottomWidth = 1;
        card.style.borderLeftWidth = 1; card.style.borderRightWidth = 1;
        card.style.borderTopColor = Color.black; card.style.borderBottomColor = Color.black;
        card.style.borderLeftColor = Color.black; card.style.borderRightColor = Color.black;

        // --- 预览图 ---
        var icon = new Image();
        icon.style.width = 100;
        icon.style.height = 100;
        icon.style.marginTop = 5;
        icon.style.alignSelf = Align.Center; // 居中
        icon.scaleMode = ScaleMode.ScaleToFit;

        Texture2D preview = AssetPreview.GetAssetPreview(asset);
        if (preview == null) preview = AssetPreview.GetMiniThumbnail(asset);
        icon.image = preview;

        // --- 文件名 ---
        var label = new Label(name);
        label.style.overflow = Overflow.Hidden;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.width = 110;
        label.style.alignSelf = Align.Center;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.fontSize = 11;

        // --- 删除按钮 (悬浮在右上角) ---
        var delBtn = new Button(() => DeleteAsset(fullPath)) { text = "×" };
        delBtn.style.position = Position.Absolute;
        delBtn.style.top = 0;
        delBtn.style.right = 0;
        delBtn.style.width = 20;
        delBtn.style.height = 20;
        delBtn.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f); // 红色
        delBtn.style.color = Color.white;
        delBtn.style.borderTopWidth = 0; delBtn.style.borderBottomWidth = 0;
        delBtn.style.borderLeftWidth = 0; delBtn.style.borderRightWidth = 0;

        card.Add(icon);
        card.Add(label);
        card.Add(delBtn);

        // 交互
        card.RegisterCallback<MouseDownEvent>(evt => {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            if (evt.clickCount == 2) AssetDatabase.OpenAsset(asset);
        });

        rightPane.Add(card);
    }

    // --- 辅助方法 ---

    private string GetTypeName(ResType type)
    {
        switch (type)
        {
            case ResType.Background: return "🖼️ 背景 (Backgrounds)";
            case ResType.BGM: return "🎵 背景音乐 (BGM)";
            case ResType.SFX: return "🔊 音效 (SFX)";
            case ResType.Voice: return "🎙️ 语音 (Voice)";
            case ResType.Video: return "🎬 视频 (Videos)";
            default: return type.ToString();
        }
    }

    private string GetPathFromConfig(ResType type)
    {
        var config = VNProjectConfig.Instance;
        if (config == null) return "";

        // Video 类型使用 StreamingAssets，其他使用 Resources
        if (type == ResType.Video)
        {
            return "Assets/StreamingAssets/" + config.VideoResPath;
        }

        string prefix = "Assets/Resources/";
        switch (type)
        {
            case ResType.Background: return prefix + config.BackgroundResPath;
            case ResType.BGM: return prefix + config.BgmResPath;
            case ResType.SFX: return prefix + config.SFXResPath;
            case ResType.Voice: return prefix + config.VoiceResPath;
            default: return "";
        }
    }

    // 定义不同类型的文件后缀过滤
    private string GetExtensionFilter(ResType type)
    {
        switch (type)
        {
            case ResType.Background: return "png,jpg,jpeg,tga";
            case ResType.BGM:
            case ResType.SFX:
            case ResType.Voice: return "mp3,wav,ogg,aiff";
            case ResType.Video: return "mp4,mov,webm,avi";
            default: return "";
        }
    }

    private string GetSearchFilter(ResType type)
    {
        if (type == ResType.Background) return "t:Sprite";
        if (type == ResType.BGM || type == ResType.SFX || type == ResType.Voice) return "t:AudioClip";
        // Video 在 StreamingAssets 中是原始文件，使用 DefaultAsset 或直接查找文件扩展名
        if (type == ResType.Video) return ""; // 空字符串表示查找所有文件，我们会在 RefreshRightPane 中过滤
        return "t:Object";
    }
}