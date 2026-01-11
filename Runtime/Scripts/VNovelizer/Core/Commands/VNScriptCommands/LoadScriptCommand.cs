using UnityEngine;

namespace VNovelizer.Core.Commands
{
    public class LoadScriptCommand : VNCommand
    {
        public override string CommandName { get { return "loadscript"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("LoadScript命令参数不能为空");
                return false;
            }

            // 解析参数：剧本名, 行ID
            string[] parts = args.Split(',');
            string scriptName = parts[0].Trim();
            // 如果 Excel 里没写第二个参数，startID 就是 null
            string startID = parts.Length >= 2 ? parts[1].Trim() : null;

            // 1. 解析新剧本
            var scriptData = ScriptParser.Parse(scriptName);

            if (scriptData != null && scriptData.Lines.Count > 0)
            {
                VNManager manager = VNManager.GetInstance();

                // 2. 注入数据 (此时 CurrentLineIndex 会重置为 0)
                manager.SetScriptData(scriptData.Lines, scriptData.IDMap, scriptName);
                Debug.Log($"[LoadScript] 成功加载剧本: {scriptName}");

                // 3. 处理跳转逻辑
                if (!string.IsNullOrEmpty(startID))
                {
                    if (manager.LineIDIndexMap.TryGetValue(startID, out int index))
                    {
                        // 【关键修复】调用预演，确保跳过去的时候背景和立绘是对的
                        // 【修复】如果遇到 choice 命令，FastForwardToLine 会停止并设置 CurrentLineIndex
                        bool encounteredChoice = manager.FastForwardToLine(index);

                        // 只有在没有遇到 choice 时才设置 CurrentLineIndex
                        if (!encounteredChoice)
                        {
                            manager.CurrentLineIndex = index;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[LoadScript] 指定的 StartID {startID} 不存在，将从头开始。");
                    }
                }
                else
                {
                    // 如果没指定行号，也要重置一下状态，防止保留了上个剧本的残留立绘
                    // 或者是 FastForwardToLine(0)
                    manager.FastForwardToLine(0);
                }

                return true;
            }

            return false;
        }
    }
}