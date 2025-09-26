using UnityEngine;

namespace PortTown01.Core
{
    public class Worksite
    {
        public int Id;
        public int? BuildingId;   // null if site is for a node
        public int? NodeId;       // null if site is for a building
        public WorkType Type;
        public Vector3 StationPos;
        public bool InUse;        // single station for now
    }
}
