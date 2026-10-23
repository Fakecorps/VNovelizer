using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Weather
{
    /// <summary>
    /// 天气/粒子特效管理器：particle_snow / particle_rain / particlecustom / unsetparticle 的运行时后端。
    ///
    /// <para>
    /// 【VFX 注册表扩展 2026-10-23】唯一真源 = VNProjectConfig.VFXParticleRegistry：
    /// 按 id 查注册表条目（未注册/已禁用 → 警告"未注册"），实例化条目 Prefab
    /// （必须挂 <see cref="VN_Particle"/>），强度控制只经 VN_Particle.SetIntensity 接口。
    /// 零配置（临时实例条目 Prefab 为空）时经 WeatherParticleFactory 运行时代码构建兜底——
    /// 兜底根对象同样补挂 VN_Particle，管理器路径统一。
    /// </para>
    ///
    /// <para>
    /// 懒单例（VN_WeatherParticle，DontDestroyOnLoad，场景无关）；每个特效一个条目。
    /// 强度渐变 = 协程 lerp（unscaledDeltaTime，不受快进 timeScale 影响）；衰减到 0 后
    /// 销毁条目。条目被外部销毁（如 VNAPI.ClearEffectLayer 清掉特效层子物体）时惰性剪枝
    /// 并在下次开启时重建。
    /// </para>
    ///
    /// <para>
    /// 粒子特效是持久视觉状态：随剧场清场（ClearTheater → ResetAll）复位；
    /// 不入存档（快进/读档经命令 Simulate 重建终态）。
    /// </para>
    /// </summary>
    public class WeatherParticleManager : MonoBehaviour
    {
        private static WeatherParticleManager _instance;

        public static WeatherParticleManager Instance => _instance;

        public static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("VN_WeatherParticle");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WeatherParticleManager>();
        }

        /// <summary>立即清空全部粒子特效（剧场清场时调用；无实例时 no-op）</summary>
        public static void ResetAll()
        {
            if (_instance != null) _instance.ClearAll();
        }

        private sealed class WeatherEntry
        {
            public string Id;
            public GameObject Root;
            public VN_Particle Particle;   // 管理器只认 VN_Particle 接口
            public float CurrentIntensity;
            public Coroutine Fade;
        }

        private readonly Dictionary<string, WeatherEntry> _entries =
            new Dictionary<string, WeatherEntry>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 注册表默认渐变时长（unsetparticle / particlecustom 缺省 duration 用）。
        /// 条目未注册/注册表为空时回退 0.5。
        /// </summary>
        public static float GetDefaultDuration(string id)
        {
            var config = VNProjectConfig.Instance;
            var entry = config != null ? config.GetParticleEntry(id) : null;
            return entry != null ? Mathf.Max(entry.DefaultDuration, 0f) : 0.5f;
        }

        /// <summary>全部可用粒子 ID（警告/诊断信息用；零配置回退 rain/snow）</summary>
        public static List<string> GetAvailableParticleIds()
        {
            var ids = new List<string>();
            var config = VNProjectConfig.Instance;
            if (config != null && config.VFXParticleRegistry != null)
            {
                foreach (var e in config.VFXParticleRegistry)
                    if (e != null && e.Enabled && !string.IsNullOrEmpty(e.Id) && !ids.Contains(e.Id))
                        ids.Add(e.Id);
            }
            if (ids.Count == 0)
            {
                ids.Add(WeatherParticleFactory.SnowId);
                ids.Add(WeatherParticleFactory.RainId);
            }
            return ids;
        }

        /// <summary>
        /// 开启/刷新粒子特效：intensity 0~1（发射率+亮度），duration 渐变秒数（0 = 立即）。
        /// 重复开启幂等（从当前强度渐变到目标强度）。id 未注册/已禁用返回 false。
        /// </summary>
        public bool SetWeather(string id, float intensity, float duration)
        {
            var entry = GetOrCreate(id);
            if (entry == null) return false;

            intensity = Mathf.Clamp01(intensity);
            RestartFade(entry, intensity, Mathf.Max(duration, 0f));
            return true;
        }

        /// <summary>关闭粒子特效：强度渐变衰减到 0 后销毁；name=all 关闭全部；未激活幂等。</summary>
        public void RemoveWeather(string id, float duration)
        {
            if (string.Equals(id, "all", System.StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kv in _entries)
                    RestartFade(kv.Value, 0f, Mathf.Max(duration, 0f));
                return;
            }

            if (!_entries.TryGetValue(id, out var entry)) return;
            RestartFade(entry, 0f, Mathf.Max(duration, 0f));
        }

        /// <summary>立即清空全部条目（不等渐变）</summary>
        public void ClearAll()
        {
            foreach (var kv in _entries)
            {
                if (kv.Value.Fade != null) StopCoroutine(kv.Value.Fade);
                if (kv.Value.Root != null) Destroy(kv.Value.Root);
            }
            _entries.Clear();
        }

        private void RestartFade(WeatherEntry entry, float target, float duration)
        {
            if (entry.Fade != null)
            {
                StopCoroutine(entry.Fade);
                entry.Fade = null;
            }
            entry.Fade = StartCoroutine(FadeTo(entry, target, duration));
        }

        private IEnumerator FadeTo(WeatherEntry entry, float target, float duration)
        {
            if (duration <= 0f)
            {
                ApplyIntensity(entry, target);
                entry.Fade = null;
                if (target <= 0f) DestroyEntry(entry);
                yield break;
            }

            float start = entry.CurrentIntensity;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                ApplyIntensity(entry, Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            ApplyIntensity(entry, target);
            entry.Fade = null;
            if (target <= 0f) DestroyEntry(entry);
        }

        private static void ApplyIntensity(WeatherEntry entry, float intensity)
        {
            if (entry.Root == null || entry.Particle == null) return;
            entry.CurrentIntensity = intensity;
            entry.Particle.SetIntensity(intensity); // 只经 VN_Particle 接口（发射率+亮度抽象）
        }

        private void DestroyEntry(WeatherEntry entry)
        {
            if (entry.Root != null) Destroy(entry.Root);
            _entries.Remove(entry.Id);
        }

        private WeatherEntry GetOrCreate(string id)
        {
            // 剪枝被外部销毁的条目（如 VNAPI.ClearEffectLayer 清掉特效层子物体）
            var dead = new List<string>();
            foreach (var kv in _entries)
                if (kv.Value.Root == null) dead.Add(kv.Key);
            foreach (var d in dead) _entries.Remove(d);

            if (_entries.TryGetValue(id, out var existing)) return existing;

            // 【VFX 注册表扩展】注册表校验：未注册/已禁用 → 警告并拒绝
            var registryEntry = VNProjectConfig.Instance != null
                ? VNProjectConfig.Instance.GetParticleEntry(id)
                : null;
            if (registryEntry == null)
            {
                Debug.LogWarning($"[WeatherParticle] 粒子 '{id}' 未在 VFX 注册表注册（或已禁用）。" +
                                 $"可用粒子: {string.Join(", ", GetAvailableParticleIds())}");
                return null;
            }

            Transform parent = VNAPI.GetEffectLayer();
            if (parent == null)
            {
                Debug.LogWarning($"[WeatherParticle] 特效层不可用，无法创建粒子特效: {id}");
                return null;
            }

            // 条目 Prefab 优先实例化；零配置临时实例 Prefab 为空 → 代码构建兜底（同挂 VN_Particle）
            GameObject root = registryEntry.Prefab != null
                ? Instantiate(registryEntry.Prefab, parent)
                : BuildRuntimeFallback(id, parent);
            if (root == null) return null;

            // 【2026-10-23 减法重构】不再自动补挂 VN_Particle——Prefab 必须自带（缺失时警告）
            var vnParticle = root.GetComponent<VN_Particle>();
            if (vnParticle == null)
            {
                Debug.LogWarning($"[WeatherParticle] 粒子 '{id}' 的 Prefab 未挂 VN_Particle 组件，强度控制将失效。" +
                                 "请在 Prefab 上添加 VN_Particle（并可移除本条目改用内置代码兜底）。");
                vnParticle = root.AddComponent<VN_Particle>();
            }

            var entry = new WeatherEntry
            {
                Id = id,
                Root = root,
                Particle = vnParticle,
            };
            _entries.Add(id, entry);
            return entry;
        }

        /// <summary>零配置兜底：临时实例条目 Prefab 为空时按内置 rain/snow 代码构建（历史行为）</summary>
        private static GameObject BuildRuntimeFallback(string id, Transform parent)
        {
            bool isSnow = string.Equals(id, WeatherParticleFactory.SnowId, System.StringComparison.OrdinalIgnoreCase);
            if (!isSnow && !string.Equals(id, WeatherParticleFactory.RainId, System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[WeatherParticle] 粒子 '{id}' 的注册表条目未指定 Prefab，且非内置 rain/snow，无法创建");
                return null;
            }
            return isSnow
                ? WeatherParticleFactory.CreateSnow(parent)
                : WeatherParticleFactory.CreateRain(parent);
        }
    }
}
