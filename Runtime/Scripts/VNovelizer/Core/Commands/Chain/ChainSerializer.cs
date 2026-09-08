using System.Collections.Generic;
using System.Text;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>
    /// AST → 命令链文本序列化器（<see cref="ChainParser"/> 的逆向）。
    ///
    /// <para>
    /// <b>括号规则是本组件的正确性核心</b>，三条硬规则缺一不可：
    /// </para>
    ///
    /// <list type="number">
    /// <item>
    /// <b>Par 的 Seq 子项必须强制加 <c>[]</c></b>。因 <c>&amp;</c> 优先级高于 <c>-&gt;</c>，
    /// <c>Par{Seq{a,b}, Seq{c,d}}</c> 若裸写成 <c>a-&gt;b &amp; c-&gt;d</c>，
    /// 反解析会得到 <c>a -&gt; (b∥c) -&gt; d</c>——**语义完全不同**。
    /// 正确输出：<c>[a-&gt;b] &amp; [c-&gt;d]</c>。
    /// </item>
    /// <item>
    /// <b>单子项的 Seq/Par 必须透传</b>，不产生 <c>[]</c>。否则 <c>wait(1)</c>
    /// 会变成 <c>[wait(1)]</c>，往复几次转换括号层层累积直撞深度警告。
    /// （<see cref="GraphToAst"/> 已做归一化，此处再兜一层——AST 也可能来自其他来源。）
    /// </item>
    /// <item>
    /// <b>Seq 的 Par 子项不需要括号</b>。<c>Seq{a, Par{b,c}, d}</c> 输出
    /// <c>a -&gt; b &amp; c -&gt; d</c> 即可正确反解析，因为 <c>&amp;</c> 结合更紧。
    /// 但 <b>Par 的 Par 子项</b>（嵌套并行）需要括号以保持结构。
    /// </item>
    /// </list>
    ///
    /// <para>
    /// <b>幂等自校验</b>：<see cref="SerializeAndVerify"/> 会把输出交给
    /// <see cref="ChainParser"/> 反解析并比对 AST **结构等价**（忽略括号与空白差异）。
    /// 不等价即说明序列化器实现有 bug，应阻断保存而非写坏 CSV。
    /// </para>
    /// </summary>
    public static class ChainSerializer
    {
        /// <summary>序列化并做幂等自校验的结果。</summary>
        public class Result
        {
            public string Text;
            public List<string> Errors = new List<string>();
            public bool Success => Errors.Count == 0;
        }

        /// <summary>
        /// 序列化 AST 为命令链文本。null 或空树返回空串。
        /// </summary>
        public static string Serialize(ChainNode root)
        {
            if (root == null) return "";

            var sb = new StringBuilder();
            WriteNode(sb, root, ParentContext.Root);
            return sb.ToString();
        }

        /// <summary>
        /// 2026-09-01：序列化 AST 为<b>格式化文本</b>（含换行 + 4 空格缩进）。
        ///
        /// <para>
        /// 用于编辑器内显示（用户编辑友好），与 <see cref="Serialize"/> 的紧凑形式互补：
        /// - 顶层命令独占一行，行尾跟 <c>&amp;</c> 或 <c>-&gt;</c>（非末位）
        /// - <c>[</c> 触发换行，单独占一行，下一行起子项缩进 +4
        /// - <c>]</c> 单独占一行，与对应 <c>[</c> 同级缩进；非末位时 <c>] &amp; ...</c> 同行
        /// - 嵌套每层 +4 空格
        /// - R11 choice 块展开（大括号对齐 <c>[]</c> 规则）：<c>choice</c> 独占一行、
        ///   <c>{</c>/<c>}</c> 单独占行深度 ±1、每对「描述, 链」一行（非末位行尾逗号）、
        ///   选项链内部命令递归换行缩进（嵌套 choice / 分组照常）
        /// </para>
        ///
        /// <para>
        /// <b>CSV 写回仍用 <see cref="Serialize"/></b>（紧凑形式，不带换行/缩进）——
        /// 编辑器内 <c>_text</c> 与 CSV 是两种表示。
        /// </para>
        /// <para>
        /// <b>幂等性</b>：<see cref="ChainLexer"/> 已跳过所有空白（含换行），
        /// 含格式化文本的 <c>_text</c> 可被 <see cref="ChainParser"/> 正确解析，
        /// 结构等价紧凑形式。
        /// </para>
        /// <para>
        /// <b>实现</b>：先用 <see cref="Serialize"/> 得到紧凑形式，再用
        /// <see cref="ChainLexer"/> 切 token 流，按 token 顺序输出（不依赖 AST 结构，
        /// 因为 <see cref="ChainParser"/> 不保留 <c>[]</c> 分组信息，按 AST 输出会
        /// 丢失语义必需的 brackets）。每个 Command token 独占一行，行尾根据下一个
        /// token 是 <c>&amp;</c> / <c>-&gt;</c> 附加操作符；<c>[</c> / <c>]</c>
        /// 单独占一行，进入 <c>[</c> depth+1，离开 <c>]</c> depth-1。
        /// choice 块 token 由 <see cref="FormatChoiceBlock"/> 递归展开。
        /// </para>
        /// </summary>
        public static string SerializeFormatted(ChainNode root)
        {
            if (root == null) return "";

            // 先用紧凑序列化得到 token 流（含语义必需的 brackets）
            string compact = Serialize(root);
            if (string.IsNullOrEmpty(compact)) return "";

            var tokens = ChainLexer.Tokenize(compact);
            if (tokens.Count == 0) return "";

            var sb = new StringBuilder();
            int depth = 0;
            FormatTokenRun(sb, tokens, ref depth);

            // 去掉末尾换行
            while (sb.Length > 0 && sb[sb.Length - 1] == '\n')
                sb.Length--;
            return sb.ToString();
        }

        // ---------------- R11：choice 块格式化排版 ----------------

        /// <summary>token 文本是否为 choice{...} 块（命令名后紧跟大括号且以 } 结尾）。</summary>
        private static bool IsChoiceBlockToken(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int curly = text.IndexOf('{');
            if (curly <= 0 || !text.TrimEnd().EndsWith("}")) return false;
            return text.Substring(0, curly).Trim().ToLower() == "choice";
        }

        /// <summary>
        /// 把 token 流排版输出：每条命令一行、<c>[</c>/<c>]</c> 单独占行（深度 ±1）、
        /// choice 块展开为多行（R11）。行尾操作符（&amp; / -&gt;）附加到前一
        /// Command / <c>]</c> / <c>}</c> 行尾，与现有规则一致。
        /// </summary>
        private static void FormatTokenRun(StringBuilder sb, List<ChainToken> tokens, ref int depth)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                var tok = tokens[i];
                string indent = new string(' ', depth * 4);

                switch (tok.Type)
                {
                    case ChainTokenType.Command:
                        if (IsChoiceBlockToken(tok.Text))
                        {
                            // R11：choice 块展开（choice 行 / { 行 / 选项对 / } 行）
                            FormatChoiceBlock(sb, tokens, i, ref depth, indent);
                        }
                        else
                        {
                            sb.Append(indent).Append(tok.Text.Trim());
                            AppendTrailingOp(sb, tokens, i);
                            sb.Append('\n');
                        }
                        break;

                    case ChainTokenType.LBracket:
                        sb.Append(indent).Append('[').Append('\n');
                        depth++;
                        break;

                    case ChainTokenType.RBracket:
                        depth = System.Math.Max(0, depth - 1);
                        indent = new string(' ', depth * 4);
                        sb.Append(indent).Append(']');
                        AppendTrailingOp(sb, tokens, i);
                        sb.Append('\n');
                        break;

                    // Amp / Arrow 已在前一 Command / ] / } 行尾附加，跳过
                    case ChainTokenType.Amp:
                    case ChainTokenType.Arrow:
                        break;
                }
            }
        }

        /// <summary>下一 token 是 & / -> 时把操作符附加到当前行尾。</summary>
        private static void AppendTrailingOp(StringBuilder sb, List<ChainToken> tokens, int i)
        {
            if (i + 1 >= tokens.Count) return;
            var next = tokens[i + 1];
            if (next.Type == ChainTokenType.Amp) sb.Append(" &");
            else if (next.Type == ChainTokenType.Arrow) sb.Append(" ->");
        }

        /// <summary>
        /// R11：展开一个 choice 块 token 为多行：
        /// <code>
        /// choice
        /// {
        ///     描述1, 链首命令 ->
        ///         链后续命令,
        ///     描述2, 链,
        /// }
        /// </code>
        /// 大括号对齐 <c>[]</c> 规则（单独占行、深度 ±1）；选项对一对一行（非末位行尾
        /// 逗号）；选项链内部按 <see cref="FormatTokenRun"/> 递归换行（缩进 +4，
        /// 嵌套 choice / 分组照常）。
        /// </summary>
        private static void FormatChoiceBlock(StringBuilder sb, List<ChainToken> allTokens,
            int blockIndex, ref int depth, string indent, string linePrefix = null)
        {
            string text = allTokens[blockIndex].Text.Trim();

            // 解析块内容（幂等：紧凑文本 → ChoiceNode；防御失败时输出空块壳）。
            // Parser 返回 Seq{Par{Choice}} 包装——需先 Flatten 剥单子项包装再取 ChoiceNode。
            ChoiceNode choiceNode = null;
            var parsed = ChainParser.Parse(text);
            choiceNode = Flatten(parsed.Root) as ChoiceNode;

            sb.Append(indent).Append(linePrefix ?? "").Append("choice").Append('\n');
            sb.Append(indent).Append('{').Append('\n');
            depth++;

            if (choiceNode != null)
            {
                string innerIndent = new string(' ', depth * 4);

                for (int i = 0; i < choiceNode.Options.Count; i++)
                {
                    var opt = choiceNode.Options[i];
                    string desc = FormatChoiceText(opt.Text ?? "");
                    string chainCompact = Serialize(opt.Chain);
                    bool trailingComma = i < choiceNode.Options.Count - 1;

                    if (string.IsNullOrEmpty(chainCompact))
                    {
                        // 空链选项：仅描述。**必须恒带尾逗号**（含最后一项）——
                        // 「继续, }」的尾逗号让切分产生配对空链段；无逗号则
                        // 「继续」成为孤立奇数段（解析报"缺少命令链"）。
                        sb.Append(innerIndent).Append(desc).Append(',').Append('\n');
                    }
                    else
                    {
                        FormatChainAfterPrefix(sb, innerIndent + desc + ", ",
                            chainCompact, depth + 1, trailingComma);
                    }
                }
            }

            depth--;
            string closeIndent = new string(' ', depth * 4);
            sb.Append(closeIndent).Append('}');
            AppendTrailingOp(sb, allTokens, blockIndex);
            sb.Append('\n');
        }

        /// <summary>
        /// R11：把一条选项链排版到「描述, 」前缀之后——链首命令与描述同行，
        /// 链内后续命令按 <see cref="FormatTokenRun"/> 递归（缩进 <paramref name="baseDepth"/>）。
        /// 链尾（非末位选项）补逗号。
        /// </summary>
        private static void FormatChainAfterPrefix(StringBuilder sb, string prefix,
            string chainCompact, int baseDepth, bool trailingComma)
        {
            if (string.IsNullOrEmpty(chainCompact))
            {
                sb.Append(prefix);
                AppendCommaAfterRun(sb, trailingComma);
                return;
            }

            var tokens = ChainLexer.Tokenize(chainCompact);
            if (tokens.Count == 0)
            {
                sb.Append(prefix);
                AppendCommaAfterRun(sb, trailingComma);
                return;
            }

            var first = tokens[0];

            if (first.Type == ChainTokenType.LBracket)
            {
                // 链以分组开头：[ 接在描述行尾，内部另起行
                sb.Append(prefix).Append('[').Append('\n');
                int d = baseDepth + 1;
                FormatTokenRun(sb, tokens.GetRange(1, tokens.Count - 1), ref d);
                AppendCommaAfterRun(sb, trailingComma);
                return;
            }

            if (first.Type == ChainTokenType.Command && IsChoiceBlockToken(first.Text))
            {
                // 链以嵌套 choice 开头：描述与 choice 同行，块内部照常展开
                int d = baseDepth;
                FormatChoiceBlock(sb, tokens, 0, ref d, new string(' ', d * 4), linePrefix: prefix);
                AppendCommaAfterRun(sb, trailingComma);
                return;
            }

            // 普通命令开头：接在描述后；后续 token 递归排版
            sb.Append(prefix).Append(first.Text.Trim());
            if (tokens.Count > 1)
            {
                AppendTrailingOp(sb, tokens, 0);
                sb.Append('\n');
                int d = baseDepth;
                FormatTokenRun(sb, tokens.GetRange(1, tokens.Count - 1), ref d);
            }
            AppendCommaAfterRun(sb, trailingComma);
        }

        /// <summary>
        /// 选项对收尾：非末位在链最后一行行尾补逗号；**恒保证行尾换行**——
        /// 单命令链（无递归换行）末尾若不带 '\n'，块内下一选项对 / 闭合的 '}'
        /// 会拼到同一行（视觉粘连）。
        /// </summary>
        private static void AppendCommaAfterRun(StringBuilder sb, bool trailingComma)
        {
            if (trailingComma)
            {
                if (sb.Length > 0 && sb[sb.Length - 1] == '\n') sb.Length--;
                sb.Append(',');
            }
            if (sb.Length == 0 || sb[sb.Length - 1] != '\n')
                sb.Append('\n');
        }

        /// <summary>
        /// 序列化 + 幂等自校验（保存前应走本入口）。
        /// </summary>
        public static Result SerializeAndVerify(ChainNode root)
        {
            var result = new Result();

            result.Text = Serialize(root);
            if (string.IsNullOrEmpty(result.Text)) return result; // 空链无需校验

            var reparsed = ChainParser.Parse(result.Text);

            if (!reparsed.Success)
            {
                result.Errors.Add("序列化结果无法被解析器还原（序列化器内部错误）：" +
                                  string.Join("; ", reparsed.Errors));
                return result;
            }

            if (!AreStructurallyEqual(root, reparsed.Root))
            {
                result.Errors.Add(
                    $"序列化结果与原 AST 结构不等价（序列化器内部错误）。输出：{result.Text}");
            }

            return result;
        }

        /// <summary>
        /// 两棵 AST 是否**结构等价**：忽略括号写法与空白差异，
        /// 但命令顺序、并行/串行关系、命令名与参数必须一致。
        ///
        /// 比较前对两侧都做归一化（剥单子项包装），因此
        /// <c>Seq{Par{a}}</c> 与 <c>a</c> 视为等价——这正是"忽略括号差异"的含义。
        /// </summary>
        public static bool AreStructurallyEqual(ChainNode a, ChainNode b)
        {
            a = Flatten(a);
            b = Flatten(b);

            if (a == null || b == null) return a == null && b == null;
            if (a.GetType() != b.GetType()) return false;

            if (a is CommandNode ca && b is CommandNode cb)
            {
                return string.Equals((ca.Name ?? "").Trim(), (cb.Name ?? "").Trim(),
                           System.StringComparison.OrdinalIgnoreCase) &&
                       (ca.Args ?? "").Trim() == (cb.Args ?? "").Trim();
            }

            // R11：choice 节点——选项数量、描述、各选项链结构必须一致。
            // 注意：ChoiceNode 不参与 Flatten/GetChildren 的 Seq/Par 包装逻辑，单独比较。
            if (a is ChoiceNode cha && b is ChoiceNode chb)
            {
                if (cha.Options.Count != chb.Options.Count) return false;
                for (int i = 0; i < cha.Options.Count; i++)
                {
                    if ((cha.Options[i].Text ?? "").Trim() != (chb.Options[i].Text ?? "").Trim())
                        return false;
                    if (!AreStructurallyEqual(cha.Options[i].Chain, chb.Options[i].Chain))
                        return false;
                }
                return true;
            }

            var childrenA = GetChildren(a);
            var childrenB = GetChildren(b);
            if (childrenA.Count != childrenB.Count) return false;

            for (int i = 0; i < childrenA.Count; i++)
                if (!AreStructurallyEqual(childrenA[i], childrenB[i])) return false;

            return true;
        }

        // ---------------- 内部实现 ----------------

        /// <summary>节点在父级中的位置——决定是否需要加括号。</summary>
        private enum ParentContext
        {
            /// <summary>整棵树的根（顶层永不加括号）</summary>
            Root,

            /// <summary>作为 Seq 的直接子项</summary>
            InSeq,

            /// <summary>作为 Par 的直接子项</summary>
            InPar,
        }

        private static void WriteNode(StringBuilder sb, ChainNode node, ParentContext context)
        {
            node = Flatten(node);
            if (node == null) return;

            if (node is CommandNode cmd)
            {
                sb.Append(FormatCommand(cmd));
                return;
            }

            // R11：choice 块——desc 与 chain 奇偶排列；描述含逗号/大括号时引号包裹。
            // 选项链以 Root 上下文序列化（链内自身的括号规则独立成立）。
            if (node is ChoiceNode choice)
            {
                sb.Append("choice{");
                for (int i = 0; i < choice.Options.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FormatChoiceText(choice.Options[i].Text ?? ""));
                    sb.Append(", ");
                    WriteNode(sb, choice.Options[i].Chain, ParentContext.Root);
                }
                sb.Append('}');
                return;
            }

            if (node is SeqNode seq)
            {
                // 规则 1：Par 的 Seq 子项必须加 []，否则 & 的高优先级会撕开分支
                bool needBrackets = context == ParentContext.InPar;

                if (needBrackets) sb.Append('[');
                for (int i = 0; i < seq.Children.Count; i++)
                {
                    if (i > 0) sb.Append(" -> ");
                    WriteNode(sb, seq.Children[i], ParentContext.InSeq);
                }
                if (needBrackets) sb.Append(']');
                return;
            }

            if (node is ParNode par)
            {
                // 规则 3：Seq 的 Par 子项不需括号（& 结合更紧）；
                // 但 Par 的 Par 子项（嵌套并行）需要括号以保持结构
                bool needBrackets = context == ParentContext.InPar;

                if (needBrackets) sb.Append('[');
                for (int i = 0; i < par.Children.Count; i++)
                {
                    if (i > 0) sb.Append(" & ");
                    WriteNode(sb, par.Children[i], ParentContext.InPar);
                }
                if (needBrackets) sb.Append(']');
            }
        }

        /// <summary>
        /// 输出 <c>name(args)</c>。无参数时仍写空括号 <c>name()</c>——
        /// 系统命令的"空参 = 隐式绑定"语义依赖这个形式。
        /// </summary>
        private static string FormatCommand(CommandNode cmd)
        {
            string name = (cmd.Name ?? "").Trim();
            string args = (cmd.Args ?? "").Trim();
            return name + "(" + args + ")";
        }

        /// <summary>
        /// R11：输出选项描述。含 <c>,</c> <c>{</c> <c>}</c> <c>"</c> 或首尾空白时必须
        /// 双引号包裹（转义内部引号），否则裸输出——保证能被 <see cref="ChainParser"/>
        /// 的块切分正确还原。
        /// </summary>
        private static string FormatChoiceText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            bool needsQuote = text.Length != text.Trim().Length;
            foreach (char c in text)
            {
                if (c == ',' || c == '{' || c == '}' || c == '"' || c == '\\')
                {
                    needsQuote = true;
                    break;
                }
            }

            if (!needsQuote) return text;

            var sb = new StringBuilder(text.Length + 2);
            sb.Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>剥掉单子项的 Seq/Par 包装（规则 2）。</summary>
        private static ChainNode Flatten(ChainNode node)
        {
            while (true)
            {
                if (node is SeqNode s && s.Children.Count == 1) { node = s.Children[0]; continue; }
                if (node is ParNode p && p.Children.Count == 1) { node = p.Children[0]; continue; }
                return node;
            }
        }

        private static List<ChainNode> GetChildren(ChainNode node)
        {
            if (node is SeqNode s) return s.Children;
            if (node is ParNode p) return p.Children;
            return new List<ChainNode>();
        }
    }
}
