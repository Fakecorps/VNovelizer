using System;
using UnityEngine;

/// <summary>
/// 资源管理器数据模型层 - 枚举与数据类。
/// </summary>

/// <summary>资源类型</summary>
public enum ResType { Background, Video, BGM, SFX, Voice }

/// <summary>视图模式</summary>
public enum ViewMode { Grid, List }

/// <summary>排序模式</summary>
public enum SortMode { NameAsc, NameDesc, DateNewest, DateOldest }

/// <summary>
/// 单条资源的数据记录。
/// 不可变结构体，避免 UI 与数据互相影响。
/// </summary>
[Serializable]
public struct ResourceItem
{
    public UnityEngine.Object Asset;     // 资源引用（视频为 null）
    public string Name;                  // 不含扩展名的文件名
    public string LogicalName;           // 逻辑名（Excel/剧本索引名：Addressables 托管模式 = 地址尾段；文件夹模式 = 文件名）
    public string AssetPath;             // 完整 Asset 路径
    public string FullPath;              // 系统绝对路径
    public long FileSize;                // 字节
    public DateTime LastModified;        // 最后修改时间

    /// <summary>显示名：优先逻辑名（剧本作者看到什么，Excel 里就写什么）</summary>
    public string DisplayName => string.IsNullOrEmpty(LogicalName) ? Name : LogicalName;

    public string FormattedSize
    {
        get
        {
            if (FileSize < 1024) return FileSize + " B";
            if (FileSize < 1024 * 1024) return (FileSize / 1024.0).ToString("0.0") + " KB";
            if (FileSize < 1024L * 1024 * 1024) return (FileSize / (1024.0 * 1024)).ToString("0.0") + " MB";
            return (FileSize / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB";
        }
    }
}

/// <summary>分类项的 UI 绑定数据</summary>
[Serializable]
public class ResTypeItem
{
    public ResType Type;
    public string DisplayName;
    public int Count;
    public int Index;
}

/// <summary>窗口持久化状态</summary>
[Serializable]
public class ResourceWindowState
{
    public ResType currentType = ResType.Background;
    public string searchKeyword = "";
    public ViewMode viewMode = ViewMode.Grid;
    public SortMode sortMode = SortMode.NameAsc;
    public float cardSize = 120f;
    public bool showStatusBar = true;
    public string lastSelectedAssetPath = "";
}
