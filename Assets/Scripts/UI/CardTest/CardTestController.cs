using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ashlight.Common.Utils;
using Ashlight.Config;
using cfg;
using cfg.Character;
using cfg.Enemy;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Scripts.UI.CardTest
{
    /// <summary>
    /// 卡牌测试场景的 UI 无关控制器。
    ///
    /// UI 只负责：
    /// 1. 用 GetCardOptionLabels / GetEnemyOptionLabels 填充列表；
    /// 2. 将列表 OnValueChanged 绑定到 SelectCard / SelectEnemy；
    /// 3. 将按钮绑定到 AddSelectedCard / AddSelectedEnemy；
    /// 4. 卡牌预览直接在指定容器内实例化 CardViewController。
    /// </summary>
    public sealed class CardTestController : MonoBehaviour
    {
        [Serializable]
        public sealed class StringEvent : UnityEvent<string> { }

        [Serializable]
        public sealed class SpriteEvent : UnityEvent<Sprite> { }

        [Header("战斗场景")]
        [SerializeField] private UI_BattleScene battleScene;

        [Header("控制台面板")]
        [Tooltip("控制显隐的 CanvasGroup；留空时自动使用或添加当前物体上的 CanvasGroup")]
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField] private KeyCode togglePanelKey = KeyCode.BackQuote;
        [SerializeField] private bool panelVisibleOnStart = true;

        [Header("控制台分页")]
        [SerializeField] private ToggleGroup pageToggleGroup;
        [SerializeField] private Toggle cardPageToggle;
        [SerializeField] private Toggle enemyPageToggle;
        [SerializeField] private GameObject cardPage;
        [SerializeField] private GameObject enemyPage;
        [SerializeField] private bool showCardPageOnStart = true;

        [Header("敌人预览事件")]
        [SerializeField] private StringEvent onEnemyPreviewChanged = new StringEvent();
        [SerializeField] private SpriteEvent onEnemyIconChanged = new SpriteEvent();

        [Header("通用反馈")]
        [SerializeField] private StringEvent onOperationResult = new StringEvent();

        [Header("可选 UI 引用（留空则使用上面的事件自行接 UI）")]
        [SerializeField] private TMP_Dropdown cardOwnerDropdown;
        [SerializeField] private TMP_Dropdown cardDropdown;
        [SerializeField] private TMP_InputField cardSearchInput;
        [Tooltip("留空时自动从 Resources/Cards/Prefabs/CardViewController 加载")]
        [SerializeField] private GameObject cardViewControllerPrefab;
        [SerializeField] private RectTransform cardPreviewContainer;
        [SerializeField] private Button addCardButton;
        [Space]
        [SerializeField] private TMP_Dropdown enemyDropdown;
        [SerializeField] private TMP_InputField enemySearchInput;
        [Tooltip("敌人模型预览容器；留空时会在 EnemyPanel 下自动创建 EnemyContainer")]
        [SerializeField] private RectTransform enemyContainer;
        [SerializeField] private TMP_Text enemyPreviewText;
        [SerializeField] private Image enemyPreviewImage;
        [SerializeField] private Button addEnemyButton;
        [Space]
        [SerializeField] private TMP_Text operationResultText;

        private readonly List<CardInfo> _visibleCards = new List<CardInfo>();
        private readonly List<EnemyInfo> _visibleEnemies = new List<EnemyInfo>();

        private string _cardSearch = string.Empty;
        private string _enemySearch = string.Empty;
        private CharacterEnum? _cardOwnerFilter;
        private bool _showExtraCards;
        private int _selectedCardIndex = -1;
        private int _selectedEnemyIndex = -1;
        private bool _optionalUiBound;
        private CardViewController _cardPreviewInstance;
        private Enemy _enemyPreviewInstance;

        public IReadOnlyList<CardInfo> VisibleCards => _visibleCards;
        public IReadOnlyList<EnemyInfo> VisibleEnemies => _visibleEnemies;
        public CardInfo SelectedCard => IsValidIndex(_selectedCardIndex, _visibleCards.Count)
            ? _visibleCards[_selectedCardIndex]
            : null;
        public EnemyInfo SelectedEnemy => IsValidIndex(_selectedEnemyIndex, _visibleEnemies.Count)
            ? _visibleEnemies[_selectedEnemyIndex]
            : null;

        public string SelectedEnemyPreview { get; private set; } = string.Empty;
        public Sprite SelectedEnemyIcon { get; private set; }
        public CardViewController CardPreviewInstance => _cardPreviewInstance;
        public bool IsPanelVisible { get; private set; }

        private void Awake()
        {
            ResolveBattleScene();
            EnsurePanelCanvasGroup();
            SetPanelVisible(panelVisibleOnStart);
            BindOptionalUi();
            SetActivePage(showCardPageOnStart ? 0 : 1);
            RefreshCatalogs();
        }

        private void Update()
        {
            if (Input.GetKeyDown(togglePanelKey))
            {
                TogglePanel();
            }
        }

        private void OnDestroy()
        {
            UnbindOptionalUi();
            ClearCardPreview();
            ClearEnemyPreview();
        }

        /// <summary>配置表更新后可由 UI 的“刷新”按钮调用。</summary>
        public void RefreshCatalogs()
        {
            ConfigLoader.Load();
            ApplyCardFilter();
            ApplyEnemyFilter();
        }

        /// <summary>供快捷键或 UI 按钮调用。</summary>
        public void TogglePanel()
        {
            SetPanelVisible(!IsPanelVisible);
        }

        public void SetPanelVisible(bool visible)
        {
            EnsurePanelCanvasGroup();
            IsPanelVisible = visible;
            if (panelCanvasGroup == null) return;

            panelCanvasGroup.alpha = visible ? 1f : 0f;
            panelCanvasGroup.interactable = visible;
            panelCanvasGroup.blocksRaycasts = visible;
        }

        /// <summary>显示卡牌页，可直接绑定按钮或 Toggle。</summary>
        public void ShowCardPage()
        {
            SetActivePage(0);
        }

        /// <summary>显示敌人页，可直接绑定按钮或 Toggle。</summary>
        public void ShowEnemyPage()
        {
            SetActivePage(1);
        }

        /// <summary>0=卡牌页，1=敌人页。</summary>
        public void SetActivePage(int pageIndex)
        {
            bool showCards = pageIndex != 1;

            if (cardPage != null) cardPage.SetActive(showCards);
            if (enemyPage != null) enemyPage.SetActive(!showCards);

            if (cardPageToggle != null) cardPageToggle.SetIsOnWithoutNotify(showCards);
            if (enemyPageToggle != null) enemyPageToggle.SetIsOnWithoutNotify(!showCards);
        }

        public List<string> GetCardOptionLabels()
        {
            return _visibleCards
                .Select(card => card.Name)
                .ToList();
        }

        public List<string> GetEnemyOptionLabels()
        {
            return _visibleEnemies
                .Select(enemy => $"{enemy.Id}  {enemy.Name}")
                .ToList();
        }

        public void SetCardSearch(string value)
        {
            _cardSearch = value ?? string.Empty;
            ApplyCardFilter();
        }

        public void SetEnemySearch(string value)
        {
            _enemySearch = value ?? string.Empty;
            ApplyEnemyFilter();
        }

        /// <summary>代码侧过滤。传 null 显示全部角色。</summary>
        public void SetCardOwnerFilter(CharacterEnum? owner)
        {
            _cardOwnerFilter = owner;
            _showExtraCards = false;
            ApplyCardFilter();
        }

        /// <summary>
        /// 方便直接绑定 Dropdown：0=全部，1=Irene，2=Rocket，3=Zhouzhou，4=Extra。
        /// </summary>
        public void SetCardOwnerFilterFromDropdown(int dropdownIndex)
        {
            switch (dropdownIndex)
            {
                case 1:
                    SetCardOwnerFilter(CharacterEnum.Irene);
                    break;
                case 2:
                    SetCardOwnerFilter(CharacterEnum.Rocket);
                    break;
                case 3:
                    SetCardOwnerFilter(CharacterEnum.Zhouzhou);
                    break;
                case 4:
                    _cardOwnerFilter = null;
                    _showExtraCards = true;
                    ApplyCardFilter();
                    break;
                default:
                    SetCardOwnerFilter(null);
                    break;
            }
        }

        public void SelectCard(int index)
        {
            _selectedCardIndex = IsValidIndex(index, _visibleCards.Count) ? index : -1;
            RefreshCardPreview();
        }

        public bool SelectCardById(string cardId)
        {
            int index = _visibleCards.FindIndex(card =>
                string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase));
            SelectCard(index);
            return index >= 0;
        }

        public void SelectEnemy(int index)
        {
            _selectedEnemyIndex = IsValidIndex(index, _visibleEnemies.Count) ? index : -1;
            RefreshEnemyPreview();
        }

        public bool SelectEnemyById(string enemyId)
        {
            int index = _visibleEnemies.FindIndex(enemy =>
                string.Equals(enemy.Id, enemyId, StringComparison.OrdinalIgnoreCase));
            SelectEnemy(index);
            return index >= 0;
        }

        public void AddSelectedCard()
        {
            var card = SelectedCard;
            if (card == null)
            {
                Report("请先选择一张卡牌。", false);
                return;
            }

            AddCardById(card.Id);
        }

        public bool AddCardById(string cardId)
        {
            if (!ResolveBattleScene())
            {
                Report("未找到 UI_BattleScene，无法添加卡牌。", false);
                return false;
            }

            if (!battleScene.TryAddCardForCardTest(cardId, out string message))
            {
                Report(message, false);
                return false;
            }

            Report(message, true);
            return true;
        }

        public void AddSelectedEnemy()
        {
            var enemy = SelectedEnemy;
            if (enemy == null)
            {
                Report("请先选择一个敌人。", false);
                return;
            }

            AddEnemyById(enemy.Id);
        }

        public bool AddEnemyById(string enemyId)
        {
            if (!ResolveBattleScene())
            {
                Report("未找到 UI_BattleScene，无法添加敌人。", false);
                return false;
            }

            if (!battleScene.TryAddEnemyForCardTest(enemyId, out string unitId, out string message))
            {
                Report(message, false);
                return false;
            }

            Report($"{message}（UnitId: {unitId}）", true);
            return true;
        }

        private void ApplyCardFilter()
        {
            string keepSelectedId = SelectedCard?.Id;
            _visibleCards.Clear();

            var table = ConfigLoader.Tables?.TbCardInfo?.DataList;
            if (table != null)
            {
                _visibleCards.AddRange(table
                    .Where(card => card != null)
                    .Where(card => !_showExtraCards || IsExtraCard(card))
                    .Where(card => !_cardOwnerFilter.HasValue || card.BelongTo == _cardOwnerFilter.Value)
                    .Where(card => Matches(card.Id, card.Name, _cardSearch))
                    .OrderBy(card => card.BelongTo)
                    .ThenBy(card => card.Id, StringComparer.OrdinalIgnoreCase));
            }

            _selectedCardIndex = FindOrFirst(_visibleCards, keepSelectedId, card => card.Id);
            RebuildCardDropdown();
            RefreshCardPreview();
        }

        private void ApplyEnemyFilter()
        {
            string keepSelectedId = SelectedEnemy?.Id;
            _visibleEnemies.Clear();

            var table = ConfigLoader.Tables?.TbEnemyInfo?.DataList;
            if (table != null)
            {
                _visibleEnemies.AddRange(table
                    .Where(enemy => enemy != null)
                    .Where(enemy => Matches(enemy.Id, enemy.Name, _enemySearch))
                    .OrderBy(enemy => enemy.Id, StringComparer.OrdinalIgnoreCase));
            }

            _selectedEnemyIndex = FindOrFirst(_visibleEnemies, keepSelectedId, enemy => enemy.Id);
            RebuildEnemyDropdown();
            RefreshEnemyPreview();
        }

        private void RefreshCardPreview()
        {
            var card = SelectedCard;
            ClearCardPreview();
            if (card == null || cardPreviewContainer == null) return;

            if (cardViewControllerPrefab == null)
            {
                string resourcePath = AssetPath.GetResourcesPath(AssetPath.CardViewControllerPath);
                cardViewControllerPrefab = Resources.Load<GameObject>(resourcePath);
            }

            if (cardViewControllerPrefab == null)
            {
                Report("找不到 CardViewController prefab，无法创建卡牌预览。", false);
                return;
            }

            GameObject previewObject = Instantiate(cardViewControllerPrefab, cardPreviewContainer, false);
            // Prefab 或其场景副本可能处于对象池隐藏态；先激活根节点，确保 Awake/UIBind 已完成。
            previewObject.SetActive(true);
            _cardPreviewInstance = previewObject.GetComponent<CardViewController>();
            if (_cardPreviewInstance == null)
            {
                Destroy(previewObject);
                Report("CardViewController prefab 上没有 CardViewController 组件。", false);
                return;
            }

            _cardPreviewInstance.Initialize(card, DescriptionMode.View);
            _cardPreviewInstance.ResetForReuse();
            _cardPreviewInstance.SetDisplayMode(DescriptionMode.View);
            _cardPreviewInstance.Show();

            // Show() 只控制 CardViewController 根节点；预览还必须显式打开完整卡面的 Card 子节点。
            if (_cardPreviewInstance.Card != null)
            {
                _cardPreviewInstance.Card.gameObject.SetActive(true);
                _cardPreviewInstance.Card.alpha = 1f;
            }

            var rect = previewObject.transform as RectTransform;
            if (rect != null)
            {
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
            }
        }

        private void RefreshEnemyPreview()
        {
            var enemy = SelectedEnemy;
            ClearEnemyPreview();
            CreateEnemyPreview(enemy);
            SelectedEnemyPreview = enemy != null ? BuildEnemyPreview(enemy) : "没有符合条件的敌人。";
            SelectedEnemyIcon = enemy != null
                ? Resources.Load<Sprite>(AssetPath.GetActionOrderIconPath(enemy.Id))
                : null;
            if (enemyPreviewText != null) enemyPreviewText.text = SelectedEnemyPreview;
            ApplyPreviewSprite(enemyPreviewImage, SelectedEnemyIcon);
            onEnemyPreviewChanged?.Invoke(SelectedEnemyPreview);
            onEnemyIconChanged?.Invoke(SelectedEnemyIcon);
        }

        private static string BuildEnemyPreview(EnemyInfo enemy)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"{enemy.Name}  [{enemy.Id}]");
            builder.AppendLine($"HP：{enemy.Hp}    速度：{enemy.Speed}    精英：{(enemy.IsElite ? "是" : "否")}");
            builder.AppendLine($"初始站位：{enemy.StartRow}    资源：{enemy.AlternativePath}");

            var skills = new List<string>();
            if (enemy.IntentionSet != null)
            {
                foreach (var group in enemy.IntentionSet)
                {
                    if (group?.EnemyIntentionList == null) continue;
                    foreach (var intention in group.EnemyIntentionList)
                    {
                        if (intention == null) continue;
                        string skillName = intention.EnemySkillIndex_Ref?.Name ?? intention.EnemySkillIndex;
                        skills.Add($"{skillName} [{intention.EnemySkillIndex}] / {intention.EnemyIntentionType}");
                    }
                }
            }

            builder.AppendLine($"技能组：{Math.Max(0, enemy.IntentionSet?.Count ?? 0)}");
            foreach (string skill in skills.Distinct())
            {
                builder.AppendLine($"- {skill}");
            }

            return builder.ToString().TrimEnd();
        }

        private void ClearCardPreview()
        {
            if (_cardPreviewInstance == null) return;
            _cardPreviewInstance.gameObject.SetActive(false);
            Destroy(_cardPreviewInstance.gameObject);
            _cardPreviewInstance = null;
        }

        private void CreateEnemyPreview(EnemyInfo enemy)
        {
            if (enemy == null || !ResolveBattleScene()) return;

            EnsureEnemyPreviewContainer();
            GameObject enemyPrefab = battleScene.EnemyPrefabForCardTest;
            if (enemyContainer == null || enemyPrefab == null)
            {
                if (enemyPrefab == null) Report("UI_BattleScene 未绑定 Enemy prefab，无法创建敌人预览。", false);
                return;
            }

            GameObject previewObject = Instantiate(enemyPrefab, enemyContainer, false);
            previewObject.name = $"EnemyPreview_{enemy.Id}";
            previewObject.SetActive(true);
            _enemyPreviewInstance = previewObject.GetComponent<Enemy>();
            if (_enemyPreviewInstance == null)
            {
                Destroy(previewObject);
                Report("Enemy prefab 上没有 Enemy 组件。", false);
                return;
            }

            _enemyPreviewInstance.Initialize(enemy);
            var rect = previewObject.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one * 0.6f;
            }
        }

        private void ClearEnemyPreview()
        {
            if (_enemyPreviewInstance == null) return;
            Destroy(_enemyPreviewInstance.gameObject);
            _enemyPreviewInstance = null;
        }

        private void EnsureEnemyPreviewContainer()
        {
            if (enemyContainer != null || enemyPage == null) return;

            var container = new GameObject("EnemyContainer", typeof(RectTransform));
            enemyContainer = container.GetComponent<RectTransform>();
            enemyContainer.SetParent(enemyPage.transform, false);
            enemyContainer.anchorMin = enemyContainer.anchorMax = new Vector2(0.5f, 0.5f);
            enemyContainer.pivot = new Vector2(0.5f, 0f);
            enemyContainer.anchoredPosition = new Vector2(208f, -138f);
            enemyContainer.sizeDelta = new Vector2(260f, 270f);
        }

        private bool ResolveBattleScene()
        {
            if (battleScene != null) return true;
            battleScene = FindObjectOfType<UI_BattleScene>();
            return battleScene != null;
        }

        private void EnsurePanelCanvasGroup()
        {
            if (panelCanvasGroup != null) return;
            panelCanvasGroup = GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void Report(string message, bool success)
        {
            string output = success ? $"成功：{message}" : $"失败：{message}";
            if (success) Debug.Log($"[CardTestController] {output}");
            else Debug.LogWarning($"[CardTestController] {output}");
            if (operationResultText != null) operationResultText.text = output;
            onOperationResult?.Invoke(output);
        }

        private void BindOptionalUi()
        {
            if (_optionalUiBound) return;
            _optionalUiBound = true;

            if (cardOwnerDropdown != null)
            {
                cardOwnerDropdown.ClearOptions();
                cardOwnerDropdown.AddOptions(new List<string> { "全部", "Irene", "Rocket", "Zhouzhou", "Extra" });
                cardOwnerDropdown.SetValueWithoutNotify(0);
                cardOwnerDropdown.onValueChanged.AddListener(SetCardOwnerFilterFromDropdown);
            }
            if (cardDropdown != null) cardDropdown.onValueChanged.AddListener(SelectCard);
            if (cardSearchInput != null) cardSearchInput.onValueChanged.AddListener(SetCardSearch);
            if (addCardButton != null) addCardButton.onClick.AddListener(AddSelectedCard);

            if (enemyDropdown != null) enemyDropdown.onValueChanged.AddListener(SelectEnemy);
            if (enemySearchInput != null) enemySearchInput.onValueChanged.AddListener(SetEnemySearch);
            if (addEnemyButton != null) addEnemyButton.onClick.AddListener(AddSelectedEnemy);

            if (pageToggleGroup != null)
            {
                pageToggleGroup.allowSwitchOff = false;
                if (cardPageToggle != null) cardPageToggle.group = pageToggleGroup;
                if (enemyPageToggle != null) enemyPageToggle.group = pageToggleGroup;
            }
            if (cardPageToggle != null) cardPageToggle.onValueChanged.AddListener(OnCardPageToggleChanged);
            if (enemyPageToggle != null) enemyPageToggle.onValueChanged.AddListener(OnEnemyPageToggleChanged);
        }

        private void UnbindOptionalUi()
        {
            if (!_optionalUiBound) return;
            _optionalUiBound = false;

            if (cardOwnerDropdown != null) cardOwnerDropdown.onValueChanged.RemoveListener(SetCardOwnerFilterFromDropdown);
            if (cardDropdown != null) cardDropdown.onValueChanged.RemoveListener(SelectCard);
            if (cardSearchInput != null) cardSearchInput.onValueChanged.RemoveListener(SetCardSearch);
            if (addCardButton != null) addCardButton.onClick.RemoveListener(AddSelectedCard);

            if (enemyDropdown != null) enemyDropdown.onValueChanged.RemoveListener(SelectEnemy);
            if (enemySearchInput != null) enemySearchInput.onValueChanged.RemoveListener(SetEnemySearch);
            if (addEnemyButton != null) addEnemyButton.onClick.RemoveListener(AddSelectedEnemy);

            if (cardPageToggle != null) cardPageToggle.onValueChanged.RemoveListener(OnCardPageToggleChanged);
            if (enemyPageToggle != null) enemyPageToggle.onValueChanged.RemoveListener(OnEnemyPageToggleChanged);
        }

        private void OnCardPageToggleChanged(bool isOn)
        {
            if (isOn) SetActivePage(0);
        }

        private void OnEnemyPageToggleChanged(bool isOn)
        {
            if (isOn) SetActivePage(1);
        }

        private void RebuildCardDropdown()
        {
            if (cardDropdown == null) return;
            cardDropdown.ClearOptions();
            cardDropdown.AddOptions(GetCardOptionLabels());
            cardDropdown.SetValueWithoutNotify(Mathf.Max(0, _selectedCardIndex));
            cardDropdown.interactable = _visibleCards.Count > 0;
            cardDropdown.RefreshShownValue();
        }

        private void RebuildEnemyDropdown()
        {
            if (enemyDropdown == null) return;
            enemyDropdown.ClearOptions();
            enemyDropdown.AddOptions(GetEnemyOptionLabels());
            enemyDropdown.SetValueWithoutNotify(Mathf.Max(0, _selectedEnemyIndex));
            enemyDropdown.interactable = _visibleEnemies.Count > 0;
            enemyDropdown.RefreshShownValue();
        }

        private static void ApplyPreviewSprite(Image image, Sprite sprite)
        {
            if (image == null) return;
            image.sprite = sprite;
            image.enabled = sprite != null;
            image.preserveAspect = true;
        }

        private static bool Matches(string id, string name, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            return (!string.IsNullOrEmpty(id) && id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                   || (!string.IsNullOrEmpty(name) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsExtraCard(CardInfo card)
        {
            return card != null
                   && !string.IsNullOrEmpty(card.Id)
                   && card.Id.StartsWith("Extra", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidIndex(int index, int count)
        {
            return index >= 0 && index < count;
        }

        private static int FindOrFirst<T>(IReadOnlyList<T> list, string selectedId, Func<T, string> getId)
        {
            if (list == null || list.Count == 0) return -1;
            if (!string.IsNullOrEmpty(selectedId))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(getId(list[i]), selectedId, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return 0;
        }
    }
}
