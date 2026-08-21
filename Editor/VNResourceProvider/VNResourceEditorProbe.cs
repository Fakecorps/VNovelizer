#if VN_ADDRESSABLES
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;

/// <summary>
/// 编辑器探针安装器：向运行时程序集的 AddressablesProvider 注入两个委托——
/// 1. <see cref="AddressablesProvider.EditorAvailabilityProbe"/>：编辑器内是否启用提供者；
/// 2. <see cref="AddressablesProvider.LabelProbe"/>：类别 Label 是否有条目（空类别噪声抑制）。
///
/// 可用性条件（全部满足）：
/// 1. Addressables 设置资产存在（Assets/AddressableAssetsData）；
/// 2. "VNovelizer" 组存在（由初始化向导/注册器创建）；
/// 3. 【关键】Play Mode Script 为 "Use Asset Database (fastest)"（BuildScriptFastMode）。
///
/// 条件 3 的原因：同步 API 内部以 WaitForCompletion 桥接，在 "Use Existing Build"
/// 等模式下（尤其未执行过 Addressables 构建）InitializeAsync 永不完成 → 同步等待
/// 永久自旋 → 编辑器主线程冻结（卡死）。限定 Fast 模式（操作同步完成、无阻塞等待）
/// 可根除该死锁。其他 Play Mode Script 下编辑器内自动回退 Resources 提供者，行为安全。
///
/// 运行时程序集不能引用 Unity.Addressables.Editor，故经委托桥接；
/// 探针每次调用实时评估，结果由提供者实例缓存（注册器注册后经
/// VNResourceService.Reset() 重建链，重新评估）。
/// </summary>
[InitializeOnLoad]
internal static class VNResourceEditorProbe
{
    static VNResourceEditorProbe()
    {
        AddressablesProvider.EditorAvailabilityProbe = CheckVNovelizerGroup;
        AddressablesProvider.LabelProbe = CheckLabelHasEntries;
        AddressablesProvider.KeyProbe = CheckKeyRegistered;
    }

    /// <summary>
    /// 键存在性探针：任意组内是否有条目使用该地址（用户可能把 VN 资产归入自己的组，
    /// 故扫描全部组而非仅 VNovelizer 组）。直接线性扫描编辑器设置——
    /// 与运行时初始化状态无关，无"locator 预检"的自锁问题；
    /// 条目量级小、调用频率低（非逐帧热路径），线性扫描无需缓存。
    /// </summary>
    private static bool CheckKeyRegistered(string address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        try
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return false;
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry != null && entry.address == address)
                        return true;
                }
            }
            return false;
        }
        catch (Exception)
        {
            // 设置不可读：返回 false（跳过 Addressables 加载 → Resources 兜底，安全方向）
            return false;
        }
    }

    /// <summary>
    /// 类别 Label 非空探针：VNovelizer 组内是否有条目携带该 Label。
    /// 直接查编辑器设置（与运行时初始化状态无关），供运行时提供者
    /// 在 LoadAll 前抑制空类别的 InvalidKeyException 噪声。
    /// </summary>
    private static bool CheckLabelHasEntries(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        try
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return false;
            var group = settings.FindGroup(VNResourceKeys.GroupName);
            if (group == null) return false;

            foreach (var entry in group.entries)
            {
                if (entry != null && entry.labels.Contains(label))
                    return true;
            }
            return false;
        }
        catch (Exception)
        {
            // 异常时返回 false（跳过 Addressables 加载 → Resources 兜底，安全方向）
            return false;
        }
    }

    private static bool CheckVNovelizerGroup()
    {
        try
        {
            // Settings 属性为纯加载（不创建资产文件）：未初始化 Addressables 的项目返回 null
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return false;
            if (settings.FindGroup(VNResourceKeys.GroupName) == null) return false;

            // Play Mode Script 必须为 "Use Asset Database (fastest)"——其余模式下
            // 同步桥（WaitForCompletion）可能永久阻塞主线程（见类注释）。
            // 注：BuildScriptFastMode 的命名空间随 Addressables 版本变动过
            // （Settings.DataBuilders → Build.DataBuilders），此处用类型名判断消除版本依赖。
            var builder = settings.ActivePlayModeDataBuilder;
            return builder != null && builder.GetType().Name == "BuildScriptFastMode";
        }
        catch (Exception)
        {
            return false;
        }
    }
}
#endif
