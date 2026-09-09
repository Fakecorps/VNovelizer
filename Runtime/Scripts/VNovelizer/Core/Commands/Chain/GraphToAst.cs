using System.Collections.Generic;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>
    /// 图 → AST 转换器：把用户自由连线画出的 SP 图还原为 fork-join 树
    /// （<see cref="ChainParser"/> 的对偶方向）。
    ///
    /// <para>
    /// <b>为何需要独立组件</b>（决策 d5）：<c>ChainGraphValidator</c> 只判定图**是否**
    /// 合法 SP 图、不产出 AST；<c>ChainSerializer</c> 只管 AST → 文本。
    /// 中间"图 → AST"这一环若缺失，序列化器就没有输入。三者职责单一：
    /// 校验 / 分解 / 序列化。
    /// </para>
    ///
    /// <para>
    /// <b>算法</b>（递归 SP 分解）：从起点沿唯一后继前进，遇 Fork 则对每条分支
    /// 递归分解至共同 Join，Join 之后继续主链。Fork/Join 必须配对——
    /// 这由校验器的致命规则保证，本组件只处理已验证的合法图，
    /// 但仍带防御性检查（返回 null 而非抛异常）。
    /// </para>
    /// </summary>
    public static class GraphToAst
    {
        /// <summary>转换结果：AST 根 + 错误列表。</summary>
        public class Result
        {
            /// <summary>分解出的 AST 根。空图（该行无命令）时为 null，此时仍算成功。</summary>
            public ChainNode Root;

            public List<string> Errors = new List<string>();

            /// <summary>
            /// 是否分解成功。
            /// <b>注意</b>：判据只看 <see cref="Errors"/>，不要求 <see cref="Root"/> 非空——
            /// 空图是合法状态（该行 Command 列为空），若把它算作失败，
            /// 调用方会把"没有命令"误报成"分解出错"。
            /// </summary>
            public bool Success => Errors.Count == 0;
        }

        /// <summary>递归深度上限：防御畸形图导致栈溢出（正常图不可能达到）。</summary>
        private const int MaxRecursionDepth = 64;

        /// <summary>
        /// 将图转换为 AST。图必须先通过 <c>ChainGraphValidator</c> 的致命规则校验。
        /// </summary>
        public static Result Convert(ChainGraph graph)
        {
            var result = new Result();

            if (graph == null || graph.NodeCount == 0)
            {
                // 空图 = 空命令链，属合法状态（该行没有命令）
                result.Root = null;
                return result;
            }

            // 2026-08-28：哨兵感知——编辑器中的图恒含 Start/End 哨兵。
            // 仅剩哨兵（哨兵间无连接）= 空链，同样合法，先于起终点判定短路。
            if (!ChainGraphDumper.HasContent(graph))
            {
                result.Root = null;
                return result;
            }

            var startSentinel = ChainGraphDumper.FindStartSentinel(graph);
            string startId;
            if (startSentinel != null)
            {
                startId = startSentinel.Id;
            }
            else
            {
                var sources = graph.FindSources();
                if (sources.Count != 1)
                {
                    result.Errors.Add(sources.Count == 0
                        ? "图中不存在起点（可能存在环）"
                        : $"图中存在 {sources.Count} 个起点，命令链必须有唯一起点");
                    return result;
                }
                startId = sources[0].Id;
            }

            // 终点检查（哨兵感知）：有 End 哨兵时非哨兵 sink 数必须为 0；
            // 无哨兵时（Runtime 兼容路径）维持唯一 sink 判定。
            // R11：choice 豁免——链尾 choice 节点自身、选项链尾（含未连 End 的留空）
            // 都是合法 sink，不阻断分解（Validator 层负责警告提示）。
            var straySinks = new List<string>();
            bool hasEndSentinel = false;
            foreach (var s in graph.FindSinks())
            {
                if (s.Kind == ChainGraphNodeKind.End) { hasEndSentinel = true; continue; }
                if (s.Kind == ChainGraphNodeKind.Start) continue; // 空链时 Start 也是 sink
                if (s.Kind == ChainGraphNodeKind.Choice) continue; // R11：链尾 choice（主延续缺失）
                if (IsReachableFromChoicePort(graph, s.Id)) continue; // R11：选项链尾留空
                straySinks.Add(s.Id);
            }

            if (hasEndSentinel)
            {
                if (straySinks.Count > 0)
                {
                    result.Errors.Add(
                        $"图中存在 {straySinks.Count} 个终点，命令链必须有唯一终点（请用 JOIN 汇合）");
                    return result;
                }
            }
            else
            {
                // R11：无哨兵路径同样豁免 choice 产生的多 sink。
                // 主链终点 = 主链可达集合内的 sink + 主延续边缺失的主链 choice。
                // 选项链内节点（含嵌套 choice）经选项端口可达，不属于主链——
                // 否则「嵌套 choice 在选项链尾」会被误算成第二个主链终点。
                var mainChain = CollectMainChainNodes(graph, startId);

                int mainEnds = 0;
                foreach (var s in graph.FindSinks())
                {
                    if (s.Kind == ChainGraphNodeKind.Choice) continue;
                    if (!mainChain.Contains(s.Id)) continue;
                    mainEnds++;
                }

                foreach (var n in graph.Nodes)
                {
                    if (n.Kind != ChainGraphNodeKind.Choice) continue;
                    if (!mainChain.Contains(n.Id)) continue;
                    bool hasMain = false;
                    foreach (var pair in graph.GetOrderedSuccessors(n.Id))
                    {
                        if (pair.Key == ChoicePort.Main) { hasMain = true; break; }
                    }
                    if (!hasMain) mainEnds++;
                }

                if (mainEnds != 1)
                {
                    result.Errors.Add(mainEnds == 0
                        ? "图中不存在终点（可能存在环）"
                        : $"图中存在 {mainEnds} 个终点，命令链必须有唯一终点");
                    return result;
                }
            }

            var ctx = new Context(graph, result);
            var seq = ParseSequence(ctx, startId, null, 0);

            if (!result.Success) return result;

            // 2026-08-28：完整性检查——孤立/悬空节点不在主链上，若静默跳过
            // 会造成"保存的命令链悄悄丢命令"，必须显式报错。
            foreach (var n in graph.Nodes)
            {
                if (n.Kind == ChainGraphNodeKind.Start || n.Kind == ChainGraphNodeKind.End) continue;
                if (!ctx.Visited.Contains(n.Id))
                    result.Errors.Add($"节点 {n} 未连入主链（悬空或未连接），无法分解");
            }

            if (!result.Success) return result;

            result.Root = Normalize(seq);
            return result;
        }

        /// <summary>
        /// 解析 Fork/Join 配对与影响范围（行演出编辑器选中高亮用，2026-09-03 新增）。
        ///
        /// <para>
        /// 传入 Fork：<paramref name="forkId"/> = 该节点，<paramref name="joinId"/> = 各分支
        /// 共同汇聚的配对 Join。传入 Join：反向遍历全部 Fork 找出配对的 Fork。
        /// <paramref name="rangeNodeIds"/> 返回闭包内全部节点 ID（含 Fork/Join 两端与
        /// 嵌套 Fork/Join 内部的节点），UI 层用其给中间节点与配对端点加「范围高亮」，
        /// 被选中的触发者自身保持选中样式。
        /// </para>
        /// </summary>
        /// <returns>是否成功配对（Fork/Join 未配对或畸形图返回 false）</returns>
        public static bool TryResolveForkJoinRange(ChainGraph graph, string nodeId,
            out string forkId, out string joinId, out HashSet<string> rangeNodeIds)
        {
            forkId = null;
            joinId = null;
            rangeNodeIds = null;

            if (graph == null || string.IsNullOrEmpty(nodeId)) return false;
            var node = graph.GetNode(nodeId);
            if (node == null) return false;

            if (node.Kind == ChainGraphNodeKind.Fork)
            {
                forkId = nodeId;
                joinId = FindMatchingJoin(graph, nodeId);
            }
            else if (node.Kind == ChainGraphNodeKind.Join)
            {
                joinId = nodeId;
                foreach (var n in graph.Nodes)
                {
                    if (n.Kind != ChainGraphNodeKind.Fork) continue;
                    if (FindMatchingJoin(graph, n.Id) == nodeId)
                    {
                        forkId = n.Id;
                        break;
                    }
                }
            }
            else
            {
                return false; // 非 Fork/Join 不构成范围
            }

            if (string.IsNullOrEmpty(forkId) || string.IsNullOrEmpty(joinId)) return false;

            // 闭包收集：从 Fork 各分支起点 DFS 直到配对 Join（含），嵌套内部节点全部入集。
            // visited 防畸形图（环）死循环——校验器已保证合法图，此处仅防御。
            rangeNodeIds = new HashSet<string> { forkId, joinId };
            var stack = new Stack<string>();
            var seen = new HashSet<string>();

            foreach (string branchStart in graph.GetSuccessors(forkId))
                stack.Push(branchStart);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                if (current == null || !seen.Add(current)) continue;
                if (current == joinId) continue; // 已入集，不越过配对 Join

                rangeNodeIds.Add(current);
                foreach (string next in graph.GetSuccessors(current))
                    stack.Push(next);
            }

            return true;
        }

        private class Context
        {
            public readonly ChainGraph Graph;
            public readonly Result Result;

            /// <summary>已访问节点（检测重复访问——畸形图的兜底；哨兵豁免）</summary>
            public readonly HashSet<string> Visited = new HashSet<string>();

            public Context(ChainGraph graph, Result result)
            {
                Graph = graph;
                Result = result;
            }
        }

        /// <summary>
        /// R11：把 choice 节点第 <paramref name="portIndex"/> 个选项连出的链
        /// 单独序列化为命令链文本（Inspector 只读预览用）。空链/分解失败返回空串或占位。
        /// </summary>
        public static string SerializeChoiceOptionChain(ChainGraph graph, string choiceId, int portIndex)
        {
            if (graph == null || string.IsNullOrEmpty(choiceId)) return "";

            string startId = null;
            foreach (var pair in graph.GetOrderedSuccessors(choiceId))
            {
                if (pair.Key == portIndex)
                {
                    startId = pair.Value;
                    break;
                }
            }
            if (startId == null) return ""; // 空链

            var result = new Result();
            var ctx = new Context(graph, result);
            var seq = ParseSequence(ctx, startId, null, 0);
            if (!result.Success) return "(选项链无法分解)";

            return ChainSerializer.Serialize(Normalize(seq));
        }

        /// <summary>
        /// R11：分解图中的一个 Choice 节点为 <see cref="ChoiceNode"/>。
        /// 每个选项端口（PortIndex ≥ 0）递归分解出一条独立的命令链；
        /// 未连线的端口 = 空链（点击后直接进入下一行）。
        /// </summary>
        private static ChoiceNode ParseChoiceNode(Context ctx, ChainGraphNode node, int depth)
        {
            var choice = new ChoiceNode { Position = node.SourcePosition };

            var texts = node.ChoiceTexts;
            int optionCount = texts != null ? texts.Count : 0;

            foreach (var pair in ctx.Graph.GetOrderedSuccessors(node.Id))
            {
                if (pair.Key < 0) continue; // 主延续边由主链 ParseSequence 消费

                var opt = new ChoiceOption
                {
                    Text = pair.Key < optionCount ? (texts[pair.Key] ?? "") : "",
                };

                var branch = ParseSequence(ctx, pair.Value, null, depth + 1);
                if (!ctx.Result.Success) return choice;

                opt.Chain = Normalize(branch);
                choice.Options.Add(opt);
            }

            // 选项文本数多于已连线端口（空链选项）：补齐为独立空链选项。
            for (int i = choice.Options.Count; i < optionCount; i++)
            {
                choice.Options.Add(new ChoiceOption { Text = texts[i] ?? "", Chain = null });
            }

            return choice;
        }

        /// <summary>
        /// R11：主链可达集合——从起点 BFS，choice 节点只沿主延续边（Port=Main）扩展，
        /// 普通节点沿全部出边扩展。选项链内节点（含嵌套 choice）不在集合中。
        /// 公开（public）供编辑器 RowGraphView 复用做「nextline 模板节点可见性」判断。
        /// </summary>
        public static HashSet<string> CollectMainChainNodes(ChainGraph graph, string startId)
        {
            var reachable = new HashSet<string>();
            if (string.IsNullOrEmpty(startId)) return reachable;

            var queue = new Queue<string>();
            queue.Enqueue(startId);
            reachable.Add(startId);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                var node = graph.GetNode(id);
                if (node == null) continue;

                foreach (var pair in graph.GetOrderedSuccessors(id))
                {
                    // choice 节点只沿主延续边进入主链（选项端口是独立分支）
                    if (node.Kind == ChainGraphNodeKind.Choice && pair.Key != ChoicePort.Main)
                        continue;
                    if (reachable.Add(pair.Value))
                        queue.Enqueue(pair.Value);
                }
            }

            return reachable;
        }

        /// <summary>
        /// R11：从任一 Choice 节点的任一选项端口出发能否到达 <paramref name="sinkId"/>。
        /// 用于豁免"选项链尾未连 End"的 sink 检查（合法 = 空链语义）。
        /// </summary>
        private static bool IsReachableFromChoicePort(ChainGraph graph, string sinkId)
        {
            if (graph == null || string.IsNullOrEmpty(sinkId)) return false;

            var queue = new Queue<string>();
            var seen = new HashSet<string>();

            foreach (var n in graph.Nodes)
            {
                if (n.Kind != ChainGraphNodeKind.Choice) continue;
                foreach (var pair in graph.GetOrderedSuccessors(n.Id))
                {
                    if (pair.Key < 0) continue; // 只走选项端口
                    queue.Enqueue(pair.Value);
                    seen.Add(pair.Value);
                }
            }

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (id == sinkId) return true;
                foreach (string next in graph.GetSuccessors(id))
                    if (seen.Add(next)) queue.Enqueue(next);
            }

            return false;
        }

        /// <summary>
        /// 从 <paramref name="startId"/> 沿链前进，直到遇到 <paramref name="stopAtId"/>
        /// （不含）或链尾，返回串行节点。
        /// </summary>
        private static SeqNode ParseSequence(Context ctx, string startId, string stopAtId, int depth)
        {
            var seq = new SeqNode();

            if (depth > MaxRecursionDepth)
            {
                ctx.Result.Errors.Add("图结构嵌套过深或存在环，分解中止");
                return seq;
            }

            string current = startId;

            while (!string.IsNullOrEmpty(current) && current != stopAtId)
            {
                var node = ctx.Graph.GetNode(current);
                if (node == null)
                {
                    ctx.Result.Errors.Add($"引用了不存在的节点：{current}");
                    return seq;
                }

                // R11：哨兵豁免重复访问——choice 的多条选项链尾都连 End 哨兵，
                // 主链/首条选项链分解时已访问过 End，后续分支再遇不应报"重复访问"。
                bool isSentinel = node.Kind == ChainGraphNodeKind.Start ||
                                  node.Kind == ChainGraphNodeKind.End;
                if (!isSentinel && !ctx.Visited.Add(current))
                {
                    ctx.Result.Errors.Add($"节点被重复访问（图中存在环或 Fork/Join 不配对）：{node}");
                    return seq;
                }

                if (node.Kind == ChainGraphNodeKind.Choice)
                {
                    seq.Children.Add(ParseChoiceNode(ctx, node, depth + 1));
                    if (!ctx.Result.Success) return seq;

                    // 主延续边（PortIndex=-1）：连 End 哨兵或下一个 choice。
                    // 缺失 = 主链在 choice 处结束（链尾）。
                    string mainNext = null;
                    foreach (var pair in ctx.Graph.GetOrderedSuccessors(node.Id))
                    {
                        if (pair.Key == ChoicePort.Main)
                        {
                            mainNext = pair.Value;
                            break;
                        }
                    }
                    current = mainNext;
                    continue;
                }

                if (node.Kind == ChainGraphNodeKind.Fork)
                {
                    string joinId = FindMatchingJoin(ctx.Graph, node.Id);
                    if (joinId == null)
                    {
                        ctx.Result.Errors.Add($"FORK 节点 {node.Id} 找不到配对的 JOIN");
                        return seq;
                    }

                    var par = new ParNode();
                    foreach (string branchStart in ctx.Graph.GetSuccessors(node.Id))
                    {
                        var branch = ParseSequence(ctx, branchStart, joinId, depth + 1);
                        if (!ctx.Result.Success) return seq;
                        par.Children.Add(Normalize(branch));
                    }

                    if (par.Children.Count == 0)
                    {
                        ctx.Result.Errors.Add($"FORK 节点 {node.Id} 没有任何分支");
                        return seq;
                    }

                    seq.Children.Add(par);

                    // Join 本身被消费掉（它不产生命令），从其唯一后继继续主链
                    ctx.Visited.Add(joinId);
                    var afterJoin = ctx.Graph.GetSuccessors(joinId);
                    current = afterJoin.Count > 0 ? afterJoin[0] : null;
                    continue;
                }

                if (node.Kind == ChainGraphNodeKind.Join)
                {
                    // 正常流程中 Join 由 Fork 分支处理时作为 stopAt 消费，
                    // 直接走到这里说明 Join 缺少配对的 Fork
                    ctx.Result.Errors.Add($"JOIN 节点 {node.Id} 找不到配对的 FORK");
                    return seq;
                }

                if (node.Kind == ChainGraphNodeKind.Command)
                {
                    seq.Children.Add(new CommandNode
                    {
                        Name = node.CommandName,
                        Args = node.Args ?? "",
                    });
                }
                // Start / End 是哨兵，不产生命令，直接跳过

                var successors = ctx.Graph.GetSuccessors(current);
                if (successors.Count == 0) break;

                if (successors.Count > 1)
                {
                    ctx.Result.Errors.Add(
                        $"节点 {node} 有 {successors.Count} 条出边，并行必须显式使用 FORK 节点");
                    return seq;
                }

                current = successors[0];
            }

            return seq;
        }

        /// <summary>
        /// 找出 Fork 各分支的共同汇聚 Join。
        ///
        /// 做法：从第一条分支出发沿链前进，记录路径上遇到的所有 Join（按顺序）；
        /// 再检查其余分支能否到达其中之一，取**最早**能被全部分支到达的那个。
        /// 嵌套 Fork 的内层 Join 会先出现在路径上，但内层 Join 只属内层分支，
        /// 无法被本 Fork 的其余分支到达，因此自然被排除。
        /// </summary>
        private static string FindMatchingJoin(ChainGraph graph, string forkId)
        {
            var branches = graph.GetSuccessors(forkId);
            if (branches.Count == 0) return null;

            // 候选：第一条分支路径上的所有 Join（按遇到顺序）
            var candidates = CollectJoinsAlongPath(graph, branches[0]);
            if (candidates.Count == 0) return null;

            foreach (string candidate in candidates)
            {
                bool reachableFromAll = true;
                for (int i = 1; i < branches.Count; i++)
                {
                    if (!CanReach(graph, branches[i], candidate))
                    {
                        reachableFromAll = false;
                        break;
                    }
                }
                if (reachableFromAll) return candidate;
            }

            return null;
        }

        /// <summary>沿单一路径前进，收集遇到的 Join 节点（遇分支时取第一条继续）。</summary>
        private static List<string> CollectJoinsAlongPath(ChainGraph graph, string startId)
        {
            var joins = new List<string>();
            var seen = new HashSet<string>();
            string current = startId;
            int guard = 0;

            while (!string.IsNullOrEmpty(current) && seen.Add(current) && guard++ < 1024)
            {
                var node = graph.GetNode(current);
                if (node == null) break;

                if (node.Kind == ChainGraphNodeKind.Join) joins.Add(current);

                var succ = graph.GetSuccessors(current);
                if (succ.Count == 0) break;
                current = succ[0];
            }

            return joins;
        }

        /// <summary>从 <paramref name="fromId"/> 是否可达 <paramref name="targetId"/>（BFS）。</summary>
        private static bool CanReach(ChainGraph graph, string fromId, string targetId)
        {
            var queue = new Queue<string>();
            var seen = new HashSet<string>();
            queue.Enqueue(fromId);
            seen.Add(fromId);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (id == targetId) return true;

                foreach (string next in graph.GetSuccessors(id))
                    if (seen.Add(next)) queue.Enqueue(next);
            }

            return false;
        }

        /// <summary>
        /// 归一化：剥掉只有一个子项的 <see cref="SeqNode"/> / <see cref="ParNode"/> 包装。
        ///
        /// <para>
        /// 必要性：单子项的 Seq/Par 在语义上等价于其子项本身，但会在序列化时
        /// 产生多余的 <c>[]</c>（如 <c>wait(1)</c> 变成 <c>[wait(1)]</c>），
        /// 往复几次转换后括号层层累积，直撞 <c>ChainParser.MaxRecommendedDepth</c> 警告。
        /// </para>
        /// </summary>
        private static ChainNode Normalize(ChainNode node)
        {
            if (node is SeqNode seq)
            {
                for (int i = 0; i < seq.Children.Count; i++)
                    seq.Children[i] = Normalize(seq.Children[i]);

                return seq.Children.Count == 1 ? seq.Children[0] : seq;
            }

            if (node is ParNode par)
            {
                for (int i = 0; i < par.Children.Count; i++)
                    par.Children[i] = Normalize(par.Children[i]);

                return par.Children.Count == 1 ? par.Children[0] : par;
            }

            // R11：choice 不剥单选项包装（N 选项的块语法不可退化），仅递归归一化各选项链
            if (node is ChoiceNode choice)
            {
                foreach (var opt in choice.Options)
                    opt.Chain = Normalize(opt.Chain);
            }

            return node;
        }
    }
}
