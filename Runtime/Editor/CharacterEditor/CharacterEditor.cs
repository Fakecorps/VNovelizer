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

    [MenuItem("VNovelizer/🙋 角色编辑器 (Character Editor)", false, 21)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<CharacterEditorWindow>();
        wnd.titleContent = new GUIContent("角色编辑器");
        wnd.minSize = new Vector2(1000, 700); // 稍微加大默认窗口尺寸
    }

    public void CreateGUI()
    {
        if (!Directory.Exists(CHARACTER_PATH))
        {
            Directory.CreateDirectory(CHARACTER_PATH);
            AssetDatabase.Refresh();
        }

        var root = rootVisualElement;
        root.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f); // 全局深色背景

        // 主分栏 (左窄右宽)
        var splitView = new TwoPaneSplitView(0, 280, TwoPaneSplitViewOrientation.Horizontal);
        root.Add(splitView);

        // ==========================
        //        左侧：列表栏
        // ==========================
        var leftPane = new VisualElement();
        leftPane.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f); // 侧边栏稍深
        leftPane.style.borderRightWidth = 1;
        leftPane.style.borderRightColor = new Color(0.1f, 0.1f, 0.1f);

        // 顶部工具栏 (更现代的样式)
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.paddingTop = 8;
        toolbar.style.paddingBottom = 8;
        toolbar.style.paddingLeft = 10;
        toolbar.style.paddingRight = 10;
        toolbar.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        toolbar.style.borderBottomWidth = 1;
        toolbar.style.borderBottomColor = new Color(0.1f, 0.1f, 0.1f);

        var createBtn = new Button(CreateNewCharacter) { text = "➕ 新建角色" };
        createBtn.style.flexGrow = 1;
        createBtn.style.height = 28;
        createBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.2f); // 绿色按钮
        createBtn.style.color = Color.white;
        createBtn.style.unityFontStyleAndWeight = FontStyle.Bold;

        var refreshBtn = new Button(LoadAllProfiles) { text = "↻" };
        refreshBtn.style.width = 32;
        refreshBtn.style.height = 28;

        toolbar.Add(createBtn);
        toolbar.Add(refreshBtn);
        leftPane.Add(toolbar);

        // 角色列表 (增加行间距和字体大小)
        leftListView = new ListView();
        leftListView.fixedItemHeight = 35; // 增加行高
        leftListView.makeItem = () =>
        {
            var label = new Label();
            label.style.paddingLeft = 15;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.fontSize = 13;
            return label;
        };
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
        rightPane.style.paddingTop = 20;
        rightPane.style.paddingLeft = 30;
        rightPane.style.paddingRight = 30;
        rightPane.style.paddingBottom = 20;

        // 默认提示 (居中大字)
        var tipContainer = new VisualElement() { style = { flexGrow = 1, justifyContent = Justify.Center, alignItems = Align.Center } };
        tipContainer.Add(new Label("请在左侧选择一个角色") { style = { color = Color.gray, fontSize = 16 } });
        rightPane.Add(tipContainer); // 初始只显示提示，选中后 Clear 掉

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
        // 1. 标题区 (ID + 删除按钮)
        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 15;
        headerRow.style.borderBottomWidth = 1;
        headerRow.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
        headerRow.style.paddingBottom = 15;

        // ID 输入框
        var idField = new TextField("角色 ID") { value = profile.CharacterID };
        idField.style.flexGrow = 1;
        idField.style.fontSize = 14;
        idField.labelElement.style.minWidth = 60; // 对齐标签
        idField.RegisterValueChangedCallback(evt => {
            profile.CharacterID = evt.newValue;
            EditorUtility.SetDirty(profile);
            leftListView.RefreshItem(allProfiles.IndexOf(profile));
        });

        // 删除按钮 (右对齐)
        var deleteBtn = new Button(() => DeleteCharacter(profile)) { text = "🗑 删除角色" };
        deleteBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f); // 红色
        deleteBtn.style.color = Color.white;
        deleteBtn.style.height = 24;
        deleteBtn.style.marginLeft = 20;

        headerRow.Add(idField);
        headerRow.Add(deleteBtn);
        rightPane.Add(headerRow);

        // 2. 基础配置区 (SpeakerBox, HeadFrame)
        var basicConfigBox = CreateSectionBox("基础配置");

        CreateObjectField(basicConfigBox, "姓名框 (SpeakerBox)", profile.SpeakerBox, (val) => {
            profile.SpeakerBox = val;
            EditorUtility.SetDirty(profile);
        });

        CreateObjectField(basicConfigBox, "头像边框 (HeadFrame)", profile.HeadFrame, (val) => {
            profile.HeadFrame = val;
            EditorUtility.SetDirty(profile);
        });

        rightPane.Add(basicConfigBox);

        // 3. 预览区域 (稍微缩小高度，留给列表)
        var previewSection = CreateSectionBox("实时预览");
        previewSection.style.height = 220;
        previewSection.style.alignItems = Align.Center;
        previewSection.style.justifyContent = Justify.Center;
        previewSection.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f); // 深黑底色

        previewImage = new Image();
        previewImage.scaleMode = ScaleMode.ScaleToFit;
        previewImage.style.width = Length.Percent(95);
        previewImage.style.height = Length.Percent(95);
        previewSection.Add(previewImage);
        rightPane.Add(previewSection);

        // 4. 选项卡栏 (Tab Bar)
        var tabContainer = new VisualElement();
        tabContainer.style.flexDirection = FlexDirection.Row;
        tabContainer.style.marginTop = 10;
        tabContainer.style.marginBottom = 0; // 紧贴下方内容

        var expTab = CreateTabButton("🎭 立绘 (Expressions)", 0);
        var headTab = CreateTabButton("🖼️ 头像 (Heads)", 1);

        tabContainer.Add(expTab);
        tabContainer.Add(headTab);
        rightPane.Add(tabContainer);

        // 5. 列表内容容器 (带背景色)
        var contentBox = new VisualElement();
        contentBox.style.flexGrow = 1;
        contentBox.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        contentBox.style.borderBottomLeftRadius = 5;
        contentBox.style.borderBottomRightRadius = 5;
        contentBox.style.paddingTop = 10;
        contentBox.style.paddingBottom = 10;
        contentBox.style.paddingLeft = 10;
        contentBox.style.paddingRight = 10;
        rightPane.Add(contentBox);

        expressionContainer = new VisualElement() { style = { flexGrow = 1 } };
        headContainer = new VisualElement() { style = { flexGrow = 1, display = DisplayStyle.None } };

        contentBox.Add(expressionContainer);
        contentBox.Add(headContainer);

        DrawExpressionList(profile);
        DrawHeadList(profile);

        SwitchTab(currentTab);

        // --- Helper: Tab Button ---
        Button CreateTabButton(string text, int tabIndex)
        {
            var btn = new Button(() => SwitchTab(tabIndex)) { text = text };
            btn.style.flexGrow = 1;
            btn.style.height = 30;
            btn.style.fontSize = 13;
            btn.style.borderTopLeftRadius = 5;
            btn.style.borderTopRightRadius = 5;
            btn.style.borderBottomLeftRadius = 0;
            btn.style.borderBottomRightRadius = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            btn.name = "Tab" + tabIndex;
            return btn;
        }

        // --- Helper: Switch Tab ---
        void SwitchTab(int index)
        {
            currentTab = index;
            expressionContainer.style.display = (index == 0) ? DisplayStyle.Flex : DisplayStyle.None;
            headContainer.style.display = (index == 1) ? DisplayStyle.Flex : DisplayStyle.None;

            var tab0 = tabContainer.Q<Button>("Tab0");
            var tab1 = tabContainer.Q<Button>("Tab1");

            // 选中态颜色 vs 未选中态颜色
            Color selectedColor = new Color(0.25f, 0.25f, 0.25f); // 与 ContentBox 同色
            Color normalColor = new Color(0.2f, 0.2f, 0.2f); // 更深一点

            if (tab0 != null)
            {
                tab0.style.backgroundColor = (index == 0) ? selectedColor : normalColor;
                tab0.style.unityFontStyleAndWeight = (index == 0) ? FontStyle.Bold : FontStyle.Normal;
            }
            if (tab1 != null)
            {
                tab1.style.backgroundColor = (index == 1) ? selectedColor : normalColor;
                tab1.style.unityFontStyleAndWeight = (index == 1) ? FontStyle.Bold : FontStyle.Normal;
            }

            UpdatePreview(null);
        }
    }

    // --- 绘制列表逻辑 ---
    private void DrawExpressionList(CharacterProfile profile)
    {
        // 列表头
        var header = CreateListHeader("立绘配置", () => {
            profile.ElementSprites.Add(new ElementSprite());
            EditorUtility.SetDirty(profile);
            elementListView.Rebuild();
        });
        expressionContainer.Add(header);

        // 列表
        elementListView = new ListView();
        elementListView.style.flexGrow = 1;
        elementListView.fixedItemHeight = 28; // 行高适中
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
        // 列表头
        var header = CreateListHeader("头像配置", () => {
            profile.HeadSprites.Add(new ElementSprite());
            EditorUtility.SetDirty(profile);
            headSpriteListView.Rebuild();
        });
        headContainer.Add(header);

        // 列表
        headSpriteListView = new ListView();
        headSpriteListView.style.flexGrow = 1;
        headSpriteListView.fixedItemHeight = 28;
        headSpriteListView.itemsSource = profile.HeadSprites;
        headSpriteListView.makeItem = () => CreateListItem("Name", "Sprite", "Delete");
        headSpriteListView.bindItem = (e, i) => BindListItem(e, i, profile.HeadSprites, profile, headSpriteListView);

        headSpriteListView.selectionChanged += (items) => {
            foreach (var item in items) { if (item is ElementSprite data) UpdatePreview(data.Sprite); break; }
        };
        headContainer.Add(headSpriteListView);
    }

    // --- 辅助 UI 创建方法 ---

    private VisualElement CreateSectionBox(string title)
    {
        var box = new Box();
        box.style.marginBottom = 15;
        box.style.paddingTop = 10; box.style.paddingBottom = 10;
        box.style.paddingLeft = 10; box.style.paddingRight = 10;
        box.style.backgroundColor = new Color(0.23f, 0.23f, 0.23f);
        box.style.borderTopWidth = 1; box.style.borderBottomWidth = 1;
        box.style.borderLeftWidth = 1; box.style.borderRightWidth = 1;
        box.style.borderTopColor = new Color(0.15f, 0.15f, 0.15f);
        box.style.borderBottomColor = new Color(0.15f, 0.15f, 0.15f);
        box.style.borderLeftColor = new Color(0.15f, 0.15f, 0.15f);
        box.style.borderRightColor = new Color(0.15f, 0.15f, 0.15f);
        box.style.borderTopLeftRadius = 5; box.style.borderTopRightRadius = 5;
        box.style.borderBottomLeftRadius = 5; box.style.borderBottomRightRadius = 5;

        var label = new Label(title);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginBottom = 8;
        label.style.color = new Color(0.7f, 0.7f, 0.7f);
        box.Add(label);

        return box;
    }

    private void CreateObjectField(VisualElement parent, string label, Object value, System.Action<Sprite> onChange)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 5;

        var field = new ObjectField(label) { objectType = typeof(Sprite), value = value };
        field.style.flexGrow = 1;
        field.RegisterValueChangedCallback(evt => onChange(evt.newValue as Sprite));

        row.Add(field);
        parent.Add(row);
    }

    private VisualElement CreateListHeader(string title, System.Action onAdd)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 5;

        // 搜索框或标题
        header.Add(new Label(title) { style = { alignSelf = Align.Center, color = Color.gray } });

        var addBtn = new Button(onAdd) { text = "＋ 添加" };
        addBtn.style.height = 20;
        addBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        header.Add(addBtn);

        return header;
    }

    private VisualElement CreateListItem(string nameId, string spriteId, string delId)
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.alignItems = Align.Center;

        var nameField = new TextField() { name = nameId, style = { width = 120, marginRight = 10 } };
        var spriteField = new ObjectField() { name = spriteId, objectType = typeof(Sprite), style = { flexGrow = 1 } };

        var delBtn = new Button() { text = "×", name = delId };
        delBtn.style.width = 24;
        delBtn.style.height = 20;
        delBtn.style.backgroundColor = Color.clear;
        delBtn.style.color = new Color(0.8f, 0.3f, 0.3f);
        delBtn.style.borderTopWidth = 0; delBtn.style.borderBottomWidth = 0;
        delBtn.style.borderLeftWidth = 0; delBtn.style.borderRightWidth = 0;

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

        // 赋值
        nameField.SetValueWithoutNotify(data.Element);
        spriteField.SetValueWithoutNotify(data.Sprite);

        // 交互：点输入框选中行
        EventCallback<FocusEvent> onFocus = (evt) => {
            listView.SetSelection(index);
            UpdatePreview(data.Sprite);
        };
        nameField.RegisterCallback(onFocus);
        spriteField.RegisterCallback(onFocus);

        // 修改回调
        nameField.RegisterValueChangedCallback(evt => {
            data.Element = evt.newValue;
            EditorUtility.SetDirty(profile);
        });

        spriteField.RegisterValueChangedCallback(evt => {
            data.Sprite = evt.newValue as Sprite;
            EditorUtility.SetDirty(profile);
            if (listView.selectedIndex == index) UpdatePreview(data.Sprite);
        });

        // 删除回调
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