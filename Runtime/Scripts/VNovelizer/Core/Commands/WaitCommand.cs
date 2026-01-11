using System.Collections;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    public class WaitCommand : VNCommand
    {
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