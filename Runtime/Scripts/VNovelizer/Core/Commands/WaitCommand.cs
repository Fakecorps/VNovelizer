using System.Collections;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 等待命令：格式 wait(秒数)。仅异步有效。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance, "等待指定秒数（阻塞所在链分支）")]
    public class WaitCommand : VNCommand
    {
        [VNParam(0, "seconds", VNParamType.Float, Min = 0f, Max = 30f, Default = "0.5",
            Description = "等待秒数")]
        public override string CommandName { get { return "wait"; } }

        public override bool Execute(string args)
        {
            Debug.LogWarning("Wait命令只能异步执行");
            return false;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            // 【Fix-62】InvariantCulture：小数点为逗号的系统上 wait(0.5) 必须按 '.' 解析
            if (float.TryParse(args, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
            {
                yield return new WaitForSeconds(seconds);
            }
        }
    }
}