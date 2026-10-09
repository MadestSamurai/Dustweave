using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Dustweave.Mansion
{
    // A complete route covers the remaining native candy positions. Local collision
    // prediction can reject its next edge without blocking the Unity frame thread.
    public sealed class RouteGuide
    {
        public sealed class Evidence
        {
            public int Steps, Candidates;
            public double Milliseconds;
        }

        public Evidence? LastSearch { get; private set; }

        readonly MazeGraph graph;
        int[] route = new int[0];
        int cursor;
        float computed = -100;
        Task<int[]>? pending;
        bool discardPending;
        public RouteGuide(MazeGraph graph)
        {
            this.graph = graph;
        }

        public void Invalidate()
        {
            route = new int[0];
            cursor = 0;
            computed = -100;
            // An in-flight search describes the old candy set. Let it finish in the
            // background, then discard its result before scheduling a fresh route.
            discardPending = pending != null;
        }

        public int Next(int start, bool[] coins, float speed, float now, bool preserveChain)
        {
            if (pending != null && pending.IsCompleted)
            {
                if (pending.Status == TaskStatus.RanToCompletion)
                {
                    if (!discardPending)
                    {
                        route = pending.Result;
                        cursor = 0;
                    }
                }
                else
                {
                    var ignored = pending.Exception;
                }

                pending = null;
                discardPending = false;
            }

            if (route.Length > 0)
            {
                for (int i = cursor; i < Math.Min(route.Length, cursor + 4); i++)
                {
                    if (route[i] != start)
                        continue;
                    cursor = i;
                    // Native pickups and evasive moves can clear an old detour. Keep
                    // its remaining targets, but do not walk an already empty loop.
                    for (int next = cursor + 1; next < route.Length; next++)
                        if (coins[route[next]])
                            return graph.Next(start, route[next]);
                    break;
                }
            }

            if (pending != null || now - computed < .4f)
                return -1;
            computed = now;
            var copy = (bool[])coins.Clone();
            pending = Task.Run(() => Search(start, copy, speed, preserveChain));
            return -1;
        }

        public int[] Search(int start, bool[] coins, float speed, bool preserveChain)
        {
            var timer = Stopwatch.StartNew();
            int[] best = new[]
            {
                start
            };
            double bestCost = double.PositiveInfinity;
            // Different equal-distance choices produce different tours. Improve each
            // complete tour instead of truncating a node-by-node beam at a fixed depth.
            int attempts = 0;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                attempts++;
                var random = new Random(7919 + attempt);
                var remaining = (bool[])coins.Clone();
                remaining[start] = false;
                var order = new List<int>
                {
                    start
                };
                int current = start;
                while (true)
                {
                    int next = -1, distance = short.MaxValue, ties = 0;
                    for (int n = 0; n < graph.Count; n++)
                    {
                        if (!remaining[n])
                            continue;
                        int d = graph.Distance(current, n);
                        if (d < distance)
                        {
                            distance = d;
                            next = n;
                            ties = 1;
                        }
                        else if (d == distance && d < short.MaxValue && random.Next(++ties) == 0)
                            next = n;
                    }

                    if (next < 0)
                        break;
                    while (current != next)
                    {
                        current = graph.Next(current, next);
                        if (remaining[current])
                        {
                            remaining[current] = false;
                            order.Add(current);
                        }
                    }
                }

                // Open-path 2-opt: the start stays fixed and the final endpoint is free.
                // Every required node remains in the order, including incidental pickups.
                for (int pass = 0; pass < 50; pass++)
                {
                    int improvement = 0, first = -1, last = -1;
                    for (int i = 1; i < order.Count - 1; i++)
                    {
                        for (int j = i + 1; j < order.Count; j++)
                        {
                            int before = graph.Distance(order[i - 1], order[i]);
                            int after = graph.Distance(order[i - 1], order[j]);
                            if (j + 1 < order.Count)
                            {
                                before += graph.Distance(order[j], order[j + 1]);
                                after += graph.Distance(order[i], order[j + 1]);
                            }

                            if (after - before < improvement)
                            {
                                improvement = after - before;
                                first = i;
                                last = j;
                            }
                        }
                    }

                    if (first < 0)
                        break;
                    order.Reverse(first, last - first + 1);
                    if (timer.Elapsed.TotalMilliseconds >= 30)
                        break;
                }

                var candidate = new List<int>
                {
                    start
                };
                current = start;
                remaining = (bool[])coins.Clone();
                remaining[start] = false;
                int chain = 0, maxChain = 0, gap = 0;
                int gapLimit = Math.Max(1, (int)Math.Floor(speed * .9 / 2));
                for (int i = 1; i < order.Count; i++)
                {
                    int destination = order[i];
                    if (!remaining[destination])
                        continue;
                    while (current != destination)
                    {
                        current = graph.Next(current, destination);
                        candidate.Add(current);
                        if (remaining[current])
                        {
                            chain = gap >= gapLimit ? 1 : chain + 1;
                            maxChain = Math.Max(maxChain, chain);
                            gap = 0;
                            remaining[current] = false;
                        }
                        else
                            gap++;
                    }
                }

                // Route length is primary. Chain only breaks near-equal route choices.
                double cost = candidate.Count - (preserveChain ? Math.Min(1.5, maxChain * .015) : 0);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = candidate.ToArray();
                }

                if (timer.Elapsed.TotalMilliseconds >= 30)
                    break;
            }

            LastSearch = new Evidence
            {
                Steps = best.Length - 1,
                Candidates = attempts,
                Milliseconds = timer.Elapsed.TotalMilliseconds
            };
            return best;
        }
    }
}
