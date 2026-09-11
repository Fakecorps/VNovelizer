using UnityEngine;
using VNovelizer.Core.Theater;

namespace VNovelizer.L2D
{
    /// <summary>
    /// Live2D 支持的运行时入口：程序集加载后替换剧场演员工厂。
    /// 命令（l2dmotion / l2dexp）由 CommandManager 反射自动注册，无需显式登记。
    /// 本程序集受 VN_L2D 符号门控（L2DSupportActivator 管理）：未装 SDK 时整程序集不编译。
    /// </summary>
    public static class L2DBridge
    {
        private static bool _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            TheaterManager.ActorFactory = new L2DActorFactory();
            Debug.Log("[L2DBridge] Live2D 演员工厂已安装（TheaterManager.ActorFactory = L2DActorFactory）");
        }
    }

    /// <summary>
    /// 演员工厂：动态立绘外观（appearance.profile 非 SpriteBased）→ L2DActor；
    /// 其余（背景/特效/静态立绘）→ 默认 MeshActor，保证单槽位立绘 ↔ Live2D 无缝切换。
    /// </summary>
    public class L2DActorFactory : IActorFactory
    {
        public IActor Create(string actorId, ActorKind kind, ActorAppearance appearance, Transform parent)
        {
            if (appearance != null && appearance.profile != null && !appearance.profile.IsSpriteBased)
                return new L2DActor(actorId, kind, appearance, parent);

            return new MeshActor(actorId, kind, parent);
        }
    }
}
