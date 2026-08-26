using System.Collections.Generic;
using System.Text;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>
    /// 默认演出模板的生成器——「提升不改变演出」硬契约的**单一信息源**。
    ///
    /// <para>
    /// 模板结构（决策 tpl）：
    /// <code>
    /// Par {
    ///     showDialogue(typewriter)                       // 分支 A：对话独立一路
    ///   &amp; [ showbg() &amp; showChar(L..R) &amp; playBGM() &amp; playVoice()
    ///       -&gt; 用户命令链 ]                              // 分支 B：瞬时系统命令 → 用户编排
    /// }
    /// </code>
    /// </para>
    ///
    /// <para>
    /// <b>为何模板中没有 showSpeaker()</b>（2026-08-26 等价性测试实测修正）：
    /// 引擎的 <c>UpdateDialogue</c> 是**一步**广播 <c>UpdateDialogue</c> +
    /// <c>UpdateHeadProfile</c> 两个事件——说话人、正文、头像同属一次更新。
    /// 而 <c>showSpeaker()</c> 也会广播 <c>UpdateHeadProfile</c>，
    /// 若模板同时含两者，该事件会发生**两次**，破坏「提升不改变演出」硬契约
    /// （实测：引擎 8 条事件 vs 模板 9 条）。
    /// 因此 <c>showDialogue</c> 已覆盖说话人显示，模板不再包含 <c>showSpeaker</c>；
    /// 后者的定位是"定制行中只想刷说话人而不重播正文"的场景（如一行内换发言人）。
    /// </para>
    ///
    /// <para>
    /// <b>为何 showDialogue 必须独立成分支 A</b>：<c>showDialogue(typewriter)</c> 会阻塞
    /// 至打字机跑完。若把它放进分支 B 的串行链中，<c>playBGM</c> / <c>playVoice</c>
    /// 与用户命令会被推迟到文本全部打完之后——与引擎隐式路径（打字与 Command 列并行）
    /// 不符，硬契约当场破裂。隔离到自己的并行分支，阻塞只阻塞自己。
    /// </para>
    ///
    /// <para>
    /// <b>为何分支 B 内部是并行而非串行</b>：引擎的
    /// <c>UpdateVisualState → UpdateCharacterSlots → UpdateAudioState → UpdateDialogue</c>
    /// 四步是**同帧同步**完成的。用并行组表达"同帧启动"，比串行链更贴近实际语义
    /// （串行链的每一步都要等前一步"完成"）。
    /// </para>
    ///
    /// <para>
    /// 本类型被三方共用，因此模板结构只有一处定义：
    /// ① <c>RowPromotion</c>（按需提升时写入 Command 列）；
    /// ② 图编辑器（渲染折叠胶囊内的影子节点）；
    /// ③ 模板等价性回归测试（验收硬契约）。
    /// </para>
    /// </summary>
    public static class DefaultPerformanceTemplate
    {
        /// <summary>
        /// 模板中的系统命令节点数：1 个对话 + 1 背景 + 5 立绘 + 1 BGM + 1 语音 = 9。
        /// （不含 showSpeaker——说话人由 showDialogue 一并处理，见类注释）
        /// </summary>
        public const int SystemNodeCount = 9;

        /// <summary>五个立绘槽位代码，顺序与视觉顺序一致（左→右）。</summary>
        public static readonly string[] SlotCodes = { "L", "ML", "M", "MR", "R" };

        /// <summary>
        /// 生成默认模板的命令链文本。
        /// </summary>
        /// <param name="userChain">
        /// 用户原有的命令链（增强行的 Command 列内容）。为空则模板只含系统命令。
        /// </param>
        public static string BuildText(string userChain = null)
        {
            var branchB = new StringBuilder();

            // 分支 B 前半：瞬时系统命令并行（对应引擎的同帧四步）
            // 注意：不含 showSpeaker——说话人由 showDialogue 一并广播，
            // 重复会使 UpdateHeadProfile 事件发生两次（见类注释）
            branchB.Append("showbg()");
            foreach (string slot in SlotCodes)
                branchB.Append(" & showChar(").Append(slot).Append(')');
            branchB.Append(" & playBGM() & playVoice()");

            // 分支 B 后半：用户编排（对应引擎随后启动的 Command 列协程）
            if (!string.IsNullOrWhiteSpace(userChain))
                branchB.Append(" -> ").Append(userChain.Trim());

            // 分支 A 与分支 B 并行；分支 B 是 Seq，作为 Par 子项必须加 []
            return "showDialogue(typewriter) & [" + branchB + "]";
        }

        /// <summary>
        /// 生成模板 AST（图编辑器渲染影子节点用）。
        /// 直接复用 <see cref="ChainParser"/> 解析 <see cref="BuildText"/> 的结果，
        /// 保证文本形式与 AST 形式**不可能漂移**。
        /// </summary>
        public static ChainNode BuildAst(string userChain = null)
        {
            var parsed = ChainParser.Parse(BuildText(userChain));
            return parsed.Root;
        }

        /// <summary>
        /// 模板中每个系统命令引用的数据列（图编辑器的 📎 徽章数据源）。
        /// key = 命令签名，value = 数据列名。
        ///
        /// <c>showDialogue</c> 同时引用 Text / Speaker / HeadProfile 三列——
        /// 它对应引擎的 <c>UpdateDialogue</c>，该方法一步处理正文、说话人与头像。
        /// </summary>
        public static Dictionary<string, string> GetColumnBindings()
        {
            return new Dictionary<string, string>
            {
                { "showDialogue(typewriter)", "Text, Speaker, HeadProfile" },
                { "showbg()",                 "Background" },
                { "playBGM()",                "BGM" },
                { "playVoice()",              "Voice" },
                { "showChar(L)",              "CharLeft" },
                { "showChar(ML)",             "CharMid_Left" },
                { "showChar(M)",              "CharMid" },
                { "showChar(MR)",             "CharMid_Right" },
                { "showChar(R)",              "CharRight" },
            };
        }
    }
}
