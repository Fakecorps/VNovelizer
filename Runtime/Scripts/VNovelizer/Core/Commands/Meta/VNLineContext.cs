namespace VNovelizer.Core.Commands.Meta
{
    /// <summary>
    /// 「当前正在处理的剧本行」的**解析后**快照，供系统命令的隐式绑定读取。
    ///
    /// <para>
    /// <b>为何需要它</b>：系统命令（<c>showDialogue()</c> / <c>showbg()</c> / <c>playBGM()</c> …）
    /// 的参数留空时表示"引用本行数据列"，但 <c>VNCommand.Execute(string args)</c> /
    /// <c>Simulate(string args)</c> 的签名里只有 args，命令内部无从知道自己属于哪一行。
    /// 由 <c>VNManager</c> 在进入每行处理前填充本上下文，命令经
    /// <c>VNAPI.GetCurrentLineContext()</c> 读取。
    /// </para>
    ///
    /// <para>
    /// <b>为何是"解析后"而非 StoryLine 原始字段</b>：背景的"空单元格 = 继承上一有效值"、
    /// 语音的"空 = 按行 ID 自动生成路径"等规则必须已经应用，否则 <c>showbg()</c>
    /// 读到空串会误判为"无背景"。因此本类型承载的是 <c>VNManager.ResolveLine()</c>
    /// 的结果，而非 <c>StoryLine</c> 的裸值。
    /// </para>
    ///
    /// <para>
    /// <b>为何不直接暴露 ResolvedLine</b>：<c>ResolvedLine</c> 是 <c>VNManager</c> 的
    /// private struct，且不含五个立绘槽位（立绘走 <c>UpdateCharacterSlots</c> 另一条路径）。
    /// 而 <c>showChar(pos)</c> 需要立绘引用，故本类型是"解析结果 + 立绘槽位 + 行标识"的并集。
    /// </para>
    ///
    /// <para>
    /// <b>不可变性</b>：所有字段只读。命令不应、也无法通过它改写行数据——
    /// 剧本数据（<c>StoryLine</c>）是长期存活的共享对象，写回会把"当次播放的运行时状态"
    /// 永久烙进剧本行（详见 <c>VNManager.ResolvedLine</c> 的注释）。
    /// </para>
    /// </summary>
    public class VNLineContext
    {
        /// <summary>行 ID（本地化键 <c>text.{lineID}</c> / <c>speaker.{lineID}</c> 的来源）</summary>
        public string LineID { get; }

        /// <summary>行在 StoryLines 中的下标</summary>
        public int LineIndex { get; }

        /// <summary>说话人（不继承，原样透传）</summary>
        public string Speaker { get; }

        /// <summary>正文（不继承，原样透传）</summary>
        public string Text { get; }

        /// <summary>头像引用（不继承）</summary>
        public string HeadProfile { get; }

        /// <summary>背景（**继承已应用**：空单元格已替换为上一有效背景）</summary>
        public string Background { get; }

        /// <summary>BGM（按 CSV 原值，空 = 不动，由播放侧判定）</summary>
        public string BGM { get; }

        /// <summary>语音（**自动路径已生成**；语音关闭时为空串）</summary>
        public string Voice { get; }

        /// <summary>左槽立绘引用（不继承，空 = 该槽隐藏）</summary>
        public string CharLeft { get; }

        /// <summary>中左槽立绘引用</summary>
        public string CharMidLeft { get; }

        /// <summary>中槽立绘引用</summary>
        public string CharMid { get; }

        /// <summary>中右槽立绘引用</summary>
        public string CharMidRight { get; }

        /// <summary>右槽立绘引用</summary>
        public string CharRight { get; }

        /// <summary>
        /// 当前处于**快进预演**（Simulate）而非真实播放（Execute）。
        /// 系统命令据此决定"只更新内部状态"还是"播放动画/音频"。
        /// </summary>
        public bool IsSimulating { get; }

        public VNLineContext(
            string lineID, int lineIndex,
            string speaker, string text, string headProfile,
            string background, string bgm, string voice,
            string charLeft, string charMidLeft, string charMid,
            string charMidRight, string charRight,
            bool isSimulating)
        {
            LineID = lineID;
            LineIndex = lineIndex;
            Speaker = speaker;
            Text = text;
            HeadProfile = headProfile;
            Background = background;
            BGM = bgm;
            Voice = voice;
            CharLeft = charLeft;
            CharMidLeft = charMidLeft;
            CharMid = charMid;
            CharMidRight = charMidRight;
            CharRight = charRight;
            IsSimulating = isSimulating;
        }

        /// <summary>
        /// 按槽位代码取立绘引用。<paramref name="posCode"/> 接受
        /// L / ML / M / MR / R 或全名 Left / MidLeft / Mid / MidRight / Right（大小写不敏感）。
        /// 未知槽位返回 null。
        /// </summary>
        public string GetCharBySlot(string posCode)
        {
            if (string.IsNullOrEmpty(posCode)) return null;

            switch (posCode.Trim().ToLower())
            {
                case "l":
                case "left":      return CharLeft;
                case "ml":
                case "midleft":   return CharMidLeft;
                case "m":
                case "mid":       return CharMid;
                case "mr":
                case "midright":  return CharMidRight;
                case "r":
                case "right":     return CharRight;
                default:          return null;
            }
        }

        /// <summary>
        /// 按数据列名取值，供隐式绑定按 <c>[VNParam(BoundColumn = "...")]</c> 声明泛化读取。
        /// 列名大小写不敏感；未知列返回 null。
        /// </summary>
        public string GetColumn(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return null;

            switch (columnName.Trim().ToLower())
            {
                case "id":          return LineID;
                case "speaker":     return Speaker;
                case "text":        return Text;
                case "headprofile": return HeadProfile;
                case "background":  return Background;
                case "bgm":         return BGM;
                case "voice":       return Voice;
                case "charleft":    return CharLeft;
                case "charmid_left":
                case "charmidleft": return CharMidLeft;
                case "charmid":     return CharMid;
                case "charmid_right":
                case "charmidright":return CharMidRight;
                case "charright":   return CharRight;
                default:            return null;
            }
        }

        public override string ToString()
        {
            return $"VNLineContext(ID={LineID}, Index={LineIndex}, " +
                   $"Simulating={IsSimulating}, Speaker='{Speaker}', BG='{Background}')";
        }
    }
}
