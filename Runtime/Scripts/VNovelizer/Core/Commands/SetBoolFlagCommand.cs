using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 设置布尔标志命令
    /// 格式：setboolflag(flagName) 或 setboolflag(flagName, false)（缺省为 true）
    /// 经 FlagService 按注册表作用域路由（Global 持久 / Save 随档回退）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Logic,
        "设置布尔标志（缺省为 true）")]
    public class SetBoolFlagCommand : VNCommand
    {
        [VNParam(0, "flag", VNParamType.String,
            Description = "标志名（区分大小写）")]
        [VNParam(1, "value", VNParamType.Bool, Default = "true",
            Optional = true, Description = "true / false（缺省 true）")]
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
                if (string.IsNullOrEmpty(flagName))
                {
                    Debug.LogError("SetFlag命令参数格式错误，应为flagName或flagName,value");
                    return false;
                }
                bool flagValue = parts.Length >= 2 ? bool.Parse(parts[1].Trim()) : true;

                // 经 FlagService 作用域路由保存
                FlagService.GetInstance().SetBool(flagName, flagValue);

                return true;
            }

            Debug.LogError("SetFlag命令参数格式错误，应为flagName或flagName,value");
            return false;
        }

        /// <summary>
        /// 【Fix P2】此前未重写 Simulate，快进（读档/跳行/开局进入）经过的行中 setboolflag 不生效，
        /// 与 setintflag/setstringflag 行为不一致。
        /// </summary>
        public override void Simulate(string args)
        {
            // flag 设置是纯逻辑操作，不影响视觉/音频，模拟模式与执行模式行为一致
            Execute(args);
        }
    }
}
