using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VNovelizer.Core.Commands;
using System;
using System.Collections;

namespace VNovelizer.Core.API
{

    public static class VNAPI
    {
        #region Gameplay Panel Access

        private static VNGameplayPanel GetPanel()
        {
            var panel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
            if (panel == null)
            {
                // 尝试直接在场景里找 (应对 UIManager 字典更新延迟)
                panel = UnityEngine.Object.FindFirstObjectByType<VNGameplayPanel>();
            }
            return panel;
        }

        /// <summary>
        /// 获取前背景图 (用于正常显示)
        /// </summary>
        public static Image GetBG_F()
        {
            var panel = GetPanel();
            return panel != null ? panel.GetBG_F() : null;
        }

        /// <summary>
        /// 获取后背景图 (用于淡入淡出过渡)
        /// </summary>
        public static Image GetBG_B()
        {
            var panel = GetPanel();
            return panel != null ? panel.GetBG_B() : null;
        }

        /// <summary>
        /// 获取指定位置的角色 RectTransform
        /// </summary>
        /// <param name="posCode">L, M, R</param>
        public static RectTransform GetCharRect(string posCode)
        {
            var panel = GetPanel();
            return panel != null ? panel.GetCharRect(posCode) : null;
        }

        /// <summary>
        /// 获取指定位置的角色 Image
        /// </summary>
        /// <param name="posCode">L, M, R</param>
        public static Image GetCharImage(string posCode)
        {
            var panel = GetPanel();
            return panel != null ? panel.GetCharImage(posCode) : null;
        }

        public static float GetCharScaleX(string posCode) => VNManager.GetInstance().GetCharacterScaleX(posCode);//获取角色朝向
        public static void SetCharScaleX(string posCode, float scaleX) => VNManager.GetInstance().SetCharacterScaleX(posCode, scaleX);//设置角色朝向
        public static TMP_Text GetDialogueText()
        {
            var panel = GetPanel();

            return panel != null ? panel.GetDialogueText() : null;
        }

        /// <summary>
        /// 获取说话人姓名框组件 (TMP_Text)
        /// </summary>
        public static Image GetSpeakerBox()
        {
            var panel = GetPanel();
            return panel != null ? panel.GetSpeakerBox() : null;
        }

        /// <summary>
        /// 设置说话人姓名框的 Sprite
        /// </summary>
        /// <param name="box">姓名框 Sprite</param>
        public static void SetSpeakerBox(Sprite box)
        {
            var panel = GetPanel();
            if (panel != null)
            {
                Image speakerBox = panel.GetSpeakerBox();
                if (speakerBox != null)
                {
                    speakerBox.sprite = box;
                }
            }
        }

        /// <summary>
        /// 设置说话人（会根据 CharacterProfile.SpeakerBox 自动决定显示方式）
        /// </summary>
        /// <param name="speaker">说话人ID或名称</param>
        public static void SetSpeaker(string speaker)
        {
            var panel = GetPanel();
            if (panel != null)
            {
                panel.UpdateSpeakerDisplay(speaker);
            }
        }

        /// <summary>
        /// 获取说话人名字文本组件 (TMP_Text)
        /// </summary>
        public static TMP_Text GetSpeakerText()
        {
            var panel = GetPanel();

            return panel != null ? panel.GetSpeakerText() : null;
        }
        public static Transform GetEffectLayer()
        {
            var panel = GetPanel();
            return panel != null ? panel.GetEffectLayer() : null;
        }

        /// <summary>
        /// 获取当前文本播放速度 (秒/字)
        /// </summary>
        public static float GetTextSpeed()
        {
            var data = GlobalDataManager.GetInstance().GetGlobalData();
            return data != null ? data.TextSpeed : 0.05f;
        }

        /// <summary>
        /// 设置文本播放速度
        /// </summary>
        /// <param name="speed">秒/字 (越小越快)</param>
        public static void SetTextSpeed(float speed)
        {
            GlobalDataManager.GetInstance().UpdateTextSpeed(speed);
        }

        /// <summary>
        /// 获取自动播放等待时间 (秒)
        /// </summary>
        public static float GetAutoSpeed()
        {
            var data = GlobalDataManager.GetInstance().GetGlobalData();
            return data != null ? data.AutoSpeed : 1.0f;
        }

        /// <summary>
        /// 设置自动播放等待时间
        /// </summary>
        /// <param name="speed">秒</param>
        public static void SetAutoSpeed(float speed)
        {
            GlobalDataManager.GetInstance().UpdateAutoSpeed(speed);
        }
        
        #endregion
        
        #region Flag Management (标志管理)
        
        /// <summary>
        /// 设置游戏标志（bool类型）
        /// </summary>
        /// <param name="flagName">标志名称</param>
        /// <param name="value">标志值</param>
        public static void SetBoolFlag(string flagName, bool value)
        {
            GlobalDataManager.GetInstance().SetBoolFlag(flagName, value);
        }
        
        /// <summary>
        /// 获取游戏标志（bool类型）
        /// </summary>
        /// <param name="flagName">标志名称</param>
        /// <returns>标志值，如果不存在则返回false</returns>
        public static bool GetBoolFlag(string flagName)
        {
            return GlobalDataManager.GetInstance().GetBoolFlag(flagName);
        }
        
        /// <summary>
        /// 设置游戏标志（int类型）
        /// </summary>
        /// <param name="flagName">标志名称</param>
        /// <param name="value">标志值</param>
        public static void SetIntFlag(string flagName, int value)
        {
            GlobalDataManager.GetInstance().SetIntFlag(flagName, value);
        }
        
        /// <summary>
        /// 获取游戏标志（int类型）
        /// </summary>
        /// <param name="flagName">标志名称</param>
        /// <returns>标志值，如果不存在则返回0</returns>
        public static int GetIntFlag(string flagName)
        {
            return GlobalDataManager.GetInstance().GetIntFlag(flagName);
        }
        
        /// <summary>
        /// 设置游戏标志（string类型）
        /// </summary>
        /// <param name="flagName">标志名称</param>
        /// <param name="value">标志值</param>
        public static void SetStringFlag(string flagName, string value)
        {
            GlobalDataManager.GetInstance().SetStringFlag(flagName, value);
        }
        
        /// <summary>
        /// 获取游戏标志（string类型）
        /// </summary>
        /// <param name="flagName">标志名称</param>
        /// <returns>标志值，如果不存在则返回空字符串</returns>
        public static string GetStringFlag(string flagName)
        {
            return GlobalDataManager.GetInstance().GetStringFlag(flagName);
        }
        
        #endregion

        public static void ClearAllEffects()
        {
            Transform effectLayer = GetEffectLayer();
            if (effectLayer == null) return;

            // 倒序遍历销毁子物体，防止索引越界
            for (int i = effectLayer.childCount - 1; i >= 0; i--)
            {
                Transform child = effectLayer.GetChild(i);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        public static void PlayVideo(string videoName, System.Action onComplete)
        {
            Transform parent = UIManager.GetInstance().GetLayerFather(E_UI_Layer.System);
            if (parent == null)
            {
                Debug.Log("[VNAPI] 找不到系统层父物体");
                onComplete?.Invoke();
                return;
            }

            // 2. 加载预制体
            string path = VNProjectConfig.Instance.VideoObjPath;
            GameObject prefab = ResourcesManager.GetInstance().Load<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogError($"[VNAPI] 找不到视频播放器预制体: {path}");
                onComplete?.Invoke();
                return;
            }

            // 3. 实例化并播放
            GameObject go = UnityEngine.Object.Instantiate(prefab, parent);

            // 确保全屏铺满
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            // 启动
            var player = go.GetComponent<VideoModel>();
            player.Play(videoName, onComplete);
        }
        public static void ShowPrompt(string text, float duration)
        {
            var panel = GetPanel();
            if (panel != null) panel.ShowPrompt(text, duration);
        }
        #region Game Flow Control

        /// <summary>
        /// 切换背景数据 (不刷新UI，仅更新内部状态)
        /// </summary>
        public static void UpdateBGData(string bgName)
        {
            VNManager.GetInstance().UpdateCurrentBG_OnlyData(bgName);
        }

        /// <summary>
        /// 执行命令字符串
        /// </summary>
        public static void ExecuteCommand(string cmd)
        {
            CommandManager.GetInstance().ExecuteCommand(cmd);
        }

        public static void RegisterEffect(string name)
        { 
            VNManager.GetInstance().RegisterEffect(name);
        }

        public static void UnregisterEffect(string name)
        { 
            VNManager.GetInstance().UnregisterEffect(name);
        }
        #endregion
        #region Coroutine Control (协程控制)

        /// <summary>
        /// 启动协程 (封装 MonoManager)
        /// </summary>
        /// <param name="routine">协程迭代器</param>
        /// <returns>协程对象引用</returns>
        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            return MonoManager.GetInstance().StartCoroutine(routine);
        }

        public static Coroutine StartCoroutine(string methodName)
        {
            return MonoManager.GetInstance().StartCoroutine(methodName);
        }

        public static Coroutine StartCoroutine(string methodName, object value)
        { 
            return MonoManager.GetInstance().StartCoroutine(methodName, value);
        }

        public static Coroutine StartCoroutine_Auto(IEnumerator routine)
        { 
            return MonoManager.GetInstance().StartCoroutine_Auto(routine);
        }
        /// <summary>
        /// 停止指定协程
        /// </summary>
        /// <param name="routine">要停止的协程对象</param>
        public static void StopCoroutine(Coroutine routine)
        {
            MonoManager.GetInstance().StopCoroutine(routine);
        }

        /// <summary>
        /// 停止所有协程 (慎用！会停止包括背景音乐、自动播放等所有逻辑)
        /// </summary>
        public static void StopAllCoroutines()
        {
            MonoManager.GetInstance().StopAllCoroutines();
        }

        #endregion
    }
}