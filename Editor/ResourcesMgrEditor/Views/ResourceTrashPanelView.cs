using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 回收站面板视图（【子文件夹支持】配套）：条目列表 + 恢复 / 彻底删除 / 清空。
/// 由 Presenter 控制显隐（侧边栏「回收站」入口切换），复用内容区位置。
/// </summary>
public class ResourceTrashPanelView
{
    public VisualElement Root { get; private set; }

    public event Action<string> OnRestore;           // 恢复条目（Id）
    public event Action<string> OnDeletePermanent;   // 彻底删除条目（Id）
    public event Action OnClearAll;                  // 清空回收站
    public event Action OnClose;                     // 返回资源浏览

    private VisualElement _listContainer;

    public ResourceTrashPanelView()
    {
        Root = new VisualElement();
        Root.style.flexGrow = 1;
        Root.style.backgroundColor = ResourceStyles.Bg;
        Root.style.display = DisplayStyle.None;
        Build();
    }

    private void Build()
    {
        Root.style.flexDirection = FlexDirection.Column;

        // 头部：标题 + 清空 + 返回
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.height = 34;
        header.style.paddingLeft = 12;
        header.style.paddingRight = 10;
        header.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        header.style.borderBottomWidth = 1;
        header.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);

        var title = new Label("回收站");
        title.style.fontSize = 13;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = ResourceStyles.TextPrimary;
        title.style.marginRight = 12;
        header.Add(title);

        var subtitle = new Label("删除的资源移入此处（GUID 保留），可恢复或彻底删除");
        subtitle.style.fontSize = 11;
        subtitle.style.color = ResourceStyles.TextSecondary;
        header.Add(subtitle);

        header.Add(new VisualElement { style = { flexGrow = 1 } });

        var clearBtn = new Button(() => OnClearAll?.Invoke()) { text = "清空回收站" };
        GalleryStyles.ApplyButton(clearBtn, GalleryTheme.Danger, true);
        clearBtn.style.fontSize = 11;
        clearBtn.style.height = 24;
        clearBtn.style.marginRight = 8;
        clearBtn.tooltip = "彻底删除全部条目（不可恢复）";
        header.Add(clearBtn);

        var closeBtn = new Button(() => OnClose?.Invoke()) { text = "← 返回资源浏览" };
        GalleryStyles.ApplyButton(closeBtn, GalleryTheme.BgCard, false);
        closeBtn.style.fontSize = 11;
        closeBtn.style.height = 24;
        header.Add(closeBtn);

        Root.Add(header);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        scroll.style.paddingTop = 10;
        scroll.style.paddingLeft = 10;
        scroll.style.paddingRight = 10;
        _listContainer = new VisualElement();
        _listContainer.style.flexDirection = FlexDirection.Column;
        scroll.Add(_listContainer);
        Root.Add(scroll);
    }

    /// <summary>渲染条目列表</summary>
    public void Refresh(List<ResourceTrashService.TrashEntry> entries)
    {
        _listContainer.Clear();

        if (entries == null || entries.Count == 0)
        {
            var empty = new Label("回收站是空的。删除的文件/文件夹会出现在这里，可随时恢复。");
            empty.style.color = ResourceStyles.TextSecondary;
            empty.style.fontSize = 12;
            empty.style.marginTop = 30;
            empty.style.alignSelf = Align.Center;
            _listContainer.Add(empty);
            return;
        }

        foreach (var entry in entries)
        {
            _listContainer.Add(BuildEntryRow(entry));
        }
    }

    private VisualElement BuildEntryRow(ResourceTrashService.TrashEntry entry)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.minHeight = 44;
        row.style.paddingLeft = 12;
        row.style.paddingRight = 12;
        row.style.marginBottom = 4;
        row.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
        ResourceStyles.SetRadius(row, ResourceStyles.CardRadius);

        // 类型图标
        var icon = new Image();
        var tex = UIElementBuilder.GetTypeIconTexture((ResType)entry.Type) as Texture;
        if (tex != null) icon.image = tex;
        icon.style.width = 28;
        icon.style.height = 28;
        icon.style.marginRight = 10;
        icon.style.flexShrink = 0;
        icon.scaleMode = ScaleMode.ScaleToFit;
        row.Add(icon);

        // 概要信息
        var info = new VisualElement();
        info.style.flexGrow = 1;
        info.style.justifyContent = Justify.Center;

        var title = new Label(entry.Title);
        title.style.fontSize = 12;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = ResourceStyles.TextPrimary;
        info.Add(title);

        string fileSummary = entry.Files.Count <= 3
            ? string.Join("、", entry.Files.Select(f => System.IO.Path.GetFileName(f.OriginalAssetPath)))
            : string.Join("、", entry.Files.Take(3).Select(f => System.IO.Path.GetFileName(f.OriginalAssetPath))) + $" 等 {entry.Files.Count} 个";
        var detail = new Label($"{UIElementBuilder.GetTypeDisplayName((ResType)entry.Type)} · {entry.TrashedAt} · {fileSummary}");
        detail.style.fontSize = 10;
        detail.style.color = ResourceStyles.TextSecondary;
        detail.style.overflow = Overflow.Hidden;
        detail.style.textOverflow = TextOverflow.Ellipsis;
        detail.style.whiteSpace = WhiteSpace.NoWrap;
        info.Add(detail);
        row.Add(info);

        // 恢复按钮
        var restoreBtn = new Button(() => OnRestore?.Invoke(entry.Id)) { text = "恢复" };
        GalleryStyles.ApplyButton(restoreBtn, GalleryTheme.Accent, true);
        restoreBtn.style.fontSize = 11;
        restoreBtn.style.height = 24;
        restoreBtn.style.marginLeft = 8;
        restoreBtn.style.flexShrink = 0;
        restoreBtn.tooltip = "移回原位置并按原地址重新注册";
        row.Add(restoreBtn);

        // 彻底删除按钮
        var deleteBtn = new Button(() => OnDeletePermanent?.Invoke(entry.Id)) { text = "彻底删除" };
        GalleryStyles.ApplyButton(deleteBtn, GalleryTheme.Danger, false);
        deleteBtn.style.fontSize = 11;
        deleteBtn.style.height = 24;
        deleteBtn.style.marginLeft = 4;
        deleteBtn.style.flexShrink = 0;
        deleteBtn.tooltip = "从回收站永久删除（不可恢复）";
        row.Add(deleteBtn);

        return row;
    }

    public void SetVisible(bool visible)
    {
        Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
