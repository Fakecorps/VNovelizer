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

    // UI 元素
    private ListView leftListView;
    private VisualElement rightPane;
    private Image previewImage;
    private TextField searchField;

    // 选项卡按钮 (成员变量)
    private Button expTab;
    private Button headTab;

    // 列表相关
    private ListView elementListView; // 立绘列表
    private ListView headSpriteListView; // 头像列表
    private VisualElement expressionContainer;
    private VisualElement headContainer;

    // 数据
    private List<CharacterProfile> allProfiles = new List<CharacterProfile>();
    private List<CharacterProfile> filteredProfiles = new List<CharacterProfile>(); // 用于搜索过滤
    private CharacterProfile selectedProfile;

    // 当前选中的 Tab (0=Expression, 1=Head)
    private int currentTab = 0;

    [MenuItem("VNovelizer/角色编辑器 (Character Editor)", false, 21)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<CharacterEditorWindow>();
        wnd.titleContent = new GUIContent("角色编辑器");
        wnd.minSize = new Vector2(900, 600);
    }

    public void CreateGUI()
    {
        EnsureDirectory();

        var root = rootVisualElement;

        // 1. 主分栏 (左侧列表，右侧详情)
        var splitView = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
        root.Add(splitView);

        // ==========================
        //        左侧：列表栏
        // ==========================
        var leftPane = new VisualElement();
        leftPane.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f); // 侧边栏深色背景
        splitView.Add(leftPane);

        // 1.1 工具栏 (搜索 + 新建 + 刷新)
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.paddingTop = 5;
        toolbar.style.paddingBottom = 5;
        toolbar.style.paddingLeft = 5;
        toolbar.style.paddingRight = 5;
        toolbar.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        toolbar.style.borderBottomWidth = 1;
        toolbar.style.borderBottomColor = new Color(0.1f, 0.1f, 0.1f);

        // 搜索框
        searchField = new TextField();
        searchField.style.flexGrow = 1;
        searchField.RegisterValueChangedCallback(evt => FilterList(evt.newValue));
        // searchField.placeholderText = "搜索角色..."; // 移除旧API
        toolbar.Add(searchField);

        var refreshBtn = new Button(LoadAllProfiles) { text = "刷新", style = { width = 48 } };
        toolbar.Add(refreshBtn);

        var createBtn = new Button(CreateNewCharacter) { text = "+" };
        createBtn.style.width = 25;
        createBtn.style.backgroundColor = new Color(0.25f, 0.5f, 0.25f);
        toolbar.Add(createBtn);

        leftPane.Add(toolbar);

        // 1.2 角色列表
        leftListView = new ListView();
        leftListView.fixedItemHeight = 30;
        leftListView.makeItem = () =>
        {
            var label = new Label();
            label.style.paddingLeft = 10;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.fontSize = 12;
            return label;
        };
        leftListView.bindItem = (element, index) =>
        {
            (element as Label).text = filteredProfiles[index].CharacterID;
        };
        leftListView.itemsSource = filteredProfiles;
        leftListView.selectionType = SelectionType.Single;
        leftListView.style.flexGrow = 1;
        leftListView.selectionChanged += OnCharacterSelected;

        leftPane.Add(leftListView);

        // ==========================
        //        右侧：详情栏
        // ==========================
        rightPane = new VisualElement();
        rightPane.style.paddingTop = 10;
        rightPane.style.paddingLeft = 15;
        rightPane.style.paddingRight = 15;
        rightPane.style.paddingBottom = 10;
        rightPane.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f); // 主视图背景

        splitView.Add(rightPane);

        // 初始加载
        LoadAllProfiles();

        // 初始显示提示
        ShowPlaceholder();
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(CHARACTER_PATH))
        {
            Directory.CreateDirectory(CHARACTER_PATH);
            AssetDatabase.Refresh();
        }
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
        FilterList(searchField?.value ?? "");
    }

    private void FilterList(string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            filteredProfiles = new List<CharacterProfile>(allProfiles);
        }
        else
        {
            filteredProfiles = allProfiles
                .Where(p => p.CharacterID.ToLower().Contains(searchText.ToLower()))
                .ToList();
        }
        leftListView.itemsSource = filteredProfiles;
        leftListView.Rebuild();
    }

    private void OnCharacterSelected(IEnumerable<object> selectedItems)
    {
        rightPane.Clear();
        var item = selectedItems.FirstOrDefault();
        if (item is CharacterProfile profile)
        {
            selectedProfile = profile;
            DrawDetailView(profile);
        }
        else
        {
            ShowPlaceholder();
        }
    }

    private void ShowPlaceholder()
    {
        rightPane.Clear();
        var label = new Label("请在左侧选择一个角色或新建角色")
        {
            style = {
                color = Color.gray,
                fontSize = 14,
                unityTextAlign = TextAnchor.MiddleCenter,
                flexGrow = 1
            }
        };
        rightPane.Add(label);
    }

    private void DrawDetailView(CharacterProfile profile)
    {
        // 1. 顶部：ID 编辑与 文件操作
        var headerBox = new VisualElement();
        headerBox.style.flexDirection = FlexDirection.Row;
        headerBox.style.marginBottom = 10;
        headerBox.style.alignItems = Align.Center;

        var idField = new TextField("Character ID") { value = profile.CharacterID };
        idField.style.flexGrow = 1;
        idField.style.fontSize = 14;
        idField.style.unityFontStyleAndWeight = FontStyle.Bold;
        idField.RegisterCallback<FocusOutEvent>(evt => {
            if (profile.CharacterID != idField.value)
            {
                string oldName = profile.CharacterID;
                profile.CharacterID = idField.value;
                EditorUtility.SetDirty(profile);
                RenameAsset(profile, idField.value); // 尝试重命名文件
                leftListView.RefreshItem(filteredProfiles.IndexOf(profile));
            }
        });

        var deleteBtn = new Button(() => DeleteCharacter(profile)) { text = "删除" };
        deleteBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
        deleteBtn.style.width = 60;

        headerBox.Add(idField);
        headerBox.Add(deleteBtn);
        rightPane.Add(headerBox);

        // 2. 中部：左右分栏 (左侧基础配置，右侧预览图)
        var middleContainer = new VisualElement();
        middleContainer.style.flexDirection = FlexDirection.Row;
        middleContainer.style.height = 200; // 固定高度区
        middleContainer.style.marginBottom = 10;

        // 2.1 左侧：基础配置
        var configPane = new VisualElement();
        configPane.style.flexGrow = 1;
        configPane.style.marginRight = 10;
        configPane.Add(CreateSectionLabel("基础配置"));

        // SpeakerBox
        CreateObjectField(configPane, "姓名框 (SpeakerBox)", profile.SpeakerBox, (val) => {
            profile.SpeakerBox = val;
            EditorUtility.SetDirty(profile);
        });

        // HeadFrame
        CreateObjectField(configPane, "头像框 (HeadFrame)", profile.HeadFrame, (val) => {
            profile.HeadFrame = val;
            EditorUtility.SetDirty(profile);
        });

        // --- 新增：Scale 和 Offset 设置 ---
        // Scale (Float)
        var scaleField = new FloatField("缩放 (Scale)") { value = profile.scale };
        scaleField.style.marginBottom = 5;
        scaleField.RegisterValueChangedCallback(evt => {
            profile.scale = evt.newValue;
            EditorUtility.SetDirty(profile);
        });
        configPane.Add(scaleField);

        // Offset (Vector2)
        var offsetField = new Vector2Field("偏移 (Offset)") { value = profile.offset };
        offsetField.style.marginBottom = 5;
        offsetField.RegisterValueChangedCallback(evt => {
            profile.offset = evt.newValue;
            EditorUtility.SetDirty(profile);
        });
        configPane.Add(offsetField);
        // ------------------------------------

        middleContainer.Add(configPane);

        // 2.2 右侧：预览图
        var previewPane = new VisualElement();
        previewPane.style.width = 200; // 固定宽度预览区

        // 边框样式
        Color borderColor = new Color(0.1f, 0.1f, 0.1f);
        previewPane.style.borderTopWidth = 1; previewPane.style.borderTopColor = borderColor;
        previewPane.style.borderBottomWidth = 1; previewPane.style.borderBottomColor = borderColor;
        previewPane.style.borderLeftWidth = 1; previewPane.style.borderLeftColor = borderColor;
        previewPane.style.borderRightWidth = 1; previewPane.style.borderRightColor = borderColor;

        previewPane.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f); // 深色底

        previewImage = new Image();
        previewImage.scaleMode = ScaleMode.ScaleToFit;
        previewImage.style.flexGrow = 1;

        previewPane.Add(previewImage);
        middleContainer.Add(previewPane);

        rightPane.Add(middleContainer);

        // 3. 底部：Tab页 (表情列表 / 头像列表)
        var tabContainer = new VisualElement();
        tabContainer.style.flexDirection = FlexDirection.Row;

        // 创建按钮并赋值给成员变量
        expTab = CreateTabButton("立绘 (Expressions)", 0);
        headTab = CreateTabButton("头像 (Heads)", 1);

        tabContainer.Add(expTab);
        tabContainer.Add(headTab);
        rightPane.Add(tabContainer);

        // 列表内容容器
        var listContainer = new VisualElement();
        listContainer.style.flexGrow = 1;
        listContainer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f); // 列表背景

        // 列表边框
        listContainer.style.borderTopWidth = 1; listContainer.style.borderTopColor = borderColor;
        listContainer.style.borderBottomWidth = 1; listContainer.style.borderBottomColor = borderColor;
        listContainer.style.borderLeftWidth = 1; listContainer.style.borderLeftColor = borderColor;
        listContainer.style.borderRightWidth = 1; listContainer.style.borderRightColor = borderColor;

        rightPane.Add(listContainer);

        expressionContainer = new VisualElement() { style = { flexGrow = 1 } };
        headContainer = new VisualElement() { style = { flexGrow = 1, display = DisplayStyle.None } };

        listContainer.Add(expressionContainer);
        listContainer.Add(headContainer);

        DrawExpressionList(profile);
        DrawHeadList(profile);

        // 初始切换到当前 Tab
        SwitchTab(currentTab);
    }

    // --- Tab 切换逻辑 ---
    private void SwitchTab(int index)
    {
        currentTab = index;

        if (expressionContainer != null)
            expressionContainer.style.display = (index == 0) ? DisplayStyle.Flex : DisplayStyle.None;

        if (headContainer != null)
            headContainer.style.display = (index == 1) ? DisplayStyle.Flex : DisplayStyle.None;

        // 简单的 Tab 样式切换 (检查 null 以防调用过早)
        if (expTab != null)
            expTab.style.backgroundColor = (index == 0) ? new Color(0.28f, 0.28f, 0.28f) : new Color(0.2f, 0.2f, 0.2f);

        if (headTab != null)
            headTab.style.backgroundColor = (index == 1) ? new Color(0.28f, 0.28f, 0.28f) : new Color(0.2f, 0.2f, 0.2f);

        // 切换时清空预览或重置
        UpdatePreview(null);
    }

    private Button CreateTabButton(string text, int index)
    {
        var btn = new Button(() => SwitchTab(index)) { text = text };
        btn.style.flexGrow = 1;
        btn.style.height = 25;
        btn.style.marginRight = 0;
        btn.style.marginLeft = 0;
        btn.style.borderBottomWidth = 0;
        return btn;
    }

    // --- 绘制列表逻辑 ---
    private void DrawExpressionList(CharacterProfile profile)
    {
        var header = CreateListHeader("表情立绘列表", () => {
            profile.ElementSprites.Add(new ElementSprite());
            EditorUtility.SetDirty(profile);
            elementListView.Rebuild();
        });
        expressionContainer.Add(header);

        elementListView = CreateStyledListView(profile.ElementSprites, profile, elementListView);
        expressionContainer.Add(elementListView);
    }

    private void DrawHeadList(CharacterProfile profile)
    {
        var header = CreateListHeader("表情头像列表", () => {
            profile.HeadSprites.Add(new ElementSprite());
            EditorUtility.SetDirty(profile);
            headSpriteListView.Rebuild();
        });
        headContainer.Add(header);

        headSpriteListView = CreateStyledListView(profile.HeadSprites, profile, headSpriteListView);
        headContainer.Add(headSpriteListView);
    }

    private ListView CreateStyledListView(List<ElementSprite> sourceList, CharacterProfile profile, ListView existingView)
    {
        var listView = new ListView();
        listView.style.flexGrow = 1;
        listView.fixedItemHeight = 32; // 行高
        listView.itemsSource = sourceList;
        listView.makeItem = () => CreateListItem();
        listView.bindItem = (e, i) => BindListItem(e, i, sourceList, profile, listView);

        // 选中时更新预览
        listView.selectionChanged += (items) => {
            foreach (var item in items) { if (item is ElementSprite data) UpdatePreview(data.Sprite); break; }
        };

        return listView;
    }

    // --- Item 渲染 ---
    private VisualElement CreateListItem()
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.alignItems = Align.Center;
        container.style.paddingLeft = 5;

        var nameField = new TextField() { name = "Name", style = { width = 120, marginRight = 5 } };
        var spriteField = new ObjectField() { name = "Sprite", objectType = typeof(Sprite), style = { flexGrow = 1 } };
        var delBtn = new Button() { text = "X", name = "Delete" };
        delBtn.style.width = 24;
        delBtn.style.backgroundColor = Color.clear;
        delBtn.style.color = new Color(0.8f, 0.4f, 0.4f);

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

        nameField.SetValueWithoutNotify(data.Element);
        spriteField.SetValueWithoutNotify(data.Sprite);

        // 交互逻辑
        nameField.RegisterValueChangedCallback(evt => {
            data.Element = evt.newValue;
            EditorUtility.SetDirty(profile);
        });

        spriteField.RegisterValueChangedCallback(evt => {
            data.Sprite = evt.newValue as Sprite;
            EditorUtility.SetDirty(profile);
            if (listView.selectedIndex == index) UpdatePreview(data.Sprite);
        });

        // 聚焦时自动选中行
        EventCallback<FocusInEvent> onFocus = (evt) => {
            if (listView.selectedIndex != index)
            {
                listView.SetSelection(index);
                UpdatePreview(data.Sprite);
            }
        };
        nameField.RegisterCallback(onFocus);
        spriteField.RegisterCallback(onFocus);

        delBtn.clicked += () => {
            list.RemoveAt(index);
            EditorUtility.SetDirty(profile);
            listView.Rebuild();
            UpdatePreview(null);
        };
    }

    // --- 辅助方法 ---
    private Label CreateSectionLabel(string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = new Color(0.7f, 0.7f, 0.7f);
        label.style.marginBottom = 5;
        label.style.marginTop = 5;
        return label;
    }

    private void CreateObjectField(VisualElement parent, string label, Object value, System.Action<Sprite> onChange)
    {
        var field = new ObjectField(label) { objectType = typeof(Sprite), value = value };
        field.style.marginBottom = 5;
        field.RegisterValueChangedCallback(evt => onChange(evt.newValue as Sprite));
        parent.Add(field);
    }

    private VisualElement CreateListHeader(string title, System.Action onAdd)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        header.style.paddingTop = 5;
        header.style.paddingBottom = 5;
        header.style.paddingLeft = 5;
        header.style.paddingRight = 5;

        header.Add(new Label(title) { style = { alignSelf = Align.Center, unityFontStyleAndWeight = FontStyle.Bold } });

        var addBtn = new Button(onAdd) { text = "+ 添加" };
        addBtn.style.backgroundColor = new Color(0.25f, 0.35f, 0.25f);
        header.Add(addBtn);

        return header;
    }

    private void UpdatePreview(Sprite sprite)
    {
        if (sprite == null)
        {
            previewImage.sprite = null;
        }
        else
        {
            previewImage.sprite = sprite;
        }
    }

    private void RenameAsset(CharacterProfile profile, string newName)
    {
        if (string.IsNullOrEmpty(newName)) return;
        string path = AssetDatabase.GetAssetPath(profile);
        string newPath = $"{CHARACTER_PATH}/{newName}.asset";

        if (path != newPath)
        {
            string error = AssetDatabase.RenameAsset(path, newName);
            if (string.IsNullOrEmpty(error))
            {
                AssetDatabase.SaveAssets();
                // 重新排序列表
                LoadAllProfiles();
            }
            else
            {
                Debug.LogWarning($"重命名失败: {error}");
            }
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

        // 选中新建的
        searchField.value = ""; // 清空搜索
        int index = allProfiles.IndexOf(newProfile);
        leftListView.SetSelection(index);
        leftListView.ScrollToItem(index);
    }

    private void DeleteCharacter(CharacterProfile profile)
    {
        if (EditorUtility.DisplayDialog("删除角色", $"确定要删除 {profile.CharacterID} 吗？\n此操作不可撤销。", "确定删除", "取消"))
        {
            string path = AssetDatabase.GetAssetPath(profile);
            AssetDatabase.DeleteAsset(path);

            rightPane.Clear();
            selectedProfile = null;
            LoadAllProfiles();
        }
    }
}