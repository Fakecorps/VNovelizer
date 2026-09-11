using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 通用粒子播放命令
    /// 格式：playparticle(特效名)
    /// 示例：playparticle(Snow)
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "播放常驻粒子特效（雪/雨/花瓣等；同名特效自动去重不叠加）")]
    public class PlayParticleCommand : VNCommand
    {
        [VNParam(0, "effect", VNParamType.ParticleName,
            Description = "特效资源名（Particle 特效目录下的预制体）")]
        public override string CommandName { get { return "playparticle"; } }

        // 【Fix-15】"请求中"占位集合：GetObj 是异步回调，同名去重检查若只在回调外做，
        // 并行触发（连续两行 playparticle(Snow)/读档双重恢复）时两次调用都通过检查 → 粒子叠加。
        private static readonly HashSet<string> _pendingRequests = new HashSet<string>();

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
            // 【Fix-15】同步占位去重：已在加载中的同名特效直接跳过
            if (!_pendingRequests.Add(effectName)) return true;

            // 4. 加载资源 (支持 Config 配置路径)
            // 假设 Config.ParticalEffectPath = "VNovelizerRes/VFX/Partical"
            string path = VNProjectConfig.Instance.ParticalEffectPath + "/" + effectName;

            PoolManager.GetInstance().GetObj(path, (go) =>
            {
                _pendingRequests.Remove(effectName);
                if (go == null)
                {
                    Debug.LogError($"[PlayParticle] 找不到特效: {path}");
                    // 【Fix-15】加载失败回滚注册状态：特效并未真正激活，
                    // 否则存档会记录一个屏幕上不存在的粒子。
                    VNAPI.UnregisterEffect(effectName);
                    return;
                }

                // 【Fix-15】回调内二次去重：挂点可能已销毁或同名特效已创建（并行竞态）
                if (parent == null || parent.Find(objName) != null)
                {
                    Object.Destroy(go);
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