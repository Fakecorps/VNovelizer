using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 视频播放命令
    /// 格式：playvideo(视频名, [可选]结束后的命令)
    /// 示例1：playvideo(op.mp4)
    /// 示例2：playvideo(ending.mp4, loadscript(Chapter2))
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Audio,
        "全屏播放视频（播放期间阻断交互；结束后可接命令链）")]
    public class PlayVideoCommand : VNCommand
    {
        [VNParam(0, "video", VNParamType.String,
            Description = "视频资源名（视频目录下，含扩展名如 op.mp4）")]
        [VNParam(1, "nextCommand", VNParamType.String, Optional = true,
            Description = "视频结束后执行的命令链，如 loadscript(Chapter2)（可选）")]
        public override string CommandName { get { return "playvideo"; } }

        private bool isFinished = false;

        public override bool Execute(string args)
        {
            MonoManager.GetInstance().StartCoroutine(ExecuteAsync(args));
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            // 1. 解析参数
            // 这里我们只需要找到第一个逗号，把后面剩下的所有内容都当作 Command String
            int commaIndex = args.IndexOf(',');

            string videoName = "";
            string nextCommand = "";

            if (commaIndex == -1)
            {
                // 只有一个参数 (视频名)
                videoName = args.Trim();
            }
            else
            {
                // 两个参数 (视频名, 命令)
                videoName = args.Substring(0, commaIndex).Trim();
                nextCommand = args.Substring(commaIndex + 1).Trim();
            }

            // 2. 播放视频
            isFinished = false;
            VNAPI.PlayVideo(videoName, () =>
            {
                isFinished = true;
            });

            // 3. 等待播放结束
            while (!isFinished)
            {
                yield return null;
            }

            // 4. 视频结束后，执行后续命令
            if (!string.IsNullOrEmpty(nextCommand))
            {
                Debug.Log($"[PlayVideo] 视频结束，执行后续命令: {nextCommand}");
                // 使用 CommandManager 执行
                VNAPI.ExecuteCommand(nextCommand);

            }
        }

        /// <summary>
        /// 中断视频播放（玩家点击跳过）：
        /// 停止 VideoModel 并销毁，不触发"结束后的命令"（外部中断不视为自然播完）
        /// </summary>
        public override void Interrupt()
        {
            VNAPI.StopVideo();
            isFinished = true; // 让可能仍在等待的 ExecuteAsync 循环（如同步入口路径）退出
        }
    }
}