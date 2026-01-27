using UnityEngine;
using UnityEngine.UI;
using TMPro;
using _Scripts.UI;
using Spine.Unity;
namespace Scripts.UI
{
    /// <summary>
    /// 自动生成的UI绑定代码
    /// </summary>
    public partial class Enemy : MonoBehaviour
    {
        #region UI Bindings

        public SkeletonGraphic Skeleton_Unit;
        public RectTransform BuffBase;
        public CanvasGroup Indicator;
        public Image Fill_Hp;
        public TextMeshProUGUI Txt_Hp;
        public RectTransform Shield;
        public TextMeshProUGUI Txt_Shield;

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
                    case "Skeleton_Unit":
                        Skeleton_Unit = bind.GetComponent<SkeletonGraphic>();
                        break;
                    case "BuffBase":
                        BuffBase = bind.GetComponent<RectTransform>();
                        break;
                    case "Indicator":
                        Indicator = bind.GetComponent<CanvasGroup>();
                        break;
                    case "Fill_Hp":
                        Fill_Hp = bind.GetComponent<Image>();
                        break;
                    case "Txt_Hp":
                        Txt_Hp = bind.GetComponent<TextMeshProUGUI>();
                        break;
                    case "Shield":
                        Shield = bind.GetComponent<RectTransform>();
                        break;
                    case "Txt_Shield":
                        Txt_Shield = bind.GetComponent<TextMeshProUGUI>();
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
