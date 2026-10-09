using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using gamfs.PackMan;
using BD2.LocalIpc;

namespace Dustweave.Mansion.Runtime
{
    public static class Loader
    {
        static Handoff handoff;
        static MainThread frame;
        static object engine;
        static bool resolver;
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant", "mansion");
        const string Entries = "state.json|control.json|command.json|runtime.json|error.json|receipt.json";
        public static void Load()
        {
            lock (typeof(Loader))
            {
                if (handoff == null)
                    handoff = new Handoff(typeof(Loader).Assembly.FullName, "mansion-runaway", "mansion-runaway", false, Start, Pause, Busy, Stop, Status);
                if (!handoff.IsActive && !handoff.Pending)
                    RuntimeFiles.Start(Root, Build.Fingerprint, Entries);
                handoff.Request(DateTime.UtcNow);
                if (frame == null)
                    frame = new MainThread(() =>
                    {
                        handoff.Tick(DateTime.UtcNow);
                        return handoff.Pending;
                    }, handoff.Fail);
                frame.Schedule();
            }
        }

        static void Start()
        {
            RuntimeFiles.Start(Root, Build.Fingerprint, Entries);
            if (!resolver)
            {
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                resolver = true;
            }

            engine = typeof(Loader).Assembly.GetType("Dustweave.Mansion.Runtime.Engine", true).GetMethod("Create").Invoke(null, null);
        }

        static object Invoke(string method)
        {
            return engine.GetType().GetMethod(method).Invoke(engine, null);
        }

        static void Pause()
        {
            RuntimeFiles.Revoke();
            if (engine != null)
                Invoke("PrepareHandoff");
        }

        static string Busy()
        {
            return engine == null ? "" : (string)Invoke("HandoffBusy");
        }

        static void Stop()
        {
            if (engine != null)
                Invoke("Stop");
            engine = null;
            if (resolver)
            {
                AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
                resolver = false;
            }
        }

        public static void Unload()
        {
            MainThread.Drain(() =>
            {
                if (handoff != null)
                    handoff.Unload();
            }, Status);
        }

        static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            if (new AssemblyName(e.Name).Name != "0Harmony")
                return null;
            using (var s = typeof(Loader).Assembly.GetManifestResourceStream("Mansion.Harmony.dll"))
            using (var m = new MemoryStream())
            {
                s.CopyTo(m);
                return Assembly.Load(m.ToArray());
            }
        }

        static void Status(string state, string error)
        {
            if (state == "active")
                RuntimeFiles.Activate();
            var text = "{\"State\":\"" + state + "\",\"Error\":\"" + error.Replace("\\", "/").Replace("\"", "'").Replace("\n", " ").Replace("\r", " ") + "\"}";
            RuntimeFiles.Write(Path.Combine(Root, "runtime.json"), System.Text.Encoding.UTF8.GetBytes(text));
        }
    }

    public sealed partial class Engine
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static Engine current;
        readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();
        PackManManager manager;
        PackManCharPlayer player;
        MazeGridSetup grid;
        object ruler;
        bool running, retired, itemWarning;
        long expires;
        float nextScan, nextSnapshot;
        string reason = "ready", lastCommand = "";
        object result;
        float resultAt, nextItem;
        int itemsUsed;
        Heading heading;
        bool headingReady;
        float frameSeconds = 1f / 60f;
        Vector3 desired;
        int target = -1;
        int gridId;
        int lastFrame = -1;
        public static object Create()
        {
            // Dynamically loaded copies must not become Unity MonoScripts: Unity caches their type identity.
            var created = new Engine();
            try
            {
                created.Awake();
                Canvas.willRenderCanvases += created.Frame;
                created.Snapshot();
                return created;
            }
            catch
            {
                created.Stop();
                throw;
            }
        }

        void Frame()
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            Update();
        }
        void Awake()
        {
            current = this;
            network.Start(typeof(BDNetwork.NetworkManager), "dustweave.mansion.network");
            var harmony = new Harmony("dustweave.mansion");
            harmony.Patch(typeof(PackManCharPlayer).GetMethod("OnUpdate"), prefix: new HarmonyMethod(typeof(Engine).GetMethod("Move", All)));
            harmony.Patch(typeof(MansionRunawayResultUI).GetMethod("Set"), postfix: new HarmonyMethod(typeof(Engine).GetMethod("OnResult", All)));
        }

        static void OnResult(object[] __args)
        {
            if (current != null)
            {
                current.result = new
                {
                    Clear = __args[0],
                    Mode = __args[1].ToString(),
                    Score = __args[4],
                    MaxChain = __args[5],
                    At = DateTime.UtcNow
                };
                current.resultAt = Time.unscaledTime;
                current.desired = Vector3.zero;
            }
        }

        static void Move(PackManCharPlayer __instance, float __0)
        {
            if (current != null && current.running && !current.retired && current.player == __instance)
            {
                var direction = current.desired;
                if (direction.sqrMagnitude > 0.0001f && current.target >= 0 && current.centers != null)
                {
                    var p = __instance.transform.position;
                    var offset = current.centers[current.target] - p;
                    offset.y = 0;
                    float speed = Convert.ToSingle(current.Field(__instance, "_moveSpeed")) * Convert.ToSingle(current.Bound(__instance, "speedMultiplier"));
                    direction = offset.normalized * Mathf.Min(1, offset.magnitude / Mathf.Max(.001f, speed * __0));
                }

                if (direction.sqrMagnitude > 0.0001f)
                {
                    current.frameSeconds = Mathf.Lerp(current.frameSeconds, Mathf.Clamp(__0, .008f, .04f), .15f);
                    if (!current.headingReady || Mathf.Abs(Motion.Delta(current.heading.Value, __instance.transform.eulerAngles.y)) > 10)
                    {
                        current.heading = Heading.At(__instance.transform.eulerAngles.y);
                        current.headingReady = true;
                    }

                    current.heading.Aim(Motion.Angle(direction.x, direction.z), Motion.TurnDuration(current.frameSeconds));
                    current.heading.Advance(__0);
                    __instance.RotateYaw(Motion.Delta(__instance.transform.eulerAngles.y, current.heading.Value));
                }

                var v = __instance.transform.InverseTransformDirection(direction);
                __instance.SetMoveInput(new Vector2(v.x, v.z));
            }
        }

        public void Pause()
        {
            running = false;
            desired = Vector3.zero;
            if (player != null)
                player.SetMoveInput(Vector2.zero);
            if (!finished && !faulted)
                reason = "paused";
        }

        public void Stop()
        {
            Pause();
            retired = true;
            network.Dispose();
            new Harmony("dustweave.mansion").UnpatchAll("dustweave.mansion");
            if (current == this)
                current = null;
            Canvas.willRenderCanvases -= Frame;
        }

        static IEnumerable<FieldInfo> Fields(Type t)
        {
            for (; t != null; t = t.BaseType)
                foreach (var f in t.GetFields(All | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        object Field(object o, string name)
        {
            if (o == null)
                return null;
            var key = o.GetType().FullName + "|" + name;
            FieldInfo f;
            if (!fields.TryGetValue(key, out f))
            {
                f = Fields(o.GetType()).Single(x => x.Name == name);
                fields[key] = f;
            }

            return f.GetValue(o);
        }

        object Bound(object o, string name)
        {
            return Field(o, Bindings.Names[name]);
        }

        T Typed<T>(object o)
        {
            if (o == null)
                return default(T);
            return (T)Fields(o.GetType()).Single(f => f.FieldType == typeof(T)).GetValue(o);
        }

        static JToken Scalar(object o)
        {
            if (o == null)
                return JValue.CreateNull();
            if (o is string || o.GetType().IsPrimitive || o.GetType().IsEnum || o is decimal)
                return JToken.FromObject(o.GetType().IsEnum ? o.ToString() : o);
            return null;
        }

        object Values(object o)
        {
            var d = new Dictionary<string, object>();
            if (o == null)
                return d;
            foreach (var f in Fields(o.GetType()))
            {
                var v = f.GetValue(o);
                if (v == null)
                    continue;
                if (v.GetType().IsPrimitive || v is string || v.GetType().IsEnum)
                    d[f.Name] = v.GetType().IsEnum ? v.ToString() : v;
            }

            return d;
        }

        static object Pos(Vector3 v)
        {
            return new
            {
                X = v.x,
                Z = v.z
            };
        }

        static JToken Read(string name)
        {
            var bytes = RuntimeFiles.Take(Path.Combine(Loader.Root, name));
            return bytes == null ? null : JToken.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        }

        static void Write(string name, object value)
        {
            RuntimeFiles.Write(Path.Combine(Loader.Root, name), System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value)));
        }

        void Update()
        {
            if (retired)
                return;
            try
            {
                var control = Read("control.json");
                if (control != null && !handingOff)
                    Control(control);
                if (running && DateTime.UtcNow.Ticks > expires)
                {
                    Pause();
                    reason = "heartbeat-expired";
                }

                if (Time.unscaledTime >= nextScan)
                {
                    nextScan = Time.unscaledTime + 0.5f;
                    if (manager == null)
                        manager = UnityEngine.Object.FindObjectOfType<PackManManager>();
                    if (manager != null)
                    {
                        player = Typed<PackManCharPlayer>(manager);
                        grid = Typed<MazeGridSetup>(manager);
                        ruler = Bound(manager, "ruler");
                    }
                }

                var command = Read("command.json");
                if (command != null && !handingOff)
                    Command(command);
                if (running && autoFlow)
                    Flow();
                if (running && player != null && grid != null && State == "Playing" && Convert.ToString(Bound(manager, "managerState")) == "InGame")
                    Step();
                else
                    desired = Vector3.zero;
                if (Time.unscaledTime >= nextSnapshot)
                {
                    nextSnapshot = Time.unscaledTime + 0.35f;
                    Snapshot();
                }
            }
            catch (Exception e)
            {
                Pause();
                faulted = true;
                reason = "runtime-error";
                Write("error.json", new { At = DateTime.UtcNow, Error = e.ToString() });
            }
        }

        string State
        {
            get
            {
                return ruler == null ? "waiting" : Convert.ToString(Bound(ruler, "playState"));
            }
        }

        void Command(JToken c)
        {
            string id = (string)c["Id"];
            if (string.IsNullOrEmpty(id) || id == lastCommand)
                return;
            lastCommand = id;
            long commandExpiry = (long? )c["Expires"] ?? 0;
            if (commandExpiry < DateTime.UtcNow.Ticks || commandExpiry > DateTime.UtcNow.AddSeconds(15).Ticks)
                throw new InvalidOperationException("expired-command");
            if (!RuntimeFiles.TryClaim(id, commandExpiry))
                return;
            string kind = (string)c["Kind"];
            if (kind == "inspect")
            {
                Snapshot();
            }
            else if (kind == "click")
            {
                string type = (string)c["Type"], field = (string)c["Field"];
                if (!type.StartsWith("MansionRunaway", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsupported UI");
                var u = UnityEngine.Object.FindObjectsOfType<UIBase>().Single(x => x.GetType().Name == type && x.gameObject.activeInHierarchy);
                var go = Field(u, field) as GameObject;
                if (go == null || !go.activeInHierarchy)
                    throw new InvalidOperationException("Button unavailable");
                u.OnClickUI(go);
            }
            else if (kind == "mode")
            {
                var u = UnityEngine.Object.FindObjectOfType<MansionRunawayModeSelectUI>();
                if (u == null || !u.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Mode selection is not visible");
                var items = (IEnumerable)Field(u, "_modeItems");
                var item = items.Cast<object>().Single(x => Convert.ToString(x.GetType().GetProperty("ModeType").GetValue(x, null)) == (string)c["Mode"]);
                var go = Fields(item.GetType()).Where(f => f.FieldType == typeof(GameObject)).Select(f => (GameObject)f.GetValue(item)).First(x => x != null && x.activeInHierarchy);
                u.OnClickUI(go);
            }
            else
                throw new InvalidOperationException("Unknown command");
            Write("receipt.json", new { Id = id, Kind = kind, At = DateTime.UtcNow, Status = "dispatched" });
        }

        object[] Uis()
        {
            return UnityEngine.Object.FindObjectsOfType<UIBase>().Where(x => x.gameObject.activeInHierarchy).Select(x => new { Type = x.GetType().Name, Id = x.GetInstanceID(), Texts = x.GetComponentsInChildren<TMPro.TMP_Text>().Where(t => t.gameObject.activeInHierarchy).Select(t => t.text).Take(35).ToArray(), Links = Fields(x.GetType()).Where(f => f.FieldType == typeof(GameObject)).Select(f => new { Field = f.Name, Go = f.GetValue(x) as GameObject }).Where(v => v.Go != null && v.Go.activeInHierarchy).Select(v => new { v.Field, Name = v.Go.name }).ToArray() }).Cast<object>().ToArray();
        }

        void Snapshot()
        {
            object board = null;
            if (grid != null && player != null)
            {
                int w, h;
                grid.TryGetMapSize(out w, out h);
                var tiles = new List<object>();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        MazeGridSetup.MazeGridTile tile;
                        if (grid.TryGetTile(new Vector2Int(x, y), out tile) && tile.walkable)
                            tiles.Add(new { X = x, Y = y, World = Pos(tile.position), Mask = tile.connectionMask, Type = tile.type.ToString() });
                    }

                var enemies = Chasers().Where(x => x != null && x.isActiveAndEnabled).Select(x =>
                {
                    var a = x.GetComponent<NavMeshAgent>();
                    var b = Bound(x, "behavior");
                    return new
                    {
                        Id = x.GetInstanceID(),
                        Position = Pos(x.transform.position),
                        Speed = a.speed,
                        Velocity = Pos(a.velocity),
                        Target = Pos(a.destination),
                        Stopped = a.isStopped,
                        Bait = Bound(x, "chaserBait"),
                        Kind = Bound(x, "chaserKind").ToString(),
                        State = Bound(b, "behaviorState").ToString(),
                        Elapsed = Bound(b, "behaviorElapsed"),
                        UpdateIn = Math.Max(0, Convert.ToSingle(Field(x, "_destinationUpdateInterval")) - Convert.ToSingle(Bound(x, "destinationElapsed"))),
                        Catch = Field(x, "_catchDistance"),
                        Corners = a.path.corners.Select(Pos).ToArray()
                    };
                }).ToArray();
                var coins = Coins().Where(x => x != null && x.isActiveAndEnabled && !Convert.ToBoolean(Bound(x, "coinCollected"))).Select(x => new { Id = x.GetInstanceID(), Position = Pos(x.transform.position) }).ToArray();
                var exits = UnityEngine.Object.FindObjectsOfType<PackManObjectExit>().Where(x => x.isActiveAndEnabled && Convert.ToBoolean(Bound(x, "exitActive"))).Select(x => Pos(x.transform.position)).ToArray();
                board = new
                {
                    Grid = grid.GetInstanceID(),
                    Width = w,
                    Height = h,
                    Tiles = tiles,
                    Player = Pos(player.transform.position),
                    Health = Bound(player, "health"),
                    Speed = Convert.ToSingle(Field(player, "_moveSpeed")) * Convert.ToSingle(Bound(player, "speedMultiplier")),
                    Invincible = Bound(player, "invincible"),
                    Enemies = enemies,
                    Coins = coins,
                    Exits = exits,
                    Ruler = Values(ruler),
                    Manager = Values(manager),
                    Items = UnityEngine.Object.FindObjectsOfType<PackManObjectItem>().Where(x => x.isActiveAndEnabled).Select(x => new { Position = Pos(x.transform.position), Kind = Bound(x, "itemKind"), Collected = Bound(x, "itemCollected"), State = Values(x) }).ToArray()
                };
            }

            Write("state.json", new { At = DateTime.UtcNow, Pid = System.Diagnostics.Process.GetCurrentProcess().Id, Running = running, Reason = reason, Finished = finished, Faulted = faulted, RunId = runId, Attempts = attempts, TargetChain = targetChain, State = State, Target = target, Board = board, Objective = Objective(), Camera = new { Yaw = player == null ? 0 : player.transform.eulerAngles.y, Target = heading.Target, TurnSeconds = Motion.TurnDuration(frameSeconds) }, Plan = plan, PlannerSettings = plannerOptions, PlannerProfile = targetChain == 80 ? "chain80-build26" : "balanced", Result = result, ItemsUsed = itemsUsed, ItemGoal = itemGoal, ItemWarning = itemWarning, Inventory = Inventory().Select(v => new { v.Index, v.Kind, v.Active }).ToArray(), Mode = manager == null ? "" : Convert.ToString(Bound(Bound(manager, "mode"), "modeType")), UIs = CachedUis() });
        }

        IEnumerable<PackManCharChaser> Chasers()
        {
            return ruler == null ? new PackManCharChaser[0] : (IEnumerable<PackManCharChaser>)Fields(ruler.GetType()).Single(f => f.FieldType == typeof(List<PackManCharChaser>)).GetValue(ruler);
        }

        IEnumerable<PackManObjectCoin> OrdinaryCoins()
        {
            return ruler == null ? new PackManObjectCoin[0] : ((Dictionary<Vector2Int, PackManObjectCoin>)Bound(ruler, "ordinaryCoins")).Values;
        }

        IEnumerable<PackManObjectCoin> Coins()
        {
            if (ruler == null)
                return new PackManObjectCoin[0];
            return Fields(ruler.GetType()).Where(f => f.FieldType == typeof(Dictionary<Vector2Int, PackManObjectCoin>)).SelectMany(f => ((Dictionary<Vector2Int, PackManObjectCoin>)f.GetValue(ruler)).Values);
        }

        sealed class Slot
        {
            public int Index;
            public string Kind;
            public bool Active;
        }

        IEnumerable<Slot> Inventory()
        {
            var itemRuler = Bound(ruler, "items");
            if (itemRuler == null)
                yield break;
            var slots = Bound(itemRuler, "slots") as Array;
            if (slots == null)
                yield break;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots.GetValue(i);
                if (slot != null)
                    yield return new Slot
                    {
                        Index = i,
                        Kind = Convert.ToString(Bound(slot, "slotKind")),
                        Active = (bool)slot.GetType().GetMethod(Bindings.Names["slotActive"], All).Invoke(slot, null)
                    };
            }
        }

        int itemGoal = -1;
        object ItemRule(string kind)
        {
            var rules = Bound(Bound(ruler, "items"), "itemRules");
            var method = rules.GetType().GetMethods(All).Single(m => m.Name == Bindings.Names["itemLookup"] && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
            return method.Invoke(rules, new[] { Enum.Parse(method.GetParameters()[0].ParameterType, kind) });
        }

        int MagnetCount(Vector2Int center, int radius)
        {
            int count = 0;
            foreach (var c in OrdinaryCoins())
            {
                Vector2Int tile;
                if (c != null && !Convert.ToBoolean(Bound(c, "coinCollected")) && grid.TryWorldToTile(c.transform.position, out tile) && Math.Abs(tile.x - center.x) <= radius && Math.Abs(tile.y - center.y) <= radius)
                    count++;
            }

            return count;
        }

        void UseItems()
        {
            if (Time.unscaledTime < nextItem)
                return;
            nextItem = Time.unscaledTime + .3f;
            itemGoal = -1;
            var p = player.transform.position;
            float nearest = 100;
            bool bait = false;
            foreach (var c in Chasers())
                if (c != null && c.isActiveAndEnabled)
                {
                    bait |= Convert.ToBoolean(Bound(c, "chaserBait"));
                    if (Convert.ToString(Bound(Bound(c, "behavior"), "behaviorState")) != "Suspicious")
                        nearest = Math.Min(nearest, Vector3.Distance(p, c.transform.position));
                }

            var inventory = Inventory().ToArray();
            int magnetHere = 0;
            if (inventory.Any(v => v.Kind == "Magnet" && !v.Active))
            {
                var data = ItemRule("Magnet");
                int radius = Convert.ToInt32(Bound(data, "itemRange")) / 2;
                Vector2Int tile;
                if (grid.TryWorldToTile(p, out tile))
                    magnetHere = MagnetCount(tile, radius);
                double best = magnetHere;
                int start = graph.Nearest(p.x, p.z);
                for (int n = 0; n < graph.Count; n++)
                {
                    int distance = graph.Distance(start, n);
                    if (distance > 6)
                        continue;
                    if (!grid.TryWorldToTile(centers[n], out tile))
                        continue;
                    int count = MagnetCount(tile, radius);
                    double value = count - distance * .75;
                    if (value > best + 1)
                    {
                        best = value;
                        itemGoal = n;
                    }
                }
            }

            foreach (var slot in inventory)
            {
                if (slot.Active)
                    continue;
                bool use = slot.Kind == "NightVision" || (slot.Kind == "SpeedUp" && !inventory.Any(v => v.Kind == "SpeedUp" && v.Active)) || (slot.Kind == "Magnet" && magnetHere > 0 && itemGoal < 0) || ((slot.Kind == "Diffuser" || slot.Kind == "Figure") && !bait && nearest < 7);
                if (use && manager.TryUseItem(slot.Index))
                {
                    itemsUsed++;
                    nextItem = Time.unscaledTime + .5f;
                    target = -1;
                    if (slot.Kind == "Magnet")
                    {
                        itemGoal = -1;
                        planner.InvalidateRoute();
                    }

                    return;
                }
            }

            ChoosePickup(inventory);
        }

        void ChoosePickup(Slot[] inventory)
        {
            if (!plannerOptions.CollectNearbyItems || itemGoal >= 0)
                return;
            var slots = (Array)Bound(Bound(ruler, "items"), "slots");
            if (inventory.Length >= slots.Length)
                return;
            var remaining = OrdinaryCoins().Where(c => c != null && c.isActiveAndEnabled && !Convert.ToBoolean(Bound(c, "coinCollected"))).ToArray();
            if (remaining.Length < 6)
                return;
            var p = player.transform.position;
            int start = graph.Nearest(p.x, p.z);
            float speed = Convert.ToSingle(Field(player, "_moveSpeed")) * Convert.ToSingle(Bound(player, "speedMultiplier"));
            if (graph.Edges[start].Length == 0)
                return;
            float tileSeconds = Vector3.Distance(centers[start], centers[graph.Edges[start][0]]) / Math.Max(.1f, speed);
            double best = 0;
            foreach (var item in UnityEngine.Object.FindObjectsOfType<PackManObjectItem>())
            {
                if (!item.isActiveAndEnabled || Convert.ToBoolean(Bound(item, "itemCollected")))
                    continue;
                string kind = Convert.ToString(Bound(item, "itemKind"));
                if ((kind != "SpeedUp" && kind != "Magnet") || inventory.Any(s => s.Kind == kind))
                    continue;
                int node = graph.Nearest(item.transform.position.x, item.transform.position.z);
                int distance = graph.Distance(start, node);
                if (distance > 3)
                    continue;
                var rule = ItemRule(kind);
                Vector2Int tile;
                int nearby = grid.TryWorldToTile(item.transform.position, out tile) ? MagnetCount(tile, Convert.ToInt32(Bound(rule, "itemRange")) / 2) : 0;
                double benefit = PickupPolicy.Benefit(kind, distance, tileSeconds, nearby, remaining.Length, Convert.ToSingle(Bound(rule, "itemDuration")), Convert.ToSingle(Bound(rule, "itemValue")));
                if (benefit > best)
                {
                    best = benefit;
                    itemGoal = node;
                }
            }
        }

        PlannerOptions plannerOptions = new PlannerOptions();
        MazeGraph graph;
        Planner planner;
        Vector3[] centers;
        float lastPlan, waitUntil;
        PlanResult plan;
        bool headingExit;
        void BuildGraph()
        {
            int w, h;
            grid.TryGetMapSize(out w, out h);
            var index = new Dictionary<int, int>();
            var positions = new List<Vector3>();
            var masks = new List<byte>();
            var keys = new List<int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    MazeGridSetup.MazeGridTile t;
                    if (grid.TryGetTile(new Vector2Int(x, y), out t) && t.walkable)
                    {
                        index[y * w + x] = positions.Count;
                        keys.Add(y * w + x);
                        positions.Add(t.position);
                        masks.Add(t.connectionMask);
                    }
                }

            var edges = new int[positions.Count][];
            var offsets = new[]
            {
                w,
                1,
                -w,
                -1
            };
            for (int n = 0; n < edges.Length; n++)
            {
                var list = new List<int>();
                for (int d = 0; d < 4; d++)
                {
                    int next;
                    if ((masks[n] & (1 << d)) != 0 && index.TryGetValue(keys[n] + offsets[d], out next))
                        list.Add(next);
                }

                edges[n] = list.ToArray();
            }

            centers = positions.ToArray();
            graph = new MazeGraph(centers.Select(p => p.x).ToArray(), centers.Select(p => p.z).ToArray(), edges);
            planner = new Planner(graph, plannerOptions);
            gridId = grid.GetInstanceID();
            target = -1;
            waitUntil = 0;
        }

        void Step()
        {
            if (graph == null || gridId != grid.GetInstanceID())
            {
                BuildGraph();
                headingReady = false;
            }

            if (!itemWarning)
                try
                {
                    UseItems();
                }
                catch (Exception error)
                {
                    itemWarning = true;
                    itemGoal = -1;
                    target = -1;
                    Write("error.json", new { At = DateTime.UtcNow, Severity = "warning", Phase = "items", Error = error.ToString(), MovementContinues = true });
                }

            if (Time.unscaledTime < waitUntil)
            {
                desired = Vector3.zero;
                reason = "avoiding-enemy";
                return;
            }

            var pos = player.transform.position;
            bool arrived = target < 0 || Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(centers[target].x, centers[target].z)) < 0.10f;
            if (arrived || Time.unscaledTime - lastPlan > 0.5f)
            {
                var coins = new bool[graph.Count];
                foreach (var c in OrdinaryCoins())
                    if (c != null && c.isActiveAndEnabled && !Convert.ToBoolean(Bound(c, "coinCollected")))
                        coins[graph.Nearest(c.transform.position.x, c.transform.position.z)] = true;
                int exit = -1;
                var door = Typed<PackManObjectExit>(ruler);
                if (door != null && door.isActiveAndEnabled && Convert.ToBoolean(Bound(door, "exitActive")))
                    exit = graph.Nearest(door.transform.position.x, door.transform.position.z);
                headingExit = exit >= 0;
                var threats = new List<Threat>();
                foreach (var c in Chasers())
                {
                    if (c == null || !c.isActiveAndEnabled)
                        continue;
                    var a = c.GetComponent<NavMeshAgent>();
                    if (!a.isOnNavMesh)
                        continue;
                    var b = Bound(c, "behavior");
                    string state = Bound(b, "behaviorState").ToString();
                    string kind = Bound(c, "chaserKind").ToString();
                    float duration = Convert.ToSingle(Field(c, state == "Scatter" ? "_scatterDuration" : state == "Chase" ? "_chaseDuration" : "_suspiciousDuration"));
                    var p = c.transform.position;
                    int node, next;
                    var velocity = a.hasPath && !a.pathPending ? a.steeringTarget - p : a.velocity;
                    var nativePath = a.path.corners;
                    graph.MovingEdge(p.x, p.z, velocity.x, velocity.z, out node, out next);
                    threats.Add(new Threat { NativeX = nativePath.Length > 1 ? nativePath.Select(v => v.x).ToArray() : null, NativeZ = nativePath.Length > 1 ? nativePath.Select(v => v.z).ToArray() : null, NativeCursor = 1, NativeSeconds = state == "Scatter" ? Math.Max(0, duration - Convert.ToSingle(Bound(b, "behaviorElapsed"))) : Math.Max(0, Convert.ToSingle(Field(c, "_destinationUpdateInterval")) - Convert.ToSingle(Bound(c, "destinationElapsed"))), X = p.x, Z = p.z, Node = node, Next = next, Destination = graph.Nearest(a.destination.x, a.destination.z), Speed = a.speed, Radius = Convert.ToSingle(Field(c, "_catchDistance")), ChaseDuration = Convert.ToSingle(Field(c, "_chaseDuration")), StalkingDistance = Convert.ToSingle(Field(c, "_stalkingDistance")), Bait = Convert.ToBoolean(Bound(c, "chaserBait")), Kind = kind == "Predict" ? 1 : kind == "Stalking" ? 2 : 0, State = state == "Scatter" ? 0 : state == "Chase" ? 1 : 2, StateRemaining = Math.Max(0, duration - Convert.ToSingle(Bound(b, "behaviorElapsed"))) });
                }

                float speed = Convert.ToSingle(Field(player, "_moveSpeed")) * Convert.ToSingle(Bound(player, "speedMultiplier"));
                plan = planner.Choose(pos.x, pos.z, speed, threats.ToArray(), coins, exit >= 0 ? exit : itemGoal, Time.unscaledTime, Convert.ToBoolean(Bound(player, "invincible")), player.transform.eulerAngles.y, Motion.TurnDuration(frameSeconds), Convert.ToString(Bound(Bound(manager, "mode"), "modeType")) == "Challenge");
                target = plan.Next;
                lastPlan = Time.unscaledTime;
                if (plan.Wait > 0)
                {
                    target = -1;
                    waitUntil = Time.unscaledTime + plan.Wait;
                    desired = Vector3.zero;
                    reason = "avoiding-enemy";
                    return;
                }
            }

            if (target < 0)
            {
                desired = Vector3.zero;
                reason = "no-route";
                return;
            }

            desired = centers[target] - pos;
            desired.y = 0;
            desired = desired.normalized;
            reason = headingExit ? "heading-exit" : "collecting";
        }
    }
}


