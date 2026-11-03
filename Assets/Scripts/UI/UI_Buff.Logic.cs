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

        /// <summary>Inspector 未赋值 descriptionViewControllerPrefab 时的兜底路径（Resources 相对，不含扩展名）。</summary>
        private const string DescriptionPrefabResourcePath = "UI/常用UI/PageViewer/DescriptionViewController";
        private static GameObject _cachedDescriptionPrefab;

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

            // 与 IntentionView 一致：Awake 阶段预先创建 tooltip 实例（挂到根 Canvas、先隐藏）
            CreateDescriptionView();
        }

        /// <summary>与 IntentionView.CreateDescriptionView 同套：预先实例化 tooltip、挂到根 Canvas 并隐藏。</summary>
        private void CreateDescriptionView()
        {
            if (_descriptionView != null) return;

            var prefab = descriptionViewControllerPrefab != null
                ? descriptionViewControllerPrefab
                : ResolveDescriptionPrefab();
            if (prefab == null) return;

            if (_parentCanvas == null) _parentCanvas = GetComponentInParent<Canvas>();
            var hostCanvas = _parentCanvas != null ? _parentCanvas.rootCanvas : null;
            if (hostCanvas == null) return;

            var obj = Instantiate(prefab, hostCanvas.transform);
            _descriptionView = obj.GetComponent<DescriptionViewController>();
            if (_descriptionView != null)
            {
                _descriptionView.Hide();
            }
            else
            {
                Debug.LogError("[UI_Buff] descriptionViewControllerPrefab 上未找到 DescriptionViewController 组件");
                Destroy(obj);
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
            Debug.Log($"[UI_Buff] OnPointerEnter: buff={_buffState?.BuffId ?? "(null)"}, view={( _descriptionView != null)}");

            if (_buffInfo == null) return;

            // 与 IntentionView 同：Awake 已建好 tooltip；这里兜底再试一次
            if (_descriptionView == null) CreateDescriptionView();
            if (_descriptionView == null) return;

            _descriptionView.Show(_buffInfo, _buffState);
            _descriptionView.transform.SetAsLastSibling();
            PositionTooltip(eventData);
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
        /// 按 Canvas 渲染模式正确定位 tooltip（与 IntentionView 同一套）：把「鼠标屏幕坐标 + 偏移」用
        /// ScreenPointToWorldPointInRectangle 换算到 Canvas 平面，兼容 Overlay / Camera / World 三种模式。
        /// 旧实现直接用 transform.position（世界坐标）+ 像素偏移，在非 Overlay 画布下会把 tooltip 丢到屏幕外。
        /// </summary>
        private void PositionTooltip(PointerEventData eventData)
        {
            if (_descriptionView == null) return;

            var canvas = _descriptionView.GetComponentInParent<Canvas>();
            var canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (canvasRect == null) return;

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (eventData != null ? (eventData.pressEventCamera ?? eventData.enterEventCamera) : canvas.worldCamera);

            // 有鼠标事件用鼠标屏幕坐标；没有则回退到本图标的屏幕坐标
            Camera screenCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 screenPoint = (eventData != null)
                ? eventData.position + descriptionOffset
                : (Vector2)RectTransformUtility.WorldToScreenPoint(screenCam, transform.position) + descriptionOffset;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, screenPoint, cam, out var worldPoint))
            {
                _descriptionView.SetPosition(worldPoint);
            }
        }

        private void HideTooltip()
        {
            if (_descriptionView != null)
            {
                _descriptionView.Hide();
            }
        }

        /// <summary>
        /// 兜底解析 DescriptionViewController 预制体：Inspector 未赋值时从 Resources 加载并缓存。
        /// 找不到时返回 null（CreateDescriptionView 会静默跳过，不报错刷屏）。
        /// </summary>
        private static GameObject ResolveDescriptionPrefab()
        {
            if (_cachedDescriptionPrefab == null)
            {
                _cachedDescriptionPrefab = Resources.Load<GameObject>(DescriptionPrefabResourcePath);
                if (_cachedDescriptionPrefab == null)
                {
                    Debug.LogWarning($"[UI_Buff] 未在 Inspector 赋值 descriptionViewControllerPrefab，也未能从 Resources 找到：{DescriptionPrefabResourcePath}");
                }
            }
            return _cachedDescriptionPrefab;
        }

        #endregion
    }
}
