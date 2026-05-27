using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using cfg;
using Ashlight.Battle.Core.Data;
using Ashlight.Config;

namespace Scripts.UI
{
    /// <summary>
    /// UI_Buff的业务逻辑部分（手动编写）
    /// 单个Buff图标控制器：加载图标、显示层数/剩余回合、悬停tooltip
    /// </summary>
    public partial class UI_Buff : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        #region 序列化字段

        [Header("描述面板（Tooltip）")]
        [SerializeField]
        [Tooltip("DescriptionViewController预制体，悬停时显示Buff详细描述")]
        private GameObject descriptionViewControllerPrefab;

        [SerializeField]
        [Tooltip("描述面板相对于Buff图标的偏移（像素）")]
        private Vector2 descriptionOffset = new Vector2(80f, 0f);

        #endregion

        #region 私有字段

        private BuffState _buffState;
        private BuffInfo _buffInfo;
        private Canvas _parentCanvas;
        private DescriptionViewController _descriptionView;

        #endregion

        #region Unity生命周期

        private void Awake()
        {
            InitUIBindings();

            // 缓存父Canvas，用于定位tooltip
            _parentCanvas = GetComponentInParent<Canvas>();

            // 确保图标能接收鼠标射线（兜底，prefab默认已开）
            if (Img_AttUp != null)
            {
                Img_AttUp.raycastTarget = true;
            }
        }

        private void OnDestroy()
        {
            if (_descriptionView != null)
            {
                Destroy(_descriptionView.gameObject);
                _descriptionView = null;
            }
        }

        private void OnDisable()
        {
            // 隐藏时立刻关掉tooltip，避免悬空显示
            HideTooltip();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 使用BuffState初始化（从战斗系统中传入的运行时状态）
        /// </summary>
        public void Initialize(BuffState buffState)
        {
            if (buffState == null)
            {
                Debug.LogError("[UI_Buff] 初始化失败：BuffState为null");
                return;
            }

            _buffState = buffState;
            _buffInfo = ConfigLoader.Tables?.TbBuffInfo?.GetOrDefault(buffState.BuffId);

            if (_buffInfo == null)
            {
                Debug.LogWarning($"[UI_Buff] 未找到Buff配置: {buffState.BuffId}");
            }

            LoadIcon(_buffInfo?.IconPath);
            UpdateText();
        }

        /// <summary>
        /// 直接用BuffId初始化（不依赖BuffState，常用于演示/预览）
        /// </summary>
        public void Initialize(string buffId, int remainingDuration, int stackCount)
        {
            _buffState = new BuffState
            {
                BuffId = buffId,
                RemainingDuration = remainingDuration,
                StackCount = stackCount
            };
            _buffInfo = ConfigLoader.Tables?.TbBuffInfo?.GetOrDefault(buffId);

            if (_buffInfo == null)
            {
                Debug.LogWarning($"[UI_Buff] 未找到Buff配置: {buffId}");
            }

            LoadIcon(_buffInfo?.IconPath);
            UpdateText();
        }

        /// <summary>
        /// 重新读取BuffState并刷新显示（外部更改层数/剩余回合后调用）
        /// </summary>
        public void Refresh()
        {
            if (_buffState == null) return;
            UpdateText();

            // 若tooltip正在显示，同步刷新一下数值
            if (_descriptionView != null && _descriptionView.gameObject.activeSelf && _buffInfo != null)
            {
                _descriptionView.Show(_buffInfo, _buffState);
            }
        }

        /// <summary>
        /// 获取当前绑定的BuffState
        /// </summary>
        public BuffState GetBuffState() => _buffState;

        /// <summary>
        /// 获取当前绑定的BuffInfo
        /// </summary>
        public BuffInfo GetBuffInfo() => _buffInfo;

        #endregion

        #region 悬停事件

        public void OnPointerEnter(PointerEventData eventData)
        {
            ShowTooltip();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideTooltip();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 加载Buff图标。iconPath为Resources相对路径（不含扩展名）。
        /// 找不到时不动 sprite，保留 prefab 里 Inspector 设的默认图作为占位。
        /// </summary>
        private void LoadIcon(string iconPath)
        {
            if (Img_AttUp == null) return;

            // 永远启用，避免出现 buff 在但图看不见的状态
            Img_AttUp.enabled = true;

            if (string.IsNullOrEmpty(iconPath))
            {
                return; // 没配 icon path：沿用 prefab 里 Inspector 设的图
            }

            var sprite = Resources.Load<Sprite>(iconPath);
            if (sprite != null)
            {
                Img_AttUp.sprite = sprite;
            }
            // else: 资源缺失时不动 sprite，沿用 prefab 占位图；不打 warning（避免战斗刷屏）
        }

        /// <summary>
        /// 更新数值文本：优先显示层数(>1)，否则显示剩余回合(>0)，都没有则隐藏
        /// </summary>
        private void UpdateText()
        {
            if (Txt_Plies == null || _buffState == null) return;

            int stack = _buffState.StackCount;
            int duration = _buffState.RemainingDuration;
            int maxStack = _buffInfo?.MaxStack ?? 1;

            string display;
            if (maxStack > 1 && stack > 1)
            {
                display = stack.ToString();
            }
            else if (duration > 0)
            {
                display = duration.ToString();
            }
            else
            {
                display = string.Empty;
            }

            Txt_Plies.text = display;
            Txt_Plies.gameObject.SetActive(!string.IsNullOrEmpty(display));
        }

        /// <summary>
        /// 显示tooltip（首次悬停时懒加载实例）
        /// </summary>
        private void ShowTooltip()
        {
            if (_buffInfo == null) return;

            if (_descriptionView == null)
            {
                if (descriptionViewControllerPrefab == null || _parentCanvas == null)
                {
                    // 未配置prefab或无父Canvas，静默跳过
                    return;
                }

                var descObj = Instantiate(descriptionViewControllerPrefab, _parentCanvas.transform);
                _descriptionView = descObj.GetComponent<DescriptionViewController>();
                if (_descriptionView == null)
                {
                    Debug.LogError("[UI_Buff] descriptionViewControllerPrefab 上未找到 DescriptionViewController 组件");
                    Destroy(descObj);
                    return;
                }
            }

            _descriptionView.Show(_buffInfo, _buffState);
            Vector3 pos = transform.position + new Vector3(descriptionOffset.x, descriptionOffset.y, 0f);
            _descriptionView.SetPosition(pos);
        }

        private void HideTooltip()
        {
            if (_descriptionView != null)
            {
                _descriptionView.Hide();
            }
        }

        #endregion
    }
}
