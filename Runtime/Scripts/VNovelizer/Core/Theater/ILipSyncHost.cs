namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 口型同步宿主接口——动态演员（Live2D 等）实现，语音系统（VoiceManager）与
    /// 外部口型激活器（如插件内 L2DLipSyncDirector）据此开关模型的嘴部动画。
    ///
    /// <para>
    /// 核心程序集零外部依赖：接口只出现核心自有类型。实现侧负责官方 SDK 组件
    /// （CubismMouthController / CubismAudioMouthInput）的驱动细节。
    /// </para>
    ///
    /// <para><b>语义</b>：active=true 表示"该演员是当前说话者"，应跟随正在播放的
    /// 语音动嘴；false 表示停止动嘴（实现侧需把口型归零，防止嘴部停在张开状态）。</para>
    /// </summary>
    public interface ILipSyncHost
    {
        /// <summary>
        /// 启用 / 停用口型同步。
        /// </summary>
        /// <param name="active">true = 开始跟随语音动嘴；false = 停止并复位口型</param>
        void SetLipSyncActive(bool active);
    }
}
