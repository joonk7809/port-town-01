using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems

{
    public class TelemetrySystem : ISimSystem
    {
        public string Name => "Telemetry";

        private float _accum;           // real-time seconds for sampling
        private const float SAMPLE_EVERY = 1f; // log once per second

        public void Tick(World world, int _, float dt)
        {
            _accum += dt;
            if (_accum < SAMPLE_EVERY) return;
            _accum = 0f;

            int n = world.Agents.Count;
            if (n == 0) return;

            float avgFood = world.Agents.Average(a => a.Food);
            float avgRest = world.Agents.Average(a => a.Rest);

            Debug.Log($"[TEL] t={world.SimTime:F1}s tick={world.Tick} agents={n} avgFood={avgFood:F1} avgRest={avgRest:F1}");
        }
    }
}
