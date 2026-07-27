using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using VNovelizer.Core.Compat;

public class DarkFadeTransitionEffect : TransitionEffectBase
{
    public const string EffectKeyConst = "DarkFade";

    public override string EffectKey => EffectKeyConst;
    public override bool IsPlaying => isPlaying;

    [Header("默认时长")]
    [SerializeField] private float defaultFadeOutDuration = 0.35f;
    [SerializeField] private float defaultFadeInDuration = 0.35f;

    [Header("渐变曲线")]
    [SerializeField] private Ease fadeOutEase = Ease.Linear;
    [SerializeField] private Ease fadeInEase = Ease.Linear;

    [Header("自动创建的遮罩层设置")]
    [SerializeField] private int sortingOrder = 10000;
    [SerializeField] private string runtimeCanvasName = "__DarkFadeCanvas";
    [SerializeField] private string runtimeImageName = "__DarkFadeImage";

    private Canvas fadeCanvas;
    private Image fadeImage;
    private CompatTween fadeTween;
    private Coroutine transitionCoroutine;
    private bool isPlaying;

    private void Awake()
    {
        EnsureRuntimeFadeUI();

        if (fadeImage == null)
        {
            Debug.LogError("[DarkFadeTransitionEffect] 运行时黑幕 UI 创建失败。");
            enabled = false;
            return;
        }

        SetAlpha(0f);
        SetBlockRaycast(false);
        BringToFront();
    }

    public override void PlayTransitionAsync(
        Action<Action> middleActionAsync,
        Action onComplete = null,
        float enterDuration = -1f,
        float exitDuration = -1f)
    {
        if (!enabled || fadeImage == null)
        {
            Debug.LogError("[DarkFadeTransitionEffect] fadeImage 无效，无法播放转场。");
            onComplete?.Invoke();
            return;
        }

        StopRunningTransition();

        float fadeOutDuration = enterDuration > 0f ? enterDuration : defaultFadeOutDuration;
        float fadeInDuration = exitDuration > 0f ? exitDuration : defaultFadeInDuration;

        transitionCoroutine = StartCoroutine(
            CoPlayTransitionAsync(
                middleActionAsync,
                onComplete,
                fadeOutDuration,
                fadeInDuration
            )
        );
    }

    public override void PlayEnterOnlyAsync(Action onComplete = null, float duration = -1f)
    {
        if (!enabled || fadeImage == null)
        {
            Debug.LogError("[DarkFadeTransitionEffect] fadeImage 无效，无法播放前半段转场。");
            onComplete?.Invoke();
            return;
        }

        StopRunningTransition();

        float fadeOutDuration = duration > 0f ? duration : defaultFadeOutDuration;
        transitionCoroutine = StartCoroutine(CoPlayEnterOnly(onComplete, fadeOutDuration));
    }

    public override void PlayExitOnlyAsync(Action onComplete = null, float duration = -1f)
    {
        if (!enabled || fadeImage == null)
        {
            Debug.LogError("[DarkFadeTransitionEffect] fadeImage 无效，无法播放后半段转场。");
            onComplete?.Invoke();
            return;
        }

        StopRunningTransition();

        float fadeInDuration = duration > 0f ? duration : defaultFadeInDuration;
        transitionCoroutine = StartCoroutine(CoPlayExitOnly(onComplete, fadeInDuration));
    }

    private IEnumerator CoPlayTransitionAsync(
        Action<Action> middleActionAsync,
        Action onComplete,
        float fadeOutDuration,
        float fadeInDuration)
    {
        isPlaying = true;

        EnsureRuntimeFadeUI();
        BringToFront();
        SetBlockRaycast(true);

        yield return FadeToAlpha(1f, fadeOutDuration, fadeOutEase);

        if (middleActionAsync != null)
        {
            bool middleDone = false;
            middleActionAsync(() => middleDone = true);
            yield return new WaitUntil(() => middleDone);
        }

        yield return FadeToAlpha(0f, fadeInDuration, fadeInEase);

        isPlaying = false;
        SetBlockRaycast(false);
        transitionCoroutine = null;

        onComplete?.Invoke();
    }

    private IEnumerator CoPlayEnterOnly(Action onComplete, float fadeOutDuration)
    {
        isPlaying = true;

        EnsureRuntimeFadeUI();
        BringToFront();
        SetBlockRaycast(true);

        yield return FadeToAlpha(1f, fadeOutDuration, fadeOutEase);

        isPlaying = false;
        transitionCoroutine = null;

        // 保持黑幕状态与拦截状态
        onComplete?.Invoke();
    }

    private IEnumerator CoPlayExitOnly(Action onComplete, float fadeInDuration)
    {
        isPlaying = true;

        EnsureRuntimeFadeUI();
        BringToFront();
        SetBlockRaycast(true);

        // 确保起始是黑的
        SetAlpha(1f);

        yield return FadeToAlpha(0f, fadeInDuration, fadeInEase);

        isPlaying = false;
        SetBlockRaycast(false);
        transitionCoroutine = null;

        onComplete?.Invoke();
    }

    private IEnumerator FadeToAlpha(float targetAlpha, float duration, Ease ease)
    {
        if (fadeImage == null)
            yield break;

        float currentAlpha = fadeImage.color.a;

        if (Mathf.Approximately(currentAlpha, targetAlpha))
        {
            SetAlpha(targetAlpha);
            yield break;
        }

        if (duration <= 0f)
        {
            SetAlpha(targetAlpha);
            yield break;
        }

        bool tweenDone = false;

        fadeTween = AnimationCompat.Alpha(
            fadeImage,
            endValue: targetAlpha,
            duration: duration,
            ease: ease
        ).OnComplete(() => tweenDone = true);

        yield return new WaitUntil(() => tweenDone);
    }

    private void EnsureRuntimeFadeUI()
    {
        if (fadeCanvas != null && fadeImage != null)
        {
            return;
        }

        Transform existingCanvas = transform.Find(runtimeCanvasName);
        if (existingCanvas != null)
        {
            fadeCanvas = existingCanvas.GetComponent<Canvas>();
            Transform existingImage = existingCanvas.Find(runtimeImageName);
            if (existingImage != null)
            {
                fadeImage = existingImage.GetComponent<Image>();
            }
        }

        if (fadeCanvas == null)
        {
            GameObject canvasGO = new GameObject(
                runtimeCanvasName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster)
            );
            canvasGO.transform.SetParent(transform, false);

            fadeCanvas = canvasGO.GetComponent<Canvas>();
            fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fadeCanvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.localScale = Vector3.one;
            canvasRect.localPosition = Vector3.zero;
        }

        if (fadeImage == null)
        {
            GameObject imageGO = new GameObject(
                runtimeImageName,
                typeof(RectTransform),
                typeof(Image)
            );
            imageGO.transform.SetParent(fadeCanvas.transform, false);

            RectTransform imageRect = imageGO.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            imageRect.localScale = Vector3.one;
            imageRect.localPosition = Vector3.zero;

            fadeImage = imageGO.GetComponent<Image>();
            fadeImage.color = new Color(0f, 0f, 0f, 0f);
            fadeImage.raycastTarget = true;
        }
    }

    private void BringToFront()
    {
        if (fadeCanvas != null)
        {
            fadeCanvas.sortingOrder = sortingOrder;
        }

        if (fadeImage != null)
        {
            fadeImage.transform.SetAsLastSibling();
        }
    }

    private void StopRunningTransition()
    {
        if (fadeTween.isAlive)
        {
            fadeTween.Stop();
        }

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        isPlaying = false;
    }

    private void SetAlpha(float alpha)
    {
        if (fadeImage == null) return;

        Color c = fadeImage.color;
        c.r = 0f;
        c.g = 0f;
        c.b = 0f;
        c.a = alpha;
        fadeImage.color = c;
    }

    private void SetBlockRaycast(bool block)
    {
        if (fadeImage == null) return;
        fadeImage.raycastTarget = block;
    }

    private void OnDisable()
    {
        StopRunningTransition();
    }

    private void OnDestroy()
    {
        StopRunningTransition();
    }
}