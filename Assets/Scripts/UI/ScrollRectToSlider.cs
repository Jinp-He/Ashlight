using UnityEngine;
using UnityEngine.UI;

public class ScrollRectToSlider : MonoBehaviour
{
    public ScrollRect scrollRect; // 拖入你的 ScrollView
    public Slider slider;         // 拖入你的 Slider

    // 是否反向？(有时候滑块向上推，内容却向下走，需要勾选这个调整)
    public bool invert = false; 

    void Start()
    {
        // 1. 监听 Slider：当拉动滑块时，控制 ScrollRect
        slider.onValueChanged.AddListener(OnSliderChanged);

        // 2. 监听 ScrollRect：当手指直接拖动内容时，同步 Slider 的位置
        scrollRect.onValueChanged.AddListener(OnScrollRectChanged);
        
        // 初始化位置
        OnScrollRectChanged(Vector2.zero);
    }

    // Slider 变动 -> 改变 ScrollRect
    void OnSliderChanged(float value)
    {
        // 垂直滚动使用 verticalNormalizedPosition
        // 水平滚动使用 horizontalNormalizedPosition
        // 这里以垂直为例：
        
        float targetValue = invert ? 1 - value : value;
        scrollRect.verticalNormalizedPosition = targetValue;
    }

    // ScrollRect 变动 -> 改变 Slider
    void OnScrollRectChanged(Vector2 val)
    {
        // 获取当前 ScrollRect 的位置 (0 ~ 1)
        float currentPos = scrollRect.verticalNormalizedPosition;
        
        // 加上防死循环判断：只有当数值差异较大时才赋值
        if (Mathf.Abs(slider.value - currentPos) > 0.001f)
        {
             slider.value = invert ? 1 - currentPos : currentPos;
        }
    }
    
    // 记得在销毁时移除监听，是个好习惯
    void OnDestroy()
    {
        slider.onValueChanged.RemoveListener(OnSliderChanged);
        scrollRect.onValueChanged.RemoveListener(OnScrollRectChanged);
    }
}