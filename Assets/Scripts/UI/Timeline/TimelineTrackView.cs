using System.Collections.Generic;
using UnityEngine;
using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Engine;
using Ashlight.Config;
using Ashlight.State.Runtime;
using cfg.Character;
using cfg.Enemy;
using Scripts.UI;
using UnityEngine.UI;
using Ashlight.Common.Utils;
namespace Scripts.UI.Timeline
{
    /// <summary>
    /// 时间轴轨道视图
    /// 管理完整的15格时间轴和卡牌标记
    /// 负责数据绑定和UI更新
    /// </summary>
    public class TimelineTrackView : MonoBehaviour
    {
        #region 序列化字段
        
        [Header("预制体")]
        [SerializeField] 
        [Tooltip("TimelineSlotView预制体")]
        private GameObject slotPrefab;
        
        [SerializeField] 
        [Tooltip("CardTimeSlot预制体")]
        private GameObject cardTimeSlotPrefab;

        [SerializeField] 
        [Tooltip("EnemyTimeSlot预制体")]
        private GameObject enemyTimeSlotPrefab;

        [Header("容器")]
        [SerializeField] 
        [Tooltip("格子容器")]
        private Transform slotsContainer;

        
        [SerializeField] 
        [Tooltip("标记容器")]
        private Transform markersContainer;
        [SerializeField] 
        [Tooltip("角色图标")]
        private Image Icon;    
        [SerializeField] 
        [Tooltip("敌人图标")]
        private Sprite EnemyIcon;    
        [Header("格子设置")]
        [SerializeField] 
        [Tooltip("格子宽度")]
        private float slotWidth = 95f;
        
        [SerializeField] 
        [Tooltip("格子间距")]
        private float slotSpacing = 5f;
        
        #endregion
        
        #region 私有字段
        
        private TimelineTrack _track;                               // 数据源
        private UnitState _unitState;                               // 关联的单位（玩家）
        private bool _isSharedEnemyTrack;                           // 是否为敌人共享轨道
        
        private TimelineSlotView[] _slots = new TimelineSlotView[TimelineTrack.TrackLength];
        private List<CardViewController> _placedCards = new List<CardViewController>(); // 已放置的卡牌
        private List<EnemyTimeSlot> _enemyTimeSlots = new List<EnemyTimeSlot>(); // 敌人时间槽列表（仅用于敌人时间轴）
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 初始化玩家角色时间轴
        /// </summary>
        /// <param name="unitState">玩家角色单位状态</param>
        public void Initialize(UnitState unitState)
        {
            if (unitState == null)
            {
                // // Debug.LogError("[TimelineTrackView] UnitState为null");
                return;
            } 
            
            _unitState = unitState;
            _track = unitState.Track;
            _isSharedEnemyTrack = false;
            
            if (_track == null)
            {
                // // Debug.LogError($"[TimelineTrackView] UnitState.Track为null (UnitId: {unitState.UnitId})");
                return;
            }
            // 使用 TimelineTrack 中记录的角色ID
            if (_track != null && _track.OwnerCharacterId.HasValue)
            {
                var characterId = _track.OwnerCharacterId.Value;
                Sprite iconSprite = Resources.Load<Sprite>(AssetPath.GetCharacterIconAssetPath(characterId.ToString()));
                if (iconSprite != null)
                {
                    Icon.sprite = iconSprite;
                }
                else
                {
                    // // Debug.LogWarning($"[TimelineTrackView] 未找到角色图标: {characterId}");
                }
            }
            else
            {
                // 如果没有角色ID，显示敌人图标
                Icon.sprite = EnemyIcon;
            }
            CreateSlots();
            EnsureContainerLayering();
            RefreshDisplay();
            
            // Debug.Log($"[TimelineTrackView] 玩家时间轴初始化完成: {unitState.UnitId}");
        }
        
        /// <summary>
        /// 初始化敌人共享时间轴
        /// </summary>
        /// <param name="sharedTrack">敌人共享时间轴数据</param>
        public void InitializeShared(TimelineTrack sharedTrack)
        {
            // Debug.Log($"[TimelineTrackView] InitializeShared 开始调用");

            if (sharedTrack == null)
            {
                // // Debug.LogError("[TimelineTrackView] SharedTrack为null");
                return;
            }

            _track = sharedTrack;
            _isSharedEnemyTrack = true;
            _unitState = null;

            // Debug.Log($"[TimelineTrackView] 敌人共享时间轴数据已设置，准备创建格子");

            CreateSlots();
            EnsureContainerLayering();

            // Debug.Log($"[TimelineTrackView] 格子已创建，准备调用RefreshDisplay");
            RefreshDisplay();

            Icon.sprite = EnemyIcon;

            // Debug.Log($"[TimelineTrackView] 敌人共享时间轴初始化完成");
        }
        
        /// <summary>
        /// 刷新显示（根据已放置的卡牌更新格子状态）
        /// </summary>
        public void RefreshDisplay()
        {
            if (_track == null)
            {
                // // Debug.LogError("[TimelineTrackView] Track为null，无法刷新显示");
                return;
            }
            
            // Debug.Log($"[TimelineTrackView] 刷新显示...");
            
            // 重置所有格子占用状态
            foreach (var slot in _slots)
            {
                if (slot != null)
                    slot.SetOccupied(false);
            }
            
            // 如果是敌人时间轴，清除旧的 EnemyTimeSlots
            // 注意：不再自动创建，改为通过事件驱动创建
            if (_isSharedEnemyTrack)
            {
                // Debug.Log($"[TimelineTrackView] RefreshDisplay - 这是敌人时间轴，清除旧的EnemyTimeSlots");
                ClearEnemyTimeSlots();
                // 移除自动创建逻辑，改为由事件驱动
                // CreateEnemyTimeSlots(); // 旧逻辑
            }
            
            // 根据数据标记格子占用状态
            int i = 0;
            while (i < TimelineTrack.TrackLength)
            {
                var block = _track.Blocks[i];
                if (block != null && !block.IsEmpty())
                {
                    // 标记格子为占用并跳过这张卡牌占用的所有格子
                    int skipCount = GetCardSlotCount(block.SourceCardId);
                    for (int j = 0; j < skipCount && i + j < TimelineTrack.TrackLength; j++)
                    {
                        if (_slots[i + j] != null)
                            _slots[i + j].SetOccupied(true);
                    }
                    i += skipCount;
                }
                else
                {
                    i++;
                }
            }
            
            // Debug.Log($"[TimelineTrackView] 刷新显示完成，已放置卡牌数: {_placedCards.Count}");
        }
        
        /// <summary>
        /// 卡牌被放置的回调（由TimelineSlotView调用）
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <param name="slotIndex">放置的格子索引</param>
        /// <param name="ownerId">所属角色ID</param>
        /// <param name="targetId">目标ID</param>
        /// <param name="cardViewController">要转移的整个CardViewController</param>
        public void OnCardDropped(string cardId, int slotIndex, string ownerId, string targetId, CardViewController cardViewController)
        {
            // Debug.Log($"[TimelineTrackView] 收到卡牌放置通知: {cardId} at 索引 {slotIndex}, slotWidth={slotWidth}, slotSpacing={slotSpacing}, 预期位置={slotIndex * (slotWidth + slotSpacing)}");
            
            // 获取卡牌配置
            var cardInfo = ConfigLoader.Tables.TbCardInfo.GetOrDefault(cardId);
            if (cardInfo == null)
            {
                // // Debug.LogError($"[TimelineTrackView] 未找到卡牌配置: {cardId}");
                return;
            }
            
            // 检查是否可以放置
            int totalSlots = cardInfo.Channeling + cardInfo.Duration + cardInfo.Recoil;
            
            // 检查位置
            bool canPlaceByPosition = _track.CanPlaceCard(slotIndex, totalSlots);
            
            // 检查角色匹配
            bool canPlaceByCharacter = true;
            if (_track.IsEnemyTrack)
            {
                // 敌人时间轴：不允许玩家卡牌放置
                canPlaceByCharacter = false;
                // // Debug.LogWarning($"[TimelineTrackView] 无法在索引 {slotIndex} 放置卡牌 {cardInfo.Name}，敌人时间轴不允许放置玩家卡牌");
            }
            else if (_track.IsPlayerTrack && _track.OwnerCharacterId.HasValue)
            {
                // 玩家角色时间轴：检查卡牌的 BelongTo 是否匹配
                canPlaceByCharacter = cardInfo.BelongTo == _track.OwnerCharacterId.Value;
                if (!canPlaceByCharacter)
                {
                    // // Debug.LogWarning($"[TimelineTrackView] 无法在索引 {slotIndex} 放置卡牌 {cardInfo.Name}，角色不匹配: 卡牌属于 {cardInfo.BelongTo}，时间轴属于 {_track.OwnerCharacterId.Value}");
                }
            }
            
            if (!canPlaceByPosition)
            {
                // // Debug.LogWarning($"[TimelineTrackView] 无法在索引 {slotIndex} 放置卡牌 {cardInfo.Name}，格子已被占用或超出范围");
                return;
            }
            
            if (!canPlaceByCharacter)
            {
                // // Debug.LogWarning($"[TimelineTrackView] 无法在索引 {slotIndex} 放置卡牌 {cardInfo.Name}，角色不匹配");
                return;
            }
            
            // 转换卡牌为TimelineBlock列表
            var converter = new CardToTimelineConverter();
            var blocks = converter.ConvertCard(cardInfo, ownerId, targetId);
            
            if (blocks == null || blocks.Count == 0)
            {
                // // Debug.LogError($"[TimelineTrackView] 卡牌转换失败: {cardId}");
                return;
            }
            
            // 放置到TimelineTrack
            if (_track.PlaceCard(slotIndex, blocks))
            {
                // Debug.Log($"[TimelineTrackView] 成功放置卡牌 {cardInfo.Name} 到索引 {slotIndex}");
                
            // 转移整个 CardViewController 到时间轴
            TransferCardViewController(cardViewController, slotIndex, cardInfo);
            
            // 设置 Card 的状态为 OnTime，并设置时间轴信息
            cardViewController.SetTimelineInfo(this, slotIndex);
            
            // 标记格子为占用
            for (int j = 0; j < totalSlots && slotIndex + j < TimelineTrack.TrackLength; j++)
            {
                if (_slots[slotIndex + j] != null)
                    _slots[slotIndex + j].SetOccupied(true);
            }
                
                // 发布卡牌放置事件，触发预解算
                Ashlight.Common.Events.GameEvent.Publish(new Ashlight.Common.Events.CardPlacedEvent
                {
                    CardId = cardId,
                    OwnerId = ownerId,
                    TargetId = targetId
                });

                // 从手牌移到InPlayPile（正在使用的卡牌）
                var battleManager = Ashlight.Battle.BattleManager.Instance;
                if (battleManager != null && battleManager.CurrentState != null && battleManager.CurrentState.DeckSystem != null)
                {
                    // 使用 InstanceId 从数据层手牌移到 InPlayPile
                    string instanceId = cardViewController.InstanceId;
                    if (!string.IsNullOrEmpty(instanceId))
                    {
                        battleManager.CurrentState.DeckSystem.PlayCardToTimelineByInstanceId(instanceId);
                    }
                    else
                    {
                        // 兼容旧逻辑：使用 CardId
                        battleManager.CurrentState.DeckSystem.PlayCardToTimelineById(cardId);
                    }
                }
                else
                {
                    Debug.LogWarning($"[TimelineTrackView] 无法从手牌移除卡牌 {cardId}：BattleManager或DeckSystem为null");
                }

                // 从 UI_BattleScene 的 _handCards 列表中移除引用
                var battleScene = UnityEngine.Object.FindObjectOfType<Scripts.UI.UI_BattleScene>();
                if (battleScene != null)
                {
                    battleScene.RemoveCardFromHandList(cardViewController);
                }
            }
            else
            {
                // // Debug.LogError($"[TimelineTrackView] 放置卡牌失败: {cardId}");
            }
        }
        
        /// <summary>
        /// 高亮可放置区域
        /// </summary>
        /// <param name="startIndex">起始索引</param>
        /// <param name="slotCount">格子数量</param>
        /// <param name="canPlace">是否可以放置</param>
        public void HighlightPlacementArea(int startIndex, int slotCount, bool canPlace)
        {
            for (int i = startIndex; i < startIndex + slotCount && i < TimelineTrack.TrackLength; i++)
            {
                if (_slots[i] != null)
                {
                    _slots[i].SetHighlight(canPlace);
                }
            }
        }
        
        /// <summary>
        /// 清除高亮
        /// </summary>
        public void ClearHighlight()
        {
            foreach (var slot in _slots)
            {
                if (slot != null)
                    slot.ClearHighlight();
            }
        }
        
        /// <summary>
        /// 重新放置卡牌到新位置
        /// </summary>
        /// <param name="cardViewController">要移动的CardViewController</param>
        /// <param name="oldSlotIndex">原始格子索引</param>
        /// <param name="newSlotIndex">新格子索引</param>
        /// <param name="targetTrack">目标轨道（可以是不同轨道）</param>
        /// <returns>是否成功</returns>
        public bool RepositionCard(CardViewController cardViewController, int oldSlotIndex, int newSlotIndex, TimelineTrackView targetTrack)
        {
            // Debug.Log($"[TimelineTrackView] RepositionCard 开始: 当前轨道={name}, oldSlotIndex={oldSlotIndex}, newSlotIndex={newSlotIndex}, 目标轨道={targetTrack?.name}");
            
            if (cardViewController == null || _track == null)
            {
                // // Debug.LogError("[TimelineTrackView] 参数无效");
                return false;
            }
            
            // 获取卡牌配置（从 CardViewController 获取，因为 CardTimeSlot._cardState 可能为 null）
            var cardInfo = cardViewController.GetCurrentCard();
            if (cardInfo == null)
            {
                // // Debug.LogError("[TimelineTrackView] 无法获取卡牌信息（CardViewController._currentCard 为 null）");
                return false;
            }
            
            int totalSlots = cardInfo.Channeling + cardInfo.Duration + cardInfo.Recoil;
            // Debug.Log($"[TimelineTrackView] 卡牌 {cardInfo.Name} 需要 {totalSlots} 个格子");
            
            // 如果是同一轨道
            if (targetTrack == this)
            {
                // 先移除原位置的数据
                _track.RemoveCard(oldSlotIndex, totalSlots);
                
                // 检查新位置是否可用
                if (!_track.CanPlaceCard(newSlotIndex, totalSlots))
                {
                    // 恢复原位置
                    var converter = new CardToTimelineConverter();
                    var blocks = converter.ConvertCard(cardInfo, GetOwnerId(), "enemy_0");
                    _track.PlaceCard(oldSlotIndex, blocks);
                    // // Debug.LogWarning($"[TimelineTrackView] 新位置 {newSlotIndex} 不可用");
                    return false;
                }
                
                // 放置到新位置
                var newConverter = new CardToTimelineConverter();
                var newBlocks = newConverter.ConvertCard(cardInfo, GetOwnerId(), "enemy_0");
                _track.PlaceCard(newSlotIndex, newBlocks);
                
                // 更新 CardViewController 位置
                UpdateCardViewControllerPosition(cardViewController, newSlotIndex);
                
                // 刷新显示
                RefreshDisplay();
                
                // 发布卡牌放置事件，触发预解算（移动卡牌也需要重新预测）
                Debug.Log($"[TimelineTrackView] ========== 【触发点：卡牌移动】 ==========");
                Debug.Log($"[TimelineTrackView] 卡牌移动: {cardInfo.Name} (ID: {cardInfo.Id}) 从索引 {oldSlotIndex} 移动到 {newSlotIndex}");
                Ashlight.Common.Events.GameEvent.Publish(new Ashlight.Common.Events.CardPlacedEvent
                {
                    CardId = cardInfo.Id,
                    OwnerId = GetOwnerId(),
                    TargetId = "enemy_0" // TODO: 获取实际目标ID
                });
                
                // Debug.Log($"[TimelineTrackView] 成功将卡牌从 {oldSlotIndex} 移动到 {newSlotIndex}");
                return true;
            }
            else
            {
                // 跨轨道移动 - TODO: 实现跨轨道逻辑
                // // Debug.LogWarning("[TimelineTrackView] 暂不支持跨轨道移动");
                return false;
            }
        }
        
        /// <summary>
        /// 撤回卡牌到手牌
        /// </summary>
        /// <param name="cardViewController">要撤回的CardViewController</param>
        /// <param name="slotIndex">当前格子索引</param>
        /// <returns>是否成功</returns>
        public bool RecallCardToHand(CardViewController cardViewController, int slotIndex)
        {
            // Debug.Log($"[TimelineTrackView] RecallCardToHand 开始: CardViewController={cardViewController?.name}, slotIndex={slotIndex}");
            
            if (cardViewController == null || _track == null)
            {
                // // Debug.LogError("[TimelineTrackView] 参数无效");
                return false;
            }
            
            // 获取卡牌配置（从 CardViewController 获取，因为 CardTimeSlot._cardState 可能为 null）
            var cardInfo = cardViewController.GetCurrentCard();
            if (cardInfo == null)
            {
                // // Debug.LogError("[TimelineTrackView] 无法获取卡牌信息（CardViewController._currentCard 为 null）");
                return false;
            }
            
            // Debug.Log($"[TimelineTrackView] 准备撤回卡牌: {cardInfo.Name}");
            
            int totalSlots = cardInfo.Channeling + cardInfo.Duration + cardInfo.Recoil;
            
            // 从时间轴数据中移除
            _track.RemoveCard(slotIndex, totalSlots);
            
            // 从已放置列表中移除
            _placedCards.Remove(cardViewController);
            
            // 发布卡牌移除事件，触发预解算
            Ashlight.Common.Events.GameEvent.Publish(new Ashlight.Common.Events.CardRemovedEvent
            {
                CardId = cardInfo.Id,
                OwnerId = GetOwnerId()
            });
            
            // 显示 Card，隐藏 CardTimeSlot
            if (cardViewController.Card != null)
            {
                cardViewController.Card.alpha = 1f;
            }
            if (cardViewController.CardTimeSlot != null)
            {
                cardViewController.CardTimeSlot.Hide();
            }
            
            // 移动到手牌容器 CardContainer
            Transform cardContainer = FindCardContainer();
            if (cardContainer != null)
            {
                cardViewController.transform.SetParent(cardContainer, false);
                // 重置位置和缩放
                RectTransform rect = cardViewController.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition = Vector2.zero;
                    rect.localScale = Vector3.one;
                }
                // Debug.Log($"[TimelineTrackView] 卡牌已移回 CardContainer");
            }
            else
            {
                // // Debug.LogWarning("[TimelineTrackView] 未找到 CardContainer");
            }
            
            // 清除 CardTimeSlot 的时间轴信息
            cardViewController.CardTimeSlot?.SetTimelineInfo(null, -1);
            
            // 设置 Card 的状态为 OnHand
            cardViewController.SetCardDragState(Scripts.UI.CardDragState.OnHand);
            
            // 刷新显示
            RefreshDisplay();
            
            // Debug.Log($"[TimelineTrackView] 成功撤回卡牌到手牌: {cardInfo.Name}");
            return true;
        }
        
        /// <summary>
        /// 更新 CardViewController 的位置
        /// </summary>
        private void UpdateCardViewControllerPosition(CardViewController cardViewController, int slotIndex)
        {
            // Debug.Log($"[TimelineTrackView] UpdateCardViewControllerPosition 开始: CardViewController={cardViewController.name}, slotIndex={slotIndex}");
            
            RectTransform rect = cardViewController.GetComponent<RectTransform>();
            if (rect != null)
            {
                // 记录旧位置
                Vector2 oldPos = rect.anchoredPosition;
                Transform oldParent = rect.parent;
                
                // 确保父对象正确（应该在 markersContainer 下）
                if (rect.parent != markersContainer)
                {
                    // // Debug.LogWarning($"[TimelineTrackView] CardViewController 不在 markersContainer 下，当前父对象: {rect.parent?.name}，重新设置");
                    rect.SetParent(markersContainer, false);
                }
                
                // 确保锚点正确
                rect.anchorMin = new Vector2(0, 0.5f);
                rect.anchorMax = new Vector2(0, 0.5f);
                rect.pivot = new Vector2(0, 0.5f);
                
                // 计算新位置
                float startX = slotIndex * (slotWidth + slotSpacing);
                rect.anchoredPosition = new Vector2(startX, 0);
                
                // Debug.Log($"[TimelineTrackView] 位置已更新: 从 {oldPos} -> {rect.anchoredPosition}, 父对象: {oldParent?.name} -> {rect.parent?.name}, slotWidth={slotWidth}, slotSpacing={slotSpacing}");
            }
            else
            {
                // // Debug.LogError($"[TimelineTrackView] CardViewController 没有 RectTransform 组件！");
            }
            
            // 更新 CardViewController 的时间轴信息（这会同时更新 CardTimeSlot）
            cardViewController.SetTimelineInfo(this, slotIndex);
            
            // Debug.Log($"[TimelineTrackView] UpdateCardViewControllerPosition 完成");
        }
        
        /// <summary>
        /// 获取时间轴数据
        /// </summary>
        public TimelineTrack GetTrack()
        {
            return _track;
        }
        
        /// <summary>
        /// 获取关联的单位ID（仅对玩家时间轴有效）
        /// </summary>
        public string GetUnitId()
        {
            return _unitState?.UnitId;
        }
        
        /// <summary>
        /// 获取所属角色ID
        /// </summary>
        public string GetOwnerId()
        {
            return _unitState?.UnitId ?? "shared_enemy";
        }

        /// <summary>
        /// 直接放置 EnemyTimeSlot 到指定位置
        /// 这是正向构建的方法：从 EnemyIntention 创建 UI，然后标记时间轴
        /// </summary>
        /// <param name="skillInfo">敌人技能信息</param>
        /// <param name="slotIndex">起始位置索引</param>
        /// <param name="attacker">攻击者（敌人），可选</param>
        /// <param name="target">被攻击者（角色），可选</param>
        public void PlaceEnemyTimeSlot(cfg.Enemy.EnemySkillInfo skillInfo, int slotIndex, Enemy attacker = null, Character target = null)
        {
            if (!_isSharedEnemyTrack)
            {
                Debug.LogWarning("[TimelineTrackView] 只能在敌人时间轴上放置 EnemyTimeSlot");
                return;
            }

            if (enemyTimeSlotPrefab == null || markersContainer == null)
            {
                Debug.LogError("[TimelineTrackView] enemyTimeSlotPrefab 或 markersContainer 未设置");
                return;
            }

            if (skillInfo == null)
            {
                Debug.LogError("[TimelineTrackView] skillInfo 为 null");
                return;
            }

            // 检查位置是否有效
            int totalSlots = skillInfo.Channeling + skillInfo.Duration + skillInfo.Recoil;
            if (slotIndex < 0 || slotIndex + totalSlots > TimelineTrack.TrackLength)
            {
                Debug.LogWarning($"[TimelineTrackView] 位置 {slotIndex} 超出范围（需要 {totalSlots} 个格子）");
                return;
            }

            // 1. 创建 EnemyTimeSlot UI
            GameObject enemyTimeSlotObj = Instantiate(enemyTimeSlotPrefab, markersContainer);
            EnemyTimeSlot enemyTimeSlot = enemyTimeSlotObj.GetComponent<EnemyTimeSlot>();

            if (enemyTimeSlot == null)
            {
                // // Debug.LogError("[TimelineTrackView] EnemyTimeSlot 预制体缺少 EnemyTimeSlot 组件");
                Destroy(enemyTimeSlotObj);
                return;
            }

            // 2. 初始化 EnemyTimeSlot（传递攻击者和目标）
            enemyTimeSlot.Init(skillInfo, attacker, target);
            enemyTimeSlot.Show();

            // 3. 设置槽位索引
            enemyTimeSlot.SetSlotIndex(slotIndex);

            // 4. 设置位置
            RectTransform rect = enemyTimeSlotObj.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0, 0.5f);
                rect.anchorMax = new Vector2(0, 0.5f);
                rect.pivot = new Vector2(0, 0.5f);

                float startX = slotIndex * (slotWidth + slotSpacing);
                rect.anchoredPosition = new Vector2(startX, 0);
            }

            // 4. 添加到列表
            _enemyTimeSlots.Add(enemyTimeSlot);

            // 5. 标记时间轴格子为占用
            for (int i = 0; i < totalSlots && slotIndex + i < TimelineTrack.TrackLength; i++)
            {
                if (_slots[slotIndex + i] != null)
                {
                    _slots[slotIndex + i].SetOccupied(true);
                }
            }

            Debug.Log($"[TimelineTrackView] 成功放置 EnemyTimeSlot: {skillInfo.Name} at 位置 {slotIndex}，攻击者: {attacker?.GetUnitState()?.UnitId}, 目标: {target?.GetUnitState()?.UnitId}");
        }

        #endregion
        
        #region 私有方法
        
        /// <summary>
        /// 确保容器层级正确（Markers 在 Slots 上层）
        /// </summary>
        private void EnsureContainerLayering()
        {
            if (slotsContainer == null || markersContainer == null)
            {
                // // Debug.LogWarning("[TimelineTrackView] 容器引用为null，无法设置层级");
                return;
            }
            
            // 检查是否在同一父级下
            if (slotsContainer.parent != markersContainer.parent)
            {
                // // Debug.LogWarning("[TimelineTrackView] SlotsContainer 和 MarkersContainer 不在同一父级下");
                return;
            }
            
            // 设置 MarkersContainer 为最后一个子对象（最上层）
            markersContainer.SetAsLastSibling();
            
            // Debug.Log($"[TimelineTrackView] 容器层级设置完成: SlotsContainer={slotsContainer.GetSiblingIndex()}, MarkersContainer={markersContainer.GetSiblingIndex()}");
        }
        
        /// <summary>
        /// 创建15个格子
        /// </summary>
        private void CreateSlots()
        {
            if (slotPrefab == null)
            {
                // // Debug.LogError("[TimelineTrackView] slotPrefab为null");
                return;
            }
            
            if (slotsContainer == null)
            {
                // // Debug.LogError("[TimelineTrackView] slotsContainer为null");
                return;
            }

            // 确保容器是激活的
            if (!slotsContainer.gameObject.activeSelf)
            {
                //// // Debug.LogWarning("[TimelineTrackView] slotsContainer 未激活，正在激活...");
                slotsContainer.gameObject.SetActive(true);
            }
            
            //// Debug.Log($"[TimelineTrackView] 创建 {TimelineTrack.TrackLength} 个格子...");
            
            for (int i = 0; i < TimelineTrack.TrackLength; i++)
            {
                GameObject slotObj = Instantiate(slotPrefab, slotsContainer);
                
                // 确保格子是激活状态
                //slotObj.SetActive(true);
                
                TimelineSlotView slot = slotObj.GetComponent<TimelineSlotView>();
                
                if (slot == null)
                {
                    // // Debug.LogError($"[TimelineTrackView] slotPrefab缺少TimelineSlotView组件");
                    Destroy(slotObj);
                    continue;
                }
                
                slot.Initialize(i, this);
                _slots[i] = slot;
                
                // 注意：不再手动设置 sizeDelta 和 anchoredPosition
                // 因为 slotsContainer 应该使用 HorizontalLayoutGroup 来控制布局
                // 如果仍然看到宽度被设置为 100，请检查：
                // 1. slotsContainer 是否有 HorizontalLayoutGroup 组件
                // 2. HorizontalLayoutGroup 的 preferred width 设置
                // 3. TimelineSlotView prefab 中的 LayoutElement 组件设置
            }
            
        }
        
        
        /// <summary>
        /// 转移整个CardViewController到时间轴
        /// Card部分隐藏，CardTimeSlot显示
        /// </summary>
        /// <param name="cardViewController">要转移的CardViewController</param>
        /// <param name="slotIndex">起始格子索引</param>
        /// <param name="cardInfo">卡牌配置</param>
        private void TransferCardViewController(CardViewController cardViewController, int slotIndex, CardInfo cardInfo)
        {
            if (cardViewController == null)
            {
                // // Debug.LogError("[TimelineTrackView] CardViewController为null，无法转移");
                return;
            }
            
            if (markersContainer == null)
            {
                // // Debug.LogError("[TimelineTrackView] markersContainer为null，无法转移CardViewController");
                return;
            }
            
            // 改变父级到 markersContainer
            cardViewController.transform.SetParent(markersContainer, false);
            
            // 设置位置和锚点
            RectTransform rect = cardViewController.GetComponent<RectTransform>();
            if (rect != null)
            {
                // 重置锚点为左侧中心
                rect.anchorMin = new Vector2(0, 0.5f);
                rect.anchorMax = new Vector2(0, 0.5f);
                rect.pivot = new Vector2(0, 0.5f);
                
                // 计算位置：slotIndex * (slotWidth + slotSpacing)
                // slotWidth=95, slotSpacing=5, 所以每个格子是100像素
                float startX = slotIndex * (slotWidth + slotSpacing);
                rect.anchoredPosition = new Vector2(startX, 0);
                
                // Debug.Log($"[TimelineTrackView] TransferCardViewController 位置计算: slotIndex={slotIndex}, slotWidth={slotWidth}, slotSpacing={slotSpacing}, startX={startX}, 实际位置={rect.anchoredPosition}");
            }
            
            // 隐藏 Card 部分，显示 CardTimeSlot
            if (cardViewController.Card != null)
            {
                cardViewController.Card.alpha = 0f;
            }
            if (cardViewController.CardTimeSlot != null)
            {
                // 更新 CardTimeSlot 的 miniimage（确保显示正确的卡牌图标）
                cardViewController.CardTimeSlot.UpdateMiniSprite(cardInfo.Id);
                
                // 确保 CardTimeSlot 可见并激活
                cardViewController.CardTimeSlot.gameObject.SetActive(true);
                cardViewController.CardTimeSlot.Show();
            }
            
            // 设置 Card 的状态为 OnTime，并设置时间轴信息（用于后续拖拽）
            cardViewController.SetTimelineInfo(this, slotIndex);
            
            // 添加到已放置卡牌列表
            _placedCards.Add(cardViewController);
            
            // Debug.Log($"[TimelineTrackView] 成功转移CardViewController到时间轴: {cardInfo.Name}, 起始索引={slotIndex}");
        }
        
        /// <summary>
        /// 设置CardTimeSlot的位置
        /// 注意：大小由CardTimeSlot自身控制，这里只设置位置
        /// </summary>
        private void SetCardTimeSlotTransform(GameObject cardTimeSlotObj, int startIndex, CardInfo cardInfo)
        {
            RectTransform rect = cardTimeSlotObj.GetComponent<RectTransform>();
            if (rect == null) return;
            
            // 只设置位置到对应的起始格子
            float startX = startIndex * (slotWidth + slotSpacing);
            rect.anchoredPosition = new Vector2(startX, 0);
            
            // CardTimeSlot 自身已经根据 Channeling/Duration/Recoil 处理了显示
            // 不需要在这里设置 sizeDelta
            
            // Debug.Log($"[TimelineTrackView] 设置CardTimeSlot位置: 起始索引={startIndex}, X={startX}");
        }
        
        /// <summary>
        /// 获取卡牌或敌人技能占用的格子数
        /// </summary>
        private int GetCardSlotCount(string id)
        {
            // 先尝试作为卡牌ID查找
            var cardInfo = ConfigLoader.Tables?.TbCardInfo?.GetOrDefault(id);
            if (cardInfo != null)
            {
                return cardInfo.Channeling + cardInfo.Duration + cardInfo.Recoil;
            }
            
            // 如果不是卡牌，尝试作为敌人技能ID查找（用于敌人时间轴）
            if (_isSharedEnemyTrack)
            {
                var skillInfo = ConfigLoader.Tables?.TbEnemySkillInfo?.GetOrDefault(id);
                if (skillInfo != null)
                {
                    return skillInfo.Channeling + skillInfo.Duration + skillInfo.Recoil;
                }
            }
            
            // // Debug.LogWarning($"[TimelineTrackView] 未找到卡牌或技能配置: {id}，默认占用1格");
            return 1;
        }
        
        /// <summary>
        /// 查找 CardContainer
        /// </summary>
        private Transform FindCardContainer()
        {
            // 方法1: 通过名称查找
            GameObject cardContainerObj = GameObject.Find("CardContainer");
            if (cardContainerObj != null)
            {
                return cardContainerObj.transform;
            }
            
            // 方法2: 通过 UI_BattleScene 查找
            UI_BattleScene battleScene = FindObjectOfType<UI_BattleScene>();
            if (battleScene != null && battleScene.CardContainer != null)
            {
                return battleScene.CardContainer.transform;
            }
            
            // // Debug.LogWarning("[TimelineTrackView] 无法找到 CardContainer");
            return null;
        }

        /// <summary>
        /// 获取所有已放置的卡牌
        /// </summary>
        /// <returns>已放置的CardViewController列表的副本</returns>
        public List<CardViewController> GetAllPlacedCards()
        {
            return new List<CardViewController>(_placedCards);
        }

        /// <summary>
        /// 从已放置卡牌列表中移除指定卡片
        /// </summary>
        public void RemoveFromPlacedCards(CardViewController card)
        {
            if (card != null && _placedCards.Contains(card))
            {
                _placedCards.Remove(card);
                Debug.Log($"[TimelineTrackView] 已从_placedCards中移除卡片，剩余数量: {_placedCards.Count}");
            }
        }

        /// <summary>
        /// 清除所有已放置的卡牌（归还到手牌并清除时间轴数据）
        /// 注意：此方法只处理玩家时间轴上的卡牌，不会处理敌人时间轴上的EnemyTimeSlot
        /// </summary>
        public void ClearAllCards()
        {
            // 如果是敌人时间轴，不处理（敌人时间轴上的是EnemyTimeSlot，不是玩家的卡牌）
            if (_isSharedEnemyTrack)
            {
                // Debug.Log("[TimelineTrackView] 这是敌人时间轴，不处理EnemyTimeSlot");
                return;
            }

            // Debug.Log($"[TimelineTrackView] 开始清除所有卡牌，共 {_placedCards.Count} 张");
            
            // 创建副本以避免在迭代时修改集合
            var cardsToRemove = new List<CardViewController>(_placedCards);
            
            foreach (var cardViewController in cardsToRemove)
            {
                if (cardViewController == null)
                {
                    continue;
                }
                
                // 获取卡牌信息
                var cardInfo = cardViewController.GetCurrentCard();
                if (cardInfo == null)
                {
                    // // Debug.LogWarning("[TimelineTrackView] 无法获取卡牌信息，跳过");
                    continue;
                }
                
                // 获取时间轴信息
                var cardTimeSlot = cardViewController.CardTimeSlot;
                if (cardTimeSlot == null)
                {
                    // // Debug.LogWarning("[TimelineTrackView] CardTimeSlot为null，跳过");
                    continue;
                }
                
                int slotIndex = cardTimeSlot.GetSlotIndex();
                if (slotIndex < 0)
                {
                    // // Debug.LogWarning("[TimelineTrackView] 无效的slotIndex，跳过");
                    continue;
                }
                
                // 归还到手牌
                RecallCardToHand(cardViewController, slotIndex);
            }
            
            // Debug.Log($"[TimelineTrackView] 已清除所有卡牌");
        }

        /// <summary>
        /// 清除所有EnemyTimeSlot（仅用于敌人时间轴）
        /// </summary>
        private void ClearEnemyTimeSlots()
        {
            foreach (var enemyTimeSlot in _enemyTimeSlots)
            {
                if (enemyTimeSlot != null)
                {
                    Destroy(enemyTimeSlot.gameObject);
                }
            }
            _enemyTimeSlots.Clear();
        }

        /// <summary>
        /// 创建EnemyTimeSlot（仅用于敌人时间轴）
        /// 遍历时间轴上的Blocks，为每个敌人技能创建EnemyTimeSlot
        /// </summary>
        private void CreateEnemyTimeSlots()
        {
            // Debug.Log($"[TimelineTrackView] CreateEnemyTimeSlots 开始调用");
            // Debug.Log($"[TimelineTrackView] _isSharedEnemyTrack = {_isSharedEnemyTrack}");
            // Debug.Log($"[TimelineTrackView] enemyTimeSlotPrefab = {(enemyTimeSlotPrefab != null ? "已设置" : "NULL")}");
            // Debug.Log($"[TimelineTrackView] markersContainer = {(markersContainer != null ? "已设置" : "NULL")}");

            if (!_isSharedEnemyTrack || enemyTimeSlotPrefab == null || markersContainer == null)
            {
                // // Debug.LogWarning($"[TimelineTrackView] CreateEnemyTimeSlots 条件不满足，退出");
                return;
            }

            if (_track == null)
            {
                // // Debug.LogError("[TimelineTrackView] Track为null，无法创建EnemyTimeSlot");
                return;
            }

            // Debug.Log($"[TimelineTrackView] 开始遍历时间轴，TrackLength = {TimelineTrack.TrackLength}");

            // 遍历时间轴，找到每个敌人技能的起始位置
            int i = 0;
            int foundBlockCount = 0;
            while (i < TimelineTrack.TrackLength)
            {
                var block = _track.Blocks[i];

                if (block != null)
                {
                    foundBlockCount++;
                    // Debug.Log($"[TimelineTrackView] 索引 {i}: Block存在, IsEmpty={block.IsEmpty()}, Phase={block.Phase}, SourceCardId={block.SourceCardId}");
                }
                // 修复：Startup阶段的Block没有Commands，所以IsEmpty()会返回true
                // 只需要检查Phase是否为Startup，不需要检查IsEmpty
                if (block != null && block.Phase == PhaseEnum.Startup)
                {
                    // 找到技能起始位置（Startup阶段）
                    string skillId = block.SourceCardId; // 实际上是技能ID
                    
                    // 从配置表获取敌人技能信息
                    var skillInfo = ConfigLoader.Tables?.TbEnemySkillInfo?.GetOrDefault(skillId);
                    if (skillInfo != null)
                    {
                        // 创建EnemyTimeSlot实例
                        GameObject enemyTimeSlotObj = Instantiate(enemyTimeSlotPrefab, markersContainer);
                        EnemyTimeSlot enemyTimeSlot = enemyTimeSlotObj.GetComponent<EnemyTimeSlot>();
                        
                        if (enemyTimeSlot != null)
                        {
                            // 初始化EnemyTimeSlot
                            enemyTimeSlot.Init(skillInfo);
                            enemyTimeSlot.Show();
                            
                            // 设置位置
                            RectTransform rect = enemyTimeSlotObj.GetComponent<RectTransform>();
                            if (rect != null)
                            {
                                // 设置锚点为左侧中心
                                rect.anchorMin = new Vector2(0, 0.5f);
                                rect.anchorMax = new Vector2(0, 0.5f);
                                rect.pivot = new Vector2(0, 0.5f);
                                
                                // 计算位置
                                float startX = i * (slotWidth + slotSpacing);
                                rect.anchoredPosition = new Vector2(startX, 0);
                            }
                            
                            _enemyTimeSlots.Add(enemyTimeSlot);
                            // Debug.Log($"[TimelineTrackView] 创建EnemyTimeSlot: {skillInfo.Name} at 索引 {i}");
                        }
                        else
                        {
                            // // Debug.LogError($"[TimelineTrackView] EnemyTimeSlot预制体缺少EnemyTimeSlot组件");
                            Destroy(enemyTimeSlotObj);
                        }
                        
                        // 跳过这个技能占用的所有格子
                        int totalSlots = skillInfo.Channeling + skillInfo.Duration + skillInfo.Recoil;
                        i += totalSlots;
                    }
                    else
                    {
                        // // Debug.LogWarning($"[TimelineTrackView] 未找到敌人技能配置: {skillId}");
                        i++;
                    }
                }
                else
                {
                    i++;
                }
            }

            // Debug.Log($"[TimelineTrackView] CreateEnemyTimeSlots 完成，找到的Block总数: {foundBlockCount}, 创建的EnemyTimeSlot数量: {_enemyTimeSlots.Count}");
        }

        /// <summary>
        /// 时间轴前进后更新所有卡片位置
        /// 所有卡片向前移动一格（即X坐标减少 slotWidth + slotSpacing）
        /// </summary>
        public void ShiftAllCardsForward()
        {
            string trackOwner = _isSharedEnemyTrack ? "敌人共享时间轴" : (_unitState?.UnitId ?? "未知时间轴");
            Debug.Log($"<color=green>========== {trackOwner} 开始前进 ==========</color>");
            Debug.Log($"<color=green>玩家卡片数: {_placedCards.Count}, 敌人时间槽数: {_enemyTimeSlots.Count}</color>");

            float offset = -(slotWidth + slotSpacing); // 向前移动的偏移量

            // 移动所有玩家卡片
            var cardsToRemove = new List<CardViewController>();
            
            if (_isSharedEnemyTrack)
            {
                Debug.Log($"<color=blue>【敌人共享时间轴】_placedCards列表数量: {_placedCards.Count}</color>");
                if (_placedCards.Count > 0)
                {
                    Debug.LogError($"<color=red>错误！敌人共享时间轴上不应该有_placedCards！数量: {_placedCards.Count}</color>");
                }
            }
            else
            {
                Debug.Log($"<color=blue>【玩家时间轴】_placedCards列表数量: {_placedCards.Count}</color>");
            }
            
            foreach (var card in _placedCards)
            {
                if (card == null) continue;

                // 获取当前槽位索引
                var cardTimeSlot = card.CardTimeSlot;
                if (cardTimeSlot == null)
                {
                    Debug.LogWarning($"[TimelineTrackView] 卡片没有CardTimeSlot，跳过");
                    continue;
                }

                int currentSlotIndex = cardTimeSlot.GetSlotIndex();
                int newSlotIndex = currentSlotIndex - 1;
                
                var cardInfo = card.GetCurrentCard();
                string cardName = cardInfo?.Name ?? "未知卡片";
                string ownerId = _unitState?.UnitId ?? "未知Owner";
                
                Debug.Log($"<color=blue>========== 【位移检查】卡片: {cardName} (Owner: {ownerId}) ==========</color>");
                Debug.Log($"<color=blue>当前索引: {currentSlotIndex}, 新索引: {newSlotIndex}</color>");
                
                // 检查数据层：这张卡牌是否还在时间轴上
                // 数据层已经执行了 ShiftBlocks(0, -1)，所以需要检查新索引位置
                bool cardStillInTrack = false;
                string dataLayerCheckResult = "";
                
                if (_track != null)
                {
                    // 首先检查新索引位置（数据层已经移动过了）
                    if (newSlotIndex >= 0 && newSlotIndex < TimelineTrack.TrackLength)
                    {
                        // 检查新索引位置是否有这张卡牌的 block
                        var block = _track.GetBlock(newSlotIndex);
                        if (block != null && !block.IsEmpty() && block.SourceCardId == cardInfo.Id)
                        {
                            cardStillInTrack = true;
                            dataLayerCheckResult = $"✓ 数据层确认：卡片 {cardName} 在新索引 {newSlotIndex} 位置仍存在";
                            Debug.Log($"<color=green>{dataLayerCheckResult}</color>");
                        }
                        else
                        {
                            // 也可能卡牌跨越多个格子，检查相邻位置
                            int totalSlots = cardInfo.Channeling + cardInfo.Duration + cardInfo.Recoil;
                            for (int i = 0; i < totalSlots && newSlotIndex + i < TimelineTrack.TrackLength; i++)
                            {
                                var checkBlock = _track.GetBlock(newSlotIndex + i);
                                if (checkBlock != null && !checkBlock.IsEmpty() && checkBlock.SourceCardId == cardInfo.Id)
                                {
                                    cardStillInTrack = true;
                                    dataLayerCheckResult = $"✓ 数据层确认：卡片 {cardName} 在新索引 {newSlotIndex + i} 位置仍存在（跨格卡牌）";
                                    Debug.Log($"<color=green>{dataLayerCheckResult}</color>");
                                    break;
                                }
                            }
                            
                            // 如果在新索引位置找不到，也检查旧索引位置（可能数据层还没移动？）
                            if (!cardStillInTrack && currentSlotIndex >= 0 && currentSlotIndex < TimelineTrack.TrackLength)
                            {
                                var oldBlock = _track.GetBlock(currentSlotIndex);
                                if (oldBlock != null && !oldBlock.IsEmpty() && oldBlock.SourceCardId == cardInfo.Id)
                                {
                                    cardStillInTrack = true;
                                    dataLayerCheckResult = $"✓ 数据层确认：卡片 {cardName} 在旧索引 {currentSlotIndex} 位置仍存在（数据层可能未移动）";
                                    Debug.Log($"<color=green>{dataLayerCheckResult}</color>");
                                }
                            }
                            
                            // 如果还是找不到，在整个时间轴上搜索（可能卡牌被移动到了其他位置）
                            if (!cardStillInTrack)
                            {
                                for (int searchI = 0; searchI < TimelineTrack.TrackLength; searchI++)
                                {
                                    var searchBlock = _track.GetBlock(searchI);
                                    if (searchBlock != null && !searchBlock.IsEmpty() && searchBlock.SourceCardId == cardInfo.Id)
                                    {
                                        cardStillInTrack = true;
                                        dataLayerCheckResult = $"✓ 数据层确认：卡片 {cardName} 在索引 {searchI} 位置仍存在（全时间轴搜索找到）";
                                        Debug.Log($"<color=green>{dataLayerCheckResult}</color>");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    
                    // 如果还是找不到，记录详细信息用于调试
                    if (!cardStillInTrack)
                    {
                        dataLayerCheckResult = $"✗ 数据层未找到：卡片 {cardName} (CardId: {cardInfo.Id})";
                        Debug.LogWarning($"<color=orange>{dataLayerCheckResult}</color>");
                        Debug.LogWarning($"<color=orange>  当前索引: {currentSlotIndex}, 新索引: {newSlotIndex}</color>");
                        Debug.LogWarning($"<color=orange>  数据层检查范围: 新索引[{newSlotIndex}]到[{newSlotIndex + cardInfo.Channeling + cardInfo.Duration + cardInfo.Recoil - 1}]</color>");
                        
                        // 打印数据层当前状态（用于调试）
                        Debug.LogWarning($"<color=orange>  数据层当前状态（前10格）:</color>");
                        for (int debugI = 0; debugI < 10 && debugI < TimelineTrack.TrackLength; debugI++)
                        {
                            var debugBlock = _track.GetBlock(debugI);
                            if (debugBlock != null && !debugBlock.IsEmpty())
                            {
                                Debug.LogWarning($"<color=orange>    索引{debugI}: CardId={debugBlock.SourceCardId}, Phase={debugBlock.Phase}, Owner={debugBlock.OwnerId}</color>");
                            }
                        }
                    }
                }
                
                // 如果新位置小于0，说明卡片已经移出时间轴
                if (newSlotIndex < 0)
                {
                    Debug.Log($"<color=red>【标记为待销毁】卡片: {cardName} (Owner: {ownerId}), 原因: 新索引 {newSlotIndex} < 0（已移出时间轴）</color>");
                    cardsToRemove.Add(card);
                    continue;
                }
                
                // 如果数据层中这张卡牌已经不存在了，说明已经被执行并移除，应该移除UI
                // 但只有在当前索引=0时才移除（因为只有index=0的卡牌才会被执行）
                if (!cardStillInTrack && currentSlotIndex == 0)
                {
                    Debug.Log($"<color=red>【标记为待销毁】卡片: {cardName} (Owner: {ownerId}), 原因: 当前索引=0 且数据层中已不存在（已执行）</color>");
                    Debug.Log($"<color=red>  数据层检查结果: {dataLayerCheckResult}</color>");
                    cardsToRemove.Add(card);
                    continue;
                }
                
                // 如果数据层中不存在，但当前索引不是0，可能是索引不同步，记录警告但不移除
                if (!cardStillInTrack && currentSlotIndex != 0)
                {
                    Debug.LogWarning($"<color=orange>⚠️ 警告：卡片 {cardName} 在数据层中不存在，但当前索引={currentSlotIndex}，可能是索引不同步</color>");
                    Debug.LogWarning($"<color=orange>  数据层检查结果: {dataLayerCheckResult}</color>");
                    Debug.LogWarning($"<color=orange>  不移除此卡片，等待后续同步</color>");
                }

                // 更新UI位置
                RectTransform rect = card.GetComponent<RectTransform>();
                if (rect != null)
                {
                    Vector2 newPos = new Vector2(rect.anchoredPosition.x + offset, rect.anchoredPosition.y);
                    rect.anchoredPosition = newPos;
                    Debug.Log($"<color=cyan>【移动TimeSlot(来自_placedCards): {cardName} Owner: {ownerId} 从索引{currentSlotIndex}到{newSlotIndex}】位置: {rect.anchoredPosition}</color>");
                }

                // 更新CardTimeSlot的槽位索引
                cardTimeSlot.SetTimelineInfo(this, newSlotIndex);
            }

            // 移动所有敌人时间槽 - 只有敌人共享时间轴才处理
            var enemySlotsToRemove = new List<EnemyTimeSlot>();
            
            if (_isSharedEnemyTrack)
            {
                // Debug.Log($"<color=magenta>【这是敌人共享时间轴】开始处理敌人时间槽，当前_enemyTimeSlots.Count = {_enemyTimeSlots.Count}</color>");
                
                int processedCount = 0;
                foreach (var enemySlot in _enemyTimeSlots)
                {
                    if (enemySlot == null)
                    {
                        // // Debug.LogWarning("<color=orange>发现空的enemySlot，跳过</color>");
                        continue;
                    }

                    processedCount++;
                    
                    // 获取当前槽位索引
                    int currentSlotIndex = enemySlot.GetSlotIndex();
                    int newSlotIndex = currentSlotIndex - 1;
                    
                    var skillInfo = enemySlot.GetEnemySkillInfo();
                    string skillName = skillInfo?.Name ?? "未知技能";
                    string ownerId = "EnemyShared";
                    
                    // 计算技能的结束位置（索引起始点是Duration开始，所以只计算 Duration + Recoil）
                    int skillTotalSlots = 0;
                    int skillEndIndex = newSlotIndex;
                    if (skillInfo != null)
                    {
                        // 注意：索引起始点是Duration的开始位置，不包括Channeling
                        skillTotalSlots = skillInfo.Duration + skillInfo.Recoil;
                        skillEndIndex = newSlotIndex + skillTotalSlots - 1; // 结束位置
                    }
                    
                    // Debug.Log($"<color=magenta>━━━━━ 处理第{processedCount}个敌人槽 ━━━━━</color>");
                    // Debug.Log($"<color=magenta>名称: {skillName}</color>");
                    // Debug.Log($"<color=magenta>GameObject名称: {enemySlot.gameObject.name}</color>");
                    // Debug.Log($"<color=magenta>当前起始索引: {currentSlotIndex}, 新起始索引: {newSlotIndex}</color>");
                    // Debug.Log($"<color=magenta>技能占用格子数: {skillTotalSlots} (Duration:{skillInfo?.Duration ?? 0} + Recoil:{skillInfo?.Recoil ?? 0}，不含Channeling)</color>");
                    // Debug.Log($"<color=magenta>技能结束索引: {skillEndIndex}</color>");
                    // Debug.Log($"<color=magenta>判断条件 skillEndIndex < 0 = {skillEndIndex < 0}</color>");

                    // 只有当整个技能的结束位置小于0时，才销毁（整个技能都移出了时间轴）
                    if (skillEndIndex < 0)
                    {
                        // Debug.Log($"<color=red>【标记为待销毁(来自_enemyTimeSlots): {skillName} Owner: {ownerId}】</color>");
                        // Debug.Log($"<color=red>  起始索引: {currentSlotIndex} → {newSlotIndex}, 结束索引: {skillEndIndex} (整个技能已移出时间轴)</color>");
                        enemySlotsToRemove.Add(enemySlot);
                        continue;
                    }

                    // 更新UI位置
                    RectTransform rect = enemySlot.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        Vector2 currentPos = rect.anchoredPosition;
                        Vector2 newPos = new Vector2(currentPos.x + offset, currentPos.y);
                        rect.anchoredPosition = newPos;
                        // Debug.Log($"<color=cyan>【移动TimeSlot(来自_enemyTimeSlots): {skillName} Owner: {ownerId} 从索引{currentSlotIndex}到{newSlotIndex}】坐标: {currentPos} -> {newPos}</color>");
                    }

                    // 更新敌人时间槽的槽位索引
                    enemySlot.SetSlotIndex(newSlotIndex);
                    // Debug.Log($"<color=magenta>已更新 {skillName} 的索引为 {newSlotIndex}</color>");
                }
                
                // Debug.Log($"<color=magenta>敌人时间槽处理完成，处理数量={processedCount}, 标记待移除={enemySlotsToRemove.Count}</color>");
            }
            else
            {
                // Debug.Log($"<color=magenta>【这是玩家时间轴】跳过敌人时间槽处理（玩家时间轴不应该有敌人槽）</color>");
                if (_enemyTimeSlots.Count > 0)
                {
                    // // Debug.LogWarning($"<color=orange>警告：玩家时间轴上发现 {_enemyTimeSlots.Count} 个敌人时间槽，这不正常！</color>");
                }
            }

            // 移除已经完全移出时间轴的卡片（这会在step5中处理）
            // 这里只返回需要移除的卡片列表，让外部处理
            _cardsToRemoveAfterShift = cardsToRemove;
            _enemySlotsToRemoveAfterShift = enemySlotsToRemove;

            trackOwner = _isSharedEnemyTrack ? "敌人共享时间轴" : (_unitState?.UnitId ?? "未知时间轴");
            // Debug.Log($"<color=green>========== {trackOwner} 前进完成 ==========</color>");
            // Debug.Log($"<color=green>待移除卡片数: {cardsToRemove.Count}, 待移除敌人槽数: {enemySlotsToRemove.Count}</color>");
        }

        // 临时存储需要移除的卡片和敌人槽（在ShiftAllCardsForward后，RemoveCompletedCards前使用）
        private List<CardViewController> _cardsToRemoveAfterShift = new List<CardViewController>();
        private List<EnemyTimeSlot> _enemySlotsToRemoveAfterShift = new List<EnemyTimeSlot>();

        /// <summary>
        /// 获取需要移除的卡片列表（已完全移出时间轴的卡片）
        /// </summary>
        public List<CardViewController> GetCardsToRemove()
        {
            return new List<CardViewController>(_cardsToRemoveAfterShift);
        }

        /// <summary>
        /// 清空待移除列表
        /// </summary>
        public void ClearCardsToRemove()
        {
            _cardsToRemoveAfterShift.Clear();
            _enemySlotsToRemoveAfterShift.Clear();
        }

        /// <summary>
        /// 移除已完全移出时间轴的敌人时间槽
        /// </summary>
        public void RemoveCompletedEnemySlots()
        {
            // Debug.Log($"<color=yellow>========== 开始实际移除敌人时间槽 ==========</color>");
            // Debug.Log($"<color=yellow>待移除列表(_enemySlotsToRemoveAfterShift)数量: {_enemySlotsToRemoveAfterShift.Count}</color>");
            // Debug.Log($"<color=yellow>当前_enemyTimeSlots数量: {_enemyTimeSlots.Count}</color>");
            
            int removeCount = 0;
            foreach (var enemySlot in _enemySlotsToRemoveAfterShift)
            {
                if (enemySlot != null)
                {
                    var skillInfo = enemySlot.GetEnemySkillInfo();
                    string skillName = skillInfo?.Name ?? "未知技能";
                    int slotIndex = enemySlot.GetSlotIndex();
                    
                    // Debug.Log($"<color=yellow>【实际销毁UI {++removeCount}: {skillName} Owner: EnemyShared, 最终索引: {slotIndex}】</color>");
                    
                    bool removed = _enemyTimeSlots.Remove(enemySlot);
                    // Debug.Log($"<color=yellow>从_enemyTimeSlots移除结果: {removed}</color>");
                    
                    Destroy(enemySlot.gameObject);
                }
                else
                {
                    // // Debug.LogWarning("<color=orange>待移除列表中发现空的enemySlot</color>");
                }
            }
            
            // Debug.Log($"<color=yellow>实际销毁数量: {removeCount}</color>");
            _enemySlotsToRemoveAfterShift.Clear();
            // Debug.Log($"<color=yellow>移除后_enemyTimeSlots剩余数量: {_enemyTimeSlots.Count}</color>");
            // Debug.Log($"<color=yellow>========== 敌人时间槽移除完成 ==========</color>");
        }
        
        #endregion
    }
}

