using System.Collections.Generic;

namespace VNovelizer.Core.Diagnostics
{
    /// <summary>
    /// 某一行的运行时执行状态（R10 行命令编辑器运行时控制器/监听器的数据单元）。
    /// 会话内按行持久：该行被再次播放时重置。
    /// </summary>
    public class LineNodeState
    {
        /// <summary>该行是否为定制行（链语法 Command 列——具备节点级三态与执行指针）。</summary>
        public bool IsChain;

        /// <summary>该行当前是否处于播放中（普通/增强行的行级"正在播放"标记）。</summary>
        public bool Playing;

        // ---- 进入段（EntryGraph）节点状态：key = Command 列文本内的源偏移 Position ----

        /// <summary>已执行完成的命令 Position 集合（"运行过"）。</summary>
        public readonly HashSet<int> EntryExecuted = new HashSet<int>();

        /// <summary>正在执行的命令 Position 集合（Par 并行时多个，"正在运行"）。</summary>
        public readonly HashSet<int> EntryRunning = new HashSet<int>();

        /// <summary>进入段执行指针：最近开始执行的命令 Position（-1 无）。</summary>
        public int EntryPointer = -1;

        // ---- 出口段（@Confirm 链）节点状态：key = 出口段文本内的源偏移 ----

        /// <summary>出口段已执行完成的命令 Position 集合。</summary>
        public readonly HashSet<int> ConfirmExecuted = new HashSet<int>();

        /// <summary>出口段正在执行的命令 Position 集合。</summary>
        public readonly HashSet<int> ConfirmRunning = new HashSet<int>();

        /// <summary>出口段执行指针（-1 无）。</summary>
        public int ConfirmPointer = -1;

        internal void ResetNodeStates()
        {
            EntryExecuted.Clear();
            EntryRunning.Clear();
            EntryPointer = -1;
            ConfirmExecuted.Clear();
            ConfirmRunning.Clear();
            ConfirmPointer = -1;
        }
    }

    /// <summary>
    /// 运行时执行状态快照（R10）：行命令编辑器在 Play 模式下充当
    /// 控制器/监听器的**唯一数据源**。
    ///
    /// <para>
    /// 写入方（运行时）：<c>ChainExecutor</c>（定制行节点级埋点）与
    /// <c>VNManager</c>（行切换/行级埋点）。
    /// 读取方（编辑器）：<c>RowPerfEditorWindow</c> 在
    /// <c>EditorApplication.update</c> 中每帧轮询，<see cref="Version"/>
    /// 变化时做脏检查刷新 UI。
    /// </para>
    ///
    /// <para>
    /// 纯静态数据对象：全部读写都在主线程（协程与 EditorApplication.update
    /// 同在主线程执行），无需加锁。Domain Reload（进/出 Play 模式）时
    /// 静态字段自动归零——运行时重新初始化、编辑器重新轮询，自然恢复。
    /// </para>
    /// </summary>
    public static class VNRuntimeDebugState
    {
        /// <summary>正在运行的剧本名（无运行时为空串）。</summary>
        public static string CurrentScriptName = "";

        /// <summary>运行时当前正在播放的行 ID（无运行时为空串）。编辑器据此跟随翻页。</summary>
        public static string CurrentLineId = "";

        /// <summary>会话内各行的执行状态（行 ID → 状态）。行级"已播放" = 字典含该键。</summary>
        public static readonly Dictionary<string, LineNodeState> LineStates =
            new Dictionary<string, LineNodeState>();

        /// <summary>状态版本号：任何状态变化时自增，编辑器据此跳过无变化的帧。</summary>
        public static long Version = 0;

        // ==================== 运行时埋点接口（VNManager / ChainExecutor 调用） ====================

        /// <summary>
        /// 开始播放某行。每次调用都重置该行的节点状态（再次播放 = 重新开始）；
        /// 同时更新当前行 ID（编辑器跟随翻页）与会话级"已播放"记录。
        /// </summary>
        public static void BeginLine(string scriptName, string lineId, bool isChain)
        {
            CurrentScriptName = scriptName ?? "";
            CurrentLineId = lineId ?? "";

            if (!string.IsNullOrEmpty(CurrentLineId))
            {
                if (!LineStates.TryGetValue(CurrentLineId, out var state))
                {
                    state = new LineNodeState();
                    LineStates[CurrentLineId] = state;
                }
                state.IsChain = isChain;
                state.Playing = true;
                state.ResetNodeStates(); // 再次播放该行 = 状态重置
            }
            Version++;
        }

        /// <summary>当前行播放结束（推进下一行 / Choice 暂停 / 中断）。</summary>
        public static void EndLine()
        {
            if (!string.IsNullOrEmpty(CurrentLineId) &&
                LineStates.TryGetValue(CurrentLineId, out var state))
            {
                state.Playing = false;
                state.EntryRunning.Clear();
                state.ConfirmRunning.Clear();
            }
            Version++;
        }

        /// <summary>命令节点开始执行（ChainExecutor 埋点）。</summary>
        public static void NodeStarted(int position, bool isConfirm)
        {
            if (position < 0) return;
            var state = GetCurrentLineState();
            if (state == null) return;

            (isConfirm ? state.ConfirmRunning : state.EntryRunning).Add(position);
            if (isConfirm) state.ConfirmPointer = position;
            else state.EntryPointer = position;
            Version++;
        }

        /// <summary>命令节点执行完成（ChainExecutor 埋点）。</summary>
        public static void NodeCompleted(int position, bool isConfirm)
        {
            if (position < 0) return;
            var state = GetCurrentLineState();
            if (state == null) return;

            (isConfirm ? state.ConfirmRunning : state.EntryRunning).Remove(position);
            (isConfirm ? state.ConfirmExecuted : state.EntryExecuted).Add(position);
            Version++;
        }

        /// <summary>Play 模式重新开始（VNManager.StartGame 入口）时重置全部状态。</summary>
        public static void ResetAll()
        {
            CurrentScriptName = "";
            CurrentLineId = "";
            LineStates.Clear();
            Version++;
        }

        /// <summary>取某行的状态（编辑器轮询用）；无记录返回 null。</summary>
        public static LineNodeState GetLineState(string lineId)
        {
            if (string.IsNullOrEmpty(lineId)) return null;
            LineStates.TryGetValue(lineId, out var state);
            return state;
        }

        private static LineNodeState GetCurrentLineState()
        {
            if (string.IsNullOrEmpty(CurrentLineId)) return null;
            LineStates.TryGetValue(CurrentLineId, out var state);
            return state;
        }
    }
}
