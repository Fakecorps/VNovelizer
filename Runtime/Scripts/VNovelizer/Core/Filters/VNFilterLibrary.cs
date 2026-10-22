using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Filters
{
    /// <summary>
    /// 滤镜定义库：内置滤镜（代码注册，任何资源模式可用）+ 用户自定义预设资产。
    ///
    /// <para>
    /// 定义来源（按优先级合并，同名后者覆盖）：
    /// ① 内置 7 款（fisheye/vhs/invert/grayscale/vignette/pixelate/chromatic）——
    ///    代码注册为运行时 ScriptableObject 实例，shader 经 Shader.Find + Resources.Load 双兜底，
    ///    与 Addressables/Resources 模式无关，保证开箱即用；
    /// ② 用户 <see cref="VNFilterDefinition"/> 资产——经资源服务链按类别
    ///    <c>VNovelizerRes/VFX/Filters</c> 批量加载（Addressables 优先、Resources 兜底），
    ///    资产放工作区 Assets/VNovelizer/VFX/Filters 即可被自动发现。
    /// </para>
    /// </summary>
    public class VNFilterLibrary : BaseManager<VNFilterLibrary>
    {
        /// <summary>滤镜预设类别键（用户自定义预设资产所在类别）</summary>
        public const string DefaultFilterResPath = "VNovelizerRes/VFX/Filters";

        private sealed class BuiltinFilter
        {
            public string FilterId;
            public string DisplayName;
            public string ShaderName;         // Shader.Find 名称（如 "VNovelizer/Filters/Fisheye"）
            public string ShaderResourcePath; // Resources.Load 兜底路径（Resources/FilterShaders/...）
            public float DefaultDuration;
            public List<VNFilterParam> Params;
        }

        private static VNFilterParam F(string name, float value)
            => new VNFilterParam { Name = name, Type = VNFilterParamType.Float, FloatValue = value };

        private static VNFilterParam C(string name, Color value)
            => new VNFilterParam { Name = name, Type = VNFilterParamType.Color, ColorValue = value };

        /// <summary>内置滤镜注册表（单一信息源：参数默认值与 shader 属性名在此维护）</summary>
        private static readonly BuiltinFilter[] BuiltinFilters =
        {
            new BuiltinFilter
            {
                FilterId = "fisheye", DisplayName = "鱼眼",
                ShaderName = "VNovelizer/Filters/Fisheye", ShaderResourcePath = "FilterShaders/VNFilterFisheye",
                DefaultDuration = 0.5f,
                Params = new List<VNFilterParam> { F("_Strength", 0.6f) },
            },
            new BuiltinFilter
            {
                FilterId = "vhs", DisplayName = "VHS 模拟",
                ShaderName = "VNovelizer/Filters/VHS", ShaderResourcePath = "FilterShaders/VNFilterVHS",
                DefaultDuration = 0.5f,
                Params = new List<VNFilterParam> { F("_Intensity", 0.6f) },
            },
            new BuiltinFilter
            {
                FilterId = "invert", DisplayName = "反色",
                ShaderName = "VNovelizer/Filters/Invert", ShaderResourcePath = "FilterShaders/VNFilterInvert",
                DefaultDuration = 0.5f,
                Params = new List<VNFilterParam> { F("_Amount", 1f) },
            },
            new BuiltinFilter
            {
                FilterId = "grayscale", DisplayName = "黑白",
                ShaderName = "VNovelizer/Filters/Grayscale", ShaderResourcePath = "FilterShaders/VNFilterGrayscale",
                DefaultDuration = 0.5f,
                Params = new List<VNFilterParam> { F("_Amount", 1f) },
            },
            new BuiltinFilter
            {
                FilterId = "vignette", DisplayName = "暗角",
                ShaderName = "VNovelizer/Filters/Vignette", ShaderResourcePath = "FilterShaders/VNFilterVignette",
                DefaultDuration = 0.5f,
                Params = new List<VNFilterParam>
                {
                    F("_Strength", 0.8f), F("_Radius", 0.85f), F("_Softness", 0.4f),
                    C("_Color", new Color(0f, 0f, 0f, 1f)),
                },
            },
            new BuiltinFilter
            {
                FilterId = "pixelate", DisplayName = "像素化",
                ShaderName = "VNovelizer/Filters/Pixelate", ShaderResourcePath = "FilterShaders/VNFilterPixelate",
                DefaultDuration = 0.5f,
                Params = new List<VNFilterParam> { F("_PixelCount", 96f) },
            },
            new BuiltinFilter
            {
                FilterId = "chromatic", DisplayName = "色差",
                ShaderName = "VNovelizer/Filters/Chromatic", ShaderResourcePath = "FilterShaders/VNFilterChromatic",
                DefaultDuration = 0.5f,
                Params = new List<VNFilterParam> { F("_Amount", 0.012f) },
            },
        };

        private readonly Dictionary<string, VNFilterDefinition> _definitions =
            new Dictionary<string, VNFilterDefinition>(System.StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Shader> _shaderCache =
            new Dictionary<string, Shader>(System.StringComparer.OrdinalIgnoreCase);

        private bool _initialized;

        /// <summary>初始化定义库（幂等；首次查询任意 API 时自动触发）</summary>
        public void Init()
        {
            if (_initialized) return;
            _initialized = true;
            _definitions.Clear();

            // ① 内置滤镜（运行时实例，非资产：任何项目模式可用）
            foreach (var builtin in BuiltinFilters)
            {
                var def = ScriptableObject.CreateInstance<VNFilterDefinition>();
                def.name = "VNFilter_" + builtin.FilterId;
                def.FilterId = builtin.FilterId;
                def.DisplayName = builtin.DisplayName;
                def.DefaultDuration = builtin.DefaultDuration;
                def.DefaultParams = new List<VNFilterParam>(builtin.Params);
                _definitions[builtin.FilterId] = def;
            }

            // ② 用户自定义预设（同名覆盖内置）。try/catch 防御：编辑模式或
            // Addressables 初始化边界的异常不应拖垮内置滤镜（候选/运行时同理）
            string category = VNProjectConfig.Instance != null
                ? VNProjectConfig.Instance.FilterResPath
                : DefaultFilterResPath;
            IList<VNFilterDefinition> userList = null;
            try
            {
                userList = VNResourceService.LoadAll<VNFilterDefinition>(category);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VNFilterLibrary] 用户滤镜预设加载失败（内置滤镜不受影响）: {e.Message}");
            }
            if (userList != null)
            {
                foreach (var asset in userList)
                {
                    if (asset == null || string.IsNullOrEmpty(asset.FilterId)) continue;
                    string id = asset.FilterId.Trim();
                    if (string.IsNullOrEmpty(id))
                    {
                        Debug.LogWarning($"[VNFilterLibrary] 滤镜预设资产 '{asset.name}' 的 FilterId 为空，已跳过");
                        continue;
                    }
                    if (_definitions.ContainsKey(id))
                        Debug.LogWarning($"[VNFilterLibrary] 滤镜 '{id}' 存在重名定义，用户预设覆盖内置/先前定义");
                    _definitions[id] = asset;
                    _shaderCache.Remove(id); // 定义更换 → shader 缓存作废
                }
            }

            Debug.Log($"[VNFilterLibrary] 滤镜定义库就绪（{_definitions.Count} 个）: {string.Join(", ", GetAllIds())}");
        }

        /// <summary>获取滤镜定义（大小写不敏感；未知返回 null）</summary>
        public VNFilterDefinition GetDefinition(string filterId)
        {
            if (string.IsNullOrEmpty(filterId)) return null;
            Init();
            _definitions.TryGetValue(filterId.Trim(), out var def);
            return def;
        }

        /// <summary>
        /// 内置滤镜 ID 列表（纯静态读取，不触发 Init / 运行时资源链）。
        /// 编辑器候选（ParamCandidateProvider）用它 + AssetDatabase 扫描用户预设——
        /// 编辑模式触碰 Addressables/Resources 运行时链不可靠（同 GetCharacterIds 惯例）。
        /// </summary>
        public static List<string> GetBuiltinFilterIds()
        {
            var ids = new List<string>();
            foreach (var builtin in BuiltinFilters)
                ids.Add(builtin.FilterId);
            return ids;
        }

        /// <summary>全部滤镜 ID（内置在前，用户自定义在后；编辑器候选与诊断共用）</summary>
        public List<string> GetAllIds()
        {
            Init();
            var ids = new List<string>();
            foreach (var builtin in BuiltinFilters)
                if (!ids.Contains(builtin.FilterId)) ids.Add(builtin.FilterId);
            foreach (var pair in _definitions)
                if (!ids.Contains(pair.Key)) ids.Add(pair.Key);
            return ids;
        }

        /// <summary>
        /// 解析滤镜 shader（按 filterId 缓存）：
        /// 用户预设 → 直接引用其 Shader；内置 → Shader.Find + Resources.Load 双兜底（照 BGTransition 惯例）。
        /// 解析失败返回 null（调用方警告并跳过该滤镜）。
        /// </summary>
        public Shader ResolveShader(string filterId)
        {
            if (string.IsNullOrEmpty(filterId)) return null;
            Init();
            string id = filterId.Trim();

            if (_shaderCache.TryGetValue(id, out var cached) && cached != null) return cached;

            Shader resolved = null;
            if (_definitions.TryGetValue(id, out var def) && def.Shader != null)
            {
                resolved = def.Shader; // 用户预设直接引用
            }
            else
            {
                foreach (var builtin in BuiltinFilters)
                {
                    if (!string.Equals(builtin.FilterId, id, System.StringComparison.OrdinalIgnoreCase)) continue;
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
