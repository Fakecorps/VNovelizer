using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    public class SetTextSpeedCommand : VNCommand
    {
        public override string CommandName { get { return "settextspeed"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[TextSpeed] 参数不能为空！请填写速度值 (秒/字)");
                return false;
            }

            // 1. 解析参数
            float newSpeed = 0.05f; // 默认值

            // 尝试解析，如果失败（比如填了非数字），TryParse 会返回 false
            if (float.TryParse(args.Trim(), out newSpeed))
            {

                VNAPI.SetTextSpeed(newSpeed);

                EventCenter.GetInstance().EventTrigger("TextSpeedChanged");

                Debug.Log($"[TextSpeed] 打字速度已设置为: {newSpeed}");
                return true;
            }
            else
            {
                Debug.LogError($"[TextSpeed] 参数格式错误: {args}。请输入数字。");
                return false;
            }
        }
    }
}

