using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 通用粒子播放命令
    /// 格式：playparticle(特效名)
    /// 示例：playparticle(Snow)
    /// </summary>
    public class PlayParticleCommand : VNCommand
    {
        public override string CommandName { get { return "playparticle"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;
            string effectName = args.Trim();

            // 1. 注册状态 (必须！)
            VNAPI.RegisterEffect(effectName);

            // 2. 获取挂点
            Transform parent = VNAPI.GetEffectLayer();
            if (parent == null) return false;

            // 3. 检查是否已存在 (防止叠加)
            // 约定：生成的物体名字叫 "VNEffect_特效名"
            string objName = "VNEffect_" + effectName;
            if (parent.Find(objName) != null) return true;

            // 4. 加载资源 (支持 Config 配置路径)
            // 假设 Config.ParticalEffectPath = "VNovelizerRes/VFX/Partical"
            string path = VNProjectConfig.Instance.ParticalEffectPath + "/" + effectName;

            PoolManager.GetInstance().GetObj(path, (go) =>
            {
                if (go == null)
                {
                    Debug.LogError($"[PlayParticle] 找不到特效: {path}");
                    return;
                }

                go.name = objName; // 统一命名规则
                go.transform.SetParent(parent, false);

                // UI 适配
                RectTransform rect = go.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    rect.localScale = Vector3.one;
                }

                // 播放逻辑
                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var em = ps.emission;
                    em.enabled = true;
                    ps.Play();
                }

                // UIParticle 支持
                var uiParticle = go.GetComponent<Coffee.UIExtensions.UIParticle>();
                if (uiParticle != null) uiParticle.Play();
            });

            return true;
        }

        public override void Simulate(string args)
        {
            if (!string.IsNullOrEmpty(args))
            {
                VNAPI.RegisterEffect(args.Trim());
            }
        }
    }
}