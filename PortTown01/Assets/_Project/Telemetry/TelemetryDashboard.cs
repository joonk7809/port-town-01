using System.Linq;
using System.Text;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.TelemetryUI
{
    // Quick IMGUI dashboard for live introspection.
    // Toggle with F1. Scroll sections; filter agents; adjust rows.
    public class TelemetryDashboard : MonoBehaviour
    {
        public SimRunner runner;                 // assign or auto-find
        public KeyCode toggleKey = KeyCode.F1;
        public bool visible = true;

        public bool showHeader = true;
        public bool showAgents = true;
        public bool showWorld  = true;
        public bool showOrders = true;

        public int maxAgentRows = 20;
        public int maxOrderRows = 12;

        public string agentFilter = "";          // e.g., "logger", "id:3"
        Vector2 _scrollAgents, _scrollWorld, _scrollOrders;

        // match DayPlan constants for readable time-of-day
        const float DAY_SECONDS = 600f;
        const float START_HOUR  = 9f;

        void Awake()
        {
            if (runner == null) runner = FindObjectOfType<SimRunner>();
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible) return;
            if (runner == null || runner.WorldRef == null)
            {
                GUILayout.BeginArea(new Rect(10,10, 420, 80), GUI.skin.box);
                GUILayout.Label("TelemetryDashboard: no SimRunner/World found.");
                GUILayout.EndArea();
                return;
            }

            var w = runner.WorldRef;

            // layout container
            const float pad = 8f;
            float width = Mathf.Min(900f, Screen.width - 20f);
            float height = Mathf.Min(Screen.height - 20f, 680f);
            GUILayout.BeginArea(new Rect(10, 10, width, height), GUI.skin.box);

            // header
            if (showHeader)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>Telemetry Dashboard</b>", Rich());
                GUILayout.FlexibleSpace();

                GUILayout.Label("Rows:");
                maxAgentRows = Mathf.Clamp(IntField(maxAgentRows, 40, 40), 5, 200);
                GUILayout.Space(pad);

                GUILayout.Label("Orders:");
                maxOrderRows = Mathf.Clamp(IntField(maxOrderRows, 40, 40), 5, 200);
                GUILayout.Space(pad);

                showAgents = GUILayout.Toggle(showAgents, "Agents");
                showWorld  = GUILayout.Toggle(showWorld,  "World");
                showOrders = GUILayout.Toggle(showOrders, "OrderBook");
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                var (hh, mm) = TimeOfDay(w.SimTime);
                GUILayout.Label($"tick={w.Tick}  sim={w.SimTime:F1}s  tod={hh:D2}:{mm:D2}");
                GUILayout.FlexibleSpace();
                agentFilter = GUILayout.TextField(agentFilter, GUILayout.Width(240));
                GUILayout.Label("  (filter: e.g. 'logger', 'id:3', 'vendor')");
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }

            // WORLD
            if (showWorld)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<b>World</b>", Rich());

                // Resource nodes
                _scrollWorld = GUILayout.BeginScrollView(_scrollWorld, GUILayout.Height(120));
                if (w.ResourceNodes.Count == 0) GUILayout.Label("No ResourceNodes");
                foreach (var n in w.ResourceNodes)
                    GUILayout.Label($"Node#{n.Id} {n.Type}  pos=({n.Pos.x:F1},{n.Pos.z:F1})  stock={n.Stock}/{n.MaxStock}");
                // Buildings (storage)
                if (w.Buildings.Count == 0) GUILayout.Label("No Buildings");
                foreach (var b in w.Buildings)
                {
                    var inv = DictString(b.Storage);
                    GUILayout.Label($"Bldg#{b.Id} {b.Type}  pos=({b.Pos.x:F1},{b.Pos.z:F1})  store={inv}");
                }

                // Vendor / Boss quick glance
                var vendor = w.Agents.FirstOrDefault(a => a.IsVendor);
                int vendorEsc = (vendor != null)
                    ? w.FoodBook.Asks.Where(o => o.AgentId == vendor.Id && o.Item == ItemType.Food).Sum(o => o.EscrowItems)
                    : 0;
                GUILayout.Label($"Vendor: inv={vendor?.Carry.Get(ItemType.Food) ?? 0} escrow={vendorEsc} coins={vendor?.Coins ?? 0}");

                var boss = w.Agents.Where(a => !a.IsVendor).OrderByDescending(a => a.Coins).FirstOrDefault();
                if (boss != null) GUILayout.Label($"Boss: id={boss.Id} coins={boss.Coins}");

                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                GUILayout.Space(pad);

                // Mill inventories (incl. crates)
                var mill = runner.WorldRef.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
                int millLogs   = mill?.Storage.Get(ItemType.Log)   ?? 0;
                int millPlanks = mill?.Storage.Get(ItemType.Plank) ?? 0;
                int millCrates = mill?.Storage.Get(ItemType.Crate) ?? 0;
                GUILayout.Label($"Mill: logs={millLogs} planks={millPlanks} crates={millCrates}");

                // Dock & buyer
                var dock = runner.WorldRef.Buildings.FirstOrDefault(b => b.Type == BuildingType.Dock);
                var dockBuyer = runner.WorldRef.Agents
                    .Where(a => !a.IsVendor && a.SpeedMps == 0f)
                    .OrderBy(a => dock == null ? 9999f : Vector3.Distance(a.Pos, dock.Pos))
                    .FirstOrDefault();
                if (dockBuyer != null)
                    GUILayout.Label($"DockBuyer: id={dockBuyer.Id} coins={dockBuyer.Coins}");
                    GUILayout.Label($"Ledger: cratesSold={runner.WorldRef.CratesSold}  revDock={runner.WorldRef.RevenueDock}  wagesHaul={runner.WorldRef.WagesHaul}  profit={runner.WorldRef.RevenueDock - runner.WorldRef.WagesHaul}");
;

            }

            // ORDER BOOK
            if (showOrders)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<b>Order Book — Food</b>", Rich());

                var bids = w.FoodBook.Bids.Where(o => o.Qty > 0)
                    .OrderByDescending(o => o.UnitPrice).ThenBy(o => o.PostTick).Take(maxOrderRows).ToList();
                var asks = w.FoodBook.Asks.Where(o => o.Qty > 0)
                    .OrderBy(o => o.UnitPrice).ThenBy(o => o.PostTick).Take(maxOrderRows).ToList();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"bids={w.FoodBook.Bids.Count(o=>o.Qty>0)}  asks={w.FoodBook.Asks.Count(o=>o.Qty>0)}");
                int bestBid = bids.Count > 0 ? bids.Max(o=>o.UnitPrice) : 0;
                int bestAsk = asks.Count > 0 ? asks.Min(o=>o.UnitPrice) : 0;
                GUILayout.Label($"   bestBid={bestBid}  bestAsk={bestAsk}");
                GUILayout.EndHorizontal();

                _scrollOrders = GUILayout.BeginScrollView(_scrollOrders, GUILayout.Height(140));
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label("<b>Top Bids</b>", Rich());
                foreach (var b in bids)
                    GUILayout.Label($"#{b.Id} ag={b.AgentId} qty={b.Qty}@{b.UnitPrice} escC={b.EscrowCoins} pt={b.PostTick}");
                GUILayout.EndVertical();

                GUILayout.Space(16);

                GUILayout.BeginVertical();
                GUILayout.Label("<b>Top Asks</b>", Rich());
                foreach (var a in asks)
                    GUILayout.Label($"#{a.Id} ag={a.AgentId} qty={a.Qty}@{a.UnitPrice} escI={a.EscrowItems} pt={a.PostTick}");
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.EndScrollView();

                GUILayout.EndVertical();
                GUILayout.Space(pad);
            }

            // AGENTS
            if (showAgents)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<b>Agents</b>", Rich());
                GUILayout.Label("Columns: id role phase coins food rest carry pos→target");

                _scrollAgents = GUILayout.BeginScrollView(_scrollAgents, GUILayout.Height(280));
                var list = w.Agents.AsEnumerable();

                // filter
                if (!string.IsNullOrWhiteSpace(agentFilter))
                {
                    string f = agentFilter.ToLowerInvariant();
                    list = list.Where(a =>
                        (f.Contains("vendor") && a.IsVendor) ||
                        (f.Contains("logger") && a.Role == JobRole.Logger) ||
                        (f.StartsWith("id:") && int.TryParse(f.Substring(3), out var id) && a.Id == id) ||
                        a.Id.ToString().Contains(f)
                    );
                }

                int rows = 0;
                foreach (var a in list)
                {
                    if (rows++ >= maxAgentRows) break;
                    var sb = new StringBuilder(256);
                    sb.Append($"#{a.Id,2} ");
                    sb.Append($"{a.Role} ");
                    sb.Append($"{a.Phase} ");
                    sb.Append($"¢{a.Coins,4} ");
                    sb.Append($"F:{a.Food,5:0.0} ");
                    sb.Append($"R:{a.Rest,5:0.0} ");
                    sb.Append("carry:["); sb.Append(DictString(a.Carry)); sb.Append("] ");
                    float dist = Vector3.Distance(a.Pos, a.TargetPos);
                    sb.Append($"pos({a.Pos.x:0.0},{a.Pos.z:0.0})->{dist:0.0}m");
                    sb.Append($" Kg:{a.Carry.Kg:0.0} ");
                    sb.Append($" Cr:{a.Carry.Get(ItemType.Crate)} ");
                    GUILayout.Label(sb.ToString());
                    
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            GUILayout.EndArea();
        }

        // ---------- helpers ----------
        static GUIStyle Rich()
        {
            var s = new GUIStyle(GUI.skin.label);
            s.richText = true;
            return s;
        }

        static (int hh, int mm) TimeOfDay(float simTime)
        {
            float daySec = (float)((simTime + (START_HOUR/24f)*DAY_SECONDS) % DAY_SECONDS);
            int hh = Mathf.FloorToInt(daySec / DAY_SECONDS * 24f);
            int mm = Mathf.FloorToInt(((daySec / DAY_SECONDS * 24f) - hh) * 60f);
            return (hh, mm);
        }

        static string DictString(Inventory inv)
        {
            if (inv == null || inv.Items.Count == 0) return "-";
            return string.Join(",", inv.Items.Select(kv => $"{kv.Key}:{kv.Value}"));
        }
        static string DictString(PortTown01.Core.Building b) => DictString(b?.Storage);

        // Simple IMGUI int input helper so we can type numbers into the HUD.
        static int IntField(int current, float width = 60f, float _unused = 0f)
        {
            var s = GUILayout.TextField(current.ToString(), GUILayout.Width(width));
            if (int.TryParse(s, out var parsed)) return parsed;
            return current; 
        }

    }
}
