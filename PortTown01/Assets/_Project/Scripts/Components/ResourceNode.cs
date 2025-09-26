using UnityEngine;

namespace PortTown01.Core
{
    public class ResourceNode
    {
        public int Id;
        public NodeType Type;
        public Vector3 Pos;

        public int Stock;        // current available units
        public int MaxStock;     // cap for regen
        public float RegenPerSec; // not used yet this step
    }
}
