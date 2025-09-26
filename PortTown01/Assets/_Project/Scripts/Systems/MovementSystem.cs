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

        public void Tick(World world, int _, float dt)
        {
            foreach (var a in world.Agents)
            {
                var to = a.TargetPos - a.Pos;
                float dist = to.magnitude;
                if (dist < 0.25f)
                {
                    // Pick a new random target within bounds (determinism: use UnityEngine.Random with a fixed seed at start)
                    a.TargetPos = new Vector3(
                        Random.Range(_min.x, _max.x),
                        0f,
                        Random.Range(_min.y, _max.y)
                    );
                    continue;
                }

                var dir = to / (dist + 1e-6f);
                float step = a.SpeedMps * dt;
                if (step > dist) step = dist;
                a.Pos += dir * step;
            }
        }
    }
}
