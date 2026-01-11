using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 设置整数标志命令
    /// 格式：setintflag(flagName, value)
    /// 示例：setintflag(score, 100) -> 设置score标志为100
    /// </summary>
    public class SetIntFlagCommand : VNCommand
    {
        public override string CommandName { get { return "setintflag"; } }
        
        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[SetIntFlagCommand] 参数不能为空，格式：setintflag(flagName, value)");
                return false;
            }
            
            // 解析参数：flagName,value
            string[] parts = args.Split(',');
            if (parts.Length >= 2)
            {
                string flagName = parts[0].Trim();
                if (int.TryParse(parts[1].Trim(), out int flagValue))
                {
                    // 保存标志到GlobalData
                    GlobalDataManager.GetInstance().SetIntFlag(flagName, flagValue);
                    Debug.Log($"[SetIntFlagCommand] 设置标志 {flagName} = {flagValue}");
                    return true;
                }
                else
                {
                    Debug.LogError($"[SetIntFlagCommand] 无法解析整数值: {parts[1].Trim()}");
                    return false;
                }
            }
            
            Debug.LogError("[SetIntFlagCommand] 参数格式错误，应为：setintflag(flagName, value)");
            return false;
        }
        
        public override void Simulate(string args)
        {
            // 在模拟模式下也执行，因为flag设置是逻辑性的，不影响视觉效果
            Execute(args);
        }
    }
}

