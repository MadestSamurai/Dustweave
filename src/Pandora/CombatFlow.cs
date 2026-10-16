#nullable disable
using System;
using System.Collections.Generic;
namespace Dustweave.Pandora
{
    // A detached copy of native flow data. The four-neighbour distance field and
    // eight-neighbour tie order match the client; no native dictionaries are mutated.
    public sealed class CombatFlow
    {
        public double CellSize, RefreshAfter, RefreshSeconds;
        public Point Origin;
        public int MinX, MinZ, Width, Height;
        public bool OpenArena;
        public int InitialGoal = -1;
        public int[] Obstacles = new int[0];
        public Point[] Initial = new Point[0];
        readonly Dictionary<int, int[]> cache = new Dictionary<int, int[]>();
        int Index(Point p, bool target)
        {
            if (target) p = CombatSimulation.Sub(p, Origin);
            int x = (int)Math.Floor(p.X / CellSize) - MinX, z = (int)Math.Floor(p.Z / CellSize) - MinZ;
            return x < 0 || z < 0 || x >= Width || z >= Height ? -1 : z * Width + x;
        }
        public bool Valid { get { return CellSize > 0 && Width > 0 && Height > 0 && Width * Height <= 65536 &&
            (OpenArena && InitialGoal >= 0 && InitialGoal < Width * Height || Obstacles.Length == Width * Height && Initial.Length == Obstacles.Length); } }
        public Point Direction(Point enemy, Point target, bool initial)
        {
            int at = Index(enemy, false); if (at < 0) return new Point();
            if (OpenArena)
            {
                int to = initial ? InitialGoal : Index(target,true); if (to < 0) to = InitialGoal;
                int dx=Math.Sign(to % Width-at % Width),dz=Math.Sign(to / Width-at / Width);
                return dx!=0 && dz!=0 ? new Point(dx*.7071067811865476,dz*.7071067811865476) : new Point(dx,dz);
            }
            if (initial) return Initial[at];
            int goal = Index(target, true); if (goal < 0 || Obstacles[goal] == int.MaxValue) return Initial[at];
            int[] distances;
            if (!cache.TryGetValue(goal, out distances))
            {
                // Bound memory for callers retaining a frame across many experiments.
                if (cache.Count >= 128) cache.Clear();
                distances = new int[Width * Height]; for (int i = 0; i < distances.Length; i++) distances[i] = -1;
                var queue = new int[distances.Length]; int head = 0, tail = 0; queue[tail++] = goal; distances[goal] = 0;
                while (head < tail)
                {
                    int cell = queue[head++], x = cell % Width, z = cell / Width;
                    if (x + 1 < Width) Visit(cell + 1, distances[cell] + 1, distances, queue, ref tail);
                    if (z > 0) Visit(cell - Width, distances[cell] + 1, distances, queue, ref tail);
                    if (x > 0) Visit(cell - 1, distances[cell] + 1, distances, queue, ref tail);
                    if (z + 1 < Height) Visit(cell + Width, distances[cell] + 1, distances, queue, ref tail);
                }
                cache.Add(goal, distances);
            }
            if (distances[at] == 0) return new Point();
            int px = at % Width, pz = at / Width, best = int.MaxValue; Point direction = new Point();
            // E, SE, S, SW, W, NW, N, NE: strict '<' retains native ties.
            for (int i = 0; i < 8; i++)
            {
                int dx = DX[i], dz = DZ[i], x = px + dx, z = pz + dz;
                if (x < 0 || z < 0 || x >= Width || z >= Height) continue;
                int value = distances[z * Width + x]; if (value < 0 || value >= best) continue;
                best = value; direction = new Point(dx, dz);
            }
            return CombatSimulation.Unit(direction);
        }
        static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 }, DZ = { 0, -1, -1, -1, 0, 1, 1, 1 };
        void Visit(int at, int distance, int[] distances, int[] queue, ref int tail)
        { if (distances[at] >= 0 || Obstacles[at] == int.MaxValue) return; distances[at] = distance; queue[tail++] = at; }
    }
}
