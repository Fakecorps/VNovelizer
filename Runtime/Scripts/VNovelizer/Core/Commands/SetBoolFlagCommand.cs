using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 设置标志命令
    /// </summary>
    public class SetBoolFlagCommand : VNCommand
    {
        public override string CommandName { get { return "setboolflag"; } }
        
        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("SetFlag命令参数不能为空");
                return false;
            }
            
            // 解析参数：flagName或flagName,value
            string[] parts = args.Split(',');
            if (parts.Length >= 1)
            {
                string flagName = parts[0].Trim();
                bool flagValue = parts.Length >= 2 ? bool.Parse(parts[1].Trim()) : true;
                
                // 保存标志到GlobalData
                GlobalDataManager.GetInstance().GetGlobalData().SetFlag(flagName, flagValue);
                
                return true;
            }
            
            Debug.LogError("SetFlag命令参数格式错误，应为flagName或flagName,value");
            return false;
        }
    }
}