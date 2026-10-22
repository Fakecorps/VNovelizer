using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Filters;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// <c>filter_xxx</c> 逐滤镜命令族公共基类（2026-10-21 拆分自已移除的 setfilter）。
    ///
    /// <para>
    /// 设计：每个内置滤镜一个命令（filter_vhs / filter_fisheye …），命令参数 =
    /// 该滤镜可调节的 shader 数值 + 可选 duration。参数缺省时用
    /// <see cref="VNFilterLibrary"/> 预设默认值补齐；子类只需声明
    /// <see cref="FilterId"/>、参数规格（<see cref="GetParamSpecs"/>）与
    /// <c>[VNParam]</c> 编辑器元数据，解析/执行/中断/预演语义全部共用。
    /// </para>
    ///
    /// <para>
    /// 滤镜是持久视觉状态：Execute/Simulate/Interrupt =
    /// 立即到终态（满权重 + 目标参数）；ExecuteAsync 等待淡入时长后返回（保持 &amp; 时序）。
    /// 重复开启同一滤镜 = 淡入到满权重并刷新参数（幂等）。
    /// </para>
    /// </summary>
    public abstract class FilterCommandBase : VNCommand
    {
        /// <summary>单个可调 shader 参数的规格（子类按命令参数顺序声明）</summary>
        protected sealed class FilterParamSpec
        {
            /// <summary>shader 属性名（如 _Strength / _Intensity）</summary>
            public readonly string ShaderName;

            /// <summary>缺省值（与 VNFilterLibrary 内置注册表一致）</summary>
            public readonly float DefaultValue;

            public FilterParamSpec(string shaderName, float defaultValue)
            {
                ShaderName = shaderName;
                DefaultValue = defaultValue;
            }
        }

        /// <summary>滤镜逻辑名（对应 VNFilterLibrary 内置注册表 ID）</summary>
        protected abstract string FilterId { get; }

        /// <summary>可调节的 shader 参数规格（顺序与命令参数一致；duration 不计入）</summary>
        protected abstract FilterParamSpec[] GetParamSpecs();

        // 淡入等待中的并行实例计数 + 中断时回放的目标参数快照
        // （同一滤镜命令可 & 并行多条，Interrupt 需逐条瞬到终值；参数快照 last-wins 可接受）
        private int _pendingCount;
        private readonly Dictionary<string, float> _pendingOverrides = new Dictionary<string, float>();

        public override bool Execute(string args)
        {
            // 同步/快进语境：直接到终态（满权重 + 目标参数）
            if (!ParseArgs(args, out var overrides, out _)) return false;
            ScreenFilterManager.EnsureInstance();
            return ScreenFilterManager.Instance.SetFilterWithParams(FilterId, 0f, overrides);
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (!ParseArgs(args, out var overrides, out float duration)) yield break;

            if (duration < 0f)
            {
                var def = VNFilterLibrary.GetInstance().GetDefinition(FilterId);
                duration = def != null ? Mathf.Max(def.DefaultDuration, 0f) : 0f;
            }

            ScreenFilterManager.EnsureInstance();
            _pendingCount++;
            _pendingOverrides.Clear();
            foreach (var kv in overrides) _pendingOverrides[kv.Key] = kv.Value;
            try
            {
                bool ok = ScreenFilterManager.Instance.SetFilterWithParams(FilterId, duration, overrides);
                if (ok && duration > 0f) yield return new WaitForSeconds(duration);
            }
            finally
            {
                _pendingCount--;
            }
        }

        public override void Interrupt()
        {
            // 点击跳过：淡入瞬间完成到满权重 + 目标参数（画面不停在中间强度）
            if (_pendingCount <= 0) return;
            _pendingCount = 0;
            ScreenFilterManager.EnsureInstance();
            ScreenFilterManager.Instance.SetFilterWithParams(FilterId, 0f, _pendingOverrides);
        }

        public override void Simulate(string args)
        {
            // 快进/读档重建：滤镜是持久视觉状态，直接到终态保证画面与真实播放一致
            if (!ParseArgs(args, out var overrides, out _)) return;
            ScreenFilterManager.EnsureInstance();
            ScreenFilterManager.Instance.SetFilterWithParams(FilterId, 0f, overrides);
        }

        /// <summary>
        /// 解析参数：每个规格参数 → 缺省值补齐（非法数值警告并回退默认）；
        /// duration 为规格之后的最后一个可选参数（缺失/非法返回 -1，由调用方回退预设默认）。
        /// </summary>
        private bool ParseArgs(string args, out Dictionary<string, float> overrides, out float duration)
        {
            overrides = new Dictionary<string, float>();
            duration = -1f;

            var specs = GetParamSpecs();
            string[] parts = string.IsNullOrEmpty(args) ? new string[0] : args.Split(',');

            for (int i = 0; i < specs.Length; i++)
            {
                float value = specs[i].DefaultValue;
                if (i < parts.Length && !string.IsNullOrWhiteSpace(parts[i]))
                {
                    if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    {
                        Debug.LogWarning($"[{CommandName}] 第 {i + 1} 个参数 '{parts[i].Trim()}' 不是有效数值，已用默认值 {value.ToString(CultureInfo.InvariantCulture)}");
                    }
                    else
                    {
                        value = parsed;
                    }
                }
                overrides[specs[i].ShaderName] = value;
            }

            if (parts.Length > specs.Length && !string.IsNullOrWhiteSpace(parts[specs.Length]))
            {
                if (float.TryParse(parts[specs.Length].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    duration = Mathf.Max(parsed, 0f);
            }

            return true;
        }
    }
}
