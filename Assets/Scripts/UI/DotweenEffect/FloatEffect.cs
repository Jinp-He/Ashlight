using UnityEngine;
using DG.Tweening;

namespace Ashlight.UI.Effects
{
    /// <summary>
    /// 使用 DOTween 实现的浮动效果
    /// 可以让 UI 元素产生上下、左右、旋转、缩放等浮动动画
    /// </summary>
    public class FloatEffect : MonoBehaviour
    {
        #region 浮动类型

        public enum FloatType
        {
            Vertical,       // 垂直浮动
            Horizontal,     // 水平浮动
            Rotate,         // 旋转
            Scale,          // 缩放
            Combined        // 组合效果（垂直 + 缩放）
        }

        #endregion

        #region Inspector 字段

        [Header("浮动类型")]
        [Tooltip("选择浮动效果类型")]
        public FloatType floatType = FloatType.Vertical;

        [Header("浮动参数")]
        [Tooltip("浮动距离（像素）或旋转角度")]
        public float floatAmount = 20f;

        [Tooltip("浮动周期（秒）")]
        public float duration = 2f;

        [Tooltip("是否在 Start 时自动播放")]
        public bool playOnStart = true;

        [Tooltip("循环次数（-1 表示无限循环）")]
        public int loops = -1;

        [Header("缓动设置")]
        [Tooltip("缓动曲线类型")]
        public Ease easeType = Ease.InOutSine;

        [Header("延迟设置")]
        [Tooltip("开始延迟（秒）")]
        public float startDelay = 0f;

        [Tooltip("随机延迟范围（秒，为0则不随机）")]
        public float randomDelayRange = 0f;

        #endregion

        #region 私有字段

        private RectTransform _rectTransform;
        private Vector3 _originalPosition;
        private Vector3 _originalScale;
        private Vector3 _originalRotation;
        private Tween _currentTween;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform == null)
            {
                Debug.LogError("[FloatEffect] 需要 RectTransform 组件");
                enabled = false;
                return;
            }

            // 保存初始状态
            _originalPosition = _rectTransform.anchoredPosition;
            _originalScale = _rectTransform.localScale;
            _originalRotation = _rectTransform.localEulerAngles;
        }

        private void Start()
        {
            if (playOnStart)
            {
                Play();
            }
        }

        private void OnDestroy()
        {
            Stop();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 播放浮动效果
        /// </summary>
        public void Play()
        {
            // 停止之前的动画
            Stop();

            // 计算实际延迟
            float actualDelay = startDelay;
            if (randomDelayRange > 0)
            {
                actualDelay += Random.Range(0f, randomDelayRange);
            }

            // 根据类型创建不同的浮动效果
            switch (floatType)
            {
                case FloatType.Vertical:
                    PlayVerticalFloat(actualDelay);
                    break;

                case FloatType.Horizontal:
                    PlayHorizontalFloat(actualDelay);
                    break;

                case FloatType.Rotate:
                    PlayRotateFloat(actualDelay);
                    break;

                case FloatType.Scale:
                    PlayScaleFloat(actualDelay);
                    break;

                case FloatType.Combined:
                    PlayCombinedFloat(actualDelay);
                    break;
            }
        }

        /// <summary>
        /// 停止浮动效果
        /// </summary>
        public void Stop()
        {
            if (_currentTween != null && _currentTween.IsActive())
            {
                _currentTween.Kill();
                _currentTween = null;
            }

            // 恢复初始状态
            ResetToOriginal();
        }

        /// <summary>
        /// 暂停浮动效果
        /// </summary>
        public void Pause()
        {
            if (_currentTween != null && _currentTween.IsActive())
            {
                _currentTween.Pause();
            }
        }

        /// <summary>
        /// 恢复浮动效果
        /// </summary>
        public void Resume()
        {
            if (_currentTween != null && _currentTween.IsActive())
            {
                _currentTween.Play();
            }
        }

        /// <summary>
        /// 重置到初始状态
        /// </summary>
        public void ResetToOriginal()
        {
            if (_rectTransform != null)
            {
                _rectTransform.anchoredPosition = _originalPosition;
                _rectTransform.localScale = _originalScale;
                _rectTransform.localEulerAngles = _originalRotation;
            }
        }

        #endregion

        #region 私有方法 - 浮动效果实现

        /// <summary>
        /// 垂直浮动效果
        /// </summary>
        private void PlayVerticalFloat(float delay)
        {
            Vector2 targetPos = _originalPosition + new Vector3(0, floatAmount, 0);

            _currentTween = _rectTransform
                .DOAnchorPosY(targetPos.y, duration / 2f)
                .SetEase(easeType)
                .SetLoops(loops, LoopType.Yoyo)
                .SetDelay(delay)
                .OnKill(() => _currentTween = null);
        }

        /// <summary>
        /// 水平浮动效果
        /// </summary>
        private void PlayHorizontalFloat(float delay)
        {
            Vector2 targetPos = _originalPosition + new Vector3(floatAmount, 0, 0);

            _currentTween = _rectTransform
                .DOAnchorPosX(targetPos.x, duration / 2f)
                .SetEase(easeType)
                .SetLoops(loops, LoopType.Yoyo)
                .SetDelay(delay)
                .OnKill(() => _currentTween = null);
        }

        /// <summary>
        /// 旋转浮动效果
        /// </summary>
        private void PlayRotateFloat(float delay)
        {
            Vector3 targetRotation = _originalRotation + new Vector3(0, 0, floatAmount);

            _currentTween = _rectTransform
                .DOLocalRotate(targetRotation, duration / 2f, RotateMode.Fast)
                .SetEase(easeType)
                .SetLoops(loops, LoopType.Yoyo)
                .SetDelay(delay)
                .OnKill(() => _currentTween = null);
        }

        /// <summary>
        /// 缩放浮动效果
        /// </summary>
        private void PlayScaleFloat(float delay)
        {
            float scaleMultiplier = 1f + (floatAmount / 100f); // floatAmount 当作百分比
            Vector3 targetScale = _originalScale * scaleMultiplier;

            _currentTween = _rectTransform
                .DOScale(targetScale, duration / 2f)
                .SetEase(easeType)
                .SetLoops(loops, LoopType.Yoyo)
                .SetDelay(delay)
                .OnKill(() => _currentTween = null);
        }

        /// <summary>
        /// 组合浮动效果（垂直 + 缩放）
        /// </summary>
        private void PlayCombinedFloat(float delay)
        {
            // 创建 Sequence 来组合多个动画
            Sequence sequence = DOTween.Sequence();

            Vector2 targetPos = _originalPosition + new Vector3(0, floatAmount, 0);
            float scaleMultiplier = 1.1f;
            Vector3 targetScale = _originalScale * scaleMultiplier;

            // 同时执行垂直移动和缩放
            sequence.Append(_rectTransform.DOAnchorPosY(targetPos.y, duration / 2f).SetEase(easeType));
            sequence.Join(_rectTransform.DOScale(targetScale, duration / 2f).SetEase(easeType));

            sequence.SetLoops(loops, LoopType.Yoyo);
            sequence.SetDelay(delay);
            sequence.OnKill(() => _currentTween = null);

            _currentTween = sequence;
        }

        #endregion

        #region Editor 辅助

#if UNITY_EDITOR
        /// <summary>
        /// 在 Editor 中绘制浮动范围的辅助线
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (_rectTransform == null)
                _rectTransform = GetComponent<RectTransform>();

            if (_rectTransform == null)
                return;

            Gizmos.color = Color.yellow;

            Vector3 worldPos = _rectTransform.position;

            switch (floatType)
            {
                case FloatType.Vertical:
                    Gizmos.DrawLine(worldPos, worldPos + Vector3.up * floatAmount);
                    break;

                case FloatType.Horizontal:
                    Gizmos.DrawLine(worldPos, worldPos + Vector3.right * floatAmount);
                    break;

                case FloatType.Combined:
                    Gizmos.DrawLine(worldPos, worldPos + Vector3.up * floatAmount);
                    Gizmos.DrawWireSphere(worldPos + Vector3.up * floatAmount, 10f);
                    break;
            }
        }
#endif

        #endregion
    }
}
