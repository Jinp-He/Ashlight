using UnityEngine;
using UnityEngine.UI;

namespace Scripts.UI
{
    /// <summary>
    /// 目标选择箭头渲染器
    /// 绘制贝塞尔曲线从卡牌到鼠标位置
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class TargetArrowRenderer : Graphic
    {
        [Header("曲线设置")]
        [SerializeField]
        [Tooltip("曲线分段数")]
        private int curveSegments = 25;

        [SerializeField]
        [Tooltip("曲线控制点垂直偏移")]
        private float curveHeight = 100f;

        [SerializeField]
        [Tooltip("线条宽度")]
        private float lineWidth = 6f;

        [Header("颜色设置")]
        [SerializeField]
        [Tooltip("合法目标颜色")]
        private Color validColor = new Color(0.1f, 0.8f, 0.1f, 0.8f);

        [SerializeField]
        [Tooltip("非法目标颜色")]
        private Color invalidColor = new Color(0.8f, 0.1f, 0.1f, 0.8f);

        private Vector3 _startPosition;
        private Vector3 _endPosition;
        private bool _isValid;
        private bool _isVisible;

        protected override void Awake()
        {
            base.Awake();
            _isVisible = false;
            material = null; // 使用默认UI材质
        }

        /// <summary>
        /// 更新箭头线条
        /// </summary>
        /// <param name="startPos">起点位置(屏幕坐标)</param>
        /// <param name="endPos">终点位置(屏幕坐标)</param>
        /// <param name="isValid">是否为合法目标</param>
        public void UpdateLine(Vector3 startPos, Vector3 endPos, bool isValid)
        {
            _startPosition = startPos;
            _endPosition = endPos;
            _isValid = isValid;
            _isVisible = true;

            SetVerticesDirty();
        }

        /// <summary>
        /// 显示箭头
        /// </summary>
        public new void Show()
        {
            _isVisible = true;
            gameObject.SetActive(true);
            SetVerticesDirty();
        }

        /// <summary>
        /// 隐藏箭头
        /// </summary>
        public new void Hide()
        {
            _isVisible = false;
            gameObject.SetActive(false);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (!_isVisible)
            {
                Debug.Log("[TargetArrowRenderer] OnPopulateMesh: 不可见,跳过绘制");
                return;
            }

            // 将世界坐标转换为Canvas本地坐标
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[TargetArrowRenderer] OnPopulateMesh: 找不到父Canvas");
                return;
            }

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (canvasRect == null)
            {
                Debug.LogWarning("[TargetArrowRenderer] OnPopulateMesh: Canvas没有RectTransform");
                return;
            }

            // 转换起点和终点到Canvas本地坐标(两个都是屏幕坐标)
            Vector2 startLocal;
            Vector2 endLocal;

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : (canvas.worldCamera ?? Camera.main);

            bool startSuccess = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, _startPosition, cam, out startLocal);
            bool endSuccess = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, _endPosition, cam, out endLocal);

            if (!startSuccess || !endSuccess)
            {
                Debug.LogWarning($"[TargetArrowRenderer] 坐标转换失败: startSuccess={startSuccess}, endSuccess={endSuccess}");
                return;
            }

            //Debug.Log($"[TargetArrowRenderer] 绘制箭头: 起点屏幕={_startPosition}, 起点本地={startLocal}, 终点屏幕={_endPosition}, 终点本地={endLocal}, 颜色={(_isValid ? "绿色" : "红色")}");

            // 计算贝塞尔曲线控制点
            Vector2 controlPoint = (startLocal + endLocal) / 2f;
            controlPoint.y += curveHeight; // 向上偏移

            // 选择颜色
            Color lineColor = _isValid ? validColor : invalidColor;

            // 生成曲线顶点
            for (int i = 0; i < curveSegments; i++)
            {
                float t1 = (float)i / curveSegments;
                float t2 = (float)(i + 1) / curveSegments;

                Vector2 point1 = CalculateBezierPoint(startLocal, controlPoint, endLocal, t1);
                Vector2 point2 = CalculateBezierPoint(startLocal, controlPoint, endLocal, t2);

                // 计算垂直于线段的方向
                Vector2 direction = (point2 - point1).normalized;
                Vector2 perpendicular = new Vector2(-direction.y, direction.x) * (lineWidth * 0.5f);

                // 创建四边形
                int vertexIndex = vh.currentVertCount;

                vh.AddVert(point1 + perpendicular, lineColor, Vector2.zero);
                vh.AddVert(point1 - perpendicular, lineColor, Vector2.zero);
                vh.AddVert(point2 - perpendicular, lineColor, Vector2.zero);
                vh.AddVert(point2 + perpendicular, lineColor, Vector2.zero);

                vh.AddTriangle(vertexIndex, vertexIndex + 1, vertexIndex + 2);
                vh.AddTriangle(vertexIndex + 2, vertexIndex + 3, vertexIndex);
            }
        }

        /// <summary>
        /// 计算贝塞尔曲线上的点
        /// </summary>
        /// <param name="p0">起点</param>
        /// <param name="p1">控制点</param>
        /// <param name="p2">终点</param>
        /// <param name="t">参数 (0-1)</param>
        /// <returns>曲线上的点</returns>
        private Vector2 CalculateBezierPoint(Vector2 p0, Vector2 p1, Vector2 p2, float t)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;

            Vector2 point = uu * p0; // (1-t)^2 * P0
            point += 2 * u * t * p1; // 2(1-t)t * P1
            point += tt * p2;         // t^2 * P2

            return point;
        }
    }
}
