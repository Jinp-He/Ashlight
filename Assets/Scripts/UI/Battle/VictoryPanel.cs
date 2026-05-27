using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Ashlight.Battle;

namespace Scripts.UI
{
    /// <summary>
    /// 战斗胜利弹窗。
    /// 由用户在 Unity 中手动搭建 UI 层级后，将 Panel 根节点上挂此脚本，
    /// 并在 Inspector 里拖入 TitleText / ContinueButton / CanvasGroup 引用。
    ///
    /// 外部由 UI_BattleScene 订阅 BattleEndedEvent 后调用 Show() 打开。
    /// Continue 按钮会设置 BattleManager.PendingEncounterId 并重载 BattleScene。
    /// </summary>
    public class VictoryPanel : MonoBehaviour
    {
        #region 序列化字段

        [Header("UI 引用")]
        [SerializeField, Tooltip("标题文本（如：战斗胜利）")]
        private TextMeshProUGUI titleText;

        [SerializeField, Tooltip("继续按钮")]
        private Button continueButton;

        [SerializeField, Tooltip("Panel 根节点的 CanvasGroup（用于整体淡入淡出与射线拦截）")]
        private CanvasGroup canvasGroup;

        [SerializeField, Tooltip("做缩放动画的 Transform；留空时默认用自身 transform")]
        private Transform scaleTarget;

        [Header("文案")]
        [SerializeField, Tooltip("标题文案（胜利）")]
        private string victoryTitle = "战斗胜利";

        [SerializeField, Tooltip("标题文案（失败）。预留：当前仅胜利会打开，失败暂不使用")]
        private string defeatTitle = "战斗失败";

        [Header("下一关配置")]
        [SerializeField, Tooltip("点击继续后加载的遭遇战 ID（在 Excel 的 Encounter 表里先配好）")]
        private string nextEncounterId = "E002";

        [SerializeField, Tooltip("战斗场景名，用于 SceneManager.LoadScene")]
        private string battleSceneName = "BattleScene";

        [Header("动画参数")]
        [SerializeField, Tooltip("弹出动画时长")]
        private float showDuration = 0.4f;

        [SerializeField, Tooltip("关闭动画时长")]
        private float hideDuration = 0.2f;

        [SerializeField, Tooltip("弹出时的起始缩放")]
        private float startScale = 0.3f;

        #endregion

        #region 私有字段

        private Sequence _currentTween;
        private bool _isShown;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            if (scaleTarget == null)
            {
                scaleTarget = transform;
            }

            // 默认隐藏（场景里可以保留激活，由 Awake 统一设为不可见态）
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            if (scaleTarget != null)
            {
                scaleTarget.localScale = Vector3.one * startScale;
            }

            gameObject.SetActive(false);

            if (continueButton != null)
            {
                continueButton.onClick.AddListener(OnContinueClicked);
            }
        }

        private void OnDestroy()
        {
            if (continueButton != null)
            {
                continueButton.onClick.RemoveListener(OnContinueClicked);
            }

            _currentTween?.Kill();
            _currentTween = null;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 播放弹出动画（缩放 0.3→1 OutBack + 淡入）。
        /// </summary>
        /// <param name="isVictory">是否胜利，决定标题文案</param>
        public void Show(bool isVictory = true)
        {
            if (_isShown)
            {
                return;
            }

            _isShown = true;

            if (titleText != null)
            {
                titleText.text = isVictory ? victoryTitle : defeatTitle;
            }

            gameObject.SetActive(true);

            _currentTween?.Kill();
            _currentTween = DOTween.Sequence();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                _currentTween.Join(canvasGroup.DOFade(1f, showDuration));
            }

            if (scaleTarget != null)
            {
                scaleTarget.localScale = Vector3.one * startScale;
                _currentTween.Join(scaleTarget.DOScale(Vector3.one, showDuration).SetEase(Ease.OutBack));
            }

            _currentTween.SetUpdate(true); // 不受 Time.timeScale 影响
            _currentTween.OnComplete(() =>
            {
                if (canvasGroup != null)
                {
                    canvasGroup.interactable = true;
                    canvasGroup.blocksRaycasts = true;
                }
            });
        }

        /// <summary>
        /// 播放收起动画（反向）。目前暂未使用，仅保留给未来需要关闭弹窗的场景。
        /// </summary>
        public void Hide()
        {
            if (!_isShown)
            {
                return;
            }

            _isShown = false;

            _currentTween?.Kill();
            _currentTween = DOTween.Sequence();

            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                _currentTween.Join(canvasGroup.DOFade(0f, hideDuration));
            }

            if (scaleTarget != null)
            {
                _currentTween.Join(scaleTarget.DOScale(Vector3.one * startScale, hideDuration).SetEase(Ease.InBack));
            }

            _currentTween.SetUpdate(true);
            _currentTween.OnComplete(() => gameObject.SetActive(false));
        }

        #endregion

        #region 按钮回调

        private void OnContinueClicked()
        {
            // 记录下一关 EncounterId，新的 BattleScene 启动时会读取并消费
            BattleManager.PendingEncounterId = nextEncounterId;
            Debug.Log($"[VictoryPanel] 继续：加载下一关 EncounterId={nextEncounterId}, Scene={battleSceneName}");

            // 重载 BattleScene（同名重载即可，新场景会重新拉取 PendingEncounterId）
            SceneManager.LoadScene(battleSceneName);
        }

        #endregion
    }
}
