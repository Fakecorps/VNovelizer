using System;
using System.Collections.Generic;
using System.Text;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 条件表达式解析与求值（jumpif / jumpifnot / loadscriptif / loadscriptifnot 共用）。
    /// 语法：
    ///   flagName                 → bool 直判（判定为 true）
    ///   !flagName                → bool 取反
    ///   flagName op value        → 比较（op: &gt; &lt; &gt;= &lt;= == !=；先匹配双字符再匹配单字符）
    ///   字符串值含逗号时必须用引号包裹：PlayerName == "Alice, B"
    /// 纯静态、不依赖 Unity 生命周期，可被 Editor 静态校验与测试复用。
    /// 详见 Docs/VNFlagSystemDesign.md §5。
    /// </summary>
    public static class ConditionParser
    {
        /// <summary>结构化条件（Op 为 null 表示 bool 直判）</summary>
        public class Condition
        {
            public string Name;
            public bool Negated;
            public string Op;
            public string Value;
            public bool ValueIsQuoted;
        }

        /// <summary>
        /// 引号/括号感知的顶层分割：跳过成对引号与括号内的分隔符。
        /// 用于 jumpif(cond, targetId) / loadscriptif(cond, script, startId) 的参数拆分。
        /// </summary>
        public static List<string> SplitTopLevel(string s, char separator = ',')
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(s)) return result;

            var sb = new StringBuilder();
            bool inQuote = false;
            char quoteChar = '\0';
            int depth = 0;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inQuote)
                {
                    sb.Append(c);
                    if (c == quoteChar) inQuote = false;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quoteChar = c;
                    sb.Append(c);
                    continue;
                }
                if (c == '(') { depth++; sb.Append(c); continue; }
                if (c == ')') { depth--; sb.Append(c); continue; }
                if (c == separator && depth == 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }

        /// <summary>解析条件表达式（失败时返回 false 并给出 error 描述）</summary>
        public static bool TryParse(string raw, out Condition cond, out string error)
        {
            cond = null;
            error = null;
            string s = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(s))
            {
                error = "条件为空";
                return false;
            }

            // 引号外查找 operator（先双字符后单字符，避免 ">=" 被截断为 ">"）
            string[] operators = { ">=", "<=", "==", "!=", ">", "<" };
            int opIndex = -1;
            string op = null;
            foreach (string o in operators)
            {
                opIndex = FindOutsideQuotes(s, o);
                if (opIndex >= 0)
                {
                    op = o;
                    break;
                }
            }

            var c = new Condition();
            if (op == null)
            {
                // bool 直判（可带 ! 前缀）
                string name = s;
                if (name.StartsWith("!", StringComparison.Ordinal))
                {
                    c.Negated = true;
                    name = name.Substring(1).Trim();
                }
                if (string.IsNullOrEmpty(name))
                {
                    error = "flag 名为空";
                    return false;
                }
                c.Name = name;
            }
            else
            {
                c.Name = s.Substring(0, opIndex).Trim();
                c.Op = op;
                string val = s.Substring(opIndex + op.Length).Trim();
                if (val.Length >= 2 && ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'"))))
                {
                    c.Value = val.Substring(1, val.Length - 2);
                    c.ValueIsQuoted = true;
                }
                else
                {
                    c.Value = val;
                }

                if (string.IsNullOrEmpty(c.Name))
                {
                    error = "条件左侧 flag 名为空";
                    return false;
                }
                if (string.IsNullOrEmpty(c.Value) && !c.ValueIsQuoted)
                {
                    error = "条件右侧值为空";
                    return false;
                }
            }

            cond = c;
            return true;
        }

        /// <summary>求值（按 FlagService 中的实际类型比较）</summary>
        public static bool Evaluate(Condition c, FlagService flags)
        {
            if (c == null) throw new ArgumentException("条件为空");
            if (flags == null) throw new ArgumentException("Flag 服务不可用");

            FlagType type = flags.GetFlagType(c.Name);

            // bool 直判
            if (c.Op == null)
            {
                if (type != FlagType.Bool)
                {
                    throw new ArgumentException(string.Format(
                        "flag '{0}' 类型为 {1}，直判语法仅支持 Bool（比较请使用 operator，如 {0} >= 1）", c.Name, type));
                }
                bool v = flags.GetBool(c.Name);
                return c.Negated ? !v : v;
            }

            // 宽松处理：未注册 + 数值比较 → 按数值（以当前值/0 为基准）
            if (type == FlagType.Bool && c.Op != null && !flags.IsRegistered(c.Name))
            {
                double dummy;
                if (double.TryParse(c.Value, out dummy)) type = FlagType.Float;
            }

            switch (type)
            {
                case FlagType.Bool:
                    throw new ArgumentException(string.Format(
                        "flag '{0}' 类型为 Bool，不支持 operator '{1}'", c.Name, c.Op));

                case FlagType.Int:
                {
                    long rhs;
                    if (!long.TryParse(c.Value, out rhs))
                        throw new ArgumentException(string.Format("无法将 '{0}' 解析为整数", c.Value));
                    long lhs = flags.GetInt(c.Name);
                    return CompareLong(lhs, rhs, c.Op);
                }

                case FlagType.Float:
                {
                    double rhs;
                    if (!double.TryParse(c.Value, out rhs))
                        throw new ArgumentException(string.Format("无法将 '{0}' 解析为数值", c.Value));
                    double lhs = flags.GetFloat(c.Name);
                    return CompareDouble(lhs, rhs, c.Op);
                }

                default: // String
                {
                    if (c.Op != "==" && c.Op != "!=")
                        throw new ArgumentException(string.Format(
                            "flag '{0}' 类型为 String，仅支持 == / !=", c.Name));
                    bool eq = flags.GetString(c.Name) == c.Value;
                    return c.Op == "==" ? eq : !eq;
                }
            }
        }

        /// <summary>解析并求值一步完成（编辑器校验与命令共用）</summary>
        public static bool TryEvaluate(string raw, FlagService flags, out bool result, out string error)
        {
            result = false;
            Condition cond;
            if (!TryParse(raw, out cond, out error)) return false;
            try
            {
                result = Evaluate(cond, flags);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        // ==================== 内部工具 ====================

        /// <summary>在引号外查找子串（operator 定位用）</summary>
        private static int FindOutsideQuotes(string s, string needle)
        {
            bool inQuote = false;
            char quoteChar = '\0';
            for (int i = 0; i <= s.Length - needle.Length; i++)
            {
                char c = s[i];
                if (inQuote)
                {
                    if (c == quoteChar) inQuote = false;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quoteChar = c;
                    continue;
                }
                if (c == needle[0] && s.Substring(i, needle.Length) == needle) return i;
            }
            return -1;
        }

        private static bool CompareLong(long l, long r, string op)
        {
            switch (op)
            {
                case ">": return l > r;
                case "<": return l < r;
                case ">=": return l >= r;
                case "<=": return l <= r;
                case "==": return l == r;
                case "!=": return l != r;
                default: throw new ArgumentException("未知 operator: " + op);
            }
        }

        private static bool CompareDouble(double l, double r, string op)
        {
            switch (op)
            {
                case ">": return l > r;
                case "<": return l < r;
                case ">=": return l >= r;
                case "<=": return l <= r;
                case "==": return l == r;
                case "!=": return l != r;
                default: throw new ArgumentException("未知 operator: " + op);
            }
        }
    }
}
