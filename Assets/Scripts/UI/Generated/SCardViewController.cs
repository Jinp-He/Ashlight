using UnityEngine;
using UnityEngine.UI;
using TMPro;
using _Scripts.UI;

namespace Scripts.UI
{
    /// <summary>
    /// 自动生成的UI绑定代码
    /// </summary>
    public partial class SCardViewController : MonoBehaviour
    {
        #region UI Bindings

        public Image Img_Outline;
        public RectTransform ExpanseCardViewController;
        public Button Img_CardBas;
        public Image Img_CardPicture;
        public Image Img_CardFrameUpgraded;
        public Image Img_CardFrameUnUpgraded;
        public Image Img_CostRight;
        public TextMeshProUGUI Txt_RightCost;
        public Image Img_CostLeft;
        public TextMeshProUGUI Txt_LeftCost;
        public TextMeshProUGUI Txt_CardName;

        #endregion

        /// <summary>
        /// 初始化UI绑定
        /// </summary>
        private void InitUIBindings()
        {
            UIBindParent bindParent = GetComponent<UIBindParent>();
            if (bindParent == null) return;

            // 收集UIBind，排除嵌套的UIBindParent（不收集自身）
            System.Collections.Generic.List<UIBind> bindsList = new System.Collections.Generic.List<UIBind>();
            CollectUIBindsRecursive(transform, bindParent, bindsList, true);
            UIBind[] binds = bindsList.ToArray();

            foreach (UIBind bind in binds)
            {
                string bindName = bind.GetBindName();
                switch (bindName)
                {
                    case "Img_Outline":
                        Img_Outline = bind.GetComponent<Image>();
                        break;
                    case "ExpanseCardViewController":
                        ExpanseCardViewController = bind.GetComponent<RectTransform>();
                        break;
                    case "Img_CardBas":
                        Img_CardBas = bind.GetComponent<Button>();
                        break;
                    case "Img_CardPicture":
                        Img_CardPicture = bind.GetComponent<Image>();
                        break;
                    case "Img_CardFrameUpgraded":
                        Img_CardFrameUpgraded = bind.GetComponent<Image>();
                        break;
                    case "Img_CardFrameUnUpgraded":
                        Img_CardFrameUnUpgraded = bind.GetComponent<Image>();
                        break;
                    case "Img_CostRight":
                        Img_CostRight = bind.GetComponent<Image>();
                        break;
                    case "Txt_RightCost":
                        Txt_RightCost = bind.GetComponent<TextMeshProUGUI>();
                        break;
                    case "Img_CostLeft":
                        Img_CostLeft = bind.GetComponent<Image>();
                        break;
                    case "Txt_LeftCost":
                        Txt_LeftCost = bind.GetComponent<TextMeshProUGUI>();
                        break;
                    case "Txt_CardName":
                        Txt_CardName = bind.GetComponent<TextMeshProUGUI>();
                        break;
                }
            }
        }

        /// <summary>
        /// 递归收集UIBind，遇到其他UIBindParent就停止（但会收集该UIBindParent自身的UIBind）
        /// </summary>
        private void CollectUIBindsRecursive(Transform current, UIBindParent rootParent, System.Collections.Generic.List<UIBind> result, bool isRoot = false)
        {
            // 不收集 root 自己的 UIBind（因为 root 就是 UIBindParent 本身）
            if (!isRoot)
            {
                UIBind bind = current.GetComponent<UIBind>();
                if (bind != null)
                {
                    result.Add(bind);
                }
            }

            foreach (Transform child in current)
            {
                UIBindParent childParent = child.GetComponent<UIBindParent>();
                if (childParent != null && childParent != rootParent)
                {
                    // 如果子对象同时有UIBind，添加它（但不继续递归）
                    UIBind childBind = child.GetComponent<UIBind>();
                    if (childBind != null)
                    {
                        result.Add(childBind);
                    }
                    continue;
                }
                CollectUIBindsRecursive(child, rootParent, result, false);
            }
        }
    }
}
