using UnityEngine;
using Spine.Unity;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Ashlight.Battle.Core.Data;
using Ashlight.Config;
using Ashlight.Common.Utils;
using Scripts.UI;
using cfg.Enemy;

/// <summary>
/// 【备份版本】中央舞台式战斗演出动画组件
/// 四步动画序列：准备 -> 入场 -> 战斗 -> 退场
/// 会将攻击者与目标抽离到屏幕中央播放，原版本备份，未挂载到任何 GameObject。
/// 当前主用版本是 BattleAnimation（原地播放）。如需切回此方案，把组件换成本类即可。
/// </summary>
public class BattleAnimation_CenterStage : MonoBehaviour
{
    #region 序列化字段

    [Header("位置设置")]
    [Tooltip("角色（玩家）生成位置")]
    public RectTransform CharacterPosition;

    [Tooltip("敌人生成位置")]
    public RectTransform EnemyPosition;

    [Header("预制体")]
    [Tooltip("SkeletonGraphic预制体")]
    public SkeletonGraphic skeletonGraphicPrefab;

    [Tooltip("伤害数字预制体（包含TextMeshProUGUI组件，如果为空则使用动态创建）")]
    public GameObject damageTextPrefab;

    #endregion

    #region 私有字段

    private RectTransform _rectTransform;
    private Canvas _canvas;
    private List<SkeletonGraphic> _spawnedSkeletons = new List<SkeletonGraphic>();

    // 动画参数
    private const float MOVE_DURATION = .3f;       // 移动时间
    private const float BATTLE_DURATION = 1.2f;    // 战斗动画时间
    private const float FADEOUT_DURATION = .3f;   // 淡出时间
    private const float INTERMITTENT_DURATION = 2f;   // 中间时间
    #endregion

    #region Unity生命周期

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 获取动画总演出时间（用于解算时等待）
    /// 包含：移动时间 + 战斗时间 + 淡出时间
    /// </summary>
    public static float GetTotalAnimationDuration()
    {
        return MOVE_DURATION + BATTLE_DURATION + FADEOUT_DURATION;
    }

    /// <summary>
    /// 播放战斗演出动画
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

        bool isEnemyAttack = !casterState.IsPlayerUnit;

        yield return StepA_Prepare(casterState, targetState, casterUI, targetUI);
        yield return StepB_Enter(isEnemyAttack);
        yield return StepC_Battle(isAttackCard, isEnemyAttack, targetUI, damage, onHit);
        yield return StepD_Exit(casterUI, targetUI);
    }

    #endregion

    #region 私有方法 - 动画步骤

    private IEnumerator StepA_Prepare(
        UnitState casterState,
        UnitState targetState,
        MonoBehaviour casterUI,
        MonoBehaviour targetUI)
    {
        ClearSpawnedSkeletons();

        SetUnitAlpha(casterUI, 0f);
        SetUnitAlpha(targetUI, 0f);

        RectTransform casterSpawnPos = casterState.IsPlayerUnit ? CharacterPosition : EnemyPosition;
        RectTransform targetSpawnPos = targetState.IsPlayerUnit ? CharacterPosition : EnemyPosition;

        SpawnSkeleton(casterState, casterSpawnPos, "Caster_Skeleton");
        SpawnSkeleton(targetState, targetSpawnPos, "Target_Skeleton");

        yield return null;
    }

    private IEnumerator StepB_Enter(bool isEnemyAttack)
    {
        if (_canvas == null || _rectTransform == null)
        {
            yield break;
        }

        RectTransform canvasRect = _canvas.GetComponent<RectTransform>();
        float screenWidth = canvasRect.rect.width;

        float characterX = CharacterPosition.localPosition.x;
        float enemyX = EnemyPosition.localPosition.x;
        float centerOffset = (characterX + enemyX) / 2f;

        float startX, targetX;

        if (isEnemyAttack)
        {
            startX = screenWidth / 2f + Mathf.Abs(centerOffset) + GetAnimationWidth() / 2f;
            targetX = -centerOffset;
        }
        else
        {
            startX = -screenWidth / 2f - Mathf.Abs(centerOffset) - GetAnimationWidth() / 2f;
            targetX = -centerOffset;
        }

        _rectTransform.anchoredPosition = new Vector2(startX, _rectTransform.anchoredPosition.y);

        yield return _rectTransform
            .DOAnchorPosX(targetX, MOVE_DURATION)
            .SetEase(Ease.OutQuad)
            .WaitForCompletion();
    }

    private const float INERTIA_DISTANCE = 12f;

    private IEnumerator StepC_Battle(bool isAttackCard, bool isEnemyAttack, MonoBehaviour targetUI, int damage, Action onHit)
    {
        SkeletonGraphic casterSkeleton = FindSkeletonByName("Caster_Skeleton");
        SkeletonGraphic targetSkeleton = FindSkeletonByName("Target_Skeleton");

        if (casterSkeleton != null && casterSkeleton.AnimationState != null)
        {
            PlayAnimation(casterSkeleton, "attack1", "施法者");
        }

        if (targetSkeleton != null && targetSkeleton.AnimationState != null)
        {
            PlayAnimation(targetSkeleton, "shouji", "目标");
        }

        if (damage > 0 && targetSkeleton != null)
        {
            Vector3 damagePos = GetSkeletonTopPosition(targetSkeleton);
            ShowDamageNumber(damagePos, damage);
        }

        onHit?.Invoke();

        float inertiaDirection = isEnemyAttack ? -1f : 1f;
        float targetX = _rectTransform.anchoredPosition.x + (INERTIA_DISTANCE * inertiaDirection);

        _rectTransform.DOAnchorPosX(targetX, BATTLE_DURATION).SetEase(Ease.OutQuad);

        yield return new WaitForSeconds(BATTLE_DURATION);
    }

    private void PlayAnimation(SkeletonGraphic skeleton, string animName, string unitName)
    {
        if (skeleton == null || skeleton.AnimationState == null)
        {
            return;
        }

        var skeletonData = skeleton.AnimationState.Data?.SkeletonData;
        if (skeletonData == null)
        {
            return;
        }

        var targetAnim = skeletonData.FindAnimation(animName);
        if (targetAnim != null)
        {
            bool isZeroFrameAnim = targetAnim.Duration <= 0.001f;

            if (isZeroFrameAnim)
            {
                skeleton.AnimationState.SetAnimation(0, animName, true);
            }
            else
            {
                skeleton.AnimationState.SetAnimation(0, animName, false);
                skeleton.AnimationState.AddAnimation(0, "idle", true, 0f);
            }
        }
    }

    private Vector3 GetSkeletonTopPosition(SkeletonGraphic skeleton)
    {
        if (skeleton == null) return Vector3.zero;

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

    private IEnumerator StepD_Exit(MonoBehaviour casterUI, MonoBehaviour targetUI)
    {
        foreach (var skeleton in _spawnedSkeletons)
        {
            if (skeleton == null) continue;

            Color targetColor = new Color(0, 0, 0, 0);

            DOTween.To(
                () => skeleton.color,
                c =>
                {
                    skeleton.color = c;
                    if (skeleton.Skeleton != null)
                    {
                        skeleton.Skeleton.R = c.r;
                        skeleton.Skeleton.G = c.g;
                        skeleton.Skeleton.B = c.b;
                        skeleton.Skeleton.A = c.a;
                    }
                },
                targetColor,
                FADEOUT_DURATION
            );
        }

        yield return new WaitForSeconds(FADEOUT_DURATION);

        ClearSpawnedSkeletons();

        SetUnitAlpha(casterUI, 1f);
        SetUnitAlpha(targetUI, 1f);
    }

    #endregion

    #region 私有方法 - 辅助

    private SkeletonGraphic SpawnSkeleton(UnitState unitState, RectTransform parentPosition, string skeletonName)
    {
        if (skeletonGraphicPrefab == null)
        {
            return null;
        }

        if (parentPosition == null)
        {
            return null;
        }

        SkeletonGraphic skeleton = Instantiate(skeletonGraphicPrefab, parentPosition);
        skeleton.transform.localPosition = Vector3.zero;
        skeleton.transform.localScale = Vector3.one;
        skeleton.gameObject.name = skeletonName;

        string skeletonPath;
        if (unitState.IsPlayerUnit)
        {
            skeletonPath = AssetPath.GetSkeletonAssetPath(unitState.ConfigId);
        }
        else
        {
            EnemyInfo enemyInfo = ConfigLoader.Tables?.TbEnemyInfo?.GetOrDefault(unitState.ConfigId);
            string enemyId = enemyInfo?.AlternativePath;
            skeletonPath = AssetPath.GetEnemySkeletonAssetPath(enemyId);
        }

        var skeletonData = Resources.Load<SkeletonDataAsset>(skeletonPath);
        if (skeletonData != null)
        {
            skeleton.skeletonDataAsset = skeletonData;
            skeleton.Initialize(true);

            if (skeleton.AnimationState != null)
            {
                skeleton.AnimationState.SetAnimation(0, "idle", true);
            }
        }

        _spawnedSkeletons.Add(skeleton);
        return skeleton;
    }

    private SkeletonGraphic FindSkeletonByName(string name)
    {
        foreach (var skeleton in _spawnedSkeletons)
        {
            if (skeleton != null && skeleton.gameObject.name == name)
            {
                return skeleton;
            }
        }
        return null;
    }

    private void ClearSpawnedSkeletons()
    {
        foreach (var skeleton in _spawnedSkeletons)
        {
            if (skeleton != null)
            {
                Destroy(skeleton.gameObject);
            }
        }
        _spawnedSkeletons.Clear();
    }

    private void SetUnitAlpha(MonoBehaviour unitUI, float alpha)
    {
        if (unitUI == null) return;

        Color color = Color.white;
        color.a = alpha;

        var character = unitUI as Character;
        if (character != null)
        {
            character.SetColor(color);
            return;
        }

        var enemy = unitUI as Enemy;
        if (enemy != null)
        {
            enemy.SetColor(color);
            return;
        }
    }

    private float GetAnimationWidth()
    {
        if (CharacterPosition == null || EnemyPosition == null)
            return 0f;

        return Mathf.Abs(EnemyPosition.localPosition.x - CharacterPosition.localPosition.x);
    }

    private void ShowDamageNumber(Vector3 targetPosition, int damage)
    {
        if (damage <= 0)
        {
            return;
        }

        GameObject damageTextObj = null;
        TMPro.TextMeshProUGUI textMesh = null;
        RectTransform rectTransform = null;

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
            rectTransform.DOAnchorPosY(rectTransform.anchoredPosition.y + 100f, 1.0f)
                .SetEase(Ease.OutQuad)
        );
        if (textMesh != null)
        {
            damageSequence.Join(
                textMesh.DOFade(0f, 1.0f).SetEase(Ease.InQuad)
            );
        }
        damageSequence.OnComplete(() => Destroy(damageTextObj));
    }

    #endregion
}
