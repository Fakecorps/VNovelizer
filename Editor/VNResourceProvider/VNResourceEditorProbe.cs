#if VN_ADDRESSABLES
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;

/// <summary>
/// 编辑器可用性探针安装器：向运行时程序集的 AddressablesProvider 注入委托，
/// 用于在编辑器内判断是否启用 Addressables 提供者
/// （Addressables 设置资产与 "VNovelizer" 组均存在才启用，
///   避免对未初始化 Addressables 的项目逐次探查产生 InvalidKeyException 日志）。
///
/// 运行时程序集不能引用 Unity.Addressables.Editor，故经此委托桥接；
/// 探针每次调用实时评估，结果由提供者实例缓存（注册器注册后经
/// VNResourceService.Reset() 重建链，重新评估）。
/// </summary>
[InitializeOnLoad]
internal static class VNResourceEditorProbe
{
    static VNResourceEditorProbe()
    {
        AddressablesProvider.EditorAvailabilityProbe = CheckVNovelizerGroup;
    }

    private static bool CheckVNovelizerGroup()
    {
        try
        {
            // Settings 属性为纯加载（不创建资产文件）：未初始化 Addressables 的项目返回 null
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings != null
                && settings.FindGroup(VNResourceKeys.GroupName) != null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
#endif
