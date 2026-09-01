using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Reflection;
using System;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 命令基类，所有具体命令都继承自这个类
    /// </summary>
    public abstract class VNCommand
    {
        /// <summary>
        /// 命令名称
        /// </summary>
        public abstract string CommandName { get; }

        /// <summary>
        /// 执行命令
        /// </summary>
        public abstract bool Execute(string args);

        /// <summary>
        /// 异步执行命令
        /// </summary>
        public virtual IEnumerator ExecuteAsync(string args)
        {
            Execute(args);
            yield break;
        }

        /// <summary>
        /// [新增] 中断命令接口
        /// 当玩家点击屏幕需要跳过当前演出时调用
        /// </summary>
        public virtual void Interrupt() { }

        public virtual void Simulate(string args) { }
    }

    /// <summary>
    /// 命令管理器，负责注册、执行和中断命令
    /// </summary>
    public class CommandManager : BaseManager<CommandManager>
    {
        // 命令映射表
        private Dictionary<string, VNCommand> _commandMap = new Dictionary<string, VNCommand>();

        // 正在运行的异步命令（同一命令类型可能并行多条，用引用计数）
        private Dictionary<VNCommand, int> _runningCommandRefCount = new Dictionary<VNCommand, int>();

        // 当前活跃的命令链运行上下文（链式语法路径）
        // 用于点击跳过时整树中断：先杀并行分支协程，再 Interrupt 命令动画
        private ChainRunContext _activeChainContext;

        public bool IsRunning => _runningCommandRefCount.Count > 0;

        /// <summary>
        /// 已注册的命令数量（0 表示尚未 Init）。
        /// </summary>
        public int RegisteredCommandCount => _commandMap.Count;

        /// <summary>
        /// 命令名是否已注册（大小写不敏感）。
        /// 行演出编辑器的图校验用它判定"未注册命令"（拼写错 / 未实现）。
        /// </summary>
        public bool IsCommandRegistered(string commandName)
        {
            return !string.IsNullOrEmpty(commandName) &&
                   _commandMap.ContainsKey(commandName.ToLower());
        }

        /// <summary>
        /// 枚举全部已注册命令实例（只读快照）。
        /// 供命令节点化元数据读取器（<c>CommandMetaReader</c>）反射读取
        /// <c>[VNCommandMeta]</c>/<c>[VNParam]</c> 特性，以及命令面板列表构建。
        /// 含通过反射自动注册的**第三方自定义命令**——这是插件扩展点得以进入
        /// 图编辑器的关键（详见 VNCommandMetaAttribute 的设计契约说明）。
        /// </summary>
        public IEnumerable<KeyValuePair<string, VNCommand>> EnumerateRegisteredCommands()
        {
            // 返回副本，避免调用方枚举期间注册新命令导致 InvalidOperationException
            return new List<KeyValuePair<string, VNCommand>>(_commandMap);
        }

        /// <summary>
        /// 确保命令表已初始化（幂等）。
        /// Editor 工具在非播放模式下需要命令表时调用——运行时由 VNManager 负责 Init。
        /// </summary>
        public void EnsureInitialized()
        {
            if (_commandMap.Count == 0) Init();
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public void Init()
        {
            RegisterDefaultCommands();
            RegisterCustomCommandsViaReflection();
        }

        /// <summary>
        /// 系统命令名集合（小写）。这六个命令构成默认演出模板，
        /// 是**三层行形态判定**的依据：Command 列含其中任一 → 该行为「定制行」
        /// （模板已实体化，引擎不再走隐式演出路径）。
        /// 详见 VNCommandChainSpec.md §11.2。
        /// </summary>
        private static readonly HashSet<string> SystemCommandNames = new HashSet<string>
        {
            "showbg", "showchar", "showspeaker", "showdialogue", "playbgm", "playvoice"
        };

        /// <summary>命令名是否属于系统命令族（大小写不敏感）。</summary>
        public static bool IsSystemCommand(string commandName)
        {
            return !string.IsNullOrEmpty(commandName) &&
                   SystemCommandNames.Contains(commandName.ToLower());
        }

        /// <summary>
        /// 判断一段命令链文本中是否含系统命令（三层行形态判定入口）。
        /// 复用 <see cref="ChainParser"/> 解析后深度优先展开，因此
        /// <c>&amp;</c> / <c>-&gt;</c> / <c>[]</c> 任意嵌套结构都能正确识别。
        /// </summary>
        public static bool ContainsSystemCommand(string commandString)
        {
            if (string.IsNullOrEmpty(commandString)) return false;

            var parsed = ChainParser.Parse(commandString);
            if (parsed.Root == null) return false;

            var collected = new List<CommandNode>();
            ChainExecutor.CollectCommands(parsed.Root, collected);
            foreach (var node in collected)
            {
                if (IsSystemCommand(node.Name)) return true;
            }
            return false;
        }

        private void RegisterDefaultCommands()
        {
            // 系统命令族：构成默认演出模板，图编辑器与三层行形态判定均依赖其必然存在，
            // 因此显式注册（不依赖反射扫描的偶然性）。详见 VNCommandChainSpec.md §11.4
            RegisterCommand(new SystemCommands.ShowBgCommand());
            RegisterCommand(new SystemCommands.ShowCharCommand());
            RegisterCommand(new SystemCommands.ShowSpeakerCommand());
            RegisterCommand(new SystemCommands.ShowDialogueCommand());
            RegisterCommand(new SystemCommands.PlayBgmCommand());
            RegisterCommand(new SystemCommands.PlayVoiceCommand());

            RegisterCommand(new LoadScriptCommand());
            RegisterCommand(new UnlockCGCommand());
            RegisterCommand(new UnlockMusicCommand());
            RegisterCommand(new UnlockSceneCommand());
            RegisterCommand(new ConfigCommand());
            RegisterCommand(new ShakeCommand());
            RegisterCommand(new WaitCommand());
            RegisterCommand(new JumpCommand());
            RegisterCommand(new NextLineCommand());
            RegisterCommand(new JumpIfCommand());
            RegisterCommand(new JumpIfNotCommand());
            RegisterCommand(new LoadScriptIfCommand());
            RegisterCommand(new LoadScriptIfNotCommand());
            RegisterCommand(new LoadSceneCommand());
            RegisterCommand(new SetBoolFlagCommand());
            RegisterCommand(new SetIntFlagCommand());
            RegisterCommand(new SetStringFlagCommand());
            RegisterCommand(new CharJumpCommand());
            RegisterCommand(new ChoiceCommand());
            RegisterCommand(new BgFadeCommand());
            RegisterCommand(new SetTextSpeedCommand());
            RegisterCommand(new SetAutoSpeedCommand());
            RegisterCommand(new TColorCommand());
            RegisterCommand(new TSizeCommand());
            RegisterCommand(new CharFadeInCommand());
            RegisterCommand(new CharFadeOutCommand());
            RegisterCommand(new CharFlipCommand());
            RegisterCommand(new CharMoveCommand());
            RegisterCommand(new SetCharTransCommand());
            RegisterCommand(new PlaySFXCommand());
            RegisterCommand(new PlayVideoCommand());
            RegisterCommand(new PlayParticleCommand());
            RegisterCommand(new StopParticleCommand());
            RegisterCommand(new ShowPromptCommand());
            RegisterCommand(new PlayAnimCommand());
            RegisterCommand(new StopAnimCommand());

        }

        private void RegisterCustomCommandsViaReflection()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var autoRegistered = new List<string>();

            foreach (var assembly in assemblies)
            {

                string name = assembly.GetName().Name;
                if (name.StartsWith("Unity") || name.StartsWith("System") || name.StartsWith("mscorlib"))
                    continue;

                // 【健壮性 2026-08-26】GetTypes() 原在 try 之外：某个程序集类型加载失败抛出的
                // ReflectionTypeLoadException 会中断整个 foreach，导致其后所有程序集的命令
                // 全部注册不上。Editor 域程序集数量远多于运行时（各类插件/分析器），风险更高，
                // 且这会直接破坏"第三方自定义命令可进图编辑器"这一核心扩展点。
                // 现按程序集粒度隔离；ReflectionTypeLoadException 时取其已成功加载的部分类型。
                Type[] allTypes;
                try
                {
                    allTypes = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    allTypes = e.Types.Where(t => t != null).ToArray();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CommandManager] 跳过程序集 {name}（类型枚举失败）: {e.Message}");
                    continue;
                }

                var commandTypes = allTypes
                    .Where(type => type != null && type.IsSubclassOf(typeof(VNCommand)) && !type.IsAbstract);

                foreach (var type in commandTypes)
                {
                    try
                    {
                        VNCommand cmdInstance = (VNCommand)Activator.CreateInstance(type);

                        if (cmdInstance != null && !string.IsNullOrEmpty(cmdInstance.CommandName))
                        {
                            string cmdNameKey = cmdInstance.CommandName.ToLower();

                            if (!_commandMap.ContainsKey(cmdNameKey))
                            {
                                RegisterCommand(cmdInstance);
                                autoRegistered.Add(cmdNameKey);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[CommandManager] 自动注册命令失败 {type.Name}: {e.Message}");
                    }
                }
            }

            // 汇总为一条日志：逐条 Log 在 Editor 工具（如命令元数据检查器）触发初始化时会刷屏
            if (autoRegistered.Count > 0)
            {
                Debug.Log($"[CommandManager] 反射自动注册 {autoRegistered.Count} 个命令: " +
                          string.Join(", ", autoRegistered));
            }
        }

        public void RegisterCommand(VNCommand command)
        {
            if (command != null && !string.IsNullOrEmpty(command.CommandName))
            {
                string commandName = command.CommandName.ToLower();
                _commandMap[commandName] = command;
            }
        }

        public bool ExecuteCommand(string commandString)
        {
            if (string.IsNullOrEmpty(commandString)) return false;

            int startIndex = commandString.IndexOf('(');
            int endIndex = commandString.LastIndexOf(')');

            if (startIndex > 0 && endIndex > startIndex)
            {
                string cmd = commandString.Substring(0, startIndex);
                string args = commandString.Substring(startIndex + 1, endIndex - startIndex - 1);
                return ExecuteSingleCommand(cmd, args);
            }
            return false;
        }

        public void SimulateCommands(string commandString)
        {
            if (string.IsNullOrEmpty(commandString)) return;

            // 链式语法：按深度优先串行序展开后逐个 Simulate
            // （预演不关心时序，只关心最终状态：背景/BGM/立绘/标志）
            var chainResult = ChainParser.Parse(commandString);
            if (chainResult.UsesChainSyntax && chainResult.Root != null)
            {
                var collected = new List<CommandNode>();
                ChainExecutor.CollectCommands(chainResult.Root, collected);
                foreach (var cmd in collected)
                {
                    string cmdName = cmd.Name.ToLower();
                    if (_commandMap.ContainsKey(cmdName))
                        _commandMap[cmdName].Simulate(cmd.Args);
                }
                return;
            }

            // 旧逻辑（兼容模式）：按 & 切分逐个 Simulate
            string[] actions = commandString.Split('&');
            foreach (string action in actions)
            {
                string trimmedAction = action.Trim();
                if (string.IsNullOrEmpty(trimmedAction)) continue;

                int start = trimmedAction.IndexOf('(');
                int end = trimmedAction.LastIndexOf(')');

                if (start > 0 && end > start)
                {
                    string cmd = trimmedAction.Substring(0, start).ToLower();
                    string args = trimmedAction.Substring(start + 1, end - start - 1);

                    if (_commandMap.ContainsKey(cmd))
                    {
                        // 只调用 Simulate
                        _commandMap[cmd].Simulate(args);
                    }
                }
            }
        }

        private bool ExecuteSingleCommand(string cmd, string args)
        {
            if (string.IsNullOrEmpty(cmd)) return false;

            string commandName = cmd.ToLower();
            if (_commandMap.ContainsKey(commandName))
            {
                // 同步执行不计入 _runningCommandRefCount（仅异步路径计数）
                return _commandMap[commandName].Execute(args);
            }
            else
            {
                Debug.LogWarning($"未找到命令: {cmd}");
                return false;
            }
        }

        /// <summary>
        /// 异步执行单个命令 (核心修改)
        /// </summary>
        public IEnumerator ExecuteSingleCommandAsync(string cmd, string args)
        {
            if (string.IsNullOrEmpty(cmd)) yield break;

            string commandName = cmd.ToLower();
            if (_commandMap.ContainsKey(commandName))
            {
                VNCommand command = _commandMap[commandName];

                if (!_runningCommandRefCount.ContainsKey(command))
                    _runningCommandRefCount[command] = 0;
                _runningCommandRefCount[command]++;

                yield return command.ExecuteAsync(args);

                if (_runningCommandRefCount.ContainsKey(command))
                {
                    _runningCommandRefCount[command]--;
                    if (_runningCommandRefCount[command] <= 0)
                        _runningCommandRefCount.Remove(command);
                }
            }
            else
            {
                Debug.LogWarning($"未找到命令: {cmd}");
            }
        }

        /// <summary>
        /// 同一行里连续的 CharFadeIn / CharFadeOut 并行执行（例如多站位同时淡入）。
        /// </summary>
        private IEnumerator ExecuteCharFadeParallelBatch(List<(string cmd, string args)> batch)
        {
            if (batch.Count <= 1)
            {
                if (batch.Count == 1)
                    yield return ExecuteSingleCommandAsync(batch[0].cmd, batch[0].args);
                yield break;
            }

            int remaining = batch.Count;
            var mono = MonoManager.GetInstance();
            foreach (var item in batch)
            {
                mono.StartCoroutine(CharFadeParallelRunner(item.cmd, item.args, () => remaining--));
            }

            while (remaining > 0)
                yield return null;
        }

        private IEnumerator CharFadeParallelRunner(string cmd, string args, Action onDone)
        {
            yield return ExecuteSingleCommandAsync(cmd, args);
            onDone?.Invoke();
        }

        public void ExecuteCommands(string commandString)
        {
            if (string.IsNullOrEmpty(commandString)) return;

            // 链式语法：同步模式无法表达时序，按展开顺序执行并提示
            var chainResult = ChainParser.Parse(commandString);
            if (chainResult.UsesChainSyntax && chainResult.Root != null)
            {
                Debug.LogWarning(
                    "[CommandManager] 检测到链式语法（-> / [），同步执行模式下将忽略并行/串行时序，按顺序执行。建议通过异步入口 ExecuteCommandsAsync 执行以获得正确时序。");

                var collected = new List<CommandNode>();
                ChainExecutor.CollectCommands(chainResult.Root, collected);
                foreach (var cmd in collected)
                    ExecuteSingleCommand(cmd.Name, cmd.Args);
                return;
            }

            // 旧逻辑（兼容模式）
            string[] actions = commandString.Split('&');
            foreach (string action in actions)
            {
                string trimmedAction = action.Trim();
                if (!string.IsNullOrEmpty(trimmedAction)) ExecuteCommand(trimmedAction);
            }
        }

        /// <summary>
        /// 【skip / 快进落地专用】按深度优先展开顺序同步执行全部命令，**不打时序警告**。
        ///
        /// <para>
        /// 与 <see cref="ExecuteCommands"/> 的区别仅在于不发出"同步模式忽略时序"的警告——
        /// 因为在 skip 语境下"忽略时序、直接取终态"正是**预期语义**，而非误用。
        /// 定制行的演出完全由命令链承载，skip 时必须走这里而非 <c>SimulateCommands</c>
        /// （后者只更新状态，画面不会出现文本/背景/立绘）。
        /// </para>
        /// </summary>
        public void ExecuteCommandsInstant(string commandString)
        {
            if (string.IsNullOrEmpty(commandString)) return;

            var chainResult = ChainParser.Parse(commandString);
            if (chainResult.UsesChainSyntax && chainResult.Root != null)
            {
                var collected = new List<CommandNode>();
                ChainExecutor.CollectCommands(chainResult.Root, collected);
                foreach (var cmd in collected)
                    ExecuteSingleCommand(cmd.Name, cmd.Args);
                return;
            }

            string[] actions = commandString.Split('&');
            foreach (string action in actions)
            {
                string trimmedAction = action.Trim();
                if (!string.IsNullOrEmpty(trimmedAction)) ExecuteCommand(trimmedAction);
            }
        }

        public IEnumerator ExecuteCommandsAsync(string commandString)
        {
            if (string.IsNullOrEmpty(commandString)) yield break;

            // ===== 双轨切换 =====
            // 命令串中含 "->" 或 "[" 时启用链式解析器：
            //   &  = 严格并行（同时启动，全部完成才继续）
            //   -> = 严格串行（上一条完成才执行下一条）
            //   [] = 分组（内部视为整体）
            // 不含新符号时走旧逻辑（顺序执行 + CharFade 批处理并行），旧剧本 100% 行为不变。
            var chainResult = ChainParser.Parse(commandString);
            if (chainResult.UsesChainSyntax)
            {
                if (chainResult.Root != null)
                {
                    // 解析错误报告（不阻断，容错继续执行可执行部分）
                    foreach (var err in chainResult.Errors)
                        Debug.LogError($"[CommandChain] 命令链解析错误（{err}）");

                    // 语义警告报告（流程命令位置等，不阻断）
                    foreach (var warn in chainResult.Warnings)
                        Debug.LogWarning($"[CommandChain] 语法警告（{warn}）");

                    // 创建运行上下文并登记，支持点击跳过时整树中断
                    var ctx = new ChainRunContext();
                    _activeChainContext = ctx;
                    yield return ChainExecutor.Execute(chainResult.Root, ctx);

                    // 正常完成（或整树中断退出）后清理上下文
                    if (_activeChainContext == ctx)
                        _activeChainContext = null;
                    yield break;
                }

                // 无树根（致命错误）：报告全部错误后回退旧逻辑容错
                foreach (var err in chainResult.Errors)
                    Debug.LogError($"[CommandChain] 命令链解析失败（{err}），回退兼容模式执行");
            }

            // ===== 旧逻辑（兼容模式，保持原有行为） =====
            string[] actions = commandString.Split('&');
            var parsed = new List<(string cmd, string args)>();
            foreach (string action in actions)
            {
                string trimmedAction = action.Trim();
                if (string.IsNullOrEmpty(trimmedAction)) continue;

                int startIndex = trimmedAction.IndexOf('(');
                int endIndex = trimmedAction.LastIndexOf(')');

                if (startIndex > 0 && endIndex > startIndex)
                {
                    string cmd = trimmedAction.Substring(0, startIndex);
                    string args = trimmedAction.Substring(startIndex + 1, endIndex - startIndex - 1);
                    parsed.Add((cmd, args));
                }
            }

            int i = 0;
            while (i < parsed.Count)
            {
                string cmdLower = parsed[i].cmd.ToLower();
                if (cmdLower == "charfadein" || cmdLower == "charfadeout")
                {
                    var batch = new List<(string cmd, string args)>();
                    while (i < parsed.Count)
                    {
                        var entry = parsed[i];
                        string cl = entry.cmd.ToLower();
                        if (cl != "charfadein" && cl != "charfadeout") break;
                        batch.Add(entry);
                        i++;
                    }
                    yield return ExecuteCharFadeParallelBatch(batch);
                }
                else
                {
                    yield return ExecuteSingleCommandAsync(parsed[i].cmd, parsed[i].args);
                    i++;
                }
            }
        }

        // [新增] 中断所有命令
        public void InterruptAll()
        {
            // 1. 链式语法：先整树中断
            //    （标记 Aborted 防止分支继续执行后续命令 + StopCoroutine 杀死分支内
            //     wait 等待与串行后续命令——Unity 的 StopCoroutine 不级联杀子协程，
            //     必须显式停止，否则残留分支会污染下一行演出）
            if (_activeChainContext != null)
            {
                ChainExecutor.Abort(_activeChainContext);
                _activeChainContext = null;
            }

            // 2. 中断当前运行中的命令动画（快进到最终态，避免画面停在中间状态）
            if (_runningCommandRefCount.Count == 0) return;

            var commands = new List<VNCommand>(_runningCommandRefCount.Keys);
            for (int i = commands.Count - 1; i >= 0; i--)
                commands[i].Interrupt();

            _runningCommandRefCount.Clear();
        }
    }
}