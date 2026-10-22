using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Weather
{
    /// <summary>
    /// 天气粒子管理器：particle_snow / particle_rain / unsetparticle 的运行时后端。
    ///
    /// <para>
    /// 懒单例（VN_WeatherParticle，DontDestroyOnLoad，场景无关）；每个天气一个条目
    /// （GameObject + ParticleSystem + UIParticle，由 WeatherParticleFactory 纯代码构建）。
    /// 强度渐变 = 协程 lerp 发射率与 startColor alpha（unscaledDeltaTime，不受快进
    /// timeScale 影响）；衰减到 0 后销毁条目。条目被外部销毁（如
    /// VNAPI.ClearEffectLayer 清掉特效层子物体）时惰性剪枝并在下次开启时重建。
    /// </para>
    ///
    /// <para>
    /// 天气粒子是持久视觉状态：随剧场清场（ClearTheater → ResetAll）复位；
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

        /// <summary>立即清空全部天气粒子（剧场清场时调用；无实例时 no-op）</summary>
        public static void ResetAll()
        {
            if (_instance != null) _instance.ClearAll();
        }

        private sealed class WeatherEntry
        {
            public string Id;
            public GameObject Root;
            public ParticleSystem PS;
            public float BaseEmissionRate;
            public Color ColorA;   // 工厂基准色（alpha 由强度调制）
            public Color ColorB;
            public float CurrentIntensity;
            public Coroutine Fade;
        }

        private readonly Dictionary<string, WeatherEntry> _entries =
            new Dictionary<string, WeatherEntry>(System.StringComparer.OrdinalIgnoreCase);

        private static readonly string[] KnownIds =
            { WeatherParticleFactory.SnowId, WeatherParticleFactory.RainId };

        /// <summary>
        /// 开启/刷新天气：intensity 0~1（密度+亮度），duration 渐变秒数（0 = 立即）。
        /// 重复开启幂等（从当前强度渐变到目标强度）。
        /// </summary>
        public bool SetWeather(string id, float intensity, float duration)
        {
            var entry = GetOrCreate(id);
            if (entry == null) return false;

            intensity = Mathf.Clamp01(intensity);
            RestartFade(entry, intensity, Mathf.Max(duration, 0f));
            return true;
        }

        /// <summary>关闭天气：密度渐变衰减到 0 后销毁；name=all 关闭全部；未激活幂等。</summary>
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

        private void ApplyIntensity(WeatherEntry entry, float intensity)
        {
            if (entry.Root == null || entry.PS == null) return;
            entry.CurrentIntensity = intensity;

            var em = entry.PS.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(entry.BaseEmissionRate * intensity);

            // 亮度随强度（alpha 下限保留可见性）
            float alpha = Mathf.Lerp(0.3f, 1f, intensity);
            var main = entry.PS.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                entry.ColorA * new Color(1f, 1f, 1f, alpha),
                entry.ColorB * new Color(1f, 1f, 1f, alpha));
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

            if (_entries.TryGetValue(id, out var entry)) return entry;

            if (!IsKnownId(id))
            {
                Debug.LogWarning($"[WeatherParticle] 未知天气名: {id}（可选 rain/snow）");
                return null;
            }

            Transform parent = VNAPI.GetEffectLayer();
            if (parent == null)
            {
                Debug.LogWarning($"[WeatherParticle] 特效层不可用，无法创建天气: {id}");
                return null;
            }

            bool isSnow = string.Equals(id, WeatherParticleFactory.SnowId, System.StringComparison.OrdinalIgnoreCase);
            GameObject root = isSnow
                ? WeatherParticleFactory.CreateSnow(parent)
                : WeatherParticleFactory.CreateRain(parent);

            var ps = root.GetComponent<ParticleSystem>();
            entry = new WeatherEntry
            {
                Id = id,
                Root = root,
                PS = ps,
                BaseEmissionRate = ps.emission.rateOverTime.constant,
                ColorA = ps.main.startColor.colorMin,
                ColorB = ps.main.startColor.colorMax,
            };
            _entries.Add(id, entry);
            return entry;
        }

        private static bool IsKnownId(string id)
        {
            foreach (var k in KnownIds)
                if (string.Equals(id, k, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
