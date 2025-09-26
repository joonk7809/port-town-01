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


    }
}