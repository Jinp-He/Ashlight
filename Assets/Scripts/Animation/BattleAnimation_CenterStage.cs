using UnityEngine;
using Spine.Unity;
using System;
using System.Collections;
using DG.Tweening;
using Ashlight.Battle.Core.Data;
using Scripts.UI;

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

    #endregion

    #region 私有字段

    private Canvas _canvas;

    private const float MOVE_DURATION = 0.3f;     // 入场（移动到中央）时间
    private const float BATTLE_DURATION = 0.9f;   // 中央对峙演出时间
    private const float RETURN_DURATION = 0.3f;   // 退场（移回原位）时间
    private const float CENTER_GAP = 420f;        // 中央对峙时两个单位的间距（Canvas 本地单位）
    private const float INERTIA_DISTANCE = 18f;   // 攻击瞬间施法者朝目标的冲刺距离
    private const float DAMAGE_FLOAT_DURATION = 0.6f;

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
        return MOVE_DURATION + BATTLE_DURATION + RETURN_DURATION;
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

        // 0. 记录原始世界坐标与同级渲染顺序，结束后还原
        Vector3 casterHome = casterRect.position;
        Vector3 targetHome = targetRect.position;
        int casterSibling = casterRect.GetSiblingIndex();
        int targetSibling = targetRect.GetSiblingIndex();

        // 把两个单位提到各自父级最前，避免被同侧其它单位遮挡
        casterRect.SetAsLastSibling();
        targetRect.SetAsLastSibling();

        // 计算中央对峙目标点：玩家恒在左、敌人恒在右，保持朝向一致；Y 保持各自原值
        Vector3 casterStage = ComputeStagePosition(casterState.IsPlayerUnit, casterHome);
        Vector3 targetStage = ComputeStagePosition(targetState.IsPlayerUnit, targetHome);

        // 1. 入场：双方滑到中央
        Tween enterCaster = casterRect.DOMove(casterStage, MOVE_DURATION).SetEase(Ease.OutQuad);
        targetRect.DOMove(targetStage, MOVE_DURATION).SetEase(Ease.OutQuad);
        yield return enterCaster.WaitForCompletion();

        // 2. 攻击者播 attack，目标播 shouji
        PlayCasterAttack(casterUI);
        PlayTargetHurt(targetUI);

        // 3. 伤害数字
        if (damage > 0)
        {
            Vector3 damagePos = GetUnitTopWorldPosition(targetUI);
            if (damagePos != Vector3.zero)
            {
                ShowDamageNumber(damagePos, damage);
            }
        }

        // 4. 受击回调（更新血量等 UI）
        onHit?.Invoke();

        // 5. 攻击惯性：施法者朝目标方向小幅冲刺
        float dir = casterState.IsPlayerUnit ? 1f : -1f; // 玩家在左、朝右冲
        Vector3 lunge = casterStage + new Vector3(INERTIA_DISTANCE * dir, 0f, 0f);
        casterRect.DOMove(lunge, BATTLE_DURATION).SetEase(Ease.OutQuad);

        yield return new WaitForSeconds(BATTLE_DURATION);

        // 6. 退场：双方滑回原位
        Tween backCaster = casterRect.DOMove(casterHome, RETURN_DURATION).SetEase(Ease.InOutQuad);
        targetRect.DOMove(targetHome, RETURN_DURATION).SetEase(Ease.InOutQuad);
        yield return backCaster.WaitForCompletion();

        // 7. 还原渲染顺序
        casterRect.SetSiblingIndex(casterSibling);
        targetRect.SetSiblingIndex(targetSibling);
    }

    #endregion

    #region 私有方法 - 位置计算

    /// <summary>
    /// 计算单位在中央舞台的世界坐标：X 落在屏幕中央左右（玩家左、敌人右），Y 保持原值。
    /// </summary>
    private Vector3 ComputeStagePosition(bool isPlayerUnit, Vector3 home)
    {
        if (_canvas == null)
        {
            return home;
        }

        RectTransform canvasRect = _canvas.transform as RectTransform;
        if (canvasRect == null)
        {
            return home;
        }

        Vector3 centerWorld = canvasRect.TransformPoint(canvasRect.rect.center);
        float scale = canvasRect.lossyScale.x;
        float halfGap = CENTER_GAP * 0.5f * scale;

        float targetX = isPlayerUnit ? centerWorld.x - halfGap : centerWorld.x + halfGap;
        return new Vector3(targetX, home.y, home.z);
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
