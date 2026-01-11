using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 配置命令
    /// </summary>
    public class ConfigCommand : VNCommand
    {
        public override string CommandName { get { return "config"; } }
        
        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("Config命令参数不能为空");
                return false;
            }
            
            // 解析参数：key:value
            string[] parts = args.Split(':');
            if (parts.Length >= 2)
            {
                string key = parts[0].Trim().ToLower();
                string value = parts[1].Trim().ToLower();
                
                VNManager manager = VNManager.GetInstance();
                
                switch (key)
                {
                    case "voice":
                        // 设置语音开关
                        bool voiceEnabled = value == "true";
                        GlobalDataManager.GetInstance().UpdateVolumeSettings(
                            GlobalDataManager.GetInstance().GetGlobalData().MasterVolume,
                            GlobalDataManager.GetInstance().GetGlobalData().BGMVolume,
                            voiceEnabled ? 1f : 0f,
                            GlobalDataManager.GetInstance().GetGlobalData().SFXVolume
                        );
                        break;
                    case "textspeed":
                        // 设置文本速度
                        if (float.TryParse(value, out float textSpeed))
                        {
                            GlobalDataManager.GetInstance().UpdateTextSpeed(textSpeed);
                        }
                        break;
                    case "autospeed":
                        // 设置自动播放速度
                        if (float.TryParse(value, out float autoSpeed))
                        {
                            GlobalDataManager.GetInstance().UpdateAutoSpeed(autoSpeed);
                        }
                        break;
                    default:
                        Debug.LogWarning($"未知的配置项: {key}");
                        break;
                }
                
                return true;
            }
            
            Debug.LogError("Config命令参数格式错误，应为key:value");
            return false;
        }
    }
}