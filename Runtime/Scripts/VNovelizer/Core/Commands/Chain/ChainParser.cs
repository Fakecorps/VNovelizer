using System.Collections.Generic;
using System.Linq;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>
    /// 命令链递归下降解析器。
    ///
    /// 语法（优先级：& 高于 ->）：
    ///   命令链     = 串行表达式
    ///   串行表达式 = 并行组 { "->" 并行组 }      // n 元平坦串行链
    ///   并行组     = 单元 { "&" 单元 }           // n 元平坦并行组
    ///   单元       = 命令 | "[" 命令链 "]"       // 分组可递归嵌套（建议 ≤2 层）
    ///
    /// 错误处理原则：
    ///   - 收集所有错误，不遇到第一个就停
    ///   - 错误恢复：跳过当前 Token 继续解析
    ///   - 每个错误带源字符串位置
    /// </summary>
    public static class ChainParser
    {
        /// <summary>建议的最大分组嵌套深度，超过时输出警告（不阻断）</summary>
        public const int MaxRecommendedDepth = 2;

        /// <summary>
        /// 流程控制命令集合：会改变当前行/剧本/场景的命令。
        /// 语义约定：必须是整条命令链的最后一个命令，
        /// 否则其后的命令会在"行已切换"的上下文中执行（演出污染/对象失效）。
        /// </summary>
        private static readonly HashSet<string> FlowCommands = new HashSet<string>
        {
            "jump", "choice", "loadscript", "loadscene"
        };

        /// <summary>
        /// 解析命令串。无论是否使用链式语法都返回解析树，
        /// 调用方根据 UsesChainSyntax 决定走新执行器还是旧逻辑。
        /// </summary>
        public static ChainParseResult Parse(string source)
        {
            var errors = new List<ChainError>();
            var result = new ChainParseResult { Errors = errors };

            if (string.IsNullOrEmpty(source))
                return result;

            var tokens = ChainLexer.Tokenize(source, errors);

            // 检测是否使用链式语法（引号外存在 -> 或 [）
            result.UsesChainSyntax = tokens.Any(t =>
                t.Type == ChainTokenType.Arrow || t.Type == ChainTokenType.LBracket);

            // 词法阶段无有效 Token（如空串或全部为空白）
            if (tokens.Count == 0)
                return result;

            int index = 0;
            int depth = 0;
            ChainNode root = ParseSeq(tokens, ref index, errors, ref depth);

            // 尾部剩余 Token 检查（正常应恰好消费完）
            if (index < tokens.Count)
            {
                errors.Add(new ChainError(
                    $"命令链末尾存在无法解析的内容: '{tokens[index].Text}'", tokens[index].Position));
            }

            result.Root = root;

            // 语义校验：流程命令位置 + playvideo 第二参数
            ValidateFlowCommands(root, result);

            return result;
        }

        /// <summary>
        /// 语义校验（产生警告，不阻断执行）：
        /// 1. 流程命令（jump/choice/loadscript/loadscene）必须是深度优先展开后的最后一个命令
        /// 2. playvideo 的"结束后命令"第二参数在链式语法下应改用 "-&gt;" 表达
        /// </summary>
        private static void ValidateFlowCommands(ChainNode root, ChainParseResult result)
        {
            if (root == null) return;

            var collected = new List<CommandNode>();
            ChainExecutor.CollectCommands(root, collected);
            if (collected.Count == 0) return;

            for (int i = 0; i < collected.Count; i++)
            {
                var cmd = collected[i];
                string name = cmd.Name.ToLower();

                // 校验 1：流程命令不在链尾
                if (FlowCommands.Contains(name) && i < collected.Count - 1)
                {
                    result.Warnings.Add(new ChainError(
                        $"流程命令 '{name}' 应位于命令链的最后一个位置——其后的命令会在行/剧本切换后的上下文中执行，可能产生演出污染",
                        cmd.Position));
                }

                // 校验 2：playvideo 第二参数携带流程命令
                if (name == "playvideo")
                {
                    int commaIndex = cmd.Args != null ? cmd.Args.IndexOf(',') : -1;
                    if (commaIndex >= 0)
                    {
                        string rest = cmd.Args.Substring(commaIndex + 1).ToLower();
                        if (rest.Contains("jump(") || rest.Contains("loadscript(") || rest.Contains("loadscene("))
                        {
                            result.Warnings.Add(new ChainError(
                                "链式语法下建议使用 '-&gt;' 代替 playvideo 的第二参数（如 playvideo(a.mp4) -&gt; jump(x)），避免双重流程语义",
                                cmd.Position));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 快速检测命令串是否使用链式语法（引号外含 -> 或 [）。
        /// 用于双轨切换的独立判断入口。
        /// </summary>
        public static bool UsesChainSyntax(string source)
        {
            if (string.IsNullOrEmpty(source)) return false;
            var tokens = ChainLexer.Tokenize(source, null);
            return tokens.Any(t =>
                t.Type == ChainTokenType.Arrow || t.Type == ChainTokenType.LBracket);
        }

        // ---------------- 串行表达式：并行组 { "->" 并行组 } ----------------

        private static ChainNode ParseSeq(List<ChainToken> tokens, ref int index,
            List<ChainError> errors, ref int depth)
        {
            var node = new SeqNode();
            node.Position = index < tokens.Count ? tokens[index].Position : 0;

            node.Children.Add(ParsePar(tokens, ref index, errors, ref depth));

            while (index < tokens.Count && tokens[index].Type == ChainTokenType.Arrow)
            {
                int arrowPos = tokens[index].Position;
                index++; // 消费 ->

                if (index >= tokens.Count)
                {
                    errors.Add(new ChainError("'->' 后缺少命令（悬空操作符）", arrowPos));
                    break;
                }

                node.Children.Add(ParsePar(tokens, ref index, errors, ref depth));
            }

            return node;
        }

        // ---------------- 并行组：单元 { "&" 单元 } ----------------

        private static ChainNode ParsePar(List<ChainToken> tokens, ref int index,
            List<ChainError> errors, ref int depth)
        {
            var node = new ParNode();
            node.Position = index < tokens.Count ? tokens[index].Position : 0;

            node.Children.Add(ParseUnit(tokens, ref index, errors, ref depth));

            while (index < tokens.Count && tokens[index].Type == ChainTokenType.Amp)
            {
                int ampPos = tokens[index].Position;
                index++; // 消费 &

                // '&' 后必须是单元（命令或 '['），不能是 -> / ] / 结尾
                if (index >= tokens.Count ||
                    tokens[index].Type == ChainTokenType.Arrow ||
                    tokens[index].Type == ChainTokenType.RBracket)
                {
                    errors.Add(new ChainError("'&' 后缺少命令（悬空操作符）", ampPos));
                    break;
                }

                node.Children.Add(ParseUnit(tokens, ref index, errors, ref depth));
            }

            return node;
        }

        // ---------------- 单元：命令 | "[" 命令链 "]" ----------------

        private static ChainNode ParseUnit(List<ChainToken> tokens, ref int index,
            List<ChainError> errors, ref int depth)
        {
            if (index >= tokens.Count)
            {
                errors.Add(new ChainError("预期命令或 '['，但已到达结尾", 0));
                return CreatePlaceholderCommand();
            }

            var token = tokens[index];

            if (token.Type == ChainTokenType.LBracket)
            {
                return ParseGroup(tokens, ref index, errors, ref depth);
            }

            if (token.Type == ChainTokenType.Command)
            {
                index++;
                return ParseCommandToken(token);
            }

            if (token.Type == ChainTokenType.RBracket)
            {
                errors.Add(new ChainError("多余的 ']'（未开启的分组）", token.Position));
                index++; // 错误恢复：跳过
                return ParseUnit(tokens, ref index, errors, ref depth); // 重试解析下一个单元
            }

            // 其他意外符号（如残留的 ->）——报错并跳过
            errors.Add(new ChainError($"预期命令或 '['，但得到 '{token.Text}'", token.Position));
            index++;
            return CreatePlaceholderCommand();
        }

        // ---------------- 分组："[" 命令链 "]" ----------------

        private static ChainNode ParseGroup(List<ChainToken> tokens, ref int index,
            List<ChainError> errors, ref int depth)
        {
            var lbracket = tokens[index];
            index++; // 消费 [
            depth++;

            if (depth > MaxRecommendedDepth)
            {
                errors.Add(new ChainError(
                    $"命令分组嵌套过深（{depth} 层，建议 ≤{MaxRecommendedDepth} 层），可读性差，建议拆分",
                    lbracket.Position));
            }

            // 空组检测：[]
            if (index < tokens.Count && tokens[index].Type == ChainTokenType.RBracket)
            {
                errors.Add(new ChainError("空的命令分组 '[]'", lbracket.Position));
                index++; // 消费 ]
                depth--;
                return CreatePlaceholderCommand();
            }

            var child = ParseSeq(tokens, ref index, errors, ref depth);

            if (index < tokens.Count && tokens[index].Type == ChainTokenType.RBracket)
            {
                index++; // 消费 ]
                depth--;
            }
            else
            {
                errors.Add(new ChainError("缺少闭合 ']'", lbracket.Position));
                // 错误恢复：不消费（可能是结尾），继续向上返回
                depth--;
            }

            return child;
        }

        // ---------------- 命令 Token → CommandNode ----------------

        private static CommandNode ParseCommandToken(ChainToken token)
        {
            var node = new CommandNode { Position = token.Position };

            int parenStart = token.Text.IndexOf('(');
            if (parenStart > 0)
            {
                node.Name = token.Text.Substring(0, parenStart).Trim();

                int parenEnd = token.Text.LastIndexOf(')');
                if (parenEnd > parenStart)
                    node.Args = token.Text.Substring(parenStart + 1, parenEnd - parenStart - 1);
                else
                    node.Args = token.Text.Substring(parenStart + 1); // 未闭合，保留原始内容
            }
            else
            {
                // 无括号命令（如裸 nextline）
                node.Name = token.Text;
                node.Args = "";
            }

            return node;
        }

        /// <summary>空占位命令（错误恢复用，执行时会被静默跳过）</summary>
        private static CommandNode CreatePlaceholderCommand()
        {
            return new CommandNode { Name = "", Args = "" };
        }
    }
}
