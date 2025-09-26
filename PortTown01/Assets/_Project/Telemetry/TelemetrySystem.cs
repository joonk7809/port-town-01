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

            int totalItems = world.Agents.Sum(a => a.Carry.Items.Values.Sum());

            int forestStock = world.ResourceNodes.Count > 0 ? world.ResourceNodes[0].Stock : 0;
            int millLogs   = (world.Buildings.Count > 0) ? world.Buildings[0].Storage.Get(ItemType.Log)   : 0;
            int millPlanks = (world.Buildings.Count > 0) ? world.Buildings[0].Storage.Get(ItemType.Plank) : 0;


            Debug.Log($"[TEL] t={world.SimTime:F1}s tick={world.Tick} agents={n} " +
                      $"avgFood={avgFood:F1} avgRest={avgRest:F1} items={totalItems}" +
                      $" forestStock={forestStock} millLogs={millLogs}" +
                      $" forestStock={forestStock} millLogs={millLogs} millPlanks={millPlanks}");
 
        }
    }
}
