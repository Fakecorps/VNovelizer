using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 解锁回忆
    /// </summary>
    public class UnlockSceneCommand : VNCommand
    {
        public override string CommandName { get { return "unlockscene"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("UnlockCG命令参数不能为空");
                return false;
            }

            string cName = args.Trim();
            GlobalDataManager.GetInstance().UnlockScene(cName);

            return true;
        }
    }
}