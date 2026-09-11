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
        [VNParam(0, "video", VNParamType.VideoName,
            Description = "视频资源名（视频目录下，含扩展名如 op.mp4）")]
        [VNParam(1, "nextCommand", VNParamType.String, Optional = true,
            Description = "视频结束后执行的命令链，如 loadscript(Chapter2)（可选）")]
        public override string CommandName { get { return "playvideo"; } }

        private bool isFinished = false;

        // 【Fix-9】中断标志：区分"自然播完"与"外部中断"——
        // 外部中断不视为自然播完，绝不执行"结束后的命令"（如 loadscript 流程命令）
        private bool _interrupted = false;

        public override bool Execute(string args)
        {
            // 【Fix-8】快进/Simulate 语境：同步路径不应启动未登记的异步协程
            // （否则快进时视频会真的全屏播放）。跳过视频播放，不触发后续命令。
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            // 【Fix-9】每次启动复位中断标志
            _interrupted = false;

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
            // 【Fix-9】加超时兜底：视频 Prepare 失败但未触发 error 回调时，
            // 命令链不再永久挂起（10 分钟上限远大于任何正常视频时长）。
            int frames = 0;
            while (!isFinished && frames++ < 60 * 60 * 10)
            {
                yield return null;
            }

            // 4. 视频自然结束后，执行后续命令；被中断则绝不执行
            if (!_interrupted && !string.IsNullOrEmpty(nextCommand))
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
            // 【Fix-9】先置中断标志：等待循环退出后不会执行"结束后的命令"
            _interrupted = true;
            VNAPI.StopVideo();
            isFinished = true; // 让可能仍在等待的 ExecuteAsync 循环（如同步入口路径）退出
        }
    }
}