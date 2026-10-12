using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Less3.Graph.Editor
{
    /// <summary>
    /// Detects when ForceGraph assets are reimported or deleted, and notifies open inspector windows.
    /// </summary>
    public class L3GraphAssetPostProcessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            L3GraphInspector[] windows = Resources.FindObjectsOfTypeAll<L3GraphInspector>();
            foreach (L3GraphInspector window in windows)
            {
                string targetPath = window.GetTargetAssetPath();
                if (string.IsNullOrEmpty(targetPath))
                    continue;

                // Check if asset was deleted - close the window
                if (System.Array.Exists(deletedAssets, path => path == targetPath))
                {
                    window.Close();
                    continue;
                }

                // Check if asset was reimported - reload the window
                if (System.Array.Exists(importedAssets, path => path == targetPath))
                {
                    window.ReloadFromAssetRefresh();
                }
            }
        }
    }

    /// <summary>
    /// The inspector, specifically for the parameters of the graph object.
    /// We need this empty class otherwise we create an infinite loop of graph inspectors
    /// </summary>
    public class ForceGraphParametersEditorBase : UnityEditor.Editor
    {
        private readonly string[] EXCLUDED_PROPERTIES = new string[] { "m_Script", "nodes", "connections" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, EXCLUDED_PROPERTIES);
            serializedObject.ApplyModifiedProperties();
        }
    }

    public class L3GraphInspector : EditorWindow
    {
        public static readonly float DEFAULT_GRAPH_HEIGHT = 400f;
        public static readonly string HEIGHT_SETTING_KEY = "ForceGraphInspectorHeight";
        public static readonly float MIN_GRAPH_HEIGHT = 100f;

        public static readonly string INSPECTOR_WIDTH_SETTING_KEY = "ForceGraphInspectorWidth";
        public static readonly float DEFAULT_INSPECTOR_WIDTH = 300f;
        public static readonly float MIN_INSPECTOR_WIDTH = 256f;
        public static readonly float MAX_INSPECTOR_WIDTH = 900f;

        public static readonly string FAST_FORWARD_SETTINGS_KEY = "ForceGraphFastForward";
        public static readonly string FIT_TO_SCREEN_SETTINGS_KEY = "ForceGraphFitToScreen";
        public static readonly string LAYERED_MODE_SETTINGS_KEY = "ForceGraphInspectorLayered";
        public static readonly string PIN_OVERLAY_SETTINGS_KEY = "ForceGraphInspectorPinOverlay";

        public static readonly string OVERLAY_X_SETTINGS_KEY = "ForceGraphInspectorOverlayX";
        public static readonly string OVERLAY_Y_SETTINGS_KEY = "ForceGraphInspectorOverlayY";

        public static System.Action<L3GraphNode> OnNodeDoubleClicked;

        [SerializeField]
        private L3Graph target;
        [SerializeField]
        private bool wasInit;

        private UnityEditor.Editor graphParametersInspector;
        private UnityEditor.Editor multiSelectionInspector;

        // assigned in the inspector
        public VisualTreeAsset inspectorLayeredUXML;
        // assigned in the inspector
        public Texture2D windowIcon;

        public VisualElement inspector;
        public Label inspectorLabel;
        public VisualElement inspectorOverlay;

        public VisualElement graphInspectorRoot;
        public VisualElement graphHeightSetter;
        public VisualElement selectionInspectorRoot;

        private LCanvas<L3GraphNode, L3GraphConnection, L3GraphGroup> canvas;
        private ToolbarBreadcrumbs breadcrumbs;

        [SerializeField]
        private List<L3Graph> graphStack = new List<L3Graph>();//

        [OnOpenAsset(1)]
        public static bool DoubleClickAsset(int instanceID, int line)
        {
            Object obj = EditorUtility.InstanceIDToObject(instanceID);
            if (obj is L3Graph forceGraph)
            {
                OpenGraphNew(forceGraph);
                return true; // we handled the open
            }
            return false; // we did not handle the open
        }

        // Try to go up (down?) one level of the stack. like a back button.
        public static void TryReverseStack()
        {
            var window = GetWindow<L3GraphInspector>();
            if (window.graphStack.Count <= 1)
            {
                return;
            }

            window.graphStack.RemoveAt(window.graphStack.Count - 1);
            OpenGraphStack(window.graphStack);
        }

        // Opens the graph as a new stack
        public static void OpenGraphNew(L3Graph graph)
        {
            var window = GetWindow<L3GraphInspector>();
            window.InitGUI(new List<L3Graph> { graph });
        }

        // Opens the graph, adding it to the top of the stack
        public static void OpenGraphOnStack(L3Graph graph)
        {
            var window = GetWindow<L3GraphInspector>();
            if (window.graphStack.Count > 0 && window.graphStack[window.graphStack.Count - 1] == graph)
            {
                // already on top of stack
                return;
            }
            window.graphStack.Add(graph);
            window.InitGUI(window.graphStack);
        }

        public static void OpenGraphStack(List<L3Graph> graphStack)
        {
            var window = GetWindow<L3GraphInspector>();
            window.InitGUI(graphStack);
        }

        public void InitGUI(List<L3Graph> newGraphStack)
        {
            graphStack = new List<L3Graph>(newGraphStack);
            L3Graph graph = graphStack[graphStack.Count - 1];

            if (target != null && target != graph)
            {
                target.OnForceGraphRepaint -= ForceRepaint;
            }
            target = graph;
            target.OnForceGraphRepaint += ForceRepaint;

            titleContent = new GUIContent(target.name, windowIcon);
            if (inspector != null)
            {
                inspector.RemoveFromHierarchy();
            }

            inspector = new VisualElement();
            inspector.name = "$ForceGraphInspector";
            inspectorLayeredUXML.CloneTree(inspector);

            canvas = new LCanvas<L3GraphNode, L3GraphConnection, L3GraphGroup>(target.GetType());
            inspector.Q("GraphOrigin").Add(canvas);

            canvas.OnSelectionChanged += OnSelectionChanged;

            // ? The graph UI creates UI elements, but has no concept of how to create the backing data.
            //   Here we are taking "create new object requests" and filling in correct data (creating new SO's)
            canvas.OnGroupCreatedInternally += (group, type) =>
            {
                var asset = (target as L3Graph).CreateGroup(type);
                group.data = asset;
            };

            canvas.OnNodeAddedToGroupInternally += (node, group) =>
            {
                (target as L3Graph).AddNodeToGroup(node, group);
            };

            canvas.OnNodeRemovedFromGroupInternally += (node, group) =>
            {
                if (group == null || node == null)
                {
                    return;
                }
                // TODO maybe don't do from all. Arguably a bug
                (target as L3Graph).RemoveNodeFromAllGroups(node);
            };
            canvas.OnGroupDeletedInternally += (group) =>
            {
                (target as L3Graph).DeleteGroup(group.data);
            };

            canvas.OnNodeCreatedInternally += (node, type) =>
            {
                var asset = (target as L3Graph).CreateNode(type);
                node.data = asset;
            };

            // ? Same as above, but for deletion and duplication etc
            canvas.OnNodeDeletedInternally += (node) =>
            {
                (target as L3Graph).DeleteNode(node.data);
            };
            canvas.OnNodeDuplicatedInternally += (node) =>
            {
                var n = (target as L3Graph).DuplicateNode(node.data);
                canvas.InitNodeExternal(n, n.position);
            };
            canvas.OnNodesCopiedInternally += CopyToClipboard;
            canvas.OnPasteRequestedInternally += PasteFromClipboard;
            canvas.CanPaste = () => _clipboardNodes.Count > 0;
            canvas.OnConnectionCreatedInternally += (connection, type) =>
            {
                var asset = (target as L3Graph).CreateConnection(connection.from.data, connection.to.data, type);
                connection.data = asset;
            };
            canvas.OnConnectionDeletedInternally += (connection) =>
            {
                (target as L3Graph).DeleteConnection(connection.data);
            };

            canvas.OnNodeDoubleClickedInternally += (node) =>
            {
                OnNodeDoubleClicked?.Invoke(node);
            };

            canvas.ConnectionValidator = target.ValidateConnectionRequest;
            canvas.AutoConnectionValidator = target.AutoConnnectionRequest;
            canvas.PossibleConnectionTypes = (target as L3Graph).GraphConnectionTypes();
            canvas.PossibleGroupTypes = (target as L3Graph).GraphGroupTypes();

            breadcrumbs = inspector.Q<ToolbarBreadcrumbs>("Breadcrumbs");
            for (int i = 0; i < graphStack.Count; i++)
            {
                int index = i;
                L3Graph g = graphStack[i];
                if (g == null)
                    continue;
                string name = g.name;
                if (i == graphStack.Count - 1)
                {
                    name = "<b>" + name + "</b>";
                    breadcrumbs.PushItem(name, () =>
                    {
                        // do nothing, we are already on this graph
                    });
                }
                else
                {
                    breadcrumbs.PushItem(name, () =>
                    {
                        // new list up to index i
                        OpenGraphStack(graphStack.GetRange(0, index + 1));
                    });
                }
            }

            inspector.Q<Label>("Typename").text = target.GetType().Name;
            Label typeNameLabel = inspector.Q<Label>("Typename");
            typeNameLabel.text = target.GetType().Name;

            var openScriptElement = inspector.Q("OpenScript");
            openScriptElement.AddManipulator(new Clickable(() =>
            {
                var so = new SerializedObject(target);
                var scriptRef = so.FindProperty("m_Script").objectReferenceValue;
                if (scriptRef != null)
                    AssetDatabase.OpenAsset(scriptRef);
            }));

            graphInspectorRoot = inspector.Q("GraphInspector");
            selectionInspectorRoot = inspector.Q("SelectionInspector");
            graphInspectorRoot.Add(new InspectorElement(target));

            inspectorLabel = inspector.Q<Label>("InspectorLabel");
            inspectorLabel.text = target.GetType().Name;
            inspectorOverlay = inspector.Q("InspectorOverlay");
            // drag
            inspectorOverlay.AddManipulator(new ForceGraphInspectorOverlayManipulator(inspectorOverlay));

            // width resize: apply the persisted width and let the right-edge handle change it
            float inspectorWidth = Mathf.Clamp(
                EditorPrefs.GetFloat(INSPECTOR_WIDTH_SETTING_KEY, DEFAULT_INSPECTOR_WIDTH),
                MIN_INSPECTOR_WIDTH, MAX_INSPECTOR_WIDTH);
            inspectorOverlay.style.maxWidth = MAX_INSPECTOR_WIDTH;
            inspectorOverlay.style.width = inspectorWidth;
            var inspectorResizeHandle = inspector.Q("InspectorResizeHandle");
            inspectorResizeHandle.AddManipulator(new ForceGraphInspectorWidthResizeManipulator(inspectorOverlay));

            foreach (var node in (target as L3Graph).nodes)
            {
                if (node.position == Vector2.zero)
                    node.position = new Vector2(UnityEngine.Random.Range(-200, 200), UnityEngine.Random.Range(-200, 200));
                canvas.InitNodeExternal(node, node.position);
            }

            foreach (var connection in (target as L3Graph).connections)
            {
                canvas.InitConnectionExternal(connection.from, connection.to, connection);
            }

            foreach (var group in (target as L3Graph).groups)
            {
                canvas.InitGroupExternal(group, group.position, group.nodes);
            }

            if (EditorPrefs.GetBool(FIT_TO_SCREEN_SETTINGS_KEY, true))
            {
                // Cause small graphs to zoom in, and large graphs to zoom out when opening if fit is enabled .
                // Just makes it feel a bit more interesting when scrolling through graphs
                canvas.SetViewScale(.5f);
            }

            rootVisualElement.Add(inspector);
        }

        public void OnEnable()
        {
            EditorApplication.update += Update;//
            if (wasInit)
            {
                InitGUI(graphStack);
            }
        }

        private void OnDisable()//
        {
            EditorApplication.update -= Update;
            if (target != null)
            {
                target.OnForceGraphRepaint -= ForceRepaint;
            }
            wasInit = true;
        }

        private void OnDestroy()
        {
            DestroyImmediate(graphParametersInspector);
            DestroyMultiSelectionInspector();
        }

        private void OnSelectionChanged()
        {
            if (canvas.selectedNodes.Count > 1)
            {
                var objects = canvas.selectedNodes
                    .ConvertAll(n => n.data as Object)
                    .Where(o => o != null)
                    .ToArray();
                if (objects.Length > 1)
                {
                    InspectObjects(objects);
                }
                else if (objects.Length == 1)
                {
                    InspectObject(objects[0]);
                }
                else
                {
                    InspectObject(target);
                }
            }
            else if (canvas.selectedNode != null)
            {
                InspectObject(canvas.selectedNode.data);
            }
            else if (canvas.selectedConnection != null)
            {
                InspectObject(canvas.selectedConnection.data);
            }
            else if (canvas.selectedGroup != null)
            {
                InspectObject(canvas.selectedGroup.data);
            }
            else
            {
                InspectObject(target);
            }
        }

        // ? I don't remember why inspect graph is slightly different. I think its not needed anymore. SHould try removing:
        private void InspectGraph()
        {
            graphInspectorRoot.style.display = DisplayStyle.Flex;
            selectionInspectorRoot.style.display = DisplayStyle.None;

            var so = new SerializedObject(target);
            inspectorLabel.text = target.GetType().Name;
        }

        private void InspectObject(Object connection)
        {
            DestroyMultiSelectionInspector();
            selectionInspectorRoot.Clear();
            selectionInspectorRoot.Add(new InspectorElement(connection));
            graphInspectorRoot.style.display = DisplayStyle.None;
            selectionInspectorRoot.style.display = DisplayStyle.Flex;
            var so = new SerializedObject(connection);
            inspectorLabel.text = connection.GetType().Name;
        }

        private void InspectObjects(Object[] objects)
        {
            DestroyMultiSelectionInspector();
            multiSelectionInspector = UnityEditor.Editor.CreateEditor(objects);
            if (multiSelectionInspector == null)
            {
                // Can happen if the targets don't share a compatible editor (e.g. stale/destroyed reference).
                InspectObject(target);
                return;
            }

            selectionInspectorRoot.Clear();
            selectionInspectorRoot.Add(new InspectorElement(multiSelectionInspector));
            graphInspectorRoot.style.display = DisplayStyle.None;
            selectionInspectorRoot.style.display = DisplayStyle.Flex;

            bool sameType = System.Array.TrueForAll(objects, o => o.GetType() == objects[0].GetType());
            inspectorLabel.text = sameType
                ? $"{objects.Length} {objects[0].GetType().Name} selected"
                : $"{objects.Length} selected";
        }

        private void DestroyMultiSelectionInspector()
        {
            if (multiSelectionInspector != null)
            {
                DestroyImmediate(multiSelectionInspector);
                multiSelectionInspector = null;
            }
        }

        #region Clipboard

        // Detached copies rather than serialized text: node bodies routinely hold
        // [SerializeReference] fields, which Instantiate deep-copies correctly and a JSON round trip
        // mangles. Taking the copy up front is also what makes Cut safe -- the clipboard never
        // points at objects the cut then destroys -- and lets one copy be pasted repeatedly.
        // Lives until a domain reload, so it crosses windows and graphs but not editor sessions.
        private static readonly List<L3GraphNode> _clipboardNodes = new List<L3GraphNode>();
        private static readonly List<L3GraphConnection> _clipboardConnections = new List<L3GraphConnection>();

        // The statics go anyway, so the copies have to go with them -- DontSave objects nothing
        // references are what Unity complains about having to clean up on quit.
        [InitializeOnLoadMethod]
        private static void ClearClipboardOnReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearClipboard;
        }

        private static void CopyToClipboard(List<LCanvasNode<L3GraphNode>> nodes)
        {
            if (nodes.Count == 0)
                return;

            L3Graph source = nodes[0].data.graph;
            ClearClipboard();

            Dictionary<L3GraphNode, L3GraphNode> copies = new Dictionary<L3GraphNode, L3GraphNode>();
            foreach (LCanvasNode<L3GraphNode> node in nodes)
            {
                L3GraphNode copy = Detach(node.data);
                copies[node.data] = copy;
                _clipboardNodes.Add(copy);
            }

            // Only an edge with both ends in the selection: one reaching outside it has nothing to
            // land on at the other end.
            foreach (L3GraphConnection connection in source.connections)
            {
                if (!copies.TryGetValue(connection.from, out L3GraphNode from))
                    continue;
                if (!copies.TryGetValue(connection.to, out L3GraphNode to))
                    continue;

                L3GraphConnection copy = Detach(connection);
                copy.from = from;
                copy.to = to;
                _clipboardConnections.Add(copy);
            }
        }

        private void PasteFromClipboard(Vector2 position)
        {
            if (_clipboardNodes.Count == 0)
                return;

            L3Graph graph = target as L3Graph;
            Vector2 offset = position - ClipboardCenter();

            Dictionary<L3GraphNode, L3GraphNode> pasted = new Dictionary<L3GraphNode, L3GraphNode>();
            List<L3GraphNode> added = new List<L3GraphNode>();

            foreach (L3GraphNode source in _clipboardNodes)
            {
                L3GraphNode node = Instantiate(source);
                node.name = source.name;
                node.position = source.position + offset;
                AssetDatabase.AddObjectToAsset(node, graph);

                // The copy arrives with a null graph -- L3GraphNode.graph is an auto-property and so
                // is never serialized -- which is what keeps SetGraph's write-once guard happy.
                node.SetGraph(graph);
                graph.nodes.Add(node);

                pasted[source] = node;
                added.Add(node);
                canvas.InitNodeExternal(node, node.position);
            }

            foreach (L3GraphConnection source in _clipboardConnections)
            {
                L3GraphNode from = pasted[source.from];
                L3GraphNode to = pasted[source.to];

                // Pasting into another graph can hit rules the origin graph did not have, and the
                // nodes are worth keeping even when an edge between them is not allowed here.
                if (!graph.ValidateConnectionRequest(from, to, source.GetType()))
                    continue;

                L3GraphConnection connection = Instantiate(source);
                connection.name = source.name;
                connection.from = from;
                connection.to = to;
                AssetDatabase.AddObjectToAsset(connection, graph);
                graph.connections.Add(connection);

                canvas.InitConnectionExternal(from, to, connection);
            }

            EditorUtility.SetDirty(graph);
            canvas.OnNodesPastedInternally?.Invoke(added);
        }

        private static T Detach<T>(T source) where T : ScriptableObject
        {
            T copy = Instantiate(source);
            copy.name = source.name;
            copy.hideFlags = HideFlags.HideAndDontSave;
            return copy;
        }

        private static Vector2 ClipboardCenter()
        {
            Vector2 sum = Vector2.zero;
            foreach (L3GraphNode node in _clipboardNodes)
            {
                sum += node.position;
            }
            return sum / _clipboardNodes.Count;
        }

        private static void ClearClipboard()
        {
            foreach (L3GraphConnection connection in _clipboardConnections)
            {
                DestroyImmediate(connection);
            }
            foreach (L3GraphNode node in _clipboardNodes)
            {
                DestroyImmediate(node);
            }
            _clipboardConnections.Clear();
            _clipboardNodes.Clear();
        }

        #endregion

        private void Update()
        {
            if (canvas != null)
            {
                canvas.Update();

                // TODO: This is reacting to node position changes:
                // This should be a built in feature of the canvas with a callback like add/delete etc.

                foreach (var node in canvas.nodes)
                {
                    // We ignore this to try and avoid unnecessary writes to the asset
                    if (Mathf.Approximately(node.data.position.x, node.position.x) && Mathf.Approximately(node.data.position.y, node.position.y))
                        continue;

                    node.data.position = node.position;
                    // mark the graph dirty if we ever do a move op.
                    if (!UnityEditor.EditorUtility.IsDirty(target))
                    {
                        UnityEditor.EditorUtility.SetDirty(target);
                    }
                }

                foreach (var group in canvas.groups)
                {
                    if (Mathf.Approximately(group.data.position.x, group.position.x) && Mathf.Approximately(group.data.position.y, group.position.y))
                        continue;
                    group.data.position = group.position;
                }

                if (inspectorOverlay != null && inspectorOverlay.panel != null)
                {
                    // validate overlay position and prefs
                    Vector2 newPos = new Vector2(EditorPrefs.GetFloat(OVERLAY_X_SETTINGS_KEY, 0), EditorPrefs.GetFloat(OVERLAY_Y_SETTINGS_KEY, 0));
                    newPos.x = Mathf.Clamp(newPos.x, 0, inspectorOverlay.panel.visualTree.worldBound.width - inspectorOverlay.layout.width - 24);
                    newPos.y = Mathf.Clamp(newPos.y, 0, inspectorOverlay.panel.visualTree.worldBound.height - inspectorOverlay.layout.height - 48);
                    EditorPrefs.SetFloat("ForceGraphInspectorOverlayManipulatorX", newPos.x);
                    EditorPrefs.SetFloat("ForceGraphInspectorOverlayManipulatorY", newPos.y);
                    inspectorOverlay.transform.position = newPos;
                }
            }
        }

        private void ForceRepaint()
        {
            canvas.RepaintAllElements();
        }

        public string GetTargetAssetPath()
        {
            if (target == null)
                return null;
            return AssetDatabase.GetAssetPath(target);
        }

        public void ReloadFromAssetRefresh()
        {
            if (target != null && graphStack.Count > 0)
            {
                InitGUI(graphStack);
            }
        }
    }
}
