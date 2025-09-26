using UnityEngine;

namespace PortTown01.Core
{
    public class Building
    {
        public int Id;
        public BuildingType Type;
        public Vector3 Pos;
        public int Slots = 1;

        public readonly Inventory Storage = new(); // building storage
    }
}
