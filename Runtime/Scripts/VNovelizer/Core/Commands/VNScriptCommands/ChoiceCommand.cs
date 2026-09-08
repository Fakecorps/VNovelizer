using VNovelizer.Core.Localization;
using VNovelizer.Core.Diagnostics;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Core.Commands
{
    [VNCommandMeta(VNCommandCategory.Flow,
        "添加选项按钮（同一行多个 choice 会汇集成同一面板；本地化用 @loc:key，仅可置于链尾）。\n" +
        "R11 块语法：choice{描述1, 命令链1, 描述2, 命令链2, ...}——每个选项一条命令链，支持完整链语法与嵌套 choice。",
        ArgSeparator = '|')]
    public class ChoiceCommand : VNCommand
    {
        [VNParam(0, "text", VNParamType.String,
            Description = "选项文字；本地化写 @loc:表名.键，如 @loc:VNScript_CH1.choice_a")]
        [VNParam(1, "command", VNParamType.String, Optional = true,
            Description = "点击后执行的命令链，如 jump(Scene_010) 或 loadscript(Chapter2)")]
        public override string CommandName { get { return "choice"; } }

        /// <summary>
        /// 旧语法入口（R11 兼容路径）：<c>choice(desc|cmd)</c>。
        /// 新块语法 <c>choice{...}</c> 被 ChainParser 解析为 <see cref="ChoiceNode"/>，
        /// 由 <see cref="ChainExecutor"/> 经 <see cref="PushChoiceOptions"/> 推入面板，不经本方法。
        /// </summary>
        public override bool Execute(string args)
        {
            // 1. 切换到 Choice 状态，阻止游戏点击下一句
            GameStateManager.GetInstance().SetState(GameState.Choice);

            // 2. 解析参数 (使用新的 | 分隔符)
            var result = ParseArgs(args);
            string text = result.Item1;
            string cmd = result.Item2;

            text = ResolveLocalizedText(text);

            VNDebug.LogVerbose($"[ChoiceCommand] 解析选项 -> Text: {text}, Cmd: {cmd}");

            // 旧语法无段上下文，点击后走 ExecuteChoiceCommand（进入段语义，与旧版一致）
            LastPushedIsConfirm = false;

            PushSingleChoice(text, cmd, null);
            return true;
        }

        // ---------------- R11：块语法面板入口（ChainExecutor 调用） ----------------

        /// <summary>
        /// 最后一批推入面板的选项所属段（false = 进入段，true = 出口段）。
        /// <see cref="ChoicePanel.OnChoiceClicked"/> 据此把选项链交还给对应段的推进语义。
        /// 同一面板的所有选项必然同段：进入段含 choice 时出口段不会执行（点击选项即消费出口）。
        /// </summary>
        public static bool LastPushedIsConfirm { get; private set; }

        /// <summary>
        /// 把 <see cref="ChoiceNode"/> 的全部选项推入共享 ChoicePanel。
        /// 不阻塞主链——玩家点击后由 <see cref="VNManager.ExecuteChoiceChain"/> 执行选项链。
        /// 同一行多个 choice 节点（或新旧语法混合）会自然合并展示（决策 7）。
        /// </summary>
        /// <param name="isConfirmChain">choice 所在段（出口段 choice 在 R11 修订后合法）</param>
        public static void PushChoiceOptions(ChoiceNode choice, bool isConfirmChain)
        {
            if (choice == null) return;

            LastPushedIsConfirm = isConfirmChain;

            // 切换到 Choice 状态，阻止游戏点击下一句
            GameStateManager.GetInstance().SetState(GameState.Choice);

            foreach (var opt in choice.Options)
            {
                PushSingleChoice(ResolveLocalizedText(opt?.Text), null, opt?.Chain);
            }
        }

        /// <summary>推单个选项：本地化解析 + 打开/复用面板。</summary>
        private static void PushSingleChoice(string text, string commandText, ChainNode chain)
        {
            var panel = UIManager.GetInstance().Get<ChoicePanel>();

            if (panel == null || !panel.gameObject.activeSelf)
            {
                // 如果面板没开，先打开（路径由 UIManager 注册表解析）
                UIManager.GetInstance().Show<ChoicePanel>((p) =>
                {
                    p.AddChoice(text, commandText, chain);
                });
            }
            else
            {
                // 如果已经开了，直接加按钮
                panel.AddChoice(text, commandText, chain);
            }
        }

        /// <summary>多语言 choice 参数：choice(@loc:FULL_KEY|...)。翻译缺失时回退可读尾段。</summary>
        private static string ResolveLocalizedText(string text)
        {
            if (VNLocalizationService.IsEnabled() && !string.IsNullOrEmpty(text) &&
                text.TrimStart().StartsWith("@loc:", System.StringComparison.OrdinalIgnoreCase))
            {
                string fullKey = text.Trim().Substring("@loc:".Length).Trim();
                string scriptName = VNManager.GetInstance().GetCurrentScriptName();
                if (VNLocalizationService.TryGetByFullKey(scriptName, fullKey, out var localized) && !string.IsNullOrEmpty(localized))
                {
                    text = localized;
                }
                else
                {
                    // 翻译缺失：不要显示 @loc: 原样，改为可读 fallback
                    text = GetReadableTail(fullKey);
                }
            }
            return text;
        }

        private (string, string) ParseArgs(string args)
        {
            if (string.IsNullOrEmpty(args)) return ("", "");

            // 找到第一个竖线的位置
            int pipeIndex = args.IndexOf('|');

            string text = "";
            string cmd = "";

            if (pipeIndex == -1)
            {
                // 没有竖线，说明整个 args 都是文字，没有命令
                text = args.Trim();
            }
            else
            {
                // 有竖线，分割成两部分
                text = args.Substring(0, pipeIndex).Trim();
                cmd = args.Substring(pipeIndex + 1).Trim();
            }

            return (text, cmd);
        }

        private static string GetReadableTail(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey))
                return "";

            int idx = fullKey.LastIndexOf('.');
            if (idx >= 0 && idx < fullKey.Length - 1)
                return fullKey.Substring(idx + 1);

            return fullKey;
        }
    }
}