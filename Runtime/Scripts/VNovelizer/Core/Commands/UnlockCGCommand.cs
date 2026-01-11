using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 解锁CG命令
    /// </summary>
    public class UnlockCGCommand : VNCommand
    {
        public override string CommandName { get { return "unlockcg"; } }
        
        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("UnlockCG命令参数不能为空");
                return false;
            }
            
            string cgName = args.Trim();
            GlobalDataManager.GetInstance().UnlockCG(cgName);
            
            return true;
        }
    }
}