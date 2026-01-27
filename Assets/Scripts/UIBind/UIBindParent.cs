using UnityEngine;

namespace _Scripts.UI
{
    /// <summary>
    /// UI绑定父级组件，用于标记需要生成绑定代码的UI面板
    /// </summary>
    public class UIBindParent : MonoBehaviour
    {
        [Tooltip("生成的脚本类名")]
        public string ClassName;
        
        [Tooltip("生成的脚本命名空间")]
        public string Namespace = "Scripts.UI";
        
        [Tooltip("生成脚本的路径（相对于Assets）")]
        public string ScriptPath = "Scripts/UI/Generated";

        public string GetClassName()
        {
            return string.IsNullOrEmpty(ClassName) ? gameObject.name : ClassName;
        }
    }
}

