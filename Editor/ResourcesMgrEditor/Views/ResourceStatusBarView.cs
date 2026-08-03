using UnityEngine.UIElements;

/// <summary>
/// 资源管理器底部状态栏视图。
/// 显示选中数、总数、当前分类、视图模式、排序、当前路径。
/// </summary>
public class ResourceStatusBarView
{
    public VisualElement Root { get; private set; }
    private Label _statusLabel;
    private Label _pathLabel;

    public ResourceStatusBarView()
    {
        Root = Build();
    }

    private VisualElement Build()
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;
        bar.style.backgroundColor = ResourceStyles.StatusBar;
        bar.style.paddingTop = 4;
        bar.style.paddingBottom = 4;
        bar.style.paddingLeft = 12;
        bar.style.paddingRight = 12;
        bar.style.borderTopWidth = 1;
        bar.style.borderTopColor = ResourceStyles.CardBorder;
        bar.style.minHeight = ResourceStyles.StatusBarHeight;

        _statusLabel = new Label();
        _statusLabel.style.color = ResourceStyles.TextSecondary;
        _statusLabel.style.fontSize = 11;
        _statusLabel.style.flexGrow = 1;
        bar.Add(_statusLabel);

        _pathLabel = new Label();
        _pathLabel.name = "pathHint";
        _pathLabel.style.color = ResourceStyles.TextSecondary;
        _pathLabel.style.fontSize = 10;
        bar.Add(_pathLabel);

        return bar;
    }

    public void Update(int selectedCount, int totalCount, ResType type, ViewMode viewMode, SortMode sortMode, string path)
    {
        if (_statusLabel == null) return;
        _statusLabel.text = $"已选: {selectedCount}  |  总数: {totalCount}  |  分类: {UIElementBuilder.GetTypeDisplayName(type)}  |  视图: {(viewMode == ViewMode.Grid ? "网格" : "列表")}  |  排序: {UIElementBuilder.GetSortModeName(sortMode)}";
        if (_pathLabel != null) _pathLabel.text = "路径: " + path;
    }
}
