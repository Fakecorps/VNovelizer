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

            for (int i = 0; i < tokens.Count; i++)
            {
                var tok = tokens[i];
                string indent = new string(' ', depth * 4);

                switch (tok.Type)
                {
                    case ChainTokenType.Command:
                        sb.Append(indent).Append(tok.Text.Trim());
                        // 看下一个 token：& / -> 附加到行尾；] / 结尾 不附加
                        if (i + 1 < tokens.Count)
                        {
                            var next = tokens[i + 1];
                            if (next.Type == ChainTokenType.Amp) sb.Append(" &");
                            else if (next.Type == ChainTokenType.Arrow) sb.Append(" ->");
                        }
                        sb.Append('\n');
                        break;

                    case ChainTokenType.LBracket:
                        sb.Append(indent).Append('[').Append('\n');
                        depth++;
                        break;

                    case ChainTokenType.RBracket:
                        depth = System.Math.Max(0, depth - 1);
                        indent = new string(' ', depth * 4);
                        sb.Append(indent).Append(']');
                        // 看下一个 token：& / -> 附加到行尾；否则不附加
                        if (i + 1 < tokens.Count)
                        {
                            var next = tokens[i + 1];
                            if (next.Type == ChainTokenType.Amp) sb.Append(" &");
                            else if (next.Type == ChainTokenType.Arrow) sb.Append(" ->");
                        }
                        sb.Append('\n');
                        break;

                    // Amp / Arrow 已在前一 Command / ] 行尾附加，跳过
                    case ChainTokenType.Amp:
                    case ChainTokenType.Arrow:
                        break;
                }
            }

            // 去掉末尾换行
            while (sb.Length > 0 && sb[sb.Length - 1] == '\n')
                sb.Length--;
            return sb.ToString();
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
