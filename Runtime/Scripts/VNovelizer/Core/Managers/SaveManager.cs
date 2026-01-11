using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 存档管理器
/// </summary>
public class SaveManager : BaseManager<SaveManager>
{
    private const string SAVE_DATA_DIR = "SaveData";
    private const string SCREENSHOT_DIR = "Screenshots";
    private const int MAX_SAVE_SLOTS = 60;

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

        string savePath = GetSaveFilePath(slotIndex);


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
            return;
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
                return;
            }
        }

        try
        {
            File.WriteAllText(savePath, contentToWrite);
            Debug.Log($"[SaveManager] 存档保存成功: {savePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveManager] 文件写入失败: {e.Message}\n{e.StackTrace}");
            return;
        }

        EventCenter.GetInstance().EventTrigger("GameSaved", slotIndex);
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
        
        string savePath = GetSaveFilePath(slotIndex);
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
                    Debug.LogWarning($"[SaveManager] 存档 {slotIndex} 解密失败，尝试按明文读取。");
                }
            }
            else
            { 
            
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

                Debug.LogError($"存档 {slotIndex} 损坏或格式无法识别。");
                return null;
            }
        }
        return null;
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

        // 1. 获取主摄像机 (通常渲染场景)
        Camera cam = Camera.main;
        if (cam == null)
        {

            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas != null && canvas.worldCamera != null) cam = canvas.worldCamera;
        }

        if (cam == null)
        {
            // 如果实在没有相机，回退到旧方法
            _tempScreenshot = ScreenCapture.CaptureScreenshotAsTexture();
            return;
        }
        int width = Screen.width;
        int height = Screen.height;
        RenderTexture rt = new RenderTexture(width, height, 24);

        // 3. 渲染
        RenderTexture oldTarget = cam.targetTexture; // 备份旧的
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = oldTarget; // 恢复旧的

        // 4. 读取像素到 Texture2D
        RenderTexture.active = rt;
        _tempScreenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        _tempScreenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        _tempScreenshot.Apply();

        // 5. 清理
        RenderTexture.active = null;
        Object.Destroy(rt);
    }

    public string SaveCachedScreenshot(int slotIndex)
    {
        if (_tempScreenshot == null)
        {
            Debug.LogWarning("没有缓存的截图，尝试重新截取（可能会包含UI）");
            CaptureCurrentScreen();
        }

        string screenshotPath = GetScreenshotFilePath(slotIndex);
        string dir = Path.GetDirectoryName(screenshotPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        byte[] bytes = _tempScreenshot.EncodeToPNG();
        File.WriteAllBytes(screenshotPath, bytes);

        return screenshotPath;
    }
}