using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 自动存档运行时配置（默认值兜底；由 SaveLoadPanel 预制体上的 Inspector 字段推送覆盖）
/// </summary>
public class AutoSaveConfigData
{
    /// <summary>是否已从 SaveLoadPanel 预制体/实例应用过配置（避免重复加载）</summary>
    public bool LoadedFromPanel = false;
    public bool Enabled = true;
    public int EveryLines = 10;
    public bool OnChoice = true;
    public bool OnScriptSwitch = true;
}

/// <summary>
/// 存档管理器
/// </summary>
public class SaveManager : BaseManager<SaveManager>
{
    private const string SAVE_DATA_DIR = "SaveData";
    private const string SCREENSHOT_DIR = "Screenshots";
    private const int MAX_SAVE_SLOTS = 60;

    // 自动存档（独立于 60 个手动槽位之外的专用文件）
    private const string AUTO_SAVE_FILE = "save_auto.json";
    private const string AUTO_SCREENSHOT_FILE = "screenshot_auto.png";

    /// <summary>自动存档配置（运行时由 SaveLoadPanel 推送；面板未加载时使用默认值）</summary>
    public static readonly AutoSaveConfigData AutoSaveConfig = new AutoSaveConfigData();

    /// <summary>
    /// 截图缩略图最长边像素（保存时下采样；由 SaveLoadPanel.Inspector 推送，默认 480）。
    /// 全分辨率截图文件大、解码慢，是存档面板卡顿的主因。
    /// </summary>
    public static int ThumbnailMaxSize = 480;

    private Texture2D _tempScreenshot;
    public void Init()
    {
        // 创建存档目录
        string saveDir = Path.Combine(Application.persistentDataPath, SAVE_DATA_DIR);
        if (!Directory.Exists(saveDir))
        {
            Directory.CreateDirectory(saveDir);
        }
        
        // 创建截图目录
        string screenshotDir = Path.Combine(Application.persistentDataPath, SCREENSHOT_DIR);
        if (!Directory.Exists(screenshotDir))
        {
            Directory.CreateDirectory(screenshotDir);
        }
    }
    
    /// <summary>
    /// 保存游戏
    /// </summary>
    /// <param name="slotIndex">存档槽位</param>
    /// <param name="saveData">存档数据</param>
    public void SaveGame(int slotIndex, SaveData saveData)
    {
        if (slotIndex < 0 || slotIndex >= MAX_SAVE_SLOTS)
            return;

        if (WriteSaveData(GetSaveFilePath(slotIndex), saveData))
        {
            EventCenter.GetInstance().EventTrigger("GameSaved", slotIndex);
        }
    }

    /// <summary>
    /// 保存自动存档（独立文件，不占用 60 个手动槽位）
    /// </summary>
    public void SaveAutoGame(SaveData saveData)
    {
        if (saveData == null) return;

        if (WriteSaveData(GetAutoSaveFilePath(), saveData))
        {
            EventCenter.GetInstance().EventTrigger("GameSaved", -1);
        }
    }

    /// <summary>
    /// 通用的存档写入：序列化 → 可选 AES 加密 → 落盘
    /// </summary>
    private bool WriteSaveData(string savePath, SaveData saveData)
    {
        string dir = Path.GetDirectoryName(savePath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json;
        try
        {
            json = LitJson.JsonMapper.ToJson(saveData);
            Debug.Log($"[SaveManager] 序列化成功，JSON长度: {json.Length}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveManager] 序列化失败: {e.Message}\n{e.StackTrace}");
            return false;
        }

        string contentToWrite = json;

        if (VNProjectConfig.Instance.UseAES)
        {
            try
            {
                contentToWrite = AESUtil.Encrypt(json);
                Debug.Log($"[SaveManager] 加密成功");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveManager] 加密失败: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        try
        {
            File.WriteAllText(savePath, contentToWrite);
            Debug.Log($"[SaveManager] 存档保存成功: {savePath}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveManager] 文件写入失败: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// 加载游戏
    /// </summary>
    /// <param name="slotIndex">存档槽位</param>
    /// <returns>存档数据</returns>
    public SaveData LoadGame(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= MAX_SAVE_SLOTS)
            return null;

        return ReadSaveData(GetSaveFilePath(slotIndex));
    }

    /// <summary>
    /// 加载自动存档（独立文件）
    /// </summary>
    public SaveData LoadAutoGame()
    {
        return ReadSaveData(GetAutoSaveFilePath());
    }

    /// <summary>
    /// 通用的存档读取：读文件 → 可选 AES 解密 → 反序列化（含兼容重试）
    /// </summary>
    private SaveData ReadSaveData(string savePath)
    {
        if (File.Exists(savePath))
        {
            string fileContent = File.ReadAllText(savePath);
            string json = fileContent;
            if (VNProjectConfig.Instance.UseAES)
            {
                // 如果开启了加密，先尝试解密
                string decrypted = AESUtil.Decrypt(fileContent);
                if (!string.IsNullOrEmpty(decrypted))
                {
                    json = decrypted; // 解密成功
                }
                else
                {
                    Debug.LogWarning($"[SaveManager] 存档 {Path.GetFileName(savePath)} 解密失败，尝试按明文读取。");
                }
            }
            try
            {
                return LitJson.JsonMapper.ToObject<SaveData>(json);
            }
            catch
            {
                // 如果解析失败，说明可能是加密的但没解开，或者文件坏了
                // 这里可以再尝试一次 AES Decrypt (防止 Config 没开但读了加密档)
                string retryDecrypt = AESUtil.Decrypt(fileContent);
                if (!string.IsNullOrEmpty(retryDecrypt))
                    return LitJson.JsonMapper.ToObject<SaveData>(retryDecrypt);

                Debug.LogError($"存档 {Path.GetFileName(savePath)} 损坏或格式无法识别。");
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// 检查自动存档是否存在
    /// </summary>
    public bool IsAutoSaveExists()
    {
        return File.Exists(GetAutoSaveFilePath());
    }

    /// <summary>
    /// 删除自动存档（存档文件 + 截图）
    /// </summary>
    public void DeleteAutoSave()
    {
        string savePath = GetAutoSaveFilePath();
        if (File.Exists(savePath))
        {
            File.Delete(savePath);
        }

        string screenshotPath = GetAutoScreenshotFilePath();
        if (File.Exists(screenshotPath))
        {
            File.Delete(screenshotPath);
        }

        EventCenter.GetInstance().EventTrigger("SaveDeleted", -1);
    }

    /// <summary>
    /// 应用自动存档配置（由 SaveLoadPanel 的 Inspector 字段推送）
    /// </summary>
    public static void ApplyAutoSaveConfig(bool enabled, int everyLines, bool onChoice, bool onScriptSwitch)
    {
        AutoSaveConfig.Enabled = enabled;
        AutoSaveConfig.EveryLines = Mathf.Max(1, everyLines);
        AutoSaveConfig.OnChoice = onChoice;
        AutoSaveConfig.OnScriptSwitch = onScriptSwitch;
        AutoSaveConfig.LoadedFromPanel = true;
    }
    
    /// <summary>
    /// 保存截图
    /// </summary>
    /// <param name="slotIndex">存档槽位</param>
    /// <param name="texture">截图纹理</param>
    /// <returns>截图路径</returns>
    public string SaveScreenshot(int slotIndex, Texture2D texture)
    {
        string screenshotPath = GetScreenshotFilePath(slotIndex);

        // 【新增】双保险：确保目录存在
        string dir = Path.GetDirectoryName(screenshotPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        byte[] bytes = texture.EncodeToPNG();
        File.WriteAllBytes(screenshotPath, bytes);
        return screenshotPath;
    }
    
    /// <summary>
    /// 获取截图
    /// </summary>
    /// <param name="slotIndex">存档槽位</param>
    /// <returns>截图Texture2D</returns>
    public Texture2D GetScreenshot(int slotIndex)
    {
        string screenshotPath = GetScreenshotFilePath(slotIndex);
        if (File.Exists(screenshotPath))
        {
            byte[] bytes = File.ReadAllBytes(screenshotPath);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(bytes);
            return texture;
        }
        return null;
    }
    
    /// <summary>
    /// 检查存档是否存在
    /// </summary>
    /// <param name="slotIndex">存档槽位</param>
    /// <returns>是否存在</returns>
    public bool IsSaveExists(int slotIndex)
    {
        string savePath = GetSaveFilePath(slotIndex);
        return File.Exists(savePath);
    }
    
    /// <summary>
    /// 删除存档
    /// </summary>
    /// <param name="slotIndex">存档槽位</param>
    public void DeleteSave(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= MAX_SAVE_SLOTS)
            return;
        
        // 删除存档文件
        string savePath = GetSaveFilePath(slotIndex);
        if (File.Exists(savePath))
        {
            File.Delete(savePath);
        }
        
        // 删除截图
        string screenshotPath = GetScreenshotFilePath(slotIndex);
        if (File.Exists(screenshotPath))
        {
            File.Delete(screenshotPath);
        }
        
        EventCenter.GetInstance().EventTrigger("SaveDeleted", slotIndex);
    }
    
    /// <summary>
    /// 获取所有存档数据
    /// </summary>
    /// <returns>存档数据列表</returns>
    public List<SaveData> GetAllSaveData()
    {
        List<SaveData> saveDatas = new List<SaveData>();
        
        for (int i = 0; i < MAX_SAVE_SLOTS; i++)
        {
            SaveData saveData = LoadGame(i);
            if (saveData != null)
            {
                saveDatas.Add(saveData);
            }
        }
        
        return saveDatas;
    }
    
    /// <summary>
    /// 获取存档文件路径
    /// </summary>
    /// <param name="slotIndex">存档槽位</param>
    /// <returns>文件路径</returns>
    private string GetSaveFilePath(int slotIndex)
    {
        return Path.Combine(Application.persistentDataPath, SAVE_DATA_DIR, "save_" + slotIndex + ".json");
    }
    
    /// <summary>
    /// 获取截图文件路径
    /// </summary>
    /// <param name="slotIndex">存档槽位</param>
    /// <returns>文件路径</returns>
    private string GetScreenshotFilePath(int slotIndex)
    {
        return Path.Combine(Application.persistentDataPath, SCREENSHOT_DIR, "screenshot_" + slotIndex + ".png");
    }

    /// <summary>
    /// 获取自动存档文件路径
    /// </summary>
    private string GetAutoSaveFilePath()
    {
        return Path.Combine(Application.persistentDataPath, SAVE_DATA_DIR, AUTO_SAVE_FILE);
    }

    /// <summary>
    /// 获取自动存档截图路径
    /// </summary>
    private string GetAutoScreenshotFilePath()
    {
        return Path.Combine(Application.persistentDataPath, SCREENSHOT_DIR, AUTO_SCREENSHOT_FILE);
    }
    
    /// <summary>
    /// 获取最大存档槽位数
    /// </summary>
    /// <returns>最大存档槽位数</returns>
    public int GetMaxSaveSlots()
    {
        return MAX_SAVE_SLOTS;
    }

    public void CaptureCurrentScreen()
    {
        if (_tempScreenshot != null) Object.Destroy(_tempScreenshot);

        // 选相机策略：遍历所有启用的相机，挑"能渲染 Default 层（背景/角色）+ depth 最高"的一个。
        // 这样能避开两种坑：
        //   ① 场景里的"遗留 Main Camera"被 SceneCameraManager 剔除 Default 层后 cullingMask=0，不再被选中；
        //   ② 带 MainCamera tag 但不是 Theatre 相机的情况（Camera.main 单一 tag 匹配不可靠）。
        // 用 Camera.Render 路径而不是 ScreenCapture，是为了**只截 3D 场景而不含 ScreenSpaceOverlay UI**
        // （存档缩略图应当是"当时游戏画面"，而非"屏上看到的一切"，否则对话框/控制按钮会污染缩略图）。
        Camera cam = PickSceneCameraForCapture();
        if (cam == null)
        {
            // 兜底：场景里没有渲染 Default 层的相机，截整屏（含 UI）也比蓝屏强
            _tempScreenshot = ScreenCapture.CaptureScreenshotAsTexture();
            return;
        }

        int width = Screen.width;
        int height = Screen.height;
        RenderTexture rt = new RenderTexture(width, height, 24);

        RenderTexture oldTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = oldTarget;

        RenderTexture.active = rt;
        _tempScreenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        _tempScreenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        _tempScreenshot.Apply();

        RenderTexture.active = null;
        Object.Destroy(rt);
    }

    /// <summary>
    /// 选取用于存档截图的相机：cullingMask 含 Default 层、已启用、按 depth 降序的第一个。
    /// 避免命中被 SceneCameraManager 自动剔除 Default 层的"遗留 Main Camera"（cullingMask=0）。
    /// </summary>
    private static Camera PickSceneCameraForCapture()
    {
        Camera best = null;
        var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera c = cameras[i];
            if (c == null || !c.isActiveAndEnabled) continue;
            if ((c.cullingMask & 1) == 0) continue; // bit 0 = Default 层（背景/角色）
            if (best == null || c.depth > best.depth) best = c;
        }
        return best;
    }

    public string SaveCachedScreenshot(int slotIndex)
    {
        return WriteScreenshotThumbnail(GetScreenshotFilePath(slotIndex));
    }

    /// <summary>
    /// 将缓存截图写入自动存档专用截图文件
    /// </summary>
    public string SaveCachedAutoScreenshot()
    {
        return WriteScreenshotThumbnail(GetAutoScreenshotFilePath());
    }

    /// <summary>
    /// 将缓存截图下采样为缩略图后写入文件：
    /// 全分辨率 PNG（如 1920×1080，单张数 MB）会显著拖慢存档面板的批量加载，
    /// 缩略图（默认最长边 480px，几十 KB）可实现近乎瞬时的槽位预览。
    /// </summary>
    private string WriteScreenshotThumbnail(string screenshotPath)
    {
        if (_tempScreenshot == null)
        {
            Debug.LogWarning("没有缓存的截图，尝试重新截取（可能会包含UI）");
            CaptureCurrentScreen();
        }

        string dir = Path.GetDirectoryName(screenshotPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        Texture2D thumb = CreateThumbnail(_tempScreenshot, ThumbnailMaxSize);
        Texture2D target = thumb != null ? thumb : _tempScreenshot; // 源图比目标更小时直接用原图

        byte[] bytes = target.EncodeToPNG();
        File.WriteAllBytes(screenshotPath, bytes);

        if (thumb != null) Object.Destroy(thumb); // 临时缩略图纹理，编码完即释放
        return screenshotPath;
    }

    /// <summary>
    /// 生成缩略图：保持宽高比缩放至最长边 ≤ maxSize。
    /// 源图尺寸不超过 maxSize 时返回 null（无需缩放）。
    /// </summary>
    public static Texture2D CreateThumbnail(Texture2D source, int maxSize)
    {
        if (source == null || maxSize <= 0 || source.width <= 0 || source.height <= 0)
            return null;

        float scale = Mathf.Min((float)maxSize / source.width, (float)maxSize / source.height);
        if (scale >= 1f) return null; // 源图已足够小

        int w = Mathf.Max(2, Mathf.RoundToInt(source.width * scale));
        int h = Mathf.Max(2, Mathf.RoundToInt(source.height * scale));

        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, rt);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D thumb = new Texture2D(w, h, TextureFormat.RGB24, false);
        thumb.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        thumb.Apply(false, false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return thumb;
    }
}