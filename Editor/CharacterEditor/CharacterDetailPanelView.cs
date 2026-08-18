using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// MVP - View：角色编辑器右侧详情面板。
/// 包含角色 ID 编辑、基础配置、预览图、表情/头像列表、拖放导入。
/// </summary>
public class CharacterDetailPanelView : VisualElement
{
    private readonly CharacterEditorPresenter presenter;
    private readonly System.Func<BigPreviewOverlay> getPreviewOverlay;
    private BigPreviewOverlay previewOverlay => getPreviewOverlay?.Invoke();

    private ScrollView scrollRoot;
    private VisualElement contentPane;
    private CharacterProfile currentProfile;

    // 预览图
    private Image previewImage;

    // Tab 按钮
    private Button expTab;
    private Button headTab;
    private int currentTab;

    // 列表
    private VisualElement expressionContainer;
    private VisualElement headContainer;

    public CharacterDetailPanelView(CharacterEditorPresenter presenter, System.Func<BigPreviewOverlay> getPreviewOverlay)
    {
        this.presenter = presenter;
        this.getPreviewOverlay = getPreviewOverlay;
        style.flexGrow = 1;
        style.flexShrink = 1;
        style.minWidth = 0;

        Build();
        presenter.OnSelectionChanged += OnSelectionChanged;
    }

    public void Dispose()
    {
        presenter.OnSelectionChanged -= OnSelectionChanged;
    }

    private void Build()
    {
        scrollRoot = new ScrollView(ScrollViewMode.Vertical);
        scrollRoot.style.flexGrow = 1;
        scrollRoot.style.flexShrink = 1;
        scrollRoot.style.minWidth = 0;
        Add(scrollRoot);

        contentPane = new VisualElement();
        contentPane.style.paddingTop = 16;
        contentPane.style.paddingLeft = 20;
        contentPane.style.paddingRight = 20;
        contentPane.style.paddingBottom = 16;
        contentPane.style.flexGrow = 1;
        contentPane.style.minWidth = 0;
        scrollRoot.Add(contentPane);
    }

    private void OnSelectionChanged(CharacterProfile profile)
    {
        Rebuild();
    }

    public void Rebuild()
    {
        // 清理可能残留的批量导入浮层
        for (int i = childCount - 1; i >= 0; i--)
        {
            var child = this[i];
            if (child.name == "batchImportOverlay")
                child.RemoveFromHierarchy();
        }

        contentPane.Clear();
        currentProfile = presenter.SelectedProfile;
        previewImage = null;

        if (currentProfile == null)
        {
            contentPane.Add(BuildEmptyHint());
            return;
        }

        DrawDetailView(currentProfile);
    }

    // =========================================================
    //                      空状态
    // =========================================================
    private VisualElement BuildEmptyHint()
    {
        var box = new VisualElement();
        box.style.flexGrow = 1;
        box.style.alignItems = Align.Center;
        box.style.justifyContent = Justify.Center;
        box.style.paddingTop = 80;
        box.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        box.Add(new Label("请在左侧选择一个角色或新建角色") { style = { fontSize = 14 } });
        return box;
    }

    // =========================================================
    //                      详情主体
    // =========================================================
    private void DrawDetailView(CharacterProfile profile)
    {
        DrawHeader(profile);
        DrawMiddleSection(profile);
        DrawTabSection(profile);
    }

    // =========================================================
    //                      Header（ID + 删除）
    // =========================================================
    private void DrawHeader(CharacterProfile profile)
    {
        var headerBox = new VisualElement();
        headerBox.style.flexDirection = FlexDirection.Row;
        headerBox.style.alignItems = Align.Center;
        headerBox.style.marginBottom = 16;
        headerBox.style.paddingBottom = 16;
        headerBox.style.borderBottomWidth = 1;
        headerBox.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        headerBox.style.minWidth = 0;

        var idField = new TextField("角色 ID") { value = profile.CharacterID };
        idField.style.flexGrow = 1;
        idField.style.flexShrink = 1;
        idField.style.minWidth = 0;
        idField.style.marginRight = 8;
        idField.style.fontSize = 14;
        idField.style.unityFontStyleAndWeight = FontStyle.Bold;
        GalleryStyles.ApplyField(idField);

        idField.RegisterCallback<FocusOutEvent>(_ =>
        {
            if (currentProfile == null) return;
            if (currentProfile.CharacterID != idField.value)
            {
                string newName = idField.value;
                presenter.RenameCharacter(currentProfile, newName);
                // 刷新列表中的卡片
                if (presenter.OnDataChanged != null) presenter.OnDataChanged();
            }
        });

        var delBtn = new Button(() => presenter.DeleteCharacter(profile)) { text = "删除" };
        GalleryStyles.ApplyButton(delBtn, GalleryTheme.Danger, true);
        delBtn.style.width = 64;
        delBtn.style.flexShrink = 0;

        headerBox.Add(idField);
        headerBox.Add(delBtn);
        contentPane.Add(headerBox);
    }

    // =========================================================
    //                      中部：配置 + 预览
    // =========================================================
    private void DrawMiddleSection(CharacterProfile profile)
    {
        var middleContainer = new VisualElement();
        middleContainer.style.flexDirection = FlexDirection.Row;
        middleContainer.style.height = 200;
        middleContainer.style.marginBottom = 10;
        middleContainer.style.minWidth = 0;

        // 左侧：基础配置卡片
        var configCard = GalleryStyles.MakeCard();
        configCard.style.flexGrow = 1;
        configCard.style.flexShrink = 1;
        configCard.style.minWidth = 0;
        configCard.style.marginRight = 10;

        configCard.Add(CreateCardSectionLabel("基础配置"));

        // SpeakerBox
        var speakerField = new ObjectField("姓名框 (SpeakerBox)")
        {
            objectType = typeof(Sprite),
            value = profile.SpeakerBox
        };
        speakerField.style.marginBottom = 5;
        speakerField.style.flexShrink = 1;
        speakerField.style.minWidth = 0;
        GalleryStyles.ApplyField(speakerField);
        speakerField.RegisterValueChangedCallback(evt =>
        {
            profile.SpeakerBox = evt.newValue as Sprite;
            EditorUtility.SetDirty(profile);
        });
        configCard.Add(speakerField);

        // HeadFrame
        var headField = new ObjectField("头像框 (HeadFrame)")
        {
            objectType = typeof(Sprite),
            value = profile.HeadFrame
        };
        headField.style.marginBottom = 5;
        headField.style.flexShrink = 1;
        headField.style.minWidth = 0;
        GalleryStyles.ApplyField(headField);
        headField.RegisterValueChangedCallback(evt =>
        {
            profile.HeadFrame = evt.newValue as Sprite;
            EditorUtility.SetDirty(profile);
        });
        configCard.Add(headField);

        // Scale
        var scaleField = new FloatField("缩放 (Scale)") { value = profile.scale };
        scaleField.style.marginBottom = 5;
        scaleField.style.flexShrink = 1;
        scaleField.style.minWidth = 0;
        GalleryStyles.ApplyField(scaleField);
        scaleField.RegisterValueChangedCallback(evt =>
        {
            profile.scale = evt.newValue;
            EditorUtility.SetDirty(profile);
        });
        configCard.Add(scaleField);

        // Offset
        var offsetField = new Vector2Field("偏移 (Offset)") { value = profile.offset };
        offsetField.style.marginBottom = 5;
        offsetField.style.flexShrink = 1;
        offsetField.style.minWidth = 0;
        GalleryStyles.ApplyField(offsetField);
        offsetField.RegisterValueChangedCallback(evt =>
        {
            profile.offset = evt.newValue;
            EditorUtility.SetDirty(profile);
        });
        configCard.Add(offsetField);

        middleContainer.Add(configCard);

        // 右侧：预览图
        var previewPane = new VisualElement();
        previewPane.style.width = 200;
        previewPane.style.flexShrink = 0;
        previewPane.style.borderTopWidth = 1;
        previewPane.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
        previewPane.style.borderBottomWidth = 1;
        previewPane.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        previewPane.style.borderLeftWidth = 1;
        previewPane.style.borderLeftColor = GalleryTheme.Hex(GalleryTheme.Border);
        previewPane.style.borderRightWidth = 1;
        previewPane.style.borderRightColor = GalleryTheme.Hex(GalleryTheme.Border);
        previewPane.style.borderTopLeftRadius = 6;
        previewPane.style.borderTopRightRadius = 6;
        previewPane.style.borderBottomLeftRadius = 6;
        previewPane.style.borderBottomRightRadius = 6;
        previewPane.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        previewPane.style.justifyContent = Justify.Center;
        previewPane.style.alignItems = Align.Center;

        previewImage = new Image();
        previewImage.scaleMode = ScaleMode.ScaleToFit;
        previewImage.style.flexGrow = 1;
        previewImage.style.width = Length.Percent(90);
        previewImage.style.height = Length.Percent(90);
        previewImage.pickingMode = PickingMode.Ignore;

        previewPane.Add(previewImage);

        // 点击预览图弹大图
        previewPane.RegisterCallback<ClickEvent>(_ =>
        {
            if (previewImage?.sprite != null && previewOverlay != null)
                previewOverlay.Show(previewImage.sprite);
        });

        // 无预览时的提示
        var previewHint = new Label("选中表情查看预览")
        {
            name = "previewHint",
            pickingMode = PickingMode.Ignore,
            style =
            {
                position = Position.Absolute,
                top = 0, bottom = 0, left = 0, right = 0,
                unityTextAlign = TextAnchor.MiddleCenter,
                color = GalleryTheme.Hex(GalleryTheme.TextMuted),
                fontSize = 11
            }
        };
        previewPane.Add(previewHint);

        middleContainer.Add(previewPane);
        contentPane.Add(middleContainer);
    }

    // =========================================================
    //                      Tab 区域
    // =========================================================
    private void DrawTabSection(CharacterProfile profile)
    {
        // Tab 按钮行
        var tabContainer = new VisualElement();
        tabContainer.style.flexDirection = FlexDirection.Row;
        tabContainer.style.marginBottom = 0;

        expTab = CreateTabButton("立绘 (Expressions)", 0);
        headTab = CreateTabButton("头像 (Heads)", 1);

        tabContainer.Add(expTab);
        tabContainer.Add(headTab);
        contentPane.Add(tabContainer);

        // 列表内容容器
        var listContainer = new VisualElement();
        listContainer.style.flexGrow = 1;
        listContainer.style.minHeight = 160;

        expressionContainer = new VisualElement() { style = { flexGrow = 1, display = DisplayStyle.Flex } };
        headContainer = new VisualElement() { style = { flexGrow = 1, display = DisplayStyle.None } };

        listContainer.Add(expressionContainer);
        listContainer.Add(headContainer);
        contentPane.Add(listContainer);

        DrawExpressionList(profile);
        DrawHeadList(profile);

        // 拖放导入（二维分组：文件名三级自动分组）
        RegisterDropHandlers(expressionContainer, profile.ElementSpriteGroups, profile, "立绘",
            () => Rebuild());
        RegisterDropHandlers(headContainer, profile.HeadSpriteGroups, profile, "头像",
            () => Rebuild());

        SwitchTab(currentTab);
    }

    // =========================================================
    //                      Tab 切换
    // =========================================================
    private void SwitchTab(int index)
    {
        currentTab = index;

        if (expressionContainer != null)
            expressionContainer.style.display = (index == 0) ? DisplayStyle.Flex : DisplayStyle.None;

        if (headContainer != null)
            headContainer.style.display = (index == 1) ? DisplayStyle.Flex : DisplayStyle.None;

        UpdateTabButtonStyle(expTab, index == 0);
        UpdateTabButtonStyle(headTab, index == 1);
    }

    private Button CreateTabButton(string text, int index)
    {
        var btn = new Button(() => SwitchTab(index)) { text = text };
        btn.style.flexGrow = 1;
        btn.style.height = 36;
        btn.style.marginRight = 0;
        btn.style.marginLeft = 0;
        btn.style.fontSize = 13;
        btn.style.borderTopWidth = 0;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        btn.style.borderBottomWidth = 2;
        btn.style.borderTopLeftRadius = 6;
        btn.style.borderTopRightRadius = 6;
        btn.style.borderBottomLeftRadius = 0;
        btn.style.borderBottomRightRadius = 0;
        return btn;
    }

    private void UpdateTabButtonStyle(Button btn, bool active)
    {
        if (btn == null) return;
        btn.style.backgroundColor = active
            ? GalleryTheme.Hex(GalleryTheme.Accent)
            : GalleryTheme.Hex(GalleryTheme.BgCard);
        btn.style.color = active ? Color.white : GalleryTheme.Hex(GalleryTheme.TextSecondary);
        btn.style.borderBottomColor = active ? Color.white : GalleryTheme.Hex(GalleryTheme.Border);
        btn.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
    }

    // =========================================================
    //                      列表绘制
    // =========================================================
    private void DrawExpressionList(CharacterProfile profile)
    {
        var header = CreateListHeader("表情立绘分组", () =>
        {
            string newName = MakeUniqueGroupName(profile.ElementSpriteGroups, "NewGroup");
            profile.ElementSpriteGroups.Add(new ElementSpriteGroup { Group = newName });
            EditorUtility.SetDirty(profile);
            Rebuild();
        }, "可拖拽图片到此批量导入（文件名 角色ID_分组_表情 自动分组）");
        expressionContainer.Add(header);

        DrawGroupSections(expressionContainer, profile.ElementSpriteGroups, profile, "立绘");
    }

    private void DrawHeadList(CharacterProfile profile)
    {
        var header = CreateListHeader("表情头像分组", () =>
        {
            string newName = MakeUniqueGroupName(profile.HeadSpriteGroups, "NewGroup");
            profile.HeadSpriteGroups.Add(new ElementSpriteGroup { Group = newName });
            EditorUtility.SetDirty(profile);
            Rebuild();
        }, "可拖拽图片到此批量导入（文件名 角色ID_分组_表情 自动分组）");
        headContainer.Add(header);

        DrawGroupSections(headContainer, profile.HeadSpriteGroups, profile, "头像");
    }

    private string MakeUniqueGroupName(List<ElementSpriteGroup> groups, string baseName)
    {
        string name = baseName;
        int i = 1;
        while (groups.Any(g => g != null && g.Group == name))
        {
            name = baseName + " " + (++i);
        }
        return name;
    }

    /// <summary>
    /// 绘制分组区块：每组一个卡片（组名可编辑 + 组内表情 ListView + 添加/删除）
    /// </summary>
    private void DrawGroupSections(VisualElement container, List<ElementSpriteGroup> groups, CharacterProfile profile, string kindLabel)
    {
        foreach (var group in groups.ToList())
        {
            if (group == null) continue;

            var section = new VisualElement();
            section.style.marginTop = 6;
            section.style.marginBottom = 8;
            section.style.borderTopWidth = 1;
            section.style.borderLeftWidth = 1;
            section.style.borderRightWidth = 1;
            section.style.borderBottomWidth = 1;
            section.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
            section.style.borderLeftColor = GalleryTheme.Hex(GalleryTheme.Border);
            section.style.borderRightColor = GalleryTheme.Hex(GalleryTheme.Border);
            section.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
            section.style.borderTopLeftRadius = 4;
            section.style.borderTopRightRadius = 4;

            // ---- 组头：组名 + 计数 + 操作按钮 ----
            var groupHeader = new VisualElement();
            groupHeader.style.flexDirection = FlexDirection.Row;
            groupHeader.style.alignItems = Align.Center;
            groupHeader.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
            groupHeader.style.paddingTop = 4;
            groupHeader.style.paddingBottom = 4;
            groupHeader.style.paddingLeft = 8;
            groupHeader.style.paddingRight = 8;

            var nameField = new TextField { value = group.Group, tooltip = "分组名（剧本中用 角色ID#分组#表情 引用，重命名会影响已有剧本引用）" };
            nameField.style.width = 160;
            nameField.style.fontSize = 12;
            nameField.style.flexShrink = 0;
            nameField.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameField.RegisterValueChangedCallback(evt =>
            {
                string newName = evt.newValue.Trim();
                if (string.IsNullOrEmpty(newName)) return;
                group.Group = newName;
                EditorUtility.SetDirty(profile);
            });
            groupHeader.Add(nameField);

            var countLabel = new Label($"{group.Sprites?.Count ?? 0} 项")
            {
                style = { color = GalleryTheme.Hex(GalleryTheme.TextMuted), fontSize = 10, marginLeft = 8, flexGrow = 1 }
            };
            groupHeader.Add(countLabel);

            var addBtn = new Button(() =>
            {
                group.Sprites = group.Sprites ?? new List<ElementSprite>();
                group.Sprites.Add(new ElementSprite());
                EditorUtility.SetDirty(profile);
                Rebuild();
            }) { text = "+ 表情" };
            GalleryStyles.ApplyButton(addBtn, GalleryTheme.AccentDim, true);
            addBtn.style.fontSize = 10;
            addBtn.style.width = 60;
            addBtn.style.flexShrink = 0;
            groupHeader.Add(addBtn);

            var delGroupBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("删除分组",
                    $"确定删除分组 '{group.Group}' 及其 {group.Sprites?.Count ?? 0} 个条目吗？\n（剧本中对该分组的引用将失效）", "删除", "取消"))
                {
                    groups.Remove(group);
                    EditorUtility.SetDirty(profile);
                    Rebuild();
                }
            }) { text = "删除组" };
            GalleryStyles.ApplyButton(delGroupBtn, GalleryTheme.Danger, false);
            delGroupBtn.style.fontSize = 10;
            delGroupBtn.style.width = 60;
            delGroupBtn.style.flexShrink = 0;
            groupHeader.Add(delGroupBtn);

            section.Add(groupHeader);

            // ---- 组内表情列表 ----
            group.Sprites = group.Sprites ?? new List<ElementSprite>();
            var listView = CreateStyledListView(group.Sprites, profile);
            section.Add(listView);

            container.Add(section);
        }

        if (groups.Count == 0)
        {
            var empty = new Label("暂无分组，点击上方「+ 添加」或拖拽图片创建分组");
            empty.style.height = 48;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
            empty.style.fontSize = 11;
            container.Add(empty);
        }
    }

    private VisualElement CreateListHeader(string title, System.Action onAdd, string hint = "可拖拽图片到此批量导入")
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
        header.style.paddingTop = 5;
        header.style.paddingBottom = 5;
        header.style.paddingLeft = 8;
        header.style.paddingRight = 8;
        header.style.borderTopWidth = 1;
        header.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
        header.style.borderTopLeftRadius = 4;
        header.style.borderTopRightRadius = 4;

        header.Add(new Label(title)
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                color = GalleryTheme.Hex(GalleryTheme.TextPrimary),
                fontSize = 12
            }
        });

        var hintLabel = new Label(hint)
        {
            style =
            {
                color = GalleryTheme.Hex(GalleryTheme.TextMuted),
                fontSize = 10,
                marginLeft = 8,
                flexGrow = 1
            }
        };
        header.Add(hintLabel);

        var addBtn = new Button(onAdd) { text = "+ 添加" };
        GalleryStyles.ApplyButton(addBtn, GalleryTheme.AccentDim, true);
        addBtn.style.flexShrink = 0;
        addBtn.style.fontSize = 10;
        addBtn.style.width = 56;
        header.Add(addBtn);

        return header;
    }

    private ListView CreateStyledListView(List<ElementSprite> sourceList, CharacterProfile profile)
    {
        var listView = new ListView();
        listView.style.flexGrow = 1;
        listView.style.minHeight = 120;
        listView.style.borderBottomWidth = 1;
        listView.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        listView.style.borderLeftWidth = 1;
        listView.style.borderLeftColor = GalleryTheme.Hex(GalleryTheme.Border);
        listView.style.borderRightWidth = 1;
        listView.style.borderRightColor = GalleryTheme.Hex(GalleryTheme.Border);
        listView.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        listView.fixedItemHeight = 32;
        listView.itemsSource = sourceList;
        listView.makeItem = () => CreateListItem();
        listView.bindItem = (e, i) => BindListItem(e, i, sourceList, profile, listView);
        listView.unbindItem = (e, i) => UnbindListItem(e);

        listView.reorderable = true;
        listView.reorderMode = ListViewReorderMode.Animated;
        listView.itemIndexChanged += (_, _) => EditorUtility.SetDirty(profile);

        listView.selectionChanged += (items) =>
        {
            foreach (var item in items)
            {
                if (item is ElementSprite data)
                {
                    UpdatePreview(data.Sprite);
                    break;
                }
            }
        };

        return listView;
    }

    // =========================================================
    //                      ListView Item
    // =========================================================
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
        container.style.paddingRight = 30;
        container.style.height = 32;
        container.style.borderBottomWidth = 1;
        container.style.borderBottomColor = new Color(0, 0, 0, 0.15f);

        var nameField = new TextField()
        {
            name = "Name",
            style = { width = 100, marginRight = 5, flexShrink = 0, fontSize = 11 }
        };
        nameField.style.borderTopWidth = 0;
        nameField.style.borderBottomWidth = 0;
        nameField.style.borderLeftWidth = 0;
        nameField.style.borderRightWidth = 0;
        nameField.style.backgroundColor = Color.clear;

        var spriteField = new ObjectField()
        {
            name = "Sprite",
            objectType = typeof(Sprite),
            style = { flexGrow = 1, flexShrink = 1, minWidth = 80, fontSize = 11 }
        };

        var delBtn = new Button() { text = "X", name = "Delete" };
        delBtn.style.position = Position.Absolute;
        delBtn.style.right = 4;
        delBtn.style.top = 4;
        delBtn.style.bottom = 4;
        delBtn.style.width = 24;
        delBtn.style.backgroundColor = GalleryTheme.Transparent_Color;
        delBtn.style.color = GalleryTheme.Hex(GalleryTheme.Danger);
        delBtn.style.fontSize = 11;

        container.Add(nameField);
        container.Add(spriteField);
        container.Add(delBtn);

        // 右键菜单（在 bind 时通过 userData 获取当前条目）
        container.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            if (container.userData is ItemContext ctx && ctx.data != null)
            {
                evt.menu.AppendAction("复制此项", _ =>
                {
                    int idx = ctx.list.IndexOf(ctx.data);
                    if (idx < 0) idx = ctx.list.Count - 1;
                    ctx.list.Insert(idx + 1, new ElementSprite { Element = ctx.data.Element + "_copy", Sprite = ctx.data.Sprite });
                    EditorUtility.SetDirty(ctx.profile);
                    ctx.listView.Rebuild();
                });
                evt.menu.AppendAction("删除此项", _ =>
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

        UnbindListItem(element);

        nameField.SetValueWithoutNotify(data.Element);
        spriteField.SetValueWithoutNotify(data.Sprite);

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

        System.Action delAction = () =>
        {
            list.Remove(data);
            EditorUtility.SetDirty(profile);
            listView.Rebuild();
            UpdatePreview(null);
        };
        delBtn.clicked += delAction;
        delBtn.userData = delAction;

        element.userData = new ItemContext
        {
            data = data,
            list = list,
            profile = profile,
            listView = listView,
            focusCb = onFocus
        };
    }

    // =========================================================
    //                      预览图
    // =========================================================
    private void UpdatePreview(Sprite sprite)
    {
        if (previewImage == null) return;
        previewImage.sprite = sprite;

        var hint = contentPane?.Q<Label>("previewHint");
        if (hint != null)
            hint.style.display = sprite != null ? DisplayStyle.None : DisplayStyle.Flex;
    }

    // =========================================================
    //                      拖放导入
    // =========================================================
    private void RegisterDropHandlers(VisualElement dropZone, List<ElementSpriteGroup> targetGroups,
        CharacterProfile profile, string kindLabel, System.Action onRebuild)
    {
        var originalBg = dropZone.resolvedStyle.backgroundColor;

        dropZone.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            if (DragAndDrop.objectReferences.Any(o => o is Sprite || o is Texture2D))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                dropZone.style.backgroundColor = new Color(0.12f, 0.32f, 0.12f, 0.4f);
            }
        });

        dropZone.RegisterCallback<DragLeaveEvent>(_ =>
            dropZone.style.backgroundColor = Color.clear);
        dropZone.RegisterCallback<DragExitedEvent>(_ =>
            dropZone.style.backgroundColor = Color.clear);

        dropZone.RegisterCallback<DragPerformEvent>(evt =>
        {
            dropZone.style.backgroundColor = Color.clear;
            var sprites = CollectDroppedSprites();
            if (sprites.Count == 0) return;

            DragAndDrop.AcceptDrag();
            ShowBatchImportPanel(sprites, targetGroups, profile, kindLabel, onRebuild);
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

    private static ElementSprite FindInGroup(List<ElementSpriteGroup> groups, string groupName, string elementName)
    {
        foreach (var g in groups)
        {
            if (g == null || g.Group != groupName) continue;
            var hit = g.Sprites?.FirstOrDefault(e => e != null && e.Element == elementName);
            if (hit != null) return hit;
        }
        return null;
    }

    // 从文件名解析分组名与情绪名：
    // Amy_uniform_Smile → (uniform, Smile)；Amy_Smile / Amy → (Default, Smile / Amy)
    private void ParseGroupedElementName(string spriteName, string charId, out string group, out string element)
    {
        group = CharacterProfile.DefaultGroupName;
        element = spriteName;

        string n = spriteName;
        if (!string.IsNullOrEmpty(charId) && n.StartsWith(charId + "_", StringComparison.OrdinalIgnoreCase))
        {
            n = n.Substring(charId.Length + 1);
        }

        int idx = n.IndexOf('_');
        if (idx > 0 && idx < n.Length - 1)
        {
            group = n.Substring(0, idx);
            element = n.Substring(idx + 1);
        }
        else
        {
            element = n;
        }
    }

    // =========================================================
    //                      批量导入面板
    // =========================================================
    private class ImportRow
    {
        public Sprite sprite;
        public TextField groupField;
        public TextField nameField;
        public Label statusLabel;
        public VisualElement row;
    }

    private void ShowBatchImportPanel(List<Sprite> sprites, List<ElementSpriteGroup> targetGroups,
        CharacterProfile profile, string kindLabel, System.Action onRebuild)
    {
        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0;
        overlay.style.right = 0;
        overlay.style.top = 0;
        overlay.style.bottom = 0;
        overlay.style.backgroundColor = new Color(0, 0, 0, 0.6f);
        overlay.style.justifyContent = Justify.Center;
        overlay.style.alignItems = Align.Center;
        overlay.name = "batchImportOverlay";

        var box = GalleryStyles.MakeCard();
        box.style.width = 540;
        box.style.maxHeight = 500;
        box.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);

        var titleLabel = new Label($"批量导入{kindLabel}（{sprites.Count} 张）")
        {
            style =
            {
                fontSize = 15,
                unityFontStyleAndWeight = FontStyle.Bold,
                color = GalleryTheme.Hex(GalleryTheme.TextPrimary),
                marginBottom = 4
            }
        };
        box.Add(titleLabel);

        box.Add(new Label("已按文件名自动解析分组与表情名（角色ID_分组_表情），可修改后再导入。同分组同名条目将被覆盖。")
        {
            style =
            {
                color = GalleryTheme.Hex(GalleryTheme.TextSecondary),
                fontSize = 11,
                marginBottom = 8,
                whiteSpace = WhiteSpace.Normal
            }
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
            string groupName = r.groupField.value.Trim();
            if (string.IsNullOrEmpty(name))
            {
                r.statusLabel.text = "名称为空";
                r.statusLabel.style.color = GalleryTheme.Hex(GalleryTheme.Danger);
            }
            else if (FindInGroup(targetGroups, groupName, name) != null)
            {
                r.statusLabel.text = "覆盖同名";
                r.statusLabel.style.color = GalleryTheme.Hex(GalleryTheme.Warning);
            }
            else
            {
                r.statusLabel.text = "新增";
                r.statusLabel.style.color = GalleryTheme.Hex(GalleryTheme.Success);
            }
        };

        foreach (var sprite in sprites)
        {
            var r = new ImportRow { sprite = sprite };

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            row.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;

            var thumb = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit };
            thumb.style.width = 36;
            thumb.style.height = 36;
            thumb.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            thumb.style.marginRight = 8;
            thumb.style.flexShrink = 0;
            row.Add(thumb);

            ParseGroupedElementName(sprite.name, profile.CharacterID, out string parsedGroup, out string parsedElement);

            var groupField = new TextField { value = parsedGroup, tooltip = "分组名" };
            groupField.style.width = 110;
            groupField.style.marginRight = 6;
            groupField.style.flexShrink = 0;
            row.Add(groupField);

            var nameField = new TextField { value = parsedElement, tooltip = "表情名" };
            nameField.style.flexGrow = 1;
            nameField.style.marginRight = 8;
            row.Add(nameField);

            var statusLabel = new Label
            {
                style =
                {
                    width = 60,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    flexShrink = 0
                }
            };
            row.Add(statusLabel);

            var removeBtn = new Button(() =>
            {
                rows.Remove(r);
                r.row.RemoveFromHierarchy();
            }) { text = "\u00D7" };
            removeBtn.style.width = 24;
            removeBtn.style.marginLeft = 6;
            removeBtn.style.flexShrink = 0;
            removeBtn.style.backgroundColor = GalleryTheme.Transparent_Color;
            removeBtn.style.color = GalleryTheme.Hex(GalleryTheme.Danger);
            row.Add(removeBtn);

            r.groupField = groupField;
            r.nameField = nameField;
            r.statusLabel = statusLabel;
            r.row = row;
            rows.Add(r);

            nameField.RegisterValueChangedCallback(_ => updateStatus(r));
            groupField.RegisterValueChangedCallback(_ => updateStatus(r));
            updateStatus(r);

            rowsScroll.Add(row);
        }

        // 底部按钮
        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.justifyContent = Justify.FlexEnd;
        btnRow.style.marginTop = 10;

        var cancelBtn = new Button(() => overlay.RemoveFromHierarchy()) { text = "取消" };
        GalleryStyles.ApplyButton(cancelBtn, GalleryTheme.BgCard, false);
        cancelBtn.style.width = 80;
        cancelBtn.style.marginRight = 8;
        btnRow.Add(cancelBtn);

        var applyBtn = new Button(() =>
        {
            int count = 0;
            foreach (var row in rows.ToList())
            {
                string name = row.nameField.value.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                string groupName = row.groupField.value.Trim();
                if (string.IsNullOrEmpty(groupName)) groupName = CharacterProfile.DefaultGroupName;

                var targetGroup = CharacterProfile.GetOrAddGroup(targetGroups, groupName);
                targetGroup.Sprites = targetGroup.Sprites ?? new List<ElementSprite>();

                var existing = targetGroup.Sprites.FirstOrDefault(e => e != null && e.Element == name);
                if (existing != null)
                {
                    existing.Sprite = row.sprite;
                }
                else
                {
                    targetGroup.Sprites.Add(new ElementSprite { Element = name, Sprite = row.sprite });
                }
                count++;
            }

            if (count > 0)
            {
                EditorUtility.SetDirty(profile);
                onRebuild?.Invoke();
                // 刷新卡片封面
                presenter.OnDataChanged?.Invoke();
            }
            overlay.RemoveFromHierarchy();
        }) { text = "导入" };
        GalleryStyles.ApplyButton(applyBtn, GalleryTheme.Success, true);
        applyBtn.style.width = 80;
        btnRow.Add(applyBtn);

        box.Add(btnRow);
        overlay.Add(box);

        // 点击遮罩空白处关闭
        overlay.RegisterCallback<ClickEvent>(evt =>
        {
            if (evt.target == overlay) overlay.RemoveFromHierarchy();
        });

        // 挂载到 this（DetailPanelView），确保 Rebuild 时能清理
        Add(overlay);
        overlay.BringToFront();
    }

    // =========================================================
    //                      辅助
    // =========================================================
    private Label CreateCardSectionLabel(string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        label.style.marginBottom = 8;
        label.style.marginTop = 2;
        label.style.fontSize = 11;
        return label;
    }
}
