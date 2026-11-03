using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts.UI
{
    /// <summary>
    /// BaixiangPanel 逻辑分部 —— 「百相（天赋）」三选一面板。
    /// UI 绑定（Baixiang1/2/3 槽位 + HeadImage）由生成的 <see cref="BaixiangPanel"/> 分部提供。
    /// 由 UpgradePanel 在角色升级时调用：把 #UpgradeOptions 候选转成 <see cref="Option"/> 列表传入，
    /// 点击某槽位即选定。（选卡是另一套：UpgradePanel 直接在 CardParent 下实例化卡牌预制体，不走本面板。）
    /// </summary>
    public partial class BaixiangPanel : MonoBehaviour
    {
        /// <summary>一个候选项的展示数据（卡牌 / 天赋通用）。</summary>
        public struct Option
        {
            public string Name;
            public string EffectText;
            public string SupplementaryText;
            public Sprite Icon;
        }

        private bool _bound;

        private RectTransform[] Slots => new[] { Baixiang1, Baixiang2, Baixiang3 };

        private void EnsureBound()
        {
            if (_bound && Baixiang1 != null) return;
            InitUIBindings();
            _bound = true;
        }

        /// <summary>
        /// 显示三选一面板。
        /// </summary>
        /// <param name="options">候选（最多取前 3 个；不足 3 个时多余槽位隐藏）</param>
        /// <param name="onPick">玩家点击某槽位后的回调，参数为该槽位在 options 中的下标</param>
        /// <param name="head">可选：顶部头像；为 null 时保留原图</param>
        public void Show(IList<Option> options, Action<int> onPick, Sprite head = null)
        {
            EnsureBound();

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (head != null && HeadImage != null) HeadImage.sprite = head;

            var slots = Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;

                if (options != null && i < options.Count)
                {
                    slot.gameObject.SetActive(true);
                    PopulateSlot(slot, options[i]);

                    int captured = i;
                    var btn = EnsureButton(slot);
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        Hide();
                        onPick?.Invoke(captured);
                    });
                }
                else
                {
                    slot.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>关闭面板并清理监听。</summary>
        public void Hide()
        {
            var slots = Slots;
            foreach (var s in slots)
            {
                if (s == null) continue;
                var btn = s.GetComponent<Button>();
                if (btn != null) btn.onClick.RemoveAllListeners();
            }
            gameObject.SetActive(false);
        }

        /// <summary>把候选数据写入槽位视图（优先用 Baixiang 视图组件，退化为按名字查找文本）。</summary>
        private void PopulateSlot(RectTransform slot, Option opt)
        {
            var view = slot.GetComponent<Baixiang>();
            if (view != null)
            {
                view.SetData(opt.Name, opt.EffectText, opt.SupplementaryText, opt.Icon);
                return;
            }

            // 退化路径：槽位上没有 Baixiang 组件时，按子物体名字写文本
            var texts = slot.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                if (t.gameObject.name == "Txt_BaixiangName") t.text = opt.Name ?? "";
                else if (t.gameObject.name == "Txt_BaixiangEffect") t.text = opt.EffectText ?? "";
                else if (t.gameObject.name == "Txt_SupplementaryText") t.text = opt.SupplementaryText ?? "";
            }
        }

        /// <summary>确保槽位根物体可点击（缺 Graphic 时补一张透明 Image，缺 Button 时补 Button）。</summary>
        private Button EnsureButton(RectTransform slot)
        {
            var go = slot.gameObject;

            var graphic = go.GetComponent<Graphic>();
            if (graphic == null)
            {
                var img = go.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0); // 透明，仅用于接收点击
                img.raycastTarget = true;
            }

            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            return btn;
        }
    }
}
