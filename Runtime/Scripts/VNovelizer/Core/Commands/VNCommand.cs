using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Reflection;
using System;

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

        public bool IsRunning => _runningCommandRefCount.Count > 0;

        /// <summary>
        /// 初始化
        /// </summary>
        public void Init()
        {
            RegisterDefaultCommands();
            RegisterCustomCommandsViaReflection();
        }

        private void RegisterDefaultCommands()
        {
            RegisterCommand(new LoadScriptCommand());
            RegisterCommand(new UnlockCGCommand());
            RegisterCommand(new UnlockMusicCommand());
            RegisterCommand(new UnlockSceneCommand());
            RegisterCommand(new ConfigCommand());
            RegisterCommand(new ShakeCommand());
            RegisterCommand(new WaitCommand());
            RegisterCommand(new JumpCommand());
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

            foreach (var assembly in assemblies)
            {

                string name = assembly.GetName().Name;
                if (name.StartsWith("Unity") || name.StartsWith("System") || name.StartsWith("mscorlib"))
                    continue;

                var commandTypes = assembly.GetTypes()
                    .Where(type => type.IsSubclassOf(typeof(VNCommand)) && !type.IsAbstract);

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
                                Debug.Log($"[CommandManager] 自动注册命令成功 {type.Name} => {cmdNameKey}");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[CommandManager] 自动注册命令失败 {type.Name}: {e.Message}");
                    }
                }
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
            if (_runningCommandRefCount.Count == 0) return;

            var commands = new List<VNCommand>(_runningCommandRefCount.Keys);
            for (int i = commands.Count - 1; i >= 0; i--)
                commands[i].Interrupt();

            _runningCommandRefCount.Clear();
        }
    }
}