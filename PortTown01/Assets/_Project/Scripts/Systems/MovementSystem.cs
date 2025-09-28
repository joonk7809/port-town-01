using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Naive point-to-point move (no navmesh yet). We’ll swap for navmesh later.
    public class MovementSystem : ISimSystem
    {
        public string Name => "Movement";

        // Sandbox bounds (meters). We'll keep agents in a 100x100 square for now.
        readonly Vector2 _min = new(-50f, -50f);
        readonly Vector2 _max = new( 50f,  50f);

        // Small dwell so wanderers don’t re-roll every frame at “arrival”
        private readonly System.Collections.Generic.Dictionary<int, float> _nextWanderAt = new();
        private const float WANDER_DWELL_MIN = 0.8f;
        private const float WANDER_DWELL_MAX = 2.2f;

        public void Tick(World world, int _, float dt)
        {
            foreach (var a in world.Agents)
            {
                // Arrival threshold scales with agent’s interact range
                float arriveDist = Mathf.Max(0.2f, a.InteractRange * 0.5f);

                var to = a.TargetPos - a.Pos;
                float dist = to.magnitude;

                // Close enough? handle arrival
                if (dist <= arriveDist)
                {
                    // Snap to exact target to avoid micro-jitter
                    a.Pos = a.TargetPos;

                    // Wander logic: only pick a new target after a small dwell
                    if (a.AllowWander)
                    {
                        float now = (float)world.SimTime;
                        if (!_nextWanderAt.TryGetValue(a.Id, out var tNext) || now >= tNext)
                        {
                            // New random target within bounds
                            float tx = Random.Range(_min.x, _max.x);
                            float tz = Random.Range(_min.y, _max.y);
                            a.TargetPos = new Vector3(tx, 0f, tz);

                            // Set next allowed wander time
                            _nextWanderAt[a.Id] = now + Random.Range(WANDER_DWELL_MIN, WANDER_DWELL_MAX);
                        }
                    }
                    continue; // nothing else to do this tick
                }

                // Micro-opt: skip very tiny moves (already nearly there, but above arriveDist)
                if (dist <= arriveDist * 0.3f)
                    continue;

                // Move toward target (no overshoot)
                float step = a.SpeedMps * dt;
                if (step >= dist)
                {
                    a.Pos = a.TargetPos;
                }
                else
                {
                    // Normalize safely
                    var dir = to / (dist + 1e-6f);
                    a.Pos += dir * step;
                }

                // If target somehow falls outside bounds (e.g., external set), clamp it for wanderers
                if (a.AllowWander)
                {
                    float clampedX = Mathf.Clamp(a.TargetPos.x, _min.x, _max.x);
                    float clampedZ = Mathf.Clamp(a.TargetPos.z, _min.y, _max.y);
                    if (!Mathf.Approximately(clampedX, a.TargetPos.x) || !Mathf.Approximately(clampedZ, a.TargetPos.z))
                        a.TargetPos = new Vector3(clampedX, 0f, clampedZ);
                }
            }
        }

    }
}
