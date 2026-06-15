using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Scripts.UI
{
    /// <summary>
    /// 回合顺序分隔条（挂在 Prefab_LevelIndicator 上）。
    ///
    /// 用途：在 TurnOrderView 里插在不同 Slot 格之间，指示“回合与回合之间”的分界。
    ///   · 本体是一根竖线（Image）。
    ///   · 若 prefab 下挂了可选的文字子物体（TMP_Text），可显示该分界对应的格数
    ///     （即“后面这些单位是在 N 个回合之后行动”）。
    ///
    /// 设计原则：所有引用都做空判断 + 自动获取，因此即便 prefab 上没有手动赋值，
    /// 运行时 AddComponent 也能正常工作。
    /// </summary>
    public class LevelIndicator : MonoBehaviour
    {
        #region 序列化字段

        [SerializeField]
        [Tooltip("竖线本体 Image，留空则自动取自身 Image")]
        private Image lineImage;

        [SerializeField]
        [Tooltip("可选：显示格数（N 个回合后）的文字，留空则自动在子物体里查找 TMP_Text")]
        private TMP_Text levelLabel;

        #endregion

        private bool _initialized;

        private void Awake() => EnsureRefs();

        private void EnsureRefs()
        {
            if (_initialized) return;
            if (lineImage == null) lineImage = GetComponent<Image>();
            if (levelLabel == null) levelLabel = GetComponentInChildren<TMP_Text>(true);
            _initialized = true;
        }

        #region 公共 API

        /// <summary>
        /// 设置该分隔条代表的格数（后面的单位在 N 个回合之后行动）。
        /// 无文字子物体时该调用无副作用。
        /// </summary>
        public void SetLevel(int slots)
        {
            EnsureRefs();
            if (levelLabel != null)
                levelLabel.text = slots.ToString();
        }

        /// <summary>设置竖线颜色（可用于区分不同梯队）。</summary>
        public void SetColor(Color color)
        {
            EnsureRefs();
            if (lineImage != null)
                lineImage.color = color;
        }

        /// <summary>显示/隐藏整条分隔条。</summary>
        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

        #endregion
    }
}
