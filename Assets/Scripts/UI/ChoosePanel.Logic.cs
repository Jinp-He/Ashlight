using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using cfg.Character;

namespace Scripts.UI
{
    /// <summary>
    /// 升级三选一面板（ChoosePanel）逻辑分部。
    /// UI 绑定由生成的 <see cref="ChoosePanel"/> 分部提供：Btn_Upgrade1 / Btn_Upgrade2 / Btn_Upgrade3。
    /// 每个按钮下约定有名为 "Title" 与 "Description" 的 TMP 文本子物体，分别显示升级名称与文本。
    ///
    /// 由 UI_BattleScene 在胜利、玩家点选角色后调用 <see cref="Show"/> 打开，
    /// 玩家点击某个按钮即选定该升级，回调交由调用方处理（写入 AcquiredUpgrades 并继续结算）。
    /// </summary>
    public partial class ChoosePanel : MonoBehaviour
    {
        private bool _bound;

        private Button[] Buttons => new[] { Btn_Upgrade1, Btn_Upgrade2, Btn_Upgrade3 };

        private void EnsureBound()
        {
            if (_bound && Btn_Upgrade1 != null) return;
            InitUIBindings();
            _bound = true;
        }

        /// <summary>
        /// 显示三选一面板。
        /// </summary>
        /// <param name="options">候选升级（最多取前 3 个；不足 3 个时多余按钮隐藏）</param>
        /// <param name="onChosen">玩家选定某升级后的回调</param>
        public void Show(IList<UpgradeOptions> options, System.Action<UpgradeOptions> onChosen)
        {
            EnsureBound();

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            var buttons = Buttons;
            for (int i = 0; i < buttons.Length; i++)
            {
                var btn = buttons[i];
                if (btn == null) continue;

                btn.onClick.RemoveAllListeners();

                if (options != null && i < options.Count && options[i] != null)
                {
                    var opt = options[i];
                    btn.gameObject.SetActive(true);
                    SetButtonTexts(btn, opt.Name, opt.Description);
                    btn.onClick.AddListener(() =>
                    {
                        Hide();
                        onChosen?.Invoke(opt);
                    });
                }
                else
                {
                    btn.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>关闭面板并清理监听。</summary>
        public void Hide()
        {
            var buttons = Buttons;
            foreach (var btn in buttons)
            {
                if (btn != null) btn.onClick.RemoveAllListeners();
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 把升级名称/文本写入按钮下名为 "Title" / "Description" 的 TMP 文本。
        /// 兼容任意嵌套层级；找不到对应子物体则忽略。
        /// </summary>
        private void SetButtonTexts(Button btn, string title, string description)
        {
            var texts = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                if (t.gameObject.name == "Title") t.text = title;
                else if (t.gameObject.name == "Description") t.text = description;
            }
        }
    }
}
