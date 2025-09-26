using UnityEngine;

namespace PortTown01.Core

{
    public class Agent

    {

        public int Id;
        public Vector3 Pos;
        public Vector3 TargetPos;
        public float SpeedMps = 1.5f;

        public float Food = 100f;
        public float Rest = 100f;
        public float Status = 50f;
        public float Security = 50f;

        public float CapacityKg = 20f;     // simple cap for week 1
        public Inventory Carry = new();    // starts empty


        // movement/interaction control
        public bool AllowWander = true;   // when false, Movement won't pick random targets
        public float InteractRange = 1.0f;


    }
}