using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Theater;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色淡入命令（剧场层实现）
    /// 格式：charfadein(位置, [时长], [InEase], [OutEase])
    /// 经 TheaterManager 驱动 IActor，不再接触 UGUI 类型。
    /// R13：InEase=淡入曲线（默认 Linear）；OutEase 保留对称写法（无退出阶段，忽略）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "角色立绘淡入（要求同行对应立绘列已填；多个淡入自动并行播放）")]
    public class CharFadeInCommand : VNCommand
    {
        [VNParam(0, "pos", VNParamType.SlotCode,
            Description = "槽位（L/ML/M/MR/R 或全名），要求同行对应立绘列非空")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0.05f, Max = 10f, Default = "0.5",
            Optional = true, Description = "淡入秒数（默认 0.5）")]
        [VNParam(2, "inEase", VNParamType.Enum, Options = EaseArgParser.InEaseOptions,
            Optional = true, Description = "淡入曲线（可选，默认 Linear；只填一个=进出同曲线）")]
        [VNParam(3, "outEase", VNParamType.Enum, Options = EaseArgParser.OutEaseOptions,
            Optional = true, Description = "退出曲线（本命令无退出阶段，仅保留对称写法，忽略）")]
        public override string CommandName { get { return "charfadein"; } }

        private float defaultDuration = 0.5f;

        // --- 活动淡入登记（支持多实例并行 + 点击跳过时正确归位） ---
        private readonly List<string> _activeFades = new List<string>();

        public override bool Execute(string args)
        {
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            string[] parts = args.Split(',');
            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null)
            {
                Debug.LogError($"[CharFadeIn] 未知位置: {parts[0].Trim()}（可用 L/ML/M/MR/R 或全名）");
                yield break;
            }
            float duration = defaultDuration;
            if (parts.Length > 1) float.TryParse(parts[1].Trim(), out duration);

            // R13：解析 [InEase, OutEase]（只填一个 = 进出同曲线）；纯淡入只有 In 阶段生效
            var easeArgs = EaseArgParser.ParseAt(parts, 2);
            Ease ease = easeArgs.In ?? easeArgs.Out ?? Ease.Linear;

            var theater = TheaterManager.GetInstance();
            var actor = theater.GetActor(posCode);
            if (actor == null)
            {
                Debug.LogError($"[CharFadeIn] 找不到位置 {posCode} 的角色");
                yield break;
            }

            theater.SetVisible(posCode, true);
            theater.SetAlpha(posCode, 0f);

            _activeFades.Add(posCode);
            try
            {
                yield return actor.FadeAsync(1f, duration, ease);
            }
            finally
            {
                _activeFades.Remove(posCode);
                // 终态回写状态字典：FadeAsync 只驱动渲染对象的 alpha，
                // 不回写会让 state.alpha 停在 0，导致存档/快进重建时角色隐形。
                theater.SetAlpha(posCode, 1f);
            }
        }

        /// <summary>中断：瞬间到终态（完全显示）</summary>
        public override void Interrupt()
        {
            var theater = TheaterManager.GetInstance();
            foreach (string posCode in _activeFades)
            {
                var actor = theater.GetActor(posCode);
                actor?.Interrupt();
                theater.SetAlpha(posCode, 1f);
                theater.SetVisible(posCode, true);
            }
            _activeFades.Clear();
        }
    }
}
