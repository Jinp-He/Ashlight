using UnityEngine;
using UnityEngine.UI;
using TMPro;
using _Scripts.UI;

namespace _Scripts.UI
{
    /// <summary>
    /// 自动生成的UI绑定代码
    /// </summary>
    public partial class ChooseCardPanel : MonoBehaviour
    {
        #region UI Bindings

        public RectTransform CardLibrary;
        public TextMeshProUGUI Txt_Statistics;
        public RectTransform CardDeckContainer;
        public Image Img_TrashCanBas;
        public Image Img_Frame;
        public Image Img_PointerIcon;
        public Button Btn_Start;

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
                    case "CardLibrary":
                        CardLibrary = bind.GetComponent<RectTransform>();
                        break;
                    case "Txt_Statistics":
                        Txt_Statistics = bind.GetComponent<TextMeshProUGUI>();
                        break;
                    case "CardDeckContainer":
                        CardDeckContainer = bind.GetComponent<RectTransform>();
                        break;
                    case "Img_TrashCanBas":
                        Img_TrashCanBas = bind.GetComponent<Image>();
                        break;
                    case "Img_Frame":
                        Img_Frame = bind.GetComponent<Image>();
                        break;
                    case "Img_PointerIcon":
                        Img_PointerIcon = bind.GetComponent<Image>();
                        break;
                    case "Btn_Start":
                        Btn_Start = bind.GetComponent<Button>();
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
