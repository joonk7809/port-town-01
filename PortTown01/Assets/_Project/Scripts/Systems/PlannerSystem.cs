using System.Linq;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Minimal planner: set a per-agent Intent; actual work stays in existing systems.
    public class PlannerSystem : ISimSystem
    {
        public string Name => "Planner";

        // Keep in sync with FoodTrade thresholds
        private const float CRIT_HUNGER = 28f; // break work only if below this

        public void Tick(World world, int _, float dt)
        {
            foreach (var a in world.Agents)
            {
                if (a.IsVendor) { a.Intent = AgentIntent.Leisure; continue; }

                // Sleep takes precedence
                if (a.Phase == DayPhase.Sleep)
                {
                    a.Intent = AgentIntent.Sleep;
                    a.AllowWander = false;
                    a.TargetPos = a.HomePos;
                    continue;
                }

                // Critical hunger overrides work/leisure
                if (a.Food < CRIT_HUNGER && a.Carry.Get(ItemType.Food) == 0)
                {
                    a.Intent = AgentIntent.Eat;
                    // (FoodTradeSystem will handle queueing & movement)
                    continue;
                }

                // Work during Work phase if you have a job
                if (a.Phase == DayPhase.Work && a.Role != JobRole.None)
                {
                    a.Intent = AgentIntent.Work;
                    continue;
                }

                // Otherwise: leisure (wander)
                a.Intent = AgentIntent.Leisure;
                a.AllowWander = true;
            }
        }
    }
}
