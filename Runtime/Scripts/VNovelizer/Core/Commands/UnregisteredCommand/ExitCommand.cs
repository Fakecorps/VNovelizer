using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 返回主菜单。Excel 写法：exit()
    ///
    /// 场景无关语义（与 PausePanel 的"退出到主菜单"完全一致）：
    /// 主菜单是面板而非场景，不切换场景——旧实现 LoadScene("VNMainMenu")
    /// 依赖已被删除的内置场景，在重构后的引擎里必然报"场景未加入 Build Settings"。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Flow,
        "返回主菜单（面板切换不加载场景；仅可置于链尾）")]
    public class ExitCommand : VNCommand
    {
        public override string CommandName { get { return "exit"; } }

        public override bool Execute(string args)
        {
            if (!string.IsNullOrEmpty(args))
            {
                Debug.LogError("[ExitCommand] exit 命令参数应为空");
                return false;
            }

            VNManager.GetInstance().ReturnToMainMenu();
            return true;
        }
    }
}
