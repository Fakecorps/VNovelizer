using System.Collections.Generic;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>
    /// AST → 图转换器（<see cref="GraphToAst"/> 的逆向，图编辑器**加载**方向）。
    ///
    /// <para>
    /// 用途：把 CSV Command 列的命令链文本经 <see cref="ChainParser"/> 解析出的 AST
    /// 铺成图，交给 GraphView 渲染（配合 <c>ChainAutoLayout</c> 决定坐标）。
    /// </para>
    ///
    /// <para>
    /// <b>节点 ID 生成规则</b>：<c>"{深度优先序号}:{命令名}"</c>。
    /// 这既是图内唯一键，也是**节点位置持久化的身份**（决策 s2a）——
    /// 命令链文本中不存在节点身份（<see cref="CommandNode.Position"/> 是源串偏移，
    /// 插入一个节点会让后续全部位移），所以只能由结构推导。
    /// 结构未变时序号与命令名全部对上，位置完美恢复；结构变更时对得上签名的恢复、
    /// 其余重新布局。
    /// </para>
    /// </summary>
    public static class AstToGraph
    {
        /// <summary>
        /// 将 AST 铺成图。空树返回空图。
        /// 生成的图必然是合法 SP 图（因为 AST 本身就是 fork-join 树）。
        /// </summary>
        public static ChainGraph Convert(ChainNode root)
        {
            var graph = new ChainGraph();
            if (root == null) return graph;

            // Emit 内部完成全部节点与边的铺设；返回的跨段仅在递归中使用
            Emit(new Context(graph), root);
            return graph;
        }

        /// <summary>
        /// 一个子树在图中占据的"跨段"：外部通过入口连入、通过出口连出。
        /// </summary>
        private struct Span
        {
            public string EntryId;
            public string ExitId;

            public Span(string entryId, string exitId)
            {
                EntryId = entryId;
                ExitId = exitId;
            }

            public static readonly Span Empty = new Span(null, null);
        }

        private class Context
        {
            public readonly ChainGraph Graph;

            /// <summary>深度优先序号计数器（节点身份的组成部分）</summary>
            public int Ordinal;

            /// <summary>Fork/Join 编号（与命令序号分开，避免插入命令导致 Fork ID 全变）</summary>
            public int ForkJoinOrdinal;

            public Context(ChainGraph graph)
            {
                Graph = graph;
            }

            public string NextCommandId(string commandName)
            {
                return Ordinal++ + ":" + (commandName ?? "?").ToLower();
            }

            public string NextForkId() => "fork" + ForkJoinOrdinal;
            public string NextJoinId() => "join" + ForkJoinOrdinal++;
        }

        private static Span Emit(Context ctx, ChainNode node)
        {
            if (node is CommandNode cmd)
            {
                string id = ctx.NextCommandId(cmd.Name);
                ctx.Graph.AddNode(id, ChainGraphNodeKind.Command, cmd.Name, cmd.Args);
                return new Span(id, id);
            }

            if (node is SeqNode seq)
            {
                string entry = null;
                string prevExit = null;

                foreach (var child in seq.Children)
                {
                    var span = Emit(ctx, child);
                    if (span.EntryId == null) continue;

                    if (entry == null) entry = span.EntryId;
                    else ctx.Graph.AddEdge(prevExit, span.EntryId);

                    prevExit = span.ExitId;
                }

                return entry == null ? Span.Empty : new Span(entry, prevExit);
            }

            if (node is ParNode par)
            {
                // 单分支并行等价于该分支本身，不生成 Fork/Join（避免图上冗余胶囊）
                if (par.Children.Count == 1) return Emit(ctx, par.Children[0]);

                string forkId = ctx.NextForkId();
                string joinId = ctx.NextJoinId();
                ctx.Graph.AddNode(forkId, ChainGraphNodeKind.Fork);
                ctx.Graph.AddNode(joinId, ChainGraphNodeKind.Join);

                bool anyBranch = false;
                foreach (var child in par.Children)
                {
                    var span = Emit(ctx, child);
                    if (span.EntryId == null) continue;

                    ctx.Graph.AddEdge(forkId, span.EntryId);
                    ctx.Graph.AddEdge(span.ExitId, joinId);
                    anyBranch = true;
                }

                if (!anyBranch) return Span.Empty;

                return new Span(forkId, joinId);
            }

            return Span.Empty;
        }
    }
}
