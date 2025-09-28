using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Hires a few loggers and binds job metadata. No per-second pay here (piece-rate in DemoHarvestSystem).
    public class EmploymentSystem : ISimSystem
    {
        public string Name => "Employment";

        private const int TARGET_LOGGERS = 4;
        private const float WAGE_PER_MIN = 2.0f; // kept for future use
        private const int TARGET_HAULERS = 2;

        private Worksite _logging;
        private Agent _boss;

        public void Tick(World world, int _, float dt)
        {
            if (_logging == null)
                _logging = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Logging);
            if (_boss == null)
                _boss = world.Agents.Where(a => !a.IsVendor).OrderByDescending(a => a.Coins).FirstOrDefault();

            if (_logging == null || _boss == null) return;

            // Ensure TARGET_LOGGERS hired
            var currentLoggers = world.Agents.Where(a => a.Role == JobRole.Logger).ToList();
            int need = TARGET_LOGGERS - currentLoggers.Count;
            if (need > 0)
            {
                var candidates = world.Agents.Where(a =>
                    a.Id != _boss.Id && !a.IsVendor && a.Role == JobRole.None).Take(need).ToList();

                foreach (var c in candidates)
                {
                    var k = new Contract
                    {
                        Id = world.NextContractId++,
                        Type = ContractType.Employment,
                        State = ContractState.Active,
                        EmployerId = _boss.Id,
                        EmployeeId = c.Id,
                        WorksiteId = _logging.Id,
                        WagePerMin = WAGE_PER_MIN,
                        AccruedSinceLastPay = 0f
                    };
                    world.Contracts.Add(k);

                    c.Role = JobRole.Logger;
                    c.EmployerId = _boss.Id;
                    c.JobWorksiteId = _logging.Id;
                    c.ContractId = k.Id;
                }
            }
            // Ensure some Haulers exist
            var currentHaulers = world.Agents.Where(a => a.Role == JobRole.Hauler).ToList();
            int needH = TARGET_HAULERS - currentHaulers.Count;
            if (needH > 0)
            {
                var candidatesH = world.Agents.Where(a =>
                    a.Id != _boss.Id && !a.IsVendor && a.Role == JobRole.None).Take(needH).ToList();

                foreach (var c in candidatesH)
                {
                    var k = new Contract
                    {
                        Id = world.NextContractId++,
                        Type = ContractType.Employment,
                        State = ContractState.Active,
                        EmployerId = _boss.Id,
                        EmployeeId = c.Id,
                        WorksiteId = _logging != null ? _logging.Id : 0, // placeholder
                        WagePerMin = WAGE_PER_MIN
                    };
                    world.Contracts.Add(k);

                    c.Role = JobRole.Hauler;
                    c.EmployerId = _boss.Id;
                    c.JobWorksiteId = null;
                    c.ContractId = k.Id;
                }
            }
        }
    }
}
