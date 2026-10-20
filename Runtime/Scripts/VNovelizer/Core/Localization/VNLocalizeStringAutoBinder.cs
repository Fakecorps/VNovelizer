#if VN_LOCALIZATION
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;

namespace VNovelizer.Core.Localization
{
    /// <summary>
    /// 【Fix-L10N-AutoBind】LocalizeStringEvent 运行时自愈绑定器。
    ///
    /// <para>背景（Localization 1.5.2 源码验证）：<see cref="LocalizeStringEvent"/> 依赖
    /// OnUpdateString 持久化事件（序列化字段 m_UpdateString.m_Calls）把译文写进 TMP；
    /// 官方 1.5.2 的 Inspector 没有任何自动绑定逻辑，程序化 <c>AddComponent</c> 后该列表恒为空
    /// → 表加载成功、StringChanged 正常触发，但 OnUpdateString.Invoke 空转，TMP 永不更新
    ///（表现为"切换语言成功、诊断条目正常，界面文字不变"）。</para>
    ///
    /// <para>装配器（VNUiLocalizationAssembler）修复后新装配的组件已带持久化绑定；
    /// 本绑定器兜底存量预制体与用户自建覆写副本：面板实例化后对缺失绑定的组件补一个
    /// 非持久运行时监听（等效 OnUpdateString → set_text），无需重跑装配即可生效。</para>
    ///
    /// <para>幂等性：已有持久化绑定的组件（装配器修复版产物 / 用户手动绑定）直接跳过。</para>
    /// </summary>
    public static class VNLocalizeStringAutoBinder
    {
        // 已补绑组件登记（弱表：组件销毁后条目自动回收，不阻止 GC、无跨场景泄漏）
        private static readonly ConditionalWeakTable<LocalizeStringEvent, object> Healed =
            new ConditionalWeakTable<LocalizeStringEvent, object>();
        private static readonly object Marker = new object();

        /// <summary>
        /// 扫描面板内全部 LocalizeStringEvent：无持久化监听且同物体挂 TMP_Text 时，
        /// 补一个运行时监听（target.SetText(value)）并强制重推一次译文。
        /// 在 UIManager 实例化面板后调用（所有面板统一出口，覆写副本/包内模板全覆盖）。
        /// </summary>
        public static void HealPanel(GameObject root)
        {
            if (root == null)
                return;

            var events = root.GetComponentsInChildren<LocalizeStringEvent>(true);
            for (int i = 0; i < events.Length; i++)
            {
                var evt = events[i];
                if (evt == null || evt.OnUpdateString == null)
                    continue;

                // 已有持久化绑定 → 官方链路完整，不动
                if (evt.OnUpdateString.GetPersistentEventCount() > 0)
                    continue;

                // 已自愈过（ConditionalWeakTable.Add 对重复 key 抛异常，且防止重复加监听）
                if (Healed.TryGetValue(evt, out _))
                    continue;

                var tmp = evt.GetComponent<TMP_Text>();
                if (tmp == null)
                    continue;

                var target = tmp; // 闭包捕获，防循环变量陷阱
                evt.OnUpdateString.AddListener(value => target.SetText(value));
                Healed.Add(evt, Marker);

                // 组件激活且表已加载时，官方组件在 OnEnable 已推过一次（本监听未赶上）→ 强制重推；
                // 组件未激活时为 no-op（激活后 OnEnable → StringChanged 会自动拉取并写入）。
                evt.RefreshString();
            }
        }

        /// <summary>该组件是否已由本绑定器补过运行时监听（诊断面板用：区分"持久绑定/自愈绑定/未绑定"三态）。</summary>
        public static bool IsHealed(LocalizeStringEvent evt)
        {
            return evt != null && Healed.TryGetValue(evt, out _);
        }
    }
}
#endif
