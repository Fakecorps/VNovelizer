using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    [VNCommandMeta(VNCommandCategory.System,
        "设置自动播放换行间隔（秒/字；持久保存到全局设置）")]
    public class SetAutoSpeedCommand : VNCommand
    {
        [VNParam(0, "speed", VNParamType.Float, Min = 0.01f, Max = 2f, Default = "1",
            Description = "秒/字（0.01-2，越小换行越快）")]
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

        /// <summary>
        /// 【Fix】此前未覆写 Simulate：快进（读档/跳行）经过的行中 setautospeed 丢失，读档后自动播放速度错误。
        /// 预演只更新持久数据（GlobalData），不触发 UI 事件——预演阶段面板可能未就绪。
        /// </summary>
        public override void Simulate(string args)
        {
            if (float.TryParse(args?.Trim(), out float speed))
            {
                GlobalDataManager.GetInstance().UpdateAutoSpeed(speed);
            }
        }
    }
}


