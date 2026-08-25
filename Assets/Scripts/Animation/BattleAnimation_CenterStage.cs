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
/// 从战场单位复制纯视觉图像，在独立演出层移动并播放 attack/shouji，结束后销毁副本。
/// 战场上的真实单位不改父节点、不改位置，也不会被演出 Tween 操作。
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

    [BoxGroup("战斗演出控制台/画面设置"), LabelText("5. 人物 Y 轴偏移"), SuffixLabel("px")]
    [SerializeField] private float characterYOffset = 0f;

    [BoxGroup("战斗演出控制台/画面设置"), LabelText("6. 黑影动画帧率"), MinValue(1f), SuffixLabel("FPS")]
    [SerializeField] private float shadowAnimationFps = 15f;

    #endregion

    #region 私有字段

    private Canvas _canvas;

    private const float MAX_DURATION = 2.5f;
    private const float DAMAGE_FLOAT_DURATION = 0.6f;
    private const float DAMAGE_BEFORE_EXIT_DELAY = 0.2f;
    private const float PAIR_GAP = 310f;
    private const float PAIR_SCREEN_Y = 360f;
    private const float EFFECT_SCREEN_Y = 400f;
    private const int PRESENTATION_SORTING_ORDER = 300;
    private const string EFFECT_RESOURCE_PATH = "UI/BattleScene/BattleCutscene/Frames";

    private RectTransform _presentationRoot;
    private Sprite[] _effectFrames;
    private readonly Queue<Image> _effectPool = new Queue<Image>();
    private readonly List<CanvasGroupSnapshot> _hiddenBattleUi = new List<CanvasGroupSnapshot>();
    private readonly List<IntentionView> _hiddenIntentionViews = new List<IntentionView>();
    private readonly HashSet<GameObject> _activeVisualCopies = new HashSet<GameObject>();
    private int _battleUiHideDepth;

    private sealed class PresentationVisual
    {
        public GameObject Root;
        public RectTransform Rect;
        public SkeletonGraphic Skeleton;
        public Image Image;
        public Sprite AttackSprite;
        public Sprite HurtSprite;
    }

    private sealed class CanvasGroupSnapshot
    {
        public CanvasGroup Group;
        public bool WasAdded;
        public float Alpha;
        public bool Interactable;
        public bool BlocksRaycasts;
    }

    #endregion

    #region Unity生命周期

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
    }

    private void OnDisable()
    {
        DestroyAllVisualCopies();
        ForceRestoreBattleStatusUi();
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

        EnsurePresentationRoot();
        if (_presentationRoot == null)
        {
            onHit?.Invoke();
            yield break;
        }

        bool sameUnit = casterUI == targetUI;
        PresentationVisual casterVisual = CreatePresentationVisual(casterUI, "Caster");
        PresentationVisual targetVisual = sameUnit
            ? casterVisual
            : CreatePresentationVisual(targetUI, "Target");
        if (casterVisual?.Rect == null || targetVisual?.Rect == null)
        {
            DestroyPresentationVisual(casterVisual);
            if (!sameUnit) DestroyPresentationVisual(targetVisual);
            onHit?.Invoke();
            yield break;
        }

        bool moveLeftToRight = casterState.IsPlayerUnit;
        bool casterOnLeft = casterState.IsPlayerUnit;
        Vector3 casterStart = ComputePairPosition(moveLeftToRight ? PairPoint.Start : PairPoint.End, casterOnLeft);
        Vector3 targetStart = ComputePairPosition(moveLeftToRight ? PairPoint.Start : PairPoint.End, !casterOnLeft);
        Vector3 casterCenter = ComputePairPosition(PairPoint.Center, casterOnLeft);
        Vector3 targetCenter = ComputePairPosition(PairPoint.Center, !casterOnLeft);
        Vector3 casterEnd = ComputePairPosition(moveLeftToRight ? PairPoint.End : PairPoint.Start, casterOnLeft);
        Vector3 targetEnd = ComputePairPosition(moveLeftToRight ? PairPoint.End : PairPoint.Start, !casterOnLeft);

        if (sameUnit)
        {
            targetStart = casterStart;
            targetCenter = casterCenter;
            targetEnd = casterEnd;
        }

        HideBattleStatusUi();

        try
        {
            casterVisual.Rect.position = casterStart;
            if (!sameUnit) targetVisual.Rect.position = targetStart;

            // 所有动作只作用于视觉副本，战场上的真实角色始终留在原父节点和原位置。
            PlayVisualAnimation(casterVisual, true);
            if (isAttackCard && !sameUnit)
            {
                PlayVisualAnimation(targetVisual, false);
            }

            // 特效与角色进入必须在同一帧启动，形成一个完整的入场动作。
            StartCoroutine(PlayEffectFrames(!moveLeftToRight));

            // 单位直接快速进入中央位置，不再在慢速区持续横穿。
            Tween entry = casterVisual.Rect.DOMove(casterCenter, enterCenterDuration).SetEase(Ease.OutCubic);
            if (!sameUnit) targetVisual.Rect.DOMove(targetCenter, enterCenterDuration).SetEase(Ease.OutCubic);
            yield return entry.WaitForCompletion();

            // 战斗动画时长从入场开始计时，扣除已经用于进入中央的时间。
            float remainingBattleDuration = Mathf.Max(0f, battleAnimationDuration - enterCenterDuration);
            if (remainingBattleDuration > 0f)
                yield return new WaitForSeconds(remainingBattleDuration);

            // 动作完整播放后停留一拍，再确认数值结果。
            if (postAnimationHoldDuration > 0f)
                yield return new WaitForSeconds(postAnimationHoldDuration);

            // 动作结束后再显示伤害数字。
            if (damage > 0)
            {
                Vector3 damagePos = GetVisualTopWorldPosition(targetVisual);
                if (damagePos != Vector3.zero)
                {
                    ShowDamageNumber(damagePos, damage);
                }
            }

            // 命中回调仅负责数字、震动等结果反馈；单位状态在入场前已经同步。
            onHit?.Invoke();

            yield return new WaitForSeconds(DAMAGE_BEFORE_EXIT_DELAY);

            Tween exit = casterVisual.Rect.DOMove(casterEnd, exitDuration).SetEase(Ease.InCubic);
            if (!sameUnit) targetVisual.Rect.DOMove(targetEnd, exitDuration).SetEase(Ease.InCubic);
            yield return exit.WaitForCompletion();
        }
        finally
        {
            DestroyPresentationVisual(casterVisual);
            if (!sameUnit) DestroyPresentationVisual(targetVisual);
            RestoreBattleStatusUi();
        }
    }

    #endregion

    #region 私有方法 - 位置计算

    private PresentationVisual CreatePresentationVisual(MonoBehaviour unitUI, string role)
    {
        if (unitUI == null || _presentationRoot == null) return null;

        SkeletonGraphic sourceSkeleton = null;
        Image sourceImage = null;
        Sprite attackSprite = null;
        Sprite hurtSprite = null;

        Character character = unitUI as Character;
        if (character != null)
        {
            sourceSkeleton = character.Skeleton_Unit;
        }
        else
        {
            Enemy enemy = unitUI as Enemy;
            if (enemy != null)
            {
                if (enemy.Skeleton_Unit != null && enemy.Skeleton_Unit.gameObject.activeInHierarchy)
                {
                    sourceSkeleton = enemy.Skeleton_Unit;
                }
                else
                {
                    sourceImage = enemy.EnemyImage;
                    attackSprite = enemy.GetBattlePresentationSprite(true);
                    hurtSprite = enemy.GetBattlePresentationSprite(false);
                }
            }
        }

        RectTransform sourceRect = sourceSkeleton != null
            ? sourceSkeleton.rectTransform
            : sourceImage != null ? sourceImage.rectTransform : null;
        if (sourceRect == null) return null;

        GameObject copy = Instantiate(sourceRect.gameObject, _presentationRoot, false);
        copy.name = $"{unitUI.name}_{role}_VisualCopy";
        copy.SetActive(true);

        RectTransform copyRect = copy.transform as RectTransform;
        if (copyRect == null)
        {
            Destroy(copy);
            return null;
        }

        // 副本只保留原图像的尺寸、轴心、世界缩放和朝向；位置由中央演出单独控制。
        copyRect.anchorMin = copyRect.anchorMax = new Vector2(0.5f, 0.5f);
        copyRect.pivot = sourceRect.pivot;
        copyRect.sizeDelta = sourceRect.rect.size;
        copyRect.rotation = sourceRect.rotation;
        copyRect.localScale = DivideScale(sourceRect.lossyScale, _presentationRoot.lossyScale);
        copyRect.SetAsLastSibling();

        SkeletonGraphic copySkeleton = copy.GetComponent<SkeletonGraphic>();
        if (copySkeleton != null)
        {
            copySkeleton.raycastTarget = false;
            copySkeleton.Initialize(true);
            if (sourceSkeleton != null)
                copySkeleton.color = sourceSkeleton.color;
        }

        Image copyImage = copy.GetComponent<Image>();
        if (copyImage != null)
        {
            copyImage.raycastTarget = false;
            copyImage.color = sourceImage != null ? sourceImage.color : Color.white;
        }

        _activeVisualCopies.Add(copy);
        return new PresentationVisual
        {
            Root = copy,
            Rect = copyRect,
            Skeleton = copySkeleton,
            Image = copyImage,
            AttackSprite = attackSprite,
            HurtSprite = hurtSprite
        };
    }

    private static Vector3 DivideScale(Vector3 worldScale, Vector3 parentWorldScale)
    {
        return new Vector3(
            Mathf.Approximately(parentWorldScale.x, 0f) ? worldScale.x : worldScale.x / parentWorldScale.x,
            Mathf.Approximately(parentWorldScale.y, 0f) ? worldScale.y : worldScale.y / parentWorldScale.y,
            Mathf.Approximately(parentWorldScale.z, 0f) ? worldScale.z : worldScale.z / parentWorldScale.z);
    }

    private static void PlayVisualAnimation(PresentationVisual visual, bool attack)
    {
        if (visual == null) return;

        if (visual.Skeleton?.AnimationState != null)
        {
            var data = visual.Skeleton.AnimationState.Data?.SkeletonData;
            string animation = attack
                ? "attack1"
                : data?.FindAnimation("shouji") != null ? "shouji" : "hit";
            if (data?.FindAnimation(animation) != null)
            {
                visual.Skeleton.AnimationState.SetAnimation(0, animation, false);
                if (data.FindAnimation("idle") != null)
                    visual.Skeleton.AnimationState.AddAnimation(0, "idle", true, 0.5f);
            }
            return;
        }

        if (visual.Image != null)
        {
            Sprite sprite = attack ? visual.AttackSprite : visual.HurtSprite;
            if (sprite != null)
            {
                visual.Image.sprite = sprite;
                visual.Image.preserveAspect = true;
                visual.Image.SetNativeSize();
            }
        }
    }

    private static Vector3 GetVisualTopWorldPosition(PresentationVisual visual)
    {
        if (visual?.Rect == null) return Vector3.zero;

        if (visual.Skeleton?.Skeleton != null)
        {
            visual.Skeleton.Skeleton.UpdateWorldTransform();
            float[] vertices = null;
            visual.Skeleton.Skeleton.GetBounds(
                out float minX,
                out float minY,
                out float maxX,
                out float maxY,
                ref vertices);
            return visual.Skeleton.transform.TransformPoint(new Vector3((minX + maxX) * 0.5f, maxY, 0f));
        }

        var corners = new Vector3[4];
        visual.Rect.GetWorldCorners(corners);
        return (corners[1] + corners[2]) * 0.5f;
    }

    private void DestroyPresentationVisual(PresentationVisual visual)
    {
        if (visual?.Root == null) return;
        if (visual.Rect != null) visual.Rect.DOKill();
        _activeVisualCopies.Remove(visual.Root);
        visual.Root.SetActive(false);
        Destroy(visual.Root);
    }

    private void DestroyAllVisualCopies()
    {
        foreach (GameObject copy in new List<GameObject>(_activeVisualCopies))
        {
            if (copy == null) continue;
            copy.transform.DOKill();
            copy.SetActive(false);
            Destroy(copy);
        }
        _activeVisualCopies.Clear();
    }

    /// <summary>
    /// 计算单位在中央舞台的世界坐标：X 落在屏幕中央左右（玩家左、敌人右），Y 保持原值。
    /// </summary>
    private enum PairPoint { Start, Center, End }

    private Vector3 ComputePairPosition(PairPoint point, bool useLeftSlot)
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

        float x = centerX + (useLeftSlot ? -pairHalfGap : pairHalfGap);
        float y = canvasRect.rect.yMax - PAIR_SCREEN_Y + characterYOffset;
        return canvasRect.TransformPoint(new Vector3(x, y, 0f));
    }

    private void HideBattleStatusUi()
    {
        _battleUiHideDepth++;
        if (_battleUiHideDepth > 1)
            return;

        _hiddenBattleUi.Clear();
        _hiddenIntentionViews.Clear();
        var roots = new HashSet<GameObject>();

        foreach (Character character in FindObjectsOfType<Character>(true))
        {
            CollectHealthUiRoots(character.transform, character.Fill_Hp, character.Txt_Hp, roots);
        }

        foreach (Enemy enemy in FindObjectsOfType<Enemy>(true))
        {
            CollectHealthUiRoots(enemy.transform, enemy.Fill_Hp, enemy.Txt_Hp, roots);
            if (enemy.IntentionView != null)
                roots.Add(enemy.IntentionView.gameObject);
            if (enemy.Txt_Intention != null)
                roots.Add(enemy.Txt_Intention.gameObject);
        }

        // 兼容尚未正确绑定到 Enemy.IntentionView 的旧场景对象。
        foreach (IntentionView intention in FindObjectsOfType<IntentionView>(true))
        {
            if (intention == null) continue;
            intention.SetPresentationHidden(true);
            _hiddenIntentionViews.Add(intention);
            roots.Add(intention.gameObject);
        }

        foreach (GameObject root in roots)
        {
            if (root == null) continue;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            bool wasAdded = group == null;
            if (wasAdded)
                group = root.AddComponent<CanvasGroup>();

            _hiddenBattleUi.Add(new CanvasGroupSnapshot
            {
                Group = group,
                WasAdded = wasAdded,
                Alpha = group.alpha,
                Interactable = group.interactable,
                BlocksRaycasts = group.blocksRaycasts
            });

            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }

    private static void CollectHealthUiRoots(
        Transform unitRoot,
        Graphic hpFill,
        Graphic hpText,
        HashSet<GameObject> roots)
    {
        Transform fillTransform = hpFill != null ? hpFill.transform : null;
        Transform textTransform = hpText != null ? hpText.transform : null;
        Transform commonRoot = FindCommonAncestorBelow(fillTransform, textTransform, unitRoot);
        if (commonRoot != null)
        {
            roots.Add(commonRoot.gameObject);
            return;
        }

        // 绑定不完整时只隐藏能找到的血条元素，不能误关整个角色根节点。
        if (hpFill != null) roots.Add(hpFill.gameObject);
        if (hpText != null) roots.Add(hpText.gameObject);
    }

    private static Transform FindCommonAncestorBelow(Transform first, Transform second, Transform boundary)
    {
        if (first == null || second == null)
            return null;

        var firstAncestors = new HashSet<Transform>();
        for (Transform current = first; current != null && current != boundary; current = current.parent)
            firstAncestors.Add(current);

        for (Transform current = second; current != null && current != boundary; current = current.parent)
        {
            if (firstAncestors.Contains(current))
                return current;
        }
        return null;
    }

    private void RestoreBattleStatusUi()
    {
        if (_battleUiHideDepth <= 0)
            return;

        _battleUiHideDepth--;
        if (_battleUiHideDepth > 0)
            return;

        ForceRestoreBattleStatusUi();
    }

    private void ForceRestoreBattleStatusUi()
    {
        _battleUiHideDepth = 0;
        foreach (CanvasGroupSnapshot snapshot in _hiddenBattleUi)
        {
            if (snapshot?.Group == null) continue;
            snapshot.Group.alpha = snapshot.Alpha;
            snapshot.Group.interactable = snapshot.Interactable;
            snapshot.Group.blocksRaycasts = snapshot.BlocksRaycasts;
            if (snapshot.WasAdded)
                Destroy(snapshot.Group);
        }
        _hiddenBattleUi.Clear();

        foreach (IntentionView intention in _hiddenIntentionViews)
        {
            if (intention != null)
                intention.SetPresentationHidden(false);
        }
        _hiddenIntentionViews.Clear();
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

        float frameDuration = 1f / Mathf.Max(1f, shadowAnimationFps);
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
