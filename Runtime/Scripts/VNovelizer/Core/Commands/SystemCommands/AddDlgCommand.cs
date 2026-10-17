using System.Collections;
using UnityEngine;
using VNovelizer.Core;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands.SystemCommands
{
    /// <summary>
    /// 【系统命令】在当前对话文本末尾追加一段文字。
    ///
    /// <para><b>格式</b>：<c>addDlg("Text")</c> 或 <c>addDlg(Text)</c>——
    /// 双引号包裹时文本可含逗号；不加引号则取整个参数串。</para>
    ///
    /// <para>
    /// <b>显示方式跟随本行 showDialogue</b>（VNManager.currentDialogueDirectMode）：
    /// <list type="bullet">
    /// <item><c>showDialogue(typewriter)</c> / 默认 —— 已显示字符保持不动，追加部分继续逐字打字，
    /// <b>阻塞本分支直到追加部分打完</b>（与 showDialogue 的阻塞语义一致）。</item>
    /// <item><c>showDialogue(direct)</c> —— 瞬间全显，不阻塞。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>边界语义</b>：追加文本是运行时瞬态呈现——不进历史记录、不参与本地化
    /// （本地化键仍锚定本行 Text 列）；快进/预演不显示；点击跳过全显（DisplayAllText 复用）。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.System,
        "在当前对话文本末尾追加文字（显示方式跟随本行 showDialogue；typewriter 会阻塞至追加部分打完）")]
    public class AddDlgCommand : VNCommand
    {
        [VNParam(0, "text", VNParamType.String,
            Description = "追加文本（可用双引号包裹以含逗号，如 addDlg(\"你好，世界\")）")]
        public override string CommandName => "adddlg";

        /// <summary>提取追加文本：引号包裹时取引号内全部内容；否则取整个 args。</summary>
        private static string ExtractText(string args)
        {
            if (string.IsNullOrEmpty(args)) return string.Empty;
            string trimmed = args.Trim();
            if (trimmed.StartsWith("\""))
            {
                int end = trimmed.IndexOf('"', 1);
                if (end > 1) return trimmed.Substring(1, end - 1);
                return trimmed.Substring(1);
            }
            return trimmed;
        }

        public override bool Execute(string args)
        {
            // 快进/Simulate 语境：追加文本是瞬态呈现，同步路径不显示、不落状态。
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            string text = ExtractText(args);
            if (string.IsNullOrEmpty(text)) yield break;

            var manager = VNManager.GetInstance();
            manager.SysAppendDialogue(text);

            // typewriter：阻塞至追加部分打完（TypingFinished 事件复位 isTextDisplaying）；
            // direct：isTextDisplaying 恒 false，立即返回。
            const int maxWaitFrames = 60 * 60; // 60 秒 @60fps 兜底
            int frames = 0;
            while (manager.IsTextDisplaying() && frames++ < maxWaitFrames)
                yield return null;

            if (frames >= maxWaitFrames)
                Debug.LogWarning("[AddDlg] 等待追加文字打字完成超时（60s），提前结束等待");
        }

        /// <summary>
        /// 阻塞中被中断：全显文本（与 showDialogue 的 Interrupt 语义一致，
        /// 防止追加部分停在半截）。
        /// </summary>
        public override void Interrupt()
        {
            EventCenter.GetInstance().EventTrigger(VNGameEvents.DisplayAllText);
        }

        /// <summary>
        /// 快进预演：追加文本是纯呈现，无状态副作用（不写历史记录）。
        /// </summary>
        public override void Simulate(string args)
        {
            // 无状态副作用，空实现
        }
    }
}
