using System.Collections;
using UnityEngine;
using VNovelizer.Core;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands.SystemCommands
{
    /// <summary>
    /// 【系统命令】显示本行对话（说话人 + 正文 + 头像 + 历史记录 + 本地化）。
    ///
    /// <para><b>格式</b>：<c>showDialogue([mode])</c>，mode ∈ { direct, typewriter }，空参默认 typewriter。</para>
    ///
    /// <para>
    /// <b>文本不可内联</b>：本命令只接显示方式参数，正文永远引用本行 Text 列。
    /// 由此本地化键 <c>text.{lineID}</c> / <c>speaker.{lineID}</c> **结构性不可能失效**——
    /// 这是"内容归数据列、编排归命令链"职责分离的直接收益。改台词请改数据列。
    /// </para>
    ///
    /// <para>
    /// <b>阻塞语义</b>：
    /// <list type="bullet">
    /// <item><c>direct</c> —— 瞬间全显，立即返回，不阻塞链。</item>
    /// <item><c>typewriter</c> —— 逐字打字，<b>阻塞本分支直到打完</b>。</item>
    /// </list>
    /// 若不希望阻塞后续演出，把本命令放进并行分支即可（<c>Par{ showDialogue &amp; [其他...] }</c>）——
    /// 并行是命令链自身的能力，无需为"不阻塞"另造命令。默认模板正是这样做的
    /// （见 VNCommandChainSpec.md §11.3.2）。
    /// </para>
    ///
    /// <para>
    /// <b>Interrupt 必须全显文本</b>：玩家点击时 <c>VNManager</c> 的处理是两个互斥分支且
    /// "命令中断"优先于"补完文本"——阻塞中的本命令会命中前者，若 Interrupt 不触发
    /// DisplayAllText，文本将永久停在半截。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.System,
        "显示本行对话（正文引用 Text 列，不可内联）。typewriter 会阻塞本分支至打完")]
    public class ShowDialogueCommand : VNCommand
    {
        [VNParam(0, "mode", VNParamType.Enum, Options = "direct|typewriter",
            Default = "typewriter", Optional = true,
            Description = "direct=瞬间全显且不阻塞；typewriter=逐字打字并阻塞至打完")]
        [VNParam(1, "text", VNParamType.String,
            ImplicitBinding = true, BoundColumn = "Text", InlineForbidden = true,
            Description = "正文：永远引用本行 Text 列，不可内联（保障本地化键不失效）")]
        public override string CommandName => "showdialogue";

        /// <summary>解析 mode：空参 / 无法识别 → typewriter（与引擎隐式路径默认一致）</summary>
        private static bool IsDirectMode(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;
            return args.Trim().ToLower() == "direct";
        }

        public override bool Execute(string args)
        {
            bool direct = IsDirectMode(args);
            VNManager.GetInstance().SysShowDialogue(direct);

            // direct 已瞬间完成；typewriter 的阻塞在 ExecuteAsync 中等待
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            bool direct = IsDirectMode(args);
            var manager = VNManager.GetInstance();

            manager.SysShowDialogue(direct);

            if (direct) yield break;

            // typewriter：阻塞至打字机跑完。
            // isTextDisplaying 由 UpdateDialogue 置 true、由 TypingFinished 事件置 false。
            // 加帧数上限兜底：若面板不存在（TypingFinished 永不到达），避免协程永久挂起。
            const int maxWaitFrames = 60 * 60; // 60 秒 @60fps
            int frames = 0;
            while (manager.IsTextDisplaying() && frames++ < maxWaitFrames)
                yield return null;

            if (frames >= maxWaitFrames)
                Debug.LogWarning("[ShowDialogue] 等待打字机完成超时（60s），提前结束等待");
        }

        /// <summary>
        /// 点击跳过：立即全显文本（快进到最终态）。
        /// 不可省略——否则阻塞中的本命令被中断后，文本会永久停在半截。
        /// </summary>
        public override void Interrupt()
        {
            EventCenter.GetInstance().EventTrigger(VNGameEvents.DisplayAllText);
        }

        /// <summary>
        /// 快进预演：对话是纯呈现，无需模拟状态。
        /// 历史记录由引擎在真实播放时写入，预演不应重复写入。
        /// </summary>
        public override void Simulate(string args)
        {
            // 无状态副作用，空实现
        }
    }
}
