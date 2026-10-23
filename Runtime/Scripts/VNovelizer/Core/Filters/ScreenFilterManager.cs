using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Filters
{
    /// <summary>
    /// 全屏滤镜管理器（ScreenFilterSystem 运行时核心）。
    ///
    /// <para>
    /// 机制（跨管线统一，零 URP 代码）：
    /// ① 有滤镜激活时，剧场相机渲染到屏幕尺寸 RenderTexture（camera.targetTexture ——
    ///    Built-in / URP 行为一致的引擎通用 API，不用 OnRenderImage / RendererFeature）；
    /// ② 每帧 WaitForEndOfFrame 把相机 RT 按滤镜栈做 ping-pong Blit 链（N 个滤镜 N 次
    ///    Blit，栈式叠加；每次 Blit 写入该滤镜的当前混合权重 _Weight）；
    /// ③ 最终 RT 交给 VN_EffectCanvas（sortingOrder=5）下新增的全屏 RawImage 显示——
    ///    层级天然位于剧场画面之上、Gameplay 对话框(10)之下，因此<b>滤镜只作用于剧场画面，
    ///    对话框等 UI 保持清晰</b>（与官方 camerafx 规划的作用域一致）。
    /// </para>
    ///
    /// <para>
    /// 生命周期：SetFilter/UnsetFilter 以权重渐变（duration）驱动淡入淡出；权重到 0 的
    /// 条目自动移除并销毁材质；栈空时恢复相机直接渲染并隐藏滤镜层。全部滤镜随剧场清场
    /// （ClearTheater → ResetAllFilters）复位。滤镜为瞬态演出状态，不入存档
    /// （快进/读档经命令 Simulate 立即重建到终态）。
    /// </para>
    /// </summary>
    public class ScreenFilterManager : MonoBehaviour
    {
        private static ScreenFilterManager _instance;

        /// <summary>全局单例（懒创建 DontDestroyOnLoad 宿主，场景无关）</summary>
        public static ScreenFilterManager Instance
        {
            get
            {
                if (_instance == null) EnsureInstance();
                return _instance;
            }
        }

        /// <summary>确保单例宿主存在（幂等）</summary>
        public static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("VN_ScreenFilter");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ScreenFilterManager>();
        }

        /// <summary>清空全部滤镜并恢复相机直接渲染（剧场清场时调用；无实例时 no-op）</summary>
        public static void ResetAllFilters()
        {
            if (_instance != null) _instance.ClearAll();
        }

        // ==================== 活跃滤镜条目 ====================

        private sealed class ActiveFilter
        {
            public string FilterId;
            public Material Material;    // 滤镜专属材质实例（每帧写入 _Weight）
            public float Weight;         // 当前混合权重（对应 shader _Weight）
            public float TargetWeight;   // 淡入目标（filtercustom 的 strength 缩放后可为 0~1）；0 = 淡出后移除
            public float Speed;          // 权重变化速率（权重/秒；0 = 立即）
        }

        private readonly List<ActiveFilter> _stack = new List<ActiveFilter>();

        // ==================== 捕获管线 ====================

        private RenderTexture _cameraRT;
        private RenderTexture _pingA;
        private RenderTexture _pingB;
        private RawImage _filterImage;
        private Coroutine _frameLoop;
        private int _rtWidth;
        private int _rtHeight;

        /// <summary>当前是否有激活滤镜（含淡入淡出中的条目）</summary>
        public bool HasAnyFilter => _stack.Count > 0;

        /// <summary>指定滤镜是否处于激活/淡入淡出中（大小写不敏感）</summary>
        public bool IsFilterActive(string filterId)
        {
            if (string.IsNullOrEmpty(filterId)) return false;
            for (int i = 0; i < _stack.Count; i++)
                if (string.Equals(_stack[i].FilterId, filterId, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // ==================== 公共 API ====================

        /// <summary>
        /// 开启滤镜（已在栈中 → 淡入到满权重；未在栈中 → 实例化材质并加入栈底淡入）。
        /// duration &lt;= 0 表示立即满权重。未知滤镜名 / shader 解析失败返回 false。
        /// </summary>
        public bool SetFilter(string filterId, float duration)
        {
            return SetFilterWithParams(filterId, duration, null);
        }

        /// <summary>
        /// 开启滤镜并覆盖其 shader 参数（<c>filter_xxx</c> 逐滤镜命令使用）。
        /// <paramref name="paramOverrides"/> = shader 属性名 → 值（仅 float 属性；命令已用
        /// 预设默认值补齐全部可调参数，因此每次调用都写入完整参数集，重复开启幂等）。
        /// 其余语义与 <see cref="SetFilter"/> 完全一致。
        /// </summary>
        public bool SetFilterWithParams(string filterId, float duration, Dictionary<string, float> paramOverrides)
        {
            return SetFilterCore(filterId, duration, paramOverrides, 1f);
        }

        /// <summary>
        /// 开启滤镜并按 strength 缩放整体强弱（<c>filtercustom</c> 命令使用）：
        /// 混合权重目标 = <paramref name="strength"/>（0~1），只乘全局 _Weight，
        /// 不暴露单个 shader 参数（shader 属性取资产默认值）。
        /// </summary>
        public bool SetFilterScaled(string filterId, float strength, float duration)
        {
            return SetFilterCore(filterId, duration, null, Mathf.Clamp01(strength));
        }

        /// <summary>开启滤镜核心：注册表校验 → shader 解析 → 权重目标淡入（重复开启幂等刷新）</summary>
        private bool SetFilterCore(string filterId, float duration, Dictionary<string, float> paramOverrides, float targetWeight)
        {
            if (string.IsNullOrEmpty(filterId)) return false;

            var library = VNFilterLibrary.GetInstance();
            var entry = library.GetEntry(filterId);
            if (entry == null)
            {
                Debug.LogWarning($"[ScreenFilterManager] 滤镜 '{filterId}' 未在 VFX 注册表注册（或已禁用）。" +
                                 $"可用滤镜: {string.Join(", ", library.GetAllIds())}");
                return false;
            }
            var shader = library.ResolveShader(filterId);
            if (shader == null)
            {
                Debug.LogWarning($"[ScreenFilterManager] 滤镜 '{filterId}' 的 Shader 无法加载（注册表条目 Shader 引用为空或内置 shader 缺失）");
                return false;
            }

            ActiveFilter existing = Find(filterId);
            if (existing != null)
            {
                ApplyOverrides(existing.Material, paramOverrides);
                existing.TargetWeight = targetWeight;
                existing.Speed = duration > 0f ? 1f / duration : 0f;
                if (duration <= 0f) existing.Weight = targetWeight;
                return true;
            }

            var mat = new Material(shader) { name = "VNFilterMat_" + entry.Id };
            ApplyOverrides(mat, paramOverrides);
            mat.SetFloat("_Weight", 0f);

            _stack.Add(new ActiveFilter
            {
                FilterId = entry.Id,
                Material = mat,
                Weight = duration > 0f ? 0f : targetWeight,
                TargetWeight = targetWeight,
                Speed = duration > 0f ? 1f / duration : 0f,
            });
            return true;
        }

        /// <summary>把参数覆盖写入材质（shader 无该属性时 SetFloat 静默无效，安全）</summary>
        private static void ApplyOverrides(Material material, Dictionary<string, float> overrides)
        {
            if (material == null || overrides == null) return;
            foreach (var kv in overrides)
                if (!string.IsNullOrEmpty(kv.Key))
                    material.SetFloat(kv.Key, kv.Value);
        }

        /// <summary>开启滤镜并立即满权重（命令 Execute/Simulate/Interrupt 的快进终态语义）</summary>
        public void SetFilterImmediate(string filterId)
        {
            SetFilter(filterId, 0f);
        }

        /// <summary>
        /// 关闭滤镜：权重淡出到 0 后移除（duration &lt;= 0 立即移除）。
        /// filterId = "all" 关闭全部。未激活时幂等静默。
        /// </summary>
        public void UnsetFilter(string filterId, float duration)
        {
            if (string.IsNullOrEmpty(filterId)) return;
            float speed = duration > 0f ? 1f / duration : 0f;

            if (string.Equals(filterId, "all", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    var af = _stack[i];
                    af.TargetWeight = 0f;
                    af.Speed = speed;
                    if (duration <= 0f) RemoveEntry(af);
                }
                return;
            }

            var entry = Find(filterId);
            if (entry == null) return;
            entry.TargetWeight = 0f;
            entry.Speed = speed;
            if (duration <= 0f) RemoveEntry(entry);
        }

        /// <summary>立即移除滤镜（命令 Execute/Simulate/Interrupt 的快进终态语义）</summary>
        public void RemoveFilterImmediate(string filterId)
        {
            if (string.IsNullOrEmpty(filterId)) return;
            if (string.Equals(filterId, "all", StringComparison.OrdinalIgnoreCase))
            {
                ClearAll();
                return;
            }
            var entry = Find(filterId);
            if (entry != null) RemoveEntry(entry);
        }

        /// <summary>清空全部滤镜并恢复相机直接渲染</summary>
        public void ClearAll()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Material != null) Destroy(_stack[i].Material);
                _stack.RemoveAt(i);
            }
            DeactivateCapture();
        }

        // ==================== 内部实现 ====================

        private ActiveFilter Find(string filterId)
        {
            for (int i = 0; i < _stack.Count; i++)
                if (string.Equals(_stack[i].FilterId, filterId, StringComparison.OrdinalIgnoreCase))
                    return _stack[i];
            return null;
        }

        private void RemoveEntry(ActiveFilter entry)
        {
            _stack.Remove(entry);
            if (entry.Material != null) Destroy(entry.Material);
            if (_stack.Count == 0) DeactivateCapture();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _frameLoop = StartCoroutine(FrameLoop());
        }

        private void OnDestroy()
        {
            DeactivateCapture();
            for (int i = 0; i < _stack.Count; i++)
                if (_stack[i].Material != null) Destroy(_stack[i].Material);
            _stack.Clear();
            ReleaseRTs();
            if (_instance == this) _instance = null;
        }

        /// <summary>帧末循环：权重推进 → 移除完成条目 → 捕获激活检查 → Blit 链</summary>
        private IEnumerator FrameLoop()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (_stack.Count == 0) continue;

                float dt = Time.deltaTime;
                for (int i = 0; i < _stack.Count; i++)
                {
                    var af = _stack[i];
                    if (af.Speed <= 0f) af.Weight = af.TargetWeight;
                    else af.Weight = Mathf.MoveTowards(af.Weight, af.TargetWeight, af.Speed * dt);
                }

                // 淡出完成的条目移除
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    var af = _stack[i];
                    if (af.Weight <= 0.0001f && af.TargetWeight <= 0.0001f)
                    {
                        _stack.RemoveAt(i);
                        if (af.Material != null) Destroy(af.Material);
                    }
                }
                if (_stack.Count == 0)
                {
                    DeactivateCapture();
                    continue;
                }

                var camera = SceneCameraManager.GetInstance().Camera;
                if (camera == null)
                {
                    DeactivateCapture();
                    continue;
                }

                if (!EnsureCaptureActive(camera)) continue; // UI 层未就绪：下帧重试
                DoBlitChain();
            }
        }

        private bool EnsureCaptureActive(Camera camera)
        {
            if (!EnsureRenderTextures()) return false;

            if (camera.targetTexture != _cameraRT) camera.targetTexture = _cameraRT;

            if (_filterImage == null && !TryCreateFilterImage()) return false;
            if (!_filterImage.gameObject.activeSelf) _filterImage.gameObject.SetActive(true);
            return true;
        }

        private bool EnsureRenderTextures()
        {
            int w = Screen.width;
            int h = Screen.height;
            if (w <= 0 || h <= 0) return false;

            if (_cameraRT != null && _rtWidth == w && _rtHeight == h) return true;

            ReleaseRTs();
            _cameraRT = new RenderTexture(w, h, 0, RenderTextureFormat.Default);
            _pingA = new RenderTexture(w, h, 0, RenderTextureFormat.Default);
            _pingB = new RenderTexture(w, h, 0, RenderTextureFormat.Default);
            _rtWidth = w;
            _rtHeight = h;
            return true;
        }

        private void ReleaseRTs()
        {
            if (_cameraRT != null) { _cameraRT.Release(); Destroy(_cameraRT); _cameraRT = null; }
            if (_pingA != null) { _pingA.Release(); Destroy(_pingA); _pingA = null; }
            if (_pingB != null) { _pingB.Release(); Destroy(_pingB); _pingB = null; }
        }

        /// <summary>
        /// 在 VN_EffectCanvas 下创建全屏滤镜 RawImage（懒创建，UI 未就绪时下帧重试）。
        /// 作为 canvas 第一个子物体：位于 EffectLayer（UI 粒子等）之下——
        /// 滤镜只滤剧场画面，特效层保持清晰（与"UI 豁免"作用域一致）。
        /// </summary>
        private bool TryCreateFilterImage()
        {
            Transform canvasRoot = null;
            var effectLayer = UIManager.GetInstance().GetEffectLayerRoot();
            if (effectLayer != null) canvasRoot = effectLayer.parent; // VN_EffectCanvas

            if (canvasRoot == null) return false;

            var existing = canvasRoot.Find("VNFilterLayer");
            if (existing != null)
            {
                _filterImage = existing.GetComponent<RawImage>();
                if (_filterImage == null) _filterImage = existing.gameObject.AddComponent<RawImage>();
            }
            else
            {
                var imageGo = new GameObject("VNFilterLayer", typeof(RectTransform), typeof(CanvasRenderer));
                imageGo.transform.SetParent(canvasRoot, false);
                imageGo.transform.SetSiblingIndex(0);

                var rect = imageGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                _filterImage = imageGo.AddComponent<RawImage>();
                _filterImage.raycastTarget = false;
            }
            return _filterImage != null;
        }

        /// <summary>ping-pong Blit 链：相机 RT 依次过每个 weight>0 的滤镜材质，最终 RT 交给 RawImage</summary>
        private void DoBlitChain()
        {
            var src = _cameraRT;
            var dst = _pingA;
            int blitted = 0;
            float aspect = _rtHeight > 0 ? (float)_rtWidth / _rtHeight : 1.777778f;

            for (int i = 0; i < _stack.Count; i++)
            {
                var af = _stack[i];
                if (af.Weight <= 0.0001f) continue; // 刚加入还未推进权重：不烧一次无效果的 Blit
                af.Material.SetFloat("_Weight", af.Weight);
                af.Material.SetFloat("_Aspect", aspect); // 圆形效果（鱼眼/暗角/色差）的宽高比校正
                Graphics.Blit(src, dst, af.Material);
                src = dst;
                dst = ReferenceEquals(dst, _pingA) ? _pingB : _pingA;
                blitted++;
            }

            if (_filterImage == null) return;
            if (blitted == 0)
            {
                // 全部条目权重为 0（淡入起始帧）：直显相机 RT
                if (_filterImage.texture != _cameraRT) _filterImage.texture = _cameraRT;
                return;
            }
            if (_filterImage.texture != src) _filterImage.texture = src;
        }

        private void DeactivateCapture()
        {
            var camera = SceneCameraManager.GetInstance().Camera;
            if (camera != null && camera.targetTexture == _cameraRT)
                camera.targetTexture = null;
            if (_filterImage != null && _filterImage.gameObject.activeSelf)
                _filterImage.gameObject.SetActive(false);
        }
    }
}
