using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Diagnostics;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 剧场管理器：剧场层的唯一事实源。
    ///
    /// 职责：
    /// - 持有全部演员状态（Dictionary&lt;id, ActorState&gt;）与相机状态（CameraState）；
    /// - 负责演员渲染对象（IActor）的生命周期（创建/应用状态/销毁）；
    /// - 消费剧本层事件（ShowCharacter/HideCharacter/ChangeBackground/HideBackground），
    ///   将舞台渲染职责从 VNGameplayPanel 接管到剧场层；
    /// - Simulate（纯数据）与 Execute（经 IActor 表达）在此汇合。
    ///
    /// 层级结构（DontDestroyOnLoad，跨场景存活）：
    /// VN_Theater
    /// ├─ VN_TheaterCamera   （SceneCameraManager 管理）
    /// └─ Actors             （全部演员节点）
    ///
    /// 槽位契约：剧本事件使用全名（Left/MidLeft/Mid/MidRight/Right），
    /// 剧场内部与命令层统一使用 posCode（L/ML/M/MR/R）。
    /// </summary>
    public class TheaterManager : BaseManager<TheaterManager>
    {
        /// <summary>剧场根节点名称</summary>
        public const string RootName = "VN_Theater";

        /// <summary>主背景演员 ID</summary>
        public const string MainBackgroundId = "MainBackground";

        /// <summary>背景交叉淡化的临时演员 ID（不进入状态字典）</summary>
        private const string BgFadeTempId = "BgFadeTemp";

        /// <summary>参考分辨率宽（与 CanvasScaler 基准一致）</summary>
        public const float ReferenceWidth = 1920f;
        /// <summary>参考分辨率高</summary>
        public const float ReferenceHeight = 1080f;

        /// <summary>自由角色（addChar 注册）默认深度：叠在全部标准槽位（1-5）之上</summary>
        public const int FreeCharZOrder = 6;

        /// <summary>五槽位默认基准位置（剧本像素语义，原点=画面中心）</summary>
        private static readonly Dictionary<string, Vector2> SlotBasePositions = new Dictionary<string, Vector2>
        {
            { "L",  new Vector2(-640f, 0f) },
            { "ML", new Vector2(-320f, 0f) },
            { "M",  new Vector2(0f,   0f) },
            { "MR", new Vector2(320f,  0f) },
            { "R",  new Vector2(640f,  0f) },
        };

        /// <summary>五槽位默认深度（越大越靠近相机；背景固定 0）</summary>
        private static readonly Dictionary<string, int> SlotZOrders = new Dictionary<string, int>
        {
            { "L", 1 }, { "ML", 2 }, { "M", 3 }, { "MR", 4 }, { "R", 5 },
        };

        private Transform _root;
        private Transform _actorsRoot;

        private readonly Dictionary<string, ActorState> _states = new Dictionary<string, ActorState>();
        private readonly Dictionary<string, IActor> _actors = new Dictionary<string, IActor>();

        // --- 自由角色注册表（addChar 命令）---
        // key = posID 小写（查找大小写不敏感），value = 原始 posID（演员 actorId）。
        // 自由角色直接以 posID 作为 actorId 进入 _states/_actors——状态、动画、
        // 存档导出（ExportStates）与标准槽位完全同构。
        private readonly Dictionary<string, string> _freeChars = new Dictionary<string, string>();

        /// <summary>相机状态（剧场唯一事实源的一部分，随存档持久化）</summary>
        public readonly CameraState Camera = new CameraState();

        // --- 背景异步加载与交叉淡化状态 ---
        // _bgRequestToken 由"瞬时切换"与"交叉淡化"两条路径共享：任一路径开始时自增，
        // 使上一条路径已在飞行中的异步加载结果作废。
        // 必须共享——否则同一行内 "背景列继承触发 ChangeBackground(旧图)" 与
        // "bgfade(新图)" 会互相覆盖，出现"画面是新图、状态是旧图"的存档错位。
        private Coroutine _bgLoadRoutine;
        private int _bgRequestToken;
        private Coroutine _bgFadeRoutine;
        private int _bgFadeToken;
        private MeshActor _bgFadeTemp;
        private Sprite _bgFadeTargetSprite;

        // --- 演员震动状态 ---
        private readonly Dictionary<string, Coroutine> _activeShakes = new Dictionary<string, Coroutine>();

        /// <summary>初始化剧场（幂等）。在 VNManager.InitializeManager 中调用。</summary>
        public void Init()
        {
            if (_root != null) return;

            var rootGo = new GameObject(RootName);
            Object.DontDestroyOnLoad(rootGo);
            _root = rootGo.transform;

            var actorsGo = new GameObject("Actors");
            actorsGo.transform.SetParent(_root, false);
            _actorsRoot = actorsGo.transform;

            // 自定义场景相机预制体（可预挂后处理组件），留空使用默认相机
            GameObject customCam = null;
            if (VNProjectConfig.Instance != null)
                customCam = VNProjectConfig.Instance.CustomSceneCameraPrefab;

            SceneCameraManager.GetInstance().Init(_root, customCam);
            ApplyCamera();

            SubscribeEvents();

            Debug.Log("[TheaterManager] 剧场初始化完成（事件已接管：ShowCharacter/HideCharacter/ChangeBackground/HideBackground）");
        }

        #region 事件消费（接管 VNGameplayPanel 的舞台渲染职责）

        private void SubscribeEvents()
        {
            var ec = EventCenter.GetInstance();
            ec.AddEventListener<Dictionary<string, string>>(VNGameEvents.ShowCharacter, OnShowCharacter);
            ec.AddEventListener<string>(VNGameEvents.HideCharacter, OnHideCharacter);
            ec.AddEventListener<string>(VNGameEvents.ChangeBackground, OnChangeBackground);
            ec.AddEventListener(VNGameEvents.HideBackground, OnHideBackground);
        }

        /// <summary>
        /// 显示/更新槽位角色。
        /// 语义与旧 VNGameplayPanel.OnShowCharacter 一致：
        /// 每次显示都从槽位基准位置 + profile.offset 重新布局（演出命令的位移是行内瞬态）。
        /// </summary>
        private void OnShowCharacter(Dictionary<string, string> characterInfo)
        {
            if (characterInfo == null) return;

            string position = characterInfo.ContainsKey("position") ? characterInfo["position"] : null;
            string characterID = characterInfo.ContainsKey("characterID") ? characterInfo["characterID"] : null;
            string group = characterInfo.ContainsKey("group") && !string.IsNullOrEmpty(characterInfo["group"])
                ? characterInfo["group"] : CharacterProfile.DefaultGroupName;
            string emotion = characterInfo.ContainsKey("emotion") ? characterInfo["emotion"] : null;

            if (string.IsNullOrEmpty(position) || string.IsNullOrEmpty(characterID)) return;

            string posCode = NormalizePosCode(position);
            if (posCode == null)
            {
                Debug.LogWarning($"[TheaterManager] 未知槽位: {position}");
                return;
            }

            CharacterProfile profile = CharacterResManager.GetInstance().GetCharacterProfile(characterID);
            if (profile == null) return; // GetCharacterProfile 已打印详细错误

            Sprite sprite = profile.GetEmotionSprite(group, emotion);
            if (sprite == null)
            {
                Debug.LogWarning($"[TheaterManager] 角色 {characterID}#{group}#{emotion} 缺少立绘 Sprite，跳过显示");
                return;
            }

            string appearanceId = $"{characterID}#{group}#{emotion}";

            // 状态 + 渲染对象
            EnsureActor(posCode, ActorKind.Character);
            var state = GetState(posCode);
            state.appearance = appearanceId;

            // 槽位基准 + 角色偏移（与旧 basePosition + profile.offset 语义一致）
            Vector2 basePos = SlotBasePositions[posCode];
            SetPosition(posCode, basePos + profile.offset);

            // 缩放（profile.scale）与翻转（沿用 VNManager 的翻转状态源）
            float profileScale = profile.scale > 0 ? profile.scale : 1f;
            SetScale(posCode, profileScale);
            bool flipped = VNManager.GetInstance().GetCharacterScaleX(posCode) < 0f;
            SetFlip(posCode, flipped);

            SetDepth(posCode, SlotZOrders[posCode]);
            SetAlpha(posCode, 1f);
            SetVisible(posCode, true);

            // 最后应用外观（网格按 Sprite 尺寸重建）
            GetActor(posCode)?.SetAppearance(new ActorAppearance(appearanceId, sprite));

            VNDebug.LogVerbose($"[TheaterManager] 登台: {appearanceId} @ {posCode} (scale={profileScale}, flip={flipped}, pos={basePos + profile.offset})");
        }

        private void OnHideCharacter(string position)
        {
            string posCode = NormalizePosCode(position);
            if (posCode == null) return;
            RemoveActor(posCode);
        }

        private void OnChangeBackground(string backgroundPath)
        {
            if (string.IsNullOrEmpty(backgroundPath)) return;

            if (backgroundPath == "black" || backgroundPath == "hide")
            {
                // 黑幕/隐藏：移除背景演员，露出相机 Clear Color（黑）
                RemoveActor(MainBackgroundId);
                return;
            }

            // 交叉淡化正在进行且目标就是本图：淡化协程自己会写入终态，此处不得插手
            // （否则瞬时应用会在淡化中途把主演员换成同一张图，破坏渐变观感）
            if (_bgFadeRoutine != null && GetState(MainBackgroundId)?.appearance == backgroundPath)
                return;

            // 异步加载后即时应用（与旧 OnChangeBackground 行为一致）
            if (_bgLoadRoutine != null) MonoManager.GetInstance().StopCoroutine(_bgLoadRoutine);
            int token = ++_bgRequestToken;
            _bgLoadRoutine = MonoManager.GetInstance().StartCoroutine(LoadAndSetBackground(backgroundPath, token));
        }

        private void OnHideBackground()
        {
            // 作废在飞行中的背景请求，避免隐藏后异步结果又把背景装回来
            _bgRequestToken++;
            if (_bgLoadRoutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_bgLoadRoutine);
                _bgLoadRoutine = null;
            }
            CancelBackgroundFade();
            RemoveActor(MainBackgroundId);
        }

        private IEnumerator LoadAndSetBackground(string bgName, int token)
        {
            var holder = new SpriteHolder();
            yield return LoadBackgroundSprite(bgName, holder);
            _bgLoadRoutine = null;
            if (token != _bgRequestToken) yield break; // 已有更新的背景请求（含 bgfade），丢弃本次结果
            if (holder.value == null) yield break;  // 加载失败已打印日志

            ApplyBackground(holder.value, bgName);
        }

        #endregion

        #region 背景

        /// <summary>协程取值容器（C# 迭代器无返回值，用持有者传递结果）</summary>
        private class SpriteHolder
        {
            public Sprite value;
        }

        /// <summary>背景资源类别前缀（配置缺失时退化为无前缀，避免空引用）</summary>
        private static string BackgroundRoot
        {
            get
            {
                var cfg = VNProjectConfig.Instance;
                return cfg != null ? cfg.BackgroundResPath : null;
            }
        }

        /// <summary>背景资源键：类别前缀 + 资源名（前缀为空时退化为裸名）</summary>
        private static string BuildBackgroundKey(string bgName)
        {
            string root = BackgroundRoot;
            return string.IsNullOrEmpty(root) ? bgName : $"{root}/{bgName}";
        }

        /// <summary>解析背景 Sprite：主路径 + 兜底路径（与旧 OnChangeBackground 一致）。
        /// 经 VNResourceService 提供者链加载（Addressables → Resources）。
        /// 含纹理形态兜底：资产已注册但按 Sprite 类型未解析时（Addressables 在 Fast 模式
        /// 按 Texture2D 登记类型），按 Texture2D 加载并就地构造 Sprite。</summary>
        private IEnumerator LoadBackgroundSprite(string bgName, SpriteHolder holder)
        {
            string primary = BuildBackgroundKey(bgName);
            var opPrimary = VNResourceService.LoadAsync<Sprite>(primary);
            while (!opPrimary.IsDone) yield return null;
            if (opPrimary.Asset != null) { holder.value = opPrimary.Asset; yield break; }

            // 纹理形态兜底（同步，通常已在提供者缓存中）
            Sprite texSprite = LoadTextureAsSprite(primary);
            if (texSprite != null) { holder.value = texSprite; yield break; }

            // 兜底：Backgrounds/ 子目录
            string fallback = "Backgrounds/" + bgName;
            var opFallback = VNResourceService.LoadAsync<Sprite>(fallback);
            while (!opFallback.IsDone) yield return null;
            if (opFallback.Asset != null) { holder.value = opFallback.Asset; yield break; }

            Sprite texSprite2 = LoadTextureAsSprite(fallback);
            if (texSprite2 != null) { holder.value = texSprite2; yield break; }

            Debug.LogError($"[TheaterManager] 背景加载失败: {bgName}（尝试路径: {primary} / {fallback}）");
        }

        /// <summary>
        /// 纹理形态兜底：按 Texture2D 加载并构造 Sprite（pixelsPerUnit=100 与 TextureImporter
        /// 默认值一致）。用于"资产存在且已注册、但按 Sprite 类型未解析"的 Addressables 类型差异。
        /// </summary>
        private static Sprite LoadTextureAsSprite(string key)
        {
            var texture = VNResourceService.Load<Texture2D>(key);
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            return sprite;
        }

        /// <summary>即时应用背景（cover-fit 铺满参考分辨率，保持纵横比）</summary>
        private void ApplyBackground(Sprite sprite, string bgName)
        {
            EnsureActor(MainBackgroundId, ActorKind.Background);
            var state = GetState(MainBackgroundId);
            state.appearance = bgName;

            GetActor(MainBackgroundId)?.SetAppearance(new ActorAppearance(bgName, sprite));
            SetPosition(MainBackgroundId, Vector2.zero);
            SetDepth(MainBackgroundId, 0);

            // cover-fit：按比例放大到完全覆盖 1920x1080（优于旧 Image 拉伸：不变形）
            float coverScale = Mathf.Max(ReferenceWidth / sprite.rect.width, ReferenceHeight / sprite.rect.height);
            SetScale(MainBackgroundId, coverScale);

            SetAlpha(MainBackgroundId, 1f);
            SetVisible(MainBackgroundId, true);
        }

        /// <summary>
        /// 背景交叉淡化（bgfade 命令实现）。
        /// 结构与旧 BgFadeCommand 的 Front/Back 双图一致：
        /// 当前背景演员保持旧图；临时演员（不进状态字典）承载新图淡入；
        /// 完成后旧演员瞬间换新图、临时演员销毁。状态始终反映终态。
        /// 重入保护：新的 bgfade 会先强制完成上一次。
        ///
        /// <para>
        /// <b>R13 ease 参数</b>：<paramref name="inEase"/> 控制新背景淡入曲线
        /// （缺省 Linear，与旧实现一致）；<paramref name="outEase"/> 为 null 时
        /// 旧背景保持不动（旧实现行为），指定时旧背景按该曲线同步淡出——
        /// 不透明背景下两者视觉等价，透明背景可做真正的交叉淡化。
        /// </para>
        /// </summary>
        public IEnumerator FadeBackgroundCoroutine(string bgName, float duration,
            Ease inEase = Ease.Linear, Ease? outEase = null)
        {
            if (string.IsNullOrEmpty(bgName)) yield break;

            // 令牌：防止被取消/取代的旧协程在清理时误伤新协程的字段
            int token = ++_bgFadeToken;

            // 同时作废"瞬时切换"路径在飞行中的加载结果（共享请求令牌）：
            // 否则同一行内的 ChangeBackground(旧图) 会在淡化开始后落地，把状态改回旧图
            _bgRequestToken++;
            if (_bgLoadRoutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_bgLoadRoutine);
                _bgLoadRoutine = null;
            }

            // 重入保护（与旧 BgFadeCommand 语义一致）
            if (_bgFadeRoutine != null)
            {
                Debug.LogWarning("[TheaterManager] 上一次背景切换尚未完成，已强制瞬间完成");
                CancelBackgroundFade();
            }

            // 异步加载新图
            var holder = new SpriteHolder();
            yield return LoadBackgroundSprite(bgName, holder);
            Sprite newSprite = holder.value;
            if (newSprite == null) yield break;

            var mainActor = GetActor(MainBackgroundId);
            bool hadBackground = mainActor != null && mainActor.IsValid;

            // 无旧背景：直接应用
            if (!hadBackground)
            {
                ApplyBackground(newSprite, bgName);
                yield break;
            }

            // 状态立即写入终态（演出是表达，状态是事实）
            GetState(MainBackgroundId).appearance = bgName;
            _bgFadeTargetSprite = newSprite;

            // 临时演员承载新图，置于旧背景之前
            _bgFadeTemp = new MeshActor(BgFadeTempId, ActorKind.Background, _actorsRoot);
            _bgFadeTemp.SetAppearance(new ActorAppearance(bgName, newSprite));
            _bgFadeTemp.SetPosition(Vector2.zero);
            _bgFadeTemp.SetDepth(1);
            float coverScale = Mathf.Max(ReferenceWidth / newSprite.rect.width, ReferenceHeight / newSprite.rect.height);
            _bgFadeTemp.SetScale(coverScale);
            _bgFadeTemp.SetAlpha(0f);
            _bgFadeTemp.SetVisible(true);

            bool finished = false;
            _bgFadeRoutine = MonoManager.GetInstance().StartCoroutine(
                RunBgFade(duration, inEase, outEase, mainActor, () => finished = true));
            while (!finished && _bgFadeTemp != null) yield return null;

            // 已被更新的切换或强制取消接管：本协程不再触碰任何共享字段
            if (token != _bgFadeToken) yield break;

            // 自然完成：主演员换新图（若未被强制取消）
            if (_bgFadeTemp != null)
            {
                mainActor = GetActor(MainBackgroundId);
                if (mainActor != null)
                {
                    mainActor.SetAppearance(new ActorAppearance(bgName, newSprite));
                    SetScale(MainBackgroundId, coverScale);
                    SetAlpha(MainBackgroundId, 1f);
                    SetVisible(MainBackgroundId, true);
                }
                _bgFadeTemp.Dispose();
                _bgFadeTemp = null;
            }
            _bgFadeTargetSprite = null;
            _bgFadeRoutine = null;
        }

        /// <summary>
        /// 背景交叉淡化驱动：
        /// - 新图（临时演员）按 <paramref name="inEase"/> 淡入；
        /// - <paramref name="outEase"/> 非 null 时旧图（主演员）按该曲线同步淡出，
        ///   为 null 时旧图保持不动（与旧实现完全一致）。
        /// 旧图 alpha 归位由完成/取消两条路径统一负责（SetAlpha 1f）。
        /// </summary>
        private IEnumerator RunBgFade(float duration, Ease inEase, Ease? outEase,
            IActor oldActor, System.Action onDone)
        {
            if (duration <= 0f || _bgFadeTemp == null)
            {
                onDone?.Invoke();
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration && _bgFadeTemp != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _bgFadeTemp.SetAlpha(EaseEvaluator.Evaluate(inEase, t));
                if (outEase.HasValue && oldActor != null)
                    oldActor.SetAlpha(1f - EaseEvaluator.Evaluate(outEase.Value, t));
                yield return null;
            }
            onDone?.Invoke();
        }

        /// <summary>强制完成背景切换（bgfade 被中断/重入时调用）：瞬间呈现终态</summary>
        public void CancelBackgroundFade()
        {
            if (_bgFadeRoutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_bgFadeRoutine);
                _bgFadeRoutine = null;
            }

            if (_bgFadeTemp != null)
            {
                if (_bgFadeTargetSprite != null)
                {
                    var mainActor = GetActor(MainBackgroundId);
                    if (mainActor != null && mainActor.IsValid)
                    {
                        var state = GetState(MainBackgroundId);
                        mainActor.SetAppearance(new ActorAppearance(state.appearance, _bgFadeTargetSprite));
                        float coverScale = Mathf.Max(ReferenceWidth / _bgFadeTargetSprite.rect.width,
                                                      ReferenceHeight / _bgFadeTargetSprite.rect.height);
                        SetScale(MainBackgroundId, coverScale);
                        SetAlpha(MainBackgroundId, 1f);
                        SetVisible(MainBackgroundId, true);
                    }
                }
                _bgFadeTemp.Dispose();
                _bgFadeTemp = null;
            }
            _bgFadeTargetSprite = null;
        }

        #endregion

        #region 演员震动（shake 命令：角色分支）

        /// <summary>
        /// 震动指定演员（剧本像素强度）。协程由命令层经 MonoManager 启动，
        /// 归位逻辑由 CancelActorShake 保证。
        /// </summary>
        public IEnumerator ShakeActorCoroutine(string posCode, float duration, float intensityPx)
        {
            var actor = GetActor(posCode);
            var state = GetState(posCode);
            if (actor == null || state == null) yield break;

            Vector2 basePos = state.position;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (GetActor(posCode) == null) yield break; // 演员被移除（换行/隐藏）
                elapsed += Time.deltaTime;
                Vector2 jitter = new Vector2(Random.Range(-intensityPx, intensityPx),
                                             Random.Range(-intensityPx, intensityPx));
                GetActor(posCode)?.SetPosition(basePos + jitter);
                yield return null;
            }
            GetActor(posCode)?.SetPosition(basePos);
            _activeShakes.Remove(posCode);
        }

        /// <summary>开始/替换某演员的震动协程</summary>
        public Coroutine BeginActorShake(string posCode, float duration, float intensityPx)
        {
            CancelActorShake(posCode);
            var co = MonoManager.GetInstance().StartCoroutine(ShakeActorCoroutine(posCode, duration, intensityPx));
            _activeShakes[posCode] = co;
            return co;
        }

        /// <summary>停止演员震动并归位到状态位置</summary>
        public void CancelActorShake(string posCode)
        {
            if (_activeShakes.TryGetValue(posCode, out var co))
            {
                MonoManager.GetInstance().StopCoroutine(co);
                _activeShakes.Remove(posCode);
            }
            var state = GetState(posCode);
            if (state != null) GetActor(posCode)?.SetPosition(state.position);
        }

        #endregion

        #region 演员生命周期

        /// <summary>确保演员存在（状态 + 渲染对象），已存在时直接返回</summary>
        public IActor EnsureActor(string actorId, ActorKind kind)
        {
            if (!_states.TryGetValue(actorId, out var state))
            {
                state = new ActorState(actorId, kind);
                _states[actorId] = state;
            }

            if (!_actors.TryGetValue(actorId, out var actor) || actor == null || !actor.IsValid)
            {
                if (actor != null) _actors.Remove(actorId);
                actor = new MeshActor(actorId, kind, _actorsRoot);
                _actors[actorId] = actor;
                ApplyState(actorId); // 新建渲染对象时全量同步一次状态
            }
            return actor;
        }

        /// <summary>获取演员渲染对象（不存在返回 null）</summary>
        public IActor GetActor(string actorId)
        {
            return _actors.TryGetValue(actorId, out var actor) && actor != null && actor.IsValid
                ? actor : null;
        }

        /// <summary>获取演员状态（不存在返回 null）</summary>
        public ActorState GetState(string actorId)
        {
            return _states.TryGetValue(actorId, out var state) ? state : null;
        }

        public bool Contains(string actorId) => _states.ContainsKey(actorId);

        /// <summary>移除演员（状态与渲染对象一并清除）</summary>
        public void RemoveActor(string actorId)
        {
            CancelActorShake(actorId);
            if (_actors.TryGetValue(actorId, out var actor))
            {
                actor?.Interrupt();
                (actor as MeshActor)?.Dispose();
                _actors.Remove(actorId);
            }
            _states.Remove(actorId);
        }

        /// <summary>清空剧场：全部演员退场、背景切换强制完成、相机归位</summary>
        public void ClearTheater()
        {
            // 作废并停止在飞行中的背景加载（否则清场后异步结果会把背景装回来）
            _bgRequestToken++;
            if (_bgLoadRoutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_bgLoadRoutine);
                _bgLoadRoutine = null;
            }
            CancelBackgroundFade();

            foreach (var posCode in new List<string>(_activeShakes.Keys))
                CancelActorShake(posCode);

            foreach (var actor in _actors.Values)
            {
                actor?.Interrupt();
                (actor as MeshActor)?.Dispose();
            }
            _actors.Clear();
            _states.Clear();
            _freeChars.Clear(); // 自由角色注册表随清场一并作废

            Camera.Reset();
            SceneCameraManager.GetInstance().ResetCamera();
        }

        #endregion

        #region 自由角色（addChar / removeChar 命令）

        /// <summary>
        /// 注册自由角色（addChar）：仅登记（visible=false，不登台），
        /// 由 showchar(posID) 展示。同名 posID（大小写不敏感）覆盖更新。
        /// posID 直接作为演员 actorId 进入状态字典——动画、存档与标准槽位同构。
        /// </summary>
        public bool RegisterFreeChar(string posID, string charRef, Vector2 posPx, float scale)
        {
            if (string.IsNullOrWhiteSpace(posID))
            {
                Debug.LogError("[TheaterManager] addChar 的 posID 不能为空");
                return false;
            }

            string key = posID.Trim();
            string lower = key.ToLower();

            // 保留名冲突：标准五槽位 / 背景演员 ID
            if (NormalizeStandardPosCode(key) != null)
            {
                Debug.LogError($"[TheaterManager] addChar 的 posID \"{key}\" 与标准槽位冲突（L/ML/M/MR/R 为保留名）");
                return false;
            }
            if (lower == MainBackgroundId.ToLower() || lower == BgFadeTempId.ToLower())
            {
                Debug.LogError($"[TheaterManager] addChar 的 posID \"{key}\" 是引擎保留名");
                return false;
            }
            // 标识符安全：不含命令解析保留字符（逗号/括号/引号/管道/与号/空格）
            if (key.IndexOfAny(new[] { ',', '(', ')', '"', '\'', '|', '&', ' ' }) >= 0)
            {
                Debug.LogError($"[TheaterManager] addChar 的 posID \"{key}\" 含非法字符（不允许 , ( ) 引号 | & 空格）");
                return false;
            }

            if (_freeChars.TryGetValue(lower, out string existing) && existing != key)
            {
                Debug.LogWarning($"[TheaterManager] 自由角色 \"{existing}\" 被大小写变体 \"{key}\" 覆盖");
                RemoveActor(existing);
            }
            _freeChars[lower] = key;

            // 状态 + 渲染对象（隐藏登记）
            EnsureActor(key, ActorKind.Character);
            var state = GetState(key);
            state.appearance = charRef ?? "";
            state.position = posPx;
            state.scale = Mathf.Max(scale, 0.0001f);
            state.scaleX = 1f;
            state.zOrder = FreeCharZOrder;
            state.alpha = 1f;
            state.visible = false;
            ApplyState(key);

            Debug.Log($"[TheaterManager] 注册自由角色 {key}: {charRef} @ ({posPx.x}, {posPx.y}) scale={state.scale}（隐藏，showchar({key}) 展示）");
            return true;
        }

        /// <summary>注销并移除自由角色（removeChar）。未注册返回 false（打警告）。</summary>
        public bool UnregisterFreeChar(string posID)
        {
            string key = posID?.Trim();
            if (string.IsNullOrEmpty(key) || !_freeChars.Remove(key.ToLower()))
            {
                Debug.LogWarning($"[TheaterManager] removeChar: 未注册的自由角色 \"{posID}\"");
                return false;
            }
            RemoveActor(key);
            Debug.Log($"[TheaterManager] 移除自由角色 {key}");
            return true;
        }

        /// <summary>
        /// 清空全部自由角色（快进重放 / 换剧本 / 清场调用）。
        /// 快进语义：FastForwardToLine 从头 Simulate，addChar 行会重新注册——
        /// 此处先清是为了防止"跳行目标之前没有 addChar"的残留角色污染状态。
        /// </summary>
        public void ClearFreeChars()
        {
            if (_freeChars.Count == 0) return;
            foreach (var key in new List<string>(_freeChars.Values))
                RemoveActor(key);
            _freeChars.Clear();
        }

        /// <summary>是否为已注册的自由角色（大小写不敏感）</summary>
        public bool IsFreeChar(string posIdOrActorId)
        {
            if (string.IsNullOrWhiteSpace(posIdOrActorId)) return false;
            return _freeChars.ContainsKey(posIdOrActorId.Trim().ToLower());
        }

        /// <summary>
        /// 展示自由角色（showchar 的自由角色路径）：
        /// <paramref name="charRef"/> 非空时先更新立绘引用（换表情/换装），
        /// 为空沿用注册立绘；随后解析 Sprite 并登台（visible=true）。
        /// </summary>
        public bool ShowFreeChar(string posID, string charRef)
        {
            var state = GetState(posID);
            if (state == null)
            {
                Debug.LogError($"[TheaterManager] showchar: 未注册的自由角色 \"{posID}\"（请先 addChar）");
                return false;
            }

            if (!string.IsNullOrEmpty(charRef)) state.appearance = charRef;

            var resolved = ResolveAppearance(state);
            if (resolved == null)
            {
                Debug.LogError($"[TheaterManager] showchar: 自由角色 {posID} 立绘解析失败: {state.appearance}");
                return false;
            }

            SetAppearance(posID, resolved);
            SetVisible(posID, true);
            Debug.Log($"[TheaterManager] 展示自由角色 {posID}: {state.appearance}");
            return true;
        }

        #endregion

        #region 状态 → 渲染 同步

        /// <summary>
        /// 将某演员的状态全量应用到渲染对象（预演/读档后重建画面用）。
        /// 渲染对象不存在时按需创建。
        /// 注意：外观资源解析依赖 CharacterResManager / Resources，
        /// 若解析失败则保留渲染对象当前外观（状态字段仍被更新）。
        /// </summary>
        public void ApplyState(string actorId)
        {
            if (!_states.TryGetValue(actorId, out var state)) return;

            var actor = EnsureActor(actorId, state.kind);
            if (!actor.IsValid) return;

            if (!string.IsNullOrEmpty(state.appearance))
            {
                var resolved = ResolveAppearance(state);
                if (resolved != null) actor.SetAppearance(resolved);
            }

            actor.SetPosition(state.position);
            actor.SetScale(state.scale);
            actor.SetFlip(state.scaleX < 0f);
            actor.SetDepth(state.zOrder);
            actor.SetAlpha(state.alpha);
            actor.SetVisible(state.visible);
        }

        /// <summary>全部演员状态应用（读档/预演完成后一次性重建画面）</summary>
        public void ApplyAllStates()
        {
            foreach (var id in new List<string>(_states.Keys))
                ApplyState(id);
            ApplyCamera();
        }

        /// <summary>
        /// 按状态语义解析外观资源：
        /// - 角色（L/ML/M/MR/R）："CharacterID#分组#表情" → CharacterResManager
        /// - 背景：资源名 → BackgroundResPath（含兜底）
        /// 解析不到返回 null。
        /// </summary>
        private ActorAppearance ResolveAppearance(ActorState state)
        {
            if (state.kind == ActorKind.Character)
            {
                string[] parts = state.appearance.Split('#');
                if (parts.Length == 3)
                {
                    var profile = CharacterResManager.GetInstance().TryGetCharacterProfile(parts[0]);
                    Sprite sprite = profile?.GetEmotionSprite(parts[1], parts[2]);
                    if (sprite != null) return new ActorAppearance(state.appearance, sprite);
                }
                return null;
            }

            if (state.kind == ActorKind.Background)
            {
                // 同步加载（ApplyState 为重建路径，通常资源已加载过），经资源服务链
                string primary = BuildBackgroundKey(state.appearance);
                Sprite sprite = VNResourceService.Load<Sprite>(primary);
                if (sprite == null) sprite = LoadTextureAsSprite(primary); // 纹理形态兜底
                if (sprite == null)
                {
                    string fallback = "Backgrounds/" + state.appearance;
                    sprite = VNResourceService.Load<Sprite>(fallback);
                    if (sprite == null) sprite = LoadTextureAsSprite(fallback);
                }
                if (sprite != null) return new ActorAppearance(state.appearance, sprite);
            }
            return null;
        }

        #endregion

        #region 便捷变更入口（状态 + 立即应用）

        public void SetAppearance(string actorId, ActorAppearance appearance)
        {
            var state = GetState(actorId);
            if (state == null)
            {
                Debug.LogWarning($"[TheaterManager] 演员 {actorId} 不存在，先 EnsureActor 再设置外观");
                return;
            }
            state.appearance = appearance?.id ?? string.Empty;
            GetActor(actorId)?.SetAppearance(appearance);
        }

        public void SetPosition(string actorId, Vector2 posPx)
        {
            var state = GetState(actorId);
            if (state == null) return;
            state.position = posPx;
            GetActor(actorId)?.SetPosition(posPx);
        }

        public void SetScale(string actorId, float scale)
        {
            var state = GetState(actorId);
            if (state == null) return;
            state.scale = Mathf.Max(scale, 0.0001f);
            GetActor(actorId)?.SetScale(state.scale);
        }

        public void SetFlip(string actorId, bool flipped)
        {
            var state = GetState(actorId);
            if (state == null) return;
            state.scaleX = flipped ? -1f : 1f;
            GetActor(actorId)?.SetFlip(flipped);
        }

        public void SetDepth(string actorId, int zOrder)
        {
            var state = GetState(actorId);
            if (state == null) return;
            state.zOrder = zOrder;
            GetActor(actorId)?.SetDepth(zOrder);
        }

        public void SetAlpha(string actorId, float alpha)
        {
            var state = GetState(actorId);
            if (state == null) return;
            state.alpha = Mathf.Clamp01(alpha);
            GetActor(actorId)?.SetAlpha(state.alpha);
        }

        public void SetVisible(string actorId, bool visible)
        {
            var state = GetState(actorId);
            if (state == null) return;
            state.visible = visible;
            GetActor(actorId)?.SetVisible(visible);
        }

        #endregion

        #region 相机

        /// <summary>应用相机状态到场景相机（预演/读档/命令修改后调用）</summary>
        public void ApplyCamera()
        {
            SceneCameraManager.GetInstance().ApplyState(Camera);
        }

        /// <summary>相机归位</summary>
        public void ResetCamera()
        {
            Camera.Reset();
            SceneCameraManager.GetInstance().ResetCamera();
        }

        #endregion

        #region 存档支持（阶段 5 完善字段持久化，此处提供状态快照入口）

        /// <summary>导出全部演员状态快照（供 SaveData 序列化）</summary>
        public List<ActorState> ExportStates()
        {
            return new List<ActorState>(_states.Values);
        }

        /// <summary>导入演员状态快照（读档；随后调用 ApplyAllStates 重建画面）</summary>
        public void ImportStates(IEnumerable<ActorState> states)
        {
            _states.Clear();
            if (states == null) return;
            foreach (var s in states)
            {
                if (s == null || string.IsNullOrEmpty(s.actorId)) continue;
                _states[s.actorId] = s;
            }
        }

        #endregion

        #region 工具

        /// <summary>标准五槽位全名/别名 → posCode（L/ML/M/MR/R），非标准槽位返回 null</summary>
        public static string NormalizeStandardPosCode(string posCode)
        {
            if (string.IsNullOrEmpty(posCode)) return null;
            switch (posCode.Trim().ToLower())
            {
                case "l":
                case "left": return "L";
                case "ml":
                case "midleft":
                case "mid_left":
                case "charmid_left":
                case "charmidleft": return "ML";
                case "m":
                case "mid":
                case "middle": return "M";
                case "mr":
                case "midright":
                case "mid_right":
                case "charmid_right":
                case "charmidright": return "MR";
                case "r":
                case "right": return "R";
                default: return null;
            }
        }

        /// <summary>
        /// 槽位解析（扩展版）：标准五槽位优先，其次查自由角色注册表（addChar 的 posID）。
        /// 自由角色返回其原始 posID（即演员 actorId）；均不匹配返回 null。
        /// 大小写不敏感。
        /// </summary>
        public static string NormalizePosCode(string posCode)
        {
            string standard = NormalizeStandardPosCode(posCode);
            if (standard != null) return standard;

            string raw = posCode?.Trim();
            if (string.IsNullOrEmpty(raw)) return null;

            var freeChars = GetInstance()._freeChars;
            if (freeChars != null && freeChars.TryGetValue(raw.ToLower(), out string original))
                return original;
            return null;
        }

        #endregion
    }
}
