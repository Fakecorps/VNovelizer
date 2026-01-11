using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 视频播放命令
    /// 格式：playvideo(视频名, [可选]结束后的命令)
    /// 示例1：playvideo(op.mp4)
    /// 示例2：playvideo(ending.mp4, loadscript(Chapter2))
    /// </summary>
    public class PlayVideoCommand : VNCommand
    {
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
    }
}