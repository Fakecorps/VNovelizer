using System.Collections;
using UnityEngine;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 演员抽象：剧场层渲染实现的唯一契约。
    /// 接口只约定演出语义（外观/变换/可见性/转场），不出现任何渲染类型
    /// （Image/RectTransform/MeshRenderer 均不可见）——命令层经由此接口驱动一切舞台内容。
    /// 坐标统一使用剧本像素语义（1920x1080 参考，原点画面中心），实现层负责换算。
    /// </summary>
    public interface IActor
    {
        string ActorId { get; }              // "MainBackground" / "Amy" / 槽位 ID
        ActorKind Kind { get; }              // Background / Character / Effect
        bool IsValid { get; }                // 渲染对象是否存活

        // ---- 外观 ----
        void SetAppearance(ActorAppearance appearance);

        // ---- 变换 ----
        void SetPosition(Vector2 posPx);     // 剧本像素语义
        void SetScale(float scale);
        void SetFlip(bool flipped);          // scaleX = -1
        void SetDepth(int zOrder);           // 前后层级

        // ---- 可见性 ----
        void SetAlpha(float alpha);
        void SetVisible(bool visible);

        // ---- 转场（外观切换的演出化包装；实现可先退化为立即切换）----
        void Transition(ActorAppearance next, string transitionName,
                        float duration, float[] parameters);

        // ---- 异步动画（命令系统驱动，协程风格与 VNCommand 一致）----
        IEnumerator FadeAsync(float targetAlpha, float duration);
        IEnumerator MoveAsync(Vector2 targetPx, float duration);
        void Interrupt();                    // 跳过/中断时瞬间到终态
    }
}
