using System.Collections;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 播放音效命令
    /// 格式：playsfx(名称, 次数)
    /// 示例1：playsfx(click, 1) -> 播放一次点击音效
    /// 示例2：playsfx(click, 3) -> 播放3次点击音效
    /// 示例3：playsfx(click) -> 播放一次（默认）
    /// </summary>
    public class PlaySFXCommand : VNCommand
    {
        public override string CommandName { get { return "playsfx"; } }

        public override bool Execute(string args)
        {
            // 音效播放是异步的，需要协程支持
            MonoManager.GetInstance().StartCoroutine(ExecuteAsync(args));
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[PlaySFX] 参数不能为空");
                yield break;
            }

            // 解析参数：名称, 次数
            string[] parts = args.Split(',');
            string sfxName = parts[0].Trim();
            int times = 1; // 默认播放1次

            if (parts.Length >= 2)
            {
                if (!int.TryParse(parts[1].Trim(), out times))
                {
                    Debug.LogWarning($"[PlaySFX] 次数参数解析失败，使用默认值1。参数: {parts[1]}");
                    times = 1;
                }
            }

            if (times <= 0)
            {
                Debug.LogWarning($"[PlaySFX] 播放次数必须大于0，当前值: {times}");
                yield break;
            }

            Debug.Log($"[PlaySFX] 准备播放音效: {sfxName}, 次数: {times}");

            // 播放指定次数的音效
            for (int i = 0; i < times; i++)
            {
                bool sourceReady = false;
                AudioSource currentSource = null;
                AudioClip loadedClip = null;

                // 播放音效（不循环，因为我们要手动控制次数）
                MusicManager.GetInstance().PlaySFX(sfxName, false, (source) =>
                {
                    if (source != null && source.clip != null)
                    {
                        sourceReady = true;
                        currentSource = source;
                        loadedClip = source.clip;
                    }
                });

                // 等待音效资源加载完成并开始播放（最多等待2秒）
                float waitTime = 0f;
                while (!sourceReady && waitTime < 2f)
                {
                    yield return null;
                    waitTime += Time.deltaTime;
                }

                if (currentSource == null || loadedClip == null)
                {
                    Debug.LogWarning($"[PlaySFX] 音效 {sfxName} 加载失败或资源不存在");
                    // 如果加载失败，跳过本次播放，继续下一次
                    if (i < times - 1)
                    {
                        yield return new WaitForSeconds(0.1f);
                    }
                    continue;
                }

                // 等待当前音效播放完成
                // 使用AudioClip.length作为最大等待时间，防止无限等待
                float clipLength = loadedClip.length;
                float elapsedTime = 0f;
                
                while (currentSource != null && currentSource.isPlaying && elapsedTime < clipLength + 0.5f)
                {
                    yield return null;
                    elapsedTime += Time.deltaTime;
                }

                // 如果不是最后一次，等待一小段时间再播放下一次（避免连续播放时重叠）
                if (i < times - 1)
                {
                    yield return new WaitForSeconds(0.05f);
                }
            }

            Debug.Log($"[PlaySFX] 音效 {sfxName} 播放完成，共播放 {times} 次");
        }

        public override void Simulate(string args)
        {
            Debug.Log($"[PlaySFX.Simulate] 模拟播放音效: {args}");
        }
    }
}

