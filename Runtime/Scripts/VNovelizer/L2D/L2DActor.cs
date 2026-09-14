using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.Motion;
using Live2D.Cubism.Framework.MotionFade;
using Live2D.Cubism.Framework.MouthMovement;
using Live2D.Cubism.Rendering;
using UnityEngine;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Diagnostics;
using VNovelizer.Core.Theater;

namespace VNovelizer.L2D
{
    /// <summary>
    /// Live2D 演员：官方 Cubism SDK 5 OW 组件体系的封装实现
    /// （IActor + IL2DControllable + ILipSyncHost）。
    ///
    /// 播放内核 100% 官方组件（见 VNLive2DCharacterDesign.md v3）：
    /// - 动作：CubismMotionController <b>单层</b>，Idle 与一次性动作共享同一层（SDK 的
    ///   CubismFadeController 负责同一层内的多动作 crossfade：旧动作按 FadeOutTime 渐出、
    ///   新动作按 FadeInTime 渐入；一次性动作结束自动重播 Idle 确保待机兜底）；
    ///   多层会让 AnimationLayerMixerPlayable 覆盖遮蔽 Idle（实测踩坑），故改单层。
    /// - 表情：CubismExpressionController.CurrentExpressionIndex（保持型，官方淡入淡出）；
    /// - 透明度：CubismRenderController.Opacity。
    /// - 口型：CubismMouthController + CubismAudioMouthInput（官方组件，AudioInput 绑定
    ///   VoiceManager 的全局语音源，采样 RMS 驱动 MouthOpening）。
    ///
    /// 坐标契约与 MeshActor 一致：1 剧本像素 = 0.01 世界单位；zOrder → 世界 Z = -0.1 * zOrder。
    /// </summary>
    public class L2DActor : IActor, IL2DControllable, ILipSyncHost
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

        // 口型同步（官方组件直封；AudioInput 懒绑定到 VoiceManager 全局语音源）
        private CubismMouthController _mouthController;
        private CubismAudioMouthInput _mouthInput;

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

            // 口型同步组件（官方直封 + 嘴参数自动标签，见 ConfigureLipSync）
            ConfigureLipSync();
        }

        /// <summary>
        /// 口型同步组件装配（Fix-L2D-LipSync，2026-09-14）：
        ///
        /// <list type="number">
        /// <item><b>嘴参数自动标签</b>：SDK 不会自动给参数挂 <c>CubismMouthParameter</c>
        ///      标签（官方需手动），且 Mao 等模型的嘴参数命名非标准（ParamMouthUp/Down/…
        ///      而非 ParamMouthOpenY）——按开合参数白名单在运行时实例上补标签，
        ///      不改动 prefab 资产，用户零操作。白名单**不包含** ParamMouthAngry/
        ///      ParamMouthForm 等表情参数（否则口型会破坏表情嘴型）。</item>
        /// <item><b>CubismMouthController</b>：缺则补。官方默认 MouthOpening=1（张嘴），
        ///      必须立即归零，否则待机时模型嘴部张开。
        ///      【实测踩坑】官方默认 BlendMode=Multiply（参数终值 = 当前值 × MouthOpening）——
        ///      嘴参数默认值是 0（闭嘴原位），0 × 任何口型值 = 0 → 嘴永远紧闭。
        ///      必须显式改为 Override（直接覆写参数值），口型才能驱动开合。</item>
        /// <item><b>CubismAudioMouthInput</b>：缺则补，AudioInput 懒绑定到 VoiceManager
        ///      的全局语音源（激活口型时解析——构造期 VoiceManager 可能尚未初始化）。</item>
        /// </list>
        /// </summary>
        private void ConfigureLipSync()
        {
            if (_modelGo == null) return;

            // 1. 嘴参数自动标签（实例级，不动 prefab 资产）。
            //    白名单 = 嘴部"开合"参数（口型直接驱动）；表情类嘴参数
            //    （ParamMouthAngry / ParamMouthForm / ParamMouthAngryLine 等）绝不打标签。
            var model = _modelGo.FindCubismModel();
            int taggedCount = 0;
            if (model != null && model.Parameters != null)
            {
                foreach (var p in model.Parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    if (!IsMouthOpenParameter(p.Id)) continue;
                    if (p.GetComponent<CubismMouthParameter>() == null)
                        p.gameObject.AddComponent<CubismMouthParameter>();
                    taggedCount++;
                }
            }

            // 2. MouthController（缺则补 + 归零 + Override 模式 + 刷新目标缓存）
            _mouthController = _modelGo.GetComponent<CubismMouthController>();
            if (_mouthController == null)
                _mouthController = _modelGo.AddComponent<CubismMouthController>();
            _mouthController.BlendMode = CubismParameterBlendMode.Override; // Multiply × 默认值 0 = 永远紧闭
            _mouthController.MouthOpening = 0f;
            _mouthController.Refresh();

            // 3. AudioMouthInput（缺则补；初始禁用——由外部口型激活器按说话者开关）
            _mouthInput = _modelGo.GetComponent<CubismAudioMouthInput>();
            if (_mouthInput == null)
                _mouthInput = _modelGo.AddComponent<CubismAudioMouthInput>();
            _mouthInput.enabled = false;

            if (taggedCount == 0)
            {
                Debug.LogWarning($"[L2DActor] {ActorId} 模型 '{_modelGo.name}' 未找到嘴部开合参数" +
                                 "（期望 ParamMouthOpenY / ParamMouthUp / ParamMouthDown 等），口型同步将不生效");
            }
            else
            {
                VNDebug.LogVerbose($"[L2DActor] {ActorId} 口型组件就绪：{taggedCount} 个开合参数已驱动（Override 模式）");
            }
        }

        /// <summary>
        /// 参数 ID 是否为嘴部"开合"参数（口型驱动白名单）。
        /// 标准 Live2D 规范 = ParamMouthOpenY；Mao 等官方模型用 ParamMouthUp / ParamMouthDown
        /// （上/下唇位移模拟张嘴）。表情类嘴参数（Angry/Form/Smile/Line 等）不在白名单。
        /// </summary>
        private static bool IsMouthOpenParameter(string parameterId)
        {
            return string.Equals(parameterId, "ParamMouthOpenY", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(parameterId, "ParamMouthOpen", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(parameterId, "ParamMouthUp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(parameterId, "ParamMouthDown", StringComparison.OrdinalIgnoreCase);
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

                // 【Fix-L2D-2】重建后必须完整恢复原状：
                // ① 重新订阅动作生命周期（播完回 Idle 兜底）；② 重播 Idle 待机；
                // ③ 重新应用变换/深度/透明度/可见性（旧实现重建后新模型默认位姿，
                //    位置缩放翻转 zOrder alpha visible 全部丢失）。
                if (_motionController != null)
                {
                    _motionController.AnimationBeginHandler += OnMotionBegin;
                    _motionController.AnimationEndHandler += OnMotionEnd;
                }
                PlayIdleMotion();

                _currentExpId = null;     // 表情按新模型重设（索引随 ExpressionList 变化）
                _currentMotionId = null;
                _activeNonIdleInstanceId = -1;
                _expectingNonIdleBegin = false;

                ApplyTransform();
                ApplyOpacity();
                if (IsValid) SetDepth(_zOrder);
                if (IsValid) _modelGo.SetActive(_visible);
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

        public bool IsMotionEndless(string motionID)
        {
            var entry = _profile.FindMotion(motionID);
            if (entry == null) return false;
            // Idle 类恒循环（垫底待机）；Motion 类按 Loop 勾选
            return entry.Kind == L2DMotionKind.Idle || entry.Loop;
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
            // 【Fix-L2D-1】单层设计下不能按"层空闲"等待——动作播完的 EndHandler 会
            // 同步重播循环 Idle，CubismMotionLayer._isFinished 立即回到 false，
            // IsPlayingAnimation 恒 true → wait 必然死锁命令链。
            // 改为等待"当前一次性动作实例真正结束"：OnMotionEnd 里匹配实例 ID 后
            // 置 -1（与"播完回 Idle"共用同一信号，无竞态）。
            // 注意：Loop 动作 / Idle 类动作不触发 EndHandler，_activeNonIdleInstanceId
            // 恒 >= 0 → 永远等待；该组合由命令层（L2DMotionCommand）校验拦截。
            while (IsValid && _activeNonIdleInstanceId >= 0)
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

        #region ILipSyncHost（口型同步）

        /// <summary>
        /// 启用 / 停用口型同步（说话者绑定，见 L2DLipSyncDirector）：
        /// active=true 时把官方 CubismAudioMouthInput 绑到 VoiceManager 的全局语音源
        /// （AudioSource.GetOutputData 只能采样"正在播放该源"的音频，故必须绑定真实播放源），
        /// Gain / Smoothing 取角色配置；false 时停用并归零口型，防止嘴部停在张开状态。
        /// </summary>
        public void SetLipSyncActive(bool active)
        {
            if (_mouthController == null || _mouthInput == null) return;

            if (active)
            {
                if (_mouthInput.AudioInput == null)
                    _mouthInput.AudioInput = VoiceManager.GetInstance().GetVoiceAudioSource();
                _mouthInput.Gain = _profile.LipSyncGain > 0f ? _profile.LipSyncGain : 5f;
                _mouthInput.Smoothing = Mathf.Clamp01(_profile.LipSyncSmoothing);
                _mouthInput.enabled = true;
            }
            else
            {
                _mouthInput.enabled = false;
                _mouthController.MouthOpening = 0f;
            }
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
            if (!IsValid) return;

            bool wasActive = _modelGo.activeSelf;
            _modelGo.SetActive(_visible);

            if (_visible)
            {
                if (!wasActive)
                {
                    // 【Fix-L2D-5】真正从"隐藏 → 显示"时，SDK 的 OnDisable/OnEnable
                    // 已销毁并重建 PlayableGraph——隐藏前播放的动作（Idle / 一次性动作）
                    // 全部丢失，_motionPriorities 数组也保留旧值。必须重置动作状态并
                    // 重播 Idle 兜底（PlayIdleMotion 内部先反射清零优先级），否则模型僵住。
                    _currentMotionId = null;
                    _activeNonIdleInstanceId = -1;
                    _expectingNonIdleBegin = false;
                    PlayIdleMotion();
                }
                // wasActive==true：SetActive(true) 是 no-op（不触发 OnEnable、graph 未重建），
                // 保持当前动作——VN 每行重发 ShowCharacter 的幂等去重语义不变。
            }
            else
            {
                // 隐藏期间动作层随 graph 销毁，EndHandler 不会再触发——
                // 立即作废动作状态，防止在途 WaitMotionFinished 死等、以及
                // 重新显示时误判"动作还在播放"（去重逻辑跳过重播）。
                _currentMotionId = null;
                _activeNonIdleInstanceId = -1;
                _expectingNonIdleBegin = false;
            }
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
            _mouthController = null;
            _mouthInput = null;
            _currentMotionId = null;
            _currentExpId = null;
            // 【Fix-L2D-1】实例信号复位——销毁后任何在途 WaitMotionFinished 立即退出
            _activeNonIdleInstanceId = -1;
            _expectingNonIdleBegin = false;
        }

        #endregion
    }
}
