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
    /// 画面滤镜预设资产（历史遗留，2026-10-23 起不再被自动扫描）。
    ///
    /// <para>
    /// VFX 注册表扩展后，滤镜的唯一入口是 VNProjectConfig.VFXFilterRegistry
    /// （Project Settings → VNovelizer → VFX），本类保留仅为防止旧工程中的预设资产
    /// 出现 Missing Script（不再提供 CreateAssetMenu，也不再被 VNFilterLibrary 扫描加载）。
    /// 新建自定义滤镜请直接在 VFX 设置页的滤镜注册表拖入 Shader，经 filtercustom 命令使用。
    /// </para>
    /// </summary>
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
