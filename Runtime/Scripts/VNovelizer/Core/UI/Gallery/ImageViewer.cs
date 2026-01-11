using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using PrimeTween;

/// <summary>
/// 图片查看器（大图预览）
/// </summary>
public class ImageViewer : MonoBehaviour
{
    private Image image; // 主图片（顶层，显示当前图片）
    private Image imageBack; // 背景图片（底层，显示下一张图片）
    private Button closeBtn;
    private GameObject viewerPanel;
    
    // 用于外部检查是否正在显示
    public bool IsShowing => isShowing;
    
    private List<Sprite> currentImages = new List<Sprite>();
    private int currentIndex = 0;
    private bool isShowing = false;
    
    // 淡化切换相关（使用 PrimeTween）
    private Tween fadeTweenFront; // 顶层图片的补间
    private Tween fadeTweenBack; // 底层图片的补间
    private float fadeDuration = 0.3f;
    private Coroutine fadeCoroutine; // 用于管理淡化协程
    
    private void Awake()
    {
        // 初始化控件
        viewerPanel = gameObject;
        image = GetComponentInChildren<Image>();
        
        // 创建背景Image用于同时淡入淡出效果
        if (image != null)
        {
            // 创建背景Image作为底层
            GameObject backImageObj = new GameObject("ImageBack");
            backImageObj.transform.SetParent(image.transform.parent, false);
            imageBack = backImageObj.AddComponent<Image>();
            
            // 设置背景Image的属性，使其与主Image一致
            RectTransform backRect = imageBack.GetComponent<RectTransform>();
            RectTransform frontRect = image.GetComponent<RectTransform>();
            
            // 复制RectTransform属性
            backRect.anchorMin = frontRect.anchorMin;
            backRect.anchorMax = frontRect.anchorMax;
            backRect.anchoredPosition = frontRect.anchoredPosition;
            backRect.sizeDelta = frontRect.sizeDelta;
            backRect.pivot = frontRect.pivot;
            backRect.localRotation = frontRect.localRotation;
            backRect.localScale = frontRect.localScale;
            
            // 设置背景Image的初始状态
            imageBack.color = new Color(1f, 1f, 1f, 0f); // 初始透明
            imageBack.raycastTarget = false; // 不接收射线，让点击事件穿透到主Image
            
            // 确保背景Image在底层（通过设置SiblingIndex）
            backImageObj.transform.SetSiblingIndex(image.transform.GetSiblingIndex());
        }
        
        // 查找关闭按钮（名称已改为CG_CloseBtn）
        Transform closeBtnTransform = transform.Find("CG_CloseBtn");
        if (closeBtnTransform != null)
        {
            closeBtn = closeBtnTransform.GetComponent<Button>();
        }
        
        // 如果没有找到关闭按钮，尝试在子对象中查找
        if (closeBtn == null)
        {
            closeBtn = GetComponentInChildren<Button>();
        }
        
        // 绑定事件
        if (closeBtn != null)
        {
            closeBtn.onClick.AddListener(OnCloseBtnClick);
        }
        
        // 添加点击事件监听（用于切换图片）
        if (image != null)
        {
            EventTrigger trigger = image.gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = image.gameObject.AddComponent<EventTrigger>();
            }
            
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerClick;
            entry.callback.AddListener((data) => { OnImageClick((PointerEventData)data); });
            trigger.triggers.Add(entry);
        }
        
        // 初始隐藏
        gameObject.SetActive(false);
    }
    
    private void Update()
    {
        // 检测ESC键（使用新版Input System）
        if (isShowing && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            OnCloseBtnClick();
        }
    }
    
    /// <summary>
    /// 显示图片组
    /// </summary>
    public void Show(List<Sprite> images)
    {
        if (images == null || images.Count == 0)
        {
            Debug.LogWarning("[ImageViewer] 图片列表为空");
            return;
        }
        
        currentImages = new List<Sprite>(images);
        currentIndex = 0;
        isShowing = true;
        
        // 显示查看器
        gameObject.SetActive(true);
        
        // 显示第一张图片
        UpdateImageDisplay();
        
        // 确保背景Image是透明的
        if (imageBack != null)
        {
            imageBack.color = new Color(1f, 1f, 1f, 0f);
            imageBack.sprite = null;
        }
    }
    
    /// <summary>
    /// 更新图片显示
    /// </summary>
    private void UpdateImageDisplay()
    {
        if (currentImages == null || currentIndex < 0 || currentIndex >= currentImages.Count)
        {
            return;
        }
        
        if (image != null && currentImages[currentIndex] != null)
        {
            image.sprite = currentImages[currentIndex];
        }
    }
    
    /// <summary>
    /// 切换到下一张图片（淡化切换，使用 PrimeTween）
    /// </summary>
    private void NextImage()
    {
        if (currentImages == null || currentImages.Count == 0) return;
        
        // 如果只有一张图片，不需要切换
        if (currentImages.Count <= 1) return;
        
        // 循环：最后一张切换到第一张
        currentIndex = (currentIndex + 1) % currentImages.Count;
        
        // 停止之前的淡化协程（如果正在运行）
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
        
        // 停止之前的 PrimeTween 动画（如果正在运行）
        if (fadeTweenFront.isAlive)
        {
            fadeTweenFront.Stop();
            fadeTweenFront = default;
        }
        if (fadeTweenBack.isAlive)
        {
            fadeTweenBack.Stop();
            fadeTweenBack = default;
        }
        
        // 启动新的淡化切换
        fadeCoroutine = StartCoroutine(FadeSwitchImage());
    }
    
    /// <summary>
    /// 淡化切换图片协程（基于 PrimeTween，同时淡入淡出）
    /// </summary>
    private IEnumerator FadeSwitchImage()
    {
        if (image == null || imageBack == null) yield break;
        
        // 1. 准备：将下一张图片设置到背景Image，初始透明
        if (currentImages != null && currentIndex >= 0 && currentIndex < currentImages.Count)
        {
            imageBack.sprite = currentImages[currentIndex];
            imageBack.color = new Color(1f, 1f, 1f, 0f);
        }
        
        // 2. 同时进行淡入和淡出
        // 顶层（当前图片）淡出：Alpha 1 -> 0
        fadeTweenFront = Tween.Alpha(image, startValue: 1f, endValue: 0f, duration: fadeDuration);
        // 底层（下一张图片）淡入：Alpha 0 -> 1
        fadeTweenBack = Tween.Alpha(imageBack, startValue: 0f, endValue: 1f, duration: fadeDuration);
        
        // 等待动画完成（两个动画同时进行，持续时间相同，只需等待一个即可）
        yield return fadeTweenFront.ToYieldInstruction();
        
        // 3. 动画完成后，交换图片
        // 将背景图片的内容复制到主图片
        image.sprite = imageBack.sprite;
        image.color = Color.white;
        
        // 清空背景图片
        imageBack.sprite = null;
        imageBack.color = new Color(1f, 1f, 1f, 0f);
        
        // 清理引用
        fadeTweenFront = default;
        fadeTweenBack = default;
        fadeCoroutine = null;
    }
    
    /// <summary>
    /// 图片点击事件（切换图片）
    /// </summary>
    private void OnImageClick(PointerEventData eventData)
    {
        // 左键点击：切换到下一张
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            NextImage();
        }
        // 右键点击：关闭查看器
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            OnCloseBtnClick();
        }
    }
    
    /// <summary>
    /// 关闭按钮点击
    /// </summary>
    private void OnCloseBtnClick()
    {
        isShowing = false;
        
        // 停止淡化协程（如果正在运行）
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
        
        // 停止 PrimeTween 动画（如果正在运行）
        if (fadeTweenFront.isAlive)
        {
            fadeTweenFront.Stop();
            fadeTweenFront = default;
        }
        if (fadeTweenBack.isAlive)
        {
            fadeTweenBack.Stop();
            fadeTweenBack = default;
        }
        
        // 恢复图片透明度
        if (image != null)
        {
            image.color = Color.white;
        }
        if (imageBack != null)
        {
            imageBack.color = new Color(1f, 1f, 1f, 0f);
            imageBack.sprite = null;
        }
        
        gameObject.SetActive(false);
    }
}

