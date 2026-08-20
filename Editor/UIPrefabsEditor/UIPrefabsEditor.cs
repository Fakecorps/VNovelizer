using UnityEditor;
using UnityEngine;
using System.Linq;
using UnityEngine.UIElements;

/// <summary>
/// UI 预制体管理器：查看每个核心面板当前生效的模板并进入编辑。
///
/// 模板解析（与运行时 VNUIPrefabs 同一语义，见 Docs/VNResourceProviderRefactoring.md）：
/// 1. VNProjectConfig"八、UI 模板覆写"指派的自定义模板（优先）；
/// 2. 旧版用户副本（Assets/Resources，存量项目）；
/// 3. 包内默认模板（Packages/...，只注册不复制）。
///
/// 注意：直接编辑包内默认模板会影响所有使用该包的项目且包升级时丢失——
/// 窗口对此给出警示并引导"从模板创建自定义"副本（复制 + 自动指派覆写）。
/// </summary>
public class UIEditorWindow : EditorWindow
{
    private ListView leftMenu;
    private VisualElement rightPane;

    public enum UIType
    {
        Gameplay, Pause, History, SaveLoad, Settings, Choice, Confirm, MainMenu, Loading
    }

    private UIType currentType = UIType.Gameplay;

    [MenuItem("VNovelizer/UI预制体管理器 (UIPrefabs Manager)", false, 24)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<UIEditorWindow>();
        wnd.titleContent = new GUIContent("UI预制体管理器");
        wnd.minSize = new Vector2(600, 300);
    }

    public void CreateGUI()
    {
        var root = rootVisualElement;
        root.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);

        var splitView = new TwoPaneSplitView(0, 200, TwoPaneSplitViewOrientation.Horizontal);
        root.Add(splitView);

        // --- 左侧菜单 ---
        var leftPane = new VisualElement();
        leftPane.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
        leftPane.style.paddingTop = 10;

        var title = new Label("核心面板")
        {
            style = { fontSize = 14, unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 10, marginBottom = 10, color = new Color(0.7f, 0.7f, 0.7f) }
        };
        leftPane.Add(title);

        var types = System.Enum.GetValues(typeof(UIType)).Cast<UIType>().ToList();
        leftMenu = new ListView();
        leftMenu.itemsSource = types;
        leftMenu.makeItem = () => new Label() { style = { paddingLeft = 10, paddingTop = 8, paddingBottom = 8, fontSize = 13 } };
        leftMenu.bindItem = (e, i) => { (e as Label).text = GetTypeName(types[i]); };
        leftMenu.selectionType = SelectionType.Single;

        leftMenu.selectionChanged += (items) => {
            foreach (var item in items)
            {
                currentType = (UIType)item;
                RefreshRightPane();
                break;
            }
        };

        leftPane.Add(leftMenu);
        splitView.Add(leftPane);

        // --- 右侧内容 ---
        rightPane = new VisualElement();
        rightPane.style.paddingLeft = 20;
        rightPane.style.paddingRight = 20;
        rightPane.style.paddingTop = 20;

        splitView.Add(rightPane);

        leftMenu.SetSelection(0);
    }

    private void RefreshRightPane()
    {
        rightPane.Clear();

        string key = GetPrefabKey(currentType);

        // 1. 标题
        var nameLabel = new Label(GetTypeName(currentType)) { style = { fontSize = 24, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 20 } };
        rightPane.Add(nameLabel);

        // 2. 解析当前生效模板（覆写 → 旧副本 → 包内默认，与运行时同语义）
        GameObject prefab = null;
        string assetPath = null;
        string sourceLabel = null;

        var config = VNProjectConfig.Instance;
        GameObject overridden = config != null ? config.GetUIPrefabOverride(key) as GameObject : null;
        if (overridden != null)
        {
            prefab = overridden;
            assetPath = AssetDatabase.GetAssetPath(overridden);
            sourceLabel = "自定义模板（覆写生效中）";
        }
        else
        {
            assetPath = VNEditorResourceResolver.KeyToAssetPath(key);
            if (!string.IsNullOrEmpty(assetPath))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                sourceLabel = assetPath.StartsWith("Packages/") ? "包内默认模板（未覆写）" : "用户副本（未覆写）";
            }
        }

        if (prefab == null)
        {
            var errorBox = new VisualElement();
            errorBox.style.alignItems = Align.Center;

            var icon = new Image() { image = EditorGUIUtility.IconContent("console.erroricon").image, style = { width = 32, height = 32, marginBottom = 10 } };
            var msg = new Label($"找不到模板！\n键: {key}") { style = { color = new Color(1f, 0.4f, 0.4f), fontSize = 14, marginBottom = 15 } };

            errorBox.Add(icon);
            errorBox.Add(msg);
            rightPane.Add(errorBox);
        }
        else
        {
            // 信息卡片
            var infoBox = new Box();
            infoBox.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            infoBox.style.paddingTop = 10; infoBox.style.paddingBottom = 10;
            infoBox.style.paddingLeft = 15; infoBox.style.paddingRight = 15;
            infoBox.style.marginBottom = 10;
            infoBox.style.width = 420;

            infoBox.Add(new Label($"当前生效: {sourceLabel}") { style = { fontSize = 13, marginBottom = 5, color = new Color(0.55f, 0.8f, 0.55f) } });
            infoBox.Add(new Label($"路径: {assetPath}") { style = { fontSize = 11, color = Color.gray, whiteSpace = WhiteSpace.Normal } });
            rightPane.Add(infoBox);

            bool isPackageDefault = assetPath != null && assetPath.StartsWith("Packages/");

            var editBtn = new Button(() =>
            {
                if (isPackageDefault)
                {
                    if (!EditorUtility.DisplayDialog("正在编辑引擎默认模板",
                        "当前打开的是包内默认模板（Packages 内）。\n\n" +
                        "直接编辑会：\n• 影响使用此插件的所有项目\n• 包升级时被覆盖丢失\n\n" +
                        "建议改用「从模板创建自定义…」复制副本后编辑。仍要继续？",
                        "仍要编辑默认模板", "取消"))
                        return;
                }
                AssetDatabase.OpenAsset(prefab);
            })
            {
                text = "进入编辑模式",
                style = {
                    width = 220, height = 50, fontSize = 16,
                    backgroundColor = new Color(0.2f, 0.5f, 0.8f),
                    color = Color.white,
                    borderTopLeftRadius = 5, borderTopRightRadius = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5
                }
            };
            rightPane.Add(editBtn);

            var pingBtn = new Button(() => { Selection.activeObject = prefab; EditorGUIUtility.PingObject(prefab); })
            {
                text = "在 Project 中定位",
                style = { marginTop = 10, width = 150, height = 25 }
            };
            rightPane.Add(pingBtn);
        }

        // 3. 从模板创建自定义（复制包内模板 + 自动指派覆写）
        var createBtn = new Button(() =>
        {
            if (config != null) VNUIPrefabTemplateCreator.CreateFromKey(config, key);
        })
        {
            text = "从模板创建自定义…",
            style = { marginTop = 15, width = 220, height = 32 }
        };
        rightPane.Add(createBtn);
    }

    private string GetTypeName(UIType type)
    {
        switch (type)
        {
            case UIType.Gameplay: return "游戏主界面 (Gameplay)";
            case UIType.Pause: return "暂停界面 (Pause)";
            case UIType.History: return "历史记录 (History)";
            case UIType.SaveLoad: return "存读档 (Save/Load)";
            case UIType.Settings: return "设置 (Settings)";
            case UIType.Choice: return "选项 (Choice)";
            case UIType.Confirm: return "确认弹窗 (Confirm)";
            case UIType.MainMenu: return "主界面 (MainMenu)";
            case UIType.Loading: return "加载进度 (Loading)";
            default: return type.ToString();
        }
    }

    /// <summary>面板 → 模板键（VNUIPrefabKeys 常量，与运行时 VNUIPrefabs 同一键空间）</summary>
    private string GetPrefabKey(UIType type)
    {
        switch (type)
        {
            case UIType.Gameplay: return VNUIPrefabKeys.VNGameplayPanel;
            case UIType.Pause: return VNUIPrefabKeys.PausePanel;
            case UIType.History: return VNUIPrefabKeys.HistoryPanel;
            case UIType.SaveLoad: return VNUIPrefabKeys.SaveLoadPanel;
            case UIType.Settings: return VNUIPrefabKeys.SettingsPanel;
            case UIType.Choice: return VNUIPrefabKeys.ChoicePanel;
            case UIType.Confirm: return VNUIPrefabKeys.ConfirmPanel;
            case UIType.MainMenu: return VNUIPrefabKeys.MainMenuPanel;
            case UIType.Loading: return VNUIPrefabKeys.LoadingProgressPanel;
            default: return "";
        }
    }
}
