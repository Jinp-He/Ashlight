using System;
using System.Collections.Generic;

namespace Ashlight.Common.Events
{
    /// <summary>
    /// 游戏事件总线 - 用于系统间解耦通信
    /// 通用模块：支持订阅/发布模式
    /// </summary>
    public static class GameEvent
    {
        private static readonly Dictionary<Type, Delegate> _events = new Dictionary<Type, Delegate>();

        /// <summary>
        /// 订阅事件
        /// </summary>
        public static void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (_events.TryGetValue(type, out var existing))
            {
                _events[type] = Delegate.Combine(existing, handler);
            }
            else
            {
                _events[type] = handler;
            }
        }

        /// <summary>
        /// 取消订阅事件
        /// </summary>
        public static void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (_events.TryGetValue(type, out var existing))
            {
                var newDelegate = Delegate.Remove(existing, handler);
                if (newDelegate == null)
                {
                    _events.Remove(type);
                }
                else
                {
                    _events[type] = newDelegate;
                }
            }
        }

        /// <summary>
        /// 发布事件
        /// </summary>
        public static void Publish<T>(T eventData)
        {
            var type = typeof(T);
            if (_events.TryGetValue(type, out var handler))
            {
                (handler as Action<T>)?.Invoke(eventData);
            }
        }

        /// <summary>
        /// 清除所有事件订阅
        /// </summary>
        public static void Clear()
        {
            _events.Clear();
        }
    }
}

