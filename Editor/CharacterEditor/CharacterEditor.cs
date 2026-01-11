using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class CharacterEditorWindow : EditorWindow
{
    private const string CHARACTER_PATH = "Assets/Resources/VNovelizerRes/Characters";

    private ListView leftListView;
    private VisualElement rightPane;
    private Image previewImage;

    // 列表相关
    private ListView elementListView; // 立绘列表
    private ListView headSpriteListView; // 头像列表
    private VisualElement expressionContainer;
    private VisualElement headContainer;

    private List<CharacterProfile> allProfiles = new List<CharacterProfile>();
    private CharacterProfile selectedProfile;

    // 当前选中的 Tab (0=Expression, 1=Head)
    private int currentTab = 0;

    [MenuItem("VNovelizer/🙋 角色编辑器 (Character Editor)",false,21)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<CharacterEditorWindow>();
        wnd.titleContent = new GUIContent("角色编辑器");
        wnd.minSize = new Vector2(900, 600);
    }

    public void CreateGUI()
    {
        if (!Directory.Exists(CHARACTER_PATH))
        {
            Directory.CreateDirectory(CHARACTER_PATH);
            AssetDatabase.Refresh();
        }

        var root = rootVisualElement;
        root.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);

        var splitView = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
        root.Add(splitView);

        // ==========================
        //        左侧：列表栏
        // ==========================
        var leftPane = new VisualElement();
        leftPane.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.paddingTop = 5; toolbar.style.paddingBottom = 5;
        toolbar.style.paddingLeft = 5; toolbar.style.paddingRight = 5;

        var createBtn = new Button(CreateNewCharacter) { text = "➕ 新建角色", style = { flexGrow = 1, height = 24 } };
        var refreshBtn = new Button(LoadAllProfiles) { text = "↻", style = { width = 30, height = 24 } };
        toolbar.Add(createBtn);
        toolbar.Add(refreshBtn);
        leftPane.Add(toolbar);

        leftListView = new ListView();
        leftListView.makeItem = () => new Label() { style = { paddingLeft = 10, paddingTop = 5, paddingBottom = 5 } };
        leftListView.bindItem = (element, index) => { (element as Label).text = allProfiles[index].CharacterID; };
        leftListView.itemsSource = allProfiles;
        leftListView.selectionType = SelectionType.Single;
        leftListView.style.flexGrow = 1;
        leftListView.selectionChanged += OnCharacterSelected;

        leftPane.Add(leftListView);
        splitView.Add(leftPane);

        // ==========================
        //        右侧：详情栏
        // ==========================
        rightPane = new VisualElement();
        rightPane.style.paddingTop = 10;
        rightPane.style.paddingLeft = 20;
        rightPane.style.paddingRight = 20;
        rightPane.Add(new Label("请在左侧选择一个角色") { style = { color = Color.gray, paddingTop = 50, unityTextAlign = TextAnchor.MiddleCenter } });

        splitView.Add(rightPane);

        LoadAllProfiles();
    }

    private void LoadAllProfiles()
    {
        allProfiles.Clear();
        string[] guids = AssetDatabase.FindAssets("t:CharacterProfile", new[] { CHARACTER_PATH });
        foreach (string guid in guids)
        {
            var p = AssetDatabase.LoadAssetAtPath<CharacterProfile>(AssetDatabase.GUIDToAssetPath(guid));
            if (p != null) allProfiles.Add(p);
        }
        leftListView.Rebuild();
    }

    private void OnCharacterSelected(IEnumerable<object> selectedItems)
    {
        rightPane.Clear();
        foreach (var item in selectedItems)
        {
            selectedProfile = item as CharacterProfile;
            if (selectedProfile != null) DrawDetailView(selectedProfile);
            break;
        }
    }

    private void DrawDetailView(CharacterProfile profile)
    {
        // 1. 顶部栏 (ID + 删除)
        var headerContainer = new VisualElement();
        headerContainer.style.flexDirection = FlexDirection.Row;
        headerContainer.style.marginBottom = 10;
        headerContainer.style.paddingBottom = 10;
        headerContainer.style.borderBottomWidth = 1;
        headerContainer.style.borderBottomColor = Color.gray;

        var idField = new TextField("角色 ID") { value = profile.CharacterID, style = { flexGrow = 1, fontSize = 14 } };
        idField.RegisterValueChangedCallback(evt => {
            profile.CharacterID = evt.newValue;
            EditorUtility.SetDirty(profile);
            leftListView.RefreshItem(allProfiles.IndexOf(profile));
        });

        var deleteBtn = new Button(() => DeleteCharacter(profile)) { text = "🗑 删除" };
        deleteBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
        deleteBtn.style.color = Color.white;

        headerContainer.Add(idField);
        headerContainer.Add(deleteBtn);
        rightPane.Add(headerContainer);

        // 1.5. SpeakerBox 字段
        var speakerBoxContainer = new VisualElement();
        speakerBoxContainer.style.marginBottom = 15;
        speakerBoxContainer.style.paddingBottom = 10;
        speakerBoxContainer.style.borderBottomWidth = 1;
        speakerBoxContainer.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);

        var speakerBoxLabel = new Label("姓名框 (SpeakerBox)");
        speakerBoxLabel.style.fontSize = 12;
        speakerBoxLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
        speakerBoxLabel.style.marginBottom = 5;
        speakerBoxContainer.Add(speakerBoxLabel);

        var speakerBoxField = new ObjectField() { objectType = typeof(Sprite) };
        speakerBoxField.value = profile.SpeakerBox;
        speakerBoxField.style.flexGrow = 1;
        speakerBoxField.RegisterValueChangedCallback(evt => {
            profile.SpeakerBox = evt.newValue as Sprite;
            EditorUtility.SetDirty(profile);
        });
        speakerBoxContainer.Add(speakerBoxField);

        rightPane.Add(speakerBoxContainer);

        // 2. 预览区域 (固定高度)
        var previewBox = new Box();
        previewBox.style.height = 250;
        previewBox.style.marginBottom = 15;
        previewBox.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        previewBox.style.borderTopWidth = 1;
        previewBox.style.borderBottomWidth = 1;
        previewBox.style.borderLeftWidth = 1;
        previewBox.style.borderRightWidth = 1;

        previewBox.style.borderTopColor = Color.black;
        previewBox.style.borderBottomColor = Color.black;
        previewBox.style.borderLeftColor = Color.black;
        previewBox.style.borderRightColor = Color.black;
        previewBox.style.alignItems = Align.Center;
        previewBox.style.justifyContent = Justify.Center;

        previewImage = new Image();
        previewImage.scaleMode = ScaleMode.ScaleToFit;
        previewImage.style.width = Length.Percent(90);
        previewImage.style.height = Length.Percent(90);
        previewBox.Add(previewImage);
        rightPane.Add(previewBox);

        // 3. 选项卡栏 (Tabs)
        var tabContainer = new VisualElement();
        tabContainer.style.flexDirection = FlexDirection.Row;
        tabContainer.style.marginBottom = 5;

        // 创建两个按钮模拟 Tab
        var expTab = CreateTabButton("立绘 (Expressions)", 0);
        var headTab = CreateTabButton("头像 (Heads)", 1);

        tabContainer.Add(expTab);
        tabContainer.Add(headTab);
        rightPane.Add(tabContainer);

        // 4. 内容容器
        var contentContainer = new VisualElement();
        contentContainer.style.flexGrow = 1;
        rightPane.Add(contentContainer);

        // 初始化容器
        expressionContainer = new VisualElement() { style = { flexGrow = 1 } };
        headContainer = new VisualElement() { style = { flexGrow = 1, display = DisplayStyle.None } }; // 默认隐藏

        contentContainer.Add(expressionContainer);
        contentContainer.Add(headContainer);

        // 绘制两个列表的内容
        DrawExpressionList(profile);
        DrawHeadList(profile);

        // 恢复上次选中的 Tab
        SwitchTab(currentTab);

        // --- 辅助函数：创建 Tab 按钮 ---
        Button CreateTabButton(string text, int tabIndex)
        {
            var btn = new Button(() => SwitchTab(tabIndex)) { text = text };
            btn.style.flexGrow = 1;
            btn.style.height = 24;
            btn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            // 标记，方便后续改样式
            btn.name = "Tab" + tabIndex;
            return btn;
        }

        // --- 辅助函数：切换 Tab ---
        void SwitchTab(int index)
        {
            currentTab = index;

            // 显示/隐藏容器
            expressionContainer.style.display = (index == 0) ? DisplayStyle.Flex : DisplayStyle.None;
            headContainer.style.display = (index == 1) ? DisplayStyle.Flex : DisplayStyle.None;

            // 更新按钮样式 (高亮选中)
            var tab0 = tabContainer.Q<Button>("Tab0");
            var tab1 = tabContainer.Q<Button>("Tab1");

            if (tab0 != null) tab0.style.backgroundColor = (index == 0) ? new Color(0.35f, 0.35f, 0.35f) : new Color(0.25f, 0.25f, 0.25f);
            if (tab1 != null) tab1.style.backgroundColor = (index == 1) ? new Color(0.35f, 0.35f, 0.35f) : new Color(0.25f, 0.25f, 0.25f);

            // 清空预览（切换列表时清空，以免混淆）
            UpdatePreview(null);
        }
    }

    private void DrawExpressionList(CharacterProfile profile)
    {
        // 头部
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        header.style.height = 24;
        header.style.alignItems = Align.Center;
        header.style.paddingLeft = 5; header.style.paddingRight = 5;

        header.Add(new Label("立绘配置") { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
        header.Add(new Button(() => {
            profile.ElementSprites.Add(new ElementSprite());
            EditorUtility.SetDirty(profile);
            elementListView.Rebuild();
        })
        { text = "+" });
        expressionContainer.Add(header);

        // 列表
        elementListView = new ListView();
        elementListView.style.flexGrow = 1;
        elementListView.itemsSource = profile.ElementSprites;
        elementListView.makeItem = () => CreateListItem("Name", "Sprite", "Delete");
        elementListView.bindItem = (e, i) => BindListItem(e, i, profile.ElementSprites, profile, elementListView);

        elementListView.selectionChanged += (items) => {
            foreach (var item in items) { if (item is ElementSprite data) UpdatePreview(data.Sprite); break; }
        };

        expressionContainer.Add(elementListView);
    }

    private void DrawHeadList(CharacterProfile profile)
    {
        // 头部
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        header.style.height = 24;
        header.style.alignItems = Align.Center;
        header.style.paddingLeft = 5; header.style.paddingRight = 5;

        header.Add(new Label("头像配置") { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
        header.Add(new Button(() => {
            profile.HeadSprites.Add(new ElementSprite());
            EditorUtility.SetDirty(profile);
            headSpriteListView.Rebuild();
        })
        { text = "+" });
        headContainer.Add(header);

        // 列表
        headSpriteListView = new ListView();
        headSpriteListView.style.flexGrow = 1;
        headSpriteListView.itemsSource = profile.HeadSprites;
        headSpriteListView.makeItem = () => CreateListItem("Name", "Sprite", "Delete");
        headSpriteListView.bindItem = (e, i) => BindListItem(e, i, profile.HeadSprites, profile, headSpriteListView);

        headSpriteListView.selectionChanged += (items) => {
            foreach (var item in items) { if (item is ElementSprite data) UpdatePreview(data.Sprite); break; }
        };

        headContainer.Add(headSpriteListView);
    }

    // --- 统一的列表项创建逻辑 ---
    private VisualElement CreateListItem(string nameId, string spriteId, string delId)
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.alignItems = Align.Center;
        container.style.paddingTop = 2;
        container.style.paddingBottom = 2;

        var nameField = new TextField() { name = nameId, style = { width = 120 } };
        var spriteField = new ObjectField() { name = spriteId, objectType = typeof(Sprite), style = { flexGrow = 1 } };
        var delBtn = new Button() { text = "×", name = delId };

        // 样式优化
        delBtn.style.width = 20;
        delBtn.style.backgroundColor = Color.clear;
        delBtn.style.color = new Color(0.8f, 0.3f, 0.3f);
        delBtn.style.borderTopWidth = 0;
        delBtn.style.borderBottomWidth = 0;
        delBtn.style.borderLeftWidth = 0;
        delBtn.style.borderRightWidth = 0;


        container.Add(nameField);
        container.Add(spriteField);
        container.Add(delBtn);
        return container;
    }

    private void BindListItem(VisualElement element, int index, List<ElementSprite> list, CharacterProfile profile, ListView listView)
    {
        if (index >= list.Count) return;
        var data = list[index];

        var nameField = element.Q<TextField>("Name");
        var spriteField = element.Q<ObjectField>("Sprite");
        var delBtn = element.Q<Button>("Delete");

        // 解绑旧事件 (简易版略过，生产环境建议用 UserData 存储 callback)
        // 这里直接重新赋值 value 不会触发 ChangeEvent

        nameField.SetValueWithoutNotify(data.Element);
        spriteField.SetValueWithoutNotify(data.Sprite);

        // 点击时选中该行
        EventCallback<FocusEvent> onFocus = (evt) => {
            listView.SetSelection(index);
            UpdatePreview(data.Sprite);
        };
        nameField.RegisterCallback(onFocus);
        spriteField.RegisterCallback(onFocus);

        nameField.RegisterValueChangedCallback(evt => {
            data.Element = evt.newValue;
            EditorUtility.SetDirty(profile);
        });

        spriteField.RegisterValueChangedCallback(evt => {
            data.Sprite = evt.newValue as Sprite;
            EditorUtility.SetDirty(profile);
            if (listView.selectedIndex == index) UpdatePreview(data.Sprite);
        });

        delBtn.clicked += () => {
            if (index < list.Count)
            {
                list.RemoveAt(index);
                EditorUtility.SetDirty(profile);
                listView.Rebuild();
                UpdatePreview(null);
            }
        };
    }

    private void UpdatePreview(Sprite sprite)
    {
        if (sprite == null)
        {
            previewImage.sprite = null;
            previewImage.image = null;
        }
        else
        {
            previewImage.sprite = sprite;
        }
    }

    private void CreateNewCharacter()
    {
        string baseName = "NewCharacter";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{CHARACTER_PATH}/{baseName}.asset");

        CharacterProfile newProfile = ScriptableObject.CreateInstance<CharacterProfile>();
        newProfile.CharacterID = Path.GetFileNameWithoutExtension(path);

        AssetDatabase.CreateAsset(newProfile, path);
        AssetDatabase.SaveAssets();
        LoadAllProfiles();

        int index = allProfiles.IndexOf(newProfile);
        leftListView.SetSelection(index);
    }

    private void DeleteCharacter(CharacterProfile profile)
    {
        if (EditorUtility.DisplayDialog("删除确认", $"确定要删除角色 {profile.CharacterID} 吗？", "删除", "取消"))
        {
            string path = AssetDatabase.GetAssetPath(profile);
            AssetDatabase.DeleteAsset(path);
            rightPane.Clear();
            LoadAllProfiles();
        }
    }
}