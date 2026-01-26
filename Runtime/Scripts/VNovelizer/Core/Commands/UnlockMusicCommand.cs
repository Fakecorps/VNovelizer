using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 解锁音乐
    /// </summary>
    public class UnlockMusicCommand : VNCommand
    {
        public override string CommandName { get { return "unlockmusic"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("UnlockCG命令参数不能为空");
                return false;
            }

            string mName = args.Trim();
            GlobalDataManager.GetInstance().UnlockMusic(mName);

            return true;
        }
    }
}