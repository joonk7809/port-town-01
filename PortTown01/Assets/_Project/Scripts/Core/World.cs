using System.Collections.Generic;
using UnityEngine;

namespace PortTown01.Core
{
    // sim state holder
    public class World
    {
        public int Tick{ get; private set; } = 0;
        public float SimTime { get; private set; } = 0f; // seconds

        public readonly List<Agent> Agents = new();

        public readonly List<ResourceNode> ResourceNodes = new();
        public readonly List<Building> Buildings = new();
        public readonly List<Worksite> Worksites = new();

        public void Advance(int tickInc, float dt)
        {
            Tick += tickInc;
            SimTime += dt;
        }
    }
}