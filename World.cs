using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace McClone;

public sealed class World
{
    public readonly long Seed;
    public readonly TerrainGen Gen;
    public readonly ConcurrentDictionary<long, Chunk> Chunks = new();
    public long Time;
    public static int SharedEbo;
    public volatile bool Disposed;

    readonly ConcurrentQueue<Chunk> genDone = new();
    readonly ConcurrentQueue<MeshResult> meshDone = new();
    int genInFlight, meshInFlight;
    readonly HashSet<long> requested = new();
    readonly HashSet<(int, int, int)> dirty = new(), urgent = new();
    public readonly Dictionary<long, Dictionary<int, byte>> Edits = new();
    public int PendingMeshes => dirty.Count + meshInFlight;

    public static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;
    public static int FloorDiv16(double v) => (int)Math.Floor(v / 16.0);

    static readonly (int dx, int dz)[] Ring = BuildRing(26);
    static (int, int)[] BuildRing(int r)
    {
        var l = new List<(int dx, int dz)>();
        for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++) if (dx * dx + dz * dz <= r * r) l.Add((dx, dz));
        l.Sort((a, b) => (a.dx * a.dx + a.dz * a.dz).CompareTo(b.dx * b.dx + b.dz * b.dz));
        return l.ToArray();
    }

    public World(long seed) { Seed = seed; Gen = new TerrainGen(seed); }

    // ------------------------------------------------------------------ block access (main thread)
    Chunk cacheC; int cacheCX = int.MinValue, cacheCZ;
    Chunk GC(int cx, int cz)
    {
        if (cx == cacheCX && cz == cacheCZ) return cacheC;
        Chunks.TryGetValue(Key(cx, cz), out var c);
        cacheC = c; cacheCX = cx; cacheCZ = cz;
        return c;
    }
    public bool IsLoaded(int wx, int wz) => GC(wx >> 4, wz >> 4) != null;
    public byte GetBlock(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height) return Blk.Air;
        var c = GC(x >> 4, z >> 4);
        return c == null ? Blk.Air : c.Blocks[(x & 15) + (z & 15) * 16 + y * 256];
    }
    public bool SolidForPhysics(int x, int y, int z)
    {
        if (y < 0) return true;
        if (y >= Chunk.Height) return false;
        var c = GC(x >> 4, z >> 4);
        if (c == null) return true;
        return Blk.Solid[c.Blocks[(x & 15) + (z & 15) * 16 + y * 256]];
    }
    public int BlockLightAt(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height) return 0;
        var c = GC(x >> 4, z >> 4);
        return c == null ? 0 : c.Bl[(x & 15) + (z & 15) * 16 + y * 256];
    }
    public int SkyAt(int x, int y, int z)
    {
        if (y < 0) return 0; if (y >= Chunk.Height) return 15;
        var c = GC(x >> 4, z >> 4);
        return c == null ? 15 : c.Sky[(x & 15) + (z & 15) * 16 + y * 256];
    }

    // ------------------------------------------------------------------ edits & persistence
    void ApplyEdits(Chunk ch)
    {
        lock (Edits)
            if (Edits.TryGetValue(Key(ch.X, ch.Z), out var d))
                foreach (var kv in d) ch.Blocks[kv.Key] = kv.Value;
    }
    void RecordEdit(int cx, int cz, int idx, byte id)
    {
        lock (Edits)
        {
            long k = Key(cx, cz);
            if (!Edits.TryGetValue(k, out var d)) Edits[k] = d = new Dictionary<int, byte>();
            d[idx] = id;
        }
    }

    public void SetBlock(int x, int y, int z, byte id, bool urgentMesh = true)
    {
        if (y < 0 || y >= Chunk.Height) return;
        var ch = GC(x >> 4, z >> 4);
        if (ch == null) return;
        int i = (x & 15) + (z & 15) * 16 + y * 256;
        byte old = ch.Blocks[i];
        if (old == id) return;
        ch.Blocks[i] = id;
        RecordEdit(ch.X, ch.Z, i, id);
        int oldOp = Blk.Opacity[old], newOp = Blk.Opacity[id];

        // ---- sky light
        if (newOp > oldOp)
        {
            RemoveLight(x, y, z, ch.Sky[i], true);
        }
        else if (newOp < oldOp)
        {
            var q = new Queue<(int, int, int)>();
            bool sun = y + 1 >= Chunk.Height || (SkyAt(x, y + 1, z) == 15 && newOp == 0);
            if (sun) { ch.Sky[i] = 15; q.Enqueue((x, y, z)); }
            EnqueueNeighbours(q, x, y, z);
            SpreadLight(q, true);
        }

        // ---- block light (torches, fire)
        int oldBl = ch.Bl[i];
        bool wasOpaque = oldOp >= 15, nowOpaque = newOp >= 15;
        if (oldBl > 0 && (Blk.Emit[old] > 0 || nowOpaque)) RemoveLight(x, y, z, oldBl, false);
        if (Blk.Emit[id] > 0)
        {
            if (ch.Bl[i] < Blk.Emit[id]) ch.Bl[i] = Blk.Emit[id];
            var q = new Queue<(int, int, int)>();
            q.Enqueue((x, y, z));
            SpreadLight(q, false);
        }
        else if (wasOpaque && !nowOpaque)
        {
            var q = new Queue<(int, int, int)>();
            EnqueueNeighbours(q, x, y, z);
            SpreadLight(q, false);
        }
        MarkCell(x, y, z, urgentMesh);
        OnBlockChanged(x, y, z, old, id);
    }

    void EnqueueNeighbours(Queue<(int, int, int)> q, int x, int y, int z)
    {
        for (int d = 0; d < 6; d++)
        {
            int nx = x + Dx[d], ny = y + Dy[d], nz = z + Dz[d];
            if (ny < 0 || ny >= Chunk.Height) continue;
            if (GC(nx >> 4, nz >> 4) != null) q.Enqueue((nx, ny, nz));
        }
    }

    // ------------------------------------------------------------------ neighbour updates (support, etc.)
    public static void SupportOf(byte b, int x, int y, int z, out int sx, out int sy, out int sz)
    {
        sx = x; sy = y; sz = z;
        switch (b)
        {
            case Blk.TorchPX: sx = x + 1; break;
            case Blk.TorchNX: sx = x - 1; break;
            case Blk.TorchPZ: sz = z + 1; break;
            case Blk.TorchNZ: sz = z - 1; break;
            default: sy = y - 1; break;
        }
    }
    public static bool CanSupportTorch(byte b) => Blk.Opaque[b];

    void CheckSupport(int x, int y, int z)
    {
        byte b = GetBlock(x, y, z);
        if (b == Blk.Air) return;
        if (Blk.Cross[b]) { if (!Blk.Solid[GetBlock(x, y - 1, z)]) SetBlock(x, y, z, Blk.Air); }
        else if (Blk.TorchT[b])
        {
            SupportOf(b, x, y, z, out int sx, out int sy, out int sz);
            if (!CanSupportTorch(GetBlock(sx, sy, sz))) SetBlock(x, y, z, Blk.Air);
        }
    }

    void OnBlockChanged(int x, int y, int z, byte old, byte now)
    {
        for (int d = 0; d < 6; d++) CheckSupport(x + Dx[d], y + Dy[d], z + Dz[d]);
        if (Blk.WaterT[now]) Schedule(x, y, z, WaterDelay);
        for (int d = 0; d < 6; d++)
            if (Blk.WaterT[GetBlock(x + Dx[d], y + Dy[d], z + Dz[d])]) Schedule(x + Dx[d], y + Dy[d], z + Dz[d], WaterDelay);
    }

    // ------------------------------------------------------------------ scheduled block ticks (fluids, fire)
    public const int WaterDelay = 5;
    readonly PriorityQueue<(int, int, int), long> tickQ = new();
    readonly HashSet<(int, int, int)> tickSet = new();
    public void Schedule(int x, int y, int z, int delay)
    {
        var p = (x, y, z);
        if (tickSet.Add(p)) tickQ.Enqueue(p, Time + delay);
    }
    public int ScheduledCount => tickQ.Count;
    public void TickScheduled()
    {
        int n = 0;
        while (n < 700 && tickQ.TryPeek(out var p, out long due) && due <= Time)
        {
            tickQ.Dequeue(); tickSet.Remove(p); n++;
            byte b = GetBlock(p.Item1, p.Item2, p.Item3);
            if (Blk.WaterT[b]) UpdateWater(p.Item1, p.Item2, p.Item3);
        }
    }

    // ------------------------------------------------------------------ water
    static readonly int[] Hx = { 1, -1, 0, 0 }, Hz = { 0, 0, 1, -1 };

    static bool FlowReplaceable(byte b) => b == Blk.Air || Blk.Cross[b] || Blk.TorchT[b] || b == Blk.Fire;
    /// <summary>Can water of the given level move into this block?  level 8 = falling.</summary>
    static bool CanFlowInto(byte b, int level)
    {
        if (FlowReplaceable(b)) return true;
        if (Blk.WaterT[b])
        {
            int l = Blk.WaterLevel(b);
            if (level == 8) return l != 0 && l != 8;   // falling replaces thinner flowing water
            return l != 0 && l != 8 && l > level;      // sideways only replaces thinner flowing water
        }
        return false;
    }

    void FlowInto(int x, int y, int z, byte id)
    {
        SetBlock(x, y, z, id, false);
    }

    /// <summary>Minecraft's search for the nearest drop: returns the number of steps until a hole (or 1000).</summary>
    int FlowCost(int x, int y, int z, int depth, int from)
    {
        int best = 1000;
        for (int d = 0; d < 4; d++)
        {
            if (from >= 0 && d == (from ^ 1)) continue;
            int nx = x + Hx[d], nz = z + Hz[d];
            byte nb = GetBlock(nx, y, nz);
            if (Blk.Solid[nb] || (Blk.WaterT[nb] && Blk.WaterLevel(nb) == 0)) continue;
            if (!Blk.Solid[GetBlock(nx, y - 1, nz)]) return depth;
            if (depth < 4) best = Math.Min(best, FlowCost(nx, y, nz, depth + 1, d));
        }
        return best;
    }

    void UpdateWater(int x, int y, int z)
    {
        byte b = GetBlock(x, y, z);
        if (!Blk.WaterT[b]) return;
        int level = Blk.WaterLevel(b);
        if (level != 0)
        {
            int nl;
            if (Blk.WaterT[GetBlock(x, y + 1, z)]) nl = 8;
            else
            {
                int best = 99, sources = 0;
                for (int d = 0; d < 4; d++)
                {
                    byte nb = GetBlock(x + Hx[d], y, z + Hz[d]);
                    if (!Blk.WaterT[nb]) continue;
                    int l = Blk.WaterLevel(nb);
                    if (l == 0) sources++;
                    int cand;
                    if (l == 8) cand = Blk.Solid[GetBlock(x + Hx[d], y - 1, z + Hz[d])] ? 1 : 99;   // falling water only feeds sideways once it has landed
                    else cand = l + 1;
                    if (cand <= 7 && cand < best) best = cand;
                }
                byte below = GetBlock(x, y - 1, z);
                if (sources >= 2 && (Blk.Solid[below] || (Blk.WaterT[below] && Blk.WaterLevel(below) == 0))) best = 0;   // infinite water
                nl = best == 99 ? -1 : best;
            }
            if (nl < 0) { SetBlock(x, y, z, Blk.Air, false); return; }
            if (nl != level) { SetBlock(x, y, z, Blk.FlowId(nl), false); return; }
        }
        level = Blk.WaterLevel(GetBlock(x, y, z));
        byte under = GetBlock(x, y - 1, z);
        if (CanFlowInto(under, 8)) { FlowInto(x, y - 1, z, Blk.Falling); return; }
        if (level == 0 || Blk.Solid[under])
        {
            int next = (level == 8 || level == 0) ? 1 : level + 1;
            if (next > 7) return;
            // choose the direction(s) with the shortest path to a drop
            Span<int> cost = stackalloc int[4];
            int min = 2000;
            for (int d = 0; d < 4; d++)
            {
                cost[d] = 2000;
                int nx = x + Hx[d], nz = z + Hz[d];
                byte nb = GetBlock(nx, y, nz);
                if (Blk.Solid[nb] || (Blk.WaterT[nb] && Blk.WaterLevel(nb) == 0)) continue;
                cost[d] = !Blk.Solid[GetBlock(nx, y - 1, nz)] ? 0 : FlowCost(nx, y, nz, 1, d);
                if (cost[d] < min) min = cost[d];
            }
            for (int d = 0; d < 4; d++)
            {
                if (cost[d] != min) continue;
                int nx = x + Hx[d], nz = z + Hz[d];
                if (CanFlowInto(GetBlock(nx, y, nz), next)) FlowInto(nx, y, nz, Blk.FlowId(next));
            }
        }
    }

    // ------------------------------------------------------------------ lighting
    static readonly int[] Dx = { 1, -1, 0, 0, 0, 0 }, Dy = { 0, 0, 1, -1, 0, 0 }, Dz = { 0, 0, 0, 0, 1, -1 };

    void MarkCell(int wx, int y, int wz, bool urg)
    {
        int cx = wx >> 4, cz = wz >> 4, sy = y >> 4, lx = wx & 15, lz = wz & 15, ly = y & 15;
        for (int dx = lx == 0 ? -1 : 0; dx <= (lx == 15 ? 1 : 0); dx++)
            for (int dz = lz == 0 ? -1 : 0; dz <= (lz == 15 ? 1 : 0); dz++)
                for (int dy = ly == 0 ? -1 : 0; dy <= (ly == 15 ? 1 : 0); dy++)
                {
                    int s = sy + dy;
                    if (s < 0 || s >= Chunk.Sections) continue;
                    var key = (cx + dx, cz + dz, s);
                    dirty.Add(key);
                    if (urg) urgent.Add(key);
                }
    }

    static byte[] Arr(Chunk c, bool sky) => sky ? c.Sky : c.Bl;

    void SpreadLight(Queue<(int x, int y, int z)> q, bool sky)
    {
        while (q.Count > 0)
        {
            var (x, y, z) = q.Dequeue();
            var ch = GC(x >> 4, z >> 4);
            if (ch == null) continue;
            int L = Arr(ch, sky)[(x & 15) + (z & 15) * 16 + y * 256];
            if (L <= 1) continue;
            for (int d = 0; d < 6; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d], nz = z + Dz[d];
                if (ny < 0 || ny >= Chunk.Height) continue;
                var nc = GC(nx >> 4, nz >> 4);
                if (nc == null) continue;
                int ni = (nx & 15) + (nz & 15) * 16 + ny * 256;
                int op = Blk.Opacity[nc.Blocks[ni]];
                if (op >= 15) continue;
                int nl = sky ? ((d == 3 && L == 15 && op == 0) ? 15 : L - Math.Max(1, op)) : L - 1;
                var arr = Arr(nc, sky);
                if (nl > arr[ni])
                {
                    arr[ni] = (byte)nl;
                    MarkCell(nx, ny, nz, false);
                    q.Enqueue((nx, ny, nz));
                }
            }
        }
    }

    void RemoveLight(int x, int y, int z, int oldL, bool sky)
    {
        var rq = new Queue<(int x, int y, int z, int l)>();
        var addq = new Queue<(int x, int y, int z)>();
        var c0 = GC(x >> 4, z >> 4);
        Arr(c0, sky)[(x & 15) + (z & 15) * 16 + y * 256] = 0;
        rq.Enqueue((x, y, z, oldL));
        while (rq.Count > 0)
        {
            var (cx, cy, cz, cl) = rq.Dequeue();
            for (int d = 0; d < 6; d++)
            {
                int nx = cx + Dx[d], ny = cy + Dy[d], nz = cz + Dz[d];
                if (ny < 0 || ny >= Chunk.Height) continue;
                var nc = GC(nx >> 4, nz >> 4);
                if (nc == null) continue;
                int ni = (nx & 15) + (nz & 15) * 16 + ny * 256;
                var arr = Arr(nc, sky);
                int nl = arr[ni];
                if (nl == 0) continue;
                if (nl < cl || (sky && d == 3 && cl == 15 && nl == 15))
                {
                    arr[ni] = 0;
                    MarkCell(nx, ny, nz, false);
                    rq.Enqueue((nx, ny, nz, nl));
                }
                else if (nl >= cl) addq.Enqueue((nx, ny, nz));
            }
        }
        SpreadLight(addq, sky);
    }

    void InitLight(Chunk ch)
    {
        int maxTop = 0;
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            {
                int l = 15;
                for (int y = Chunk.Height - 1; y >= 0; y--)
                {
                    int idx = x + z * 16 + y * 256;
                    int op = Blk.Opacity[ch.Blocks[idx]];
                    if (op >= 15) { if (y > maxTop) maxTop = y; break; }
                    if (op > 0) { l = Math.Max(0, l - op); if (y > maxTop) maxTop = y; }
                    ch.Sky[idx] = (byte)l;
                    if (l == 0) break;
                }
            }
        var q = new Queue<(int, int, int)>();
        int yMax = Math.Min(Chunk.Height - 1, maxTop + 2);
        int wx0 = ch.X * 16, wz0 = ch.Z * 16;
        for (int y = 0; y <= yMax; y++)
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    int l = ch.Sky[x + z * 16 + y * 256];
                    if (l < 2) continue;
                    bool need = false;
                    for (int d = 0; d < 6 && !need; d++)
                    {
                        int nx = wx0 + x + Dx[d], ny = y + Dy[d], nz = wz0 + z + Dz[d];
                        if (ny < 0 || ny >= Chunk.Height) continue;
                        var nc = GC(nx >> 4, nz >> 4);
                        if (nc == null) continue;
                        int ni = (nx & 15) + (nz & 15) * 16 + ny * 256;
                        int op = Blk.Opacity[nc.Blocks[ni]];
                        if (op >= 15) continue;
                        int nl = (d == 3 && l == 15 && op == 0) ? 15 : l - Math.Max(1, op);
                        if (nl > nc.Sky[ni]) need = true;
                    }
                    if (need) q.Enqueue((wx0 + x, y, wz0 + z));
                }
        // block light emitters (placed torches / fire restored from the save) and neighbour spill
        var qb = new Queue<(int, int, int)>();
        lock (Edits)
            if (Edits.TryGetValue(Key(ch.X, ch.Z), out var ed))
                foreach (var kv in ed)
                {
                    int em = Blk.Emit[kv.Value];
                    if (em == 0) continue;
                    ch.Bl[kv.Key] = (byte)em;
                    qb.Enqueue((wx0 + (kv.Key & 15), kv.Key >> 8, wz0 + ((kv.Key >> 4) & 15)));
                }
        // neighbour edge cells spill light into the new chunk
        int[] hdx = { 1, -1, 0, 0 }, hdz = { 0, 0, 1, -1 };
        for (int d = 0; d < 4; d++)
        {
            int ncx = ch.X + hdx[d], ncz = ch.Z + hdz[d];
            var nc = GC(ncx, ncz);
            if (nc == null) continue;
            for (int t = 0; t < 16; t++)
                for (int y = 0; y < Chunk.Height; y++)
                {
                    int lx, lz;
                    if (d == 0) { lx = 0; lz = t; } else if (d == 1) { lx = 15; lz = t; } else if (d == 2) { lx = t; lz = 0; } else { lx = t; lz = 15; }
                    int idx = lx + lz * 16 + y * 256;
                    if (nc.Sky[idx] >= 2) q.Enqueue((ncx * 16 + lx, y, ncz * 16 + lz));
                    if (nc.Bl[idx] >= 2) qb.Enqueue((ncx * 16 + lx, y, ncz * 16 + lz));
                }
        }
        SpreadLight(q, true);
        SpreadLight(qb, false);
        for (int sy = 0; sy < Chunk.Sections; sy++) dirty.Add((ch.X, ch.Z, sy));
    }

    // ------------------------------------------------------------------ per-frame streaming
    int unloadRadius;
    public void Update(double px, double py, double pz, int rd)
    {
        if (Disposed) return;
        int pcx = FloorDiv16(px), pcz = FloorDiv16(pz);
        int genR = rd + 1;
        int maxFlight = Math.Max(2, Environment.ProcessorCount - 2);
        if (genInFlight < maxFlight)
        {
            foreach (var (dx, dz) in Ring)
            {
                if (dx * dx + dz * dz > genR * genR) break;
                int cx = pcx + dx, cz = pcz + dz;
                long k = Key(cx, cz);
                if (Chunks.ContainsKey(k) || requested.Contains(k)) continue;
                requested.Add(k);
                Interlocked.Increment(ref genInFlight);
                Task.Run(() =>
                {
                    try
                    {
                        var ch = new Chunk(cx, cz);
                        Gen.Generate(ch);
                        ApplyEdits(ch);
                        genDone.Enqueue(ch);
                    }
                    finally { Interlocked.Decrement(ref genInFlight); }
                });
                if (genInFlight >= maxFlight) break;
            }
        }
        var sw = Stopwatch.StartNew();
        int n = 0;
        while (n < 4 && sw.Elapsed.TotalMilliseconds < 7 && genDone.TryDequeue(out var done)) { Insert(done); n++; }

        // unload
        unloadRadius = rd + 3;
        if ((Time & 7) == 0 || true)
            foreach (var kv in Chunks)
            {
                var c = kv.Value;
                if (Math.Abs(c.X - pcx) > unloadRadius || Math.Abs(c.Z - pcz) > unloadRadius) Unload(c);
            }
        ScheduleMeshes(px, py, pz, pcx, pcz);
        // upload finished meshes
        sw.Restart();
        int up = 0;
        while (up < 12 && sw.Elapsed.TotalMilliseconds < 5 && meshDone.TryDequeue(out var mr)) { ApplyMesh(mr); up++; }
    }

    void Insert(Chunk ch)
    {
        long k = Key(ch.X, ch.Z);
        requested.Remove(k);
        if (Chunks.ContainsKey(k)) return;
        Chunks[k] = ch; cacheCX = int.MinValue;
        InitLight(ch);
        ch.State = 1;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                if (Chunks.TryGetValue(Key(ch.X + dx, ch.Z + dz), out var nb)) { nb.ReadyNeighbors++; ch.ReadyNeighbors++; }
            }
    }

    void Unload(Chunk c)
    {
        if (!Chunks.TryRemove(Key(c.X, c.Z), out _)) return;
        cacheCX = int.MinValue;
        c.State = -1;
        c.FreeGpu();
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dz != 0) && Chunks.TryGetValue(Key(c.X + dx, c.Z + dz), out var nb)) nb.ReadyNeighbors--;
    }

    void ScheduleMeshes(double px, double py, double pz, int pcx, int pcz)
    {
        if (urgent.Count > 0)
        {
            foreach (var key in urgent.ToArray())
            {
                if (Chunks.TryGetValue(Key(key.Item1, key.Item2), out var ch) && ch.ReadyNeighbors == 8)
                {
                    var r = Mesher.Get().Build(this, ch, key.Item3, ++ch.JobId[key.Item3]);
                    ApplyMesh(r);
                    dirty.Remove(key);
                }
            }
            urgent.Clear();
        }
        int cap = Math.Max(2, Environment.ProcessorCount - 2);
        if (meshInFlight >= cap || dirty.Count == 0) return;
        var cand = new List<(float d, (int, int, int) k)>();
        List<(int, int, int)> stale = null;
        foreach (var key in dirty)
        {
            if (!Chunks.TryGetValue(Key(key.Item1, key.Item2), out var ch) || ch.State < 0) { (stale ??= new()).Add(key); continue; }
            if (ch.ReadyNeighbors != 8) continue;
            float dx = key.Item1 - pcx, dz = key.Item2 - pcz, dy = (key.Item3 * 16 + 8 - (float)py) / 16f;
            cand.Add((dx * dx + dz * dz + dy * dy * 0.5f, key));
        }
        if (stale != null) foreach (var k in stale) dirty.Remove(k);
        int take = Math.Min(cap - meshInFlight, cand.Count);
        for (int t = 0; t < take; t++)
        {
            int best = t;
            for (int i = t + 1; i < cand.Count; i++) if (cand[i].d < cand[best].d) best = i;
            (cand[t], cand[best]) = (cand[best], cand[t]);
            var key = cand[t].k;
            dirty.Remove(key);
            Chunks.TryGetValue(Key(key.Item1, key.Item2), out var ch);
            int job = ++ch.JobId[key.Item3];
            Interlocked.Increment(ref meshInFlight);
            Task.Run(() =>
            {
                try { var r = Mesher.Get().Build(this, ch, key.Item3, job); if (!Disposed) meshDone.Enqueue(r); }
                finally { Interlocked.Decrement(ref meshInFlight); }
            });
        }
    }

    void ApplyMesh(MeshResult r)
    {
        var ch = r.C;
        if (ch.State < 0 || r.Job < ch.Uploaded[r.Sy]) return;
        ch.Uploaded[r.Sy] = r.Job;
        ch.Opaque[r.Sy] = Upload(ch.Opaque[r.Sy], r.Ov, r.Oq);
        ch.Water[r.Sy] = Upload(ch.Water[r.Sy], r.Wv, r.Wq);
    }

    static GpuMesh Upload(GpuMesh m, float[] data, int quads)
    {
        if (data == null || quads == 0) { m?.Dispose(); return null; }
        if (m == null || m.Vao == 0)
        {
            m = new GpuMesh { Vao = GL.GenVertexArray(), Vbo = GL.GenBuffer() };
            GL.BindVertexArray(m.Vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.Vbo);
            int stride = Mesher.FloatsPerVertex * 4;
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 12);
            GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, 20);
            GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, 24);
            GL.EnableVertexAttribArray(4); GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, 28);
            GL.EnableVertexAttribArray(5); GL.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, stride, 32);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, SharedEbo);
        }
        else { GL.BindVertexArray(m.Vao); GL.BindBuffer(BufferTarget.ArrayBuffer, m.Vbo); }
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * 4, data, BufferUsageHint.StaticDraw);
        m.IndexCount = quads * 6;
        return m;
    }

    public bool AreaReady(int pcx, int pcz, int r)
    {
        if (meshInFlight > 0 || !meshDone.IsEmpty) return false;
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                if (!Chunks.TryGetValue(Key(pcx + dx, pcz + dz), out var c) || c.ReadyNeighbors != 8) return false;
        foreach (var k in dirty) if (Math.Abs(k.Item1 - pcx) <= r && Math.Abs(k.Item2 - pcz) <= r) return false;
        return true;
    }
    public float AreaProgress(int pcx, int pcz, int r)
    {
        int tot = (2 * r + 1) * (2 * r + 1), ok = 0;
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                if (Chunks.TryGetValue(Key(pcx + dx, pcz + dz), out var c) && c.ReadyNeighbors == 8) ok++;
        return ok / (float)tot;
    }

    public void Dispose()
    {
        Disposed = true;
        foreach (var c in Chunks.Values) c.FreeGpu();
        Chunks.Clear();
    }

    // ------------------------------------------------------------------ raycast
    public bool Raycast(Vector3d o, Vector3d d, double max, out int hx, out int hy, out int hz, out int nx, out int ny, out int nz)
    {
        hx = hy = hz = nx = ny = nz = 0;
        int x = (int)Math.Floor(o.X), y = (int)Math.Floor(o.Y), z = (int)Math.Floor(o.Z);
        int sx = d.X > 0 ? 1 : -1, sy = d.Y > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
        double tdx = d.X != 0 ? Math.Abs(1 / d.X) : double.MaxValue, tdy = d.Y != 0 ? Math.Abs(1 / d.Y) : double.MaxValue, tdz = d.Z != 0 ? Math.Abs(1 / d.Z) : double.MaxValue;
        double tmx = d.X > 0 ? (x + 1 - o.X) * tdx : d.X < 0 ? (o.X - x) * tdx : double.MaxValue;
        double tmy = d.Y > 0 ? (y + 1 - o.Y) * tdy : d.Y < 0 ? (o.Y - y) * tdy : double.MaxValue;
        double tmz = d.Z > 0 ? (z + 1 - o.Z) * tdz : d.Z < 0 ? (o.Z - z) * tdz : double.MaxValue;
        int fx = 0, fy = 0, fz = 0;
        for (int i = 0; i < 200; i++)
        {
            byte b = GetBlock(x, y, z);
            if (b != Blk.Air && !Blk.WaterT[b]) { hx = x; hy = y; hz = z; nx = fx; ny = fy; nz = fz; return true; }
            double t;
            if (tmx < tmy && tmx < tmz) { t = tmx; x += sx; tmx += tdx; fx = -sx; fy = 0; fz = 0; }
            else if (tmy < tmz) { t = tmy; y += sy; tmy += tdy; fx = 0; fy = -sy; fz = 0; }
            else { t = tmz; z += sz; tmz += tdz; fx = 0; fy = 0; fz = -sz; }
            if (t > max) return false;
        }
        return false;
    }
}
