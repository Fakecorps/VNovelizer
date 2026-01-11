using UnityEditor;
using UnityEngine;
using System.Linq;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.SceneManagement;

public class UIEditorWindow : EditorWindow
{
    private ListView leftMenu;
    private VisualElement rightPane;

    public enum UIType
    {
        Gameplay, Pause, History, SaveLoad, Settings, Choice, Confirm, MainMenu, Loading
    }

    private UIType currentType = UIType.Gameplay;

    [MenuItem("VNovelizer/🎨 UI预制体管理器 (UIPrefabs Manager)",false,24)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<UIEditorWindow>();
        wnd.titleContent = new GUIContent("UI预制体管理器");
        wnd.minSize = new Vector2(600, 300); // 尺寸可以小一点了
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
        rightPane.style.justifyContent = Justify.Center; // 垂直居中
        rightPane.style.alignItems = Align.Center; // 水平居中

        splitView.Add(rightPane);

        leftMenu.SetSelection(0);
    }

    private void RefreshRightPane()
    {
        rightPane.Clear();

        string prefabPath = GetPrefabPath(currentType);
        string fullPath = "Assets/Resources/" + prefabPath + "/" + GetDefaultFileName(currentType) + ".prefab";

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);

        // 1. 标题
        var nameLabel = new Label(GetTypeName(currentType)) { style = { fontSize = 24, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 30 } };
        rightPane.Add(nameLabel);

        if (prefab == null)
        {
            var errorBox = new VisualElement();
            errorBox.style.flexDirection = FlexDirection.Row;
            errorBox.style.alignItems = Align.Center;

            var icon = new Image() { image = EditorGUIUtility.IconContent("console.erroricon").image, style = { width = 32, height = 32, marginRight = 10 } };
            var msg = new Label($"找不到预制体！\n路径: {fullPath}") { style = { color = new Color(1f, 0.4f, 0.4f), fontSize = 14 } };

            errorBox.Add(icon);
            errorBox.Add(msg);
            rightPane.Add(errorBox);
            return;
        }

        // 2. 信息卡片
        var infoBox = new Box();
        infoBox.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        infoBox.style.paddingTop = 10; infoBox.style.paddingBottom = 10;
        infoBox.style.paddingLeft = 15; infoBox.style.paddingRight = 15;
        infoBox.style.marginBottom = 30;
        infoBox.style.borderTopWidth = 1; infoBox.style.borderBottomWidth = 1;
        infoBox.style.borderLeftWidth = 1; infoBox.style.borderRightWidth = 1;
        infoBox.style.borderTopColor = Color.black; infoBox.style.borderBottomColor = Color.black;
        infoBox.style.borderLeftColor = Color.black; infoBox.style.borderRightColor = Color.black;
        infoBox.style.width = 350;

        infoBox.Add(new Label($"文件: {GetDefaultFileName(currentType)}.prefab") { style = { fontSize = 12, marginBottom = 5 } });
        infoBox.Add(new Label($"路径: {prefabPath}") { style = { fontSize = 11, color = Color.gray, whiteSpace = WhiteSpace.Normal } });

        rightPane.Add(infoBox);

        var editBtn = new Button(() => OpenPrefab(fullPath))
        {
            text = "✏️ 进入编辑模式",
            style = {
                width = 200, height = 50, fontSize = 16,
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

    private void OpenPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
        {
            AssetDatabase.OpenAsset(prefab);
        }
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

    private string GetPrefabPath(UIType type)
    {
        var config = VNProjectConfig.Instance;
        if (config == null) return "";

        switch (type)
        {
            case UIType.Gameplay: return config.UI_VNGamePlayPath;
            case UIType.Pause: return config.UI_PausePath;
            case UIType.History: return config.UI_HistoryPath;
            case UIType.SaveLoad: return config.UI_SaveLoadPath;
            case UIType.Settings: return config.UI_SettingsPath;
            case UIType.Choice: return config.UI_ChoicePath;
            case UIType.Confirm: return config.UI_ConfirmPath;
            case UIType.MainMenu: return config.UI_MainMenuPath;
            case UIType.Loading: return config.UI_LoadingPath;
            default: return "";
        }
    }

    private string GetDefaultFileName(UIType type)
    {
        switch (type)
        {
            case UIType.Gameplay: return "VNGameplayPanel";
            case UIType.Pause: return "PausePanel";
            case UIType.History: return "HistoryPanel";
            case UIType.SaveLoad: return "SaveLoadPanel";
            case UIType.Settings: return "SettingsPanel";
            case UIType.Choice: return "ChoicePanel";
            case UIType.Confirm: return "ConfirmPanel";
            case UIType.MainMenu: return "MainMenuPanel";
            case UIType.Loading: return "LoadingProgressPanel";
            default: return "";
        }
    }
}