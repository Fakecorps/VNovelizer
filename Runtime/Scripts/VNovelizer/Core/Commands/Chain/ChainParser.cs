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
        ///
        /// 【2026-08-26 补全】条件跳转族（jumpif/jumpifnot/loadscriptif/loadscriptifnot）
        /// 同样改写行索引/剧本数据源，此前遗漏导致其非链尾时不报警告。
        /// 行演出编辑器的图结构校验（流程命令仅可连链尾）必须与本集合对齐。
        /// </summary>
        private static readonly HashSet<string> FlowCommands = new HashSet<string>
        {
            "jump", "jumpif", "jumpifnot",
            "loadscript", "loadscriptif", "loadscriptifnot",
            "choice", "loadscene"
        };

        /// <summary>
        /// 判断命令名是否为流程控制命令（大小写不敏感）。
        /// 行演出编辑器的图结构校验（"流程命令仅可连在链尾"）应调用本方法，
        /// 避免 Editor 侧另抄一份集合造成定义漂移。
        /// </summary>
        public static bool IsFlowCommand(string commandName)
        {
            return !string.IsNullOrEmpty(commandName) &&
                   FlowCommands.Contains(commandName.ToLower());
        }

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
            // R11：choice{...} 块语法同样启用链式语义（其内命令链由 ChainExecutor 独立执行）
            result.UsesChainSyntax = tokens.Any(t =>
                t.Type == ChainTokenType.Arrow || t.Type == ChainTokenType.LBracket ||
                t.Text.IndexOf('{') > 0);

            // 词法阶段无有效 Token（如空串或全部为空白）
            if (tokens.Count == 0)
                return result;

            int index = 0;
            int depth = 0;
            ChainNode root = ParseSeq(tokens, ref index, errors, result.Warnings, ref depth);

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
        /// 1. 流程命令（jump 族 / loadscript 族 / choice / loadscene，见 <see cref="FlowCommands"/>）
        ///    必须是其所在串行链的最后一个元素
        /// 2. playvideo 的"结束后命令"第二参数在链式语法下应改用 "-&gt;" 表达
        ///
        /// <para>
        /// <b>R11 结构感知</b>：choice 的选项链不是主链的一部分——未选中的选项链不会执行，
        /// 因此不参与"choice 之后还有命令"的展开序判定（旧 DFS 平铺会把全部选项链命令
        /// 排在 choice 之后造成误报）。选项链各自作为独立上下文递归校验。
        /// </para>
        /// </summary>
        private static void ValidateFlowCommands(ChainNode root, ChainParseResult result)
        {
            if (root == null) return;
            ValidateFlowRecursive(root, result, isTail: true);
        }

        /// <summary>
        /// 结构感知递归。<paramref name="isTail"/>：本节点是否是其所在串行链的最后一个元素
        /// （尾元素之后的命令不存在，流程命令在链尾合法）。
        /// </summary>
        private static void ValidateFlowRecursive(ChainNode node, ChainParseResult result, bool isTail)
        {
            if (node == null) return;

            switch (node)
            {
                case SeqNode seq:
                    for (int i = 0; i < seq.Children.Count; i++)
                        ValidateFlowRecursive(seq.Children[i], result,
                            isTail && i == seq.Children.Count - 1);
                    break;

                case ParNode par:
                    // 并行分支间无先后：分支的"链尾性"沿用 Par 整体在父链中的位置。
                    // （Par 之后的兄弟命令须等全部分支完成才执行，分支内流程命令
                    //   改变行号后其并行分支在"行已切换"上下文收尾，与原平铺判定
                    //   对非尾 Par 的语义一致。）
                    foreach (var child in par.Children)
                        ValidateFlowRecursive(child, result, isTail);
                    break;

                case ChoiceNode choice:
                    // 校验 1-choice：choice 必须位于其所在串行链的末尾
                    //（其后主链命令会在选项面板弹出前执行，演出污染）。
                    if (!isTail)
                    {
                        result.Warnings.Add(new ChainError(
                            "流程命令 'choice' 应位于命令链的最后一个位置——其后的命令会在选项面板弹出前执行，可能产生演出污染",
                            choice.Position));
                    }
                    // 选项链独立上下文：链尾无后继，内部命令按自身结构校验。
                    foreach (var opt in choice.Options)
                        ValidateFlowRecursive(opt.Chain, result, isTail: true);
                    break;

                case CommandNode cmd:
                    {
                        string name = cmd.Name.ToLower();

                        // 校验 1：流程命令不在链尾
                        if (FlowCommands.Contains(name) && !isTail)
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
                                // 【2026-08-26】改为遍历 FlowCommands 集合，与校验 1 共用同一份定义
                                // （此前硬编码 jump/loadscript/loadscene 三个，漏掉条件跳转族）
                                bool carriesFlowCommand = false;
                                foreach (var flow in FlowCommands)
                                {
                                    if (rest.Contains(flow + "("))
                                    {
                                        carriesFlowCommand = true;
                                        break;
                                    }
                                }

                                if (carriesFlowCommand)
                                {
                                    result.Warnings.Add(new ChainError(
                                        "链式语法下建议使用 '-&gt;' 代替 playvideo 的第二参数（如 playvideo(a.mp4) -&gt; jump(x)），避免双重流程语义",
                                        cmd.Position));
                                }
                            }
                        }
                    }
                    break;
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
            List<ChainError> errors, List<ChainError> warnings, ref int depth)
        {
            var node = new SeqNode();
            node.Position = index < tokens.Count ? tokens[index].Position : 0;

            node.Children.Add(ParsePar(tokens, ref index, errors, warnings, ref depth));

            while (index < tokens.Count && tokens[index].Type == ChainTokenType.Arrow)
            {
                int arrowPos = tokens[index].Position;
                index++; // 消费 ->

                if (index >= tokens.Count)
                {
                    errors.Add(new ChainError("'->' 后缺少命令（悬空操作符）", arrowPos));
                    break;
                }

                node.Children.Add(ParsePar(tokens, ref index, errors, warnings, ref depth));
            }

            return node;
        }

        // ---------------- 并行组：单元 { "&" 单元 } ----------------

        private static ChainNode ParsePar(List<ChainToken> tokens, ref int index,
            List<ChainError> errors, List<ChainError> warnings, ref int depth)
        {
            var node = new ParNode();
            node.Position = index < tokens.Count ? tokens[index].Position : 0;

            node.Children.Add(ParseUnit(tokens, ref index, errors, warnings, ref depth));

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

                node.Children.Add(ParseUnit(tokens, ref index, errors, warnings, ref depth));
            }

            return node;
        }

        // ---------------- 单元：命令 | "[" 命令链 "]" ----------------

        private static ChainNode ParseUnit(List<ChainToken> tokens, ref int index,
            List<ChainError> errors, List<ChainError> warnings, ref int depth)
        {
            if (index >= tokens.Count)
            {
                errors.Add(new ChainError("预期命令或 '['，但已到达结尾", 0));
                return CreatePlaceholderCommand();
            }

            var token = tokens[index];

            if (token.Type == ChainTokenType.LBracket)
            {
                return ParseGroup(tokens, ref index, errors, warnings, ref depth);
            }

            if (token.Type == ChainTokenType.Command)
            {
                index++;
                return ParseCommandToken(token, errors, warnings);
            }

            if (token.Type == ChainTokenType.RBracket)
            {
                errors.Add(new ChainError("多余的 ']'（未开启的分组）", token.Position));
                index++; // 错误恢复：跳过
                return ParseUnit(tokens, ref index, errors, warnings, ref depth); // 重试解析下一个单元
            }

            // 其他意外符号（如残留的 ->）——报错并跳过
            errors.Add(new ChainError($"预期命令或 '['，但得到 '{token.Text}'", token.Position));
            index++;
            return CreatePlaceholderCommand();
        }

        // ---------------- 分组："[" 命令链 "]" ----------------

        private static ChainNode ParseGroup(List<ChainToken> tokens, ref int index,
            List<ChainError> errors, List<ChainError> warnings, ref int depth)
        {
            var lbracket = tokens[index];
            index++; // 消费 [
            depth++;

            // 【2026-08-26 修正】嵌套过深属"建议拆分"性质的提示，此前误入 Errors 导致
            // ChainParseResult.Success == false + 上层 Debug.LogError；且行演出编辑器若以
            // Success 作为保存闸门，3 层嵌套将无法保存。规范（§7.5）明确其为警告不阻断。
            if (depth > MaxRecommendedDepth)
            {
                warnings.Add(new ChainError(
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

            var child = ParseSeq(tokens, ref index, errors, warnings, ref depth);

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

        // ---------------- 命令 Token → CommandNode / ChoiceNode ----------------

        private static ChainNode ParseCommandToken(ChainToken token,
            List<ChainError> errors, List<ChainError> warnings)
        {
            // R11：choice{...} 块语法（大括号平衡已由 Lexer 保证，整个块是一个 Token）。
            // 块内「desc, chain」奇偶配对；chain 递归 Parse，天然支持嵌套 choice 与完整链语法。
            int curlyStart = token.Text.IndexOf('{');
            if (curlyStart > 0 && token.Text.TrimEnd().EndsWith("}"))
            {
                string name = token.Text.Substring(0, curlyStart).Trim();
                string body = token.Text.Substring(curlyStart + 1, token.Text.Length - curlyStart - 2);

                if (name.ToLower() == "choice")
                    return ParseChoiceBlock(body, token.Position, curlyStart, errors, warnings);

                errors.Add(new ChainError(
                    $"命令 '{name}' 不支持大括号块语法（仅 choice 支持）", token.Position + curlyStart));
                return CreatePlaceholderCommand();
            }

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

        // ---------------- choice{...} 块解析 ----------------

        /// <summary>
        /// 解析 choice 块内容。块内语法：<c>desc1, chain1, desc2, chain2, ...</c>——
        /// 逗号分隔、奇偶交替（描述 → 命令链）。描述含逗号/大括号时必须用双引号包裹。
        /// 命令链段递归 <see cref="Parse"/>，支持完整链语法与嵌套 choice{...}。
        /// </summary>
        /// <param name="body">大括号内的原文（不含大括号）</param>
        /// <param name="tokenPosition">choice Token 的源偏移</param>
        /// <param name="curlyOffset">'{' 在 Token 内的偏移（块内文本起点 = tokenPosition + curlyOffset + 1）</param>
        private static ChainNode ParseChoiceBlock(string body, int tokenPosition, int curlyOffset,
            List<ChainError> errors, List<ChainError> warnings)
        {
            var node = new ChoiceNode { Position = tokenPosition };

            var segments = SplitChoiceBody(body, errors, tokenPosition + curlyOffset + 1);

            if (segments.Count == 0)
            {
                errors.Add(new ChainError("choice 块不能为空（至少需要一个选项）", tokenPosition));
                return node;
            }

            if (segments.Count % 2 == 1)
            {
                errors.Add(new ChainError(
                    $"choice 块的最后一个选项缺少命令链：'{segments[segments.Count - 1].Text.Trim()}'",
                    tokenPosition + curlyOffset + 1 + segments[segments.Count - 1].Offset));
            }

            int pairCount = segments.Count / 2;
            for (int i = 0; i < pairCount; i++)
            {
                var descSeg = segments[i * 2];
                var chainSeg = segments[i * 2 + 1];

                var opt = new ChoiceOption
                {
                    Text = UnquoteChoiceText(descSeg.Text.Trim(),
                        errors, tokenPosition + curlyOffset + 1 + descSeg.Offset),
                };

                string chainText = chainSeg.Text.Trim();
                if (!string.IsNullOrEmpty(chainText))
                {
                    // 递归解析选项链（其内嵌套 choice 自然递归）
                    int chainBase = tokenPosition + curlyOffset + 1 + chainSeg.Offset;
                    var sub = Parse(chainText);
                    ShiftPositions(sub.Root, chainBase);

                    foreach (var e in sub.Errors)
                        errors.Add(new ChainError(e.Message, chainBase + e.Position));
                    foreach (var w in sub.Warnings)
                        warnings.Add(new ChainError(w.Message, chainBase + w.Position));

                    opt.Chain = sub.Root;
                }

                node.Options.Add(opt);
            }

            return node;
        }

        /// <summary>块内切段结果：文本 + 相对块内容起点的偏移。</summary>
        private struct ChoiceSegment
        {
            public string Text;
            public int Offset;
        }

        /// <summary>
        /// 按顶层逗号切分 choice 块内容。引号、圆括号、方括号、大括号感知——
        /// 嵌套 choice{...} 与命令参数内的逗号不会被切散。
        /// </summary>
        private static List<ChoiceSegment> SplitChoiceBody(string body, List<ChainError> errors, int basePosition)
        {
            var segments = new List<ChoiceSegment>();
            if (string.IsNullOrEmpty(body)) return segments;

            var sb = new System.Text.StringBuilder();
            bool inQuote = false;
            int paren = 0, bracket = 0, curly = 0;
            int segStart = 0;

            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];

                if (inQuote)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < body.Length) { sb.Append(body[i + 1]); i++; }
                    else if (c == '"') inQuote = false;
                    continue;
                }

                if (c == '"') { inQuote = true; sb.Append(c); continue; }

                if (c == '(') { paren++; sb.Append(c); continue; }
                if (c == ')') { paren = System.Math.Max(0, paren - 1); sb.Append(c); continue; }
                if (c == '[') { bracket++; sb.Append(c); continue; }
                if (c == ']') { bracket = System.Math.Max(0, bracket - 1); sb.Append(c); continue; }
                if (c == '{') { curly++; sb.Append(c); continue; }
                if (c == '}') { curly = System.Math.Max(0, curly - 1); sb.Append(c); continue; }

                if (c == ',' && paren == 0 && bracket == 0 && curly == 0)
                {
                    segments.Add(new ChoiceSegment { Text = sb.ToString(), Offset = segStart });
                    sb.Length = 0;
                    segStart = i + 1;
                    continue;
                }

                sb.Append(c);
            }

            segments.Add(new ChoiceSegment { Text = sb.ToString(), Offset = segStart });
            return segments;
        }

        /// <summary>
        /// 解析选项描述：双引号包裹 → 去引号并还原转义；裸文本原样返回。
        /// 引号未闭合时报错（不阻断，返回已读内容）。
        /// </summary>
        private static string UnquoteChoiceText(string text, List<ChainError> errors, int position)
        {
            if (text.Length < 2 || !text.StartsWith("\"") || !text.EndsWith("\""))
                return text;

            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < text.Length - 1; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length - 1)
                {
                    sb.Append(text[i + 1]);
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>把子树内全部节点的源偏移平移 <paramref name="delta"/>（递归选项链的偏移修正）。</summary>
        private static void ShiftPositions(ChainNode node, int delta)
        {
            if (node == null || delta == 0) return;

            node.Position += delta;

            if (node is SeqNode seq)
            {
                foreach (var child in seq.Children) ShiftPositions(child, delta);
            }
            else if (node is ParNode par)
            {
                foreach (var child in par.Children) ShiftPositions(child, delta);
            }
            else if (node is ChoiceNode choice)
            {
                foreach (var opt in choice.Options) ShiftPositions(opt.Chain, delta);
            }
        }

        /// <summary>空占位命令（错误恢复用，执行时会被静默跳过）</summary>
        private static CommandNode CreatePlaceholderCommand()
        {
            return new CommandNode { Name = "", Args = "" };
        }
    }
}
