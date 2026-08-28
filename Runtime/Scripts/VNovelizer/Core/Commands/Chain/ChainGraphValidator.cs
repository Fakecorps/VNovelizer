using System.Collections.Generic;
using System.Linq;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>校验问题的严重级别。</summary>
    public enum ChainGraphIssueLevel
    {
        /// <summary>致命：图无法转换为合法命令链，**阻断保存**并标红节点</summary>
        Fatal = 0,

        /// <summary>警告：可保存，但很可能不是作者意图，高亮提示</summary>
        Warning,
    }

    /// <summary>一条校验问题。</summary>
    public class ChainGraphIssue
    {
        public ChainGraphIssueLevel Level;

        /// <summary>规则编号（对应 VNRowPerfEditorSpec.md §5 的规则表）</summary>
        public int RuleId;

        public string Message;

        /// <summary>相关节点 ID（UI 据此标红/高亮；可为空表示全图问题）</summary>
        public List<string> NodeIds = new List<string>();

        public ChainGraphIssue(ChainGraphIssueLevel level, int ruleId, string message,
            params string[] nodeIds)
        {
            Level = level;
            RuleId = ruleId;
            Message = message;
            if (nodeIds != null) NodeIds.AddRange(nodeIds);
        }

        public override string ToString()
        {
            string prefix = Level == ChainGraphIssueLevel.Fatal ? "【致命】" : "【警告】";
            return $"{prefix}[规则{RuleId}] {Message}";
        }
    }

    /// <summary>校验结果。</summary>
    public class ChainGraphValidationResult
    {
        public List<ChainGraphIssue> Issues = new List<ChainGraphIssue>();

        public bool HasFatal
        {
            get
            {
                foreach (var i in Issues)
                    if (i.Level == ChainGraphIssueLevel.Fatal) return true;
                return false;
            }
        }

        public int FatalCount
        {
            get
            {
                int n = 0;
                foreach (var i in Issues) if (i.Level == ChainGraphIssueLevel.Fatal) n++;
                return n;
            }
        }

        public int WarningCount => Issues.Count - FatalCount;

        /// <summary>是否允许保存（仅致命错误阻断）</summary>
        public bool CanSave => !HasFatal;

        public void Add(ChainGraphIssueLevel level, int ruleId, string message, params string[] nodeIds)
        {
            Issues.Add(new ChainGraphIssue(level, ruleId, message, nodeIds));
        }
    }

    /// <summary>
    /// 图结构校验器：保证自由连线画出的图 ≡ 合法命令链树（SP 图）。
    ///
    /// <para>
    /// <b>职责边界</b>：只**判定**图是否合法，不产出 AST（那是 <see cref="GraphToAst"/> 的事）。
    /// 保存链路为：Validator → GraphToAst → ChainSerializer → ChainParser 自校验 → 写 CSV。
    /// </para>
    ///
    /// <para>
    /// <b>分级阻断</b>（决策 s1）：致命错误阻断保存并标红；警告不阻断、仅高亮提示。
    /// 这个分级不是随意的——例如"流程命令非链尾"在运行时只是警告
    /// （<see cref="ChainParser.ValidateFlowCommands"/> 的既有行为），
    /// 编辑器若把它升级为阻断，就与运行时语义不一致了。
    /// </para>
    /// </summary>
    public static class ChainGraphValidator
    {
        /// <summary>
        /// 校验一条链的图。
        /// </summary>
        /// <param name="graph">待校验的图</param>
        /// <param name="isConfirmSection">是否为出口段（@Confirm:）——出口段禁止 choice</param>
        /// <param name="entrySectionHasChoice">进入段是否含 choice（用于规则 11）</param>
        public static ChainGraphValidationResult Validate(
            ChainGraph graph, bool isConfirmSection = false, bool entrySectionHasChoice = false)
        {
            var result = new ChainGraphValidationResult();

            if (graph == null || graph.NodeCount == 0) return result; // 空图合法（该行无命令）

            ValidateTopology(graph, result);
            ValidatePorts(graph, result);
            ValidateForkJoinPairing(graph, result);
            ValidateCommands(graph, result, isConfirmSection);
            ValidateNestingDepth(graph, result);
            ValidateConfirmSectionReachability(graph, result, isConfirmSection, entrySectionHasChoice);

            return result;
        }

        // ---------- 规则 1 / 5：唯一起终点、可达性、无环 ----------

        private static void ValidateTopology(ChainGraph graph, ChainGraphValidationResult result)
        {
            // 规则 5：无环（先查环——有环会导致起终点为 0，报错信息更准确）
            var cycleNodes = FindCycle(graph);
            if (cycleNodes != null)
            {
                result.Add(ChainGraphIssueLevel.Fatal, 5,
                    "命令链中存在环（命令会无限循环）：" + string.Join(" → ", cycleNodes),
                    cycleNodes.ToArray());
                return; // 有环时其余拓扑判定无意义
            }

            // 2026-08-28：哨兵感知。编辑器中的图恒含 Start/End 哨兵（ChainGraphDumper.EnsureSentinels），
            // 起终点角色由哨兵端口规则（ValidatePorts）表达；本方法的 sources/sinks 判定只对
            // 非哨兵节点生效——否则"单命令链"（Start→A→End，A 恰是数据层唯一无入边节点）
            // 会被误判为多起点，空链（哨兵间无连接）会被误判为 2 起点 2 终点。
            var startSentinel = ChainGraphDumper.FindStartSentinel(graph);
            var sources = NonSentinelSources(graph);
            var sinks = NonSentinelSinks(graph);

            // 孤立节点（既无入边也无出边，如刚从面板拖入尚未连线的新节点）
            // 单独归类报错，不计入起点/终点——否则"刚拖入新节点"被误导性报
            // "存在 2 个起点（请用 FORK 表达并行）"，用户会以为需要加 FORK。
            var isolated = new List<string>();
            foreach (var n in sources)
                if (graph.OutDegree(n.Id) == 0) isolated.Add(n.Id);
            if (isolated.Count > 0)
            {
                sources = sources.Where(s => !isolated.Contains(s.Id)).ToList();
                sinks = sinks.Where(s => !isolated.Contains(s.Id)).ToList();
                result.Add(ChainGraphIssueLevel.Fatal, 1,
                    $"存在 {isolated.Count} 个未连接节点（请连线到链上或删除）",
                    isolated.ToArray());
            }

            if (startSentinel != null)
            {
                // 哨兵在图：悬空节点检测以 Start 哨兵为可达性根。
                var reachable = CollectReachable(graph, startSentinel.Id);
                var reachableOnly = new List<string>();
                foreach (var node in graph.Nodes)
                {
                    if (node.Kind == ChainGraphNodeKind.Start ||
                        node.Kind == ChainGraphNodeKind.End) continue;
                    if (reachable.Contains(node.Id)) continue;
                    // 入==0 出==0 的孤立节点已被上面的"未连接节点"诊断捕获，不重复报
                    if (graph.InDegree(node.Id) == 0 && graph.OutDegree(node.Id) == 0) continue;
                    reachableOnly.Add(node.Id);
                }

                if (reachableOnly.Count > 0)
                {
                    result.Add(ChainGraphIssueLevel.Fatal, 1,
                        $"存在 {reachableOnly.Count} 个未连接到主链的悬空节点（不会被执行）",
                        reachableOnly.ToArray());
                }
                return;
            }

            // ---- 无哨兵（防御路径；编辑器正常流程不经过）----
            // 规则 1：唯一起点
            if (sources.Count == 0)
            {
                // 全部节点都孤立时也归入"未连接"，不重复报"无起点"；
                // 仅在存在已连接节点却无起点时报此错
                if (isolated.Count == 0)
                    result.Add(ChainGraphIssueLevel.Fatal, 1, "图中不存在起点（所有节点都有入边）");
            }
            else if (sources.Count > 1)
            {
                var ids = new List<string>();
                foreach (var n in sources) ids.Add(n.Id);
                result.Add(ChainGraphIssueLevel.Fatal, 1,
                    $"存在 {sources.Count} 个起点，命令链必须有唯一起点（请用 FORK 表达并行）",
                    ids.ToArray());
            }

            // 规则 1：唯一终点
            if (sinks.Count == 0)
            {
                if (isolated.Count == 0)
                    result.Add(ChainGraphIssueLevel.Fatal, 1, "图中不存在终点（所有节点都有出边）");
            }
            else if (sinks.Count > 1)
            {
                var ids = new List<string>();
                foreach (var n in sinks) ids.Add(n.Id);
                result.Add(ChainGraphIssueLevel.Fatal, 1,
                    $"存在 {sinks.Count} 个终点，命令链必须有唯一终点（请用 JOIN 汇合）",
                    ids.ToArray());
            }

            // 规则 1：全节点可达（悬空节点）
            if (sources.Count == 1)
            {
                var reachable = CollectReachable(graph, sources[0].Id);
                var reachableOnly = new List<string>();
                foreach (var node in graph.Nodes)
                {
                    if (reachable.Contains(node.Id)) continue;
                    // 排除 in==0 out==0 的孤立节点（已被诊断过）
                    if (graph.InDegree(node.Id) == 0 && graph.OutDegree(node.Id) == 0) continue;
                    reachableOnly.Add(node.Id);
                }

                if (reachableOnly.Count > 0)
                {
                    result.Add(ChainGraphIssueLevel.Fatal, 1,
                        $"存在 {reachableOnly.Count} 个未连接到主链的悬空节点（不会被执行）",
                        reachableOnly.ToArray());
                }
            }
        }

        private static List<ChainGraphNode> NonSentinelSources(ChainGraph graph)
        {
            var result = new List<ChainGraphNode>();
            foreach (var n in graph.FindSources())
                if (n.Kind != ChainGraphNodeKind.Start && n.Kind != ChainGraphNodeKind.End)
                    result.Add(n);
            return result;
        }

        private static List<ChainGraphNode> NonSentinelSinks(ChainGraph graph)
        {
            var result = new List<ChainGraphNode>();
            foreach (var n in graph.FindSinks())
                if (n.Kind != ChainGraphNodeKind.Start && n.Kind != ChainGraphNodeKind.End)
                    result.Add(n);
            return result;
        }

        // ---------- 规则 2 / 3：端口连线数量 ----------

        private static void ValidatePorts(ChainGraph graph, ChainGraphValidationResult result)
        {
            foreach (var node in graph.Nodes)
            {
                int inDeg = graph.InDegree(node.Id);
                int outDeg = graph.OutDegree(node.Id);

                switch (node.Kind)
                {
                    case ChainGraphNodeKind.Command:
                        // 规则 2：命令节点入/出各至多 1 条（0 条仅允许于链首/链尾）
                        if (inDeg > 1)
                            result.Add(ChainGraphIssueLevel.Fatal, 2,
                                $"命令 {node.CommandName} 有 {inDeg} 条入边，多路汇合必须经 JOIN 节点", node.Id);
                        if (outDeg > 1)
                            result.Add(ChainGraphIssueLevel.Fatal, 2,
                                $"命令 {node.CommandName} 有 {outDeg} 条出边，并行分流必须经 FORK 节点", node.Id);
                        break;

                    case ChainGraphNodeKind.Fork:
                        if (inDeg > 1)
                            result.Add(ChainGraphIssueLevel.Fatal, 2,
                                $"FORK 有 {inDeg} 条入边（应为 1）", node.Id);
                        if (outDeg == 0)
                            result.Add(ChainGraphIssueLevel.Fatal, 3, "FORK 没有任何分支", node.Id);
                        // 规则 8：单分支 Fork 是冗余结构（警告，不阻断）
                        else if (outDeg == 1)
                            result.Add(ChainGraphIssueLevel.Warning, 8,
                                "FORK 只有一条分支，可简化为直接连线", node.Id);
                        break;

                    case ChainGraphNodeKind.Join:
                        if (outDeg > 1)
                            result.Add(ChainGraphIssueLevel.Fatal, 2,
                                $"JOIN 有 {outDeg} 条出边（应为 1）", node.Id);
                        // 规则 3：JOIN 至少 2 条入边
                        if (inDeg < 2)
                            result.Add(ChainGraphIssueLevel.Fatal, 3,
                                $"JOIN 只有 {inDeg} 条入边（至少需要 2 条，否则无并行可汇合）", node.Id);
                        break;

                    // 2026-08-28：哨兵端口规则——哨兵常驻后"多起点/多终点"改由哨兵出/入度表达
                    //（FindSources 统计被哨兵吸收，旧的多起点判定不再触发）。
                    case ChainGraphNodeKind.Start:
                        if (outDeg > 1)
                            result.Add(ChainGraphIssueLevel.Fatal, 2,
                                $"起点终端连出了 {outDeg} 条线（命令链只能有一个起点，并行请用 FORK）",
                                node.Id);
                        break;

                    case ChainGraphNodeKind.End:
                        if (inDeg > 1)
                            result.Add(ChainGraphIssueLevel.Fatal, 2,
                                $"终点终端接收了 {inDeg} 条线（命令链只能有一个终点，多路汇合请用 JOIN）",
                                node.Id);
                        break;
                }
            }
        }

        // ---------- 规则 4：Fork/Join 配对 ----------

        private static void ValidateForkJoinPairing(ChainGraph graph, ChainGraphValidationResult result)
        {
            int forkCount = 0, joinCount = 0;
            foreach (var node in graph.Nodes)
            {
                if (node.Kind == ChainGraphNodeKind.Fork) forkCount++;
                else if (node.Kind == ChainGraphNodeKind.Join) joinCount++;
            }

            if (forkCount != joinCount)
            {
                result.Add(ChainGraphIssueLevel.Fatal, 4,
                    $"FORK 与 JOIN 数量不匹配（{forkCount} 个 FORK，{joinCount} 个 JOIN），并行必须成对");
                return;
            }

            if (forkCount == 0) return;

            // 逐个 Fork 检查其全部分支是否汇入同一 Join。
            // 直接复用 GraphToAst 的分解结果——它的失败即意味着配对有问题，
            // 且能给出比重复实现更准确的定位（避免两处算法漂移）。
            var converted = GraphToAst.Convert(graph);
            if (!converted.Success)
            {
                foreach (string error in converted.Errors)
                    result.Add(ChainGraphIssueLevel.Fatal, 4, error);
            }
        }

        // ---------- 规则 6 / 7 / 10：命令语义 ----------

        private static void ValidateCommands(ChainGraph graph, ChainGraphValidationResult result,
            bool isConfirmSection)
        {
            foreach (var node in graph.Nodes)
            {
                if (node.Kind != ChainGraphNodeKind.Command) continue;

                string name = (node.CommandName ?? "").Trim();
                if (string.IsNullOrEmpty(name))
                {
                    result.Add(ChainGraphIssueLevel.Fatal, 10, "存在未指定命令名的节点", node.Id);
                    continue;
                }

                bool isChoice = name.ToLower() == "choice";

                // 规则 6：出口段禁止 choice
                // （出口执行后引擎立即推进，选项面板尚未响应即被跳过——
                //   ScriptParser 在运行时报错，编辑期前置拦截）
                if (isConfirmSection && isChoice)
                {
                    result.Add(ChainGraphIssueLevel.Fatal, 6,
                        "出口段（@Confirm）不能包含 choice——出口执行后会立即推进，选项无法响应", node.Id);
                }

                // 规则 7：流程命令必须位于链尾（与运行时一致：警告级，不阻断）
                // 2026-08-28：哨兵常驻后 sink 恒为 End 哨兵——"链尾"改按
                // 「出边全部指向哨兵（或无出边）」判定，与视觉直觉一致。
                if (ChainParser.IsFlowCommand(name) && !IsChainEnd(graph, node.Id))
                {
                    result.Add(ChainGraphIssueLevel.Warning, 7,
                        $"流程命令 {name} 不在链尾，其后的命令会在「行已切换」的上下文中执行", node.Id);
                }

                // 规则 10：未注册命令（拼写错 / 尚未实现）
                var manager = CommandManager.GetInstance();
                if (manager.RegisteredCommandCount > 0 && !manager.IsCommandRegistered(name))
                {
                    result.Add(ChainGraphIssueLevel.Warning, 10,
                        $"命令 {name} 未注册（拼写错误或尚未实现），运行时将被忽略", node.Id);
                }
                else
                {
                    // 已注册但标记为 Planned 的占位命令
                    var info = CommandMetaReader.Get(name);
                    if (info != null && info.Planned)
                    {
                        result.Add(ChainGraphIssueLevel.Warning, 10,
                            $"命令 {name} 标记为「计划中」，尚未实现完整行为", node.Id);
                    }
                }
            }
        }

        /// <summary>
        /// 节点是否处于链尾：出边为空，或全部指向哨兵（链尾命令的出边连向 End 终端）。
        /// </summary>
        private static bool IsChainEnd(ChainGraph graph, string nodeId)
        {
            var successors = graph.GetSuccessors(nodeId);
            if (successors.Count == 0) return true;

            foreach (string succId in successors)
            {
                var succ = graph.GetNode(succId);
                if (succ == null) return false;
                if (succ.Kind != ChainGraphNodeKind.Start && succ.Kind != ChainGraphNodeKind.End)
                    return false;
            }
            return true;
        }

        // ---------- 规则 9：嵌套深度 ----------

        private static void ValidateNestingDepth(ChainGraph graph, ChainGraphValidationResult result)
        {
            // 用 Fork 的嵌套层数近似 AST 深度：图上画 3 层并行很自然，
            // 但序列化后会撞 ChainParser 的深度警告，应在编辑期就提示
            int maxDepth = ComputeMaxForkNesting(graph);
            if (maxDepth > ChainParser.MaxRecommendedDepth)
            {
                result.Add(ChainGraphIssueLevel.Warning, 9,
                    $"并行嵌套 {maxDepth} 层（建议不超过 {ChainParser.MaxRecommendedDepth} 层），" +
                    "可读性差，建议拆分到多行");
            }
        }

        private static int ComputeMaxForkNesting(ChainGraph graph)
        {
            // 2026-08-28：起点感知哨兵——哨兵常驻后 FindSources 返回的"起点"是 Start 哨兵
            //（命令节点都有入边），旧逻辑 sources.Count != 1 会直接漏检嵌套深度。
            string startId = ChainGraphDumper.FindStartSentinel(graph)?.Id;
            if (startId == null)
            {
                var sources = graph.FindSources();
                if (sources.Count != 1) return 0;
                startId = sources[0].Id;
            }

            int max = 0;
            var stack = new Stack<(string id, int depth)>();
            var seen = new HashSet<string>();
            stack.Push((startId, 0));

            while (stack.Count > 0)
            {
                var (id, depth) = stack.Pop();
                if (!seen.Add(id)) continue;

                var node = graph.GetNode(id);
                if (node == null) continue;

                int next = depth;
                if (node.Kind == ChainGraphNodeKind.Fork)
                {
                    next = depth + 1;
                    if (next > max) max = next;
                }
                else if (node.Kind == ChainGraphNodeKind.Join)
                {
                    next = depth > 0 ? depth - 1 : 0;
                }

                foreach (string succ in graph.GetSuccessors(id))
                    stack.Push((succ, next));
            }

            return max;
        }

        // ---------- 规则 11：进入段含 choice 时出口段不会执行 ----------

        private static void ValidateConfirmSectionReachability(
            ChainGraph graph, ChainGraphValidationResult result,
            bool isConfirmSection, bool entrySectionHasChoice)
        {
            if (!isConfirmSection || !entrySectionHasChoice) return;
            if (graph.NodeCount == 0) return;

            result.Add(ChainGraphIssueLevel.Warning, 11,
                "进入段含 choice，玩家点选项即推进，本出口段不会被执行" +
                "（与 ScriptParser 运行时警告一致）");
        }

        // ---------- 工具 ----------

        /// <summary>找出一个环（返回环上节点，无环返回 null）。</summary>
        private static List<string> FindCycle(ChainGraph graph)
        {
            var state = new Dictionary<string, int>(); // 0=未访问 1=在栈中 2=已完成
            var path = new List<string>();

            foreach (var node in graph.Nodes)
            {
                if (state.TryGetValue(node.Id, out int s) && s != 0) continue;
                var cycle = DfsFindCycle(graph, node.Id, state, path);
                if (cycle != null) return cycle;
            }

            return null;
        }

        private static List<string> DfsFindCycle(ChainGraph graph, string id,
            Dictionary<string, int> state, List<string> path)
        {
            state[id] = 1;
            path.Add(id);

            foreach (string next in graph.GetSuccessors(id))
            {
                state.TryGetValue(next, out int s);

                if (s == 1)
                {
                    // 找到环：截取 path 中从 next 开始的部分
                    int start = path.IndexOf(next);
                    var cycle = new List<string>();
                    for (int i = start; i < path.Count; i++) cycle.Add(path[i]);
                    cycle.Add(next);
                    return cycle;
                }

                if (s == 0)
                {
                    var found = DfsFindCycle(graph, next, state, path);
                    if (found != null) return found;
                }
            }

            state[id] = 2;
            path.RemoveAt(path.Count - 1);
            return null;
        }

        private static HashSet<string> CollectReachable(ChainGraph graph, string startId)
        {
            var reachable = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(startId);
            reachable.Add(startId);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                foreach (string next in graph.GetSuccessors(id))
                    if (reachable.Add(next)) queue.Enqueue(next);
            }

            return reachable;
        }
    }
}
