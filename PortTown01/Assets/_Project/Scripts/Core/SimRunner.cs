using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;  // Stopwatch
using UnityEngine;
using PortTown01.Core;
using PortTown01.Systems;

namespace PortTown01.Core
{
    // Attach this to an empty GameObject in Main scene.
    // It runs a fixed-step sim loop independent of render frame rate and profiles per-system cost.
    public class SimRunner : MonoBehaviour
    {
        [Header("Fixed-step settings")]
        [Tooltip("Simulation tick size, seconds (0.05 = 20 Hz)")]
        public float fixedDelta = 0.05f;

        [Tooltip("Clamp the number of catch-up sim steps per frame to avoid spiral of death")]
        public int maxStepsPerFrame = 8;

        [Header("Seed for deterministic randomness (Movement targets, etc.)")]
        public int randomSeed = 12345;

        [Header("Bootstrap")]
        [Tooltip("How many placeholder agents to spawn at start")]
        [SerializeField] public int bootstrapAgents = 200;

        private World _world;
        private List<ISimSystem> _pipeline;

        [SerializeField] private float _fixedDt = 0.05f;   // 20 Hz sim
        [SerializeField] private int   _maxStepsPerFrame = 6; // cap work per frame
        private float _accum = 0f;

        public World WorldRef => _world;   // read-only access for other systems/HUD

        // ---- Perf probe state ----
        private const float PERF_REPORT_AT_SECONDS = 600f; // match Gatekeeper default (10 minutes)
        private Stopwatch _sw;
        private List<float>[] _sysDurMs;   // per-system sample list (ms)
        private string[] _sysNames;
        private bool _perfReported;

        // ---------- UNITY LIFECYCLE ----------
        void Awake()
        {
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = fixedDelta;

            // sync public inspector fields into the loop fields
            _fixedDt = fixedDelta;
            _maxStepsPerFrame = maxStepsPerFrame;

            Random.InitState(randomSeed);

            _world = new World();

            _world.Food_Q = 20;          // batch size
            _world.Food_s = 120;         // reorder point (inv+escrow)
            _world.Food_S = 360;         // order-up-to level
            _world.FoodWholesale = 3.0f; // unit wholesale cost (coins)
            _world.FoodLeadTimeSec = 10f;

            _pipeline = new List<ISimSystem>
            {
                // Must run first so scripted shocks apply before controllers/markets
                new ScenarioRunnerSystem(),

                // Input → Contracts → Work → Trade → Needs → Planner → Movement → Economy → Telemetry
                new NeedsDecaySystem(),
                new DayPlanSystem(),
                new EmploymentSystem(),

                new PlannerSystem(),
                new ResourceRegenSystem(),

                new DemoHarvestSystem(),
                new FoodTradeSystem(),

                new MovementSystem(),
                new SleepSystem(),

                new OrderMatchingSystem(),
                new OrderBookCleanupSystem(),   // expire stale orders; guarantee refunds/releases
                new EatingSystem(),

                new MillProcessingSystem(),
                new CratePackingSystem(),
                new HaulCratesSystem(),

                new PricePIControllerSystem(),
                new VendorRestockSystem(),
                new CityBudgetAndDemandSystem(),
   
                new TelemetrySystem(),
                new AgentCSVSnapshotSystem(),
                new CSVSnapshotSystem(),

                // Audit must precede Gatekeeper and Guardrails
                new AuditSystem(),
                // Gatekeeper reads final invariants before Guardrails clamps anything
                new GatekeeperSystem(),
                // Guardrails can clamp in dev to keep sim from exploding after we've recorded violations
                new GuardrailsSystem(),

                new KPISystem(),
            };

            // ---- Perf probe init ----
            _sw = new Stopwatch();
            _sysNames = _pipeline.Select(s => s.Name).ToArray();
            _sysDurMs = new List<float>[_pipeline.Count];
            for (int i = 0; i < _pipeline.Count; i++) _sysDurMs[i] = new List<float>(12000);

            BootstrapAgents(bootstrapAgents);
            BootstrapWorld();

            UnityEngine.Debug.Log($"[SimRunner] bootstrapAgents = {bootstrapAgents}");
        }

        private void Update()
        {
            _accum += Time.deltaTime;

            int steps = 0;
            while (_accum >= _fixedDt && steps < _maxStepsPerFrame)
            {
                // one fixed tick
                for (int i = 0; i < _pipeline.Count; i++)
                {
                    _sw.Restart();
                    _pipeline[i].Tick(_world, _world.Tick, _fixedDt);
                    _sw.Stop();
                    _sysDurMs[i].Add((float)_sw.Elapsed.TotalMilliseconds);
                }

                _world.Advance(1, _fixedDt);
                _accum -= _fixedDt;
                steps++;
            }

            // If we fall way behind (e.g., pause/unpause), trim backlog to avoid spiral of death
            if (steps == _maxStepsPerFrame && _accum > _fixedDt * 4f)
                _accum = _fixedDt; // keep one extra step pending

            // Emit perf summary once per run
            if (!_perfReported && _world.SimTime >= PERF_REPORT_AT_SECONDS)
            {
                WritePerfSummaryCsv();
                _perfReported = true;
            }
        }

        // ---------- SIM CORE ----------
        private void StepOnce()
        {
            int tickIndex = _world.Tick + 1; // 1-based tick notion for readability

            for (int i = 0; i < _pipeline.Count; i++)
            {
                _sw.Restart();
                _pipeline[i].Tick(_world, tickIndex, fixedDelta);
                _sw.Stop();
                _sysDurMs[i].Add((float)_sw.Elapsed.TotalMilliseconds);
            }

            _world.Advance(1, fixedDelta);
        }

        // ---------- BOOTSTRAP ----------
        private void BootstrapAgents(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var a = new Agent
                {
                    Id = i,
                    Pos = new Vector3(Random.Range(-10f, 10f), 0f, Random.Range(-10f, 10f)),
                    TargetPos = new Vector3(Random.Range(-30f, 30f), 0f, Random.Range(-30f, 30f)),
                    SpeedMps = Random.Range(1.2f, 2.0f)
                };
                _world.Agents.Add(a);
                a.HomePos = new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(14f, 24f));

                a.Food = Random.Range(70f, 100f);
                a.Rest = Random.Range(60f, 100f);

                a.Coins = 100;

                // Optional: spawn a tiny visual so you can see motion
                SpawnView(a);
                SpawnMarker(a.HomePos, new Color(0.4f,0.4f,1f,0.6f), $"Home_{a.Id}");
            }
        }

        private void SpawnView(Agent a)
        {
            // Simple capsule so you can see things move; replace with prefab later
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Agent_{a.Id}";
            go.transform.position = a.Pos;
            // Attach a tiny component to follow sim position
            go.AddComponent<AgentView>().Bind(a);
        }

        private void BootstrapWorld()
        {
            // 1) Forest node
            var forest = new ResourceNode
            {
                Id = 0,
                Type = NodeType.Forest,
                Pos = new Vector3(-20f, 0f, 0f),
                    Stock = 1500,
                    MaxStock = 5000,
                    RegenPerSec = 0.5f
            };
            _world.ResourceNodes.Add(forest);

            // 2) Mill building
            var mill = new Building
            {
                Id = 0,
                Type = BuildingType.Mill,
                Pos = new Vector3(20f, 0f, 0f),
                Slots = 1
            };
            _world.Buildings.Add(mill);

            // 3) Worksites: Logging + Milling
            _world.Worksites.Add(new Worksite
            {
                Id = 0,
                NodeId = forest.Id,
                BuildingId = null,
                Type = WorkType.Logging,
                StationPos = forest.Pos + new Vector3(2f, 0f, 0f),
                InUse = false,
                OccupantId = null
            });

            _world.Worksites.Add(new Worksite
            {
                Id = 1,
                NodeId = null,
                BuildingId = mill.Id,
                Type = WorkType.Milling,
                StationPos = mill.Pos + new Vector3(-2f, 0f, 0f),
                InUse = false,
                OccupantId = null
            });

            // 4) Visual markers
            SpawnMarker(forest.Pos, Color.green, "Forest");
            SpawnMarker(mill.Pos, Color.yellow, "Mill");

            // 5) Vendor agent (market)
            var vendor = new Agent
            {
                Id = _world.Agents.Count,
                Pos = new Vector3(0f, 0f, -15f),
                TargetPos = new Vector3(0f, 0f, -15f),
                SpeedMps = 0f,
                IsVendor = true,
                Coins = 0,
                AllowWander = false
            };
            vendor.Carry.Add(ItemType.Food, 200);
            vendor.Coins = 1500;
            _world.Agents.Add(vendor);
            SpawnView(vendor);
            SpawnMarker(vendor.Pos, Color.red, "Market");

            // 6) Trading worksite at vendor (single occupant)
            _world.Worksites.Add(new Worksite
            {
                Id = 2,
                NodeId = null,
                BuildingId = null,
                Type = WorkType.Trading,
                StationPos = vendor.Pos + new Vector3(0.0f, 0f, 1.0f),
                InUse = false,
                OccupantId = null,
                ServiceRemainingSec = 0f,
                ServiceDurationSec = 0.7f
            });

            var boss = new Agent
            {
                Id = _world.Agents.Count,
                Pos = new Vector3(22f, 0f, 0f),
                TargetPos = new Vector3(22f, 0f, 0f),
                SpeedMps = 0f,
                Coins = 5000,
                AllowWander = false,
                IsEmployer = true
            };
            _world.Agents.Add(boss);
            SpawnView(boss);
            SpawnMarker(boss.Pos, new Color(1.0f, 0.5f, 0f), "Boss");

            // 7) Dock building
            var dock = new Building
            {
                Id = 1,
                Type = BuildingType.Dock,
                Pos = new Vector3(40f, 0f, 0f),
                Slots = 1
            };
            _world.Buildings.Add(dock);
            SpawnMarker(dock.Pos, new Color(0.2f, 0.8f, 1f), "Dock");

            // 8) Dock loading worksite
            _world.Worksites.Add(new Worksite
            {
                Id = 3,
                NodeId = null,
                BuildingId = dock.Id,
                Type = WorkType.DockLoading,
                StationPos = dock.Pos + new Vector3(-2f, 0f, 0f),
                InUse = false,
                OccupantId = null,
                ServiceRemainingSec = 0f,
                ServiceDurationSec  = 0.8f
            });

            // 9) Dock Authority (crate buyer with coins)
            var dockBuyer = new Agent
            {
                Id = _world.Agents.Count,
                Pos = dock.Pos + new Vector3(2f, 0f, 0f),
                TargetPos = dock.Pos + new Vector3(2f, 0f, 0f),
                SpeedMps = 0f,
                Coins = 10000,
                AllowWander = false
            };
            _world.Agents.Add(dockBuyer);
            SpawnView(dockBuyer);
            SpawnMarker(dockBuyer.Pos, new Color(0.2f, 0.8f, 1f), "Buyer");
        }

        private void SpawnMarker(Vector3 pos, Color color, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Marker_{name}";
            go.transform.position = pos;
            go.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.color = color;
        }

        // ---------- Perf summary ----------
        private void WritePerfSummaryCsv()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Snapshots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "PerfSummary.csv");

            bool writeHeader = !File.Exists(path);
            using (var sw = new StreamWriter(path, append: true))
            {
                if (writeHeader)
                {
                    sw.WriteLine(string.Join(",",
                        "timestampUtc","simSeconds",
                        "system","samples","mean_ms","p50_ms","p95_ms","p99_ms"));
                }

                for (int i = 0; i < _pipeline.Count; i++)
                {
                    var samples = _sysDurMs[i];
                    if (samples.Count == 0) continue;

                    // copy + sort for percentile calc
                    var arr = samples.ToArray();
                    System.Array.Sort(arr);

                    float mean = 0f;
                    for (int k = 0; k < arr.Length; k++) mean += arr[k];
                    mean /= arr.Length;

                    float p50 = Percentile(arr, 0.50f);
                    float p95 = Percentile(arr, 0.95f);
                    float p99 = Percentile(arr, 0.99f);

                    sw.WriteLine(string.Join(",",
                        System.DateTime.UtcNow.ToString("o"),
                        _world.SimTime.ToString("F1"),
                        SanitizeName(_sysNames[i]),
                        arr.Length,
                        mean.ToString("F4"),
                        p50.ToString("F4"),
                        p95.ToString("F4"),
                        p99.ToString("F4")));
                }
            }

            UnityEngine.Debug.Log($"[Perf] Wrote {_pipeline.Count} system summaries to {path}");
        }

        private static float Percentile(float[] sorted, float p)
        {
            if (sorted == null || sorted.Length == 0) return 0f;
            float pos = (sorted.Length - 1) * Mathf.Clamp01(p);
            int lo = Mathf.FloorToInt(pos);
            int hi = Mathf.CeilToInt(pos);
            if (lo == hi) return sorted[lo];
            float frac = pos - lo;
            return Mathf.Lerp(sorted[lo], sorted[hi], frac);
        }

        private static string SanitizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unknown";
            // Remove commas to keep CSV simple
            return s.Replace(",", " ");
        }
    }
}
