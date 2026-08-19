using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using DG.Tweening;
using Ashlight.Battle;
using Ashlight.Systems.Map;

namespace Scripts.UI
{
    /// <summary>
    /// 战斗胜利面板（WinPanel）逻辑分部。
    /// UI 绑定由生成的 <see cref="WinPanel"/> 分部提供：Txt_Win（标题）、Btn_NextBattle（继续）。
    ///
    /// 演出：标题文字从右侧滑入，继续按钮随后缓缓淡入。
    /// 由 UI_BattleScene 在 BattleEndedEvent（玩家胜利）时调用 <see cref="Show"/> 打开。
    /// </summary>
    public partial class WinPanel : MonoBehaviour
    {
        #region 序列化字段

        [Header("演出参数")]
        [SerializeField, Tooltip("标题从右侧滑入的起始水平偏移（像素）")]
        private float titleSlideDistance = 600f;

        [SerializeField, Tooltip("标题滑入时长")]
        private float titleSlideDuration = 0.5f;

        [SerializeField, Tooltip("按钮淡入时长")]
        private float buttonFadeDuration = 0.6f;

        [SerializeField, Tooltip("按钮淡入相对标题滑入的延迟")]
        private float buttonFadeDelay = 0.35f;

        [Header("下一关配置")]
        [SerializeField, Tooltip("点击继续后加载的遭遇战 ID（在 Excel 的 Encounter 表里先配好）")]
        private string nextEncounterId = "M102";

        [SerializeField, Tooltip("战斗场景名，用于 SceneManager.LoadScene")]
        private string battleSceneName = "BattleScene";

        #endregion

        #region 私有字段

        private bool _inited;
        private bool _isShown;
        private Vector2 _titleHomePos;
        private CanvasGroup _buttonGroup;
        private Sequence _currentTween;

        #endregion

        #region Unity 生命周期

        private void OnDestroy()
        {
            if (Btn_NextBattle != null)
            {
                Btn_NextBattle.onClick.RemoveListener(OnNextBattleClicked);
            }

            _currentTween?.Kill();
            _currentTween = null;
        }

        #endregion

        #region 公共方法

        public void SetNextEncounterId(string encounterId)
        {
            nextEncounterId = encounterId;
        }

        /// <summary>
        /// 打开胜利面板并播放演出：标题从右滑入 + 按钮缓缓淡入。
        /// </summary>
        public void Show()
        {
            if (_isShown)
            {
                return;
            }
            _isShown = true;

            EnsureInit();

            gameObject.SetActive(true);

            _currentTween?.Kill();
            _currentTween = DOTween.Sequence();
            _currentTween.SetUpdate(true); // 不受 Time.timeScale 影响（战斗结束可能已暂停）

            // 标题：从右侧滑入 + 淡入
            if (Txt_Win != null)
            {
                RectTransform titleRect = Txt_Win.rectTransform;
                titleRect.anchoredPosition = _titleHomePos + new Vector2(titleSlideDistance, 0f);
                Txt_Win.alpha = 0f;

                _currentTween.Join(
                    titleRect.DOAnchorPos(_titleHomePos, titleSlideDuration).SetEase(Ease.OutCubic));
                _currentTween.Join(
                    Txt_Win.DOFade(1f, titleSlideDuration).SetEase(Ease.OutQuad));
            }

            // 按钮：延迟后缓缓淡入
            if (_buttonGroup != null)
            {
                _buttonGroup.alpha = 0f;
                _buttonGroup.interactable = false;
                _buttonGroup.blocksRaycasts = false;

                _currentTween.Insert(
                    buttonFadeDelay,
                    _buttonGroup.DOFade(1f, buttonFadeDuration).SetEase(Ease.InOutQuad));
            }

            _currentTween.OnComplete(() =>
            {
                if (_buttonGroup != null)
                {
                    _buttonGroup.interactable = true;
                    _buttonGroup.blocksRaycasts = true;
                }
            });
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 懒初始化：绑定 UI、记录标题静止位、确保按钮有 CanvasGroup、挂按钮回调。
        /// 即使面板初始为非激活态也可安全调用（GetComponent/序列化值在非激活时仍有效）。
        /// </summary>
        private void EnsureInit()
        {
            if (_inited)
            {
                return;
            }
            _inited = true;

            InitUIBindings();

            if (Txt_Win != null)
            {
                _titleHomePos = Txt_Win.rectTransform.anchoredPosition;
            }

            if (Btn_NextBattle != null)
            {
                _buttonGroup = Btn_NextBattle.GetComponent<CanvasGroup>();
                if (_buttonGroup == null)
                {
                    _buttonGroup = Btn_NextBattle.gameObject.AddComponent<CanvasGroup>();
                }

                Btn_NextBattle.onClick.RemoveListener(OnNextBattleClicked);
                Btn_NextBattle.onClick.AddListener(OnNextBattleClicked);
            }
        }

        private void OnNextBattleClicked()
        {
            if (MapRunSession.Instance != null && MapRunSession.Instance.ContinueAfterBattleVictory())
            {
                return;
            }

            if (string.IsNullOrEmpty(nextEncounterId))
            {
                Debug.LogWarning("[WinPanel] 未配置下一关 EncounterId，保持在结算界面");
                return;
            }

            // 记录下一关 EncounterId，新的 BattleScene 启动时会读取并消费
            BattleManager.PendingEncounterId = nextEncounterId;
            Debug.Log($"[WinPanel] 继续：加载下一关 EncounterId={nextEncounterId}, Scene={battleSceneName}");

            SceneManager.LoadScene(battleSceneName);
        }

        #endregion
    }
}
