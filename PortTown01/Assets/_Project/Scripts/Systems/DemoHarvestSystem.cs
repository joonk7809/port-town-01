using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Simple logger workflow: go to forest, harvest logs, carry to mill, get paid (handled in other systems).
    public class DemoHarvestSystem : ISimSystem
    {
        public string Name => "DemoHarvest";

        private Worksite _forestSite;
        private Worksite _millSite;
        private Building _mill;

        private const float HARVEST_TIME_PER_LOG = 0.9f;
        private const float LOG_KG = 3.0f;

        private enum Phase { ToForest, Harvest, ToMill, Store }
        private class State { public Phase P = Phase.ToForest; public float HarvestTimer = 0f; }
        private readonly System.Collections.Generic.Dictionary<int, State> _s = new();

        public void Tick(World world, int _, float dt)
        {
            if (_forestSite == null) _forestSite = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Logging);
            if (_millSite   == null) _millSite   = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Milling);
            if (_mill       == null) _mill       = world.Buildings.FirstOrDefault(b  => b.Type == BuildingType.Mill);
            if (_forestSite == null || _millSite == null || _mill == null) return;

            foreach (var a in world.Agents)
            {
                if (a.Role != JobRole.Logger) continue;
                if (a.Phase != DayPhase.Work) { a.AllowWander = true; continue; }

                if (!_s.TryGetValue(a.Id, out var s)) _s[a.Id] = s = new State();
                a.AllowWander = false;

                switch (s.P)
                {
                    case Phase.ToForest:
                        a.TargetPos = _forestSite.StationPos;
                        if (Arrived(a)) s.P = Phase.Harvest;
                        break;

                    case Phase.Harvest:
                    {
                        if (!Arrived(a)) { a.TargetPos = _forestSite.StationPos; break; }

                        // Find node bound to this worksite if any
                        var node = (_forestSite.NodeId.HasValue)
                            ? world.ResourceNodes.Find(n => n.Id == _forestSite.NodeId.Value)
                            : world.ResourceNodes.FirstOrDefault();

                        if (node == null || node.Stock <= 0)
                        {
                            s.P = Phase.ToMill;
                            break;
                        }

                        s.HarvestTimer += dt;
                        if (s.HarvestTimer >= HARVEST_TIME_PER_LOG)
                        {
                            s.HarvestTimer = 0f;

                            // Try to add one log; if overweight, go deliver
                            if (a.Carry.TryAdd(ItemType.Log, 1, a.CapacityKg))
                            {
                                node.Stock -= 1;
                            }
                            else
                            {
                                s.P = Phase.ToMill;
                            }
                        }
                        break;
                    }

                    case Phase.ToMill:
                        a.TargetPos = _millSite.StationPos;
                        if (Arrived(a)) s.P = Phase.Store;
                        break;

                    case Phase.Store:
                    {
                        if (!Arrived(a)) { a.TargetPos = _millSite.StationPos; break; }

                        int qty = a.Carry.Get(ItemType.Log);
                        if (qty > 0 && a.Carry.TryRemove(ItemType.Log, qty))
                        {
                            _mill.Storage.Add(ItemType.Log, qty);
                        }

                        // head back to forest
                        s.P = Phase.ToForest;
                        break;
                    }
                }
            }
        }

        private static bool Arrived(Agent a)
            => Vector3.Distance(a.Pos, a.TargetPos) <= a.InteractRange * 1.25f;
    }
}
