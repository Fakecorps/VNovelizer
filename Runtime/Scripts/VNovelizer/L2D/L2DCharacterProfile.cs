using System;
using System.Collections.Generic;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.MotionFade;
using UnityEngine;

namespace VNovelizer.L2D
{
    /// <summary>
    /// 动作类别（两层设计，见 VNLive2DCharacterDesign.md §5.3）：
    /// Idle = 垫底循环待机（层 0，PriorityIdle，永不被误杀）；
    /// Motion = 一次性动作（层 1，PriorityForce 同层替换，播完自动回 Idle）。
    /// </summary>
    public enum L2DMotionKind
    {
        Idle,
        Motion
    }

    /// <summary>表情条目：剧本 ID → SDK 表情资产（.exp3 导入产物）</summary>
    [Serializable]
    public class L2DExpressionEntry
    {
        [Tooltip("剧本里写的表情 ID（立绘列情绪串 / l2dexp 命令引用）")]
        public string ID;

        [Tooltip("SDK 导入的 .exp3 资产（必须存在于 ExpressionList 总表内）")]
        public CubismExpressionData Data;

        [Tooltip("覆写淡入秒数；-1 = 用 exp3 自带")]
        public float FadeOverride = -1f;
    }

    /// <summary>动作条目：剧本 ID → SDK 动作 clip（motions/*.anim 导入产物）</summary>
    [Serializable]
    public class L2DMotionEntry
    {
        [Tooltip("剧本里写的动作 ID（l2dmotion 命令引用）")]
        public string ID;

        [Tooltip("SDK 导入生成的 .anim（必须含 InstanceId 事件，用于匹配 fade 数据）")]
        public AnimationClip Clip;

        [Tooltip("动作类别：Idle（唯一、垫底循环）/ Motion（一次性，同层打断替换）")]
        public L2DMotionKind Kind = L2DMotionKind.Motion;

        [Tooltip("循环播放（Idle 必须勾选）")]
        public bool Loop;

        [Tooltip("覆写淡入秒数；-1 = 用 model3 默认")]
        public float FadeInOverride = -1f;

        [Tooltip("覆写淡出秒数；-1 = 用 model3 默认")]
        public float FadeOutOverride = -1f;
    }

    /// <summary>
    /// Live2D 角色配置（继承 CharacterProfile：ID/姓名框/头像框/缩放/偏移/静态头图全复用）。
    /// 剧本语义与静态立绘完全同构：立绘列情绪串 = 表情 ID；存档 appearance 字符串
    /// 沿用 CharacterID#group#emotion。运行时由 CharacterResManager 自动发现加载。
    /// </summary>
    [CreateAssetMenu(fileName = "L2DCharacterProfile", menuName = "VNovelizer/L2D角色配置")]
    public class L2DCharacterProfile : CharacterProfile
    {
        public override bool IsSpriteBased => false;

        [Header("Live2D 模型（SDK 导入产物）")]
        [Tooltip("SDK 自动生成的模型 Prefab（Animator 必须无 controller，官方 OW 要求）")]
        public GameObject ModelPrefab;

        [Tooltip("表情总表（.expressionList 资产；官方导入通常已自动赋值到 prefab，此处为单一事实源）")]
        public CubismExpressionList ExpressionList;

        [Tooltip("动作 fade 总表（.fadeMotionList 资产；clip 经 InstanceId 事件与之匹配）")]
        public CubismFadeMotionList FadeMotionList;

        [Header("表情配置（ID → 表情资产）")]
        public List<L2DExpressionEntry> Expressions = new List<L2DExpressionEntry>();

        [Header("动作配置（ID → 动作 clip）")]
        public List<L2DMotionEntry> Motions = new List<L2DMotionEntry>();

        // =========================================================
        //                      查询 API
        // =========================================================

        /// <summary>按 ID 查表情条目（大小写不敏感，未命中返回 null）</summary>
        public L2DExpressionEntry FindExpression(string expID)
        {
            if (string.IsNullOrEmpty(expID) || Expressions == null) return null;
            foreach (var e in Expressions)
            {
                if (e != null && string.Equals(e.ID, expID, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        /// <summary>按 ID 查动作条目（大小写不敏感，未命中返回 null）</summary>
        public L2DMotionEntry FindMotion(string motionID)
        {
            if (string.IsNullOrEmpty(motionID) || Motions == null) return null;
            foreach (var m in Motions)
            {
                if (m != null && string.Equals(m.ID, motionID, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        /// <summary>Idle 待机条目（配置校验保证恰好 1 个；缺失时返回 null）</summary>
        public L2DMotionEntry IdleMotion
        {
            get
            {
                if (Motions == null) return null;
                foreach (var m in Motions)
                {
                    if (m != null && m.Kind == L2DMotionKind.Idle) return m;
                }
                return null;
            }
        }

        /// <summary>表情资产在总表中的索引（表情控制器用 CurrentExpressionIndex 驱动）</summary>
        public int GetExpressionIndex(CubismExpressionData data)
        {
            if (ExpressionList == null || ExpressionList.CubismExpressionObjects == null || data == null)
                return -1;
            for (int i = 0; i < ExpressionList.CubismExpressionObjects.Length; i++)
            {
                if (ExpressionList.CubismExpressionObjects[i] == data) return i;
            }
            return -1;
        }
    }
}
