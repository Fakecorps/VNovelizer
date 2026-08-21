using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

/// <summary>
/// 资产自动登记（两条路径，覆盖"Assets 任意位置创建"工作流）：
/// 1. 工作区（Assets/VNovelizer/**）新增/移入/改名 → 延迟全量重扫（SyncWorkspace，
///    类别锚定地址，文件夹单复数/大小写差异不影响寻址）；
/// 2. 全 Assets 范围内的"引擎已知类型"资产 → 按运行时查询键逐条自动登记
///    （<see cref="VNAddressablesRegistrar.TryAutoRegister"/>：已在任何组中的资产不动、
///    地址冲突时警告跳过，因此对用户手动组织完全无破坏）：
///    - FlagRegistry → 固定键 <see cref="FlagService.DefaultRegistryPath"/>；
///    - 画廊数据容器（CG/Music/Scene）→ 固定键（VNUIPrefabKeys.*DataContainer）；
///    - CharacterProfile → CharacterResPath/角色ID；
///    - 配置 CSV 输出目录下的 .csv → VNScriptResPath/文件名。
///
/// 物理位置与运行时寻址完全解耦：资产只要被登记（此处自动 / 编辑器窗口创建 /
/// 资源管理器拖放分配），放在 Assets 任意位置均可被运行时找到。
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
        // 工作区前缀之外又非候选类型的导入不触发任何检查（廉价前置过滤）
        bool workspaceTouched = (importedAssets != null && importedAssets.Any(p => IsWorkspaceAsset(p)))
                             || (movedAssets != null && movedAssets.Any(p => IsWorkspaceAsset(p)));

        // 工作区外已知类型候选（工作区内资产由全量重扫统一处理，无需逐条登记）
        var candidates = new List<string>();
        CollectCandidates(importedAssets, candidates);
        CollectCandidates(movedAssets, candidates);

        if (!workspaceTouched && candidates.Count == 0) return;
        if (!VNAddressablesRegistrar.HasAddressablesData()) return;

        if (candidates.Count > 0)
        {
            // 延迟到导入管线结束后执行（避免在导入回调内修改设置引发重入问题）
            EditorApplication.delayCall += () => AutoRegisterCandidates(candidates);
        }
        if (workspaceTouched)
        {
            EditorApplication.delayCall += VNAddressablesRegistrar.SyncWorkspace;
        }
    }

    private static bool IsWorkspaceAsset(string assetPath)
    {
        return assetPath != null
            && assetPath.StartsWith(VNProjectPaths.WorkspaceRoot + "/", System.StringComparison.Ordinal);
    }

    /// <summary>筛选工作区外、可能属于引擎已知类型的资产（.csv / .asset），收进候选列表</summary>
    private static void CollectCandidates(string[] paths, List<string> sink)
    {
        if (paths == null) return;
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p) || IsWorkspaceAsset(p)) continue;
            if (!p.StartsWith("Assets/", System.StringComparison.Ordinal)) continue;
            string ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext == ".csv" || ext == ".asset") sink.Add(p);
        }
    }

    /// <summary>按类型/来源目录推导运行时查询键并逐条登记（延迟回调时资产可能已被再次移动或删除，逐条容错）</summary>
    private static void AutoRegisterCandidates(List<string> paths)
    {
        string csvOutputFolder = null;
        string scriptCategory = null;
        string characterCategory = null;
        if (VNProjectConfig.TryGetInstance(out var config) && config != null)
        {
            csvOutputFolder = config.GetCsvOutputPath(); // 空串 = 未配置
            scriptCategory = config.VNScriptResPath;
            characterCategory = config.CharacterResPath;
        }
        if (string.IsNullOrEmpty(scriptCategory)) scriptCategory = "VNovelizerRes/VNScripts";
        if (string.IsNullOrEmpty(characterCategory)) characterCategory = "VNovelizerRes/Characters";

        foreach (var path in paths)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".csv")
            {
                // 只登记"配置 CSV 输出目录"下的剧本 CSV（其他位置的 csv 与引擎无关）
                if (!string.IsNullOrEmpty(csvOutputFolder)
                    && path.StartsWith(csvOutputFolder + "/", System.StringComparison.Ordinal))
                {
                    VNAddressablesRegistrar.TryAutoRegister(path,
                        scriptCategory + "/" + Path.GetFileNameWithoutExtension(path));
                }
                continue;
            }

            var main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main == null) continue;

            if (main is FlagRegistry)
            {
                VNAddressablesRegistrar.TryAutoRegister(path, FlagService.DefaultRegistryPath);
            }
            else if (main is CGDataContainer)
            {
                VNAddressablesRegistrar.TryAutoRegister(path, VNUIPrefabKeys.CGDataContainer);
            }
            else if (main is MusicDataContainer)
            {
                VNAddressablesRegistrar.TryAutoRegister(path, VNUIPrefabKeys.MusicDataContainer);
            }
            else if (main is SceneDataContainer)
            {
                VNAddressablesRegistrar.TryAutoRegister(path, VNUIPrefabKeys.SceneDataContainer);
            }
            else if (main is CharacterProfile profile)
            {
                string id = !string.IsNullOrEmpty(profile.CharacterID)
                    ? profile.CharacterID
                    : Path.GetFileNameWithoutExtension(path);
                VNAddressablesRegistrar.TryAutoRegister(path, characterCategory + "/" + id);
            }
        }
    }
}
