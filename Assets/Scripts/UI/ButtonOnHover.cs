using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum HoverEffectType
{
    Glow,
    OnHoverEnlarge
}

public class ButtonOnHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("功能选择")]
    [Tooltip("选择悬停效果类型")]
    public HoverEffectType effectType = HoverEffectType.Glow;
    
    [Header("Glow 设置 (AllIn1Shader)")]
    [Tooltip("Glow强度（hover时）")]
    public float glowIntensity = 1.0f;
    
    [Tooltip("Glow颜色")]
    public Color glowColor = Color.white;
    
    [Tooltip("Glow强度属性名称")]
    public string glowPropertyName = "_Glow";
    
    [Tooltip("是否使用颜色属性")]
    public bool useGlowColor = true;
    
    [Tooltip("Glow颜色属性名称")]
    public string glowColorPropertyName = "_GlowColor";
    
    [Tooltip("是否需要启用GLOW_ON关键字")]
    public bool enableGlowKeyword = true;
    
    [Header("OnHoverEnlarge 设置")]
    [Tooltip("要放大的Sprite（Image组件）")]
    public Image targetSprite;
    
    [Tooltip("放大倍数")]
    [Range(1.0f, 3.0f)]
    public float enlargeScale = 1.2f;
    
    [Tooltip("动画过渡时间（秒）")]
    [Range(0.0f, 1.0f)]
    public float animationDuration = 0.2f;
    
    private Image image;
    private float originalGlow;
    private Color originalGlowColor;
    private Vector3 originalScale;
    private Coroutine scaleCoroutine;
    
    void Start()
    {
        // 获取Image组件
        image = GetComponent<Image>();
        
        // 保存原始glow值
        if (image != null && image.material != null)
        {
            if (image.material.HasProperty(glowPropertyName))
            {
                originalGlow = image.material.GetFloat(glowPropertyName);
            }
            
            if (useGlowColor && image.material.HasProperty(glowColorPropertyName))
            {
                originalGlowColor = image.material.GetColor(glowColorPropertyName);
            }
        }
        Debug.Log("originalGlow: " + originalGlow);
        
        // 初始化OnHoverEnlarge相关
        if (effectType == HoverEffectType.OnHoverEnlarge)
        {
            // 如果没有指定targetSprite，使用当前GameObject的Image组件
            if (targetSprite == null)
            {
                targetSprite = image;
            }
            
            // 保存原始缩放值
            if (targetSprite != null)
            {
                originalScale = targetSprite.transform.localScale;
            }
        }
        
        // 如果有Button组件，添加点击监听
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnClick);
        }
    }
    
    /// <summary>
    /// 鼠标进入时触发
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        switch (effectType)
        {
            case HoverEffectType.Glow:
                SetGlow(true);
                break;
            case HoverEffectType.OnHoverEnlarge:
                EnlargeSprite(true);
                break;
        }
    }
    
    /// <summary>
    /// 鼠标离开时触发
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        switch (effectType)
        {
            case HoverEffectType.Glow:
                SetGlow(false);
                break;
            case HoverEffectType.OnHoverEnlarge:
                EnlargeSprite(false);
                break;
        }
    }
    
    /// <summary>
    /// 设置Glow效果
    /// </summary>
    private void SetGlow(bool isHover)
    {
       if (image == null || image.material == null) return;
       Debug.Log("SetGlow: " + isHover);
        // 启用或禁用GLOW_ON关键字（AllIn1Shader需要）
        if (enableGlowKeyword)
        {
            if (isHover)
            {
                image.material.EnableKeyword("GLOW_ON");
            }
            else
            {
                image.material.DisableKeyword("GLOW_ON");
            }
        }
        
        // 设置Glow强度
        if (image.material.HasProperty(glowPropertyName))
        {
            float targetGlow = isHover ? glowIntensity : originalGlow;
            image.material.SetFloat(glowPropertyName, targetGlow);
        }
        
        // 设置Glow颜色
        if (useGlowColor && image.material.HasProperty(glowColorPropertyName))
        {
            Color targetColor = isHover ? glowColor : originalGlowColor;
            image.material.SetColor(glowColorPropertyName, targetColor);
        }
    }
    
    /// <summary>
    /// 设置Sprite放大效果
    /// </summary>
    private void EnlargeSprite(bool isEnlarge)
    {
        if (targetSprite == null) return;
        
        // 停止之前的协程
        if (scaleCoroutine != null)
        {
            StopCoroutine(scaleCoroutine);
        }
        
        // 计算目标缩放值
        Vector3 targetScale = isEnlarge ? originalScale * enlargeScale : originalScale;
        
        // 启动缩放动画协程
        scaleCoroutine = StartCoroutine(ScaleAnimation(targetScale));
    }
    
    /// <summary>
    /// 缩放动画协程
    /// </summary>
    private IEnumerator ScaleAnimation(Vector3 targetScale)
    {
        if (targetSprite == null) yield break;
        
        Vector3 startScale = targetSprite.transform.localScale;
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / animationDuration);
            // 使用平滑插值
            t = t * t * (3f - 2f * t); // Smoothstep插值
            
            targetSprite.transform.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }
        
        // 确保最终值准确
        targetSprite.transform.localScale = targetScale;
        scaleCoroutine = null;
    }
    
    /// <summary>
    /// 点击事件
    /// </summary>
    private void OnClick()
    {
        // 点击逻辑
    }
    
    /// <summary>
    /// 重置为原始状态
    /// </summary>
    private void OnDisable()
    {
        if (effectType == HoverEffectType.Glow)
        {
            SetGlow(false);
        }
        else if (effectType == HoverEffectType.OnHoverEnlarge)
        {
            if (scaleCoroutine != null)
            {
                StopCoroutine(scaleCoroutine);
                scaleCoroutine = null;
            }
            if (targetSprite != null)
            {
                targetSprite.transform.localScale = originalScale;
            }
        }
    }
}
