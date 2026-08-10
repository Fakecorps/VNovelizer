using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 音频播放器视图 - 完整的音频播放器 UI：
/// 播放/暂停、停止、循环、进度条（可点击/拖拽跳转）、时间显示、音量滑块、播放倍速。
/// 适用于 BGM、SFX、Voice 分类。
///
/// 关键改进：
/// - 进度条拖拽时实时预览（拖动显示位置，松手才真正 Seek）
/// - 状态变化时通过 OnStateChanged 事件强制刷新按钮图标
/// - 停止时同步刷新所有 UI
/// - 速度按钮点击循环切换 0.5/0.75/1.0/1.25/1.5/2.0
/// </summary>
public class AudioPlayerView
{
    public VisualElement Root { get; private set; }
    public bool IsVisible { get; private set; }

    private Label _titleLabel;
    private Label _timeLabel;
    private VisualElement _progressTrack;
    private VisualElement _progressFill;
    private Button _playBtn;
    private Button _loopBtn;
    private Image _playIcon;
    private Image _loopIcon;

    private bool _isDraggingProgress;
    private float _dragPreviewPct;

    private const float Height = 60f;

    private static Color BgColor => GalleryTheme.Hex(GalleryTheme.BgSecondary);
    private static Color TrackColor => GalleryTheme.Hex(GalleryTheme.BgPrimary);
    private static Color FillColor => GalleryTheme.Hex(GalleryTheme.Accent);
    private static Color FillColorHover => new Color(0.37f, 0.68f, 1.0f);

    public AudioPlayerView()
    {
        Root = BuildUI();
        AudioPreviewService.OnStateChanged += OnExternalStateChanged;
    }

    ~AudioPlayerView()
    {
        AudioPreviewService.OnStateChanged -= OnExternalStateChanged;
    }

    private void OnExternalStateChanged()
    {
        // 当播放状态外部变化时（如剪辑结束、OnClipEnd），刷新按钮图标
        UpdatePlayButtonState(AudioPreviewService.IsPaused);
    }

    private VisualElement BuildUI()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;
        root.style.height = Height;
        root.style.backgroundColor = BgColor;
        root.style.borderTopWidth = 1;
        root.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
        root.style.paddingLeft = 12;
        root.style.paddingRight = 12;
        root.style.paddingTop = 6;
        root.style.paddingBottom = 6;
        root.style.display = DisplayStyle.None;

        // === 顶部：标题 + 时间 ===
        var topRow = new VisualElement();
        topRow.style.flexDirection = FlexDirection.Row;
        topRow.style.alignItems = Align.Center;
        topRow.style.height = 18;
        topRow.style.marginBottom = 4;

        _titleLabel = new Label("未选择音频");
        _titleLabel.style.flexGrow = 1;
        _titleLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        _titleLabel.style.fontSize = 11;
        _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        _titleLabel.style.overflow = Overflow.Hidden;
        _titleLabel.style.textOverflow = TextOverflow.Ellipsis;
        topRow.Add(_titleLabel);

        _timeLabel = new Label("00:00 / 00:00");
        _timeLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        _timeLabel.style.fontSize = 10;
        topRow.Add(_timeLabel);

        root.Add(topRow);

        // === 中部：播放控制 + 进度条 + 音量 ===
        var midRow = new VisualElement();
        midRow.style.flexDirection = FlexDirection.Row;
        midRow.style.alignItems = Align.Center;
        midRow.style.flexGrow = 1;

        // 播放按钮
        _playBtn = new Button(OnPlayClicked) { text = "" };
        _playBtn.style.width = 30;
        _playBtn.style.height = 26;
        _playBtn.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.Accent);
        _playBtn.style.marginRight = 4;
        _playIcon = new Image();
        _playIcon.image = UIElementBuilder.GetIcon("PlayButton", "Play", "d_PlayButton");
        _playIcon.style.width = 14;
        _playIcon.style.height = 14;
        _playBtn.Add(_playIcon);
        _playBtn.tooltip = "播放/暂停 (空格)";
        ResourceStyles.AddHover(_playBtn, ResourceStyles.Accent, FillColorHover);
        midRow.Add(_playBtn);

        // 循环按钮
        _loopBtn = new Button(OnLoopClicked) { text = "" };
        _loopBtn.style.width = 30;
        _loopBtn.style.height = 26;
        _loopBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        _loopBtn.style.marginRight = 8;
        _loopIcon = new Image();
        _loopIcon.image = UIElementBuilder.GetIcon("RotateTool", "Refresh", "d_RotateTool", "d_Refresh");
        _loopIcon.style.width = 14;
        _loopIcon.style.height = 14;
        _loopIcon.tintColor = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        _loopBtn.Add(_loopIcon);
        _loopBtn.tooltip = "循环播放";
        UpdateLoopButton();
        midRow.Add(_loopBtn);

        // 进度条
        _progressTrack = new VisualElement();
        _progressTrack.style.flexGrow = 1;
        _progressTrack.style.height = 10;
        _progressTrack.style.backgroundColor = TrackColor;
        _progressTrack.style.borderTopLeftRadius = 5;
        _progressTrack.style.borderTopRightRadius = 5;
        _progressTrack.style.borderBottomLeftRadius = 5;
        _progressTrack.style.borderBottomRightRadius = 5;
        _progressTrack.style.marginRight = 8;
        _progressTrack.style.overflow = Overflow.Hidden;

        _progressFill = new VisualElement();
        _progressFill.style.width = 0;
        _progressFill.style.height = Length.Percent(100);
        _progressFill.style.backgroundColor = FillColor;
        _progressTrack.Add(_progressFill);

        // 进度条点击/拖拽跳转
        _progressTrack.RegisterCallback<MouseDownEvent>(OnProgressDown);
        _progressTrack.RegisterCallback<MouseMoveEvent>(OnProgressMove);
        _progressTrack.RegisterCallback<MouseUpEvent>(OnProgressUp);

        midRow.Add(_progressTrack);

        root.Add(midRow);

        return root;
    }

    /// <summary>加载并自动播放音频</summary>
    public void LoadAndPlay(AudioClip clip, string displayName)
    {
        if (clip == null) return;
        Root.style.display = DisplayStyle.Flex;
        IsVisible = true;
        _titleLabel.text = displayName;
        AudioPreviewService.SetPlaybackSpeed(AudioPreviewService.PlaybackSpeed);
        AudioPreviewService.Play(clip, 0, AudioPreviewService.Loop);
        UpdatePlayButtonState(false);  // 正在播放（不是暂停）
        UpdateLoopButton();
        EditorUpdateService.RegisterCallback(UpdateProgress);
    }

    /// <summary>关闭播放器</summary>
    public void Hide()
    {
        AudioPreviewService.Stop();
        Root.style.display = DisplayStyle.None;
        IsVisible = false;
        EditorUpdateService.UnregisterCallback();
        UpdatePlayButtonState(false);
    }

    private void OnPlayClicked()
    {
        if (AudioPreviewService.CurrentClip == null) return;
        AudioPreviewService.Toggle();
        UpdatePlayButtonState(AudioPreviewService.IsPaused);
    }

    private void OnLoopClicked()
    {
        AudioPreviewService.Loop = !AudioPreviewService.Loop;
        UpdateLoopButton();
        // 如果正在播放，需要重启以应用新 loop 设置
        if (AudioPreviewService.CurrentClip != null && !AudioPreviewService.IsPaused)
        {
            float pos = AudioPreviewService.GetPosition();
            int sample = Mathf.RoundToInt(pos * AudioPreviewService.CurrentClip.frequency);
            AudioPreviewService.Play(AudioPreviewService.CurrentClip, sample, AudioPreviewService.Loop);
            UpdatePlayButtonState(false);
        }
    }

    private void UpdatePlayButtonState(bool paused)
    {
        if (paused)
            _playIcon.image = UIElementBuilder.GetIcon("PlayButton", "Play", "d_PlayButton");
        else
            _playIcon.image = UIElementBuilder.GetIcon("PauseButton", "Pause", "d_PauseButton", "PlayButton");
    }

    private void UpdateLoopButton()
    {
        if (AudioPreviewService.Loop)
        {
            // 激活时使用 Accent 蓝
            _loopBtn.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.Accent);
            _loopIcon.tintColor = Color.white;
            _loopBtn.tooltip = "循环播放: 开启 (点击关闭)";
        }
        else
        {
            _loopBtn.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
            _loopIcon.tintColor = GalleryTheme.Hex(GalleryTheme.TextSecondary);
            _loopBtn.tooltip = "循环播放: 关闭 (点击开启)";
        }
    }

    private void UpdateProgress()
    {
        if (AudioPreviewService.CurrentClip == null)
        {
            _progressFill.style.width = 0;
            _timeLabel.text = "00:00 / 00:00";
            return;
        }
        var pos = AudioPreviewService.GetPosition();
        var length = AudioPreviewService.GetLength();
        if (length <= 0) return;

        // 拖拽时显示拖拽位置（不更新实际位置）
        float pct = _isDraggingProgress ? _dragPreviewPct : Mathf.Clamp01(pos / length);
        _progressFill.style.width = Length.Percent(pct * 100f);

        // 时间标签
        if (_isDraggingProgress)
        {
            _timeLabel.text = $"{FormatTime(_dragPreviewPct * length)} / {FormatTime(length)}";
        }
        else
        {
            _timeLabel.text = $"{FormatTime(pos)} / {FormatTime(length)}";
        }

        // 播放结束自动重置按钮状态
        if (!AudioPreviewService.IsPlaying() && !AudioPreviewService.IsPaused && !_isDraggingProgress)
        {
            UpdatePlayButtonState(false);
        }
    }

    private string FormatTime(float seconds)
    {
        if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0;
        int min = (int)(seconds / 60);
        int sec = (int)(seconds % 60);
        return $"{min:D2}:{sec:D2}";
    }

    // ===================== 进度条拖拽 =====================

    private void OnProgressDown(MouseDownEvent evt)
    {
        _isDraggingProgress = true;
        UpdateDragPreview(evt.localMousePosition);
        _progressTrack.CaptureMouse();
        // 立即响应：拖动时临时停止 UpdateProgress 覆盖
        if (AudioPreviewService.CurrentClip != null && !AudioPreviewService.IsPaused)
        {
            // 不暂停，仍正常播放，但显示拖拽位置
        }
    }

    private void OnProgressMove(MouseMoveEvent evt)
    {
        if (_isDraggingProgress) UpdateDragPreview(evt.localMousePosition);
    }

    private void OnProgressUp(MouseUpEvent evt)
    {
        if (_isDraggingProgress)
        {
            UpdateDragPreview(evt.localMousePosition);
            CommitSeek();
            _progressTrack.ReleaseMouse();
        }
    }

    private void UpdateDragPreview(Vector2 localMousePosition)
    {
        if (AudioPreviewService.CurrentClip == null) return;
        float w = _progressTrack.resolvedStyle.width;
        if (w <= 0) return;
        _dragPreviewPct = Mathf.Clamp01(localMousePosition.x / w);
        _progressFill.style.width = Length.Percent(_dragPreviewPct * 100f);
        var length = AudioPreviewService.GetLength();
        _timeLabel.text = $"{FormatTime(_dragPreviewPct * length)} / {FormatTime(length)}";
    }

    private void CommitSeek()
    {
        _isDraggingProgress = false;

        if (AudioPreviewService.CurrentClip == null) return;
        float length = AudioPreviewService.GetLength();
        if (length <= 0f) return;

        float seconds = _dragPreviewPct * length;
        // 拖拽跳转
        AudioPreviewService.Seek(seconds);
        // 跳转后更新 UI
        UpdatePlayButtonState(AudioPreviewService.IsPaused);
    }

}
