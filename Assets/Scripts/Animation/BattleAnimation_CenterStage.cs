using UnityEngine;
using Spine.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Ashlight.Battle.Core.Data;
using Scripts.UI;
using UnityEngine.UI;
using Sirenix.OdinInspector;

/// <summary>
/// 中央舞台式战斗演出动画组件。
/// 不生成副本、不隐藏原单位，而是把"施法者"和"目标"两个真实单位 Tween 到屏幕中央对峙，
/// 播放 attack/shouji 后再 Tween 回各自原位。其它单位不受影响。
/// 普通（迅捷）打牌仍走原地播放版 <see cref="BattleAnimation"/>。
/// </summary>
public class BattleAnimation_CenterStage : MonoBehaviour, IBattleAnimationPlayer
{
    #region 序列化字段

    // 以下三个字段为旧"抽离副本"方案遗留，现方案（移动真实单位）已不再使用，
    // 保留以避免破坏场景里已有的序列化引用。
    [Header("旧方案遗留字段（现已不使用）")]
    public RectTransform CharacterPosition;
    public RectTransform EnemyPosition;
    public SkeletonGraphic skeletonGraphicPrefab;

    [Header("预制体")]
    [Tooltip("伤害数字预制体（包含TextMeshProUGUI组件，如果为空则使用动态创建）")]
    public GameObject damageTextPrefab;

    [TitleGroup("战斗演出控制台")]
    [BoxGroup("战斗演出控制台/时间设置"), LabelText("1. 角色战斗动画播放时间"), MinValue(0f), SuffixLabel("秒")]
    [SerializeField] private float battleAnimationDuration = 0.55f;

    [BoxGroup("战斗演出控制台/时间设置"), LabelText("2. 角色进入中间的时间"), MinValue(0.01f), SuffixLabel("秒")]
    [SerializeField] private float enterCenterDuration = 0.2f;

    [BoxGroup("战斗演出控制台/时间设置"), LabelText("3. 离场时间"), MinValue(0.01f), SuffixLabel("秒")]
    [SerializeField] private float exitDuration = 0.2f;

    [BoxGroup("战斗演出控制台/时间设置"), LabelText("4. 动画播放完毕停留时间"), MinValue(0f), SuffixLabel("秒")]
    [SerializeField] private float postAnimationHoldDuration = 0.15f;

    #endregion

    #region 私有字段

    private Canvas _canvas;

    private const float MAX_DURATION = 2.5f;
    private const float DAMAGE_FLOAT_DURATION = 0.6f;
    private const float DAMAGE_BEFORE_EXIT_DELAY = 0.2f;
    private const float PAIR_GAP = 310f;
    private const float PAIR_SCREEN_Y = 360f;
    private const float EFFECT_SCREEN_Y = 400f;
    private const float EFFECT_FPS = 15f;
    private const int PRESENTATION_SORTING_ORDER = 300;
    private const string EFFECT_RESOURCE_PATH = "UI/BattleScene/BattleCutscene/Frames";

    private RectTransform _presentationRoot;
    private Sprite[] _effectFrames;
    private readonly Queue<Image> _effectPool = new Queue<Image>();

    private sealed class CanvasSortingSnapshot
    {
        public Canvas Canvas;
        public bool WasAdded;
        public bool OverrideSorting;
        public int SortingOrder;
    }

    private sealed class LayoutSlotSnapshot
    {
        public RectTransform Rect;
        public Transform Parent;
        public Vector3 HomeWorldPosition;
        public Vector3 HomeLocalScale;
        public GameObject Placeholder;
    }

    #endregion

    #region Unity生命周期

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 获取动画总演出时间（用于解算时等待）。
    /// </summary>
    public static float GetTotalAnimationDuration()
    {
        return MAX_DURATION;
    }

    /// <summary>独立于手牌层的战场演出/震动层。</summary>
    public RectTransform GetPresentationRoot()
    {
        EnsurePresentationRoot();
        return _presentationRoot;
    }

    /// <summary>
    /// 播放中央舞台战斗演出。
    /// </summary>
    public IEnumerator PlayBattleAnimation(
        UnitState casterState,
        UnitState targetState,
        MonoBehaviour casterUI,
        MonoBehaviour targetUI,
        bool isAttackCard,
        int damage = 0,
        Action onHit = null)
    {
        if (casterState == null || targetState == null || casterUI == null || targetUI == null)
        {
            onHit?.Invoke();
            yield break;
        }

        RectTransform casterRect = casterUI.transform as RectTransform;
        RectTransform targetRect = targetUI.transform as RectTransform;
        if (casterRect == null || targetRect == null)
        {
            onHit?.Invoke();
            yield break;
        }

        // SetAsLastSibling 只能解决同一排容器内的遮挡。前后排属于不同父节点，
        // 因此演出期间还要用独立 Canvas 覆盖全局排序，结束后完整恢复。
        CanvasSortingSnapshot casterSorting = ElevateForPresentation(casterRect, PRESENTATION_SORTING_ORDER + 1);
        CanvasSortingSnapshot targetSorting = casterRect == targetRect
            ? null
            : ElevateForPresentation(targetRect, PRESENTATION_SORTING_ORDER);

        EnsurePresentationRoot();

        bool moveLeftToRight = casterState.IsPlayerUnit;
        Vector3 casterStart = ComputePairPosition(moveLeftToRight ? PairPoint.Start : PairPoint.End, true);
        Vector3 targetStart = ComputePairPosition(moveLeftToRight ? PairPoint.Start : PairPoint.End, false);
        Vector3 casterCenter = ComputePairPosition(PairPoint.Center, true);
        Vector3 targetCenter = ComputePairPosition(PairPoint.Center, false);
        Vector3 casterEnd = ComputePairPosition(moveLeftToRight ? PairPoint.End : PairPoint.Start, true);
        Vector3 targetEnd = ComputePairPosition(moveLeftToRight ? PairPoint.End : PairPoint.Start, false);

        bool sameUnit = casterRect == targetRect;
        if (sameUnit)
        {
            targetStart = casterStart;
            targetCenter = casterCenter;
            targetEnd = casterEnd;
        }

        // 把真实单位临时挂到战场演出层：跨前后排时层级稳定，且震动只影响演出层，不影响手牌。
        LayoutSlotSnapshot casterLayout = DetachToPresentationLayer(casterRect);
        LayoutSlotSnapshot targetLayout = sameUnit ? null : DetachToPresentationLayer(targetRect);

        try
        {
            casterRect.position = casterStart;
            if (!sameUnit) targetRect.position = targetStart;

            // 特效与角色进入必须在同一帧启动，形成一个完整的入场动作。
            StartCoroutine(PlayEffectFrames(!moveLeftToRight));

            // 单位直接快速进入中央位置，不再在慢速区持续横穿。
            Tween entry = casterRect.DOMove(casterCenter, enterCenterDuration).SetEase(Ease.OutCubic);
            if (!sameUnit) targetRect.DOMove(targetCenter, enterCenterDuration).SetEase(Ease.OutCubic);
            yield return entry.WaitForCompletion();

            PlayCasterAttack(casterUI);
            if (isAttackCard && !sameUnit)
            {
                PlayTargetHurt(targetUI);
            }

            if (battleAnimationDuration > 0f)
                yield return new WaitForSeconds(battleAnimationDuration);

            // 动作完整播放后停留一拍，再确认数值结果。
            if (postAnimationHoldDuration > 0f)
                yield return new WaitForSeconds(postAnimationHoldDuration);

            // 动作结束后再显示伤害数字。
            if (damage > 0)
            {
                Vector3 damagePos = GetUnitTopWorldPosition(targetUI);
                if (damagePos != Vector3.zero)
                {
                    ShowDamageNumber(damagePos, damage);
                }
            }

            // 命中回调仅负责数字、震动等结果反馈；单位状态在入场前已经同步。
            onHit?.Invoke();

            yield return new WaitForSeconds(DAMAGE_BEFORE_EXIT_DELAY);

            Tween exit = casterRect.DOMove(casterEnd, exitDuration).SetEase(Ease.InCubic);
            if (!sameUnit) targetRect.DOMove(targetEnd, exitDuration).SetEase(Ease.InCubic);
            yield return exit.WaitForCompletion();
        }
        finally
        {
            casterRect.DOKill();
            targetRect.DOKill();
            RestoreLayoutSlot(casterLayout);
            RestoreLayoutSlot(targetLayout);
            RestorePresentationSorting(casterSorting);
            RestorePresentationSorting(targetSorting);
        }
    }

    #endregion

    #region 私有方法 - 位置计算

    private static CanvasSortingSnapshot ElevateForPresentation(RectTransform rect, int sortingOrder)
    {
        if (rect == null) return null;

        Canvas canvas = rect.GetComponent<Canvas>();
        bool wasAdded = canvas == null;
        if (wasAdded)
        {
            canvas = rect.gameObject.AddComponent<Canvas>();
        }

        var snapshot = new CanvasSortingSnapshot
        {
            Canvas = canvas,
            WasAdded = wasAdded,
            OverrideSorting = canvas.overrideSorting,
            SortingOrder = canvas.sortingOrder
        };

        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
        return snapshot;
    }

    private static void RestorePresentationSorting(CanvasSortingSnapshot snapshot)
    {
        if (snapshot?.Canvas == null) return;

        snapshot.Canvas.overrideSorting = snapshot.OverrideSorting;
        snapshot.Canvas.sortingOrder = snapshot.SortingOrder;
        if (snapshot.WasAdded)
        {
            Destroy(snapshot.Canvas);
        }
    }

    /// <summary>
    /// 计算单位在中央舞台的世界坐标：X 落在屏幕中央左右（玩家左、敌人右），Y 保持原值。
    /// </summary>
    private enum PairPoint { Start, Center, End }

    private Vector3 ComputePairPosition(PairPoint point, bool isCaster)
    {
        if (_canvas == null)
        {
            return transform.position;
        }

        RectTransform canvasRect = _canvas.transform as RectTransform;
        if (canvasRect == null)
        {
            return transform.position;
        }

        float halfWidth = canvasRect.rect.width * 0.5f;
        float pairHalfGap = PAIR_GAP * 0.5f;
        float centerX;
        switch (point)
        {
            case PairPoint.Start: centerX = -halfWidth - pairHalfGap; break;
            case PairPoint.End: centerX = halfWidth + pairHalfGap; break;
            default: centerX = 0f; break;
        }

        float x = centerX + (isCaster ? -pairHalfGap : pairHalfGap);
        float y = canvasRect.rect.yMax - PAIR_SCREEN_Y;
        return canvasRect.TransformPoint(new Vector3(x, y, 0f));
    }

    private LayoutSlotSnapshot DetachToPresentationLayer(RectTransform rect)
    {
        if (rect == null || rect.parent == null || _presentationRoot == null) return null;

        var snapshot = new LayoutSlotSnapshot
        {
            Rect = rect,
            Parent = rect.parent,
            HomeWorldPosition = rect.position,
            HomeLocalScale = rect.localScale
        };

        GameObject placeholder = new GameObject(rect.name + "_PresentationSlot", typeof(RectTransform), typeof(LayoutElement));
        RectTransform slotRect = placeholder.GetComponent<RectTransform>();
        slotRect.SetParent(snapshot.Parent, false);
        slotRect.SetSiblingIndex(rect.GetSiblingIndex());
        slotRect.sizeDelta = rect.sizeDelta;

        LayoutElement sourceLayout = rect.GetComponent<LayoutElement>();
        LayoutElement slotLayout = placeholder.GetComponent<LayoutElement>();
        if (sourceLayout != null)
        {
            slotLayout.minWidth = sourceLayout.minWidth;
            slotLayout.minHeight = sourceLayout.minHeight;
            slotLayout.preferredWidth = sourceLayout.preferredWidth;
            slotLayout.preferredHeight = sourceLayout.preferredHeight;
            slotLayout.flexibleWidth = sourceLayout.flexibleWidth;
            slotLayout.flexibleHeight = sourceLayout.flexibleHeight;
        }
        else
        {
            slotLayout.preferredWidth = rect.rect.width;
            slotLayout.preferredHeight = rect.rect.height;
        }

        snapshot.Placeholder = placeholder;
        rect.SetParent(_presentationRoot, true);
        rect.SetAsLastSibling();
        return snapshot;
    }

    private static void RestoreLayoutSlot(LayoutSlotSnapshot snapshot)
    {
        if (snapshot?.Rect == null || snapshot.Parent == null) return;

        int slotIndex = snapshot.Placeholder != null
            ? snapshot.Placeholder.transform.GetSiblingIndex()
            : snapshot.Parent.childCount;
        if (snapshot.Placeholder != null)
        {
            snapshot.Placeholder.transform.SetParent(null, false);
            Destroy(snapshot.Placeholder);
        }

        snapshot.Rect.SetParent(snapshot.Parent, true);
        snapshot.Rect.SetSiblingIndex(Mathf.Clamp(slotIndex, 0, snapshot.Parent.childCount - 1));
        snapshot.Rect.position = snapshot.HomeWorldPosition;
        snapshot.Rect.localScale = snapshot.HomeLocalScale;
    }

    private void EnsurePresentationRoot()
    {
        if (_presentationRoot != null || _canvas == null) return;

        GameObject root = new GameObject("BattlefieldPresentationRoot", typeof(RectTransform), typeof(Canvas));
        _presentationRoot = root.GetComponent<RectTransform>();
        _presentationRoot.SetParent(_canvas.transform, false);
        _presentationRoot.anchorMin = Vector2.zero;
        _presentationRoot.anchorMax = Vector2.one;
        _presentationRoot.offsetMin = Vector2.zero;
        _presentationRoot.offsetMax = Vector2.zero;
        _presentationRoot.SetAsLastSibling();

        Canvas layerCanvas = root.GetComponent<Canvas>();
        layerCanvas.overrideSorting = true;
        layerCanvas.sortingOrder = PRESENTATION_SORTING_ORDER - 1;
    }

    private IEnumerator PlayEffectFrames(bool flipHorizontal)
    {
        EnsurePresentationRoot();
        if (_presentationRoot == null) yield break;

        if (_effectFrames == null || _effectFrames.Length == 0)
        {
            _effectFrames = Resources.LoadAll<Sprite>(EFFECT_RESOURCE_PATH);
            Array.Sort(_effectFrames, (a, b) => string.CompareOrdinal(a.name, b.name));
        }
        if (_effectFrames.Length == 0) yield break;

        Image image = AcquireEffectImage();
        GameObject instance = image.gameObject;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, ((_canvas.transform as RectTransform)?.rect.yMax ?? 450f) - EFFECT_SCREEN_Y);
        rect.localScale = new Vector3(flipHorizontal ? -1f : 1f, 1f, 1f);

        float frameDuration = 1f / EFFECT_FPS;
        foreach (Sprite frame in _effectFrames)
        {
            if (instance == null) yield break;
            image.sprite = frame;
            image.SetNativeSize();
            AlignEffectFrameToEntryEdge(rect, flipHorizontal);
            yield return new WaitForSeconds(frameDuration);
        }
        ReleaseEffectImage(image);
    }

    /// <summary>
    /// 序列首帧比后续帧窄，不能固定居中；玩家方向贴左边缘，敌人方向镜像贴右边缘。
    /// 这样首帧从画外扫入，而不是突然出现在屏幕中央。
    /// </summary>
    private void AlignEffectFrameToEntryEdge(RectTransform effectRect, bool flipHorizontal)
    {
        RectTransform canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
        if (effectRect == null || canvasRect == null) return;

        float halfWidth = effectRect.rect.width * 0.5f;
        float x = flipHorizontal
            ? canvasRect.rect.xMax - halfWidth
            : canvasRect.rect.xMin + halfWidth;
        effectRect.anchoredPosition = new Vector2(x, canvasRect.rect.yMax - EFFECT_SCREEN_Y);
    }

    private Image AcquireEffectImage()
    {
        Image image = _effectPool.Count > 0 ? _effectPool.Dequeue() : null;
        if (image == null)
        {
            GameObject instance = new GameObject("BattleCutsceneEffect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            image = instance.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
        }

        image.transform.SetParent(_presentationRoot, false);
        image.transform.SetAsLastSibling();
        image.gameObject.SetActive(true);
        image.color = Color.white;
        return image;
    }

    private void ReleaseEffectImage(Image image)
    {
        if (image == null) return;
        image.sprite = null;
        image.gameObject.SetActive(false);
        _effectPool.Enqueue(image);
    }

    #endregion

    #region 私有方法 - 动画驱动

    private void PlayCasterAttack(MonoBehaviour casterUI)
    {
        if (casterUI == null) return;

        var character = casterUI as Character;
        if (character != null)
        {
            character.PlayAttackAnimation();
            return;
        }

        var enemy = casterUI as Enemy;
        if (enemy != null)
        {
            enemy.PlayAttackAnimation();
        }
    }

    private void PlayTargetHurt(MonoBehaviour targetUI)
    {
        if (targetUI == null) return;

        var character = targetUI as Character;
        if (character != null)
        {
            character.PlayShoujiAnimation();
            return;
        }

        var enemy = targetUI as Enemy;
        if (enemy != null)
        {
            enemy.PlayShoujiAnimation();
        }
    }

    /// <summary>
    /// 取目标 Skeleton 的头顶世界坐标，用于伤害数字定位。
    /// </summary>
    private Vector3 GetUnitTopWorldPosition(MonoBehaviour unitUI)
    {
        if (unitUI == null) return Vector3.zero;

        SkeletonGraphic skeleton = null;
        var character = unitUI as Character;
        if (character != null)
        {
            skeleton = character.Skeleton_Unit;
        }
        else
        {
            var enemy = unitUI as Enemy;
            if (enemy != null)
            {
                skeleton = enemy.Skeleton_Unit;
            }
        }

        if (skeleton == null)
        {
            return unitUI.transform.position + new Vector3(0, 200f, 0);
        }

        Vector3 worldPos = skeleton.transform.position;
        if (skeleton.Skeleton != null)
        {
            float[] vertexBuffer = null;
            skeleton.Skeleton.GetBounds(out float minX, out float minY, out float maxX, out float maxY, ref vertexBuffer);
            float height = maxY - minY;
            worldPos.y += height * skeleton.transform.lossyScale.y;
        }
        else
        {
            worldPos.y += 200f;
        }
        return worldPos;
    }

    private void ShowDamageNumber(Vector3 targetPosition, int damage)
    {
        if (damage <= 0 || _canvas == null)
        {
            return;
        }

        GameObject damageTextObj;
        TMPro.TextMeshProUGUI textMesh;
        RectTransform rectTransform;

        if (damageTextPrefab != null)
        {
            damageTextObj = Instantiate(damageTextPrefab, _canvas.transform);
            damageTextObj.transform.position = targetPosition;
            damageTextObj.name = "BattleDamageText";

            textMesh = damageTextObj.GetComponent<TMPro.TextMeshProUGUI>();
            if (textMesh == null)
            {
                textMesh = damageTextObj.AddComponent<TMPro.TextMeshProUGUI>();
            }

            rectTransform = damageTextObj.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                rectTransform = damageTextObj.AddComponent<RectTransform>();
            }
        }
        else
        {
            damageTextObj = new GameObject("BattleDamageText");
            damageTextObj.transform.SetParent(_canvas.transform);
            damageTextObj.transform.position = targetPosition;

            textMesh = damageTextObj.AddComponent<TMPro.TextMeshProUGUI>();
            textMesh.fontSize = 48;
            textMesh.color = Color.red;
            textMesh.alignment = TMPro.TextAlignmentOptions.Center;

            rectTransform = damageTextObj.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(200, 100);
        }

        if (textMesh != null)
        {
            textMesh.text = damage.ToString();
        }

        Sequence damageSequence = DOTween.Sequence();
        damageSequence.Append(
            rectTransform.DOAnchorPosY(rectTransform.anchoredPosition.y + 100f, DAMAGE_FLOAT_DURATION)
                .SetEase(Ease.OutQuad)
        );
        if (textMesh != null)
        {
            damageSequence.Join(
                textMesh.DOFade(0f, DAMAGE_FLOAT_DURATION).SetEase(Ease.InQuad)
            );
        }
        damageSequence.OnComplete(() => Destroy(damageTextObj));
    }

    #endregion
}
