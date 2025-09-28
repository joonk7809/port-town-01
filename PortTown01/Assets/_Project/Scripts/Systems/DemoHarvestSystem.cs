using System.Collections.Generic;
using UnityEngine;
using PortTown01.Core;
using System.Linq;

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
                if (a.Phase != DayPhase.Work) { a.AllowWander = true; continue; }
                

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
                        
                        if (!Arrived(a)) { a.TargetPos = _forestSite.StationPos; break; }

                        s.HarvestTimer += dt;
                        if (s.HarvestTimer >= HARVEST_TIME_PER_LOG)
                        {
                            s.HarvestTimer = 0f;

                            var node = (_forestSite.NodeId.HasValue)
                                ? world.ResourceNodes.Find(n => n.Id == _forestSite.NodeId.Value)
                                : null;
                            if (node == null || node.Stock <= 0) { s.P = Phase.ToMill; break; }

                            // Try to add; if overweight, go deliver
                            if (a.Carry.TryAdd(ItemType.Log, 1, a.CapacityKg))
                                node.Stock -= 1;
                            else
                                s.P = Phase.ToMill;
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
                            if (a.Carry.TryRemove(ItemType.Log, qty))
                                _mill.Storage.Add(ItemType.Log, qty);

                            // --- PAY PIECE-RATE ON DELIVERY ---
                            var k = world.Contracts.FirstOrDefault(c =>
                                c.State == ContractState.Active &&
                                c.Type  == ContractType.Employment &&
                                c.EmployeeId == a.Id);

                            if (k != null)
                            {
                                var boss = world.Agents.FirstOrDefault(x => x.Id == k.EmployerId);
                                if (boss != null && boss.Coins > 0)
                                {
                                    int owed = Mathf.Min(boss.Coins, qty * Econ.EconDefs.WAGE_PER_LOG);
                                    int pay  = Mathf.Min(owed, boss.Coins);
                                    boss.Coins -= pay;
                                    a.Coins    += pay;
                                    // (optional) accumulate stats in k.AccruedSinceLastPay += pay;
                                }
                            }
                        }
                        
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
