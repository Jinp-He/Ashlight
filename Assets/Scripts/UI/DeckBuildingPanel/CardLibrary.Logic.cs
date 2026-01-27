using System.Collections.Generic;
using UnityEngine;
using cfg;
using cfg.Character;
using Ashlight.Config;
using Ashlight.UI;

namespace Scripts.UI
{
    /// <summary>
    /// CardLibrary的业务逻辑部分（手动编写）
    /// </summary>
    public partial class CardLibrary
    {
        #region 序列化字段

        [Header("卡牌预制体")]
        [SerializeField]
        [Tooltip("CardViewController预制体，用于实例化卡牌")]
        private GameObject cardViewControllerPrefab;

        [Header("显示设置")]
        [SerializeField]
        [Tooltip("卡牌显示模式（默认为阅览模式）")]
        private DescriptionMode displayMode = DescriptionMode.View;

        [Header("分页组件")]
        [SerializeField]
        [Tooltip("PageViewer组件，用于分页显示卡牌")]
        private PageViewer pageViewer;

        #endregion

        #region 私有字段

        private List<CardInfo> _allCards = new List<CardInfo>();
        private List<CardViewController> _instantiatedCards = new List<CardViewController>();
        private string _currentSearchText = string.Empty;
        private CharacterEnum? _currentCharacterFilter = null;

        #endregion

        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();
        }

        /// <summary>
        /// 启动时初始化卡牌库
        /// </summary>
        private void Start()
        {
            InitializeCardLibrary();
            SetupSearchListener();
        }

        /// <summary>
        /// 销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            ClearCards();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 刷新卡牌库（重新加载所有卡牌）
        /// </summary>
        public void RefreshLibrary()
        {
            InitializeCardLibrary();
        }

        /// <summary>
        /// 设置显示模式
        /// </summary>
        /// <param name="mode">显示模式</param>
        public void SetDisplayMode(DescriptionMode mode)
        {
            displayMode = mode;
            // 更新所有已实例化的卡牌
            foreach (var cardView in _instantiatedCards)
            {
                if (cardView != null)
                {
                    cardView.SetDisplayMode(mode);
                }
            }
        }

        /// <summary>
        /// 根据角色更新卡牌库显示
        /// </summary>
        /// <param name="character">要显示的角色</param>
        public void UpdateByCharacter(CharacterEnum character)
        {
            // ✅ 检查是否重复点击同一个角色按钮
            if (_currentCharacterFilter.HasValue && _currentCharacterFilter.Value == character)
            {
                // 重复点击同一个角色，取消过滤，显示所有卡牌
                Debug.Log($"[CardLibrary] 重复点击角色按钮 {character}，取消过滤，显示所有卡牌");
                ClearCharacterFilter();
                return;
            }
            
            Debug.Log($"[CardLibrary] 按角色过滤卡牌: {character}");
            
            _currentCharacterFilter = character;
            
            // 清理现有卡牌
            ClearCards();
            
            // 重新实例化符合角色的卡牌
            InstantiateCards();
            
            // 刷新分页显示
            RefreshPageViewer();
            
            Debug.Log($"[CardLibrary] 角色过滤完成，显示 {_instantiatedCards.Count} 张卡牌");
        }

        /// <summary>
        /// 清除角色过滤，显示所有卡牌
        /// </summary>
        public void ClearCharacterFilter()
        {
            Debug.Log("[CardLibrary] 清除角色过滤");
            
            _currentCharacterFilter = null;
            
            // 清理现有卡牌
            ClearCards();
            
            // 重新实例化所有卡牌
            InstantiateCards();
            
            // 刷新分页显示
            RefreshPageViewer();
            
            Debug.Log($"[CardLibrary] 显示所有卡牌，共 {_instantiatedCards.Count} 张");
        }

        #endregion

        #region 私有方法 - 初始化

        /// <summary>
        /// 初始化卡牌库
        /// </summary>
        private void InitializeCardLibrary()
        {
            // 验证必要组件
            if (!ValidateComponents())
            {
                return;
            }

            // 清理现有卡牌
            ClearCards();

            // 从ConfigLoader加载所有卡牌配置
            LoadAllCards();

            // 实例化并显示卡牌
            InstantiateCards();

            // 刷新分页显示
            RefreshPageViewer();

            Debug.Log($"[CardLibrary] 卡牌库初始化完成，共加载 {_allCards.Count} 张卡牌，实例化 {_instantiatedCards.Count} 张");
        }

        /// <summary>
        /// 验证必要组件
        /// </summary>
        private bool ValidateComponents()
        {
            if (cardViewControllerPrefab == null)
            {
                Debug.LogError("[CardLibrary] CardViewControllerPrefab 未设置！请在Inspector中指定预制体。");
                return false;
            }

            if (CardLibraryContainer == null)
            {
                Debug.LogError("[CardLibrary] CardLibraryContainer 未绑定！");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 从ConfigLoader加载所有卡牌
        /// </summary>
        private void LoadAllCards()
        {
            _allCards.Clear();

            // 获取配置表
            var tables = ConfigLoader.Tables;
            if (tables == null || tables.TbCardInfo == null)
            {
                Debug.LogError("[CardLibrary] 配置表未加载或TbCardInfo为空！请先调用ConfigLoader.Load()");
                return;
            }

            // 获取所有卡牌数据
            var cardList = tables.TbCardInfo.DataList;
            if (cardList == null || cardList.Count == 0)
            {
                Debug.LogWarning("[CardLibrary] 卡牌配置表为空！");
                return;
            }

            // 加载所有卡牌（可以在这里添加过滤逻辑，比如过滤掉锁定的卡牌）
            foreach (var cardInfo in cardList)
            {
                if (cardInfo != null)
                {
                    _allCards.Add(cardInfo);
                }
            }

            Debug.Log($"[CardLibrary] 成功加载 {_allCards.Count} 张卡牌配置");
        }

        /// <summary>
        /// 实例化卡牌
        /// </summary>
        private void InstantiateCards()
        {
            foreach (var cardInfo in _allCards)
            {
                // 只应用角色过滤（不应用搜索过滤，搜索过滤由FilterCards处理）
                if (_currentCharacterFilter.HasValue && cardInfo.BelongTo != _currentCharacterFilter.Value)
                {
                    continue;
                }

                // 实例化卡牌预制体
                GameObject cardObj = Instantiate(cardViewControllerPrefab, CardLibraryContainer);
                if (cardObj == null)
                {
                    Debug.LogError($"[CardLibrary] 实例化卡牌失败：{cardInfo.Name}");
                    continue;
                }

                // 获取CardViewController组件
                CardViewController cardView = cardObj.GetComponent<CardViewController>();
                if (cardView == null)
                {
                    Debug.LogError($"[CardLibrary] CardViewController组件未找到：{cardInfo.Name}");
                    Destroy(cardObj);
                    continue;
                }

                // 初始化卡牌数据
                cardView.Initialize(cardInfo, displayMode);

                // 添加到列表
                _instantiatedCards.Add(cardView);
            }

            Debug.Log($"[CardLibrary] 成功实例化 {_instantiatedCards.Count} 张卡牌（角色过滤：{(_currentCharacterFilter.HasValue ? _currentCharacterFilter.Value.ToString() : "无")}）");
        }

        /// <summary>
        /// 清理所有已实例化的卡牌
        /// </summary>
        private void ClearCards()
        {
            foreach (var cardView in _instantiatedCards)
            {
                if (cardView != null)
                {
                    Destroy(cardView.gameObject);
                }
            }

            _instantiatedCards.Clear();
        }

        #endregion

        #region 私有方法 - 搜索功能

        /// <summary>
        /// 设置搜索框监听
        /// </summary>
        private void SetupSearchListener()
        {
            if (Search != null)
            {
                // 监听搜索框内容变化
                Search.onValueChanged.AddListener(OnSearchTextChanged);
            }
        }

        /// <summary>
        /// 搜索文本变化回调
        /// </summary>
        private void OnSearchTextChanged(string searchText)
        {
            _currentSearchText = searchText.Trim().ToLower();

            // 重新过滤和显示卡牌
            FilterCards();
        }

        /// <summary>
        /// 过滤卡牌显示
        /// </summary>
        private void FilterCards()
        {
            int visibleCount = 0;

            // 遍历所有已实例化的卡牌
            foreach (var cardView in _instantiatedCards)
            {
                if (cardView != null)
                {
                    // 从CardViewController获取对应的CardInfo
                    var cardInfo = cardView.GetCardInfo();
                    
                    if (cardInfo != null)
                    {
                        // 应用搜索过滤
                        bool shouldShow = PassSearchFilter(cardInfo);
                        cardView.gameObject.SetActive(shouldShow);

                        if (shouldShow)
                        {
                            visibleCount++;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[CardLibrary] CardViewController未包含CardInfo数据");
                        cardView.gameObject.SetActive(false);
                    }
                }
            }

            // 刷新分页显示
            RefreshPageViewer();

            Debug.Log($"[CardLibrary] 搜索结果：显示 {visibleCount}/{_instantiatedCards.Count} 张卡牌");
        }

        /// <summary>
        /// 检查卡牌是否通过搜索过滤
        /// </summary>
        private bool PassSearchFilter(CardInfo cardInfo)
        {
            // 首先检查角色过滤
            if (_currentCharacterFilter.HasValue && cardInfo.BelongTo != _currentCharacterFilter.Value)
            {
                return false;
            }

            // 如果搜索文本为空，显示所有符合角色过滤的卡牌
            if (string.IsNullOrEmpty(_currentSearchText))
            {
                return true;
            }

            // 搜索卡牌名称
            if (!string.IsNullOrEmpty(cardInfo.Name) && 
                cardInfo.Name.ToLower().Contains(_currentSearchText))
            {
                return true;
            }

            // 搜索卡牌描述
            if (!string.IsNullOrEmpty(cardInfo.Description) && 
                cardInfo.Description.ToLower().Contains(_currentSearchText))
            {
                return true;
            }

            // 可以添加更多搜索条件，比如按ID、稀有度等

            return false;
        }

        /// <summary>
        /// 刷新PageViewer分页显示
        /// </summary>
        private void RefreshPageViewer()
        {
            if (pageViewer != null)
            {
                pageViewer.Refresh();
            }
            else
            {
                Debug.LogWarning("[CardLibrary] PageViewer未设置，无法刷新分页！");
            }
        }

        #endregion
    }
}

