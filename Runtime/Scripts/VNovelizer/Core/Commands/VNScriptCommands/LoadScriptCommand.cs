using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    [VNCommandMeta(VNCommandCategory.Flow,
        "切换剧本（跨剧本自动存档；仅可置于链尾）")]
    public class LoadScriptCommand : VNCommand
    {
        [VNParam(0, "script", VNParamType.String,
            Description = "剧本名（CSV 文件名，不含扩展名）")]
        [VNParam(1, "startId", VNParamType.String, Optional = true,
            Description = "起始行 ID（缺省从头开始；跳转目标行会自动预演重建状态）")]
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

            // 自动存档：跨剧本切换前保存当前剧本进度（新游戏首个 loadscript 无进度可存，内部自动跳过）
            VNManager.GetInstance().TriggerAutoSaveOnScriptSwitch();

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
                        else
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

        /// <summary>
        /// 快进预演：写入 PendingScriptSwitch，由 VNManager.FastForwardToLine 消费（切换剧本数据源后重定向预演）。
        /// 【Fix P3】此前未重写 Simulate，读档/跳行快进会静默丢失剧本切换。
        /// </summary>
        public override void Simulate(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[LoadScript] Simulate 参数不能为空");
                return;
            }

            string[] parts = args.Split(',');
            string scriptName = parts[0].Trim();
            string startID = parts.Length >= 2 && !string.IsNullOrEmpty(parts[1].Trim()) ? parts[1].Trim() : null;

            VNManager.GetInstance().PendingScriptSwitch = (scriptName, startID);
        }
    }
}