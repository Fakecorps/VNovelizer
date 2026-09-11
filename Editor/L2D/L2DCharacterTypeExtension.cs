using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.L2D;

namespace VNovelizer.L2D.EditorTools
{
    /// <summary>
    /// Live2D 角色类型扩展：把 L2DCharacterProfile 接入核心角色编辑器
    /// （新建类型选择卡片 / 列表徽标 / 详情面板专属区块）。
    /// 核心编辑器经 ICharacterTypeExtension 抽象调用，无任何 Live2D 编译期依赖。
    /// </summary>
    [InitializeOnLoad]
    public static class L2DCharacterTypeExtensionRegistration
    {
        static L2DCharacterTypeExtensionRegistration()
        {
            CharacterTypeExtensionRegistry.Register(new L2DCharacterTypeExtension());
        }
    }

    public class L2DCharacterTypeExtension : ICharacterTypeExtension
    {
        public string TypeName => "Live2D 角色";

        public string TypeDescription =>
            "动态立绘角色：拖入 SDK 生成的模型 Prefab、表情总表与动作总表，按 ID 配置表情与动作（剧本用 角色ID_表情ID 引用，Command 列用 l2dmotion / l2dexp）。";

        public bool IsTypeOf(CharacterProfile profile)
        {
            return profile is L2DCharacterProfile;
        }

        public CharacterProfile CreateProfileAsset(string path, string characterId)
        {
            var profile = ScriptableObject.CreateInstance<L2DCharacterProfile>();
            profile.CharacterID = characterId;
            // 新建自带一个空 Idle 条目占位（配置校验要求恰好 1 个 Idle 且必须 Loop）
            profile.Motions.Add(new L2DMotionEntry
            {
                ID = "idle",
                Kind = L2DMotionKind.Idle,
                Loop = true
            });
            return profile;
        }

        public VisualElement CreateDetailSection(CharacterProfile profile, Action onRebuild)
        {
            return new L2DDetailSectionView(profile as L2DCharacterProfile, onRebuild);
        }

        public bool HideSpriteTabs(CharacterProfile profile)
        {
            return true; // L2D 无立绘分组列表；头像 Tab 保留（静态头图供对话框头像）
        }

        public string GetPreviewPlaceholder(CharacterProfile profile)
        {
            return "Live2D 模型预览开发中（P4）\n可在运行时经剧本试玩查看模型效果";
        }
    }
}
