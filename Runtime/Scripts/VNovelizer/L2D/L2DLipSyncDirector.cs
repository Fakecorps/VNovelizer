using UnityEngine;
using VNovelizer.Core.Theater;

namespace VNovelizer.L2D
{
    /// <summary>
    /// L2D 口型同步激活器（Fix-L2D-LipSync，2026-09-14）：
    /// 把「VoiceManager 语音播放」与「官方 Cubism 口型组件」桥接起来。
    ///
    /// <para><b>工作机制</b>（共享源方案）：语音仍由 VoiceManager 的全局 AudioSource
    /// 播放（播放/音量/AutoPlay 判定零改动）；本激活器监听语音开始/停止事件，
    /// 按"说话者角色 ID"解析台上对应的 L2D 演员，仅启用该演员的
    /// <c>CubismAudioMouthInput</c>（其 AudioInput 懒绑定到全局语音源，官方组件
    /// 每帧 GetOutputData 采样 RMS 驱动口型），其余演员口型保持关闭。
    /// 说话者不是 L2D / 未登台 / 无说话者上下文时静默降级（无口型）。</para>
    ///
    /// <para><b>事件顺序保证</b>：StopVoice 先触发 Stopped（关闭旧口型）→
    /// 新语音开始触发 Started（打开新口型）；播放自然结束在协程尾部触发 Stopped；
    /// 显式停止已中止协程，不会二次触发。</para>
    /// </summary>
    public static class L2DLipSyncDirector
    {
        private static bool _installed;

        /// <summary>当前启用口型的演员（语音停止时复位用）</summary>
        private static ILipSyncHost _activeHost;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            var vm = VoiceManager.GetInstance();
            vm.VoicePlaybackStarted += OnVoiceStarted;
            vm.VoicePlaybackStopped += OnVoiceStopped;
        }

        private static void OnVoiceStarted(string speakerId)
        {
            var host = ResolveHost(speakerId);

            // 切换说话者：先关旧演员口型再开新的（同屏多 L2D 角色只有说话者动嘴）
            if (_activeHost != null && !ReferenceEquals(_activeHost, host))
                _activeHost.SetLipSyncActive(false);

            _activeHost = host;
            if (_activeHost != null)
                _activeHost.SetLipSyncActive(true);
        }

        private static void OnVoiceStopped()
        {
            if (_activeHost != null)
            {
                _activeHost.SetLipSyncActive(false);
                _activeHost = null;
            }
        }

        /// <summary>
        /// 解析说话者对应的口型宿主：角色必须已注册（L2D 配置）且当前登台，
        /// 台上演员需实现 <see cref="ILipSyncHost"/>（L2DActor）。
        /// 严格精确匹配——Speaker 列必须与注册的 CharacterID 完全一致（大小写不敏感），
        /// 不做任何模糊回退。
        /// 说话者未注册任何角色配置 = 非 L2D 角色（纯立绘工程/旁白配音等常态），静默降级；
        /// 仅对"L2D 配置存在但口型无法生效"的异常情况（未登台/演员不支持）输出警告。
        /// </summary>
        private static ILipSyncHost ResolveHost(string speakerId)
        {
            if (string.IsNullOrEmpty(speakerId)) return null;

            var profile = CharacterResManager.GetInstance().TryGetCharacterProfile(speakerId);
            if (profile == null)
                return null; // 【Fix-L2D-Lip】非 L2D 角色是常态（纯立绘工程每句配音都会走到这里），静默跳过
            if (profile.IsSpriteBased)
                return null; // 静态立绘角色无口型，静默跳过

            var actor = TheaterManager.GetInstance().FindCharacterActor(speakerId);
            if (actor == null)
            {
                Debug.LogWarning($"[L2DLipSync] L2D 角色 '{speakerId}' 未在台上（本行立绘列未登台），口型不生效");
                return null;
            }

            var host = actor as ILipSyncHost;
            if (host == null)
                Debug.LogWarning($"[L2DLipSync] 角色 '{speakerId}' 的演员不支持口型（非 L2DActor），口型不生效");
            return host;
        }
    }
}
