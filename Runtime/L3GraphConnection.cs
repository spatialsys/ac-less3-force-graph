using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Less3.Graph
{
    public abstract class L3GraphConnection : ScriptableObject
    {
        [HideInInspector]
        public L3GraphNode from;
        [HideInInspector]
        public L3GraphNode to;

        public static Color defaultColor = new Color(.7f, .7f, .7f, 1f);

        public L3GraphNode GetOtherNode(L3GraphNode node)
        {
            if (from == node)
                return to;
            if (to == node)
                return from;
            return null;
        }
    }
}
