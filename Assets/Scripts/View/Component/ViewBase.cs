using UnityEngine;

namespace Ashlight.View.Component
{
    /// <summary>
    /// 视图组件基类 - 所有视觉组件的基类
    /// 表现层：只负责展示状态，不处理业务逻辑
    /// </summary>
    public abstract class ViewBase : MonoBehaviour
    {
        /// <summary>
        /// 初始化视图
        /// </summary>
        public virtual void Initialize()
        {
        }

        /// <summary>
        /// 刷新视图显示
        /// </summary>
        public abstract void Refresh();

        /// <summary>
        /// 清理视图
        /// </summary>
        public virtual void Cleanup()
        {
        }
    }
}

