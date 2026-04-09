namespace VNovelizer.Core
{
    /// <summary>
    /// EventCenter 事件名与常用 payload 键，避免魔法字符串分散。
    /// </summary>
    public static class VNGameEvents
    {
        public const string UpdateDialogue = "UpdateDialogue";
        public const string UpdateHeadProfile = "UpdateHeadProfile";
        public const string ChangeBackground = "ChangeBackground";
        public const string ShowCharacter = "ShowCharacter";
        public const string HideCharacter = "HideCharacter";
        /// <summary>无参事件（EventTrigger 无参重载）。</summary>
        public const string HideBackground = "HideBackground";
        public const string TypingFinished = "TypingFinished";
        public const string DisplayAllText = "DisplayAllText";
        public const string ToggleAutoPlay = "ToggleAutoPlay";
        public const string ToggleSkip = "ToggleSkip";
        public const string AddHistoryEntry = "AddHistoryEntry";

        public const string KeySpeaker = "speaker";
        public const string KeyText = "text";
        public const string KeyHeadProfile = "headProfile";
    }
}
