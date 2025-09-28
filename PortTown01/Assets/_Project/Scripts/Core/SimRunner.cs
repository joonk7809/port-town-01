using System.Collections.Generic;
using UnityEngine;
using PortTown01.Systems;

namespace PortTown01.Core
{
    // Attach this to an empty GameObject in Main scene.
    // It runs a fixed-step sim loop independent of render frame rate.
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
        public int bootstrapAgents = 20;

        private float _accum;
        private World _world;
        private List<ISimSystem> _pipeline;

        public World WorldRef => _world;   // read-only access for other systems/HUD


        // ---------- UNITY LIFECYCLE ----------
        void Awake()
        {
            Application.targetFrameRate = 60;  // render target (not enforced hard)
            Time.fixedDeltaTime = fixedDelta;  // for reference; we use our own loop

            Random.InitState(randomSeed);

            _world = new World();
            _pipeline = new List<ISimSystem>
            {
                
                // Input → Contracts → Work → Trade → Needs → Planner → Movement → Economy → Telemetry
                new NeedsDecaySystem(),
                new DayPlanSystem(),
                new EmploymentSystem(),
                new DemoHarvestSystem(),
                new FoodTradeSystem(),
                new MovementSystem(),
                new SleepSystem(),
                new OrderMatchingSystem(),
                new EatingSystem(),
                new MillProcessingSystem(),
                new TelemetrySystem(),
                new CSVSnapshotSystem(),
                new AuditSystem(),
                new GuardrailsSystem()
            };

            BootstrapAgents(bootstrapAgents);
            BootstrapWorld();
        }

        void Update()
        {
            _accum += Time.deltaTime;

            int steps = 0;
            while (_accum >= fixedDelta && steps < maxStepsPerFrame)
            {
                StepOnce();
                _accum -= fixedDelta;
                steps++;
            }
            // If we fell behind massively, drop leftover to keep things stable
            if (steps == maxStepsPerFrame) _accum = 0f;

            // (Optional later) interpolate visuals using _accum/fixedDelta
        }

        // ---------- SIM CORE ----------
        private void StepOnce()
        {
            int tickIndex = _world.Tick + 1; // 1-based tick notion for readability

            foreach (var sys in _pipeline)
                sys.Tick(_world, tickIndex, fixedDelta);

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
                a.HomePos = new Vector3(UnityEngine.Random.Range(-6f, 6f), 0f, UnityEngine.Random.Range(14f, 24f));

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
                Stock = 100,
                MaxStock = 100,
                RegenPerSec = 0f
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
                ServiceDurationSec = 1.2f
            });

            var boss = new Agent
            {
                Id = _world.Agents.Count,
                Pos = new Vector3(22f, 0f, 0f),
                TargetPos = new Vector3(22f, 0f, 0f),
                SpeedMps = 0f,
                Coins = 5000,
                AllowWander = false
            };
            _world.Agents.Add(boss);
            SpawnView(boss);
            SpawnMarker(boss.Pos, new Color(1.0f, 0.5f, 0f), "Boss");
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

        



        

    }
}
