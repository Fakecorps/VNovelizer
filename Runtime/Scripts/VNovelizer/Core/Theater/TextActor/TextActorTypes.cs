using System;
using System.Collections.Generic;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 一段累积正文的数据记录。
    /// 只保存内容与来源（剧本/行/说话者），不序列化 TMP/GameObject——
    /// 渲染实例可随时按数据重建（读档恢复、换场景重建）。
    /// </summary>
    [Serializable]
    public class TextActorParagraph
    {
        public string ScriptName;
        public string LineId;
        public string Speaker;
        public string Text;
    }

    /// <summary>
    /// 单个文字演员的存档快照（<see cref="SaveData.TextActorStates"/> 的条目）。
    /// ActorId = 槽位 posCode（L/ML/M/MR/R）。
    /// </summary>
    [Serializable]
    public class TextActorSaveState
    {
        public string ActorId;
        public List<TextActorParagraph> Paragraphs = new List<TextActorParagraph>();
    }
}
