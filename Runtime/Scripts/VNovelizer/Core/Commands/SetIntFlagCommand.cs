using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Diagnostics;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 设置整数标志命令
    /// 格式：
    ///   setintflag(flagName, 100)   → 绝对赋值
    ///   setintflag(flagName, +10)   → 相对运算（当前值 +10；支持 + - * /，/ 为整数除法）
    /// 经 FlagService 按注册表作用域路由（Global 持久 / Save 随档回退）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Logic,
        "设置整数标志（支持 +10/-10/*2//2 相对运算）")]
    public class SetIntFlagCommand : VNCommand
    {
        [VNParam(0, "flag", VNParamType.String,
            Description = "标志名（区分大小写）")]
        [VNParam(1, "value", VNParamType.String,
            Description = "整数值或相对运算：100（绝对）/ +10 / -10 / *2 / /2（整数除法）")]
        public override string CommandName { get { return "setintflag"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[SetIntFlagCommand] 参数不能为空，格式：setintflag(flagName, value)，value 支持 +10/-10/*2//2 相对运算");
                return false;
            }

            // 解析参数：flagName,value
            string[] parts = args.Split(',');
            if (parts.Length >= 2)
            {
                string flagName = parts[0].Trim();
                string valueStr = parts[1].Trim();

                // 1) 绝对赋值
                int flagValue;
                if (int.TryParse(valueStr, out flagValue))
                {
                    FlagService.GetInstance().SetInt(flagName, flagValue);
                    VNDebug.LogVerbose($"[SetIntFlagCommand] 设置标志 {flagName} = {flagValue}");
                    return true;
                }

                // 2) 相对运算（+ - * / 前缀，operand 为整数）
                if (valueStr.Length >= 2 && IsRelativeOp(valueStr[0]))
                {
                    int operand;
                    if (int.TryParse(valueStr.Substring(1).Trim(), out operand))
                    {
                        int result = FlagService.GetInstance().ApplyIntOperation(flagName, valueStr[0], operand);
                        VNDebug.LogVerbose($"[SetIntFlagCommand] 标志 {flagName} = {result}（{valueStr}）");
                        return true;
                    }
                }

                Debug.LogError($"[SetIntFlagCommand] 无法解析整数值: {valueStr}（支持 100 / +10 / -10 / *2 / /2）");
                return false;
            }

            Debug.LogError("[SetIntFlagCommand] 参数格式错误，应为：setintflag(flagName, value)");
            return false;
        }

        private static bool IsRelativeOp(char c)
        {
            return c == '+' || c == '-' || c == '*' || c == '/';
        }

        public override void Simulate(string args)
        {
            // 在模拟模式下也执行，因为flag设置是逻辑性的，不影响视觉效果
            Execute(args);
        }
    }
}
