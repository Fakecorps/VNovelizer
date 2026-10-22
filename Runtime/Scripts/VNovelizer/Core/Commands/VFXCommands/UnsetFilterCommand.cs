using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Filters;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 关闭画面滤镜命令：unsetfilter(name[, duration])
    ///
    /// <para>
    /// name = 滤镜逻辑名（内置 7 款或自定义滤镜预设）；特殊值 <c>all</c> = 关闭全部滤镜。
    /// duration = 淡出秒数（可选；缺省 = 预设默认时长，0 = 立即移除）。
    /// 权重淡出到 0 后滤镜条目自动移除并恢复相机直接渲染；未激活时幂等静默。
    /// </para>
    ///
    /// <para>语义：Execute/Simulate/Interrupt = 立即移除（快进/中断画面一致）；
    /// ExecuteAsync 等待淡出时长后返回（保持 &amp; 时序）。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "关闭画面滤镜（权重渐变淡出后移除）；name=all 关闭全部滤镜")]
    public class UnsetFilterCommand : VNCommand
    {
        [VNParam(0, "name", VNParamType.FilterName,
            Description = "滤镜名（内置 7 款或自定义滤镜预设；all = 关闭全部滤镜）")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "淡出秒数（缺省 = 预设默认时长；0 = 立即移除）")]
        public override string CommandName { get { return "unsetfilter"; } }

        // 多实例并行支持：淡出等待中的滤镜名清单（Interrupt 时逐个立即移除）
        private readonly List<string> _pending = new List<string>();

        public override bool Execute(string args)
        {
            string name = ParseName(args);
            if (string.IsNullOrEmpty(name)) return false;
            ScreenFilterManager.EnsureInstance();
            ScreenFilterManager.Instance.RemoveFilterImmediate(name);
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            string name = ParseName(args);
            if (string.IsNullOrEmpty(name)) yield break;

            float duration = ParseDuration(args);
            if (duration < 0f)
            {
                var def = VNFilterLibrary.GetInstance().GetDefinition(name);
                duration = def != null ? Mathf.Max(def.DefaultDuration, 0f) : 0f;
            }

            ScreenFilterManager.EnsureInstance();
            _pending.Add(name);
            try
            {
                ScreenFilterManager.Instance.UnsetFilter(name, duration);
                if (duration > 0f) yield return new WaitForSeconds(duration);
            }
            finally
            {
                _pending.Remove(name);
            }
        }

        public override void Interrupt()
        {
            // 点击跳过：淡出瞬间完成（滤镜立即移除，画面恢复原始）
            if (_pending.Count == 0) return;
            var snapshot = new List<string>(_pending);
            _pending.Clear();
            ScreenFilterManager.EnsureInstance();
            foreach (var n in snapshot)
                ScreenFilterManager.Instance.RemoveFilterImmediate(n);
        }

        public override void Simulate(string args)
        {
            // 快进/读档重建：直接移除到终态（保证重建画面与真实播放一致）
            string name = ParseName(args);
            if (string.IsNullOrEmpty(name)) return;
            ScreenFilterManager.EnsureInstance();
            ScreenFilterManager.Instance.RemoveFilterImmediate(name);
        }

        private static string ParseName(string args)
        {
            if (string.IsNullOrEmpty(args)) return null;
            string[] parts = args.Split(',');
            string name = parts[0].Trim();
            if (name.Length == 0)
            {
                Debug.LogWarning("[UnsetFilter] 滤镜名为空，已忽略");
                return null;
            }
            return name;
        }

        /// <summary>解析可选时长参数；未提供返回 -1（由调用方回退预设默认）</summary>
        private static float ParseDuration(string args)
        {
            string[] parts = args.Split(',');
            if (parts.Length < 2) return -1f;
            float duration;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration))
                return -1f;
            return Mathf.Max(duration, 0f);
        }
    }
}
