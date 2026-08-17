using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>
    /// 命令链运行上下文：支持整树中断（用户点击跳过 / 场景重置）。
    ///
    /// 背景：Unity 的 StopCoroutine 只杀目标协程本身，不级联杀其内部
    /// StartCoroutine 启动的子协程。命令链的并行分支是独立协程，
    /// 若只停主链，分支内的后续命令仍会继续执行（残留命令污染下一行）。
    ///
    /// 双保险机制：
    ///   1. aborted 标志——Seq 每步 / Par 每分支启动前 / join 循环均检查，
    ///      即使协程句柄因时序未及时停止，标志也会让分支逐级快速退出
    ///   2. 分支协程句柄登记——Abort 时逐个 StopCoroutine，直接杀死
    ///      wait 等待与分支内后续命令链
    /// </summary>
    public class ChainRunContext
    {
        /// <summary>整树中断标志（置 true 后所有执行路径逐步退出）</summary>
        public bool Aborted;

        /// <summary>已启动的并行分支协程句柄（Abort 时全部停止）</summary>
        public List<Coroutine> Branches = new List<Coroutine>();
    }

    /// <summary>
    /// 命令链树执行器（协程调度）。
    ///
    /// 执行规则：
    ///   - SeqNode（串行链）：子节点逐个等待，上一项完成才执行下一项
    ///   - ParNode（并行组）：子节点同时启动，全部完成后才视为本节点完成（fork-join）
    ///   - CommandNode（叶子）：复用 CommandManager.ExecuteSingleCommandAsync
    ///     （瞬时命令立即返回，动画命令等协程跑完；命令不存在仅警告，不阻断整链）
    ///
    /// 中断模型：
    ///   - Abort(context)：标记 Aborted + 停止全部分支协程（杀死 wait 与后续命令）
    ///   - 随后调用方应再执行 CommandManager.InterruptAll()，
    ///     让"正在运行的命令"的动画快进到最终态（避免画面停在中间态）
    ///
    /// Simulate：
    ///   - CollectCommands 按深度优先串行序展开树（预演用，不关心时序只关心最终状态）
    /// </summary>
    public static class ChainExecutor
    {
        /// <summary>
        /// 递归执行命令链树（创建独立运行上下文）。
        /// </summary>
        public static IEnumerator Execute(ChainNode node)
        {
            var ctx = new ChainRunContext();
            yield return Execute(node, ctx);
        }

        /// <summary>
        /// 递归执行命令链树（外部传入运行上下文，用于整树中断）。
        /// CommandManager 双轨路径应使用本重载并登记上下文。
        /// </summary>
        public static IEnumerator Execute(ChainNode node, ChainRunContext ctx)
        {
            if (node == null || ctx == null) yield break;

            switch (node)
            {
                case SeqNode seq:
                    yield return ExecuteSeq(seq, ctx);
                    break;

                case ParNode par:
                    yield return ExecutePar(par, ctx);
                    break;

                case CommandNode cmd:
                    yield return ExecuteCommand(cmd);
                    break;
            }
        }

        /// <summary>
        /// 整树中断：标记 Aborted 并停止全部分支协程。
        /// 调用后应继续执行 CommandManager.InterruptAll() 让动画命令快进到最终态。
        /// </summary>
        public static void Abort(ChainRunContext ctx)
        {
            if (ctx == null || ctx.Aborted) return;

            ctx.Aborted = true;

            if (ctx.Branches.Count > 0)
            {
                var mono = MonoManager.GetInstance();
                foreach (var branch in ctx.Branches)
                {
                    if (branch != null)
                        mono.StopCoroutine(branch);
                }
                ctx.Branches.Clear();
            }
        }

        /// <summary>
        /// 串行链：逐个等待子节点完成。每步检查中断标志。
        /// </summary>
        private static IEnumerator ExecuteSeq(SeqNode seq, ChainRunContext ctx)
        {
            foreach (var child in seq.Children)
            {
                if (ctx.Aborted) yield break;
                yield return Execute(child, ctx);
            }
        }

        /// <summary>
        /// 并行组：全部子节点同时启动，等待全部完成（fork-join）。
        /// join 循环同时检查中断标志，防止分支被外部杀死后主链死等。
        /// </summary>
        private static IEnumerator ExecutePar(ParNode par, ChainRunContext ctx)
        {
            if (par.Children.Count == 0)
                yield break;

            // 单子节点退化：直接内联执行，省去协程开销（也无需登记句柄）
            if (par.Children.Count == 1)
            {
                yield return Execute(par.Children[0], ctx);
                yield break;
            }

            int remaining = par.Children.Count;
            var mono = MonoManager.GetInstance();

            // fork：全部子节点同时启动（中断则不再启动新分支）
            foreach (var child in par.Children)
            {
                if (ctx.Aborted) break;
                var handle = mono.StartCoroutine(RunBranch(child, () => remaining--, ctx));
                ctx.Branches.Add(handle);
            }

            // join：等待全部完成（或被中断）
            while (remaining > 0 && !ctx.Aborted)
                yield return null;
        }

        /// <summary>
        /// 并行分支包装协程：执行完毕后回调减计数。
        /// 分支内每层执行都会检查 Aborted（由 Execute/ExecuteSeq 保证）。
        /// </summary>
        private static IEnumerator RunBranch(ChainNode node, Action onDone, ChainRunContext ctx)
        {
            yield return Execute(node, ctx);
            onDone?.Invoke();
        }

        /// <summary>
        /// 命令叶子：复用现有命令体系（含引用计数与未知命令警告）。
        /// 命令失败/不存在时视为完成，不阻断整链（演出容错优先）。
        /// </summary>
        private static IEnumerator ExecuteCommand(CommandNode cmd)
        {
            if (string.IsNullOrEmpty(cmd.Name))
                yield break; // 错误恢复产生的占位命令，静默跳过

            yield return CommandManager.GetInstance()
                .ExecuteSingleCommandAsync(cmd.Name, cmd.Args);
        }

        /// <summary>
        /// 按深度优先串行序展开树，收集全部命令叶子。
        /// 用于 Simulate（预演）：不关心时序，只关心最终状态，
        /// 因此串行/并行结构都按出现顺序平铺。
        /// </summary>
        public static void CollectCommands(ChainNode node, List<CommandNode> output)
        {
            if (node == null || output == null) return;

            switch (node)
            {
                case SeqNode seq:
                    foreach (var child in seq.Children)
                        CollectCommands(child, output);
                    break;

                case ParNode par:
                    foreach (var child in par.Children)
                        CollectCommands(child, output);
                    break;

                case CommandNode cmd:
                    if (!string.IsNullOrEmpty(cmd.Name))
                        output.Add(cmd);
                    break;
            }
        }
    }
}
