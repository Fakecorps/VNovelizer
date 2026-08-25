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

        /// <summary>
        /// 解锁是持久数据（GlobalData）而非演出状态，预演与执行行为一致。
        /// 【Fix】此前未覆写 Simulate：读档/跳行快进经过的行中 unlockcg 不生效，画廊解锁丢失。
        /// </summary>
        public override void Simulate(string args) => Execute(args);
    }
}