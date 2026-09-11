using UnityEngine;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 演员工厂：剧场层创建渲染对象的可插拔入口。
    /// 默认实现只生产 MeshActor（静态立绘/背景）；外部扩展（如插件内 Live2D 支持）
    /// 可在运行时替换 <see cref="TheaterManager.ActorFactory"/>，按 appearance 中
    /// 携带的 profile 类型返回对应动态演员（L2DActor 等）。
    /// 核心程序集不感知任何外部类型——工厂接口只出现核心自有类型。
    /// </summary>
    public interface IActorFactory
    {
        /// <summary>
        /// 创建演员。
        /// </summary>
        /// <param name="actorId">演员 ID（槽位 posCode / 自由角色 posID / 背景 ID）</param>
        /// <param name="kind">演员类别</param>
        /// <param name="appearance">即将承载的外观；<c>EnsureActor</c> 的无外观路径传 null，
        /// 换装重建路径（外观与现有演员实现不匹配）总是传入具体外观供工厂决策。</param>
        /// <param name="parent">剧场 Actors 根节点</param>
        IActor Create(string actorId, ActorKind kind, ActorAppearance appearance, Transform parent);
    }

    /// <summary>默认工厂：恒产 MeshActor（历史行为）</summary>
    public class DefaultActorFactory : IActorFactory
    {
        public IActor Create(string actorId, ActorKind kind, ActorAppearance appearance, Transform parent)
        {
            return new MeshActor(actorId, kind, parent);
        }
    }
}
