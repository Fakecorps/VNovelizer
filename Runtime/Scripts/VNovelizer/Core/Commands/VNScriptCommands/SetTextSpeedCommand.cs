using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    [VNCommandMeta(VNCommandCategory.System,
        "设置打字机速度（秒/字，越小越快；持久保存到全局设置）")]
    public class SetTextSpeedCommand : VNCommand
    {
        [VNParam(0, "speed", VNParamType.Float, Min = 0.001f, Max = 1f, Default = "0.05",
            Description = "秒/字（0.001-1，越小越快）")]
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

        /// <summary>
        /// 【Fix】此前未覆写 Simulate：快进（读档/跳行）经过的行中 settextspeed 丢失，读档后打字速度错误。
        /// 预演只更新持久数据（GlobalData），不触发 UI 事件——预演阶段面板可能未就绪。
        /// </summary>
        public override void Simulate(string args)
        {
            if (float.TryParse(args?.Trim(), out float speed))
            {
                GlobalDataManager.GetInstance().UpdateTextSpeed(speed);
            }
        }
    }
}

