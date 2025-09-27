using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Hires a few loggers, attaches them to the Logging worksite, and pays wages per second while working nearby.
    public class EmploymentSystem : ISimSystem
    {
        public string Name => "Employment";

        private const int TARGET_LOGGERS = 4;
        private const float WAGE_PER_MIN = 2.0f;  // coins/min
        private const float ON_SITE_RADIUS = 2.0f; // meters to count as "working"

        private Worksite _logging;
        private Agent _boss;

        public void Tick(World world, int _, float dt)
        {
            // Cache references
            if (_logging == null)
                _logging = world.Worksites.FirstOrDefault(ws => ws.Type == JobSiteType());
            if (_boss == null)
                _boss = world.Agents
                    .Where(a => !a.IsVendor)
                    .OrderByDescending(a => a.Coins)
                    .FirstOrDefault();

            if (_logging == null || _boss == null) return;

            // 1) Ensure TARGET_LOGGERS are hired
            var currentLoggers = world.Agents.Where(a => a.Role == JobRole.Logger).ToList();
            int need = TARGET_LOGGERS - currentLoggers.Count;
            if (need > 0)
            {
                var candidates = world.Agents.Where(a =>
                    a.Id != _boss.Id && !a.IsVendor && a.Role == JobRole.None).Take(need).ToList();

                foreach (var c in candidates)
                {
                    // Create contract
                    var k = new Contract
                    {
                        Id = world.NextContractId++,
                        Type = ContractType.Employment,
                        State = ContractState.Active,
                        EmployerId = _boss.Id,
                        EmployeeId = c.Id,
                        WorksiteId = _logging.Id,
                        WagePerMin = WAGE_PER_MIN
                    };
                    world.Contracts.Add(k);

                    // Bind agent job fields
                    c.Role = JobRole.Logger;
                    c.EmployerId = _boss.Id;
                    c.JobWorksiteId = _logging.Id;
                    c.ContractId = k.Id;
                }
            }

            // 2) Pay wages per second for those on site
            foreach (var k in world.Contracts.Where(x => x.State == ContractState.Active && x.Type == ContractType.Employment))
            {
                var emp = world.Agents.FirstOrDefault(a => a.Id == k.EmployeeId);
                var boss = world.Agents.FirstOrDefault(a => a.Id == k.EmployerId);
                var site = world.Worksites.FirstOrDefault(ws => ws.Id == k.WorksiteId);
                if (emp == null || boss == null || site == null) continue;

                // working if within ON_SITE_RADIUS of the worksite station
                bool working = Vector3.Distance(emp.Pos, site.StationPos) <= ON_SITE_RADIUS;

                if (working && boss.Coins > 0)
                {
                    float payPerSec = k.WagePerMin / 60f;
                    // clamp to boss remaining coins so we never go negative
                    float pay = Mathf.Min(payPerSec * dt, boss.Coins);
                    int coins = Mathf.FloorToInt(pay);   // integer coins
                    if (coins > 0)
                    {
                        boss.Coins -= coins;
                        emp.Coins  += coins;
                    }
                }
            }
        }

        private static WorkType JobSiteType() => WorkType.Logging;
    }
}
