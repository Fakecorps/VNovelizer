using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        // [新增] 正在运行的命令列表
        private List<VNCommand> _runningCommands = new List<VNCommand>();

        // [新增] 是否有命令正在运行
        public bool IsRunning => _runningCommands.Count > 0;

        /// <summary>
        /// 初始化
        /// </summary>
        public void Init()
        {
            RegisterDefaultCommands();
        }

        private void RegisterDefaultCommands()
        {
            RegisterCommand(new LoadScriptCommand());
            RegisterCommand(new UnlockCGCommand());
            RegisterCommand(new ConfigCommand());
            RegisterCommand(new ShakeCommand());
            RegisterCommand(new WaitCommand());
            RegisterCommand(new JumpCommand());
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
                // 同步执行不计入 _runningCommands，因为它是瞬间完成的
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

                // 1. 记录正在运行
                if (!_runningCommands.Contains(command))
                    _runningCommands.Add(command);

                // 2. 等待执行
                yield return command.ExecuteAsync(args);

                // 3. 执行完毕，移除记录
                if (_runningCommands.Contains(command))
                    _runningCommands.Remove(command);
            }
            else
            {
                Debug.LogWarning($"未找到命令: {cmd}");
            }
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
            foreach (string action in actions)
            {
                string trimmedAction = action.Trim();
                if (!string.IsNullOrEmpty(trimmedAction))
                {
                    int startIndex = trimmedAction.IndexOf('(');
                    int endIndex = trimmedAction.LastIndexOf(')');

                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        string cmd = trimmedAction.Substring(0, startIndex);
                        string args = trimmedAction.Substring(startIndex + 1, endIndex - startIndex - 1);
                        yield return ExecuteSingleCommandAsync(cmd, args);
                    }
                }
            }
        }

        // [新增] 中断所有命令
        public void InterruptAll()
        {
            if (_runningCommands.Count == 0) return;

            // 倒序遍历，防止在中断过程中集合被修改导致报错
            for (int i = _runningCommands.Count - 1; i >= 0; i--)
            {
                _runningCommands[i].Interrupt();
            }
            _runningCommands.Clear();
        }
    }
}