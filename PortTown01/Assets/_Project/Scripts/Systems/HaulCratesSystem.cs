using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Haulers shuttle Crates from the Mill to the Dock and get paid on delivery.
    public class HaulCratesSystem : ISimSystem
    {
        public string Name => "HaulCrates";

        private Worksite _millSite;
        private Worksite _dockSite;
        private Building _mill;
        private Agent _dockBuyer;

        private enum Phase { ToMill, Load, ToDock, Unload }
        private class S { public Phase P = Phase.ToMill; }
        private readonly Dictionary<int, S> _s = new();

        private const int HAUL_PAY_PER_CRATE = 3;   // tune piece-rate

        public void Tick(World world, int _, float dt)
        {
            // cache refs
            if (_mill == null) _mill = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
            if (_millSite == null) _millSite = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Milling);
            if (_dockSite == null) _dockSite = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.DockLoading);
            if (_dockBuyer == null)
                _dockBuyer = world.Agents.OrderByDescending(a => a.Coins)
                    .FirstOrDefault(a => !a.IsVendor && a.SpeedMps == 0f &&
                                         Vector3.Distance(a.Pos, _dockSite?.StationPos ?? Vector3.zero) < 6f);

            if (_mill == null || _millSite == null || _dockSite == null || _dockBuyer == null) return;

            foreach (var a in world.Agents)
            {
                if (a.Role != JobRole.Hauler) continue;
                if (a.Phase != DayPhase.Work) { a.AllowWander = true; continue; }

                if (!_s.TryGetValue(a.Id, out var st)) _s[a.Id] = st = new S();

                a.AllowWander = false;

                switch (st.P)
                {
                    case Phase.ToMill:
                        a.TargetPos = _millSite.StationPos;
                        if (Arrived(a)) st.P = Phase.Load;
                        break;

                    case Phase.Load:
                    {
                        if (!Arrived(a)) { a.TargetPos = _millSite.StationPos; break; }

                        int carried = a.Carry.Get(ItemType.Crate);
                        int maxCarry = Mathf.Max(0, Mathf.FloorToInt(a.CapacityKg / ItemDefs.KgPerUnit(ItemType.Crate)));
                        if (carried >= maxCarry || _mill.Storage.Get(ItemType.Crate) <= 0)
                        {
                            st.P = Phase.ToDock; break;
                        }

                        int want = Mathf.Max(1, maxCarry - carried);
                        int take = Mathf.Min(want, _mill.Storage.Get(ItemType.Crate));
                        if (take > 0 && _mill.Storage.TryRemove(ItemType.Crate, take))
                        {
                            a.Carry.Add(ItemType.Crate, take);
                        }
                        st.P = Phase.ToDock;
                        break;
                    }

                    case Phase.ToDock:
                        a.TargetPos = _dockSite.StationPos;
                        if (Arrived(a)) st.P = Phase.Unload;
                        break;

                    case Phase.Unload:
                    {
                        if (!Arrived(a)) { a.TargetPos = _dockSite.StationPos; break; }

                        int qty = a.Carry.Get(ItemType.Crate);
                        if (qty > 0 && a.Carry.TryRemove(ItemType.Crate, qty))
                        {
                            // --- Company sale: Dock pays the Boss (company), not the hauler ---
                            var boss = world.Agents.FirstOrDefault(x => x.IsEmployer)
                                ?? world.Agents.Where(x => !x.IsVendor).OrderByDescending(x => x.Coins).FirstOrDefault();

                            if (boss == null)
                            {
                                Debug.LogError("[HAUL] No boss/company found for crate sale settlement.");
                                st.P = Phase.ToMill;
                                break;
                            }

                            int saleOwed = world.CratePrice * qty;
                            int salePaid = Mathf.Min(saleOwed, _dockBuyer.Coins);

                            if (salePaid < saleOwed)
                                Debug.LogWarning($"[DOCK] Underpaid sale: owed={saleOwed}, paid={salePaid}, buyerCoins={_dockBuyer.Coins}");

                            // Transfer dock → boss
                            Ledger.Transfer(world, ref _dockBuyer.Coins, ref boss.Coins,
                                            salePaid, LedgerWriter.DockSale, $"crate sale qty={qty}");

                            world.CratesSold  += qty;
                            world.RevenueDock += salePaid;

                            // --- Hauler piece-rate payout (from the Boss) ---
                            int wageOwed = HAUL_PAY_PER_CRATE * qty;
                            int wagePaid = Mathf.Min(wageOwed, boss.Coins);

                            if (wagePaid > 0)
                            {
                                Ledger.Transfer(world, ref boss.Coins, ref a.Coins,
                                                wagePaid, LedgerWriter.WagePayout, $"haul pay qty={qty}");
                                world.WagesHaul += wagePaid;
                            }
                        }

                        st.P = Phase.ToMill;
                        break;
                    }
                }
            }
        }

        private static bool Arrived(Agent a)
        {
            return Vector3.Distance(a.Pos, a.TargetPos) <= a.InteractRange * 1.25f;
        }
    }
}
