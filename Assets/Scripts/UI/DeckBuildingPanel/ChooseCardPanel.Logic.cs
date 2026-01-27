using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using cfg;
using cfg.Character;
using Ashlight.Common.Events;
using Ashlight.Systems.Character;
using Scripts.UI;

namespace _Scripts.UI
{
    /// <summary>
    /// ChooseCardPanel的业务逻辑部分（手动编写）
    /// 负责管理卡牌选择界面的交互逻辑
    /// </summary>
    public partial class ChooseCardPanel
    {
        #region 序列化字段

        [Header("卡牌预制体")]
        [SerializeField]
        [Tooltip("SCardViewController预制体，用于在卡组中显示")]
        private GameObject sCardViewControllerPrefab;

        [Header("角色设置")]
        [SerializeField]
        [Tooltip("当前编辑的角色ID")]
        private CharacterEnum currentCharacter = CharacterEnum.Irene;

        #endregion

        #region 私有字段

        private List<Scripts.UI.SCardViewController> _deckCards = new List<Scripts.UI.SCardViewController>();
        private Scripts.UI.CardLibrary _cardLibraryComponent;

        #endregion

        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();

            // 获取CardLibrary组件
            if (CardLibrary != null)
            {
                _cardLibraryComponent = CardLibrary.GetComponent<Scripts.UI.CardLibrary>();
                if (_cardLibraryComponent == null)
                {
                    Debug.LogError("[ChooseCardPanel] CardLibrary组件未找到！");
                }
            }
        }

        /// <summary>
        /// 启动时初始化
        /// </summary>
        private void Start()
        {
            InitializePanel();
        }

        /// <summary>
        /// 启用时订阅事件
        /// </summary>
        private void OnEnable()
        {
            // 订阅选择卡牌到卡组事件
            GameEvent.Subscribe<SelectCardToDeckEvent>(OnSelectCardToDeck);
            
            // 订阅删除卡牌事件
            GameEvent.Subscribe<DeleteCardFromDeckEvent>(OnDeleteCardFromDeck);

            // 订阅卡牌库角色过滤事件
            GameEvent.Subscribe<CardLibraryChangeByCharacterEvent>(OnCardLibraryChangeByCharacter);

            // 订阅清除角色卡组事件
            GameEvent.Subscribe<ClearCharacterDeckEvent>(OnClearCharacterDeck);
        }

        /// <summary>
        /// 禁用时取消订阅
        /// </summary>
        private void OnDisable()
        {
            // 取消订阅
            GameEvent.Unsubscribe<SelectCardToDeckEvent>(OnSelectCardToDeck);
            GameEvent.Unsubscribe<DeleteCardFromDeckEvent>(OnDeleteCardFromDeck);
            GameEvent.Unsubscribe<CardLibraryChangeByCharacterEvent>(OnCardLibraryChangeByCharacter);
            GameEvent.Unsubscribe<ClearCharacterDeckEvent>(OnClearCharacterDeck);
        }

        /// <summary>
        /// 销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化面板
        /// </summary>
        public void InitializePanel()
        {
            Debug.Log("[ChooseCardPanel] 面板初始化");
            
            // 验证必要组件
            if (sCardViewControllerPrefab == null)
            {
                Debug.LogError("[ChooseCardPanel] SCardViewController预制体未设置！");
            }
            
            if (CardDeckContainer == null)
            {
                Debug.LogError("[ChooseCardPanel] CardDeckContainer未绑定！");
            }

            // 注册按钮事件
            if (Btn_Start != null)
            {
                Btn_Start.onClick.AddListener(OnStartButtonClick);
            }
        }

        /// <summary>
        /// 刷新面板显示
        /// </summary>
        public void RefreshPanel()
        {
            Debug.Log("[ChooseCardPanel] 刷新面板");
            UpdateStatistics();
        }

        /// <summary>
        /// 设置当前编辑的角色
        /// </summary>
        /// <param name="character">角色ID</param>
        public void SetCurrentCharacter(CharacterEnum character)
        {
            if (currentCharacter == character)
            {
                return;
            }

            Debug.Log($"[ChooseCardPanel] 切换角色: {currentCharacter} -> {character}");
            
            currentCharacter = character;
            
            // 清空当前UI显示
            ClearDeckUI();
            
            // 从角色系统加载卡组
            LoadCharacterDeck();
            
            // 更新统计信息
            UpdateStatistics();
        }

        /// <summary>
        /// 从角色系统加载当前角色的卡组
        /// </summary>
        public void LoadCharacterDeck()
        {
            Debug.Log($"[ChooseCardPanel] 加载角色 {currentCharacter} 的卡组");

            // 清空现有UI
            ClearDeckUI();

            // 获取角色状态
            var characterState = CharacterSystem.GetCharacterState(currentCharacter);
            if (characterState == null)
            {
                Debug.LogWarning($"[ChooseCardPanel] 未找到角色 {currentCharacter} 的状态");
                return;
            }

            // 加载卡组中的每张卡牌
            if (characterState.CurrentDeck != null)
            {
                foreach (var cardState in characterState.CurrentDeck)
                {
                    LoadCardToUI(cardState.CardId);
                }
            }

            Debug.Log($"[ChooseCardPanel] 成功加载 {_deckCards.Count} 张卡牌");
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 清理资源
        /// </summary>
        private void Cleanup()
        {
            Debug.Log("[ChooseCardPanel] 清理资源");
            
            // 取消按钮事件绑定
            if (Btn_Start != null)
            {
                Btn_Start.onClick.RemoveListener(OnStartButtonClick);
            }
            
            // 清理所有卡组中的卡牌
            ClearDeckUI();
        }

        /// <summary>
        /// 清空卡组UI显示
        /// </summary>
        private void ClearDeckUI()
        {
            foreach (var card in _deckCards)
            {
                if (card != null)
                {
                    Destroy(card.gameObject);
                }
            }
            _deckCards.Clear();
        }

        /// <summary>
        /// 加载单张卡牌到UI
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        private void LoadCardToUI(string cardId)
        {
            // 从配置表获取卡牌信息
            var cardInfo = Ashlight.Config.ConfigLoader.Tables.TbCardInfo.GetOrDefault(cardId);
            if (cardInfo == null)
            {
                Debug.LogError($"[ChooseCardPanel] 未找到卡牌配置: {cardId}");
                return;
            }

            // 验证预制体和容器
            if (sCardViewControllerPrefab == null || CardDeckContainer == null)
            {
                Debug.LogError("[ChooseCardPanel] 预制体或容器未设置！");
                return;
            }

            // 实例化卡牌
            GameObject cardObj = Instantiate(sCardViewControllerPrefab, CardDeckContainer);
            if (cardObj == null)
            {
                Debug.LogError($"[ChooseCardPanel] 实例化卡牌失败: {cardId}");
                return;
            }

            // 获取组件并初始化
            var sCardView = cardObj.GetComponent<Scripts.UI.SCardViewController>();
            if (sCardView == null)
            {
                Debug.LogError($"[ChooseCardPanel] SCardViewController组件未找到: {cardId}");
                Destroy(cardObj);
                return;
            }

            sCardView.Initialize(cardInfo, DescriptionMode.View);
            _deckCards.Add(sCardView);
        }

        /// <summary>
        /// 更新统计信息
        /// </summary>
        private void UpdateStatistics()
        {
            if (Txt_Statistics != null)
            {
                Txt_Statistics.text = $"卡组: {_deckCards.Count}";
            }
        }

        /// <summary>
        /// 开始按钮点击事件
        /// </summary>
        private void OnStartButtonClick()
        {
            Debug.Log($"[ChooseCardPanel] 开始按钮点击，当前角色: {currentCharacter}，卡组数量: {_deckCards.Count}");
            
            // 保存当前卡组到存档
            Ashlight.Systems.Core.GameManager.Instance.SaveGame();
            
            // TODO: 这里可以添加进入战斗或其他游戏场景的逻辑
            Debug.Log($"[ChooseCardPanel] 卡组已保存，准备开始游戏");
        
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 处理选择卡牌到卡组事件
        /// </summary>
        private void OnSelectCardToDeck(SelectCardToDeckEvent evt)
        {
            if (evt.cardInfo == null)
            {
                Debug.LogWarning("[ChooseCardPanel] 收到空的卡牌信息");
                return;
            }

            Debug.Log($"[ChooseCardPanel] 添加卡牌到卡组: {evt.cardInfo.Name}");

            // 验证预制体和容器
            if (sCardViewControllerPrefab == null)
            {
                Debug.LogError("[ChooseCardPanel] SCardViewController预制体未设置！");
                return;
            }

            if (CardDeckContainer == null)
            {
                Debug.LogError("[ChooseCardPanel] CardDeckContainer未绑定！");
                return;
            }

            // === 添加到角色系统（数据层） ===
            bool addedToSystem = CharacterSystem.AddCardToDeck(currentCharacter, evt.cardInfo.Id);
            if (!addedToSystem)
            {
                Debug.LogError($"[ChooseCardPanel] 无法将卡牌 {evt.cardInfo.Name} 添加到角色 {currentCharacter} 的数据中");
                return;
            }

            // === UI显示层 ===
            // 实例化SCardViewController
            GameObject cardObj = Instantiate(sCardViewControllerPrefab, CardDeckContainer);
            if (cardObj == null)
            {
                Debug.LogError($"[ChooseCardPanel] 实例化卡牌失败: {evt.cardInfo.Name}");
                // 回滚数据层的添加
                CharacterSystem.RemoveCardFromDeck(currentCharacter, evt.cardInfo.Id);
                return;
            }

            // 获取SCardViewController组件
            var sCardView = cardObj.GetComponent<Scripts.UI.SCardViewController>();
            if (sCardView == null)
            {
                Debug.LogError($"[ChooseCardPanel] SCardViewController组件未找到: {evt.cardInfo.Name}");
                Destroy(cardObj);
                // 回滚数据层的添加
                CharacterSystem.RemoveCardFromDeck(currentCharacter, evt.cardInfo.Id);
                return;
            }

            // 初始化卡牌数据（使用View模式）
            sCardView.Initialize(evt.cardInfo, DescriptionMode.View);

            // 添加到卡组列表
            _deckCards.Add(sCardView);

            // 更新统计信息
            UpdateStatistics();

            Debug.Log($"[ChooseCardPanel] 成功添加卡牌: {evt.cardInfo.Name}，当前卡组数量: {_deckCards.Count}");
        }

        /// <summary>
        /// 处理从卡组删除卡牌事件
        /// </summary>
        private void OnDeleteCardFromDeck(DeleteCardFromDeckEvent evt)
        {
            if (evt.sCardView == null)
            {
                Debug.LogWarning("[ChooseCardPanel] 收到空的SCardView");
                return;
            }

            if (evt.cardInfo == null)
            {
                Debug.LogWarning("[ChooseCardPanel] 收到空的CardInfo");
                return;
            }

            Debug.Log($"[ChooseCardPanel] 删除卡牌: {evt.cardInfo.Name}");

            // === 从角色系统删除（数据层） ===
            bool removedFromSystem = CharacterSystem.RemoveCardFromDeck(currentCharacter, evt.cardInfo.Id);
            if (!removedFromSystem)
            {
                Debug.LogWarning($"[ChooseCardPanel] 无法从角色 {currentCharacter} 的数据中移除卡牌 {evt.cardInfo.Name}");
                // 即使数据层删除失败，仍然继续删除UI显示
            }

            // === UI显示层 ===
            // 从列表中移除
            if (_deckCards.Contains(evt.sCardView))
            {
                _deckCards.Remove(evt.sCardView);
            }

            // 销毁GameObject
            if (evt.sCardView != null)
            {
                Destroy(evt.sCardView.gameObject);
            }

            // 更新统计信息
            UpdateStatistics();

            Debug.Log($"[ChooseCardPanel] 成功删除卡牌，当前卡组数量: {_deckCards.Count}");
        }

        /// <summary>
        /// 处理卡牌库按角色过滤事件
        /// </summary>
        private void OnCardLibraryChangeByCharacter(CardLibraryChangeByCharacterEvent evt)
        {
            Debug.Log($"[ChooseCardPanel] 收到角色过滤事件: {evt.character}");

            if (_cardLibraryComponent != null)
            {
                _cardLibraryComponent.UpdateByCharacter(evt.character);
            }
            else
            {
                Debug.LogError("[ChooseCardPanel] CardLibrary组件未找到，无法过滤卡牌！");
            }
        }

        /// <summary>
        /// 处理清除角色卡组事件
        /// </summary>
        private void OnClearCharacterDeck(ClearCharacterDeckEvent evt)
        {
            Debug.Log($"[ChooseCardPanel] 清除角色卡组: {evt.character}");

            // 找出所有该角色的卡牌
            var cardsToRemove = _deckCards
                .Where(card => card != null && card.GetCardInfo()?.BelongTo == evt.character)
                .ToList();

            // 删除这些卡牌
            foreach (var card in cardsToRemove)
            {
                _deckCards.Remove(card);
                Destroy(card.gameObject);
            }

            // 更新统计信息
            UpdateStatistics();

            Debug.Log($"[ChooseCardPanel] 成功清除 {cardsToRemove.Count} 张 {evt.character} 卡牌，当前卡组数量: {_deckCards.Count}");
        }

        #endregion
    }
}

