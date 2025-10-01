using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    /// <summary>
    /// Lightweight HUD overlay for money-integrity parity with Audit/CSV.
    /// Toggle with F1. Updates once per second to match Audit cadence.
    /// </summary>
    public sealed class TelemetryDashboard : MonoBehaviour
    {
        [Header("HUD")]
        public KeyCode toggleKey = KeyCode.F1;
        public bool show = true;

        [Header("Style")]
        public int fontSize = 14;
        public float panelWidth = 460f;

        // refs
        private SimRunner _runner;
        private World _world;

        // 1 Hz sampler
        private float _accum;
        private bool _hasBaseline;

        // snapshots for residual calc
        private long _prevTotalMoney = long.MinValue;
        private long _prevInflow;
        private long _prevOutflow;

        // per-pool snapshots for ΔA/ΔE/ΔC
        private int _prevAgents;
        private int _prevEscrow;
        private int _prevCity;

        // current display values
        private int  _agentsCoins, _escrowCoins, _cityCoins;
        private long _totalMoney, _dIn, _dOut, _residual;
        private int  _dA, _dE, _dC;
        private int  _foodPrice, _cratePrice;

        private GUIStyle _label, _labelBad, _header;

        private void Awake()
        {
            _runner = FindObjectOfType<SimRunner>();
            if (_runner != null) _world = _runner.WorldRef;

            _label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, normal = { textColor = Color.white } };
            _labelBad = new GUIStyle(_label)      { normal = { textColor = new Color(1f, 0.4f, 0.4f) } };
            _header = new GUIStyle(_label)        { fontStyle = FontStyle.Bold, fontSize = fontSize + 1 };
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) show = !show;
            if (_world == null) return;

            _accum += Time.deltaTime;
            if (_accum < 1f) return;
            _accum -= 1f;

            // Pools (integer coins)
            _agentsCoins = _world.Agents.Sum(a => a.Coins);
            _escrowCoins = _world.FoodBook?.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins) ?? 0;
            _cityCoins   = _world.CityBudget;
            _totalMoney  = (long)_agentsCoins + _escrowCoins + _cityCoins;

            long inflow  = _world.CoinsExternalInflow;
            long outflow = _world.CoinsExternalOutflow;

            // Per-pool deltas
            _dA = (_prevAgents == int.MinValue) ? 0 : _agentsCoins - _prevAgents;
            _dE = (_prevEscrow == int.MinValue) ? 0 : _escrowCoins - _prevEscrow;
            _dC = (_prevCity   == int.MinValue) ? 0 : _cityCoins   - _prevCity;

            if (_prevTotalMoney == long.MinValue)
            {
                _prevTotalMoney = _totalMoney;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;
                _residual       = 0;
            }
            else
            {
                _dIn  = inflow  - _prevInflow;
                _dOut = outflow - _prevOutflow;

                long expectedNow = _prevTotalMoney + (_dIn - _dOut);
                _residual = _totalMoney - expectedNow;

                _prevTotalMoney = _totalMoney;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;
            }

            _prevAgents = _agentsCoins;
            _prevEscrow = _escrowCoins;
            _prevCity   = _cityCoins;

            _foodPrice  = Mathf.Max(1, _world.FoodPrice);
            _cratePrice = Mathf.Max(1, _world.CratePrice);
        }

        private void OnGUI()
        {
            if (!show || _world == null) return;

            float x = 10f, y = 10f, line = fontSize + 6f;
            Rect r = new Rect(x, y, panelWidth, 9999f);
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label($"Port Town Telemetry — t={_world.SimTime:F1}s", _header);

            // Money integrity
            GUILayout.Label("Money Integrity (per-second)", _header);
            GUILayout.Label($"Agents={_agentsCoins} | Escrow={_escrowCoins} | City={_cityCoins} | Total={_totalMoney}", _label);
            GUILayout.Label($"ΔA={_dA} | ΔE={_dE} | ΔC={_dC} | ΔIn={_dIn} | ΔOut={_dOut}", _label);
            var lbl = (_residual == 0) ? _label : _labelBad;
            GUILayout.Label($"Residual={_residual}", lbl);

            // Prices (context)
            GUILayout.Space(6f);
            GUILayout.Label($"Prices: Food={_foodPrice} | Crate={_cratePrice}", _label);

            GUILayout.EndArea();
        }
    }
}
