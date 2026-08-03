using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class CharacterEditorWindow : EditorWindow
{
    private const string CHARACTER_PATH = "Assets/Resources/VNovelizerRes/Characters";

    // UI 元素
    private VisualElement cardContainer; // 角色卡片网格
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
    private Dictionary<CharacterProfile, VisualElement> cardElements = new Dictionary<CharacterProfile, VisualElement>();

    // 当前选中的 Tab (0=Expression, 1=Head)
    private int currentTab = 0;

    // 配色
    private static readonly Color BgSidebar = new Color(0.16f, 0.16f, 0.16f);
    private static readonly Color BgMain = new Color(0.22f, 0.22f, 0.22f);
    private static readonly Color BgCard = new Color(0.25f, 0.25f, 0.25f);
    private static readonly Color BgCardSelected = new Color(0.23f, 0.33f, 0.47f);
    private static readonly Color BorderDark = new Color(0.1f, 0.1f, 0.1f);
    private static readonly Color AccentBlue = new Color(0.35f, 0.6f, 0.95f);
    private static readonly Color DropHighlight = new Color(0.22f, 0.35f, 0.26f);

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

        // 1. 主分栏 (左侧卡片网格，右侧详情)
        var splitView = new TwoPaneSplitView(0, 320, TwoPaneSplitViewOrientation.Horizontal);
        root.Add(splitView);

        // ==========================
        //        左侧：卡片栏
        // ==========================
        var leftPane = new VisualElement();
        leftPane.style.backgroundColor = BgSidebar;
        splitView.Add(leftPane);

        // 1.1 工具栏 (搜索 + 刷新 + 新建)
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.paddingTop = 5;
        toolbar.style.paddingBottom = 5;
        toolbar.style.paddingLeft = 5;
        toolbar.style.paddingRight = 5;
        toolbar.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        toolbar.style.borderBottomWidth = 1;
        toolbar.style.borderBottomColor = BorderDark;

        searchField = new TextField();
        searchField.style.flexGrow = 1;
        searchField.RegisterValueChangedCallback(evt => FilterList(evt.newValue));
        toolbar.Add(searchField);

        var refreshBtn = new Button(LoadAllProfiles) { text = "刷新", style = { width = 48 } };
        toolbar.Add(refreshBtn);

        var createBtn = new Button(CreateNewCharacter) { text = "+" };
        createBtn.style.width = 25;
        createBtn.style.backgroundColor = new Color(0.25f, 0.5f, 0.25f);
        toolbar.Add(createBtn);

        leftPane.Add(toolbar);

        // 1.2 角色卡片网格
        var cardScroll = new ScrollView(ScrollViewMode.Vertical);
        cardScroll.style.flexGrow = 1;

        cardContainer = new VisualElement();
        cardContainer.style.flexDirection = FlexDirection.Row;
        cardContainer.style.flexWrap = Wrap.Wrap;
        cardContainer.style.alignContent = Align.FlexStart;
        cardContainer.style.paddingTop = 4;
        cardContainer.style.paddingLeft = 4;

        cardScroll.Add(cardContainer);
        leftPane.Add(cardScroll);

        // ==========================
        //        右侧：详情栏
        // ==========================
        rightPane = new VisualElement();
        rightPane.style.paddingTop = 10;
        rightPane.style.paddingLeft = 15;
        rightPane.style.paddingRight = 15;
        rightPane.style.paddingBottom = 10;
        rightPane.style.backgroundColor = BgMain;

        splitView.Add(rightPane);

        // 初始加载
        LoadAllProfiles();

        // 初始显示提示
        ShowPlaceholder();
    }

    // ==========================
    //        P1: 卡片网格
    // ==========================

    private void RefreshCards()
    {
        cardContainer.Clear();
        cardElements.Clear();

        foreach (var profile in filteredProfiles)
        {
            var card = MakeCard(profile);
            cardElements[profile] = card;
            cardContainer.Add(card);
        }

        if (filteredProfiles.Count == 0)
        {
            var hint = new Label(allProfiles.Count == 0 ? "暂无角色，点击右上角 + 新建" : "没有匹配的角色")
            {
                style = { color = Color.gray, fontSize = 12, paddingTop = 10, paddingLeft = 6 }
            };
            cardContainer.Add(hint);
        }

        // 恢复选中高亮
        if (selectedProfile != null && cardElements.TryGetValue(selectedProfile, out var el))
        {
            SetCardSelected(el, true);
        }
    }

    private VisualElement MakeCard(CharacterProfile profile)
    {
        var card = new VisualElement();
        card.style.width = 92;
        card.style.marginTop = 4;
        card.style.marginBottom = 4;
        card.style.marginLeft = 4;
        card.style.marginRight = 4;
        card.style.paddingTop = 4;
        card.style.paddingBottom = 4;
        card.style.backgroundColor = BgCard;
        card.style.borderTopLeftRadius = 6;
        card.style.borderTopRightRadius = 6;
        card.style.borderBottomLeftRadius = 6;
        card.style.borderBottomRightRadius = 6;
        SetCardBorder(card, Color.clear);

        // 封面图（取第一个有立绘的表情）
        var cover = profile.ElementSprites.FirstOrDefault(e => e != null && e.Sprite != null)?.Sprite;

        var imgWrap = new VisualElement();
        imgWrap.style.width = 84;
        imgWrap.style.height = 84;
        imgWrap.style.alignSelf = Align.Center;
        imgWrap.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
        imgWrap.style.borderTopLeftRadius = 4;
        imgWrap.style.borderTopRightRadius = 4;
        imgWrap.style.borderBottomLeftRadius = 4;
        imgWrap.style.borderBottomRightRadius = 4;

        var img = new Image { scaleMode = ScaleMode.ScaleToFit, sprite = cover };
        img.style.position = Position.Absolute;
        img.style.left = 0; img.style.right = 0; img.style.top = 0; img.style.bottom = 0;
        imgWrap.Add(img);

        if (cover == null)
        {
            var ph = new Label("无立绘")
            {
                style = {
                    position = Position.Absolute,
                    left = 0, right = 0, top = 0, bottom = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = new Color(0.45f, 0.45f, 0.45f),
                    fontSize = 10
                }
            };
            imgWrap.Add(ph);
        }

        var nameLabel = new Label(profile.CharacterID)
        {
            style = {
                unityTextAlign = TextAnchor.MiddleCenter,
                fontSize = 11,
                marginTop = 3,
                whiteSpace = WhiteSpace.Normal,
                color = new Color(0.85f, 0.85f, 0.85f)
            }
        };

        card.Add(imgWrap);
        card.Add(nameLabel);

        card.RegisterCallback<ClickEvent>(evt => SelectProfile(profile));

        // P3: 右键菜单
        card.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction("编辑", a => SelectProfile(profile));
            evt.menu.AppendAction("在 Project 中显示", a => EditorGUIUtility.PingObject(profile));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("复制角色", a => DuplicateCharacter(profile));
            evt.menu.AppendAction("删除角色", a => DeleteCharacter(profile));
        }));

        return card;
    }

    private void SetCardSelected(VisualElement card, bool selected)
    {
        card.style.backgroundColor = selected ? BgCardSelected : BgCard;
        SetCardBorder(card, selected ? AccentBlue : Color.clear);
    }

    private void SetCardBorder(VisualElement card, Color color)
    {
        card.style.borderTopWidth = 1; card.style.borderTopColor = color;
        card.style.borderBottomWidth = 1; card.style.borderBottomColor = color;
        card.style.borderLeftWidth = 1; card.style.borderLeftColor = color;
        card.style.borderRightWidth = 1; card.style.borderRightColor = color;
    }

    private void SelectProfile(CharacterProfile profile)
    {
        selectedProfile = profile;

        foreach (var kv in cardElements)
        {
            SetCardSelected(kv.Value, kv.Key == profile);
        }

        if (profile != null)
        {
            rightPane.Clear();
            DrawDetailView(profile);
        }
        else
        {
            ShowPlaceholder();
        }
    }

    // ==========================
    //        数据加载
    // ==========================

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
        RefreshCards();
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

    // ==========================
    //        右侧详情
    // ==========================

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
                profile.CharacterID = idField.value;
                EditorUtility.SetDirty(profile);
                RenameAsset(profile, idField.value); // 尝试重命名文件
                RefreshCards();
            }
        });

        var deleteBtn = new Button(() => DeleteCharacter(profile)) { text = "删除" };
        deleteBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
        deleteBtn.style.width = 60;
        deleteBtn.style.flexShrink = 0; // 防止被挤出可视区

        // TextField 在横向布局中有默认最小宽度，需允许收缩并清除 minWidth，否则会把右侧按钮挤出去
        idField.style.flexShrink = 1;
        idField.style.minWidth = 0;
        idField.style.marginRight = 8;

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

        middleContainer.Add(configPane);

        // 2.2 右侧：预览图
        var previewPane = new VisualElement();
        previewPane.style.width = 200; // 固定宽度预览区

        previewPane.style.borderTopWidth = 1; previewPane.style.borderTopColor = BorderDark;
        previewPane.style.borderBottomWidth = 1; previewPane.style.borderBottomColor = BorderDark;
        previewPane.style.borderLeftWidth = 1; previewPane.style.borderLeftColor = BorderDark;
        previewPane.style.borderRightWidth = 1; previewPane.style.borderRightColor = BorderDark;

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

        expTab = CreateTabButton("立绘 (Expressions)", 0);
        headTab = CreateTabButton("头像 (Heads)", 1);

        tabContainer.Add(expTab);
        tabContainer.Add(headTab);
        rightPane.Add(tabContainer);

        // 列表内容容器
        var listContainer = new VisualElement();
        listContainer.style.flexGrow = 1;
        listContainer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f); // 列表背景

        listContainer.style.borderTopWidth = 1; listContainer.style.borderTopColor = BorderDark;
        listContainer.style.borderBottomWidth = 1; listContainer.style.borderBottomColor = BorderDark;
        listContainer.style.borderLeftWidth = 1; listContainer.style.borderLeftColor = BorderDark;
        listContainer.style.borderRightWidth = 1; listContainer.style.borderRightColor = BorderDark;

        rightPane.Add(listContainer);

        expressionContainer = new VisualElement() { style = { flexGrow = 1 } };
        headContainer = new VisualElement() { style = { flexGrow = 1, display = DisplayStyle.None } };

        listContainer.Add(expressionContainer);
        listContainer.Add(headContainer);

        DrawExpressionList(profile);
        DrawHeadList(profile);

        // P2: 拖放导入
        RegisterDropHandlers(expressionContainer, profile.ElementSprites, elementListView, profile, "立绘");
        RegisterDropHandlers(headContainer, profile.HeadSprites, headSpriteListView, profile, "头像");

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

        if (expTab != null)
            expTab.style.backgroundColor = (index == 0) ? new Color(0.28f, 0.28f, 0.28f) : new Color(0.2f, 0.2f, 0.2f);

        if (headTab != null)
            headTab.style.backgroundColor = (index == 1) ? new Color(0.28f, 0.28f, 0.28f) : new Color(0.2f, 0.2f, 0.2f);

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

        elementListView = CreateStyledListView(profile.ElementSprites, profile);
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

        headSpriteListView = CreateStyledListView(profile.HeadSprites, profile);
        headContainer.Add(headSpriteListView);
    }

    private ListView CreateStyledListView(List<ElementSprite> sourceList, CharacterProfile profile)
    {
        var listView = new ListView();
        listView.style.flexGrow = 1;
        listView.fixedItemHeight = 32; // 行高
        listView.itemsSource = sourceList;
        listView.makeItem = () => CreateListItem();
        listView.bindItem = (e, i) => BindListItem(e, i, sourceList, profile, listView);
        // 关键：列表回收时清理回调和数据引用，避免复用时数据错乱
        listView.unbindItem = (e, i) => UnbindListItem(e);

        // P3: 拖放排序
        listView.reorderable = true;
        listView.reorderMode = ListViewReorderMode.Animated;
        listView.itemIndexChanged += (a, b) => EditorUtility.SetDirty(profile);

        // 选中时更新预览
        listView.selectionChanged += (items) => {
            foreach (var item in items) { if (item is ElementSprite data) UpdatePreview(data.Sprite); break; }
        };

        return listView;
    }

    // --- Item 渲染 ---
    private class ItemContext
    {
        public ElementSprite data;
        public List<ElementSprite> list;
        public CharacterProfile profile;
        public ListView listView;
        public EventCallback<FocusInEvent> focusCb;
    }

    private VisualElement CreateListItem()
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.alignItems = Align.Center;
        container.style.paddingLeft = 5;
        container.style.paddingRight = 30; // 为右侧的 X 按钮预留空间

        var nameField = new TextField() { name = "Name", style = { width = 100, marginRight = 5, flexShrink = 0 } };
        var spriteField = new ObjectField() { name = "Sprite", objectType = typeof(Sprite), style = { flexGrow = 1, flexShrink = 1, minWidth = 80 } };
        var delBtn = new Button() { text = "X", name = "Delete" };
        delBtn.style.position = Position.Absolute;
        delBtn.style.right = 4;
        delBtn.style.top = 4;
        delBtn.style.bottom = 4;
        delBtn.style.width = 24;
        delBtn.style.backgroundColor = new Color(0, 0, 0, 0);
        delBtn.style.color = new Color(0.8f, 0.4f, 0.4f);

        container.Add(nameField);
        container.Add(spriteField);
        container.Add(delBtn);

        // P3: 列表项右键菜单（在 bind 时通过 userData 获取当前条目）
        container.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            if (container.userData is ItemContext ctx && ctx.data != null)
            {
                evt.menu.AppendAction("复制此项", a =>
                {
                    int idx = ctx.list.IndexOf(ctx.data);
                    if (idx < 0) idx = ctx.list.Count - 1;
                    ctx.list.Insert(idx + 1, new ElementSprite { Element = ctx.data.Element + "_copy", Sprite = ctx.data.Sprite });
                    EditorUtility.SetDirty(ctx.profile);
                    ctx.listView.Rebuild();
                });
                evt.menu.AppendAction("删除此项", a =>
                {
                    ctx.list.Remove(ctx.data);
                    EditorUtility.SetDirty(ctx.profile);
                    ctx.listView.Rebuild();
                    UpdatePreview(null);
                });
            }
        }));

        return container;
    }

    // 在 item 从 ListView 池中回收时清理回调，避免复用时多个 callback 互相影响
    private void UnbindListItem(VisualElement element)
    {
        var nameField = element.Q<TextField>("Name");
        var spriteField = element.Q<ObjectField>("Sprite");
        var delBtn = element.Q<Button>("Delete");

        if (nameField?.userData is EventCallback<ChangeEvent<string>> nameCb)
        {
            nameField.UnregisterValueChangedCallback(nameCb);
            nameField.userData = null;
        }
        if (spriteField?.userData is EventCallback<ChangeEvent<UnityEngine.Object>> spriteCb)
        {
            spriteField.UnregisterValueChangedCallback(spriteCb);
            spriteField.userData = null;
        }
        if (delBtn?.userData is System.Action delAction)
        {
            delBtn.clicked -= delAction;
            delBtn.userData = null;
        }

        // 清理聚焦回调与上下文
        if (element.userData is ItemContext ctx)
        {
            if (ctx.focusCb != null)
            {
                nameField?.UnregisterCallback(ctx.focusCb);
                spriteField?.UnregisterCallback(ctx.focusCb);
            }
            element.userData = null;
        }
    }

    private void BindListItem(VisualElement element, int index, List<ElementSprite> list, CharacterProfile profile, ListView listView)
    {
        if (index >= list.Count) return;
        var data = list[index];

        var nameField = element.Q<TextField>("Name");
        var spriteField = element.Q<ObjectField>("Sprite");
        var delBtn = element.Q<Button>("Delete");

        // 先清理可能残留的旧回调
        UnbindListItem(element);

        nameField.SetValueWithoutNotify(data.Element);
        spriteField.SetValueWithoutNotify(data.Sprite);

        // 持久化回调到 userData，便于 unbind 时清理
        EventCallback<ChangeEvent<string>> nameChanged = evt =>
        {
            data.Element = evt.newValue;
            EditorUtility.SetDirty(profile);
        };
        nameField.RegisterValueChangedCallback(nameChanged);
        nameField.userData = nameChanged;

        EventCallback<ChangeEvent<UnityEngine.Object>> spriteChanged = evt =>
        {
            data.Sprite = evt.newValue as Sprite;
            EditorUtility.SetDirty(profile);
            if (listView.selectedIndex == index) UpdatePreview(data.Sprite);
        };
        spriteField.RegisterValueChangedCallback(spriteChanged);
        spriteField.userData = spriteChanged;

        // 聚焦时自动选中行
        EventCallback<FocusInEvent> onFocus = evt =>
        {
            if (listView.selectedIndex != index)
            {
                listView.SetSelection(index);
                UpdatePreview(data.Sprite);
            }
        };
        nameField.RegisterCallback(onFocus);
        spriteField.RegisterCallback(onFocus);

        // 删除按钮
        System.Action delAction = () =>
        {
            list.Remove(data);
            EditorUtility.SetDirty(profile);
            listView.Rebuild();
            UpdatePreview(null);
        };
        delBtn.clicked += delAction;
        delBtn.userData = delAction;

        // 记录上下文（供 unbind 清理 + 右键菜单使用）
        element.userData = new ItemContext
        {
            data = data,
            list = list,
            profile = profile,
            listView = listView,
            focusCb = onFocus
        };
    }

    // ==========================
    //   P2: 拖放导入 + 批量解析
    // ==========================

    private void RegisterDropHandlers(VisualElement dropZone, List<ElementSprite> targetList, ListView targetListView, CharacterProfile profile, string kindLabel)
    {
        dropZone.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            if (DragAndDrop.objectReferences.Any(o => o is Sprite || o is Texture2D))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                dropZone.style.backgroundColor = DropHighlight;
            }
        });

        dropZone.RegisterCallback<DragLeaveEvent>(evt => dropZone.style.backgroundColor = Color.clear);
        dropZone.RegisterCallback<DragExitedEvent>(evt => dropZone.style.backgroundColor = Color.clear);

        dropZone.RegisterCallback<DragPerformEvent>(evt =>
        {
            dropZone.style.backgroundColor = Color.clear;
            var sprites = CollectDroppedSprites();
            if (sprites.Count == 0) return;

            DragAndDrop.AcceptDrag();
            ShowBatchImportPanel(sprites, targetList, targetListView, profile, kindLabel);
        });
    }

    private List<Sprite> CollectDroppedSprites()
    {
        var result = new List<Sprite>();
        foreach (var obj in DragAndDrop.objectReferences)
        {
            if (obj is Sprite s)
            {
                if (!result.Contains(s)) result.Add(s);
            }
            else if (obj is Texture2D)
            {
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                if (sp != null && !result.Contains(sp)) result.Add(sp);
            }
        }
        return result;
    }

    // 从文件名解析情绪名：去掉 "角色ID_" 前缀
    private string ParseElementName(string spriteName, string charId)
    {
        string n = spriteName;
        if (!string.IsNullOrEmpty(charId) && n.StartsWith(charId + "_", StringComparison.OrdinalIgnoreCase))
        {
            n = n.Substring(charId.Length + 1);
        }
        return n;
    }

    private class ImportRow
    {
        public Sprite sprite;
        public TextField nameField;
        public Label statusLabel;
        public VisualElement row;
    }

    private void ShowBatchImportPanel(List<Sprite> sprites, List<ElementSprite> targetList, ListView targetListView, CharacterProfile profile, string kindLabel)
    {
        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0; overlay.style.right = 0; overlay.style.top = 0; overlay.style.bottom = 0;
        overlay.style.backgroundColor = new Color(0, 0, 0, 0.6f);
        overlay.style.justifyContent = Justify.Center;
        overlay.style.alignItems = Align.Center;

        var box = new VisualElement();
        box.style.width = 540;
        box.style.maxHeight = 500;
        box.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        box.style.paddingTop = 12; box.style.paddingBottom = 12;
        box.style.paddingLeft = 12; box.style.paddingRight = 12;
        box.style.borderTopLeftRadius = 8; box.style.borderTopRightRadius = 8;
        box.style.borderBottomLeftRadius = 8; box.style.borderBottomRightRadius = 8;

        box.Add(new Label($"批量导入{kindLabel}（{sprites.Count} 张）")
        {
            style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
        });
        box.Add(new Label("已按文件名自动解析情绪名称（去掉角色ID前缀），可修改后再导入。同名条目将被覆盖。")
        {
            style = { color = new Color(0.65f, 0.65f, 0.65f), fontSize = 11, marginBottom = 8, whiteSpace = WhiteSpace.Normal }
        });

        var rowsScroll = new ScrollView(ScrollViewMode.Vertical);
        rowsScroll.style.flexGrow = 1;
        rowsScroll.style.minHeight = 60;
        box.Add(rowsScroll);

        var rows = new List<ImportRow>();

        System.Action<ImportRow> updateStatus = null;
        updateStatus = (r) =>
        {
            string name = r.nameField.value.Trim();
            if (string.IsNullOrEmpty(name))
            {
                r.statusLabel.text = "名称为空";
                r.statusLabel.style.color = new Color(0.9f, 0.35f, 0.35f);
            }
            else if (targetList.Any(e => e != null && e.Element == name))
            {
                r.statusLabel.text = "覆盖同名";
                r.statusLabel.style.color = new Color(0.95f, 0.7f, 0.3f);
            }
            else
            {
                r.statusLabel.text = "新增";
                r.statusLabel.style.color = new Color(0.5f, 0.85f, 0.5f);
            }
        };

        foreach (var sprite in sprites)
        {
            var r = new ImportRow { sprite = sprite };

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.paddingTop = 3; row.style.paddingBottom = 3;
            row.style.paddingLeft = 4; row.style.paddingRight = 4;
            row.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            row.style.borderTopLeftRadius = 4; row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4; row.style.borderBottomRightRadius = 4;

            var thumb = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit };
            thumb.style.width = 36; thumb.style.height = 36;
            thumb.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            thumb.style.marginRight = 8;
            thumb.style.flexShrink = 0;
            row.Add(thumb);

            var nameField = new TextField { value = ParseElementName(sprite.name, profile.CharacterID) };
            nameField.style.flexGrow = 1;
            nameField.style.marginRight = 8;
            row.Add(nameField);

            var statusLabel = new Label { style = { width = 60, unityTextAlign = TextAnchor.MiddleCenter, fontSize = 11, flexShrink = 0 } };
            row.Add(statusLabel);

            var removeBtn = new Button(() =>
            {
                rows.Remove(r);
                r.row.RemoveFromHierarchy();
            }) { text = "×" };
            removeBtn.style.width = 24;
            removeBtn.style.marginLeft = 6;
            removeBtn.style.flexShrink = 0;
            removeBtn.style.backgroundColor = new Color(0, 0, 0, 0);
            removeBtn.style.color = new Color(0.8f, 0.4f, 0.4f);
            row.Add(removeBtn);

            r.nameField = nameField;
            r.statusLabel = statusLabel;
            r.row = row;
            rows.Add(r);

            nameField.RegisterValueChangedCallback(evt => updateStatus(r));
            updateStatus(r);

            rowsScroll.Add(row);
        }

        // 底部按钮
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.justifyContent = Justify.FlexEnd;
        btnRow.style.marginTop = 10;

        var cancelBtn = new Button(() => overlay.RemoveFromHierarchy()) { text = "取消" };
        cancelBtn.style.width = 80;
        cancelBtn.style.marginRight = 8;
        btnRow.Add(cancelBtn);

        var applyBtn = new Button(() =>
        {
            int count = 0;
            foreach (var r in rows.ToList())
            {
                string name = r.nameField.value.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var existing = targetList.FirstOrDefault(e => e != null && e.Element == name);
                if (existing != null)
                {
                    existing.Sprite = r.sprite;
                }
                else
                {
                    targetList.Add(new ElementSprite { Element = name, Sprite = r.sprite });
                }
                count++;
            }

            if (count > 0)
            {
                EditorUtility.SetDirty(profile);
                targetListView.Rebuild();
                RefreshCards(); // 封面可能变化
            }
            overlay.RemoveFromHierarchy();
        }) { text = "导入" };
        applyBtn.style.width = 80;
        applyBtn.style.backgroundColor = new Color(0.25f, 0.5f, 0.3f);
        btnRow.Add(applyBtn);

        box.Add(btnRow);
        overlay.Add(box);

        // 点击遮罩空白处关闭
        overlay.RegisterCallback<ClickEvent>(evt =>
        {
            if (evt.target == overlay) overlay.RemoveFromHierarchy();
        });

        rootVisualElement.Add(overlay);
    }

    // ==========================
    //        辅助方法
    // ==========================

    private Label CreateSectionLabel(string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = new Color(0.7f, 0.7f, 0.7f);
        label.style.marginBottom = 5;
        label.style.marginTop = 5;
        return label;
    }

    private void CreateObjectField(VisualElement parent, string label, UnityEngine.Object value, System.Action<Sprite> onChange)
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
        header.style.alignItems = Align.Center;
        header.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        header.style.paddingTop = 5;
        header.style.paddingBottom = 5;
        header.style.paddingLeft = 5;
        header.style.paddingRight = 5;

        header.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold } });

        var hint = new Label("可拖拽图片到此批量导入")
        {
            style = { color = new Color(0.5f, 0.5f, 0.5f), fontSize = 10, marginLeft = 8, flexGrow = 1 }
        };
        header.Add(hint);

        var addBtn = new Button(onAdd) { text = "+ 添加" };
        addBtn.style.backgroundColor = new Color(0.25f, 0.35f, 0.25f);
        addBtn.style.flexShrink = 0;
        header.Add(addBtn);

        return header;
    }

    private void UpdatePreview(Sprite sprite)
    {
        if (previewImage == null) return;
        previewImage.sprite = sprite;
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
        SelectProfile(newProfile);
    }

    private void DuplicateCharacter(CharacterProfile profile)
    {
        string path = AssetDatabase.GetAssetPath(profile);
        string newPath = AssetDatabase.GenerateUniqueAssetPath(path);

        if (AssetDatabase.CopyAsset(path, newPath))
        {
            var copy = AssetDatabase.LoadAssetAtPath<CharacterProfile>(newPath);
            if (copy != null)
            {
                copy.CharacterID = Path.GetFileNameWithoutExtension(newPath);
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssets();
                LoadAllProfiles();
                SelectProfile(copy);
            }
        }
    }

    private void DeleteCharacter(CharacterProfile profile)
    {
        if (EditorUtility.DisplayDialog("删除角色", $"确定要删除 {profile.CharacterID} 吗？\n此操作不可撤销。", "确定删除", "取消"))
        {
            string path = AssetDatabase.GetAssetPath(profile);
            AssetDatabase.DeleteAsset(path);

            selectedProfile = null;
            LoadAllProfiles();
            ShowPlaceholder();
        }
    }
}
