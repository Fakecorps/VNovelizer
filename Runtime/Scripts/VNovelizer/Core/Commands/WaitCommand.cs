using System.Collections;
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
            if (float.TryParse(args, out float seconds))
            {
                yield return new WaitForSeconds(seconds);
            }
        }
    }
}