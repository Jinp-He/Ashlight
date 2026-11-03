using UnityEngine;

namespace Scripts.UI
{
    /// <summary>
    /// Baixiang（三选一槽位视图）逻辑分部。
    /// UI 绑定（img_BaixiangIcon / Txt_BaixiangName / Txt_BaixiangEffect / Txt_SupplementaryText / img_BaixiangBackground）
    /// 由生成的 <see cref="Baixiang"/> 分部提供。被 <see cref="BaixiangPanel"/> 当作通用「候选项」展示单元复用（卡牌 / 天赋皆可）。
    /// </summary>
    public partial class Baixiang : MonoBehaviour
    {
        private bool _bound;

        private void EnsureBound()
        {
            if (_bound && Txt_BaixiangName != null) return;
            InitUIBindings();
            _bound = true;
        }

        /// <summary>填充一个候选项的展示数据。空文本字段会被清空，icon 为 null 时保留原图。</summary>
        public void SetData(string name, string effect, string supplementary, Sprite icon)
        {
            EnsureBound();

            if (Txt_BaixiangName != null) Txt_BaixiangName.text = name ?? "";
            if (Txt_BaixiangEffect != null) Txt_BaixiangEffect.text = effect ?? "";
            if (Txt_SupplementaryText != null) Txt_SupplementaryText.text = supplementary ?? "";
            if (icon != null && img_BaixiangIcon != null) img_BaixiangIcon.sprite = icon;
        }
    }
}
