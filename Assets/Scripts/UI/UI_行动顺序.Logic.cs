using UnityEngine;
using UnityEngine.EventSystems;
using cfg.Enemy;

namespace Scripts.UI
{
    /// <summary>
    /// UI_ActionOrder 单张卡片的业务逻辑
    ///
    /// Prefab 子物体结构：
    ///   Img_highlight   — 高亮金框（激活态显示）
    ///   Img_BlackLayer  — 暗色背景
    ///   UnitsPiece      — 角色图片容器
    ///     Img_DogKnight / Img_Irene / Img_Rocket / Img_Zhouzhou / ...
    ///   Img_Attack      — 攻击图标
    ///
    /// 敌人进入执行轨后，hover 本卡会弹出「即将执行技能」的说明（复用 DescriptionViewController，与 IntentionView 同款）。
    /// </summary>
    public partial class UI_行动顺序 : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        // 当前执行技能（仅敌人进入执行轨时有值）；共享 tooltip 由 TurnOrderView 注入
        private EnemySkillInfo _executingSkill;
        private DescriptionViewController _tooltip;

        #region Unity 生命周期

        private void Awake()
        {
            InitUIBindings();

            // 初始状态：关闭高亮和攻击图标
            SetHighlight(false);
            SetAttacking(false);

            EnsureRaycastTarget();
        }

        #endregion

        #region 公共 API

        /// <summary>
        /// 根据 configId 显示对应的角色/敌人图片。
        /// UnitsPiece 下的子物体名称格式为 Img_{configId}（例如 Img_Irene、Img_DogKnight）。
        /// </summary>
        public void Setup(string configId)
        {
            if (UnitsPiece == null)
            {
                Debug.LogWarning("[UI_行动顺序] UnitsPiece 未绑定");
                return;
            }

            // 先隐藏所有子图
            foreach (Transform child in UnitsPiece.transform)
                child.gameObject.SetActive(false);

            // 按名称激活匹配项
            string targetName = $"Img_{configId}";
            Transform target = UnitsPiece.transform.Find(targetName);
            if (target != null)
            {
                target.gameObject.SetActive(true);
            }
            else
            {
                Debug.LogWarning($"[UI_行动顺序] UnitsPiece 中未找到 {targetName}，保留默认子物体");
                // 找不到时显示第一个子物体作为兜底
                if (UnitsPiece.transform.childCount > 0)
                    UnitsPiece.transform.GetChild(0).gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 打开/关闭高亮金框（当前行动单位）
        /// </summary>
        public void SetHighlight(bool active)
        {
            if (Img_highlight != null)
                Img_highlight.gameObject.SetActive(active);
        }

        /// <summary>
        /// 显示/隐藏攻击图标（单位进入执行轨时显示）
        /// </summary>
        public void SetAttacking(bool attacking)
        {
            if (Img_Attack != null)
                Img_Attack.gameObject.SetActive(attacking);
        }

        /// <summary>
        /// 设置"即将执行技能"与共享 tooltip。skill 为 null 表示清除（退出执行轨），并收起 tooltip。
        /// </summary>
        public void SetExecutingSkill(EnemySkillInfo skill, DescriptionViewController tooltip)
        {
            _executingSkill = skill;
            _tooltip = tooltip;
            if (skill == null && _tooltip != null)
                _tooltip.Hide();
        }

        #endregion

        #region Tooltip（悬停显示执行技能说明）

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_executingSkill == null || _tooltip == null) return;
            _tooltip.Show(_executingSkill);
            PositionTooltip(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_tooltip != null) _tooltip.Hide();
        }

        /// <summary>把 tooltip 放到鼠标右侧（复刻 IntentionView 的定位逻辑，兼容 Overlay / Camera 画布）。</summary>
        private void PositionTooltip(PointerEventData eventData)
        {
            if (_tooltip == null) return;

            var canvas = _tooltip.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (eventData != null ? (eventData.pressEventCamera ?? eventData.enterEventCamera) : canvas.worldCamera);

            Vector2 screenPoint = (eventData != null ? eventData.position : (Vector2)Input.mousePosition)
                                  + new Vector2(120f, 0f);

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, screenPoint, cam, out var world))
                _tooltip.SetPosition(world);
        }

        /// <summary>确保卡根有可接收射线的 Graphic，否则收不到 hover 事件。</summary>
        private void EnsureRaycastTarget()
        {
            if (GetComponent<UnityEngine.UI.Graphic>() != null) return;
            var img = gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0, 0, 0, 0); // 透明，仅用于接收 hover
            img.raycastTarget = true;
        }

        #endregion
    }
}
