using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 角色类型扩展：外部程序集（如插件内 Live2D 目录 VNovelizer.L2D.Editor）把"新类型角色"
/// 接入核心角色编辑器的插件式扩展点。
///
/// 核心 VNovelizer.Editor 程序集不引用任何外部类型——本接口只出现核心自有类型；
/// 扩展在 [InitializeOnLoadMethod] 里调用 <see cref="CharacterTypeExtensionRegistry.Register"/>。
/// 未注册任何扩展时，角色编辑器行为与历史版本完全一致（新建只有普通角色）。
/// </summary>
public interface ICharacterTypeExtension
{
    /// <summary>类型显示名（新建对话框的卡片标题），如 "Live2D 角色"</summary>
    string TypeName { get; }

    /// <summary>类型说明（新建对话框卡片正文，1-2 句）</summary>
    string TypeDescription { get; }

    /// <summary>该资产是否属于本扩展负责的类型</summary>
    bool IsTypeOf(CharacterProfile profile);

    /// <summary>
    /// 创建并初始化该类型的角色资产实例（CharacterID、默认 Idle 条目等）。
    /// 不落盘、不注册地址——CreateAsset 与 Addressables 注册由核心 Presenter 统一处理。
    /// </summary>
    CharacterProfile CreateProfileAsset(string path, string characterId);

    /// <summary>
    /// 详情面板的专属区块（选中该类型角色时渲染在 Tab 区）。返回 null = 不渲染专属区块。
    /// </summary>
    VisualElement CreateDetailSection(CharacterProfile profile, Action onRebuild);

    /// <summary>是否隐藏"立绘"Tab（动态立绘无立绘分组列表；头像 Tab 保留给静态头图）</summary>
    bool HideSpriteTabs(CharacterProfile profile);

    /// <summary>预览区占位文案（MVP 无模型预览；P4 可换实例化预览）</summary>
    string GetPreviewPlaceholder(CharacterProfile profile);
}

/// <summary>角色类型扩展注册表（核心编辑器读取；扩展写入）</summary>
public static class CharacterTypeExtensionRegistry
{
    private static readonly List<ICharacterTypeExtension> _extensions = new List<ICharacterTypeExtension>();

    /// <summary>已注册扩展（只读视图；新建对话框遍历生成卡片）</summary>
    public static IReadOnlyList<ICharacterTypeExtension> All => _extensions;

    /// <summary>注册扩展（幂等，重复注册忽略）</summary>
    public static void Register(ICharacterTypeExtension extension)
    {
        if (extension != null && !_extensions.Contains(extension))
            _extensions.Add(extension);
    }

    /// <summary>查找负责该角色的扩展；普通角色或未注册返回 null</summary>
    public static ICharacterTypeExtension Find(CharacterProfile profile)
    {
        if (profile == null) return null;
        foreach (var ext in _extensions)
        {
            if (ext.IsTypeOf(profile)) return ext;
        }
        return null;
    }
}
