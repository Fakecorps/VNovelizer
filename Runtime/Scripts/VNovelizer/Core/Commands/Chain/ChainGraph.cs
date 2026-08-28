using System.Collections.Generic;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>图节点的类别。</summary>
    public enum ChainGraphNodeKind
    {
        /// <summary>命令节点（对应 <see cref="CommandNode"/>）</summary>
        Command = 0,

        /// <summary>并行分流（对应 <see cref="ParNode"/> 的开始）</summary>
        Fork,

        /// <summary>并行汇合（对应 <see cref="ParNode"/> 的结束）</summary>
        Join,

        /// <summary>链起点（无入边的哨兵节点，不产生命令）</summary>
        Start,

        /// <summary>链终点（无出边的哨兵节点，不产生命令）</summary>
        End,
    }

    /// <summary>
    /// 图中的一个节点。**与 GraphView 无关的纯数据模型**——
    /// 使 SP 分解、序列化、校验三个组件可脱离 UI 独立开发与测试，
    /// UI 层（<c>CommandNodeView</c> 等）只需持有本类型的引用。
    /// </summary>
    public class ChainGraphNode
    {
        /// <summary>图内唯一 ID（UI 层生成，用于连线引用与错误定位）</summary>
        public string Id;

        public ChainGraphNodeKind Kind;

        /// <summary>命令名（仅 <see cref="ChainGraphNodeKind.Command"/> 有效）</summary>
        public string CommandName;

        /// <summary>原始参数串（仅 Command 有效，与 <see cref="CommandNode.Args"/> 同语义）</summary>
        public string Args;

        public ChainGraphNode(string id, ChainGraphNodeKind kind,
            string commandName = null, string args = null)
        {
            Id = id;
            Kind = kind;
            CommandName = commandName;
            Args = args;
        }

        public override string ToString()
        {
            return Kind == ChainGraphNodeKind.Command
                ? $"{Id}:{CommandName}({Args})"
                : $"{Id}:{Kind}";
        }
    }

    /// <summary>一条有向边（从 <see cref="FromId"/> 的出端口连到 <see cref="ToId"/> 的入端口）。</summary>
    public struct ChainGraphEdge
    {
        public string FromId;
        public string ToId;

        public ChainGraphEdge(string fromId, string toId)
        {
            FromId = fromId;
            ToId = toId;
        }

        public override string ToString() => $"{FromId} -> {ToId}";
    }

    /// <summary>
    /// 一条命令链的图表示（单条链；进入段与出口段各自是一张独立的图）。
    ///
    /// <para>
    /// <b>不变式</b>：合法的图必须是 SP 图（Series-Parallel，串并联图）——
    /// 这是命令链文本能表达的全部结构。校验由 <c>ChainGraphValidator</c> 负责，
    /// 分解由 <c>GraphToAst</c> 负责，本类型只管数据与邻接查询。
    /// </para>
    /// </summary>
    public class ChainGraph
    {
        private readonly Dictionary<string, ChainGraphNode> _nodes = new Dictionary<string, ChainGraphNode>();
        private readonly List<ChainGraphEdge> _edges = new List<ChainGraphEdge>();

        /// <summary>出边邻接表（保持添加顺序——Fork 的分支顺序即序列化后的分支顺序）</summary>
        private readonly Dictionary<string, List<string>> _outgoing = new Dictionary<string, List<string>>();

        /// <summary>入边邻接表</summary>
        private readonly Dictionary<string, List<string>> _incoming = new Dictionary<string, List<string>>();

        public IEnumerable<ChainGraphNode> Nodes => _nodes.Values;
        public IReadOnlyList<ChainGraphEdge> Edges => _edges;
        public int NodeCount => _nodes.Count;

        public ChainGraphNode AddNode(ChainGraphNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Id)) return null;
            _nodes[node.Id] = node;
            if (!_outgoing.ContainsKey(node.Id)) _outgoing[node.Id] = new List<string>();
            if (!_incoming.ContainsKey(node.Id)) _incoming[node.Id] = new List<string>();
            return node;
        }

        public ChainGraphNode AddNode(string id, ChainGraphNodeKind kind,
            string commandName = null, string args = null)
        {
            return AddNode(new ChainGraphNode(id, kind, commandName, args));
        }

        /// <summary>添加边。重复边会被忽略（同两点间不允许平行边——SP 图中无意义）。</summary>
        public void AddEdge(string fromId, string toId)
        {
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) return;
            if (!_nodes.ContainsKey(fromId) || !_nodes.ContainsKey(toId)) return;
            if (_outgoing[fromId].Contains(toId)) return;

            _edges.Add(new ChainGraphEdge(fromId, toId));
            _outgoing[fromId].Add(toId);
            _incoming[toId].Add(fromId);
        }

        /// <summary>移除边（2026-08-27：支持链中插入操作）。不存在则忽略。</summary>
        public void RemoveEdge(string fromId, string toId)
        {
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) return;
            if (!_outgoing.TryGetValue(fromId, out var outs)) return;

            // 找到并移除 _edges 中对应记录
            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                if (_edges[i].FromId == fromId && _edges[i].ToId == toId)
                {
                    _edges.RemoveAt(i);
                    break;
                }
            }
            outs.Remove(toId);
            if (_incoming.TryGetValue(toId, out var ins)) ins.Remove(fromId);
        }

        public ChainGraphNode GetNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _nodes.TryGetValue(id, out var node);
            return node;
        }

        /// <summary>后继节点 ID 列表（顺序 = 添加顺序）</summary>
        public List<string> GetSuccessors(string id)
        {
            if (id != null && _outgoing.TryGetValue(id, out var list)) return list;
            return new List<string>();
        }

        /// <summary>前驱节点 ID 列表</summary>
        public List<string> GetPredecessors(string id)
        {
            if (id != null && _incoming.TryGetValue(id, out var list)) return list;
            return new List<string>();
        }

        public int OutDegree(string id) => GetSuccessors(id).Count;
        public int InDegree(string id) => GetPredecessors(id).Count;

        /// <summary>入度为 0 的节点（合法图恰有一个）</summary>
        public List<ChainGraphNode> FindSources()
        {
            var result = new List<ChainGraphNode>();
            foreach (var node in _nodes.Values)
                if (InDegree(node.Id) == 0) result.Add(node);
            return result;
        }

        /// <summary>出度为 0 的节点（合法图恰有一个）</summary>
        public List<ChainGraphNode> FindSinks()
        {
            var result = new List<ChainGraphNode>();
            foreach (var node in _nodes.Values)
                if (OutDegree(node.Id) == 0) result.Add(node);
            return result;
        }
    }
}
