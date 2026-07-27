using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using VNovelizer.Core.API;
using VNovelizer.Core.Compat;

namespace VNovelizer.Core.Commands
{
    public class ExitCommand : VNCommand
    {
        public override string CommandName { get { return "exit"; } }

        public override bool Execute(string args)
        {
            if (!string.IsNullOrEmpty(args))
            {
                Debug.LogError("hide命令参数应为空");
                return false;
            }
            
            if (GameStateManager.GetInstance() != null && 
                GameStateManager.GetInstance().CurrentState == GameState.Pause)
            {
                GameStateManager.GetInstance().RestoreState();
                AnimationCompat.StopAll();
                VNAPI.ClearAllEffects();
                PoolManager.GetInstance().Clear();
            }
        

            // 加载主菜单场景
            SceneManager.LoadScene("VNMainMenu");
        
            Debug.Log("[PausePanel] 返回主菜单场景");
            return true;
        }
    }
}