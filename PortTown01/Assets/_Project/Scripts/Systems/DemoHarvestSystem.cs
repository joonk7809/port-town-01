using System.Collections.Generic;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Drives a few agents through: Go to forest → harvest logs → go to mill → store logs → repeat.
    // Purely for bring-up; we’ll replace with proper jobs/planner later.
    public class DemoHarvestSystem : ISimSystem
    {
        public string Name => "DemoHarvest";

        private enum Phase { ToForest, Harvest, ToMill, Store }
        private class State { public Phase P; public float HarvestTimer; }

        private readonly Dictionary<int, State> _state = new();
        private Worksite _forestSite;
        private Worksite _millSite;
        private Building _mill;

        // Tunables
        private const int AGENTS_TO_DRIVE = 5;
        private const float HARVEST_TIME_PER_LOG = 1.0f; // sec/log
        private const int LOG_KG = 5;

        public void Tick(World world, int _, float dt)
        {
            // Ensure we have one forest site and one mill site
            if (_forestSite == null || _millSite == null || _mill == null)
            {
                _forestSite = FindFirst(world, WorkType.Logging);
                _millSite = FindFirst(world, WorkType.Milling);
                _mill = (_millSite != null && _millSite.BuildingId.HasValue)
                    ? world.Buildings.Find(b => b.Id == _millSite.BuildingId.Value)
                    : null;
                if (_forestSite == null || _millSite == null || _mill == null) return; // not bootstrapped yet
            }

            // Drive first N agents
            int driven = 0;
            foreach (var a in world.Agents)
            {
                if (a.Role != JobRole.Logger) continue;
                

                if (!_state.TryGetValue(a.Id, out var s))
                    _state[a.Id] = s = new State { P = Phase.ToForest, HarvestTimer = 0f };

                a.AllowWander = false;

                switch (s.P)
                {
                    case Phase.ToForest:
                        a.TargetPos = _forestSite.StationPos;
                        if (Arrived(a)) s.P = Phase.Harvest;
                        break;

                    case Phase.Harvest:
                        // capacity gate: simple max logs by kg
                        int logsCarried = a.Carry.Get(ItemType.Log);
                        int maxLogs = Mathf.FloorToInt(a.CapacityKg / LOG_KG);
                        if (logsCarried >= maxLogs) { s.P = Phase.ToMill; break; }

                        // need stock at node
                        var node = (_forestSite.NodeId.HasValue)
                            ? world.ResourceNodes.Find(n => n.Id == _forestSite.NodeId.Value)
                            : null;
                        if (node == null || node.Stock <= 0) { s.P = Phase.ToMill; break; }

                        if (!Arrived(a)) { a.TargetPos = _forestSite.StationPos; break; }

                        s.HarvestTimer += dt;
                        if (s.HarvestTimer >= HARVEST_TIME_PER_LOG)
                        {
                            s.HarvestTimer = 0f;
                            // harvest one log
                            node.Stock -= 1;
                            a.Carry.Add(ItemType.Log, 1);
                            a.Carry.Kg += LOG_KG;
                        }
                        break;

                    case Phase.ToMill:
                        a.TargetPos = _millSite.StationPos;
                        if (Arrived(a)) s.P = Phase.Store;
                        break;

                    case Phase.Store:
                        if (!Arrived(a)) { a.TargetPos = _millSite.StationPos; break; }

                        int qty = a.Carry.Get(ItemType.Log);
                        if (qty > 0)
                        {
                            a.Carry.TryRemove(ItemType.Log, qty);
                            a.Carry.Kg -= qty * LOG_KG;
                            _mill.Storage.Add(ItemType.Log, qty);
                        }
                        // loop
                        s.P = Phase.ToForest;
                        break;
                }
            }
        }

        private static bool Arrived(Agent a)
        {
            return Vector3.Distance(a.Pos, a.TargetPos) <= a.InteractRange;
        }

        private static Worksite FindFirst(World w, WorkType t)
        {
            return w.Worksites.Find(ws => ws.Type == t);
        }
    }
}
