namespace VNovelizer.Core.Utils
{
    public static class VNCommandBridge
    {
        /// <summary>
        /// 请求：当前行的所有命令执行完成后，自动进入下一行。
        /// </summary>
        public static void AdvanceAfterCommands()
        {
            VNManager.GetInstance().RequestAdvanceAfterCommands();
        }

        /// <summary>
        /// 清除这个请求。
        /// </summary>
        public static void ClearAdvanceAfterCommands()
        {
            VNManager.GetInstance().ClearAdvanceAfterCommandsRequest();
        }
    }
}