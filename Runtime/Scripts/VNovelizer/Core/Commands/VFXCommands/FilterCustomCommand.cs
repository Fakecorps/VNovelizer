using System.Collections;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Filters;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 自定义滤镜命令：filtercustom(id[, strength[, duration]])（VFX 注册表扩展 2026-10-23）
    ///
    /// <para>
    /// id = VFX 注册表中的滤镜条目（含用户拖入的自定义 Shader 滤镜；下拉候选 = 注册表启用条目）。
    /// strength = 整体强弱 0~1（只乘全局混合权重 _Weight，不暴露单个 shader 参数——
    /// shader 属性取资产默认值；缺省 1.0）。duration = 渐变秒数（缺省 = 注册表条目默认时长，0 = 立即）。
    /// 重复开启幂等（渐变到目标权重并刷新）。关闭用 unsetfilter(id) / unsetfilter(all)。
    /// </para>
    ///
    /// <para>语义：Execute/Simulate/Interrupt = 立即终态（满 strength 权重）；
    /// ExecuteAsync 等待渐变时长后返回（保持 &amp; 时序）。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启注册表中的任意滤镜（含自定义 Shader 滤镜条目）；strength 只调整体强弱，shader 参数用其资产默认值")]
    public class FilterCustomCommand : VNCommand
    {
        [VNParam(0, "id", VNParamType.FilterName,
            Description = "注册表滤镜 id（内置 7 款或自定义 shader 滤镜条目）")]
        [VNParam(1, "strength", VNParamType.Float, Min = 0f, Max = 1f, Default = "1", Optional = true,
            Description = "整体强弱（0~1；缺省 = 1.0 满强度）")]
        [VNParam(2, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = 注册表条目默认时长；0 = 立即生效）")]
        public override string CommandName { get { return "filtercustom"; } }

        private const float DefaultStrength = 1f;

        // 淡入等待中的并行实例计数 + 中断时回放的目标（与 FilterCommandBase 同构）
        private int _pendingCount;
        private string _pendingId;
        private float _pendingStrength;

        public override bool Execute(string args)
        {
            if (!ParseArgs(args, out string id, out float strength, out _)) return false;
            ScreenFilterManager.EnsureInstance();
            return ScreenFilterManager.Instance.SetFilterScaled(id, strength, 0f);
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (!ParseArgs(args, out string id, out float strength, out float duration)) yield break;
            if (duration < 0f) duration = VNFilterLibrary.GetInstance().GetDefaultDuration(id);

            ScreenFilterManager.EnsureInstance();
            _pendingCount++;
            _pendingId = id;
            _pendingStrength = strength;
            try
            {
                bool ok = ScreenFilterManager.Instance.SetFilterScaled(id, strength, duration);
                if (ok && duration > 0f) yield return new WaitForSeconds(duration);
            }
            finally
            {
                _pendingCount--;
            }
        }

        public override void Interrupt()
        {
            // 点击跳过：渐变瞬间完成到目标强度（画面不停在中间权重）
            if (_pendingCount <= 0) return;
            _pendingCount = 0;
            ScreenFilterManager.EnsureInstance();
            ScreenFilterManager.Instance.SetFilterScaled(_pendingId, _pendingStrength, 0f);
        }

        public override void Simulate(string args)
        {
            // 快进/读档重建：滤镜是持久视觉状态，直接到终态保证画面与真实播放一致
            if (!ParseArgs(args, out string id, out float strength, out _)) return;
            ScreenFilterManager.EnsureInstance();
            ScreenFilterManager.Instance.SetFilterScaled(id, strength, 0f);
        }

        private static bool ParseArgs(string args, out string id, out float strength, out float duration)
        {
            id = null;
            strength = DefaultStrength;
            duration = -1f;

            string[] parts = string.IsNullOrEmpty(args) ? new string[0] : args.Split(',');

            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                Debug.LogWarning("[filtercustom] 缺少滤镜 id 参数（格式 filtercustom(id[,strength[,duration]])）");
                return false;
            }
            id = parts[0].Trim();

            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    Debug.LogWarning($"[filtercustom] 第 2 个参数 '{parts[1].Trim()}' 不是有效数值，已用默认值 1");
                }
                else
                {
                    strength = Mathf.Clamp(parsed, 0f, 1f);
                }
            }

            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                if (float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    duration = Mathf.Max(parsed, 0f);
            }

            return true;
        }
    }
}
