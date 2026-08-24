using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目设置页：Edit → Project Settings → VNovelizer（方案 A：配置数据存于
/// Assets/Resources/VNProjectConfig.asset，Project Settings 窗口作为编辑入口）。
///
/// - UI 入口挂进 Unity Project Settings 窗口（用户不必去 Assets 里找配置资产）；
/// - 数据本体仍是 Assets/Resources/VNProjectConfig.asset（运行时零成本读取通道，
///   Project Settings 只是 UI 入口，不存数据）；
/// - 首次打开自动创建配置资产（用户主动行为）。
///
/// 本版按功能拆分为子页面（左侧导航 + 右侧内容），与 Unity 标准 Project Settings
/// 风格保持一致。子页面归并依据：相关功能的字段集中展示，避免一个长列表无重点。
/// </summary>
internal class VNProjectSettingsProvider : SettingsProvider
{
    private Editor _editor;
    private VNProjectConfig _config;

    private enum SubPage { Resources, Startup, Localization, Security, Theater, UIPrefabs }
    private SubPage _currentPage = SubPage.Resources;

    private static readonly string[] PageLabels =
    {
        "资源 / Resources",
        "启动 / Startup",
        "本地化 / Localization",
        "加密 / Security",
        "剧场 / Theater",
        "UI 模板 / UI Prefabs"
    };

    private VNProjectSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
        : base(path, scope) { }

    [SettingsProvider]
    public static SettingsProvider CreateProvider()
    {
        var provider = new VNProjectSettingsProvider("Project/VNovelizer")
        {
            label = "VNovelizer",
            keywords = new[] { "VNovelizer", "VN", "Visual Novel", "剧本", "UI 模板" }
        };
        return provider;
    }

    public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
    {
        _config = LoadOrCreateConfig();
        if (_config != null)
        {
            _editor = Editor.CreateEditor(_config);
        }
    }

    public override void OnTitleBarGUI()
    {
        if (_config != null)
        {
            GUILayout.Space(4);
            if (GUILayout.Button("在 Project 中定位", EditorStyles.miniButton, GUILayout.Width(110)))
            {
                Selection.activeObject = _config;
                EditorGUIUtility.PingObject(_config);
            }
        }
    }

    public override void OnGUI(string searchContext)
    {
        if (_config == null)
        {
            EditorGUILayout.HelpBox("无法加载配置资产。", MessageType.Error);
            return;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();

        // 左侧：子页面导航
        DrawSubPageSidebar();

        // 右侧：当前子页面内容
        EditorGUILayout.BeginVertical(GUILayout.MaxWidth(800));
        DrawCurrentSubPage();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    public override void OnDeactivate()
    {
        if (_editor != null)
        {
            Object.DestroyImmediate(_editor);
            _editor = null;
        }
    }

    /// <summary>左侧子页面导航栏</summary>
    private void DrawSubPageSidebar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(190));
        EditorGUILayout.LabelField("VNovelizer", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        for (int i = 0; i < PageLabels.Length; i++)
        {
            bool isActive = (int)_currentPage == i;
            var style = isActive ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
            if (GUILayout.Toggle(isActive, PageLabels[i], style) != isActive)
            {
                _currentPage = (SubPage)i;
            }
        }
        EditorGUILayout.EndVertical();
    }

    /// <summary>右侧子页面内容（按当前 _currentPage 派发到具体子页渲染）</summary>
    private void DrawCurrentSubPage()
    {
        EditorGUI.BeginChangeCheck();

        switch (_currentPage)
        {
            case SubPage.Resources:    DrawResourcesPage();    break;
            case SubPage.Startup:      DrawStartupPage();      break;
            case SubPage.Localization: DrawLocalizationPage(); break;
            case SubPage.Security:     DrawSecurityPage();     break;
            case SubPage.Theater:      DrawTheaterPage();      break;
            case SubPage.UIPrefabs:    DrawUIPrefabsPage();    break;
        }

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
        }
    }

    // ==================== 子页面渲染 ====================

    /// <summary>资源页：路径前缀（只读）+ Excel/CSV 工作流 + 默认 Sprite</summary>
    private void DrawResourcesPage()
    {
        EditorGUILayout.LabelField("工作流", EditorStyles.boldLabel);
        DrawFields("ExcelSourceFolder", "CsvOutputFolder", "AutoConvertExcel");
        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("默认 UI 资源", EditorStyles.boldLabel);
        DrawFields("DefaultSpeakerBoxSprite", "DefaultHeadFrameSprite");
        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("引擎内部地址（只读）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "VN 引擎内部地址前缀，正常使用无需修改。\n" +
            "如需自定义媒体资源位置，请在资源管理器窗口拖放分配（与路径无关）。",
            MessageType.Info);
        DrawFields(
            "VNScriptResPath", "BackgroundResPath", "VideoResPath", "CharacterResPath",
            "ParticalEffectPath", "AnimationPath",
            "BgmResPath", "SFXResPath", "VoiceResPath");
    }

    /// <summary>启动页：默认剧本与行 ID</summary>
    private void DrawStartupPage()
    {
        EditorGUILayout.LabelField("游戏启动", EditorStyles.boldLabel);
        DrawFields("DefaultScriptName", "DefaultLineID");
    }

    /// <summary>本地化页</summary>
    private void DrawLocalizationPage()
    {
        EditorGUILayout.LabelField("剧情本地化", EditorStyles.boldLabel);
        DrawFields("EnableLocalization", "LocalizationCollectionName", "ScriptTablePrefix", "FallbackToCsvWhenMissing");
    }

    /// <summary>加密页</summary>
    private void DrawSecurityPage()
    {
        EditorGUILayout.LabelField("AES 存档加密", EditorStyles.boldLabel);
        DrawFields("UseAES");

        if (!_config.UseAES)
        {
            EditorGUILayout.HelpBox("开发期建议关闭；发布前开启并配置密钥。", MessageType.Info);
            return;
        }

        DrawFields("Key", "IV");

        EditorGUILayout.Space(4);
        if (GUILayout.Button("生成随机密钥与偏移向量"))
        {
            _config.GenerateRandomKey();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>剧场页</summary>
    private void DrawTheaterPage()
    {
        EditorGUILayout.LabelField("剧场相机", EditorStyles.boldLabel);
        DrawFields("CustomSceneCameraPrefab");
    }

    /// <summary>UI 模板页：所有 Override_* 字段 + 模板创建按钮（复用 VNProjectConfigEditor 完整 UI）</summary>
    private void DrawUIPrefabsPage()
    {
        EditorGUILayout.LabelField("UI 模板覆写", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "留空 = 使用包内默认模板（无需任何配置）。\n" +
            "要自定义某个 UI，点击下方按钮从模板创建副本（位置自选）。",
            MessageType.None);

        // 复用自定义 Inspector 的"从模板创建"按钮（一致入口）
        if (GUILayout.Button("从模板创建自定义 UI…", GUILayout.Height(28)))
        {
            VNUIPrefabTemplateCreator.ShowTemplateMenu(_config);
        }
        EditorGUILayout.Space(4);

        // 全部 Override_* 字段：复用 Editor 的批量渲染（不重复硬编码）
        if (_editor != null)
        {
            var so = new SerializedObject(_config);
            so.Update();
            var iter = so.GetIterator();
            iter.NextVisible(true);
            while (iter.NextVisible(false))
            {
                if (!iter.name.StartsWith("Override_")) continue;
                EditorGUILayout.PropertyField(iter, true);
            }
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>通过 SerializedObject 按字段名渲染（保持只读与可编辑语义一致）</summary>
    private void DrawFields(params string[] fieldNames)
    {
        if (_editor == null) return;
        var so = new SerializedObject(_config);
        so.Update();
        foreach (var name in fieldNames)
        {
            var prop = so.FindProperty(name);
            if (prop == null) continue;
            EditorGUILayout.PropertyField(prop, true);
        }
        so.ApplyModifiedProperties();
    }

    /// <summary>加载配置资产；不存在则创建（首次打开设置页 = 用户主动创建配置）</summary>
    private static VNProjectConfig LoadOrCreateConfig()
    {
        VNProjectConfig.TryGetInstance(out var config);
        if (config != null) return config;

        string folder = "Assets/Resources";
        string path = folder + "/VNProjectConfig.asset";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Resources");

        config = ScriptableObject.CreateInstance<VNProjectConfig>();
        AssetDatabase.CreateAsset(config, path);
        AssetDatabase.SaveAssets();
        Debug.Log("[VNProjectSettings] 已创建配置资产: " + path);

        // 顺带为剧本工作流填默认文件夹引用（文件夹存在时）
        var excelFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(VNProjectPaths.ExcelFolder);
        var csvFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(VNProjectPaths.ScriptsFolder);
        if (excelFolder != null) config.ExcelSourceFolder = excelFolder;
        if (csvFolder != null) config.CsvOutputFolder = csvFolder;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        return config;
    }
}
