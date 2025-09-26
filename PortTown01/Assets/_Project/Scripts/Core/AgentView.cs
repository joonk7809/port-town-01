using UnityEngine;

namespace PortTown01.Core
{
    // Renders Agent.Pos from sim into the scene (no interpolation yet).
    public class AgentView : MonoBehaviour
    {
        private Agent _agent;

        public void Bind(Agent a) => _agent = a;

        void LateUpdate()
        {
            if (_agent != null)
                transform.position = _agent.Pos;
        }
    }
}
