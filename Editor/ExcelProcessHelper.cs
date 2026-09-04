using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
// System.Diagnostics 提供 Process，同时会引入 System.Diagnostics.Debug，
// 与 UnityEngine.Debug 冲突——用别名显式指定日志 API。
using Debug = UnityEngine.Debug;

/// <summary>
/// Excel 文件锁处理助手（Windows）。
///
/// <para>
/// <b>背景</b>：行演出编辑器保存时会把 CSV 的 Command 列镜像写回 xlsx（ClosedXML）。
/// 若用户正用 Excel / WPS 表格打开该文件，xlsx 被文件锁占用，ClosedXML 保存必然失败
/// （IOException）。本助手用 Windows Restart Manager API 精确定位「锁定指定文件」的
/// 进程——只针对锁定目标 xlsx 的电子表格进程（EXCEL / WPS / ET）操作，不误伤其他进程。
/// </para>
///
/// <para>
/// <b>流程</b>（由 <see cref="ExcelToCsvConverter"/> 的写回重试逻辑驱动）：
/// 写回失败 → 找到锁定文件的电子表格进程 → 关闭（Kill）→ 写回成功 → 用系统默认
/// 应用重新打开该文件，恢复用户视图。全程自动，不需要用户手动去任务栏关 Excel。
/// </para>
///
/// <para>
/// <b>风险提示</b>：Kill 电子表格进程会丢失其未保存的修改（其他打开的工作簿同样
/// 受影响）。这是「写回前自动关、写回后自动重开」需求的固有代价；Excel 自身的
/// 文档恢复机制可在下次启动时找回部分内容。
/// </para>
/// </summary>
public static class ExcelProcessHelper
{
    // ==================== Restart Manager P/Invoke ====================

    private const int ERROR_SUCCESS = 0;
    private const int ERROR_MORE_DATA = 234;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
        uint nApplications, [In] RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
        ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

    // ==================== 对外接口 ====================

    /// <summary>查找锁定指定文件的所有进程 PID（Restart Manager）。失败 / 非 Windows 返回空列表。</summary>
    public static List<int> FindLockingProcessIds(string filePath)
    {
        var result = new List<int>();
        if (Application.platform != RuntimePlatform.WindowsEditor) return result;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return result;

        // RmRegisterResources 要求规范化的完整路径；LocateExcelFile 返回的是
        // Assets/... 相对路径，不转换会导致锁进程定位失败。
        filePath = Path.GetFullPath(filePath);

        uint session = 0;
        try
        {
            var key = new StringBuilder(256);
            if (RmStartSession(out session, 0, key) != ERROR_SUCCESS) return result;

            string[] files = { filePath };
            if (RmRegisterResources(session, (uint)files.Length, files, 0, null, 0, null) != ERROR_SUCCESS)
                return result;

            uint needed = 0, count = 0, reasons = 0;
            int res = RmGetList(session, out needed, ref count, null, ref reasons);
            if (res == ERROR_MORE_DATA && needed > 0)
            {
                count = needed;
                var infos = new RM_PROCESS_INFO[count];
                res = RmGetList(session, out needed, ref count, infos, ref reasons);
                if (res == ERROR_SUCCESS)
                {
                    uint n = Math.Min(needed, count);
                    for (uint i = 0; i < n; i++)
                    {
                        if (infos[i].Process.dwProcessId > 0)
                            result.Add(infos[i].Process.dwProcessId);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CmdSync] Restart Manager 查询文件锁失败：{e.Message}");
        }
        finally
        {
            if (session != 0) RmEndSession(session);
        }
        return result;
    }

    /// <summary>
    /// 关闭锁定指定文件的电子表格进程（Excel / WPS 表格）。
    /// </summary>
    /// <param name="filePath">被锁定的 xlsx 绝对路径（相对路径亦可，内部规范化）。</param>
    /// <param name="closedAny">输出：是否真的关闭了至少一个电子表格进程（用于决定写回成功后是否重新打开）。</param>
    /// <returns>true = 已无电子表格进程锁定（进程已关闭 / 本就没有锁定进程），可以继续重试写入；false = 存在非电子表格进程锁定，不应继续。</returns>
    public static bool CloseLockingExcel(string filePath, out bool closedAny)
    {
        closedAny = false;

        var pids = FindLockingProcessIds(filePath);
        var excelProcs = new List<Process>();
        foreach (int pid in pids)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                if (p != null && IsSpreadsheetApp(p.ProcessName))
                    excelProcs.Add(p);
            }
            catch { /* 进程已退出 */ }
        }

        // 没有电子表格进程锁定（可能是别的瞬时原因）→ 让调用方直接重试写入
        if (excelProcs.Count == 0) return true;

        string fileName = Path.GetFileName(filePath);
        Debug.LogWarning($"[CmdSync] 电子表格程序正占用 {fileName}，自动关闭以完成镜像写回，写回后会自动重新打开。");

        foreach (var p in excelProcs)
        {
            try
            {
                p.Kill();
                p.WaitForExit(8000);
                closedAny = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CmdSync] 关闭电子表格进程（PID {p.Id}）失败：{e.Message}");
            }
        }
        return true;
    }

    /// <summary>
    /// 进程名是否为电子表格程序：Microsoft Excel（EXCEL）或 WPS 表格（WPS / 老版 ET）。
    /// Restart Manager 已保证这些进程确实锁定了目标 xlsx，匹配只是分类过滤，
    /// 不会误杀其他程序。
    /// </summary>
    private static bool IsSpreadsheetApp(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        string n = processName.ToLowerInvariant();
        return n.Contains("excel") || n.Contains("wps") || n == "et";
    }

    /// <summary>用系统默认应用重新打开文件（写回完成后恢复用户的 Excel 视图）。</summary>
    public static void ReopenInDefaultApp(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CmdSync] 重新打开 {Path.GetFileName(filePath)} 失败：{e.Message}");
        }
    }
}
