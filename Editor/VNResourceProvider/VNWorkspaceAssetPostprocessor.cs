using System.Linq;
using UnityEditor;

/// <summary>
/// 工作区资产自动登记：Assets/VNovelizer 下新增/移入/改名的资产自动注册进
/// Addressables "VNovelizer" 组（延迟执行，避免在导入回调内修改设置引发重入问题）。
///
/// 仅在项目已初始化 Addressables（设置资产存在）时生效——避免用户单纯拖放文件
/// 就意外创建 Assets/AddressableAssetsData；首次批量注册由初始化向导完成。
/// 删除的资产由 Addressables 自身的资产挂钩自动清理，无需在此处理。
/// </summary>
public class VNWorkspaceAssetPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // 廉价前置过滤：工作区前缀之外的导入不触发任何检查
        bool touched = (importedAssets != null && importedAssets.Any(p => IsWorkspaceAsset(p)))
                    || (movedAssets != null && movedAssets.Any(p => IsWorkspaceAsset(p)));
        if (!touched) return;
        if (!VNAddressablesRegistrar.HasAddressablesData()) return;

        // 延迟到导入管线结束后执行（幂等的全工作区重扫，成本可接受）
        EditorApplication.delayCall += VNAddressablesRegistrar.SyncWorkspace;
    }

    private static bool IsWorkspaceAsset(string assetPath)
    {
        return assetPath != null
            && assetPath.StartsWith(VNProjectPaths.WorkspaceRoot + "/", System.StringComparison.Ordinal);
    }
}
