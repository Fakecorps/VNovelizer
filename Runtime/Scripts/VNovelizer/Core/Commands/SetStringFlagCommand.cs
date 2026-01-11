using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 设置字符串标志命令
    /// 格式：setstringflag(flagName, value)
    /// 示例：setstringflag(playerName, Alice) -> 设置playerName标志为"Alice"
    /// 注意：如果value包含逗号，需要用引号包裹，例如：setstringflag(message, "Hello, World")
    /// </summary>
    public class SetStringFlagCommand : VNCommand
    {
        public override string CommandName { get { return "setstringflag"; } }
        
        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[SetStringFlagCommand] 参数不能为空，格式：setstringflag(flagName, value)");
                return false;
            }
            
            // 解析参数：flagName,value
            // 处理可能包含逗号的字符串值（如果值用引号包裹）
            string flagName = "";
            string flagValue = "";
            
            int firstCommaIndex = args.IndexOf(',');
            if (firstCommaIndex > 0)
            {
                flagName = args.Substring(0, firstCommaIndex).Trim();
                string remaining = args.Substring(firstCommaIndex + 1).Trim();
                
                // 检查是否用引号包裹
                if (remaining.StartsWith("\"") && remaining.EndsWith("\""))
                {
                    // 移除首尾引号
                    flagValue = remaining.Substring(1, remaining.Length - 2);
                }
                else if (remaining.StartsWith("'") && remaining.EndsWith("'"))
                {
                    // 支持单引号
                    flagValue = remaining.Substring(1, remaining.Length - 2);
                }
                else
                {
                    // 没有引号，直接使用（可能是简单的字符串，不包含逗号）
                    flagValue = remaining;
                }
            }
            else
            {
                Debug.LogError("[SetStringFlagCommand] 参数格式错误，应为：setstringflag(flagName, value)");
                return false;
            }
            
            if (!string.IsNullOrEmpty(flagName))
            {
                // 保存标志到GlobalData
                GlobalDataManager.GetInstance().SetStringFlag(flagName, flagValue);
                Debug.Log($"[SetStringFlagCommand] 设置标志 {flagName} = \"{flagValue}\"");
                return true;
            }
            
            Debug.LogError("[SetStringFlagCommand] 标志名称不能为空");
            return false;
        }
        
        public override void Simulate(string args)
        {
            // 在模拟模式下也执行，因为flag设置是逻辑性的，不影响视觉效果
            Execute(args);
        }
    }
}

