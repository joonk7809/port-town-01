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

        // ---------- UNITY LIFECYCLE ----------
        void Awake()
        {
            Application.targetFrameRate = 60;  // render target (not enforced hard)
            Time.fixedDeltaTime = fixedDelta;  // for reference; we use our own loop

            Random.InitState(randomSeed);

            _world = new World();
            _pipeline = new List<ISimSystem>
            {
                // Order mirrors the doc (we’ll fill more later):
                // Input → Contracts → Work → Trade → Needs → Planner → Movement → Economy → Telemetry
                new NeedsDecaySystem(),
                // (Planner comes later)
                new MovementSystem(),
                // (Economy/Trade later)
                new TelemetrySystem()
            };

            BootstrapAgents(bootstrapAgents);
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

                // Optional: spawn a tiny visual so you can see motion
                SpawnView(a);
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
    }
}
