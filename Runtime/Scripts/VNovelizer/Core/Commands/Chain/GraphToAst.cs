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
            var straySinks = new List<string>();
            bool hasEndSentinel = false;
            foreach (var s in graph.FindSinks())
            {
                if (s.Kind == ChainGraphNodeKind.End) { hasEndSentinel = true; continue; }
                if (s.Kind == ChainGraphNodeKind.Start) continue; // 空链时 Start 也是 sink
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
                var sinks = graph.FindSinks();
                if (sinks.Count != 1)
                {
                    result.Errors.Add(sinks.Count == 0
                        ? "图中不存在终点（可能存在环）"
                        : $"图中存在 {sinks.Count} 个终点，命令链必须有唯一终点");
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

        private class Context
        {
            public readonly ChainGraph Graph;
            public readonly Result Result;

            /// <summary>已访问节点（检测重复访问——畸形图的兜底）</summary>
            public readonly HashSet<string> Visited = new HashSet<string>();

            public Context(ChainGraph graph, Result result)
            {
                Graph = graph;
                Result = result;
            }
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

                if (!ctx.Visited.Add(current))
                {
                    ctx.Result.Errors.Add($"节点被重复访问（图中存在环或 Fork/Join 不配对）：{node}");
                    return seq;
                }

                if (node.Kind == ChainGraphNodeKind.Fork)
                {
                    string joinId = FindMatchingJoin(ctx, node.Id);
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
        private static string FindMatchingJoin(Context ctx, string forkId)
        {
            var branches = ctx.Graph.GetSuccessors(forkId);
            if (branches.Count == 0) return null;

            // 候选：第一条分支路径上的所有 Join（按遇到顺序）
            var candidates = CollectJoinsAlongPath(ctx, branches[0]);
            if (candidates.Count == 0) return null;

            foreach (string candidate in candidates)
            {
                bool reachableFromAll = true;
                for (int i = 1; i < branches.Count; i++)
                {
                    if (!CanReach(ctx, branches[i], candidate))
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
        private static List<string> CollectJoinsAlongPath(Context ctx, string startId)
        {
            var joins = new List<string>();
            var seen = new HashSet<string>();
            string current = startId;
            int guard = 0;

            while (!string.IsNullOrEmpty(current) && seen.Add(current) && guard++ < 1024)
            {
                var node = ctx.Graph.GetNode(current);
                if (node == null) break;

                if (node.Kind == ChainGraphNodeKind.Join) joins.Add(current);

                var succ = ctx.Graph.GetSuccessors(current);
                if (succ.Count == 0) break;
                current = succ[0];
            }

            return joins;
        }

        /// <summary>从 <paramref name="fromId"/> 是否可达 <paramref name="targetId"/>（BFS）。</summary>
        private static bool CanReach(Context ctx, string fromId, string targetId)
        {
            var queue = new Queue<string>();
            var seen = new HashSet<string>();
            queue.Enqueue(fromId);
            seen.Add(fromId);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (id == targetId) return true;

                foreach (string next in ctx.Graph.GetSuccessors(id))
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

            return node;
        }
    }
}
