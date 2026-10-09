using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Dustweave.Mansion
{
    public sealed class MazeGraph
    {
        public readonly float[] X, Z;
        public readonly int[][] Edges;
        public readonly int Count;
        readonly short[] distance;
        public MazeGraph(float[] x, float[] z, int[][] edges)
        {
            X = x;
            Z = z;
            Edges = edges;
            Count = x.Length;
            if (Count == 0 || Count > 2048 || z.Length != Count || edges.Length != Count)
                throw new ArgumentException("Invalid maze size");
            distance = new short[Count * Count];
            var q = new int[Count];
            for (int start = 0; start < Count; start++)
            {
                int offset = start * Count;
                for (int n = 0; n < Count; n++)
                    distance[offset + n] = short.MaxValue;
                int head = 0, tail = 0;
                q[tail++] = start;
                distance[offset + start] = 0;
                while (head < tail)
                {
                    int n = q[head++];
                    foreach (int next in Edges[n])
                    {
                        if (next < 0 || next >= Count)
                            throw new ArgumentException("Invalid maze edge");
                        if (distance[offset + next] != short.MaxValue)
                            continue;
                        distance[offset + next] = (short)(distance[offset + n] + 1);
                        q[tail++] = next;
                    }
                }
            }
        }

        public int Distance(int a, int b)
        {
            return a < 0 || b < 0 ? short.MaxValue : distance[a * Count + b];
        }

        public int Nearest(float x, float z)
        {
            int best = 0;
            float d = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                float dx = X[i] - x, dz = Z[i] - z, dd = dx * dx + dz * dz;
                if (dd < d)
                {
                    d = dd;
                    best = i;
                }
            }

            return best;
        }

        public void MovingEdge(float x, float z, float vx, float vz, out int from, out int next)
        {
            int center = Nearest(x, z);
            from = center;
            next = -1;
            float projection = (X[center] - x) * vx + (Z[center] - z) * vz;
            if (projection > .05f)
            {
                // The nearest center is still ahead. Reach that junction before choosing
                // another edge; otherwise the prediction skips an entire native turn.
                float alignment = -.1f;
                int behind = -1;
                foreach (int n in Edges[center])
                {
                    float dx = X[n] - X[center], dz = Z[n] - Z[center];
                    float dot = (dx * vx + dz * vz) / (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dot < alignment)
                    {
                        alignment = dot;
                        behind = n;
                    }
                }

                if (behind >= 0)
                {
                    from = behind;
                    next = center;
                    return;
                }
            }

            float best = .1f;
            foreach (int n in Edges[center])
            {
                float dx = X[n] - X[center], dz = Z[n] - Z[center];
                float dot = (dx * vx + dz * vz) / (float)Math.Sqrt(dx * dx + dz * dz);
                if (dot > best)
                {
                    best = dot;
                    next = n;
                }
            }
        }

        public int Next(int from, int to)
        {
            int best = from, d = Distance(from, to);
            foreach (int n in Edges[from])
                if (Distance(n, to) < d)
                {
                    best = n;
                    d = Distance(n, to);
                }

            return best;
        }
    }

    public struct Threat
    {
        public float X, Z, Speed, Radius, StateRemaining, ChaseDuration, StalkingDistance;
        public int Node, Next, Destination, Kind, State;
        public bool Bait;
        public float[]? NativeX, NativeZ;
        public int NativeCursor;
        public float NativeSeconds;
    }

    public sealed class PlannerOptions
    {
        // Build26's 83-chain run used 3 / 110 / 0 before nearby-pickup detours
        // were introduced. Keep this goal profile fixed when normal tuning changes.
        public static PlannerOptions ForGoal(int targetChain, PlannerOptions normal = null)
        {
            return RunGoal.Normalize(targetChain) == 80
                ? new PlannerOptions(3f, 110, 0, false)
                : normal ?? new PlannerOptions();
        }
        public readonly float CollisionHorizon;
        public readonly double GuideWeight;
        public readonly float WaitSeconds;
        public readonly bool CollectNearbyItems;
        public PlannerOptions(float collisionHorizon = 3f, double guideWeight = 110, float waitSeconds = 0, bool collectNearbyItems = true)
        {
            CollisionHorizon = Math.Max(.75f, Math.Min(3, collisionHorizon));
            GuideWeight = Math.Max(30, Math.Min(250, guideWeight));
            WaitSeconds = Math.Max(0, Math.Min(.4f, waitSeconds));
            CollectNearbyItems = collectNearbyItems;
        }
    }

    public static class PickupPolicy
    {
        // Use a conservative round-trip charge. Only short, profitable detours are
        // candidates; the collision planner still has the final choice of edge.
        public static double Benefit(string kind, int distance, float tileSeconds, int nearbyCandy, int remainingCandy, float duration, float speedPercent)
        {
            if (distance < 0 || distance > 3 || tileSeconds <= 0 || remainingCandy < 6)
                return 0;
            double saved = kind == "SpeedUp" ? Math.Min(duration, remainingCandy * tileSeconds) * speedPercent / 100.0 : kind == "Magnet" && nearbyCandy >= 6 ? (nearbyCandy - 2) * tileSeconds * .5 : 0;
            return Math.Max(0, saved - distance * tileSeconds * 2 - .2);
        }
    }

    public sealed class PlanResult
    {
        public RouteGuide.Evidence? Guide;
        public int Next = -1, GuideNext = -1;
        public double Score, Milliseconds, GuideMilliseconds;
        public float Wait;
        public int Expanded;
        public bool Unsafe;
    }

    public sealed class Planner
    {
        sealed class Path
        {
            public int Node, First, Length;
            public float X, Z, Time, FirstWait;
            public Heading Heading;
            public double Score, Rank;
            public Threat[] Threats = Array.Empty<Threat>();
            public readonly int[] Visited = new int[34];
        }

        readonly PlannerOptions options;
        readonly MazeGraph graph;
        readonly RouteGuide guide;
        readonly float[] visits;
        float lastTime;
        int lastNode = -1, previousNode = -1;
        public Planner(MazeGraph graph, PlannerOptions? options = null)
        {
            this.options = options ?? new PlannerOptions();
            this.graph = graph;
            guide = new RouteGuide(graph);
            visits = new float[graph.Count];
        }

        public void InvalidateRoute()
        {
            guide.Invalidate();
        }

        public PlanResult Choose(float x, float z, float speed, Threat[] enemies, bool[] coins, int exit, float now, bool invincible, float yaw = float.NaN, float turnSeconds = Motion.DefaultTurnSeconds, bool preserveChain = false)
        {
            var watch = Stopwatch.StartNew();
            var result = new PlanResult();
            speed = Math.Max(0.1f, speed);
            int start = graph.Nearest(x, z);
            if (start == exit)
            {
                result.Next = exit;
                return result;
            }

            if (start != lastNode)
            {
                previousNode = lastNode;
                lastNode = start;
            }

            float decay = (float)Math.Exp(-Math.Max(0, now - lastTime) / 12);
            lastTime = now;
            for (int n = 0; n < visits.Length; n++)
                visits[n] *= decay;
            visits[start] += 1;
            int preferred = exit < 0 ? guide.Next(start, coins, speed, now, preserveChain) : -1;
            result.Guide = guide.LastSearch;
            result.GuideNext = preferred;
            result.GuideMilliseconds = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var beam = new List<Path>
            {
                new Path
                {
                    Node = start,
                    First = -1,
                    X = x,
                    Z = z,
                    Heading = Heading.At(yaw),
                    Threats = enemies,
                    Length = 1
                }
            };
            beam[0].Visited[0] = start;
            // A brief yield can be faster than circling the maze around a crossing
            // pursuer. Simulate the stationary interval too; it must itself be safe.
            for (float delay = .2f; delay <= options.WaitSeconds + .001f; delay += .2f)
            {
                var waitThreats = (Threat[])enemies.Clone();
                var waitHeading = Heading.At(yaw);
                double danger = Advance(waitThreats, x, z, x, z, start, start, delay, invincible, 0, ref waitHeading, turnSeconds);
                if (danger >= 500)
                    continue;
                var waitPath = new Path
                {
                    Node = start,
                    First = -1,
                    X = x,
                    Z = z,
                    Heading = waitHeading,
                    Threats = waitThreats,
                    Length = 1,
                    Time = delay,
                    FirstWait = delay,
                    Score = -delay * 20 - danger
                };
                waitPath.Visited[0] = start;
                beam.Add(waitPath);
            }

            float terminalWait = 0, fallbackWait = 0;
            double terminalBest = double.NegativeInfinity;
            int terminalNext = -1;
            double best = double.NegativeInfinity;
            int expanded = 0;
            double fallback = double.NegativeInfinity;
            int fallbackNext = -1;
            for (int depth = 0; depth < (exit >= 0 ? 30 : 14); depth++)
            {
                var nextBeam = new List<Path>();
                double depthBest = double.NegativeInfinity;
                int depthNext = -1;
                float depthWait = 0;
                foreach (var p in beam)
                    foreach (int n in graph.Edges[p.Node])
                    {
                        if (p.Length > 1 && p.Visited[p.Length - 2] == n && graph.Edges[p.Node].Length > 1)
                            continue;
                        float dx = graph.X[n] - p.X, dz = graph.Z[n] - p.Z;
                        float duration = (float)Math.Sqrt(dx * dx + dz * dz) / speed;
                        if (duration < 0.01f)
                            duration = 0.01f;
                        var q = new Path
                        {
                            Node = n,
                            First = p.First < 0 ? n : p.First,
                            X = graph.X[n],
                            Z = graph.Z[n],
                            Length = p.Length + 1,
                            Time = p.Time + duration,
                            FirstWait = p.FirstWait,
                            Heading = p.Heading,
                            Score = p.Score,
                            Threats = (Threat[])p.Threats.Clone()
                        };
                        Array.Copy(p.Visited, q.Visited, p.Length);
                        q.Visited[p.Length] = n;
                        double danger = Advance(q.Threats, p.X, p.Z, q.X, q.Z, p.Node, n, duration, invincible && p.Time < .1f, p.Time, ref q.Heading, turnSeconds);
                        expanded++;
                        bool revisited = false;
                        for (int i = 0; i < p.Length; i++)
                            if (p.Visited[i] == n)
                                revisited = true;
                        double reward = exit < 0 && coins[n] && !revisited ? 12 : 0;
                        if (n == exit && danger < 500)
                        {
                            double terminalValue = 10000 - q.Time * 10 + p.Score - danger;
                            if (terminalValue > terminalBest)
                            {
                                terminalBest = terminalValue;
                                terminalNext = q.First;
                                terminalWait = q.FirstWait;
                            }

                            continue;
                        }

                        q.Score += reward + (depth == 0 && n == preferred ? options.GuideWeight : 0) - 0.9 * duration - visits[n] * 0.5 - (revisited ? 5 : 0) - (depth == 0 && n == previousNode ? 7 : 0) - danger;
                        int nearest = exit >= 0 ? graph.Distance(n, exit) : short.MaxValue;
                        if (exit < 0)
                            for (int i = 0; i < coins.Length; i++)
                                if (coins[i])
                                {
                                    bool consumed = false;
                                    for (int j = 0; j < q.Length; j++)
                                        if (q.Visited[j] == i)
                                            consumed = true;
                                    if (!consumed)
                                        nearest = Math.Min(nearest, graph.Distance(n, i));
                                }

                        if (nearest == short.MaxValue)
                            nearest = 0;
                        double value = q.Score - nearest * (exit >= 0 ? 3.0 : 2.0) + Math.Min(4, graph.Edges[n].Length) * 0.15;
                        q.Rank = value;
                        if (danger < 500)
                        {
                            nextBeam.Add(q);
                            if (value > depthBest)
                            {
                                depthBest = value;
                                depthNext = q.First;
                                depthWait = q.FirstWait;
                            }
                        }

                        if (depth == 0 && value > fallback)
                        {
                            fallback = value;
                            fallbackNext = q.First;
                            fallbackWait = q.FirstWait;
                        }
                    }

                if (nextBeam.Count == 0)
                    break;
                best = depthBest;
                result.Next = depthNext;
                result.Score = best;
                result.Wait = depthWait;
                nextBeam.Sort((a, b) => b.Rank.CompareTo(a.Rank));
                if (nextBeam.Count > 48)
                    nextBeam.RemoveRange(48, nextBeam.Count - 48);
                beam = nextBeam;
                if (watch.Elapsed.TotalMilliseconds > 5)
                    break;
            }

            if (terminalNext >= 0)
            {
                result.Next = terminalNext;
                result.Score = terminalBest;
                result.Wait = terminalWait;
            }

            if (result.Next < 0)
            {
                result.Next = fallbackNext;
                result.Score = fallback;
                result.Wait = fallbackWait;
                result.Unsafe = true;
            }

            if (preferred >= 0 && result.Next >= 0 && result.Next != preferred)
                guide.Invalidate();
            result.Expanded = expanded;
            result.Milliseconds = watch.Elapsed.TotalMilliseconds;
            return result;
        }

        public int PredictTarget(int playerNode, float yaw)
        {
            float fx = (float)Math.Sin(yaw * Math.PI / 180), fz = (float)Math.Cos(yaw * Math.PI / 180);
            if (Math.Abs(fx) > Math.Abs(fz))
            {
                fx = fx >= 0 ? 1 : -1;
                fz = 0;
            }
            else
            {
                fz = fz >= 0 ? 1 : -1;
                fx = 0;
            }

            int current = playerNode;
            // Native Predict only projects from a straight corridor. At a junction it
            // falls back to the player's current position, not one tile beyond it.
            for (int k = 0; k < graph.Count; k++)
            {
                if (graph.Edges[current].Length != 2)
                    break;
                int forward = -1;
                bool behind = false;
                foreach (int n in graph.Edges[current])
                {
                    float dx = graph.X[n] - graph.X[current], dz = graph.Z[n] - graph.Z[current];
                    float dot = (dx * fx + dz * fz) / (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dot > .99f)
                        forward = n;
                    else if (dot < -.99f)
                        behind = true;
                }

                if (forward < 0 || !behind)
                    break;
                current = forward;
            }

            return current;
        }

        double Advance(Threat[] enemies, float sx, float sz, float tx, float tz, int from, int to, float duration, bool invincible, float elapsed, ref Heading heading, float turnSeconds)
        {
            double penalty = 0;
            int steps = Math.Max(1, (int)Math.Ceiling(duration / 0.05f));
            float dt = duration / steps;
            float facing = Motion.Angle(tx - sx, tz - sz);
            if (float.IsNaN(heading.Value))
                heading = Heading.At(facing);
            if (Math.Abs(tx - sx) + Math.Abs(tz - sz) > .001f)
                heading.Aim(facing, turnSeconds);
            for (int step = 1; step <= steps; step++)
            {
                float px = sx + (tx - sx) * step / steps, pz = sz + (tz - sz) * step / steps;
                heading.Advance(dt);
                float yaw = heading.Value;
                for (int i = 0; i < enemies.Length; i++)
                {
                    var e = enemies[i];
                    float oldX = e.X, oldZ = e.Z;
                    e.StateRemaining -= dt;
                    bool stopped = e.State == 2 && e.StateRemaining > 0;
                    if (e.State == 0 && (e.StateRemaining <= 0 || e.Node == e.Destination))
                    {
                        e.State = 1;
                        e.StateRemaining = e.ChaseDuration > 0 ? e.ChaseDuration : 10;
                    }

                    // The next random scatter target is not knowable. Beyond that transition,
                    // retain a conservative pursuit estimate instead of predicting a false stop.
                    bool chasing = e.State == 1 && e.StateRemaining > 0;
                    int playerNode = graph.Nearest(px, pz), goal = to;
                    if (e.State == 0 && e.StateRemaining > 0 && e.Destination >= 0)
                        goal = e.Destination;
                    else if (chasing && e.Bait)
                        goal = e.Destination;
                    else if (chasing && e.Kind == 2 && graph.Distance(e.Node, playerNode) < (e.StalkingDistance > 0 ? e.StalkingDistance : 5))
                        stopped = true;
                    else if (chasing && e.Kind == 1)
                        goal = PredictTarget(playerNode, yaw);
                    float nativeStep = Math.Min(dt, Math.Max(0, e.NativeSeconds));
                    e.NativeSeconds = Math.Max(0, e.NativeSeconds - dt);
                    float nativeBudget = stopped ? 0 : e.Speed * nativeStep;
                    if (e.NativeX != null && nativeStep > 0)
                    {
                        while (nativeBudget > 0 && e.NativeCursor < e.NativeX.Length)
                        {
                            float nx = e.NativeX[e.NativeCursor] - e.X, nz = e.NativeZ[e.NativeCursor] - e.Z;
                            float nd = (float)Math.Sqrt(nx * nx + nz * nz);
                            if (nd < .001f)
                            {
                                e.NativeCursor++;
                                continue;
                            }

                            float travel = Math.Min(nativeBudget, nd);
                            e.X += nx / nd * travel;
                            e.Z += nz / nd * travel;
                            nativeBudget -= travel;
                            if (travel >= nd - .001f)
                                e.NativeCursor++;
                        }

                        if (e.NativeCursor < e.NativeX.Length)
                            graph.MovingEdge(e.X, e.Z, e.NativeX[e.NativeCursor] - e.X, e.NativeZ[e.NativeCursor] - e.Z, out e.Node, out e.Next);
                        else
                        {
                            e.Node = graph.Nearest(e.X, e.Z);
                            e.Next = -1;
                        }
                    }
                    else
                        nativeStep = 0;
                    float budget = stopped ? 0 : e.Speed * (dt - nativeStep);
                    for (int moves = 0; moves < 4 && budget > 0; moves++)
                    {
                        if (e.Next < 0 || e.Next == e.Node)
                            e.Next = graph.Next(e.Node, goal);
                        float dx = graph.X[e.Next] - e.X, dz = graph.Z[e.Next] - e.Z, dist = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (dist < 0.001f)
                        {
                            e.Node = e.Next;
                            e.Next = graph.Next(e.Node, goal);
                            if (e.Next == e.Node)
                                break;
                            continue;
                        }

                        float travel = Math.Min(budget, dist);
                        e.X += dx / dist * travel;
                        e.Z += dz / dist * travel;
                        budget -= travel;
                        if (travel >= dist - 0.001f)
                        {
                            e.Node = e.Next;
                            e.Next = -1;
                        }
                    }

                    // Swept relative distance catches swaps along an edge, not just equal endpoints.
                    float p0x = sx + (tx - sx) * (step - 1) / steps, p0z = sz + (tz - sz) * (step - 1) / steps;
                    float rx = p0x - oldX, rz = p0z - oldZ, vx = (px - e.X) - rx, vz = (pz - e.Z) - rz;
                    float t = Math.Max(0, Math.Min(1, -(rx * vx + rz * vz) / Math.Max(0.0001f, vx * vx + vz * vz)));
                    float close = (float)Math.Sqrt((rx + vx * t) * (rx + vx * t) + (rz + vz * t) * (rz + vz * t));
                    if (!invincible)
                    {
                        float margin = close - e.Radius - 0.2f;
                        if (margin < 0)
                            penalty += elapsed + step * dt <= options.CollisionHorizon ? 1500 : 30 * dt * Math.Exp(-(elapsed + step * dt - 1) / 3);
                        else if (margin < 1.4f)
                            penalty += (1.4f - margin) * 3 * dt;
                    }

                    enemies[i] = e;
                }
            }

            return penalty;
        }
    }
}

