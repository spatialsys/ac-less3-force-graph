using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Less3.Graph.Editor
{
    // N = Node type, C = Connection type, G = Group type
    public class ForceNodeDragManipulator<N, C, G> : PointerManipulator where N : class where C : class where G : class
    {
        private bool _enabled;// track if an event started inside the root
        private LCanvasGroup<G, N> _hoveredGroup;

        private Vector2 _targetStartPosition { get; set; }
        private Vector3 _pointerStartPosition { get; set; }
        private LCanvasNode<N> _node;
        private Dictionary<LCanvasNode<N>, Vector2> _groupStartPositions;

        private Action<LCanvasNode<N>, bool> _leftClickAction;
        private Action<LCanvasNode<N>> _rightClickAction;

        private Action<LCanvasNode<N>> _enterAction;
        private Action<LCanvasNode<N>> _exitAction;

        private LCanvas<N, C, G> _canvas;

        public ForceNodeDragManipulator(
            LCanvasNode<N> node,
            LCanvas<N, C, G> c,
            Action<LCanvasNode<N>, bool> leftClickAction,
            Action<LCanvasNode<N>> rightClickAction,
            Action<LCanvasNode<N>> enterAction,
            Action<LCanvasNode<N>> exitAction)
        {
            _node = node;
            _canvas = c;
            _leftClickAction = leftClickAction;
            _rightClickAction = rightClickAction;
            _enterAction = enterAction;
            _exitAction = exitAction;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(PointerDownHandler);
            target.RegisterCallback<PointerMoveEvent>(PointerMoveHandler);
            target.RegisterCallback<PointerUpEvent>(PointerUpHandler);
            target.RegisterCallback<PointerEnterEvent>(PointerEnterHandler);
            target.RegisterCallback<PointerOutEvent>(PointerOutHandler);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(PointerDownHandler);
            target.UnregisterCallback<PointerMoveEvent>(PointerMoveHandler);
            target.UnregisterCallback<PointerUpEvent>(PointerUpHandler);
            target.UnregisterCallback<PointerEnterEvent>(PointerEnterHandler);
            target.UnregisterCallback<PointerOutEvent>(PointerOutHandler);
        }

        private void PointerDownHandler(PointerDownEvent evt)
        {
            _targetStartPosition = _node.element.transform.position;
            _pointerStartPosition = evt.position;
            if (evt.button == (int)MouseButton.RightMouse)
            {
                _rightClickAction?.Invoke(_node);
                return;
            }
            if (evt.button == (int)MouseButton.LeftMouse)
            {
                _enabled = true;
                PointerCaptureHelper.CapturePointer(target, evt.pointerId);
                _node.element.Q("Border").AddToClassList("Pressed");

                _leftClickAction?.Invoke(_node, evt.shiftKey);

                _groupStartPositions = null;
                if (_canvas.selectedNodes.Count > 1 && _canvas.selectedNodes.Contains(_node))
                {
                    _groupStartPositions = new Dictionary<LCanvasNode<N>, Vector2>();
                    foreach (var n in _canvas.selectedNodes)
                        _groupStartPositions[n] = n.position;
                }
                return;
            }
        }

        private void PointerMoveHandler(PointerMoveEvent evt)
        {
            if (_enabled && target.HasPointerCapture(evt.pointerId))
            {
                Vector3 pointerDelta = evt.position - _pointerStartPosition;
                pointerDelta = pointerDelta * (1f / EditorPrefs.GetFloat(LCanvasPrefs.ZOOM_KEY, LCanvasPrefs.DEFAULT_ZOOM));
                Vector2 newPos = new Vector2(_targetStartPosition.x + pointerDelta.x, _targetStartPosition.y + pointerDelta.y);

                if (EditorPrefs.GetBool(LCanvasPrefs.SNAP_SETTINGS_KEY, true))
                {
                    newPos = _canvas.TryGetNodeSnapPosition(newPos, _node);
                }
                _node.SetPosition(newPos);

                if (_groupStartPositions != null)
                {
                    Vector2 appliedDelta = newPos - _targetStartPosition;
                    foreach (var kvp in _groupStartPositions)
                    {
                        if (kvp.Key == _node) continue;
                        kvp.Key.SetPosition(kvp.Value + appliedDelta);
                    }
                }

                // look for groups hover
                if (_canvas.TryGetGroupNodeIsIn(_node, out var groupNodeIsIn))
                {
                    if (_hoveredGroup != null)
                    {
                        _hoveredGroup.SetHoveredWithNode(false);
                        _hoveredGroup = null;
                    }
                }
                else if (_canvas.TryGetGroupAtPosition(newPos, out var group))
                {
                    if (_hoveredGroup != group && _hoveredGroup != null)
                    {
                        _hoveredGroup.SetHoveredWithNode(false);
                    }
                    _hoveredGroup = group;
                    _hoveredGroup.SetHoveredWithNode(true);
                }
                else
                {
                    if (_hoveredGroup != null)
                    {
                        _hoveredGroup.SetHoveredWithNode(false);
                        _hoveredGroup = null;
                    }
                }
            }
        }

        private void PointerUpHandler(PointerUpEvent evt)
        {
            if (_enabled && target.HasPointerCapture(evt.pointerId))
            {
                _enabled = false;
                target.ReleasePointer(evt.pointerId);
                _node.element.Q("Border").RemoveFromClassList("Pressed");

                if (_hoveredGroup != null)
                {
                    _hoveredGroup.SetHoveredWithNode(false);
                    _canvas.AddNodeToGroupInternal(_node, _hoveredGroup);
                    _hoveredGroup = null;
                }
            }
        }

        private void PointerEnterHandler(PointerEnterEvent evt)
        {
            _enterAction.Invoke(_node);
        }

        private void PointerOutHandler(PointerOutEvent evt)
        {
            _exitAction.Invoke(_node);
        }
    }
}
