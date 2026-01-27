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
/// 战斗演出动画组件
/// 负责播放四步动画序列：准备 -> 入场 -> 战斗 -> 退场
/// </summary>
public class BattleAnimation : MonoBehaviour
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

        if (_rectTransform == null)
        {
            // // Debug.LogError("[BattleAnimation] 缺少RectTransform组件");
        }
        if (_canvas == null)
        {
            // // Debug.LogError("[BattleAnimation] 未找到父Canvas");
        }
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
    /// <param name="casterState">施法者状态</param>
    /// <param name="targetState">目标状态</param>
    /// <param name="casterUI">施法者UI对象（Character或Enemy）</param>
    /// <param name="targetUI">目标UI对象（Character或Enemy）</param>
    /// <param name="isAttackCard">是否是攻击类卡片</param>
    /// <param name="damage">伤害值（用于显示伤害数字）</param>
    /// <param name="onHit">受击回调（用于更新UI）</param>
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
            // // Debug.LogError("[BattleAnimation] casterState或targetState为null");
            yield break;
        }

        // 获取战斗方向：敌人攻击从右往左，玩家攻击从左往右
        bool isEnemyAttack = !casterState.IsPlayerUnit;

        // Debug.Log($"[BattleAnimation] 开始战斗演出: {casterState.UnitId} -> {targetState.UnitId}, 敌人攻击={isEnemyAttack}, 攻击卡片={isAttackCard}");

        // === 步骤A: 准备阶段 ===
        yield return StepA_Prepare(casterState, targetState, casterUI, targetUI);

        // === 步骤B: 入场阶段 ===
        yield return StepB_Enter(isEnemyAttack);

        // === 步骤C: 战斗动画阶段 ===
        yield return StepC_Battle(isAttackCard, isEnemyAttack, targetUI, damage, onHit);

        // === 步骤D: 退场阶段 ===
        yield return StepD_Exit(casterUI, targetUI);

        // Debug.Log("[BattleAnimation] 战斗演出完成");
    }

    #endregion

    #region 私有方法 - 动画步骤

    /// <summary>
    /// 步骤A: 准备阶段
    /// - 隐藏原角色（alpha=0）
    /// - 生成SkeletonGraphic副本到对应位置
    /// </summary>
    private IEnumerator StepA_Prepare(
        UnitState casterState,
        UnitState targetState,
        MonoBehaviour casterUI,
        MonoBehaviour targetUI)
    {
        // Debug.Log("[BattleAnimation] 步骤A: 准备阶段");

        // 清理之前的Skeleton
        ClearSpawnedSkeletons();

        // 1. 隐藏原角色（仅alpha=0）
        SetUnitAlpha(casterUI, 0f);
        SetUnitAlpha(targetUI, 0f);

        // 2. 确定生成位置：根据单位是玩家还是敌人来决定
        RectTransform casterSpawnPos = casterState.IsPlayerUnit ? CharacterPosition : EnemyPosition;
        RectTransform targetSpawnPos = targetState.IsPlayerUnit ? CharacterPosition : EnemyPosition;

        // 3. 按顺序生成：先施法者（技能使用者），后目标（被使用者）
        SpawnSkeleton(casterState, casterSpawnPos, "Caster_Skeleton");
        SpawnSkeleton(targetState, targetSpawnPos, "Target_Skeleton");

        yield return null; // 等待一帧确保生成完成
    }

    /// <summary>
    /// 步骤B: 入场阶段
    /// - 根据战斗方向从屏幕边缘移动到中间
    /// </summary>
    private IEnumerator StepB_Enter(bool isEnemyAttack)
    {
        // Debug.Log($"[BattleAnimation] 步骤B: 入场阶段, 敌人攻击={isEnemyAttack}");

        if (_canvas == null || _rectTransform == null)
        {
            // // Debug.LogError("[BattleAnimation] Canvas或RectTransform为null，跳过入场动画");
            yield break;
        }

        // 获取屏幕宽度
        RectTransform canvasRect = _canvas.GetComponent<RectTransform>();
        float screenWidth = canvasRect.rect.width;

        // 计算两方平分的中间位置
        // CharacterPosition和EnemyPosition之间的中点作为屏幕中心对齐点
        float characterX = CharacterPosition.localPosition.x;
        float enemyX = EnemyPosition.localPosition.x;
        float centerOffset = (characterX + enemyX) / 2f;

        float startX, targetX;

        if (isEnemyAttack)
        {
            // 敌人攻击：从右往左
            startX = screenWidth / 2f + Mathf.Abs(centerOffset) + GetAnimationWidth() / 2f;
            targetX = -centerOffset;
        }
        else
        {
            // 玩家攻击：从左往右
            startX = -screenWidth / 2f - Mathf.Abs(centerOffset) - GetAnimationWidth() / 2f;
            targetX = -centerOffset;
        }

        // Debug.Log($"[BattleAnimation] 移动: {startX} -> {targetX}");

        // 设置初始位置
        _rectTransform.anchoredPosition = new Vector2(startX, _rectTransform.anchoredPosition.y);

        // 移动动画
        yield return _rectTransform
            .DOAnchorPosX(targetX, MOVE_DURATION)
            .SetEase(Ease.OutQuad)
            .WaitForCompletion();
    }

    // 惯性移动距离（像素）
    private const float INERTIA_DISTANCE = 12f;

    /// <summary>
    /// 步骤C: 战斗动画阶段
    /// - 施法者播放attack1
    /// - 被攻击者播放shouji（所有卡片都播放）
    /// - 显示伤害数字（在目标Skeleton头顶位置）
    /// - 整体保持惯性移动
    /// </summary>
    private IEnumerator StepC_Battle(bool isAttackCard, bool isEnemyAttack, MonoBehaviour targetUI, int damage, Action onHit)
    {
        // Debug.Log($"[BattleAnimation] 步骤C: 战斗动画阶段, 攻击卡片={isAttackCard}, 敌人攻击={isEnemyAttack}, 伤害={damage}");

        // 找到生成的Skeleton
        SkeletonGraphic casterSkeleton = FindSkeletonByName("Caster_Skeleton");
        SkeletonGraphic targetSkeleton = FindSkeletonByName("Target_Skeleton");

        // Debug.Log($"[BattleAnimation] 查找Skeleton结果: casterSkeleton={casterSkeleton != null}, targetSkeleton={targetSkeleton != null}");
        // Debug.Log($"[BattleAnimation] _spawnedSkeletons数量: {_spawnedSkeletons.Count}");
        foreach (var s in _spawnedSkeletons)
        {
            // Debug.Log($"[BattleAnimation] Skeleton: name={s?.gameObject.name}, AnimationState={s?.AnimationState != null}");
        }

        // 施法者播放attack1动画
        if (casterSkeleton != null && casterSkeleton.AnimationState != null)
        {
            PlayAnimation(casterSkeleton, "attack1", "施法者");
        }
        else
        {
            // // Debug.LogError($"[BattleAnimation] 施法者Skeleton播放attack1失败: skeleton={casterSkeleton != null}, AnimationState={casterSkeleton?.AnimationState != null}");
        }

        // 目标播放shouji动画
        if (targetSkeleton != null && targetSkeleton.AnimationState != null)
        {
            PlayAnimation(targetSkeleton, "shouji", "目标");
        }
        else
        {
            // // Debug.LogError($"[BattleAnimation] 目标Skeleton播放shouji失败: skeleton={targetSkeleton != null}, AnimationState={targetSkeleton?.AnimationState != null}");
        }

        // 显示伤害数字（在目标Skeleton头顶位置）
        if (damage > 0 && targetSkeleton != null)
        {
            // 获取目标Skeleton的头顶位置（向上偏移一定距离）
            Vector3 damagePos = GetSkeletonTopPosition(targetSkeleton);
            ShowDamageNumber(damagePos, damage);
        }

        onHit?.Invoke();

        // 惯性移动：沿入场方向继续移动一小段距离
        // 敌人攻击从右往左，所以惯性向左（负方向）
        // 玩家攻击从左往右，所以惯性向右（正方向）
        float inertiaDirection = isEnemyAttack ? -1f : 1f;
        float targetX = _rectTransform.anchoredPosition.x + (INERTIA_DISTANCE * inertiaDirection);

        // Debug.Log($"[BattleAnimation] 惯性移动: {_rectTransform.anchoredPosition.x} -> {targetX} (方向={inertiaDirection})");

        // 惯性移动动画（在BATTLE_DURATION期间完成，使用缓出效果模拟减速）
        _rectTransform.DOAnchorPosX(targetX, BATTLE_DURATION).SetEase(Ease.OutQuad);

        // 等待战斗动画时间
        yield return new WaitForSeconds(BATTLE_DURATION);
    }

    /// <summary>
    /// 播放指定动画（统一方法，带详细日志）
    /// 自动检测0帧动画并保持姿势
    /// </summary>
    /// <param name="skeleton">目标Skeleton</param>
    /// <param name="animName">动画名称</param>
    /// <param name="unitName">单位名称（用于日志）</param>
    private void PlayAnimation(SkeletonGraphic skeleton, string animName, string unitName)
    {
        if (skeleton == null || skeleton.AnimationState == null)
        {
            // // Debug.LogError($"[BattleAnimation] {unitName}播放{animName}失败: AnimationState为null");
            return;
        }

        var skeletonData = skeleton.AnimationState.Data?.SkeletonData;
        if (skeletonData == null)
        {
            // // Debug.LogError($"[BattleAnimation] {unitName}播放{animName}失败: SkeletonData为null");
            return;
        }

        // 列出所有可用动画
        var animations = skeletonData.Animations;
        // Debug.Log($"[BattleAnimation] {unitName}可用动画列表 (共{animations.Count}个):");
        foreach (var anim in animations)
        {
            // Debug.Log($"  [BattleAnimation] - {anim.Name} (时长={anim.Duration}秒)");
        }

        var targetAnim = skeletonData.FindAnimation(animName);
        if (targetAnim != null)
        {
            // 检测是否是0帧动画（Duration为0或非常小）
            bool isZeroFrameAnim = targetAnim.Duration <= 0.001f;

            if (isZeroFrameAnim)
            {
                // 0帧动画，设置为循环播放以保持姿势
                skeleton.AnimationState.SetAnimation(0, animName, true);
                // Debug.Log($"[BattleAnimation] {unitName}成功播放{animName}动画 (0帧动画，保持姿势模式，Duration={targetAnim.Duration})");
            }
            else
            {
                // 普通动画，播放完后切换回idle
                skeleton.AnimationState.SetAnimation(0, animName, false);
                skeleton.AnimationState.AddAnimation(0, "idle", true, 0f);
                // Debug.Log($"[BattleAnimation] {unitName}成功播放{animName}动画 (Duration={targetAnim.Duration}秒)");
            }
        }
        else
        {
            // // Debug.LogError($"[BattleAnimation] {unitName}未找到{animName}动画!");
        }
    }

    /// <summary>
    /// 获取Skeleton头顶位置（用于显示伤害数字）
    /// </summary>
    private Vector3 GetSkeletonTopPosition(SkeletonGraphic skeleton)
    {
        if (skeleton == null) return Vector3.zero;

        // 计算头顶位置：基于Skeleton的bounds或固定偏移
        Vector3 worldPos = skeleton.transform.position;

        // 尝试从Skeleton获取实际bounds
        if (skeleton.Skeleton != null)
        {
            // 获取Skeleton的边界（需要传入vertexBuffer）
            float[] vertexBuffer = null;
            skeleton.Skeleton.GetBounds(out float minX, out float minY, out float maxX, out float maxY, ref vertexBuffer);

            // 头顶位置 = 当前位置 + 高度
            float height = maxY - minY;
            worldPos.y += height * skeleton.transform.lossyScale.y;

            // Debug.Log($"[BattleAnimation] Skeleton bounds: minY={minY}, maxY={maxY}, height={height}, worldPos.y={worldPos.y}");
        }
        else
        {
            // 回退：固定向上偏移200像素
            worldPos.y += 200f;
        }

        return worldPos;
    }

    /// <summary>
    /// 步骤D: 退场阶段
    /// - 所有角色淡出，变黑并且alpha变成0
    /// </summary>
    private IEnumerator StepD_Exit(MonoBehaviour casterUI, MonoBehaviour targetUI)
    {
        // Debug.Log("[BattleAnimation] 步骤D: 退场阶段");

        // 对所有生成的Skeleton执行淡出效果（变黑+透明）
        foreach (var skeleton in _spawnedSkeletons)
        {
            if (skeleton == null) continue;

            // 同时变黑和淡出
            Color targetColor = new Color(0, 0, 0, 0); // 黑色且透明

            DOTween.To(
                () => skeleton.color,
                c =>
                {
                    skeleton.color = c;
                    // 同时更新Skeleton的RGBA
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

        // 等待淡出完成
        yield return new WaitForSeconds(FADEOUT_DURATION);

        // 清理生成的Skeleton
        ClearSpawnedSkeletons();

        // 恢复原角色显示
        SetUnitAlpha(casterUI, 1f);
        SetUnitAlpha(targetUI, 1f);
    }

    #endregion

    #region 私有方法 - 辅助

    /// <summary>
    /// 生成SkeletonGraphic副本
    /// </summary>
    private SkeletonGraphic SpawnSkeleton(UnitState unitState, RectTransform parentPosition, string skeletonName)
    {
        if (skeletonGraphicPrefab == null)
        {
            // // Debug.LogError("[BattleAnimation] skeletonGraphicPrefab未设置");
            return null;
        }

        if (parentPosition == null)
        {
            // // Debug.LogError("[BattleAnimation] parentPosition为null");
            return null;
        }

        // 实例化预制体
        SkeletonGraphic skeleton = Instantiate(skeletonGraphicPrefab, parentPosition);
        skeleton.transform.localPosition = Vector3.zero;
        skeleton.transform.localScale = Vector3.one;
        skeleton.gameObject.name = skeletonName;

        // 加载SkeletonDataAsset
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

        // Debug.Log($"[BattleAnimation] 加载Skeleton: name={skeletonName}, configId={unitState.ConfigId}, path={skeletonPath}");

        var skeletonData = Resources.Load<SkeletonDataAsset>(skeletonPath);
        if (skeletonData != null)
        {
            skeleton.skeletonDataAsset = skeletonData;
            skeleton.Initialize(true);

            // 播放idle动画
            if (skeleton.AnimationState != null)
            {
                skeleton.AnimationState.SetAnimation(0, "idle", true);
            }

            // Debug.Log($"[BattleAnimation] 生成Skeleton成功: {skeletonName}, 资源路径: {skeletonPath}");
        }
        else
        {
            // // Debug.LogWarning($"[BattleAnimation] 未找到Skeleton资源: {skeletonPath}");
        }

        _spawnedSkeletons.Add(skeleton);
        return skeleton;
    }

    /// <summary>
    /// 根据名称查找已生成的Skeleton
    /// </summary>
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

    /// <summary>
    /// 清理所有生成的Skeleton
    /// </summary>
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

    /// <summary>
    /// 设置单位的alpha值
    /// </summary>
    private void SetUnitAlpha(MonoBehaviour unitUI, float alpha)
    {
        if (unitUI == null) return;

        Color color = Color.white;
        color.a = alpha;

        // 尝试获取Character组件
        var character = unitUI as Character;
        if (character != null)
        {
            character.SetColor(color);
            return;
        }

        // 尝试获取Enemy组件
        var enemy = unitUI as Enemy;
        if (enemy != null)
        {
            enemy.SetColor(color);
            return;
        }

        // // Debug.LogWarning($"[BattleAnimation] 无法设置单位alpha: {unitUI.name}");
    }

    /// <summary>
    /// 获取动画区域的宽度（用于计算起始位置）
    /// </summary>
    private float GetAnimationWidth()
    {
        if (CharacterPosition == null || EnemyPosition == null)
            return 0f;

        return Mathf.Abs(EnemyPosition.localPosition.x - CharacterPosition.localPosition.x);
    }

    /// <summary>
    /// 显示伤害数字（使用DOTween动画）
    /// </summary>
    private void ShowDamageNumber(Vector3 targetPosition, int damage)
    {
        if (damage <= 0)
        {
            // Debug.Log("[BattleAnimation] 伤害为0，不显示伤害数字");
            return;
        }

        GameObject damageTextObj = null;
        TMPro.TextMeshProUGUI textMesh = null;
        RectTransform rectTransform = null;

        // 优先使用prefab，如果prefab为空则使用动态创建（向后兼容）
        if (damageTextPrefab != null)
        {
            // 使用prefab实例化
            damageTextObj = Instantiate(damageTextPrefab, _canvas.transform);
            damageTextObj.transform.position = targetPosition;
            damageTextObj.name = "BattleDamageText";

            // 获取TextMeshPro组件
            textMesh = damageTextObj.GetComponent<TMPro.TextMeshProUGUI>();
            if (textMesh == null)
            {
                // // Debug.LogWarning("[BattleAnimation] 伤害数字prefab缺少TextMeshProUGUI组件，尝试添加");
                textMesh = damageTextObj.AddComponent<TMPro.TextMeshProUGUI>();
            }

            // 获取RectTransform
            rectTransform = damageTextObj.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                rectTransform = damageTextObj.AddComponent<RectTransform>();
            }
        }
        else
        {
            // 回退到动态创建（向后兼容）
            damageTextObj = new GameObject("BattleDamageText");
            damageTextObj.transform.SetParent(_canvas.transform);
            damageTextObj.transform.position = targetPosition;

            // 添加TextMeshPro组件
            textMesh = damageTextObj.AddComponent<TMPro.TextMeshProUGUI>();
            textMesh.fontSize = 48;
            textMesh.color = Color.red;
            textMesh.alignment = TMPro.TextAlignmentOptions.Center;

            // 设置RectTransform
            rectTransform = damageTextObj.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(200, 100);
        }

        // 设置伤害数值
        if (textMesh != null)
        {
            textMesh.text = damage.ToString();
        }

        // DOTween动画：向上飘动 + 淡出
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

        // Debug.Log($"[BattleAnimation] 显示伤害数字: {damage} (使用{(damageTextPrefab != null ? "Prefab" : "动态创建")})");
    }

    #endregion
}
