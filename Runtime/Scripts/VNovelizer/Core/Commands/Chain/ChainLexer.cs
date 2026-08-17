using System.Collections.Generic;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>命令链 Token 类型</summary>
    public enum ChainTokenType
    {
        Command,   // 命令单元，如 showChar(Amy)
        Amp,       // &  并行
        Arrow,     // -> 串行
        LBracket,  // [
        RBracket   // ]
    }

    /// <summary>命令链 Token</summary>
    public struct ChainToken
    {
        public ChainTokenType Type;
        public string Text;
        public int Position;

        public ChainToken(ChainTokenType type, string text, int position)
        {
            Type = type;
            Text = text;
            Position = position;
        }
    }

    /// <summary>
    /// 命令链词法切分器。
    /// 将 Command 列字符串切分为 Token 流，规则：
    /// - 引号内的字符不参与符号识别（参数含 &、->、[] 时必须引号包裹）
    /// - 命令括号（含嵌套）内部不切分，保证 choice(@loc:key|jump(...)) 这类复杂参数完整
    /// - 忽略所有空白（含换行，支持单元格内多行排版）
    /// </summary>
    public static class ChainLexer
    {
        /// <summary>
        /// 切分命令串为 Token 流。
        /// </summary>
        /// <param name="source">Command 列原始字符串</param>
        /// <param name="errors">错误收集列表（可为 null）</param>
        public static List<ChainToken> Tokenize(string source, List<ChainError> errors = null)
        {
            var tokens = new List<ChainToken>();
            if (string.IsNullOrEmpty(source)) return tokens;

            int pos = 0;
            int len = source.Length;

            while (pos < len)
            {
                char c = source[pos];

                // 跳过空白（含换行）
                if (char.IsWhiteSpace(c)) { pos++; continue; }

                // 符号 Token
                if (c == '&')
                {
                    tokens.Add(new ChainToken(ChainTokenType.Amp, "&", pos));
                    pos++;
                    continue;
                }
                if (c == '[')
                {
                    tokens.Add(new ChainToken(ChainTokenType.LBracket, "[", pos));
                    pos++;
                    continue;
                }
                if (c == ']')
                {
                    tokens.Add(new ChainToken(ChainTokenType.RBracket, "]", pos));
                    pos++;
                    continue;
                }
                if (c == '-' && pos + 1 < len && source[pos + 1] == '>')
                {
                    tokens.Add(new ChainToken(ChainTokenType.Arrow, "->", pos));
                    pos += 2;
                    continue;
                }

                // 命令单元：读取到顶层（括号深度 0 且引号外）分隔符为止
                int start = pos;
                int parenDepth = 0;
                bool inQuote = false;

                while (pos < len)
                {
                    char cc = source[pos];

                    if (inQuote)
                    {
                        if (cc == '\\') { pos += 2; continue; } // 转义（如 \"）
                        if (cc == '"') inQuote = false;
                        pos++;
                        continue;
                    }

                    if (cc == '"') { inQuote = true; pos++; continue; }

                    if (cc == '(') { parenDepth++; pos++; continue; }

                    if (cc == ')')
                    {
                        parenDepth--;
                        pos++;
                        continue; // 括号闭合后仍可继续读取（顶层遇到分隔符才断开）
                    }

                    if (parenDepth == 0)
                    {
                        // 顶层遇到链式语法符号 → 命令单元结束
                        if (cc == '&' || cc == '[' || cc == ']') break;
                        if (cc == '-' && pos + 1 < len && source[pos + 1] == '>') break;
                    }

                    pos++;
                }

                string text = source.Substring(start, pos - start).Trim();
                if (text.Length > 0)
                {
                    tokens.Add(new ChainToken(ChainTokenType.Command, text, start));
                }
                else
                {
                    // 空命令单元（如 "&&" 或 "& ->" 中间）——记录错误但继续前进
                    errors?.Add(new ChainError("空的命令单元（悬空操作符附近）", start));
                    pos++; // 防止死循环
                }
            }

            return tokens;
        }
    }
}
