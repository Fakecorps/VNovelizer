using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Filters
{
    /// <summary>
    /// 滤镜参数的 shader 属性类型。
    /// </summary>
    public enum VNFilterParamType
    {
        /// <summary>float 属性（material.SetFloat）</summary>
        Float = 0,

        /// <summary>Color 属性（material.SetColor）</summary>
        Color = 1,

        /// <summary>Vector4 属性（material.SetVector）</summary>
        Vector = 2,
    }

    /// <summary>
    /// 单个滤镜参数的默认值：shader 属性名 + 类型 + 值。
    /// </summary>
    [System.Serializable]
    public class VNFilterParam
    {
        /// <summary>shader 属性名（如 _Strength / _Color）</summary>
        public string Name;

        /// <summary>属性类型</summary>
        public VNFilterParamType Type = VNFilterParamType.Float;

        public float FloatValue;
        public Color ColorValue = Color.black;
        public Vector4 VectorValue;

        /// <summary>把默认值应用到材质实例（属性缺失时 SetXxx 静默无效，安全）</summary>
        public void ApplyTo(Material material)
        {
            if (material == null || string.IsNullOrEmpty(Name)) return;
            switch (Type)
            {
                case VNFilterParamType.Color:
                    material.SetColor(Name, ColorValue);
                    break;
                case VNFilterParamType.Vector:
                    material.SetVector(Name, VectorValue);
                    break;
                default:
                    material.SetFloat(Name, FloatValue);
                    break;
            }
        }
    }

    /// <summary>
    /// 画面滤镜预设（ScreenFilterSystem 的扩展资产）。
    ///
    /// <para>
    /// 滤镜 = Shader + 参数默认值。内置 7 款由 <see cref="VNFilterLibrary"/> 代码注册，
    /// 用户可用 CreateAssetMenu 自建预设资产（新预设会经 Addressables 类别自动被加载）。
    /// 注意：2026-10-21 起命令层已移除通用 <c>setfilter</c>——内置滤镜使用逐滤镜命令
    /// （filter_vhs / filter_fisheye 等，可调参数），<c>unsetfilter</c> 的 name 下拉仍会
    /// 列出本预设供关闭。
    /// </para>
    ///
    /// <para>
    /// <b>Shader 契约（必须遵守，见 Docs/VNScreenFilterSystem.md）</b>：
    /// ① 采样输入纹理 <c>_MainTex</c>；② 实现全局混合权重 <c>_Weight</c>（0=无效果，1=满效果，
    /// 管理器每帧写入，滤镜命令的渐变/中断都依赖它）；③ 圆形效果可用 <c>_Aspect</c>
    /// （屏幕宽高比，管理器每帧写入）；④ Built-in 兼容写法（无 RenderPipeline tag）。
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "VNFilterDefinition", menuName = "VNovelizer/画面滤镜预设")]
    public class VNFilterDefinition : ScriptableObject
    {
        /// <summary>滤镜逻辑名（unsetfilter 命令引用；大小写不敏感唯一；不支持逗号/括号/空格）</summary>
        public string FilterId;

        /// <summary>显示名（编辑器/日志展示用）</summary>
        public string DisplayName;

        /// <summary>滤镜 shader（必须实现 _MainTex / _Weight 契约；留空则按内置注册表解析）</summary>
        public Shader Shader;

        /// <summary>shader 参数默认值（滤镜实例化时应用到材质）</summary>
        public List<VNFilterParam> DefaultParams = new List<VNFilterParam>();

        /// <summary>命令未显式指定 duration 时的默认渐变秒数</summary>
        public float DefaultDuration = 0.5f;

        /// <summary>把全部默认参数应用到材质实例</summary>
        public void ApplyTo(Material material)
        {
            if (DefaultParams == null) return;
            foreach (var p in DefaultParams)
                if (p != null) p.ApplyTo(material);
        }
    }
}
