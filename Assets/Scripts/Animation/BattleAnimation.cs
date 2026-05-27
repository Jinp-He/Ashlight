using UnityEngine;
using Spine.Unity;
using System;
using System.Collections;
using DG.Tweening;
using Ashlight.Battle.Core.Data;
using Scripts.UI;

/// <summary>
/// 战斗演出动画组件（原地播放版）
/// 不再把双方抽离到屏幕中央，直接在各自当前位置播放 attack/shouji 动画。
/// 旧版"中央舞台"流程参见 BattleAnimation_CenterStage。
/// </summary>
public class BattleAnimation : MonoBehaviour
{
    #region 序列化字段

    // 旧版用于在中央舞台生成 SkeletonGraphic 副本时定位用，原地播放版不再使用，
    // 但保留字段以避免破坏场景里已有的序列化引用，方便日后切回中央舞台版。
    [Header("位置设置（仅旧版使用，原地播放版不需要）")]
    [Tooltip("角色（玩家）生成位置（仅旧版使用）")]
    public RectTransform CharacterPosition;

    [Tooltip("敌人生成位置（仅旧版使用）")]
    public RectTransform EnemyPosition;

    [Header("预制体")]
    [Tooltip("SkeletonGraphic预制体（仅旧版使用）")]
    public SkeletonGraphic skeletonGraphicPrefab;

    [Tooltip("伤害数字预制体（包含TextMeshProUGUI组件，如果为空则使用动态创建）")]
    public GameObject damageTextPrefab;

    #endregion

    #region 私有字段

    private Canvas _canvas;

    // 原地播放整体加快 1 倍：Spine 动画 TimeScale 设为 2，等待时长也减半。
    private const float SPEED_MULTIPLIER = 2f;
    private const float BATTLE_DURATION = 0.4f;
    private const float DAMAGE_FLOAT_DURATION = 0.5f;

    #endregion

    #region Unity生命周期

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 获取动画总演出时间（用于解算时等待）
    /// </summary>
    public static float GetTotalAnimationDuration()
    {
        return BATTLE_DURATION;
    }

    /// <summary>
    /// 播放战斗演出动画（原地版）
    /// 直接在 caster/target 各自当前位置播放 attack/shouji，不做位移与抽离。
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
        if (casterState == null || targetState == null)
        {
            yield break;
        }

        // 0. 临时把双方 Spine 动画提速，结束后还原，避免影响后续 idle
        SkeletonGraphic casterSkeleton = GetUnitSkeleton(casterUI);
        SkeletonGraphic targetSkeleton = GetUnitSkeleton(targetUI);
        float casterOriginalScale = SetSkeletonTimeScale(casterSkeleton, SPEED_MULTIPLIER);
        float targetOriginalScale = SetSkeletonTimeScale(targetSkeleton, SPEED_MULTIPLIER);

        try
        {
            // 1. 攻击者在原位播放 attack1
            PlayCasterAttack(casterUI);

            // 2. 目标在原位播放 shouji
            PlayTargetHurt(targetUI);

            // 3. 在目标头顶显示伤害数字
            if (damage > 0)
            {
                Vector3 damagePos = GetUnitTopWorldPosition(targetUI);
                if (damagePos != Vector3.zero)
                {
                    ShowDamageNumber(damagePos, damage);
                }
            }

            // 4. 触发受击回调（更新血量等 UI）
            onHit?.Invoke();

            // 5. 等待动画播放完毕
            yield return new WaitForSeconds(BATTLE_DURATION);
        }
        finally
        {
            // 还原 TimeScale，避免后续 idle 一直 2 倍速
            SetSkeletonTimeScale(casterSkeleton, casterOriginalScale);
            SetSkeletonTimeScale(targetSkeleton, targetOriginalScale);
        }
    }

    private static SkeletonGraphic GetUnitSkeleton(MonoBehaviour unitUI)
    {
        if (unitUI == null) return null;
        var character = unitUI as Character;
        if (character != null) return character.Skeleton_Unit;
        var enemy = unitUI as Enemy;
        if (enemy != null) return enemy.Skeleton_Unit;
        return null;
    }

    /// <summary>
    /// 设置 Skeleton 动画的 TimeScale，返回旧值用于稍后还原。
    /// </summary>
    private static float SetSkeletonTimeScale(SkeletonGraphic skeleton, float scale)
    {
        if (skeleton == null || skeleton.AnimationState == null) return 1f;
        float old = skeleton.AnimationState.TimeScale;
        skeleton.AnimationState.TimeScale = scale;
        return old;
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
            // 回退：单位自身 transform 上方一点
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
