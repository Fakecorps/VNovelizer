using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 背景切换过渡命令（着色器驱动，剧场层实现）。
    ///
    /// <para><b>格式</b>：<c>bgtrans(bgID, type, duration, [ease])</c></para>
    ///
    /// <para><b>type</b>（大小写不敏感，支持别名）：
    /// <c>fade</c>=交叉淡化；<c>blinds</c>(shutter)=竖向百叶窗；
    /// <c>wipe</c>(slide)=从左到右擦除；<c>iris</c>(circle)=圆心扩散；
    /// <c>scroll</c>(roll)=从下往上卷开；<c>dissolve</c>(noise)=噪点溶解。
    /// 非法 type 打警告并降级为 fade。</para>
    ///
    /// <para><b>duration</b>：过渡持续秒数（缺省 1.0；&lt;=0 视为瞬间切换）。</para>
    ///
    /// <para><b>ease</b>：第 4 参为缓动曲线预留位（R13 ease 语法，首批不解析，
    /// 进度恒为 Linear），后续版本实现后旧剧本自动生效。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance, "着色器驱动的背景切换过渡动画（fade/blinds/wipe/iris/scroll/dissolve）")]
    public class BgTransCommand : VNCommand
    {
        [VNParam(0, "background", VNParamType.BackgroundName, Description = "目标背景资源名")]
        [VNParam(1, "type", VNParamType.Enum, Default = "fade",
            Options = "fade|blinds|wipe|iris|scroll|dissolve",
            Description = "过渡动画类型：fade 交叉淡化 / blinds 百叶窗 / wipe 擦除 / iris 圆形 / scroll 卷轴 / dissolve 溶解")]
        [VNParam(2, "duration", VNParamType.Float, Min = 0f, Max = 10f, Default = "1.0",
            Description = "过渡持续秒数（<=0 视为瞬间切换）")]
        [VNParam(3, "ease", VNParamType.Enum, Options = EaseArgParser.InEaseOptions,
            Optional = true, Description = "缓动曲线（预留位，首批不生效，进度恒为 Linear）")]
        public override string CommandName { get { return "bgtrans"; } }

        private const float DefaultDuration = 1.0f;

        public override bool Execute(string args)
        {
            return true; // 异步命令，返回 true 表示已接受
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            string[] parts = args.Split(',');

            string bgName = parts[0].Trim();
            if (string.IsNullOrEmpty(bgName))
            {
                Debug.LogError("[BgTrans] 背景资源名不能为空");
                yield break;
            }

            BgTransitionType type = BgTransitionType.Fade;
            if (parts.Length > 1 &&
                !TheaterManager.TryParseBgTransitionType(parts[1], out type))
            {
                Debug.LogWarning($"[BgTrans] 未知过渡类型 \"{parts[1].Trim()}\"，已降级为 fade" +
                                 "（可用: fade|blinds|wipe|iris|scroll|dissolve）");
            }

            float duration = DefaultDuration;
            if (parts.Length > 2) float.TryParse(parts[2].Trim(), out duration);
            if (duration < 0f) duration = 0f;

            // 更新剧本层背景数据状态（继承语义的数据源）
            VNManager.GetInstance().UpdateCurrentBG_OnlyData(bgName);

            // 剧场层着色器过渡（内部含重入保护与异步加载）
            yield return TheaterManager.GetInstance().TransitionBackgroundCoroutine(bgName, type, duration);
        }

        /// <summary>中断：强制瞬间完成切换（终态呈现）</summary>
        public override void Interrupt()
        {
            TheaterManager.GetInstance().CancelBackgroundTransition();
        }

        public override void Simulate(string args)
        {
            if (string.IsNullOrEmpty(args)) return;

            string[] parts = args.Split(',');
            if (parts.Length < 1) return;

            string bgName = parts[0].Trim();
            if (string.IsNullOrEmpty(bgName)) return;

            // 预演时直接更新数据状态，不播放动画
            VNAPI.UpdateBGData(bgName);
        }
    }
}
