using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 解锁CG命令
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Logic,
        "解锁 CG 画廊条目（持久数据，读档快进也生效）")]
    public class UnlockCGCommand : VNCommand
    {
        [VNParam(0, "name", VNParamType.String,
            Description = "CG 画廊条目名（画廊编辑器中登记的名称）")]
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