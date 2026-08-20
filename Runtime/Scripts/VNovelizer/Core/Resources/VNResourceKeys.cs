/// <summary>
/// 资源键约定（运行时与编辑器注册器共用的"合同"）。
///
/// 统一键空间（Phase 0/1 资源重构，见 Docs/VNResourceProviderRefactoring.md）：
///   资源键 = 旧 Resources 相对路径 = Addressables 地址
///   例："VNovelizerRes/Backgrounds/Beach"（无扩展名）
///
/// - ResourcesProvider：按 Assets/Resources/{键} 查找（旧行为，存量项目零迁移）；
/// - AddressablesProvider：按同名地址查找（由编辑器端 VNAddressablesRegistrar 注册资产时写入）；
/// - 批量加载（LoadAll）按类别 Label 检索，Label 由 <see cref="CategoryToLabel"/> 从类别路径派生。
/// </summary>
public static class VNResourceKeys
{
    /// <summary>VNovelizer 专用 Addressables 组名（所有由 VNovelizer 注册的资产所在组）。
    /// 组内条目的地址与 Label 由注册器托管，请勿在 Addressables Groups 窗口手动编辑（组设置除外）。</summary>
    public const string GroupName = "VNovelizer";

    /// <summary>资源键根前缀（与旧 Resources 目录名保持一致，保证键空间不变）</summary>
    public const string RootPrefix = "VNovelizerRes";

    /// <summary>类别路径 → Addressables Label。
    /// Addressables Label 为任意字符串，这里统一把路径分隔符转为 '_' 以规避潜在的特殊字符问题。
    /// 例："VNovelizerRes/Characters" → "VNovelizerRes_Characters"。
    /// 编辑器注册器写入 Label 与运行时 LoadAll 检索都使用本函数，保证两侧一致。</summary>
    public static string CategoryToLabel(string category)
    {
        if (string.IsNullOrEmpty(category)) return category;
        return category.Replace('/', '_').Replace('\\', '_');
    }

    /// <summary>资源键 → 所属类别（去掉最后一段资源名）。
    /// 例："VNovelizerRes/Characters/Amy" → "VNovelizerRes/Characters"。</summary>
    public static string KeyToCategory(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        int idx = key.LastIndexOf('/');
        return idx > 0 ? key.Substring(0, idx) : key;
    }
}
