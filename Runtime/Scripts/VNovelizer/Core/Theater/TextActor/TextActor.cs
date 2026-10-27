using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Diagnostics;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 文字演员（UGUI 面板）：<see cref="IActor"/> 的实现，承载累积正文段落。
    ///
    /// 坐标契约：TextActor 不遵循剧本像素语义——面板位置/尺寸/锚点完全由面板 Prefab 决定
    /// （PosID 仅作寻址标识），因此 SetPosition/SetFlip/SetRotation/SetDepth 均为空操作；
    /// SetScale 应用到面板 localScale（profile.scale），SetAlpha 经 CanvasGroup。
    ///
    /// 打字状态与 Dialogue 共用同一套信号：逐字播放中触发
    /// <see cref="VNGameEvents.DisplayAllText"/>（点击补全/跳过/命令中断）时瞬间全显；
    /// 播放完成广播 <see cref="VNGameEvents.TypingFinished"/> 释放 VNManager.isTextDisplaying。
    /// 段落只以数据记录（<see cref="TextActorParagraph"/>）存在，渲染实例可随时重建。
    /// </summary>
    public class TextActor : IActor
    {
        public string ActorId { get; }
        public ActorKind Kind { get; }
        public bool IsValid => _root != null && !_disposed;

        private GameObject _root;
        private CanvasGroup _canvasGroup;
        private TextActorView _view;
        private readonly TextActorProfile _profile;

        private float _alpha = 1f;
        private bool _visible = true;
        private bool _disposed;

        // 段落数据（存档快照源；渲染实例由 _view 管理）
        private readonly List<TextActorParagraph> _paragraphs = new List<TextActorParagraph>();
        private TextActorParagraph _lastParagraph;
        private bool _skipRestoredLastParagraph;

        // 打字状态（与 Dialogue 共用 isTextDisplaying / TypingFinished / DisplayAllText 语义）
        private Coroutine _typingCoroutine;
        private TMP_Text _typingText;
        private bool _typingActive;

        private readonly UnityAction _onDisplayAllText;

        public TextActor(string actorId, ActorKind kind, ActorAppearance appearance, Transform parent)
        {
            ActorId = actorId;
            Kind = kind;
            _profile = appearance?.profile as TextActorProfile;

            BuildPanel();

            _onDisplayAllText = OnDisplayAllText;
            EventCenter.GetInstance().AddEventListener(VNGameEvents.DisplayAllText, _onDisplayAllText);
        }

        private void BuildPanel()
        {
            GameObject prefab = _profile != null ? _profile.PanelPrefab : null;
            RectTransform parentRoot = TheaterManager.GetInstance().TextActorRoot;

            if (prefab != null)
                _root = UnityEngine.Object.Instantiate(prefab, parentRoot);
            else
                _root = TextActorDefaultPanelBuilder.Build(_profile);
            _root.transform.SetParent(parentRoot, false);
            _root.name = $"TextActor_{ActorId}";

            _canvasGroup = _root.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = _root.AddComponent<CanvasGroup>();

            _view = _root.GetComponent<TextActorView>();
            if (_view == null) _view = _root.GetComponentInChildren<TextActorView>(true);
            if (_view == null)
            {
                Debug.LogWarning($"[TextActor] {ActorId} 的面板 Prefab 缺少 TextActorView 组件，改用内置默认面板（原 Prefab 已销毁）");
                UnityEngine.Object.Destroy(_root);
                _root = TextActorDefaultPanelBuilder.Build(_profile);
                _root.transform.SetParent(parentRoot, false);
                _root.name = $"TextActor_{ActorId}";
                _canvasGroup = _root.GetComponent<CanvasGroup>();
                if (_canvasGroup == null) _canvasGroup = _root.AddComponent<CanvasGroup>();
                _view = _root.GetComponent<TextActorView>();
            }

            _view.EnsureStructure();
            _root.SetActive(_visible);
            _canvasGroup.alpha = _alpha;
        }

        // ======================= IActor：外观 =======================

        public bool Accepts(ActorAppearance appearance)
        {
            return appearance != null && appearance.profile == _profile;
        }

        public void SetAppearance(ActorAppearance appearance)
        {
            // 同类型外观（TextActorProfile）已由 Accepts 保证；换 Prefab 属编辑器配置变更，
            // 运行时保持当前面板不动（段落保留），不重建。
        }

        // ======================= IActor：变换 =======================

        public void SetPosition(Vector2 posPx)
        {
            // 面板锚点/位置由 Prefab 决定，PosID 仅作寻址标识
        }

        public void SetScale(float scale)
        {
            if (_root != null)
                _root.transform.localScale = Vector3.one * Mathf.Max(scale, 0.0001f);
        }

        public void SetFlip(bool flipped)
        {
            // 文字面板不做镜像（会倒转文字）
        }

        public void SetRotation(float degreesZ)
        {
            // 文字面板不旋转
        }

        public void SetDepth(int zOrder)
        {
            // 排序由 UGUI Canvas 统一管理
        }

        // ======================= IActor：可见性 =======================

        public void SetAlpha(float alpha)
        {
            _alpha = Mathf.Clamp01(alpha);
            if (_canvasGroup != null) _canvasGroup.alpha = _alpha;
        }

        public void SetVisible(bool visible)
        {
            if (!visible) CompleteTyping();
            _visible = visible;
            if (_root != null) _root.SetActive(_visible);
        }

        /// <summary>面板当前是否显示（正文路由候选判定）</summary>
        public bool IsVisible() => _visible && IsValid;

        // ======================= IActor：转场与动画 =======================

        public void Transition(ActorAppearance next, string transitionName,
                               float duration, float[] parameters)
        {
            SetAppearance(next);
        }

        public IEnumerator FadeAsync(float targetAlpha, float duration, Ease ease = Ease.Linear)
        {
            targetAlpha = Mathf.Clamp01(targetAlpha);
            if (duration <= 0f || !IsValid)
            {
                SetAlpha(targetAlpha);
                yield break;
            }

            float start = _alpha;
            float elapsed = 0f;
            while (elapsed < duration && IsValid)
            {
                elapsed += Time.deltaTime;
                float t = EaseEvaluator.Evaluate(ease, Mathf.Clamp01(elapsed / duration));
                SetAlpha(Mathf.Lerp(start, targetAlpha, t));
                yield return null;
            }
            SetAlpha(targetAlpha);
        }

        public IEnumerator MoveAsync(Vector2 targetPx, float duration, Ease ease = Ease.Linear)
        {
            // 变换命令暂不支持文字面板（面板位置由 Prefab 决定），立即完成
            yield break;
        }

        public void Interrupt()
        {
            CompleteTyping(); // 中断/隐藏：补全当前打字并释放 isTextDisplaying
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interrupt();
            EventCenter.GetInstance().RemoveEventListener(VNGameEvents.DisplayAllText, _onDisplayAllText);
            _paragraphs.Clear();
            _lastParagraph = null;
            if (_root != null)
            {
                _root.SetActive(false);
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;
            _canvasGroup = null;
            _view = null;
        }

        // ======================= 正文段落 =======================

        /// <summary>已累积的段落数据（只读；存档快照源）</summary>
        public IReadOnlyList<TextActorParagraph> Paragraphs => _paragraphs;

        /// <summary>
        /// 追加一段正文并开始播放。
        /// </summary>
        /// <param name="record">段落数据（含来源剧本/行，供存档与去重）</param>
        /// <param name="direct">true = 瞬间全显；false = 逐字打字（速度跟随全局/角色覆盖）</param>
        /// <returns>
        /// true = 正在逐字打字（调用方需保持 isTextDisplaying，完成时由 TypingFinished 释放）；
        /// false = 未产生打字（空文本 / 已在面板上完整显示 / 瞬间完成）。
        /// </returns>
        public bool AppendParagraph(TextActorParagraph record, bool direct)
        {
            if (_disposed || _view == null || record == null || string.IsNullOrEmpty(record.Text))
                return false;

            // 【幂等守卫】同一行（剧本 + 行号 + 文本）紧接重复追加视为重复显示，直接跳过。
            // 覆盖的真实触发路径：
            //   ① R10 重播：「前置 Simulate 建段」+「同步显示」+「裁剪执行 showDialogue」三触发；
            //   ② 场景切换后界面重建对当前行正文的重复广播（RestoreGameplayUIAfterSceneLoad）；
            //   ③ 定制行链尾正文兜底与链内 showDialogue 的并发双触发。
            // 回跳/换剧本经 FastForwardToLine 重建文字角色（_lastParagraph 复位），不受影响；
            // 读档恢复路径的 _skipRestoredLastParagraph 语义保持独立不变。
            // 返回值沿用当前打字状态：守卫命中时若首段仍在逐字播放，调用方应继续
            // 保持 isTextDisplaying（否则提前置 false 会放行自动播放/点击推进）。
            if (_lastParagraph != null &&
                string.Equals(_lastParagraph.ScriptName, record.ScriptName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_lastParagraph.LineId, record.LineId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_lastParagraph.Text, record.Text, StringComparison.Ordinal))
                return _typingActive;

            bool skipRestored = _skipRestoredLastParagraph;
            _skipRestoredLastParagraph = false;
            if (skipRestored && _lastParagraph != null &&
                string.Equals(_lastParagraph.ScriptName, record.ScriptName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_lastParagraph.LineId, record.LineId, StringComparison.OrdinalIgnoreCase))
                return false;

            CompleteTyping(); // 防御：上一段仍在打字时先补全

            string speaker = _profile != null && _profile.ShowSpeaker ? record.Speaker : null;
            TMP_Text tmp = _view.CreateParagraph(record.Text, speaker);
            _paragraphs.Add(record);
            _lastParagraph = record;

            EnforceMaxParagraphs();

            bool typewriter = !direct && !(_profile != null && _profile.DirectDisplay);
            float speed = ResolveSpeed();
            if (!typewriter || speed <= 0f || tmp == null)
            {
                if (tmp != null) tmp.maxVisibleCharacters = 99999;
                return false; // 瞬间完成，无打字状态
            }

            int characterCount = tmp.textInfo.characterCount;
            if (characterCount == 0) return false;
            StartTyping(tmp, characterCount, speed);
            return true;
        }

        private float ResolveSpeed()
        {
            if (_profile != null && _profile.TextSpeedOverride >= 0f)
                return _profile.TextSpeedOverride;
            var gd = GlobalDataManager.GetInstance();
            return gd != null && gd.GetGlobalData() != null ? gd.GetGlobalData().TextSpeed : 0.05f;
        }

        private void StartTyping(TMP_Text tmp, int totalLen, float speedPerChar)
        {
            _typingText = tmp;
            _typingActive = true;
            tmp.maxVisibleCharacters = 0;
            _typingCoroutine = MonoManager.GetInstance().StartCoroutine(TypeRoutine(tmp, totalLen, speedPerChar));
        }

        private IEnumerator TypeRoutine(TMP_Text tmp, int totalLen, float speedPerChar)
        {
            for (int i = 0; i <= totalLen; i++)
            {
                if (tmp == null || _disposed)
                {
                    CompleteTyping();
                    yield break;
                }
                tmp.maxVisibleCharacters = i;
                if (i < totalLen) yield return new WaitForSeconds(speedPerChar);
            }
            _typingCoroutine = null;
            CompleteTyping();
        }

        /// <summary>补全当前打字并广播 TypingFinished（释放 isTextDisplaying）；无打字时无操作。</summary>
        private void CompleteTyping()
        {
            if (!_typingActive) return;
            _typingActive = false;
            if (_typingCoroutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_typingCoroutine);
                _typingCoroutine = null;
            }
            if (_typingText != null)
            {
                _typingText.maxVisibleCharacters = 99999;
                _typingText = null;
            }
            EventCenter.GetInstance().EventTrigger(VNGameEvents.TypingFinished);
        }

        private void OnDisplayAllText()
        {
            CompleteTyping(); // 点击补全 / 跳过 / 命令中断：瞬间全显
        }

        private void EnforceMaxParagraphs()
        {
            if (_profile == null || _profile.MaxParagraphs <= 0) return;
            int excess = _paragraphs.Count - _profile.MaxParagraphs;
            if (excess <= 0) return;
            for (int i = 0; i < excess; i++)
            {
                _paragraphs.RemoveAt(0);
                _view.RemoveOldestParagraph();
            }
            _lastParagraph = _paragraphs.Count > 0 ? _paragraphs[_paragraphs.Count - 1] : null;
        }

        /// <summary>清空全部段落（保留面板与显隐状态）。当前打字先补全。</summary>
        public void ClearParagraphs()
        {
            CompleteTyping();
            _paragraphs.Clear();
            _lastParagraph = null;
            _skipRestoredLastParagraph = false;
            _view?.ClearParagraphs();
        }

        /// <summary>读档恢复：按数据重建全部段落（完整显示，不逐字播放）。</summary>
        public void RestoreParagraphs(IEnumerable<TextActorParagraph> records)
        {
            if (_disposed || _view == null || records == null) return;
            ClearParagraphs();
            foreach (var r in records)
            {
                if (r == null || string.IsNullOrEmpty(r.Text)) continue;
                string speaker = _profile != null && _profile.ShowSpeaker ? r.Speaker : null;
                var tmp = _view.CreateParagraph(r.Text, speaker);
                if (tmp != null) tmp.maxVisibleCharacters = 99999;
                _paragraphs.Add(r);
                _lastParagraph = r;
            }
            EnforceMaxParagraphs();
            _skipRestoredLastParagraph = _paragraphs.Count > 0;
        }
    }
}
