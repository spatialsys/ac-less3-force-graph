using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Less3.Graph.Editor
{
    /// <summary>
    /// Manipulator for resizing the width of the inspector overlay panel in the force graph inspector.
    /// Attach to a thin handle element on the right edge of the overlay; drag horizontally to resize.
    /// The resulting width is persisted in EditorPrefs so it is remembered across sessions.
    /// </summary>
    public class ForceGraphInspectorWidthResizeManipulator : PointerManipulator
    {
        private bool _enabled;// track if a drag started on the handle
        private Vector3 _pointerStartPosition;
        private float _startWidth;
        private readonly VisualElement _overlay;

        public ForceGraphInspectorWidthResizeManipulator(VisualElement overlay)
        {
            _overlay = overlay;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(PointerDownHandler);
            target.RegisterCallback<PointerMoveEvent>(PointerMoveHandler);
            target.RegisterCallback<PointerUpEvent>(PointerUpHandler);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(PointerDownHandler);
            target.UnregisterCallback<PointerMoveEvent>(PointerMoveHandler);
            target.UnregisterCallback<PointerUpEvent>(PointerUpHandler);
        }

        private void PointerDownHandler(PointerDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            _pointerStartPosition = evt.position;
            _startWidth = _overlay.layout.width;
            _enabled = true;
            PointerCaptureHelper.CapturePointer(target, evt.pointerId);
            // Prevent the overlay drag manipulator (on the parent) from also reacting.
            evt.StopPropagation();
        }

        private void PointerMoveHandler(PointerMoveEvent evt)
        {
            if (!_enabled || !target.HasPointerCapture(evt.pointerId))
                return;

            float diff = evt.position.x - _pointerStartPosition.x;
            float newWidth = Mathf.Clamp(
                _startWidth + diff,
                L3GraphInspector.MIN_INSPECTOR_WIDTH,
                L3GraphInspector.MAX_INSPECTOR_WIDTH);

            EditorPrefs.SetFloat(L3GraphInspector.INSPECTOR_WIDTH_SETTING_KEY, newWidth);
            _overlay.style.width = newWidth;
            evt.StopPropagation();
        }

        private void PointerUpHandler(PointerUpEvent evt)
        {
            if (!_enabled || !target.HasPointerCapture(evt.pointerId))
                return;

            _enabled = false;
            target.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }
    }
}
