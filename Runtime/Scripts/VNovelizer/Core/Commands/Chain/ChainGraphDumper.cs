using System;
using System.Collections.Generic;
using System.Text;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>
    /// 图转储与哨兵装配（2026-08-28：修复"终端不进图"与"快照存占位文本"两大根因）。
    ///
    /// <para>
    /// <b>哨兵常驻</b>（对齐蓝图/Shader Graph 的 Entry-Exit 一等公民模型）：
    /// 编辑器中的图始终包含 Start / End 两个哨兵节点，
    /// 终端锚点边（Start→链头、链尾→End）是<b>真实图数据</b>而非视图装饰——
    ///
    /// <list type="bullet">
    /// <item>用户连「行入口终端 → 节点」＝ 声明该节点为链头，边进入图数据；</item>
    /// <item>「单命令链」在数据层为 Start→A→End，不再被误判为孤立节点；</item>
    /// <item>空泳道也渲染终端，拖入的第一个节点有处可连（不再死锁）；</item>
    /// <item><see cref="GraphToAst"/> 分解时跳过哨兵（不产生命令），序列化天然不含。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>图转储</b>（对齐 Shader Graph 的快照模型）：
    /// Undo 快照必须能完整记录<b>非法中间态</b>（孤立节点、多起点、环），
    /// 而「图→命令链文本」的序列化在非法态下必然失败——以前快照存占位文本
    /// "(图结构待修正)"，恢复时解析失败直接得到空图，用户辛苦画的图被撤销"删光"。
    /// 转储格式是纯结构文本（节点行 + 边行），任何拓扑都能无损往返。
    /// </para>
    /// </summary>
    public static class ChainGraphDumper
    {
        // ---------------- 哨兵 ----------------

        /// <summary>取链的哨兵节点 ID（与终端视图的固定 ID 一致，位置持久化键天然对齐）。</summary>
        public static string SentinelId(bool isConfirm, bool isStart)
        {
            if (isStart)
                return isConfirm ? "__terminal_ConfirmStart__" : "__terminal_LineStart__";
            return isConfirm ? "__terminal_ChainEnd__" : "__terminal_WaitConfirm__";
        }

        /// <summary>查找图中的 Start 哨兵（无则 null）。</summary>
        public static ChainGraphNode FindStartSentinel(ChainGraph graph)
        {
            if (graph == null) return null;
            return graph.GetNode(SentinelId(false, true)) ?? graph.GetNode(SentinelId(true, true))
                ?? FindByKind(graph, ChainGraphNodeKind.Start);
        }

        /// <summary>查找图中的 End 哨兵（无则 null）。</summary>
        public static ChainGraphNode FindEndSentinel(ChainGraph graph)
        {
            if (graph == null) return null;
            return graph.GetNode(SentinelId(false, false)) ?? graph.GetNode(SentinelId(true, false))
                ?? FindByKind(graph, ChainGraphNodeKind.End);
        }

        private static ChainGraphNode FindByKind(ChainGraph graph, ChainGraphNodeKind kind)
        {
            foreach (var n in graph.Nodes)
                if (n.Kind == kind) return n;
            return null;
        }

        /// <summary>图是否含有任何非哨兵节点（命令 / Fork / Join）。</summary>
        public static bool HasContent(ChainGraph graph)
        {
            if (graph == null) return false;
            foreach (var n in graph.Nodes)
                if (n.Kind != ChainGraphNodeKind.Start && n.Kind != ChainGraphNodeKind.End)
                    return true;
            return false;
        }

        /// <summary>
        /// 装配哨兵：确保图包含 Start / End 节点。
        ///
        /// <para>
        /// <paramref name="linkAnchors"/> = true 时额外铺设锚点边（用于「文本 → 图」的加载方向）：
        /// Start 连向唯一的入度 0 非哨兵节点（链头），End 收接唯一的出度 0 非哨兵节点（链尾）。
        /// 仅在「刚解析出的合法 SP 图」上调用——对用户编辑中的图调 linkAnchors 会把孤立节点
        /// 错误地接上链。
        /// </para>
        /// </summary>
        public static void EnsureSentinels(ChainGraph graph, bool isConfirm, bool linkAnchors)
        {
            if (graph == null) return;

            string startId = SentinelId(isConfirm, true);
            string endId = SentinelId(isConfirm, false);

            if (graph.GetNode(startId) == null)
                graph.AddNode(startId, ChainGraphNodeKind.Start);
            if (graph.GetNode(endId) == null)
                graph.AddNode(endId, ChainGraphNodeKind.End);

            if (!linkAnchors) return;

            if (graph.OutDegree(startId) == 0)
            {
                var head = FindFreeEnd(graph, wantSource: true);
                if (head != null) graph.AddEdge(startId, head.Id);
            }

            if (graph.InDegree(endId) == 0)
            {
                var tail = FindFreeEnd(graph, wantSource: false);
                if (tail != null) graph.AddEdge(tail.Id, endId);
            }
        }

        /// <summary>找入度 0（链头候选）或出度 0（链尾候选）的非哨兵节点。多个时取第一个（残缺文本的防御）。</summary>
        private static ChainGraphNode FindFreeEnd(ChainGraph graph, bool wantSource)
        {
            foreach (var n in graph.Nodes)
            {
                if (n.Kind == ChainGraphNodeKind.Start || n.Kind == ChainGraphNodeKind.End) continue;
                if (wantSource ? graph.InDegree(n.Id) == 0 : graph.OutDegree(n.Id) == 0)
                    return n;
            }
            return null;
        }

        // ---------------- 转储 ----------------

        /// <summary>
        /// 把图转储为可逆文本。节点行 <c>N\tid\tkind\tcmd\targs</c>，边行 <c>E\tfrom\tto</c>。
        /// 字段内的 \、制表符、换行被转义为两字符序列，args 可含任意字符。
        /// 空图返回空串。
        /// </summary>
        public static string Dump(ChainGraph graph)
        {
            if (graph == null || graph.NodeCount == 0) return "";

            var sb = new StringBuilder();
            foreach (var n in graph.Nodes)
            {
                sb.Append("N\t").Append(Escape(n.Id))
                  .Append('\t').Append((int)n.Kind)
                  .Append('\t').Append(Escape(n.CommandName ?? ""))
                  .Append('\t').Append(Escape(n.Args ?? ""))
                  .Append('\n');
            }

            foreach (var e in graph.Edges)
            {
                sb.Append("E\t").Append(Escape(e.FromId))
                  .Append('\t').Append(Escape(e.ToId))
                  .Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从转储文本恢复图（<see cref="Dump"/> 的逆）。坏行跳过（防御），
        /// 绝不抛异常——快照数据宁可缺行也不能让撤销崩溃。
        /// </summary>
        public static ChainGraph Restore(string dump)
        {
            var graph = new ChainGraph();
            if (string.IsNullOrEmpty(dump)) return graph;

            foreach (var rawLine in dump.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length == 0) continue;

                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                if (parts[0] == "N" && parts.Length >= 5)
                {
                    if (!int.TryParse(parts[2], out int kind)) continue;
                    var node = new ChainGraphNode(
                        Unescape(parts[1]),
                        (ChainGraphNodeKind)kind,
                        Unescape(parts[3]),
                        Unescape(parts[4]));
                    graph.AddNode(node);
                }
                else if (parts[0] == "E" && parts.Length >= 3)
                {
                    graph.AddEdge(Unescape(parts[1]), Unescape(parts[2]));
                }
            }

            return graph;
        }

        // ---------------- 转义 ----------------

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case '\\': sb.Append('\\'); break;
                        case 't': sb.Append('\t'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
