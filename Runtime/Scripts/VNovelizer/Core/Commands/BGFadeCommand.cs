using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 背景淡化切换命令（剧场层实现）
    /// 结构迁移：旧实现操作面板的双 Image（Front/Back），新实现经 TheaterManager 的
    /// 主背景演员 + 临时演员交叉淡化（视觉行为一致，状态始终反映终态）。
    /// 后续阶段将接入"资源名.过渡名"的着色器转场语法。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance, "交叉淡化切换背景（同时更新继承状态）")]
    public class BgFadeCommand : VNCommand
    {
        [VNParam(0, "background", VNParamType.BackgroundName, Description = "目标背景资源名")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Default = "1.0",
            Optional = true, Description = "淡化秒数")]
        public override string CommandName { get { return "bgfade"; } }

        private float defaultDuration = 1.0f;

        public override bool Execute(string args)
        {
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            // 解析参数
            string[] parts = args.Split(',');
            string bgName = parts[0].Trim();
            float duration = defaultDuration;
            if (parts.Length > 1) float.TryParse(parts[1].Trim(), out duration);

            // 更新剧本层背景数据状态（继承语义的数据源）
            VNManager.GetInstance().UpdateCurrentBG_OnlyData(bgName);

            // 剧场层交叉淡化（内部含重入保护与异步加载）
            yield return TheaterManager.GetInstance().FadeBackgroundCoroutine(bgName, duration);
        }

        /// <summary>中断：强制瞬间完成切换（终态呈现）</summary>
        public override void Interrupt()
        {
            TheaterManager.GetInstance().CancelBackgroundFade();
        }

        public override void Simulate(string args)
        {
            string[] parts = args.Split(',');
            if (parts.Length < 1) return;
            string bgName = parts[0].Trim();
            // 预演时直接更新数据状态，不播放动画
            VNAPI.UpdateBGData(bgName);
        }
    }
}
