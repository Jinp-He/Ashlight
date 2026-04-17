using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using Ashlight.Battle;
using Ashlight.Battle.Core.Data;
using Ashlight.Common.Events;

namespace Scripts.UI
{
    /// <summary>
    /// 战斗动画处理器
    /// 负责管理战斗动画播放、伤害数字显示和视觉效果
    /// </summary>
    public class BattleAnimationHandler : MonoBehaviour
    {
        #region 序列化字段

        [Header("伤害数字设置")]
        [SerializeField]
        [Tooltip("伤害数字预制体（包含TextMeshProUGUI组件）")]
        private GameObject damageTextPrefab;

        #endregion

        #region 私有字段

        /// <summary>
        /// 待处理的伤害缓存：attackerId->targetId -> damage
        /// </summary>
        private Dictionary<string, int> _pendingDamageByPair = new Dictionary<string, int>();

        /// <summary>
        /// 战斗管理器引用
        /// </summary>
        private BattleManager _battleManager;

        /// <summary>
        /// 单位UI管理器引用
        /// </summary>
        private BattleUnitUIManager _unitUIManager;

        /// <summary>
        /// BattleAnimation组件引用
        /// </summary>
        private RectTransform _battleAnimationRect;

        /// <summary>
        /// 更新所有单位显示的回调
        /// </summary>
        private Action _updateAllUnitsCallback;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化动画处理器
        /// </summary>
        /// <param name="battleManager">战斗管理器</param>
        /// <param name="unitUIManager">单位UI管理器</param>
        /// <param name="battleAnimationRect">BattleAnimation的RectTransform</param>
        /// <param name="updateAllUnitsCallback">更新所有单位显示的回调</param>
        public void Initialize(
            BattleManager battleManager,
            BattleUnitUIManager unitUIManager,
            RectTransform battleAnimationRect,
            Action updateAllUnitsCallback)
        {
            _battleManager = battleManager;
            _unitUIManager = unitUIManager;
            _battleAnimationRect = battleAnimationRect;
            _updateAllUnitsCallback = updateAllUnitsCallback;
        }

        /// <summary>
        /// 设置伤害数字预制体
        /// </summary>
        /// <param name="prefab">预制体</param>
        public void SetDamageTextPrefab(GameObject prefab)
        {
            damageTextPrefab = prefab;
        }

        #endregion

        #region 伤害缓存管理

        /// <summary>
        /// 缓存伤害值
        /// </summary>
        /// <param name="attackerId">攻击者ID</param>
        /// <param name="targetId">目标ID</param>
        /// <param name="damage">伤害值</param>
        public void CacheDamage(string attackerId, string targetId, int damage)
        {
            string key = GetAttackPairKey(attackerId, targetId);
            if (_pendingDamageByPair.ContainsKey(key))
            {
                _pendingDamageByPair[key] += damage;
            }
            else
            {
                _pendingDamageByPair[key] = damage;
            }
            Debug.Log($"[BattleAnimationHandler] 缓存伤害: {attackerId} -> {targetId}, 伤害: {damage}");
        }

        /// <summary>
        /// 尝试消费缓存的伤害值
        /// </summary>
        /// <param name="attackerId">攻击者ID</param>
        /// <param name="targetId">目标ID</param>
        /// <param name="damage">输出伤害值</param>
        /// <returns>是否成功获取</returns>
        public bool TryConsumePendingDamage(string attackerId, string targetId, out int damage)
        {
            string key = GetAttackPairKey(attackerId, targetId);
            if (_pendingDamageByPair.TryGetValue(key, out damage))
            {
                _pendingDamageByPair.Remove(key);
                return true;
            }
            damage = 0;
            return false;
        }

        /// <summary>
        /// 检查指定攻击对是否已有待消费伤害
        /// </summary>
        private bool HasPendingDamage(string attackerId, string targetId)
        {
            string key = GetAttackPairKey(attackerId, targetId);
            return _pendingDamageByPair.ContainsKey(key);
        }

        /// <summary>
        /// 获取攻击对的key
        /// </summary>
        private string GetAttackPairKey(string attackerId, string targetId)
        {
            return $"{attackerId}->{targetId}";
        }

        /// <summary>
        /// 清空伤害缓存
        /// </summary>
        public void ClearDamageCache()
        {
            _pendingDamageByPair.Clear();
        }

        #endregion

        #region 动画播放

        /// <summary>
        /// 播放战斗演出动画（使用BattleAnimation组件）
        /// </summary>
        /// <param name="evt">卡片执行事件</param>
        /// <returns>协程</returns>
        public IEnumerator PlayBattleAnimation(CardExecutedEvent evt)
        {
            // 获取BattleAnimation组件
            if (_battleAnimationRect == null)
            {
                Debug.LogError("[BattleAnimationHandler] BattleAnimation RectTransform未绑定");
                SignalAnimationComplete();
                yield break;
            }

            var battleAnimComponent = _battleAnimationRect.GetComponent<BattleAnimation>();
            if (battleAnimComponent == null)
            {
                Debug.LogError("[BattleAnimationHandler] BattleAnimation组件未找到");
                SignalAnimationComplete();
                yield break;
            }

            // 获取施法者和目标的UnitState
            UnitState casterState = _unitUIManager.FindUnitState(evt.CasterId);
            UnitState targetState = _unitUIManager.FindUnitState(evt.TargetId);

            if (casterState == null || targetState == null)
            {
                Debug.LogError($"[BattleAnimationHandler] 无法找到UnitState: {evt.CasterId} 或 {evt.TargetId}");
                SignalAnimationComplete();
                yield break;
            }

            // 获取对应的UI组件
            MonoBehaviour casterUI = _unitUIManager.FindUnitComponent(evt.CasterId);
            MonoBehaviour targetUI = _unitUIManager.FindUnitComponent(evt.TargetId);

            if (casterUI == null || targetUI == null)
            {
                Debug.LogWarning($"[BattleAnimationHandler] 无法找到UI组件: {evt.CasterId} 或 {evt.TargetId}");
                SignalAnimationComplete();
                yield break;
            }

            // 获取缓存的伤害值
            int damage = 0;
            if (evt.IsAttackCard)
            {
                // 时序修正：
                // CardExecutedEvent 可能先于 AttackExecutedEvent 到达，
                // 最多等待2帧让伤害事件完成缓存，避免伤害数字延后一拍。
                int waitFrames = 2;
                while (waitFrames > 0 && !HasPendingDamage(evt.CasterId, evt.TargetId))
                {
                    waitFrames--;
                    yield return null;
                }
            }

            if (TryConsumePendingDamage(evt.CasterId, evt.TargetId, out damage))
            {
                Debug.Log($"[BattleAnimationHandler] 获取到缓存的伤害值: {damage}");
            }

            // 播放战斗演出动画
            yield return battleAnimComponent.PlayBattleAnimation(
                casterState,
                targetState,
                casterUI,
                targetUI,
                evt.IsAttackCard,
                damage,
                () => {
                    // 受击回调：更新目标血量显示
                    UpdateUnitDisplay(targetUI);
                }
            );

            // 更新所有单位的UI显示
            _updateAllUnitsCallback?.Invoke();

            // 通知动画完成
            SignalAnimationComplete();
        }

        /// <summary>
        /// 播放攻击动画序列（遗留方法，用于直接攻击动画）
        /// </summary>
        /// <param name="evt">攻击执行事件</param>
        /// <returns>协程</returns>
        public IEnumerator PlayAttackAnimationSequence(AttackExecutedEvent evt)
        {
            // 1. 找到攻击者和目标UI对象
            GameObject attackerObj = _unitUIManager.FindUnitObject(evt.AttackerId);
            GameObject targetObj = _unitUIManager.FindUnitObject(evt.TargetId);

            float maxDuration = 1.0f;

            // 2. 应用攻击演出效果：无关角色变暗，有关角色放大
            ApplyAttackPerformanceEffect(attackerObj, targetObj);

            // 3. 同时播放攻击者attack动画和目标shouji动画
            PlayAttackAnimation(attackerObj);

            // 4. 同时播放目标shouji动画 + 伤害数字 + 实时更新血量显示
            PlayHitAnimation(targetObj, evt.ActualDamage);

            // 5. 等待所有动画完成
            yield return new WaitForSeconds(Mathf.Max(maxDuration, 1.0f));

            // 6. 恢复所有角色的视觉效果
            RestoreAllUnitsVisualEffect();

            // 7. 通知动画完成
            SignalAnimationComplete();
            Debug.Log("[BattleAnimationHandler] 攻击动画序列完成");
        }

        #endregion

        #region 视觉效果

        /// <summary>
        /// 应用攻击演出效果（无关角色变黑，有关角色放大）
        /// </summary>
        /// <param name="attackerObj">攻击者对象</param>
        /// <param name="targetObj">目标对象</param>
        public void ApplyAttackPerformanceEffect(GameObject attackerObj, GameObject targetObj)
        {
            // 遍历所有玩家角色
            foreach (var character in _unitUIManager.PlayerCharacters)
            {
                if (character == null || character.gameObject == null) continue;

                bool isRelated = (attackerObj != null && character.gameObject == attackerObj) ||
                                 (targetObj != null && character.gameObject == targetObj);

                if (isRelated)
                {
                    character.SetScale(1.2f);
                    character.SetColor(Color.white);
                }
                else
                {
                    character.SetColor(Color.black);
                    character.SetScale(1.0f);
                }
            }

            // 遍历所有敌人
            foreach (var enemy in _unitUIManager.Enemies)
            {
                if (enemy == null || enemy.gameObject == null) continue;

                bool isRelated = (attackerObj != null && enemy.gameObject == attackerObj) ||
                                 (targetObj != null && enemy.gameObject == targetObj);

                if (isRelated)
                {
                    enemy.SetScale(1.2f);
                    enemy.SetColor(Color.white);
                }
                else
                {
                    enemy.SetColor(Color.black);
                    enemy.SetScale(1.0f);
                }
            }

            Debug.Log("[BattleAnimationHandler] 应用攻击演出效果");
        }

        /// <summary>
        /// 恢复所有单位的视觉效果（颜色和缩放）
        /// </summary>
        public void RestoreAllUnitsVisualEffect()
        {
            foreach (var character in _unitUIManager.PlayerCharacters)
            {
                if (character != null)
                {
                    character.SetColor(Color.white);
                    character.SetScale(1.0f);
                }
            }

            foreach (var enemy in _unitUIManager.Enemies)
            {
                if (enemy != null)
                {
                    enemy.SetColor(Color.white);
                    enemy.SetScale(1.0f);
                }
            }

            Debug.Log("[BattleAnimationHandler] 恢复所有单位视觉效果");
        }

        #endregion

        #region 伤害数字

        /// <summary>
        /// 显示伤害数字（使用DOTween动画）
        /// </summary>
        /// <param name="targetPosition">目标位置</param>
        /// <param name="damage">伤害值</param>
        public void ShowDamageNumber(Vector3 targetPosition, int damage)
        {
            if (damage <= 0)
            {
                Debug.Log("[BattleAnimationHandler] 伤害为0，不显示伤害数字");
                return;
            }

            GameObject damageTextObj = null;
            TMPro.TextMeshProUGUI textMesh = null;
            RectTransform rectTransform = null;

            // 优先使用prefab
            if (damageTextPrefab != null)
            {
                damageTextObj = Instantiate(damageTextPrefab, transform);
                damageTextObj.transform.position = targetPosition;
                damageTextObj.name = "DamageText";

                textMesh = damageTextObj.GetComponent<TMPro.TextMeshProUGUI>();
                if (textMesh == null)
                {
                    Debug.LogWarning("[BattleAnimationHandler] 伤害数字prefab缺少TextMeshProUGUI组件");
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
                // 动态创建
                damageTextObj = new GameObject("DamageText");
                damageTextObj.transform.SetParent(transform);
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

            // DOTween动画
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

            Debug.Log($"[BattleAnimationHandler] 显示伤害数字: {damage}");
        }

        #endregion

        #region 私有辅助方法

        /// <summary>
        /// 通知战斗管理器动画完成
        /// </summary>
        private void SignalAnimationComplete()
        {
            if (_battleManager != null)
            {
                _battleManager.SignalAnimationComplete();
                Debug.Log("[BattleAnimationHandler] 已发送动画完成信号");
            }
        }

        /// <summary>
        /// 更新单位UI显示
        /// </summary>
        private void UpdateUnitDisplay(MonoBehaviour unitUI)
        {
            if (unitUI == null) return;

            var character = unitUI.GetComponent<Character>();
            var enemy = unitUI.GetComponent<Enemy>();

            if (character != null)
            {
                var unitState = character.GetUnitState();
                if (unitState != null)
                {
                    character.UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                    character.UpdateShield(unitState.Defense);
                }
            }
            else if (enemy != null)
            {
                var unitState = enemy.GetUnitState();
                if (unitState != null)
                {
                    enemy.UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                    enemy.UpdateShield(unitState.Defense);
                }
            }
        }

        /// <summary>
        /// 播放攻击动画
        /// </summary>
        private void PlayAttackAnimation(GameObject attackerObj)
        {
            if (attackerObj == null)
            {
                Debug.LogWarning("[BattleAnimationHandler] 攻击者对象为空");
                return;
            }

            var character = attackerObj.GetComponent<Character>();
            var enemy = attackerObj.GetComponent<Enemy>();

            if (character != null)
            {
                character.PlayAttackAnimation();
            }
            else if (enemy != null)
            {
                enemy.PlayAttackAnimation();
            }
        }

        /// <summary>
        /// 播放受击动画并显示伤害
        /// </summary>
        private void PlayHitAnimation(GameObject targetObj, int damage)
        {
            if (targetObj == null)
            {
                Debug.LogWarning("[BattleAnimationHandler] 目标对象为空");
                return;
            }

            var character = targetObj.GetComponent<Character>();
            var enemy = targetObj.GetComponent<Enemy>();

            if (character != null)
            {
                character.PlayShoujiAnimation();
                ShowDamageNumber(targetObj.transform.position, damage);

                var unitState = character.GetUnitState();
                if (unitState != null)
                {
                    character.UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                    character.UpdateShield(unitState.Defense);
                }
            }
            else if (enemy != null)
            {
                enemy.PlayShoujiAnimation();
                ShowDamageNumber(targetObj.transform.position, damage);

                var unitState = enemy.GetUnitState();
                if (unitState != null)
                {
                    enemy.UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                    enemy.UpdateShield(unitState.Defense);
                }
            }
        }

        #endregion
    }
}
