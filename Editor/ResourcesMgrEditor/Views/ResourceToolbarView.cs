using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 资源管理器顶部工具栏视图。
/// 包含：标题、导入按钮、搜索框（含"搜索:"标签）、视图模式、排序、设置、刷新。
/// 所有交互通过事件回调给 Presenter。
/// </summary>
public class ResourceToolbarView
{
    public VisualElement Root { get; private set; }
    public TextField SearchField { get; private set; }
    public Button GridBtn { get; private set; }
    public Button ListBtn { get; private set; }
    public Button RefreshBtn { get; private set; }

    public event Action OnImportFile;
    public event Action OnImportFolder;
    public event Action<string> OnSearchChanged;
    public event Action OnViewGrid;
    public event Action OnViewList;
    public event Action OnSortClicked;
    public event Action OnSettingsClicked;
    public event Action OnRefreshClicked;

    public ResourceToolbarView()
    {
        Root = new VisualElement();
        Build();
    }

    private void Build()
    {
        Root.style.flexDirection = FlexDirection.Row;
        Root.style.alignItems = Align.Center;
        Root.style.backgroundColor = ResourceStyles.Toolbar;
        Root.style.paddingTop = 6;
        Root.style.paddingBottom = 6;
        Root.style.paddingLeft = 10;
        Root.style.paddingRight = 10;
        Root.style.borderBottomWidth = 1;
        Root.style.borderBottomColor = ResourceStyles.CardBorder;
        Root.style.height = ResourceStyles.ToolbarHeight + 4;

        // 标题
        var title = new Label("资源管理器");
        title.style.fontSize = 14;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = ResourceStyles.TextPrimary;
        title.style.marginRight = 16;
        Root.Add(title);

        // 导入文件
        var importFileBtn = UIElementBuilder.MakeIconButton(
            UIElementBuilder.GetIcon("Import", "d_Import"), "导入文件...");
        ResourceStyles.StylePrimary(importFileBtn, ResourceStyles.AccentSuccess);
        importFileBtn.tooltip = "导入单个文件";
        importFileBtn.clicked += () => OnImportFile?.Invoke();
        Root.Add(importFileBtn);

        // 导入文件夹
        var importFolderBtn = UIElementBuilder.MakeIconButton(
            UIElementBuilder.GetIcon("FolderOpened Icon", "FolderOpened", "d_FolderOpened"), "导入文件夹...");
        ResourceStyles.StyleNormal(importFolderBtn);
        importFolderBtn.tooltip = "递归导入整个文件夹";
        importFolderBtn.clicked += () => OnImportFolder?.Invoke();
        importFolderBtn.style.marginLeft = 6;
        Root.Add(importFolderBtn);

        Root.Add(ResourceStyles.MakeSpacer());

        // 搜索框：含"搜索:"标签
        var searchContainer = new VisualElement();
        searchContainer.style.flexDirection = FlexDirection.Row;
        searchContainer.style.alignItems = Align.Center;
        searchContainer.style.width = 300;
        searchContainer.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
        ResourceStyles.SetRadius(searchContainer, ResourceStyles.ButtonRadius);
        searchContainer.style.paddingLeft = 8;
        searchContainer.style.paddingRight = 4;

        // "搜索:" 标签
        var searchLabel = new Label("搜索:");
        searchLabel.style.color = ResourceStyles.TextSecondary;
        searchLabel.style.fontSize = 11;
        searchLabel.style.marginRight = 6;
        searchContainer.Add(searchLabel);

        // 搜索图标
        var searchIcon = new Image();
        searchIcon.image = UIElementBuilder.GetIcon("Search Icon", "Search", "d_Search Icon", "Find");
        searchIcon.style.width = 14;
        searchIcon.style.height = 14;
        searchIcon.style.marginRight = 4;
        searchIcon.style.opacity = 0.7f;
        searchContainer.Add(searchIcon);

        // 搜索输入框
        SearchField = new TextField();
        SearchField.style.flexGrow = 1;
        SearchField.style.borderTopWidth = 0;
        SearchField.style.borderBottomWidth = 0;
        SearchField.style.borderLeftWidth = 0;
        SearchField.style.borderRightWidth = 0;
        SearchField.style.backgroundColor = Color.clear;
        SearchField.style.marginTop = 0;
        SearchField.style.marginBottom = 0;
        SearchField.tooltip = "搜索文件名...";
        SearchField.RegisterValueChangedCallback(evt => OnSearchChanged?.Invoke(evt.newValue));
        searchContainer.Add(SearchField);

        // 清除按钮
        var clearBtn = new Button(() => SearchField.value = "") { text = "X" };
        clearBtn.style.width = 20;
        clearBtn.style.height = 18;
        clearBtn.style.backgroundColor = Color.clear;
        clearBtn.style.color = ResourceStyles.TextSecondary;
        clearBtn.style.borderTopWidth = 0; clearBtn.style.borderBottomWidth = 0;
        clearBtn.style.borderLeftWidth = 0; clearBtn.style.borderRightWidth = 0;
        clearBtn.tooltip = "清除搜索";
        clearBtn.name = "clearBtn";
        clearBtn.RegisterCallback<MouseEnterEvent>(_ => clearBtn.style.color = ResourceStyles.TextPrimary);
        clearBtn.RegisterCallback<MouseLeaveEvent>(_ => clearBtn.style.color = ResourceStyles.TextSecondary);
        searchContainer.Add(clearBtn);

        Root.Add(searchContainer);

        Root.Add(ResourceStyles.MakeSpacer());

        // 视图模式
        GridBtn = new Button(() => OnViewGrid?.Invoke()) { text = "网" };
        ListBtn = new Button(() => OnViewList?.Invoke()) { text = "列" };
        ResourceStyles.StyleIcon(GridBtn, false);
        ResourceStyles.StyleIcon(ListBtn, false);
        GridBtn.tooltip = "网格视图";
        ListBtn.tooltip = "列表视图";
        GridBtn.style.fontSize = 13;
        ListBtn.style.fontSize = 13;
        GridBtn.style.marginRight = 2;
        Root.Add(GridBtn);
        Root.Add(ListBtn);

        // 排序
        var sortBtn = new Button(() => OnSortClicked?.Invoke()) { text = "排序" };
        ResourceStyles.StyleNormal(sortBtn);
        sortBtn.tooltip = "排序方式";
        sortBtn.style.marginLeft = 6;
        Root.Add(sortBtn);

        // 设置
        var settingsBtn = new Button(() => OnSettingsClicked?.Invoke());
        var settingsIcon = new Image();
        settingsIcon.image = UIElementBuilder.GetIcon("SettingsIcon", "Settings", "d_Settings");
        settingsIcon.style.width = 14;
        settingsIcon.style.height = 14;
        settingsBtn.Add(settingsIcon);
        ResourceStyles.StyleIcon(settingsBtn, false);
        settingsBtn.tooltip = "设置";
        settingsBtn.style.marginLeft = 6;
        Root.Add(settingsBtn);

        // 刷新
        RefreshBtn = new Button(() => OnRefreshClicked?.Invoke());
        var refreshIcon = new Image();
        refreshIcon.image = UIElementBuilder.GetIcon("Refresh", "d_Refresh");
        refreshIcon.style.width = 14;
        refreshIcon.style.height = 14;
        RefreshBtn.Add(refreshIcon);
        var refreshLabel = new Label("刷新");
        refreshLabel.style.marginLeft = 4;
        RefreshBtn.Add(refreshLabel);
        ResourceStyles.StyleNormal(RefreshBtn);
        RefreshBtn.tooltip = "刷新当前分类";
        RefreshBtn.style.marginLeft = 6;
        Root.Add(RefreshBtn);
    }

    public void SetSearchValue(string value) => SearchField.value = value;

    public void SetViewModeButtons(ViewMode mode)
    {
        ResourceStyles.StyleIcon(GridBtn, mode == ViewMode.Grid);
        ResourceStyles.StyleIcon(ListBtn, mode == ViewMode.List);
    }
}
