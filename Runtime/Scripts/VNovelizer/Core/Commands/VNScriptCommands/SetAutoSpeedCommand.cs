using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    public class SetAutoSpeedCommand : VNCommand
    {
        public override string CommandName { get { return "setautospeed"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[AutoSpeed] 参数不能为空！请填写速度值 (秒/字)");
                return false;
            }

            float newSpeed = 1.0f;
            if (float.TryParse(args.Trim(), out newSpeed))
            {
                VNAPI.SetAutoSpeed(newSpeed);
                EventCenter.GetInstance().EventTrigger("AutoSpeedChanged");
                Debug.Log($"[AutoSpeed] 打字速度已设置为: {newSpeed}");
                return true;
            }
            else
            {
                Debug.Log($"[AutoSpeed] 无法解析参数: {args.Trim()}");
                return false;
            }
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            yield return null;
        }

        public override void Interrupt()
        {

        }
    }
}


