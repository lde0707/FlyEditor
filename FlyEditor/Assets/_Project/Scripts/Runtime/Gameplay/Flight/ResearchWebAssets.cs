using System;
using UnityEngine;

namespace FlyEditor.Flight
{
    [CreateAssetMenu(menuName = "FlyEditor/Research Web Assets")]
    public sealed class ResearchWebAssets : ScriptableObject
    {
        [Serializable]
        public sealed class Family
        {
            public Color accent, dark;
            public Texture2D ready, pressed, success, maximum, panel;
            public Texture2D[] feedback;
        }
        [Serializable]
        public sealed class Node
        {
            public string id, title, summary;
            public int runtimeIndex = -1;
            public bool unlocksAbility;
            public float frameSize = 104;
            public Texture2D icon;
            public int family, parent = -1;
            public int[] prerequisiteIndices;
            public Vector2 position;
            public int[] costs;
            public Texture2D[] normal, selected;
        }
        public Texture2D background, panel, plane, root, currency, backButton;
        public Family[] families;
        public Node[] nodes;

        public static bool IsOwned(Node n, FlightResearch research) => research != null && research.Owns(n.id);

        public bool IsVisible(int index, FlightResearch research)
        {
            if (research == null || nodes == null || index < 0 || index >= nodes.Length) return false;
            var n = nodes[index];
            if (!research.Unlocked || n.runtimeIndex < 0) return false;
            if (IsOwned(n, research)) return true;
            if (n.prerequisiteIndices != null)
                for (int i = 0; i < n.prerequisiteIndices.Length; i++)
                {
                    int p = n.prerequisiteIndices[i];
                    if (p < 0 || p >= nodes.Length || !IsOwned(nodes[p], research)) return false;
                }
            return true;
        }
        public bool CanBuy(int index, FlightResearch research)
        {
            if (!IsVisible(index, research)) return false;
            var n = nodes[index];
            return !IsOwned(n, research) && n.costs != null && n.costs.Length == 1
                && n.costs[0] > 0 && research.ActiveRunId == 0 && research.Points >= n.costs[0];
        }
    }
}
