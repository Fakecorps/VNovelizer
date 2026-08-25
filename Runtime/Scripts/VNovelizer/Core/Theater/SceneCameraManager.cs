using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 场景相机管理：负责剧场专用相机的创建/装配与相机状态应用。
    ///
    /// 单相机契约（UI 全 Overlay 后）：
    /// - 剧场相机（本类管理）：正交 Size = 5.4（19.2x10.8 世界单位 = 1920x1080 剧本像素），
    ///   位于 (0,0,-10) 朝 +Z，只渲染 Default 层（UI 层由 Overlay Canvas 负责）；
    /// - 无 UI 相机：所有面板 Canvas 均为 ScreenSpaceOverlay，天然压在剧场画面之上。
    ///
    /// 相机名固定为 <see cref="CameraName"/>（诊断与去重用）。
    /// </summary>
    public class SceneCameraManager : BaseManager<SceneCameraManager>
    {
        /// <summary>剧场相机固定名称</summary>
        public const string CameraName = "VN_TheaterCamera";

        /// <summary>默认正交尺寸：1080 / 2 / 100 = 5.4（画面高度恰好铺满）</summary>
        public const float BaseOrthoSize = 5.4f;

        /// <summary>相机默认距离（负 Z，朝 +Z 观看舞台）</summary>
        public const float BaseDistance = -10f;

        /// <summary>剧场相机（可能为 null：尚未 Init 或自定义预制体装配失败）</summary>
        public Camera Camera { get; private set; }

        /// <summary>初始化/装配场景相机（幂等）</summary>
        /// <param name="parent">剧场根节点</param>
        /// <param name="customPrefab">自定义相机预制体（可空；可预挂后处理组件）</param>
        public void Init(Transform parent, GameObject customPrefab = null)
        {
            if (Camera != null) return; // 幂等

            if (customPrefab != null)
            {
                var go = Object.Instantiate(customPrefab, parent, false);
                go.name = CameraName;
                Camera = go.GetComponent<Camera>();
                if (Camera == null)
                {
                    Debug.LogError($"[SceneCameraManager] 自定义相机预制体 {customPrefab.name} 缺少 Camera 组件，回退默认相机");
                    Object.Destroy(go);
                }
                else
                {
                    ConfigureCamera(Camera);
                    return;
                }
            }

            var camGo = new GameObject(CameraName);
            camGo.transform.SetParent(parent, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, BaseDistance);
            camGo.transform.localRotation = Quaternion.identity;
            Camera = camGo.AddComponent<Camera>();
            ConfigureCamera(Camera);
        }

        private static void ConfigureCamera(Camera cam)
        {
            cam.orthographic = true;
            cam.orthographicSize = BaseOrthoSize;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            // depth=10：UI 已全部 Overlay（无 UI 相机），剧场相机是唯一需要"最后画"的相机。
            // 若用户场景遗留旧 Main Camera（渲染 Default 层），必须让剧场相机后渲染覆盖之，
            // 否则遗留相机的静止画面会盖住剧场相机的演出（相机震动等不可见）。
            cam.depth = 10f;
            // 只渲染 Default 层：UI 层（5）由 Overlay Canvas 负责，避免 UI 被画两遍
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.tag = "Untagged"; // 不占用 MainCamera 标签，避免干扰 Camera.main 语义

            // 保证场景恰有一个"耳朵"：BGM/SFX/Voice 发声的前提（见方法注释）
            EnsureSceneHasAudioListener(cam);

            WarnAboutLegacyCameras(cam);
        }

        /// <summary>
        /// 保证场景中恰有一个启用的 AudioListener（BGM/SFX/Voice 能被听到的前提）。
        /// - 场景其他位置已有启用的 listener（如 Main Camera）→ 保持现状；
        ///   剧场相机上如有（自定义预制体携带）则移除，避免双 listener 警告；
        /// - 全场景没有任何启用的 listener（如用户按建议删除了遗留 Main Camera、
        ///   或场景副本中相机被禁用）→ 剧场相机自动补挂兜底，
        ///   消除 "There are no audio listeners in the scene" 与"BGM 无声"问题。
        /// </summary>
        private static void EnsureSceneHasAudioListener(Camera cam)
        {
            var own = cam.GetComponent<AudioListener>();
            bool othersEnabled = false;
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            foreach (var l in listeners)
            {
                if (l == null || l == own) continue;
                if (l.isActiveAndEnabled) { othersEnabled = true; break; }
            }

            if (othersEnabled)
            {
                // 场景已有"耳朵"：剧场相机不得再带（双 listener 会被 Unity 警告）
                if (own != null) Object.Destroy(own);
                return;
            }

            // 全场景无"耳朵"：剧场相机兜底（自定义预制体带来的 listener 保留复用）
            if (own == null)
            {
                cam.gameObject.AddComponent<AudioListener>();
                Debug.Log("[SceneCameraManager] 场景中没有启用的 AudioListener，已在剧场相机上自动补齐" +
                          "（BGM/SFX/Voice 依赖它发声；此前若被删除的 Main Camera 带 listener，现在无需手动恢复）");
            }
        }

        /// <summary>
        /// 处理与剧场相机冲突的遗留相机（渲染 Default 层的其他相机）。
        /// 这些相机通常源于旧 ScreenSpaceCamera Canvas 时代——UI 已全部 Overlay，
        /// 它们除了白白渲染一遍剧场画面（然后被 depth=10 的剧场相机覆盖）外毫无作用。
        /// 处理：自动将其 cullingMask 中的 Default 层剔除（渲染空集 = 零渲染成本），
        /// 保留相机对象与其余层/组件（AudioListener 等不受影响）；若确认无用可手动删除。
        /// </summary>
        private static void WarnAboutLegacyCameras(Camera theaterCamera)
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in cameras)
            {
                if (c == null || c == theaterCamera) continue;
                if ((c.cullingMask & 1) == 0) continue; // 不渲染 Default 层（bit 0），无冲突

                c.cullingMask &= ~1; // 运行时剔除 Default 层（不改资产），幂等
                Debug.LogWarning($"[SceneCameraManager] 检测到遗留相机 '{c.name}' 仍在渲染 Default 层，" +
                                 "已自动剔除其 Default 层渲染（画面本就被剧场相机覆盖，纯浪费）。" +
                                 "若确认无用可删除该相机（UI 已全部 Overlay；其 AudioListener 若被删除，剧场相机会自动补齐）。");
            }
        }

        /// <summary>应用相机状态（推拉/平移/旋转/后处理开关）</summary>
        public void ApplyState(CameraState state)
        {
            if (Camera == null || state == null) return;

            var t = Camera.transform;
            t.localPosition = new Vector3(state.offset.x, state.offset.y, BaseDistance + state.offset.z);
            t.localRotation = Quaternion.Euler(state.rotation);

            if (Camera.orthographic != state.orthographic)
                Camera.orthographic = state.orthographic;

            if (state.orthographic)
            {
                // zoom > 1 = 放大 = 正交尺寸缩小
                Camera.orthographicSize = BaseOrthoSize / Mathf.Max(state.zoom, 0.05f);
            }
            else
            {
                // 透视模式：以 FOV 表达 zoom（默认按 60° 基准）
                Camera.fieldOfView = 60f / Mathf.Max(state.zoom, 0.05f);
            }

            ApplyFxComponents(state.activeFxComponents);
        }

        /// <summary>按组件类型名开关相机上的后处理组件，返回当前启用列表</summary>
        public void SetFxEnabled(string componentTypeName, bool enabled, CameraState state)
        {
            if (state == null) return;
            bool changed = false;
            if (enabled && !state.activeFxComponents.Contains(componentTypeName))
            {
                state.activeFxComponents.Add(componentTypeName);
                changed = true;
            }
            else if (!enabled && state.activeFxComponents.Contains(componentTypeName))
            {
                state.activeFxComponents.Remove(componentTypeName);
                changed = true;
            }
            if (changed) ApplyFxComponents(state.activeFxComponents);
        }

        private void ApplyFxComponents(List<string> enabledNames)
        {
            if (Camera == null) return;
            var components = Camera.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (!(components[i] is Behaviour behaviour)) continue;
                if (!IsManagedFxComponent(behaviour)) continue;

                bool on = enabledNames != null && enabledNames.Contains(behaviour.GetType().Name);
                if (behaviour.enabled != on) behaviour.enabled = on;
            }
        }

        /// <summary>
        /// 是否属于"由剧场状态托管开关"的后处理组件。
        ///
        /// 关键排除项（不可省略）：Camera / Transform 是相机本体；
        /// <see cref="AudioListener"/> 与 <see cref="AudioSource"/> 同为 Behaviour，
        /// 一旦被当作后处理组件统一关闭，会直接导致全局静音
        /// （剧场相机是场景 AudioListener 的兜底宿主，见 EnsureSceneHasAudioListener）。
        /// </summary>
        private static bool IsManagedFxComponent(Behaviour behaviour)
        {
            if (behaviour is Camera) return false;
            if (behaviour is AudioListener) return false;
            if (behaviour is AudioSource) return false;
            return true;
        }

        /// <summary>相机与全部后处理组件恢复默认（剧场清空时调用）</summary>
        public void ResetCamera()
        {
            CancelShake();
            if (Camera == null) return;
            var t = Camera.transform;
            t.localPosition = new Vector3(0f, 0f, BaseDistance);
            t.localRotation = Quaternion.identity;
            Camera.orthographic = true;
            Camera.orthographicSize = BaseOrthoSize;

            // 关闭全部托管后处理组件（AudioListener/AudioSource 已被排除，见 IsManagedFxComponent）
            ApplyFxComponents(null);
        }

        #region 相机震动（shake screen 分支）

        private Coroutine _shakeRoutine;
        private Vector3 _shakeBasePos;

        /// <summary>
        /// 开始/替换相机震动（intensityWorld 为世界单位强度）。
        /// 震动结束后相机归位到开始时的位置。
        /// </summary>
        public void BeginShake(float duration, float intensityWorld)
        {
            CancelShake();
            if (Camera == null) return;
            _shakeBasePos = Camera.transform.localPosition;
            _shakeRoutine = MonoManager.GetInstance().StartCoroutine(ShakeCoroutine(duration, intensityWorld));
        }

        private IEnumerator ShakeCoroutine(float duration, float intensityWorld)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (Camera == null) yield break;
                elapsed += Time.deltaTime;
                Vector3 jitter = new Vector3(
                    Random.Range(-intensityWorld, intensityWorld),
                    Random.Range(-intensityWorld, intensityWorld),
                    0f);
                Camera.transform.localPosition = _shakeBasePos + jitter;
                yield return null;
            }
            if (Camera != null) Camera.transform.localPosition = _shakeBasePos;
            _shakeRoutine = null;
        }

        /// <summary>
        /// 停止相机震动并归位（仅在有活动震动时生效——无震动时是 no-op，
        /// 否则首次 BeginShake 会用字段默认值 (0,0,0) 破坏相机基准位置）
        /// </summary>
        public void CancelShake()
        {
            if (_shakeRoutine == null) return;

            MonoManager.GetInstance().StopCoroutine(_shakeRoutine);
            _shakeRoutine = null;
            if (Camera != null)
                Camera.transform.localPosition = _shakeBasePos;
        }

        #endregion
    }
}
