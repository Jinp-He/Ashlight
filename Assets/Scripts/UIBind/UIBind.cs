using UnityEngine;

namespace _Scripts.UI
{
    /// <summary>
    /// 标记需要绑定的UI组件
    /// 可以嵌套使用，形成层级结构来管理复杂的UI
    /// </summary>
    public class UIBind : MonoBehaviour
    {
        [Tooltip("绑定的名称，留空则自动使用GameObject名称")]
        public string BindName;
        
        [Tooltip("组件类型，留空则自动检测")]
        public string ComponentType;
        
        [Tooltip("是否作为容器组件，可以包含其他UIBind")]
        public bool IsContainer = false;

        public string GetBindName()
        {
            if (string.IsNullOrEmpty(BindName))
            {
                BindName = gameObject.name;
            }
            return BindName;
        }
        
        public string GetComponentType()
        {
            return ComponentType;
        }
        
        /// <summary>
        /// 获取直接子级的UIBind组件（不包括更深层级的）
        /// </summary>
        public UIBind[] GetChildBinds()
        {
            if (!IsContainer) return new UIBind[0];
            
            System.Collections.Generic.List<UIBind> childBinds = new System.Collections.Generic.List<UIBind>();
            foreach (Transform child in transform)
            {
                UIBind bind = child.GetComponent<UIBind>();
                if (bind != null)
                {
                    childBinds.Add(bind);
                }
            }
            return childBinds.ToArray();
        }
    }
}

