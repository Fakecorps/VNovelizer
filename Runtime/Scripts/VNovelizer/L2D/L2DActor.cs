using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.Motion;
using Live2D.Cubism.Framework.MotionFade;
using Live2D.Cubism.Rendering;
using UnityEngine;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Diagnostics;
using VNovelizer.Core.Theater;

namespace VNovelizer.L2D
{
    /// <summary>
    /// Live2D 演员：官方 Cubism SDK 5 OW 组件体系的封装实现（IActor + IL2DControllable）。
    ///
    /// 播放内核 100% 官方组件（见 VNLive2DCharacterDesign.md v3）：
    /// - 动作：CubismMotionController <b>单层</b>，Idle 与一次性动作共享同一层（SDK 的
    ///   CubismFadeController 负责同一层内的多动作 crossfade：旧动作按 FadeOutTime 渐出、
    ///   新动作按 FadeInTime 渐入；一次性动作结束自动重播 Idle 确保待机兜底）；
    ///   多层会让 AnimationLayerMixerPlayable 覆盖遮蔽 Idle（实测踩坑），故改单层。
    /// - 表情：CubismExpressionController.CurrentExpressionIndex（保持型，官方淡入淡出）；
    /// - 透明度：CubismRenderController.Opacity。
    ///
    /// 坐标契约与 MeshActor 一致：1 剧本像素 = 0.01 世界单位；zOrder → 世界 Z = -0.1 * zOrder。
    /// </summary>
    public class L2DActor : IActor, IL2DControllable
    {
        public const float PixelsToWorld = 0.01f;
        public const float DepthStep = -0.1f;

        /// <summary>单层：Idle 与一次性动作共享同层（SDK 源码 CubismFadeController 自动 crossfade）</summary>
        private const int ActionLayer = 0;
        /// <summary>官方 PlayableGraph 层数（单层）</summary>
        private const int LayerCount = 1;

        public string ActorId { get; }
        public ActorKind Kind { get; }
        public bool IsValid => _modelGo != null;

        private L2DCharacterProfile _profile;

        private GameObject _modelGo;
        private CubismMotionController _motionController;
        private CubismExpressionController _expressionController;
        private CubismRenderController _renderController;

        // 视觉状态缓存（动画与归位使用）
        private float _scale = 1f;
        private bool _flipped;
        private int _zOrder;
        private float _alpha = 1f;
        private bool _visible = true;
        private Vector2 _positionPx;

        // 活动动画句柄与终值（Interrupt 瞬间到终态；初值与视觉初值一致防回跳）
        private Coroutine _fadeRoutine;
        private float _pendingFadeTarget = 1f;
        private Coroutine _moveRoutine;
        private Vector2 _pendingMoveTargetPx = Vector2.zero;

        // 表情告警去重：VN 每行重发 ShowCharacter，同一个查无的表情 ID 只警告一次
        private string _lastWarnedExpId;
        // 入场动作告警去重（同上）
        private string _lastWarnedMotionId;

        // ---- 当前状态去重（VN 每行重发 ShowCharacter，相同内容必须幂等不重播）----
        /// <summary>当前已应用的表情 ID（null = 无表情）；相同 ID 的立绘列情绪不重复设置</summary>
        private string _currentExpId;
        /// <summary>当前活动动作：null=无 / "idle"=待机循环中 / 其他=一次性动作 ID 播放中</summary>
        private string _currentMotionId;

        // 当前"非循环一次性动作"的 instanceId；其结束时自动重播 Idle 兜底。
        // 多个一次性动作连续触发时覆写为最新值，旧动作的 EndHandler 命中时已不等于该值 → 不会误重播
        private int _activeNonIdleInstanceId = -1;
        private bool _expectingNonIdleBegin;   // 标记：下一次 AnimationBegin 应被视为"非 Idle 开始"

        // 【SDK 内部状态机兜底】CubismMotionController 的 _motionPriorities 私有数组
        // 只有 OnAnimationEnd 会重置为 0，StopAnimation 不会——导致同帧内
        // 播完一次再播同优先级动作（最常见：cell 写 idle 想重播 Idle）会被 SDK 检查拒绝。
        // 用反射兜底强制清零，确保 PlayAnimation 永远能接受新动作。FieldInfo 缓存一次。
        private static FieldInfo _prioritiesField;

        // =========================================================
        //                      构造与初始化
        // =========================================================

        public L2DActor(string actorId, ActorKind kind, ActorAppearance appearance, Transform parent)
        {
            ActorId = actorId;
            Kind = kind;

            _profile = appearance?.profile as L2DCharacterProfile;
            if (_profile == null)
            {
                Debug.LogError($"[L2DActor] 外观未携带 L2DCharacterProfile 配置，无法创建 Live2D 演员 {actorId}");
                return;
            }
            if (_profile.ModelPrefab == null)
            {
                Debug.LogError($"[L2DActor] L2D 角色 '{_profile.CharacterID}' 未配置模型 Prefab");
                return;
            }

            _modelGo = UnityEngine.Object.Instantiate(_profile.ModelPrefab, parent);
            _modelGo.name = $"Actor_{kind}_{actorId}";

            ConfigureOfficialComponents();

            // 订阅动作生命周期：一次性动作结束时自动重播 Idle，确保"播完回待机"兜底
            if (_motionController != null)
            {
                _motionController.AnimationBeginHandler += OnMotionBegin;
                _motionController.AnimationEndHandler += OnMotionEnd;
            }

            // 播放 Idle 待机（若配置了 Idle 条目）；PlayableGraph 已在 OnEnable 同步建好，可直接播放
            PlayIdleMotion();
        }

        /// <summary>
        /// 播放 Idle 待机（循环）。构造时、自动重播、以及 StopMotion 后都会调用。
        /// 播放前强制重置层的优先级（绕过 SDK StopAnimation 不重置优先级的延迟问题），
        /// 否则同帧/跨帧连续调用（cell 反复写 idle）会被 SDK 拒绝。
        /// </summary>
        private void PlayIdleMotion()
        {
            var idle = _profile.IdleMotion;
            if (idle == null || idle.Clip == null || _motionController == null) return;
            ResetLayerPriorityImpl(ActionLayer);
            try
            {
                _motionController.PlayAnimation(idle.Clip, ActionLayer,
                    CubismMotionPriority.PriorityIdle, true, 1f);
                _currentMotionId = "idle"; // 记录当前状态为待机（供立绘列去重）
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[L2DActor] Idle 待机播放失败: {e.Message}");
            }
        }

        /// <summary>
        /// 反射重置 CubismMotionController 私有 _motionPriorities[layer] = 0，
        /// 绕过 SDK 内部只有 OnAnimationEnd 才重置的延迟问题（StopAnimation 不重置）。
        /// FieldInfo 缓存一次；SDK 改名/删字段则静默跳过（不崩溃）。
        /// </summary>
        private void ResetLayerPriorityImpl(int layer)
        {
            if (_motionController == null) return;
            if (_prioritiesField == null)
            {
                _prioritiesField = typeof(Live2D.Cubism.Framework.Motion.CubismMotionController)
                    .GetField("_motionPriorities",
                        BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_prioritiesField == null) return;
            var arr = _prioritiesField.GetValue(_motionController) as int[];
            if (arr != null && layer >= 0 && layer < arr.Length)
                arr[layer] = 0;
        }

        private void OnMotionBegin(int instanceId)
        {
            // 仅标记"非 Idle 开始"的那次：begin 触发时若正处于"期待非 Idle 播"的窗口，记录为活跃一次性动作
            if (_expectingNonIdleBegin)
            {
                _activeNonIdleInstanceId = instanceId;
                _expectingNonIdleBegin = false;
            }
        }

        private void OnMotionEnd(int instanceId)
        {
            // 只在"当前活跃的一次性动作"真正结束时才重播 Idle；
            // 旧动作被新动作 Force 替换时它的 EndHandler 也会触发但 instanceId 已不匹配 → 不会误打断新动作
            if (instanceId == _activeNonIdleInstanceId)
            {
                _activeNonIdleInstanceId = -1;
                PlayIdleMotion();
            }
        }

        /// <summary>
        /// 官方组件注入序列（顺序关键）：
        /// 清 Animator controller（官方 OW 要求）→ 缺啥补啥（FadeController）→
        /// <b>MotionController 一律移除后重建</b> → 未激活状态下 AddComponent + 设 LayerCount=2
        /// → 激活（首次 OnEnable 即按正确层数建内部数组）。
        ///
        /// 【SDK 陷阱（重要）】CubismMotionController 在首次 OnEnable 时按 LayerCount 创建内部
        /// _motionLayers 数组并永久缓存，不随 LayerCount 变化重建——若先以 LayerCount=1 激活过，
        /// 再改 2 并重新 enable，OnEnable 与 StopAnimation 会按 2 层访问 size=1 的数组直接
        /// IndexOutOfRange（官方源码未考虑该场景）。因此必须"未激活创建 + 预先设层数"。
        /// </summary>
        private void ConfigureOfficialComponents()
        {
            if (_modelGo == null) return;

            _expressionController = _modelGo.GetComponent<CubismExpressionController>();
            _renderController = _modelGo.GetComponent<CubismRenderController>();
            var fade = _modelGo.GetComponent<CubismFadeController>();
            var mc = _modelGo.GetComponent<CubismMotionController>();

            // 官方 OW 硬约束：Animator 不得挂 controller（否则 CubismMotionController 罢工）
            var animator = _modelGo.GetComponent<Animator>();
            if (animator != null) animator.runtimeAnimatorController = null;

            // 缺啥补啥：FadeController（SDK 5 R4.2 部分示例模型如 Mao 导入时未必齐备）
            if (fade == null) fade = _modelGo.AddComponent<CubismFadeController>();

            // MotionController：无论 prefab 上有没有，一律移除后按目标层数重建。
            // DestroyImmediate 而非 Destroy：本构造器不在任何组件回调栈内，立即移除可避免
            // 帧末延迟销毁与新组件共存（GetComponent 会返回旧实例导致配置错位）。
            if (mc != null)
                UnityEngine.Object.DestroyImmediate(mc);

            bool wasActive = _modelGo.activeSelf;
            _modelGo.SetActive(false);
            mc = _modelGo.AddComponent<CubismMotionController>();

            // OnEnable 会读取 CubismFadeController.CubismFadeMotionList——必须在激活前注入
            if (fade != null) fade.CubismFadeMotionList = _profile.FadeMotionList;
            mc.LayerCount = LayerCount;

            _modelGo.SetActive(wasActive); // 此刻 OnEnable 首次运行：数组按 LayerCount=2 创建
            _motionController = mc;

            // 表情总表注入（配置为单一事实源；官方导入异常为 null 时兜底）
            if (_expressionController != null)
                _expressionController.ExpressionsList = _profile.ExpressionList;

            // fade states 重建（mc 层数组此时已按正确层数建好）
            if (fade != null) fade.Refresh();
        }

        #region 外观

        /// <summary>只承载携带 L2DCharacterProfile 配置的外观（动态立绘路径）</summary>
        public bool Accepts(ActorAppearance appearance)
        {
            return appearance != null && appearance.profile is L2DCharacterProfile;
        }

        public void SetAppearance(ActorAppearance appearance)
        {
            if (!IsValid) return;

            var newProfile = appearance?.profile as L2DCharacterProfile;
            if (newProfile != null && newProfile != _profile)
            {
                // 同槽位换了另一位 L2D 角色：重建模型（罕见路径）
                Debug.Log($"[L2DActor] {ActorId} 配置由 '{_profile?.CharacterID}' 切换为 '{newProfile.CharacterID}'，重建模型");
                var parent = _modelGo.transform.parent;
                DestroyModel();
                _profile = newProfile;
                _modelGo = UnityEngine.Object.Instantiate(_profile.ModelPrefab, parent);
                _modelGo.name = $"Actor_{Kind}_{ActorId}";
                ConfigureOfficialComponents();
            }

            // 情绪串（appearance.id 第 3 段，已归一为 ID#Default#表情）= 表情 ID：应用并保持。
            // 去重：与当前已应用表情相同时跳过（VN 每行重发，避免无谓重设）
            string expID = ExtractEmotion(appearance?.id);
            if (!string.IsNullOrEmpty(expID) &&
                !string.Equals(_currentExpId, expID, System.StringComparison.OrdinalIgnoreCase))
            {
                SetExpressionInternal(expID, null);
            }

            // 现场登台动作（仅 OnShowCharacter 填充 showMotionId；读档/ApplyState 路径为 null，动作不重播）
            if (!string.IsNullOrEmpty(appearance?.showMotionId))
                PlayMotionFromCell(appearance.showMotionId);
        }

        /// <summary>
        /// 立绘列的入场动作（角色ID#动作#表情 的动作段，默认非阻塞）：
        /// 特殊值 idle = 停动作回待机；其余按配置 Kind 播放（播完自动回 Idle）。
        /// <b>去重</b>：与当前活动动作相同时直接跳过——VN 每行重发 ShowCharacter，
        /// 相同内容必须幂等，绝不重播（否则 Idle 会每行从头跳一遍）。
        /// </summary>
        private void PlayMotionFromCell(string motionID)
        {
            // 已是待机 → 无操作
            if (string.Equals(motionID, "idle", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(_currentMotionId, "idle", System.StringComparison.OrdinalIgnoreCase))
                    TryStopMotion(); // 非待机状态（动作播放中/无动作）才收动作回待机
                return;
            }

            // 相同动作已在进行 → 不重播
            if (string.Equals(_currentMotionId, motionID, System.StringComparison.OrdinalIgnoreCase))
                return;

            if (!TryPlayMotion(motionID, out string error))
            {
                if (_lastWarnedMotionId != motionID)
                {
                    _lastWarnedMotionId = motionID;
                    Debug.LogWarning($"[L2DActor] {ActorId} 入场动作失败: {error}");
                }
            }
            else
            {
                _lastWarnedMotionId = null;
            }
        }

        private static string ExtractEmotion(string appearanceId)
        {
            if (string.IsNullOrEmpty(appearanceId)) return null;
            string[] parts = appearanceId.Split('#');
            return parts.Length >= 3 ? parts[2] : null;
        }

        #endregion

        #region IL2DControllable

        /// <summary>
        /// 结构就绪即视为可用：构造器同步完成官方组件注入与 PlayableGraph 建立
        /// （Cubism 表情/动作 API 均可在首帧前直接调用），无需等待帧。
        /// </summary>
        public bool IsReady => IsValid && _motionController != null;

        public bool TryPlayMotion(string motionID, out string error)
        {
            error = null;
            if (!IsValid)
            {
                error = "模型已销毁";
                return false;
            }

            var entry = _profile.FindMotion(motionID);
            if (entry == null)
            {
                error = $"动作 ID '{motionID}' 不存在（可用: {DescribeMotionIds()}）";
                return false;
            }
            if (entry.Clip == null)
            {
                error = $"动作 '{motionID}' 未配置 clip";
                return false;
            }

            int layer = ActionLayer; // 单层：Idle 与动作共享，SDK CubismFadeController 自动 crossfade
            int priority = entry.Kind == L2DMotionKind.Idle
                ? CubismMotionPriority.PriorityIdle
                : CubismMotionPriority.PriorityForce; // 强制替换：后动打断前动（同层）
            bool loop = entry.Kind == L2DMotionKind.Idle || entry.Loop;

            return DoPlayMotion(entry, layer, priority, loop, out error);
        }

        private bool DoPlayMotion(L2DMotionEntry entry, int layer, int priority, bool loop, out string error)
        {
            error = null;
            if (_motionController == null)
            {
                error = "模型无 CubismMotionController";
                return false;
            }
            // TODO(P4): FadeIn/FadeOutOverride 覆写（需克隆 CubismFadeMotionData 避免污染共享资产）
            if (entry.FadeInOverride >= 0f || entry.FadeOutOverride >= 0f)
                VNDebug.LogVerbose($"[L2DActor] 动作 '{entry.ID}' 的 fade 覆写暂未生效（P4 支持）");

            // 标记下一次 begin 为"非 Idle 开始"；OnMotionBegin 会记录 instanceId 用于结束时重播 Idle
            // Idle 类的入场走这条路径时也会标记为"非 Idle"，但 Idle 本身 loop=true EndHandler 不触发 → 无影响
            _expectingNonIdleBegin = true;

            try
            {
                _motionController.PlayAnimation(entry.Clip, layer, priority, loop, 1f);
                _currentMotionId = entry.ID; // 记录当前活动动作（供立绘列去重）
                return true;
            }
            catch (Exception e)
            {
                error = $"播放异常: {e.Message}";
                _expectingNonIdleBegin = false;
                Debug.LogWarning($"[L2DActor] {ActorId} 播放动作 '{entry.ID}' 失败: {e.Message}");
                return false;
            }
        }

        public bool TryStopMotion()
        {
            if (_motionController == null || !IsValid) return false;
            // 单层：停当前层内容（参数化 clip），然后立刻重播 Idle 兜底
            // try/catch 兜底：SDK 内部层数组与 LayerCount 不一致等异常不让 VN 事件链路崩掉
            try
            {
                _motionController.StopAnimation(0, ActionLayer);
                // 清掉"活跃一次性动作"标记，避免其延迟 EndHandler（force-stop 也可能触发）
                // 触发重播后误判为"主动结束"
                _activeNonIdleInstanceId = -1;
                // 与 PlayIdleMotion 一致，强制重置优先级避免 SDK 延迟状态导致 Idle 不接受
                ResetLayerPriorityImpl(ActionLayer);
                PlayIdleMotion();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[L2DActor] {ActorId} 停动作失败: {e.Message}");
                return false;
            }
        }

        public bool TrySetExpression(string expID, float? fadeOverride, out string error)
        {
            error = null;
            if (!IsValid)
            {
                error = "模型已销毁";
                return false;
            }
            SetExpressionInternal(expID, fadeOverride, out error);
            return error == null;
        }

        public bool TryClearExpression()
        {
            if (_expressionController == null || !IsValid) return false;
            _expressionController.CurrentExpressionIndex = -1;
            _currentExpId = null;
            return true;
        }

        private void SetExpressionInternal(string expID, float? fadeOverride)
        {
            SetExpressionInternal(expID, fadeOverride, out _);
        }

        private void SetExpressionInternal(string expID, float? fadeOverride, out string error)
        {
            error = null;
            if (_expressionController == null)
            {
                error = "模型无 CubismExpressionController";
                Debug.LogWarning($"[L2DActor] {ActorId} {error}，无法设置表情 '{expID}'");
                return;
            }

            var entry = _profile.FindExpression(expID);
            if (entry == null)
            {
                error = $"表情 ID '{expID}' 不存在（可用: {DescribeExpressionIds()}）";
                if (_lastWarnedExpId != expID)
                {
                    _lastWarnedExpId = expID;
                    Debug.LogWarning($"[L2DActor] {ActorId} {error}");
                }
                return;
            }
            _lastWarnedExpId = null;
            if (entry.Data == null)
            {
                error = $"表情 '{expID}' 未配置资产";
                Debug.LogWarning($"[L2DActor] {ActorId} {error}");
                return;
            }

            int index = _profile.GetExpressionIndex(entry.Data);
            if (index < 0)
            {
                error = $"表情 '{expID}' 的资产不在 ExpressionList 总表内";
                Debug.LogWarning($"[L2DActor] {ActorId} {error}");
                return;
            }

            // TODO(P4): FadeOverride 覆写（需克隆 CubismExpressionData 避免污染共享资产）
            if (fadeOverride.HasValue || entry.FadeOverride >= 0f)
                VNDebug.LogVerbose($"[L2DActor] 表情 '{expID}' 的 fade 覆写暂未生效（P4 支持）");

            _expressionController.CurrentExpressionIndex = index;
            _currentExpId = entry.ID; // 记录已应用表情（供立绘列去重）
        }

        public IEnumerator WaitMotionFinished()
        {
            // 循环动作永不结束——由命令层校验避免误用
            while (IsValid && _motionController != null &&
                   _motionController.IsPlayingAnimation(ActionLayer))
            {
                yield return null;
            }
        }

        private string DescribeMotionIds()
        {
            var ids = new List<string>();
            if (_profile.Motions != null)
                foreach (var m in _profile.Motions)
                    if (m != null && !string.IsNullOrEmpty(m.ID)) ids.Add(m.ID);
            return ids.Count > 0 ? string.Join(", ", ids) : "（空）";
        }

        private string DescribeExpressionIds()
        {
            var ids = new List<string>();
            if (_profile.Expressions != null)
                foreach (var e in _profile.Expressions)
                    if (e != null && !string.IsNullOrEmpty(e.ID)) ids.Add(e.ID);
            return ids.Count > 0 ? string.Join(", ", ids) : "（空）";
        }

        #endregion

        #region 变换 / 可见性（IActor）

        public void SetPosition(Vector2 posPx)
        {
            _positionPx = posPx;
            _pendingMoveTargetPx = posPx; // 同步终值，避免 Interrupt 回跳
            ApplyTransform();
        }

        public void SetScale(float scale)
        {
            _scale = Mathf.Max(scale, 0.0001f);
            ApplyTransform();
        }

        public void SetFlip(bool flipped)
        {
            _flipped = flipped;
            ApplyTransform();
        }

        public void SetDepth(int zOrder)
        {
            _zOrder = zOrder;
            if (!IsValid) return;
            var pos = _modelGo.transform.localPosition;
            pos.z = zOrder * DepthStep;
            _modelGo.transform.localPosition = pos;
        }

        public void SetAlpha(float alpha)
        {
            _alpha = Mathf.Clamp01(alpha);
            _pendingFadeTarget = _alpha;
            ApplyOpacity();
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (IsValid) _modelGo.SetActive(_visible);
        }

        private void ApplyTransform()
        {
            if (!IsValid) return;
            _modelGo.transform.localPosition = new Vector3(
                _positionPx.x * PixelsToWorld,
                _positionPx.y * PixelsToWorld,
                _zOrder * DepthStep);
            _modelGo.transform.localScale = new Vector3(
                _scale * (_flipped ? -1f : 1f), _scale, 1f);
        }

        private void ApplyOpacity()
        {
            if (_renderController != null) _renderController.Opacity = _alpha;
        }

        #endregion

        #region 转场与异步动画（IActor）

        public void Transition(ActorAppearance next, string transitionName, float duration, float[] parameters)
        {
            // 与 MeshActor 同阶段语义：立即切换（转场系统后续阶段接入）
            SetAppearance(next);
        }

        public IEnumerator FadeAsync(float targetAlpha, float duration, Ease ease = Ease.Linear)
        {
            if (!IsValid) yield break;
            InterruptFade();

            targetAlpha = Mathf.Clamp01(targetAlpha);
            _pendingFadeTarget = targetAlpha;

            if (duration <= 0f)
            {
                SetAlpha(targetAlpha);
                yield break;
            }

            float start = _alpha;
            _fadeRoutine = MonoManager.GetInstance().StartCoroutine(RunFade(start, targetAlpha, duration, ease));
            yield return new WaitUntil(() => _fadeRoutine == null);
        }

        private IEnumerator RunFade(float start, float target, float duration, Ease ease)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (!IsValid) yield break;
                elapsed += Time.deltaTime;
                float t = EaseEvaluator.Evaluate(ease, Mathf.Clamp01(elapsed / duration));
                _alpha = Mathf.Lerp(start, target, t);
                ApplyOpacity();
                yield return null;
            }
            _alpha = target;
            ApplyOpacity();
            _fadeRoutine = null;
        }

        public IEnumerator MoveAsync(Vector2 targetPx, float duration, Ease ease = Ease.Linear)
        {
            if (!IsValid) yield break;
            InterruptMove();

            _pendingMoveTargetPx = targetPx;

            if (duration <= 0f)
            {
                SetPosition(targetPx);
                yield break;
            }

            Vector2 start = _positionPx;
            _moveRoutine = MonoManager.GetInstance().StartCoroutine(RunMove(start, targetPx, duration, ease));
            yield return new WaitUntil(() => _moveRoutine == null);
        }

        private IEnumerator RunMove(Vector2 startPx, Vector2 targetPx, float duration, Ease ease)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (!IsValid) yield break;
                elapsed += Time.deltaTime;
                float t = EaseEvaluator.Evaluate(ease, Mathf.Clamp01(elapsed / duration));
                SetPosition(Vector2.Lerp(startPx, targetPx, t));
                yield return null;
            }
            SetPosition(targetPx);
            _moveRoutine = null;
        }

        private void InterruptFade()
        {
            if (_fadeRoutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }
        }

        private void InterruptMove()
        {
            if (_moveRoutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_moveRoutine);
                _moveRoutine = null;
            }
        }

        /// <summary>
        /// 中断（行跳过 / 换演员前）：动画瞬到终态 + 停动作层回 Idle。
        /// 表情保持——由下一行立绘列决定去留（设计 §6.4）。
        /// </summary>
        public void Interrupt()
        {
            InterruptFade();
            InterruptMove();

            SetAlpha(_pendingFadeTarget);
            SetPosition(_pendingMoveTargetPx);

            // 走带防护的停动作路径（SDK 层数组异常不会让事件链路崩掉）
            TryStopMotion();
        }

        #endregion

        #region 销毁

        /// <summary>销毁模型实例（状态由 TheaterManager 持有，可重建）</summary>
        public void Dispose()
        {
            Interrupt();
            DestroyModel();
        }

        private void DestroyModel()
        {
            if (_modelGo != null)
            {
                UnityEngine.Object.Destroy(_modelGo);
                _modelGo = null;
            }
            _motionController = null;
            _expressionController = null;
            _renderController = null;
            _currentMotionId = null;
            _currentExpId = null;
        }

        #endregion
    }
}
