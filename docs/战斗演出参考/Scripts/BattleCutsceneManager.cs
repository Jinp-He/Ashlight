using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// 战斗演出预览 · Unity 工程版
// 对应 web 版 BattleCutscene_Preview.html 的全部逻辑。
// 坐标系约定：Canvas 使用 Reference Resolution 1600 x 900（见下方常量 W/H），
// 所有 XY 数值与网页版一致（px）。方块/特效/背景挂在 shakeRoot 下随屏幕震动位移；
// 手牌挂在 handRoot 下（不随震动位移，等价于网页版 DOM 覆盖层）。
// ============================================================================

public enum CutsceneDirection { Player, Enemy }

[RequireComponent(typeof(RectTransform))]
public class BattleCutsceneManager : MonoBehaviour
{
    // ---- 画布常量 ----
    private const float W = 1600f;
    private const float H = 900f;

    // ===================== 演出核心参数 =====================
    [Header("演出方向")]
    public CutsceneDirection direction = CutsceneDirection.Player;   // 我方左→右 / 敌方右→左

    [Header("方块(单位)")]
    public float spacing = 310f;       // A/B 方块中心间距
    public float pairY = 360f;         // 方块对 Y 坐标
    public float blockSize = 240f;     // 方块固定尺寸（240x240，不可调）

    [Header("滑入运动")]
    [Tooltip("方块从屏幕外生成到开始滑入的延迟（秒）")]
    public float spawnDelay = 0.2f;
    [Tooltip("进场/退场统一滑动速度（px/s）")]
    public float slideSpeed = 4000f;
    [Tooltip("慢速区宽度（居中于画面中心）")]
    public float slowWidth = 250f;
    [Tooltip("慢速倍率（越小停留越久）")]
    public float slowFactor = 0.15f;
    [Tooltip("单次循环总时长（秒）")]
    public float maxDuration = 1.5f;

    [Header("插入特效（序列帧）")]
    public bool effectShow = true;
    [Tooltip("特效垂直中心位置 Y")]
    public float effectY = 400f;
    [Tooltip("序列帧播放帧率（越低越慢）")]
    public float effectFps = 15f;
    [Tooltip("14 张序列帧，对应 anim_CutsceneEffect_01..14")]
    public Sprite[] effectFrames;      // 长度应为 14

    [Header("屏幕震动")]
    [Tooltip("震动最大幅度（px）")]
    public float shakeAmp = 3f;
    [Tooltip("震动持续时间（秒），从触发后延迟 shakeDelay 起算")]
    public float shakeDuration = 0.25f;
    [Tooltip("震动延迟（秒），演出触发后等待该时长才开始震动")]
    public float shakeDelay = 0.3f;

    // ===================== 手牌系统参数 =====================
    [Header("手牌系统")]
    public int handCount = 5;
    public float handGap = 10f;        // 手牌间距
    public float compactGap = 5f;      // 紧凑间距
    [Tooltip("抽牌动画时长（秒）")]
    public float handAnimDur = 0.35f;
    [Tooltip("逐张延迟（毫秒）")]
    public float handStaggerMs = 100f;
    [Tooltip("手牌高度（Y，整排纵向位置）")]
    public float handY = 720f;
    [Tooltip("悬停上移量（px）")]
    public float hoverLift = 50f;
    [Tooltip("高亮放大倍数")]
    public float hoverScale = 1.4f;
    public float cardW = 130f;         // 卡牌原始宽度
    public float cardH = 190f;         // 卡牌原始高度
    public Vector2 spawnPoint = new Vector2(35f, 735f);     // 生成点（抽牌起点）
    public Vector2 recyclePoint = new Vector2(1325f, 775f); // 回收点（牌库/打出落点）

    // ===================== 场景引用 =====================
    [Header("场景引用")]
    public RectTransform shakeRoot;    // 背景+方块+特效 的容器（承载震动位移）
    public Image bgImage;              // 背景 Img_BattleBG
    public RectTransform blockA;       // 进攻方 A（红）
    public RectTransform blockB;       // 受击方 B（蓝）
    public Transform effectLayer;      // 特效实例容器
    public GameObject effectPrefab;    // 含 CutsceneEffect + Image 的预制体
    public RectTransform handRoot;     // 手牌容器（不随震动位移）
    public GameObject cardPrefab;      // 含 HandCard + Image + Text 的预制体

    // web 坐标系（0,0=左上，y 向下）→ RectTransform 局部坐标（y 向上）
    private Vector2 ToLocal(Vector2 screen) => new Vector2(screen.x - W / 2f, H / 2f - screen.y);

    // ===================== 运行时状态 =====================
    private float time;
    private bool playing;
    private bool oneShot;
    private List<CutsceneEffect> activeEffects = new List<CutsceneEffect>();
    private List<CutsceneEffect> effectPool = new List<CutsceneEffect>();

    private HandCard[] cards;
    private int hoveredIdx = -1;
    private bool isCompacted;
    private bool handAnimating;

    // ------------------------------------------------------------------
    void Awake()
    {
        // 方块固定尺寸与配色：A=进攻方(红) / B=受击方(蓝)
        blockA.sizeDelta = Vector2.one * blockSize;
        blockB.sizeDelta = Vector2.one * blockSize;
        blockA.GetComponent<Image>().color = new Color(0.85f, 0f, 0f);
        blockB.GetComponent<Image>().color = new Color(0f, 0.4f, 0.8f);
        SetupCards();
        InitIdle();
        DealHand();
    }

    // ===================== 时间轴 =====================
    void Update()
    {
        float dt = Time.deltaTime;

        // 演出时间推进（仅 playing 时）
        if (playing)
        {
            time += dt;
            if (time >= maxDuration)
            {
                if (oneShot)
                {
                    oneShot = false;
                    playing = false;   // 仅播放一次：播完停在起始帧并暂停
                    time = 0f;
                }
                else
                {
                    time = maxDuration; // 循环分支（本项目默认关闭，仅保留结构）
                }
            }
        }

        // 方块位置
        UpdateBlocks();
        // 特效实例（独立时钟，即使演出暂停也继续播完）
        UpdateEffects(dt);
        // 屏幕震动（基于当前活跃特效）
        ApplyShake();
        // 手牌插值
        UpdateCards(dt);
    }

    // 计算 pair 中心 X：分段 进场 → 慢速区 → 退场，进场/退场速度统一 slideSpeed
    float PairXAt(float t)
    {
        int d = direction == CutsceneDirection.Player ? 1 : -1;
        if (t < spawnDelay) return float.NaN;
        float xTime = t - spawnDelay;

        float slowStart = (W - slowWidth) / 2f;
        float slowEnd = (W + slowWidth) / 2f;
        float speed = slideSpeed;
        float slowSpeed = speed * slowFactor;

        float startX = d == 1 ? -spacing : W + spacing;
        float slowEntryX = d == 1 ? slowStart : slowEnd;
        float slowExitX = d == 1 ? slowEnd : slowStart;

        float distToSlow = Mathf.Abs(slowEntryX - startX);
        float entryDur = distToSlow / speed;
        if (xTime < entryDur) return startX + d * speed * xTime;

        xTime -= entryDur;
        float slowDist = Mathf.Abs(slowExitX - slowEntryX);
        float slowDur = slowDist / slowSpeed;
        if (xTime < slowDur) return slowEntryX + d * slowSpeed * xTime;

        xTime -= slowDur;
        return slowExitX + d * speed * xTime;
    }

    void UpdateBlocks()
    {
        float cx = PairXAt(time);
        if (float.IsNaN(cx))
        {
            blockA.gameObject.SetActive(false);
            blockB.gameObject.SetActive(false);
            return;
        }
        blockA.gameObject.SetActive(true);
        blockB.gameObject.SetActive(true);
        // A 恒在 B 左侧（A-B 顺序恒定），仅整体移动方向随阵营翻转
        blockA.anchoredPosition = new Vector2(cx - W / 2f - spacing * 0.5f, H / 2f - pairY);
        blockB.anchoredPosition = new Vector2(cx - W / 2f + spacing * 0.5f, H / 2f - pairY);
    }

    // ===================== 特效实例 =====================
    void UpdateEffects(float dt)
    {
        if (!effectShow) { foreach (var e in activeEffects) e.gameObject.SetActive(false); return; }

        float duration = effectFrames.Length / effectFps;
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            var e = activeEffects[i];
            e.age += dt;
            if (!e.IsAlive(spawnDelay, duration))   // 自然播完才移除（不销毁正在播放的）
            {
                ReturnEffect(e);
                activeEffects.RemoveAt(i);
                continue;
            }
            float local = e.age - spawnDelay;          // 与方块同步：方块出现的瞬间即开始
            if (local < 0) { e.image.enabled = false; continue; }
            int idx = Mathf.FloorToInt(local * effectFps);
            if (idx >= effectFrames.Length) { e.image.enabled = false; continue; }

            e.image.enabled = true;
            e.image.sprite = effectFrames[idx];        // 1→14 按原生速度播放，播完即隐藏
            e.image.SetNativeSize();                   // 保留原生尺寸（不缩放）
            // 敌方进攻：水平翻转（扫击方向反转）。层级在方块下方由 Inspector 父子顺序保证。
            e.root.localScale = e.direction == CutsceneDirection.Enemy
                ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            // 垂直中心对齐 effectY（web 坐标 0=上，Unity 坐标 y 向上 → 取 H/2 - effectY）
            e.root.anchoredPosition = new Vector2(0f, H / 2f - effectY);
        }
    }

    CutsceneEffect GetEffect()
    {
        CutsceneEffect e;
        if (effectPool.Count > 0)
        {
            e = effectPool[effectPool.Count - 1];
            effectPool.RemoveAt(effectPool.Count - 1);
        }
        else
        {
            var go = Instantiate(effectPrefab, effectLayer);
            e = go.GetComponent<CutsceneEffect>();
            e.image = go.GetComponent<Image>();
            e.root = e.image.rectTransform;   // 特效预制体上 CutsceneEffect 与 Image 同 GO
        }
        return e;
    }
    void ReturnEffect(CutsceneEffect e) { e.gameObject.SetActive(false); effectPool.Add(e); }

    // 触发一次完整演出（播完即停在起始帧，不进入自动循环）—— 对应网页 triggerOneShot/playOnce
    public void PlayOnce()
    {
        oneShot = true;
        time = 0f;
        playing = true;
    }

    void SpawnEffect()
    {
        var e = GetEffect();
        e.Reset(direction);
        e.image.enabled = false;
        activeEffects.Add(e);     // 后生成的实例叠在旧实例之上（列表顺序 = 绘制顺序）
    }

    // ===================== 屏幕震动 =====================
    void ApplyShake()
    {
        // 背景按 shakeAmp 外扩，避免位移后露出边缘空隙
        bgImage.rectTransform.sizeDelta = new Vector2(W + 2f * shakeAmp, H + 2f * shakeAmp);

        float amp = 0f;
        float duration = effectFrames.Length / effectFps;
        foreach (var e in activeEffects)
        {
            float elapsed = e.age - shakeDelay;       // 自触发后延迟 shakeDelay 起算
            if (elapsed < 0f || elapsed >= shakeDuration) continue;
            float a = shakeAmp * (1f - elapsed / shakeDuration);
            if (a > amp) amp = a;                     // 取所有活跃实例中剩余力度最大者
        }
        if (amp <= 0f) { shakeRoot.anchoredPosition = Vector2.zero; return; }
        shakeRoot.anchoredPosition = new Vector2(Random.Range(-amp, amp), Random.Range(-amp, amp));
    }

    // ===================== 手牌系统 =====================
    void SetupCards()
    {
        // 清理旧卡（热重载安全）
        if (cards != null) foreach (var c in cards) if (c) Destroy(c.gameObject);
        cards = new HandCard[handCount];
        for (int i = 0; i < handCount; i++)
        {
            var go = Instantiate(cardPrefab, handRoot);
            var c = go.GetComponent<HandCard>();
            c.index = i;
            c.rect.sizeDelta = new Vector2(cardW, cardH);
            if (c.label) c.label.text = "#" + (i + 1);
            c.onHover = (idx) => SetHover(idx);
            c.onPlay = (idx) => PlayCard(idx);
            cards[i] = c;
        }
    }

    // 抽牌序列：从生成点逐张飞入手牌，再自动紧凑
    public void DealHand()
    {
        if (handAnimating) return;
        handAnimating = true;
        hoveredIdx = -1;
        isCompacted = false;
        for (int i = 0; i < handCount; i++)
        {
            var c = cards[i];
            c.state = CardState.Idle;
            c.inHand = false;
            c.targetScale = 0.12f;
            c.targetPos = ToLocal(spawnPoint);
            c.rect.anchoredPosition = ToLocal(spawnPoint);
            c.rect.localScale = Vector3.one * 0.12f;
        }
        StartCoroutine(DealRoutine());
    }

    IEnumerator DealRoutine()
    {
        int drawn = 0;
        while (drawn < handCount)
        {
            var c = cards[drawn];
            c.state = CardState.Hand;
            c.inHand = true;
            LayoutHand();
            drawn++;
            yield return new WaitForSeconds(handStaggerMs / 1000f);
        }
        yield return new WaitForSeconds(0.2f);
        isCompacted = true;
        LayoutHand();
        handAnimating = false;
    }

    // 计算并应用当前手牌布局（仅 inHand 的牌参与，居中排列）
    void LayoutHand()
    {
        var inHand = new List<HandCard>();
        foreach (var c in cards) if (c.inHand) inHand.Add(c);
        int n = inHand.Count;
        if (n == 0) return;
        float gap = isCompacted ? compactGap : handGap;
        float totalW = n * cardW + (n - 1) * gap;
        float startX = (W - totalW) / 2f;
        for (int i = 0; i < n; i++)
        {
            inHand[i].slotPos = ToLocal(new Vector2(startX + i * (cardW + gap), handY));
            if (inHand[i].state != CardState.Played)
            {
                inHand[i].targetPos = inHand[i].slotPos;
                inHand[i].targetScale = 1f;
            }
        }
        ApplyTransforms();
    }

    // 应用悬停/邻居让位（保持被悬停牌两侧间距固定）
    void ApplyTransforms()
    {
        var inHand = new List<HandCard>();
        foreach (var c in cards) if (c.inHand && c.state != CardState.Played) inHand.Add(c);
        float delta = (hoverScale - 1f) * cardW / 2f;     // 放大后每侧溢出的半宽
        int hi = -1;
        for (int i = 0; i < inHand.Count; i++) if (inHand[i].index == hoveredIdx) hi = i;

        for (int i = 0; i < inHand.Count; i++)
        {
            var c = inHand[i];
            Vector2 tx = c.slotPos;
            float sc = 1f;
            if (hi >= 0)                 // 仅在确有悬停牌时才做放大/让位
            {
                // 注意：局部坐标 y 向上，所以"上移"是 +hoverLift
                if (i == hi) { tx.y += hoverLift; sc = hoverScale; }
                else if (i == hi - 1) tx.x -= delta;   // 左邻让位
                else if (i == hi + 1) tx.x += delta;   // 右邻让位
            }
            c.targetPos = tx;
            c.targetScale = sc;
        }
    }

    void SetHover(int idx)
    {
        hoveredIdx = idx;
        ApplyTransforms();
    }

    // 打出一张牌：飞向牌库并触发一次演出
    public void PlayCard(int idx)
    {
        var c = cards[idx];
        if (c == null || c.state != CardState.Hand) return;
        c.state = CardState.Played;
        c.inHand = false;
        c.targetPos = ToLocal(recyclePoint);
        c.targetScale = 0.12f;
        hoveredIdx = -1;
        isCompacted = false;       // 剩余手牌重新按 handGap 展开居中
        LayoutHand();
        SpawnEffect();             // 同时触发一次演出（多实例：新特效叠在旧特效之上）
        PlayOnce();
    }

    void UpdateCards(float dt)
    {
        if (cards == null) return;
        float k = 1f - Mathf.Exp(-dt * (1f / Mathf.Max(handAnimDur, 0.01f)) * 6f); // 平滑插值
        foreach (var c in cards)
        {
            c.rect.anchoredPosition = Vector2.Lerp(c.rect.anchoredPosition, c.targetPos, k);
            float s = Mathf.Lerp(c.rect.localScale.x, c.targetScale, k);
            c.rect.localScale = Vector3.one * s;
        }
    }

    // ===================== 待机初始态 =====================
    void InitIdle()
    {
        time = 0f;
        playing = false;
        oneShot = false;
    }

    // 手动预览一次（与点击手牌等价，只是不飞牌）
    public void PreviewOnce()
    {
        SpawnEffect();
        PlayOnce();
    }
}
