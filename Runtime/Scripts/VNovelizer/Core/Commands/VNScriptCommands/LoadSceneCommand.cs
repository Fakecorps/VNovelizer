using UnityEngine;
using UnityEngine.SceneManagement;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 加载指定场景。
    /// Excel 写法：loadscene(SceneName)
    /// </summary>
    public class LoadSceneCommand : VNCommand
    {
        public override string CommandName { get { return "loadscene"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Debug.LogError("[LoadSceneCommand] 参数不能为空，请使用 loadscene(场景名称)");
                return false;
            }

            string sceneName = args.Trim();
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[LoadSceneCommand] 场景未加入 Build Settings 或名称错误: {sceneName}");
                return false;
            }

            SceneManager.LoadScene(sceneName);
            Debug.Log($"[LoadSceneCommand] 加载场景: {sceneName}");
            return true;
        }
    }
}
