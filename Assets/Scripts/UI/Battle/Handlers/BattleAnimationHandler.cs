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
        private struct PendingDamage
        {
            public string TargetId;
            public int HealthDamage;
            public int ArmorDamage;
        }

        // 伤害事件必须逐次保留：同一技能的 AOE 会命中多个目标，多段攻击也可能连续命中同一目标。
        // 不能按 attacker/target 聚合，否则 UI 只能播放最后一个目标或合并后的总伤害。
        private readonly Dictionary<string, Queue<PendingDamage>> _pendingDamageByAttacker =
            new Dictionary<string, Queue<PendingDamage>>();

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
        public void CacheDamage(string attackerId, string targetId, int healthDamage, int armorDamage)
        {
            if (string.IsNullOrEmpty(attackerId) || string.IsNullOrEmpty(targetId))
            {
                return;
            }

            if (!_pendingDamageByAttacker.TryGetValue(attackerId, out Queue<PendingDamage> pendingHits))
            {
                pendingHits = new Queue<PendingDamage>();
                _pendingDamageByAttacker.Add(attackerId, pendingHits);
            }

            pendingHits.Enqueue(new PendingDamage
            {
                TargetId = targetId,
                HealthDamage = healthDamage,
                ArmorDamage = armorDamage
            });
            Debug.Log($"[BattleAnimationHandler] 缓存伤害: {attackerId} -> {targetId}, 血量: {healthDamage}, 护甲: {armorDamage}");
        }

        /// <summary>
        /// 尝试消费缓存的伤害值
        /// </summary>
        /// <param name="attackerId">攻击者ID</param>
        /// <param name="targetId">目标ID</param>
        /// <param name="damage">输出伤害值</param>
        /// <returns>是否成功获取</returns>
        private bool TryConsumePendingDamages(string attackerId, List<PendingDamage> hits)
        {
            if (hits == null || string.IsNullOrEmpty(attackerId)
                || !_pendingDamageByAttacker.TryGetValue(attackerId, out Queue<PendingDamage> pendingHits))
            {
                return false;
            }

            while (pendingHits.Count > 0)
            {
                hits.Add(pendingHits.Dequeue());
            }
            _pendingDamageByAttacker.Remove(attackerId);
            return hits.Count > 0;
        }

        /// <summary>
        /// 检查指定攻击对是否已有待消费伤害
        /// </summary>
        private bool HasPendingDamage(string attackerId)
        {
            return !string.IsNullOrEmpty(attackerId)
                   && _pendingDamageByAttacker.TryGetValue(attackerId, out Queue<PendingDamage> hits)
                   && hits.Count > 0;
        }

        /// <summary>
        /// 清空伤害缓存
        /// </summary>
        public void ClearDamageCache()
        {
            _pendingDamageByAttacker.Clear();
        }

        #endregion

        #region 动画播放

        /// <summary>正在播放的战斗演出数量。每个演出协程入口 +1、SignalAnimationComplete -1。</summary>
        private int _activeAnimations;

        /// <summary>
        /// 是否有战斗演出正在播放。ATB 的回合推进用它做闸门：
        /// 上一个单位的演出没播完，不开下一个单位的回合（保证演出顺序 = 行动顺序）。
        /// </summary>
        public bool IsAnimating => _activeAnimations > 0;

        /// <summary>
        /// 播放战斗演出动画（使用BattleAnimation组件）
        /// </summary>
        /// <param name="evt">卡片执行事件</param>
        /// <returns>协程</returns>
        public IEnumerator PlayBattleAnimation(CardExecutedEvent evt)
        {
            _activeAnimations++;
            Debug.Log($"[BattleAnimationHandler] ▶ 演出开始 {evt.CasterId} → {evt.TargetId} (卡/技能={evt.CardId}, 并发数={_activeAnimations}, t={Time.time:F2})");

            // 获取BattleAnimation组件
            if (_battleAnimationRect == null)
            {
                Debug.LogError("[BattleAnimationHandler] BattleAnimation RectTransform未绑定");
                SignalAnimationComplete();
                yield break;
            }

            // 根据事件标志选择演出方式：
            //   UseCenterStage=true  -> 中央舞台版（玩家执行牌、敌人技能）
            //   UseCenterStage=false -> 原地播放版（迅捷牌等普通打牌）
            IBattleAnimationPlayer battleAnimComponent = null;
            if (evt.UseCenterStage)
            {
                battleAnimComponent = _battleAnimationRect.GetComponent<BattleAnimation_CenterStage>();
                if (battleAnimComponent == null)
                {
                    Debug.LogWarning("[BattleAnimationHandler] 未挂载 BattleAnimation_CenterStage，回退到原地播放版");
                }
            }

            if (battleAnimComponent == null)
            {
                battleAnimComponent = _battleAnimationRect.GetComponent<BattleAnimation>();
            }

            if (battleAnimComponent == null)
            {
                Debug.LogError("[BattleAnimationHandler] 未找到任何 BattleAnimation 组件");
                SignalAnimationComplete();
                yield break;
            }

            // 获取施法者状态和 UI；攻击的实际目标须以 AttackExecutedEvent 为准。
            UnitState casterState = _unitUIManager.FindUnitState(evt.CasterId);

            if (casterState == null)
            {
                Debug.LogError($"[BattleAnimationHandler] 无法找到施法者 UnitState: {evt.CasterId}");
                SignalAnimationComplete();
                yield break;
            }

            MonoBehaviour casterUI = _unitUIManager.FindUnitComponent(evt.CasterId);
            if (casterUI == null)
            {
                Debug.LogWarning($"[BattleAnimationHandler] 无法找到施法者 UI: {evt.CasterId}");
                SignalAnimationComplete();
                yield break;
            }

            var hits = new List<PendingDamage>();
            if (evt.IsAttackCard)
            {
                // 时序修正：
                // CardExecutedEvent 可能先于 AttackExecutedEvent 到达，
                // 最多等待2帧让伤害事件完成缓存，避免伤害数字延后一拍。
                int waitFrames = 2;
                while (waitFrames > 0 && !HasPendingDamage(evt.CasterId))
                {
                    waitFrames--;
                    yield return null;
                }
            }

            TryConsumePendingDamages(evt.CasterId, hits);

            // 未命中任何目标（例如目标分区为空）时仍保留一次原有的施法演出。
            if (hits.Count == 0)
            {
                hits.Add(new PendingDamage { TargetId = evt.TargetId });
            }

            foreach (PendingDamage hit in hits)
            {
                UnitState targetState = _unitUIManager.FindUnitState(hit.TargetId);
                MonoBehaviour targetUI = _unitUIManager.FindUnitComponent(hit.TargetId);
                if (targetState == null || targetUI == null)
                {
                    Debug.LogWarning($"[BattleAnimationHandler] 跳过找不到的受击目标: {hit.TargetId}");
                    continue;
                }

                if (hit.ArmorDamage > 0)
                {
                    ShowFloatingLabel(targetUI.transform.position + new Vector3(-0.35f, 0f, 0f), hit.ArmorDamage.ToString(), Color.gray);
                }

                yield return battleAnimComponent.PlayBattleAnimation(
                    casterState,
                    targetState,
                    casterUI,
                    targetUI,
                    evt.IsAttackCard,
                    hit.HealthDamage,
                    () => UpdateUnitDisplay(targetUI)
                );
            }

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
            _activeAnimations++;

            // 1. 找到攻击者和目标UI对象
            GameObject attackerObj = _unitUIManager.FindUnitObject(evt.AttackerId);
            GameObject targetObj = _unitUIManager.FindUnitObject(evt.TargetId);

            float maxDuration = 1.0f;

            // 2. 应用攻击演出效果：无关角色变暗，有关角色放大
            ApplyAttackPerformanceEffect(attackerObj, targetObj);

            // 3. 同时播放攻击者attack动画和目标shouji动画
            PlayAttackAnimation(attackerObj);

            // 4. 同时播放目标shouji动画 + 伤害数字 + 实时更新血量显示
            if (evt.ArmorDamage > 0)
            {
                ShowFloatingLabel(targetObj.transform.position + new Vector3(-0.35f, 0f, 0f), evt.ArmorDamage.ToString(), Color.gray);
            }
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

        /// <summary>
        /// 在指定世界坐标飘一段文字标签（如 "MISS"），上浮 + 淡出后销毁。
        /// 与 <see cref="ShowDamageNumber"/> 同款动画，但文字与颜色可自定义、不做伤害&gt;0 过滤。
        /// </summary>
        public void ShowFloatingLabel(Vector3 worldPosition, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;

            GameObject labelObj;
            TMPro.TextMeshProUGUI textMesh;
            RectTransform rectTransform;

            if (damageTextPrefab != null)
            {
                labelObj = Instantiate(damageTextPrefab, transform);
                labelObj.transform.position = worldPosition;
                labelObj.name = "FloatingLabel";

                textMesh = labelObj.GetComponent<TMPro.TextMeshProUGUI>();
                if (textMesh == null) textMesh = labelObj.AddComponent<TMPro.TextMeshProUGUI>();

                rectTransform = labelObj.GetComponent<RectTransform>();
                if (rectTransform == null) rectTransform = labelObj.AddComponent<RectTransform>();
            }
            else
            {
                labelObj = new GameObject("FloatingLabel");
                labelObj.transform.SetParent(transform);
                labelObj.transform.position = worldPosition;

                textMesh = labelObj.AddComponent<TMPro.TextMeshProUGUI>();
                textMesh.fontSize = 48;
                textMesh.alignment = TMPro.TextAlignmentOptions.Center;

                rectTransform = labelObj.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(200, 100);
            }

            if (textMesh != null)
            {
                textMesh.text = text;
                textMesh.color = color;
            }

            Sequence seq = DOTween.Sequence();
            seq.Append(rectTransform.DOAnchorPosY(rectTransform.anchoredPosition.y + 100f, 1.0f).SetEase(Ease.OutQuad));
            if (textMesh != null)
                seq.Join(textMesh.DOFade(0f, 1.0f).SetEase(Ease.InQuad));
            seq.OnComplete(() => Destroy(labelObj));

            Debug.Log($"[BattleAnimationHandler] 显示飘字: {text}");
        }

        #endregion

        #region 私有辅助方法

        /// <summary>
        /// 通知战斗管理器动画完成
        /// </summary>
        private void SignalAnimationComplete()
        {
            // 每条演出协程的所有出口（含各早退分支）恰好各调用一次本方法，计数在此归还
            _activeAnimations = Mathf.Max(0, _activeAnimations - 1);
            Debug.Log($"[BattleAnimationHandler] ■ 演出结束 (剩余并发={_activeAnimations}, t={Time.time:F2})");

            if (_battleManager != null)
            {
                _battleManager.SignalAnimationComplete();
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
