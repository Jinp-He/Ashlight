using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Scripts.UI
{
    /// <summary>
    /// 经验卡拖拽：从 UpgradePanel.ExpArea 拖到某个角色身上 → 通知 UpgradePanel 为其 +1 经验。
    /// 由 UpgradePanel 在生成 Prefab_ExpCard 时通过 <see cref="Init"/> 注入归属面板（组件不存在时运行时自动挂载）。
    /// 命中角色且被接受则由 UpgradePanel 销毁本卡；未命中 / 被拒绝则弹回原位。
    /// </summary>
    public class ExpCardDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private UpgradePanel _owner;
        private RectTransform _rt;
        private Canvas _canvas;
        private CanvasGroup _cg;

        private Transform _originalParent;
        private int _originalSiblingIndex;
        private Vector2 _originalAnchoredPos;

        /// <summary>由 UpgradePanel 注入归属面板。</summary>
        public void Init(UpgradePanel owner)
        {
            _owner = owner;
            _rt = GetComponent<RectTransform>();

            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();

            // 根上的 Image 需作为射线目标，才能被 EventSystem 拖拽捕获
            var img = GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.raycastTarget = true;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_rt == null) _rt = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();

            _originalParent = _rt.parent;
            _originalSiblingIndex = _rt.GetSiblingIndex();
            _originalAnchoredPos = _rt.anchoredPosition;

            // 提到画布顶层，拖动时不被遮挡
            if (_canvas != null) _rt.SetParent(_canvas.transform, true);
            _rt.SetAsLastSibling();

            // 拖动期间不阻挡射线，松手 RaycastAll 时才能命中卡片背后的角色
            if (_cg != null) _cg.blocksRaycasts = false;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_rt == null || _canvas == null) return;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    _canvas.transform as RectTransform, e.position, _canvas.worldCamera, out var world))
            {
                _rt.position = world;
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_cg != null) _cg.blocksRaycasts = true;

            var character = RaycastCharacter(e);
            bool consumed = false;
            if (character != null && _owner != null)
            {
                // 被接受时 UpgradePanel 会销毁本卡；此处不再回弹
                consumed = _owner.TryDropExpOnCharacter(this, character);
            }

            if (!consumed && this != null) ReturnToOrigin();
        }

        private Character RaycastCharacter(PointerEventData e)
        {
            if (EventSystem.current == null) return null;

            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(e, results);
            foreach (var r in results)
            {
                var ch = r.gameObject.GetComponentInParent<Character>();
                if (ch != null) return ch;
            }
            return null;
        }

        private void ReturnToOrigin()
        {
            if (_originalParent != null) _rt.SetParent(_originalParent, false);
            _rt.SetSiblingIndex(_originalSiblingIndex);
            _rt.anchoredPosition = _originalAnchoredPos;
        }
    }
}
