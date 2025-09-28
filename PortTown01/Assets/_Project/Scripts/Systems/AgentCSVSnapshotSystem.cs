using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Writes one CSV row PER AGENT at a slow cadence (default 5s)
    // to Application.persistentDataPath/Snapshots/Agents/run_agents_YYYYMMDD_HHMMSS.csv
    public class AgentCSVSnapshotSystem : ISimSystem
    {
        public string Name => "AgentCSV";

        const float SAMPLE_EVERY_SEC = 5f;   // <- tune cadence here
        const float DAY_SECONDS = 600f;
        const float START_HOUR  = 9f;

        private float _accum = 0f;
        private string _filePath;
        private bool _wroteHeader = false;

        public void Tick(World world, int _, float dt)
        {
            _accum += dt;
            if (_accum < SAMPLE_EVERY_SEC) return;
            _accum = 0f;

            EnsureFile();

            float sim = world.SimTime;
            float daySec = (float)((sim + (START_HOUR/24f)*DAY_SECONDS) % DAY_SECONDS);
            int hh = Mathf.FloorToInt(daySec / DAY_SECONDS * 24f);
            int mm = Mathf.FloorToInt(((daySec / DAY_SECONDS * 24f) - hh) * 60f);
            string tod = $"{hh:D2}:{mm:D2}";

            if (!_wroteHeader)
            {
                var header = string.Join(",",
                    "tick","sim_s","tod",
                    "id","isVendor","isEmployer",
                    "role","phase","intent",
                    "coins","food","rest",
                    "kg","capKg",
                    "carryFood","carryLog","carryPlank","carryCrate",
                    "posX","posZ","tgtX","tgtZ","dist",
                    "employerId","worksiteId","contractId"
                );
                File.AppendAllText(_filePath, header + "\n", Encoding.UTF8);
                Debug.Log($"[AGCSV] Snapshotting per-agent to: {_filePath}");
                _wroteHeader = true;
            }

            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(2048);

            foreach (var a in world.Agents.OrderBy(x => x.Id))
            {
                float dist = Vector3.Distance(a.Pos, a.TargetPos);

                int cFood   = a.Carry.Get(ItemType.Food);
                int cLog    = a.Carry.Get(ItemType.Log);
                int cPlank  = a.Carry.Get(ItemType.Plank);
                int cCrate  = a.Carry.Get(ItemType.Crate);

                sb.Append(world.Tick.ToString(inv)).Append(',');
                sb.Append(sim.ToString("F1", inv)).Append(',');
                sb.Append(tod).Append(',');

                sb.Append(a.Id.ToString(inv)).Append(',');
                sb.Append(a.IsVendor ? "1," : "0,");
                sb.Append(a.IsEmployer ? "1," : "0,");

                sb.Append(a.Role).Append(',');
                sb.Append(a.Phase).Append(',');
                sb.Append(a.Intent).Append(',');

                sb.Append(a.Coins.ToString(inv)).Append(',');
                sb.Append(a.Food.ToString("F1", inv)).Append(',');
                sb.Append(a.Rest.ToString("F1", inv)).Append(',');

                sb.Append(a.Carry.Kg.ToString("F2", inv)).Append(',');
                sb.Append(a.CapacityKg.ToString("F1", inv)).Append(',');

                sb.Append(cFood.ToString(inv)).Append(',');
                sb.Append(cLog.ToString(inv)).Append(',');
                sb.Append(cPlank.ToString(inv)).Append(',');
                sb.Append(cCrate.ToString(inv)).Append(',');

                sb.Append(a.Pos.x.ToString("F2", inv)).Append(',');
                sb.Append(a.Pos.z.ToString("F2", inv)).Append(',');
                sb.Append(a.TargetPos.x.ToString("F2", inv)).Append(',');
                sb.Append(a.TargetPos.z.ToString("F2", inv)).Append(',');
                sb.Append(dist.ToString("F2", inv)).Append(',');

                sb.Append((a.EmployerId?.ToString() ?? "-")).Append(',');
                sb.Append((a.JobWorksiteId?.ToString() ?? "-")).Append(',');
                sb.Append((a.ContractId?.ToString() ?? "-")).Append('\n');
            }

            File.AppendAllText(_filePath, sb.ToString(), Encoding.UTF8);
        }

        private void EnsureFile()
        {
            if (!string.IsNullOrEmpty(_filePath)) return;
            string dir = Path.Combine(Application.persistentDataPath, "Snapshots", "Agents");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _filePath = Path.Combine(dir, $"run_agents_{ts}.csv");
        }
    }
}
