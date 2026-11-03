using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum CardState { Idle, Hand, Played }

// 手牌中的单张卡。悬停放大+上移、邻牌让位、点击打出等逻辑由管理器统一驱动，
// 这里只暴露指针事件回调与基础状态。
[RequireComponent(typeof(RectTransform))]
public class HandCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public int index;
    public CardState state = CardState.Idle;

    [HideInInspector] public RectTransform rect;
    [HideInInspector] public Image image;
    [HideInInspector] public Text label;

    // 当前目标位置（管理器每帧把 anchoredPosition 朝它插值）
    [HideInInspector] public Vector2 targetPos;
    [HideInInspector] public float targetScale = 1f;
    [HideInInspector] public bool inHand;          // 是否参与手牌布局（已抽入）
    [HideInInspector] public Vector2 slotPos;       // 在手中的基础槽位（不出手时位置）

    public System.Action<int> onHover;
    public System.Action<int> onPlay;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        image = GetComponent<Image>();
        label = GetComponentInChildren<Text>();
    }

    public void OnPointerEnter(PointerEventData e) => onHover?.Invoke(index);
    public void OnPointerExit(PointerEventData e)  => onHover?.Invoke(-1);
    public void OnPointerClick(PointerEventData e) => onPlay?.Invoke(index);
}
