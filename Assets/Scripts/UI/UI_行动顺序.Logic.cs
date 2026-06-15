using UnityEngine;

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
    /// </summary>
    public partial class UI_行动顺序 : MonoBehaviour
    {
        #region Unity 生命周期

        private void Awake()
        {
            InitUIBindings();

            // 初始状态：关闭高亮和攻击图标
            SetHighlight(false);
            SetAttacking(false);
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

        #endregion
    }
}
