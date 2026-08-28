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
    /// <b>2026-08-28：模板按数据列填写状态过滤</b>。<see cref="BuildText"/> 增加可选
    /// <paramref name="slots"/> 参数——只生成用户在 Char 列实际填了角色的槽位
    /// 的 <c>showChar</c> 节点。Null/空 = 全 5 槽（向后兼容，模板等价性测试用）。
    /// 行为上：
    /// <list type="bullet">
    /// <item>用户在 Excel 填了 <c>CharLeft=Amy</c>、<c>CharMid=Jenny</c> → 模板图只有
    /// <c>showChar(L)</c>、<c>showChar(M)</c>，没有 L/M 之外的多余节点；</item>
    /// <item>用户再加 <c>CharMid_Left=Bob</c> → 重新打开该行 / 切行即生效（按新
    /// 槽位集合重新生成），也可手动添加 <c>showChar(ML)</c> 显示新增立绘。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// 本类型被三方共用，因此模板结构只有一处定义：
    /// ① <c>RowPromotion</c>（按需提升时写入 Command 列）；
    /// ② 图编辑器（渲染合成模板的图节点）；
    /// ③ 模板等价性回归测试（验收硬契约）。
    /// </para>
    /// </summary>
    public static class DefaultPerformanceTemplate
    {
        /// <summary>
        /// 模板中的系统命令节点数（默认满载）：1 对话 + 1 背景 + 5 立绘 + 1 BGM + 1 语音 = 9。
        /// （不含 showSpeaker——说话人由 showDialogue 一并处理，见类注释）
        ///
        /// <para>
        /// 实际生成的节点数因 <c>slots</c> 过滤而异（按 slots 数量动态生成 showChar），
        /// 此处保留为"全 5 槽上限"参考值。
        /// </para>
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
        /// <param name="slots">
        /// 实际要生成的立绘槽位代码集合（顺序不限）。
        /// <list type="bullet">
        /// <item>null / 空 = 全 5 槽（向后兼容；模板等价性测试走此路径，验收
        /// "全槽位时的硬契约"）；</item>
        /// <item>非空 = 只为列出的槽位生成 <c>showChar(pos)</c> 节点。</item>
        /// </list>
        /// </param>
        public static string BuildText(string userChain = null, IReadOnlyCollection<string> slots = null)
        {
            var branchB = new StringBuilder();

            // 分支 B 前半：瞬时系统命令并行（对应引擎的同帧四步）
            // 注意：不含 showSpeaker——说话人由 showDialogue 一并广播，
            // 重复会使 UpdateHeadProfile 事件发生两次（见类注释）
            branchB.Append("showbg()");

            // showChar 节点：按实际填写的槽位过滤。维护原视觉顺序（L → R），
            // 让生成的图编辑器节点排布从左到右，与 Excel 槽位列顺序一致。
            foreach (string slot in OrderedSlots(slots))
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
        public static ChainNode BuildAst(string userChain = null, IReadOnlyCollection<string> slots = null)
        {
            var parsed = ChainParser.Parse(BuildText(userChain, slots));
            return parsed.Root;
        }

        /// <summary>
        /// 模板中每个系统命令引用的数据列（图编辑器的 📎 徽章数据源）。
        /// key = 命令签名，value = 数据列名。
        ///
        /// <c>showDialogue</c> 同时引用 Text / Speaker / HeadProfile 三列——
        /// 它对应引擎的 <c>UpdateDialogue</c>，该方法一步处理正文、说话人与头像。
        ///
        /// 注意：本表列出**全部 5 槽**绑定关系，即使当前行只填了部分槽位——
        /// 这样用户在图编辑器里手动拖入 <c>showChar(ML)</c> 等节点时，
        /// 📎 徽章始终能查到正确的列名映射。
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

        /// <summary>
        /// 按 <see cref="SlotCodes"/> 的视觉顺序输出传入的槽位集合。
        /// 未在集合中的槽位被丢弃；集合外的未知槽位代码（防御）也丢弃。
        /// </summary>
        private static IEnumerable<string> OrderedSlots(IReadOnlyCollection<string> slots)
        {
            if (slots == null || slots.Count == 0)
                return SlotCodes;

            var set = new HashSet<string>(slots, System.StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(SlotCodes.Length);
            foreach (string code in SlotCodes)
                if (set.Contains(code)) result.Add(code);
            return result;
        }
    }
}
