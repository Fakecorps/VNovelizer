using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Filters
{
    /// <summary>
    /// 滤镜注册表访问层（2026-10-23 VFX 注册表扩展重构）。
    ///
    /// <para>
    /// <b>唯一真源 = VNProjectConfig.VFXFilterRegistry</b>（Project Settings → VNovelizer → VFX 手动维护）：
    /// 拖入 Shader 的条目才可用；条目删除/禁用后，filter_xxx / filtercustom / unsetfilter 一律警告"未注册"。
    /// 内置 7 款滤镜由编辑器预填进注册表（可禁用/可删）；零配置场景（未创建持久配置资产，
    /// 临时实例）由 <see cref="VNProjectConfig.PrefillBuiltinVfxRegistry"/> 预填空引用条目，
    /// shader 经本类内置名称表兜底解析（Shader.Find + Resources.Load 双兜底）。
    /// 旧的代码注册（BuiltinFilters）与 VNFilterDefinition 资产自动扫描均已废弃——
    /// VNFilterDefinition 类保留仅为防旧资产 Missing Script，不再被扫描。
    /// </para>
    /// </summary>
    public class VNFilterLibrary : BaseManager<VNFilterLibrary>
    {
        /// <summary>滤镜预设类别键（工作区类别锚定仍用；注册表条目不依赖此路径）</summary>
        public const string DefaultFilterResPath = "VNovelizerRes/VFX/Filters";

        /// <summary>
        /// 内置滤镜静态元信息：编辑器预填（VNVfxRegistryPrefiller）、零配置临时实例预填、
        /// shader 兜底解析与编辑器无配置候选共用这一张表。仅作兜底，注册表才是权威入口。
        /// </summary>
        public sealed class VNBuiltinFilterInfo
        {
            public string Id;
            public string DisplayName;
            public float DefaultDuration;
            public string ShaderName;          // Shader.Find 名称（如 "VNovelizer/Filters/Fisheye"）
            public string ShaderResourcePath;  // Resources.Load 兜底路径（Resources/FilterShaders/...）
        }

        /// <summary>内置 7 款滤镜元信息（shader 兜底解析 + 编辑器预填唯一数据源）</summary>
        private static readonly VNBuiltinFilterInfo[] BuiltinFilters =
        {
            new VNBuiltinFilterInfo
            {
                Id = "fisheye", DisplayName = "鱼眼", DefaultDuration = 0.5f,
                ShaderName = "VNovelizer/Filters/Fisheye", ShaderResourcePath = "FilterShaders/VNFilterFisheye",
            },
            new VNBuiltinFilterInfo
            {
                Id = "vhs", DisplayName = "VHS 模拟", DefaultDuration = 0.5f,
                ShaderName = "VNovelizer/Filters/VHS", ShaderResourcePath = "FilterShaders/VNFilterVHS",
            },
            new VNBuiltinFilterInfo
            {
                Id = "invert", DisplayName = "反色", DefaultDuration = 0.5f,
                ShaderName = "VNovelizer/Filters/Invert", ShaderResourcePath = "FilterShaders/VNFilterInvert",
            },
            new VNBuiltinFilterInfo
            {
                Id = "grayscale", DisplayName = "黑白", DefaultDuration = 0.5f,
                ShaderName = "VNovelizer/Filters/Grayscale", ShaderResourcePath = "FilterShaders/VNFilterGrayscale",
            },
            new VNBuiltinFilterInfo
            {
                Id = "vignette", DisplayName = "暗角", DefaultDuration = 0.5f,
                ShaderName = "VNovelizer/Filters/Vignette", ShaderResourcePath = "FilterShaders/VNFilterVignette",
            },
            new VNBuiltinFilterInfo
            {
                Id = "pixelate", DisplayName = "像素化", DefaultDuration = 0.5f,
                ShaderName = "VNovelizer/Filters/Pixelate", ShaderResourcePath = "FilterShaders/VNFilterPixelate",
            },
            new VNBuiltinFilterInfo
            {
                Id = "chromatic", DisplayName = "色差", DefaultDuration = 0.5f,
                ShaderName = "VNovelizer/Filters/Chromatic", ShaderResourcePath = "FilterShaders/VNFilterChromatic",
            },
        };

        private readonly Dictionary<string, Shader> _shaderCache =
            new Dictionary<string, Shader>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>内置滤镜元信息副本（预填/候选共用，纯静态读取不触碰运行时资源链）</summary>
        public static List<VNBuiltinFilterInfo> GetBuiltinFilterInfos()
        {
            return new List<VNBuiltinFilterInfo>(BuiltinFilters);
        }

        /// <summary>
        /// 内置滤镜 ID 列表（纯静态读取，不触发运行时资源链）。
        /// 编辑器候选在无持久配置（注册表为空）时用它兜底；正常路径候选来自注册表。
        /// </summary>
        public static List<string> GetBuiltinFilterIds()
        {
            var ids = new List<string>();
            foreach (var builtin in BuiltinFilters)
                ids.Add(builtin.Id);
            return ids;
        }

        /// <summary>内置滤镜显示名（编辑器预填用；未知返回 id 本身）</summary>
        public static string GetBuiltinDisplayName(string filterId)
        {
            foreach (var builtin in BuiltinFilters)
                if (string.Equals(builtin.Id, filterId, System.StringComparison.OrdinalIgnoreCase))
                    return builtin.DisplayName;
            return filterId;
        }

        /// <summary>
        /// 注册表条目查询（大小写不敏感；仅返回启用条目）。
        /// null = 未注册 / 已禁用 / 注册表为空——调用方据此警告"未注册"。
        /// </summary>
        public VNProjectFilterEntry GetEntry(string filterId)
        {
            if (string.IsNullOrEmpty(filterId)) return null;
            var config = VNProjectConfig.Instance;
            return config != null ? config.GetFilterEntry(filterId) : null;
        }

        /// <summary>
        /// 全部可用滤镜 ID（警告/诊断信息用）。零配置（临时实例）经预填保证非空；
        /// 持久配置注册表为空（从未打开 VFX 设置页自愈）时回退内置清单，警告信息仍有意义。
        /// </summary>
        public List<string> GetAllIds()
        {
            var ids = new List<string>();
            var config = VNProjectConfig.Instance;
            if (config != null && config.VFXFilterRegistry != null)
            {
                foreach (var e in config.VFXFilterRegistry)
                    if (e != null && e.Enabled && !string.IsNullOrEmpty(e.Id) && !ids.Contains(e.Id))
                        ids.Add(e.Id);
            }
            if (ids.Count == 0)
            {
                foreach (var builtin in BuiltinFilters)
                    if (!ids.Contains(builtin.Id)) ids.Add(builtin.Id);
            }
            return ids;
        }

        /// <summary>注册表默认渐变时长（条目未注册/注册表为空时回退 0.5）</summary>
        public float GetDefaultDuration(string filterId)
        {
            var entry = GetEntry(filterId);
            return entry != null ? Mathf.Max(entry.DefaultDuration, 0f) : 0.5f;
        }

        /// <summary>
        /// 解析滤镜 shader（按 filterId 缓存）：
        /// 注册表条目直接引用其 Shader → 零配置临时实例条目 Shader 为空时经内置名称表
        /// Shader.Find + Resources.Load 双兜底（照 BGTransition 惯例）。
        /// 解析失败返回 null（调用方警告并跳过该滤镜）。
        /// </summary>
        public Shader ResolveShader(string filterId)
        {
            if (string.IsNullOrEmpty(filterId)) return null;
            string id = filterId.Trim();

            if (_shaderCache.TryGetValue(id, out var cached) && cached != null) return cached;

            Shader resolved = null;
            var entry = GetEntry(id);
            if (entry != null && entry.Shader != null)
            {
                resolved = entry.Shader; // 注册表条目直接引用（含用户自定义 shader）
            }
            else
            {
                // 内置兜底：零配置临时实例条目 Shader 为空时按内置名称表解析
                foreach (var builtin in BuiltinFilters)
                {
                    if (!string.Equals(builtin.Id, id, System.StringComparison.OrdinalIgnoreCase)) continue;
                    resolved = Shader.Find(builtin.ShaderName);
                    if (resolved == null)
                        resolved = Resources.Load<Shader>(builtin.ShaderResourcePath);
                    break;
                }
            }

            if (resolved != null) _shaderCache[id] = resolved;
            return resolved;
        }
    }
}
