using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.Core.Theater;

/// <summary>
/// 文字角色类型扩展：把 TextActorProfile 接入核心角色编辑器
/// （新建类型选择卡片 / 列表徽标 / 详情面板专属区块）。
/// 核心编辑器经 ICharacterTypeExtension 抽象调用，无任何专属编译期依赖。
/// </summary>
[InitializeOnLoad]
public static class TextActorTypeExtensionRegistration
{
    static TextActorTypeExtensionRegistration()
    {
        CharacterTypeExtensionRegistry.Register(new TextActorTypeExtension());
    }
}

public class TextActorTypeExtension : ICharacterTypeExtension
{
    public string TypeName => "文字角色";

    public string TypeDescription =>
        "整屏文字面板，每行正文累积显示，样式由面板 Prefab 决定。";

    public bool IsTypeOf(CharacterProfile profile)
    {
        return profile is TextActorProfile;
    }

    public CharacterProfile CreateProfileAsset(string path, string characterId)
    {
        var profile = ScriptableObject.CreateInstance<TextActorProfile>();
        profile.CharacterID = characterId;
        return profile;
    }

    public VisualElement CreateDetailSection(CharacterProfile profile, Action onRebuild)
    {
        return new TextActorDetailSectionView(profile as TextActorProfile, onRebuild);
    }

    public bool HideSpriteTabs(CharacterProfile profile)
    {
        return true; // 无立绘分组列表；头像 Tab 保留（静态头图供对话框头像）
    }

    public string GetPreviewPlaceholder(CharacterProfile profile)
    {
        return "文字角色无立绘预览\n面板外观请在面板 Prefab 中编辑（可点下方「生成内置默认面板预制体」生成副本）";
    }
}
