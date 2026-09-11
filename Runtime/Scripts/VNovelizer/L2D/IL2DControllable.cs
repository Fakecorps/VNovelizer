using System.Collections;

namespace VNovelizer.L2D
{
    /// <summary>
    /// Live2D 演员的控制接口——命令层（l2dmotion / l2dexp）的唯一控制面。
    /// 由 <see cref="L2DActor"/> 实现。命令层与 L2DActor 之间只有本接口，
    /// 官方 SDK 组件（CubismMotionController / CubismExpressionController）细节封死在实现侧。
    /// </summary>
    public interface IL2DControllable
    {
        /// <summary>模型是否已完成初始化（就绪前调用会入队等待，见 L2DActor 实现）</summary>
        bool IsReady { get; }

        /// <summary>
        /// 播放动作（官方 CubismMotionController 多层 + 优先级语义）：
        /// Idle 垫底循环、Motion 类动作同层 PriorityForce 替换、播完自动回 Idle。
        /// 播放失败（ID 查无 / clip 缺失 / 模型未就绪）返回 false 并给出原因。
        /// </summary>
        bool TryPlayMotion(string motionID, out string error);

        /// <summary>停止动作层当前动作回 Idle 待机（保留 Idle 层循环）</summary>
        bool TryStopMotion();

        /// <summary>
        /// 切换表情（官方 CubismExpressionController.CurrentExpressionIndex 语义）：
        /// 保持型——应用并持续挂在模型上，直到下一次切换或清除。
        /// </summary>
        bool TrySetExpression(string expID, float? fadeOverride, out string error);

        /// <summary>清除表情（回无表情状态，官方自带淡出）</summary>
        bool TryClearExpression();

        /// <summary>
        /// 等待动作层当前动作播放结束（供 l2dmotion 的 wait 阻塞语义）。
        /// 循环动作永不结束——调用方需自行判断。
        /// </summary>
        IEnumerator WaitMotionFinished();
    }
}
