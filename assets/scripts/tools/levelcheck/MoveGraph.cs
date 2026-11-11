using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.World;

namespace SonOfMud.Tools.LevelCheck;

public enum EdgeKind
{
    /// <summary>落到另一段地面上。</summary>
    Move,
    /// <summary>从出口离开参与检查的房间。</summary>
    Exit,
    /// <summary>站在这里用普通攻击能打碎一块 B / H（不移动）。</summary>
    Break,
    /// <summary>按交互键过门。</summary>
    Door,
}

/// <summary>移动图的一条边：从 From 段上的 StartX（全局像素）出发，用 Form 形态做 Action，到达 To（或出口）。</summary>
public sealed class MoveEdge
{
    public Segment From;
    public double StartX;
    public int Form;
    public MoveAction Action;
    public EdgeKind Kind;
    public Segment To;
    public ExitDef Exit;
    public Vector2 End;
    public int Frames;
    public bool Climb;
    public bool WallJump;
    /// <summary>起点左右挪 3 像素结果不变（真实按键机器人走到起点的误差在这个范围内）。</summary>
    public bool Robust;
    public List<string> Crossings = new();
    public Vector2I BreakTile;
    public int BreakDir;

    public string TargetKey => Kind switch
    {
        EdgeKind.Exit => ExitKey(From.Room, Exit),
        EdgeKind.Break => $"break:{From.Room.Id}:{BreakTile.X},{BreakTile.Y}",
        _ => To.Key,
    };

    public static string ExitKey(LevelRoom room, ExitDef exit) => $"exit:{room.Id}:{exit.Side}{exit.Offset}";

    public string Describe()
    {
        string form = Form == SimConfig.Mud ? "泥" : "水";
        string from = $"{From.Room.Id} x={StartX - From.Room.Left:F0}（第{From.Row}行地面）";
        return Kind switch
        {
            EdgeKind.Exit => $"[{form}] {from} {Action?.Name} → 出口 {Exit.Side}{Exit.Offset}（{Exit.Target}）",
            EdgeKind.Break => $"[{form}] {from} 朝{(BreakDir > 0 ? "右" : "左")}攻击 → 打碎格({BreakTile.X},{BreakTile.Y})",
            EdgeKind.Door => $"[{form}] {from} 过门 → {To.Describe()}",
            _ => $"[{form}] {from} {Action?.Name} → {To.Describe()}{(Climb ? "（攀墙）" : "")}{(WallJump ? "（蹬墙跳）" : "")}",
        };
    }
}

/// <summary>
/// 移动图：节点是各房间的地面段，边由逐帧模拟得到（按房间懒计算、缓存）。
/// 同一状态（剧情标记、已打碎的 B / H）下有效；状态变了要新建一张图。
/// </summary>
public sealed class MoveGraph
{
    public SimConfig Cfg { get; }
    public LevelWorld World { get; }
    public LevelState State { get; }
    public IReadOnlyList<MoveAction> Actions { get; set; } = ActionSet.Default;
    public int[] Forms { get; set; } = { SimConfig.Water, SimConfig.Mud };
    /// <summary>额外的边（门）。WorldCheck 设置。</summary>
    public Func<MoveGraph, LevelRoom, IEnumerable<MoveEdge>> ExtraEdges { get; set; }
    /// <summary>模拟中发现的问题（走出房间却没有出口、换房卡墙），去重。</summary>
    public SortedSet<string> Problems { get; } = new();
    public int SimulatedRuns { get; private set; }

    private readonly Dictionary<string, RoomSegments> _segments = new();
    private readonly Dictionary<string, GridWorld> _geometry = new();
    private readonly Dictionary<Segment, List<MoveEdge>> _edges = new();
    private readonly HashSet<string> _roomsDone = new();

    public MoveGraph(SimConfig cfg, LevelWorld world, LevelState state)
    {
        Cfg = cfg;
        World = world;
        State = state;
    }

    public GridWorld Geometry(LevelRoom room)
    {
        if (!_geometry.TryGetValue(room.Id, out var g)) _geometry[room.Id] = g = World.Geometry(room, State);
        return g;
    }

    public RoomSegments Segments(LevelRoom room)
    {
        if (!_segments.TryGetValue(room.Id, out var s)) _segments[room.Id] = s = RoomSegments.Build(room, Geometry(room), Cfg);
        return s;
    }

    public Segment FindSegment(LevelRoom room, Vector2 center) => Segments(room).Find(center.X, center.Y + Cfg.HalfH);

    public IReadOnlyList<MoveEdge> EdgesFrom(Segment s)
    {
        EnsureRoom(s.Room);
        return _edges.TryGetValue(s, out var list) ? list : (IReadOnlyList<MoveEdge>)Array.Empty<MoveEdge>();
    }

    public IEnumerable<MoveEdge> AllEdges(LevelRoom room)
    {
        EnsureRoom(room);
        return Segments(room).All.SelectMany(s => _edges.TryGetValue(s, out var l) ? l : new List<MoveEdge>());
    }

    /// <summary>在 feet（全局，脚底中点）放下身体，松手等它落地站稳，返回所在的段（放不下或掉出世界时为 null）。</summary>
    public Segment SettleAt(LevelRoom room, Vector2 feet, int form, out SimRun run)
    {
        var script = new InputScript("落地");
        run = World.Simulate(Cfg, State, room, feet.X, feet.Y - Cfg.HalfH, form, script, startGrounded: false);
        SimulatedRuns++;
        return run.End == RunEnd.Settled ? FindSegment(run.EndRoom, run.EndPos) : null;
    }

    // ---------- 采样点 ----------

    public List<int> Samples(Segment s)
    {
        var xs = new SortedSet<int>();
        int width = s.MaxX - s.MinX;
        if (width < 8)
        {
            xs.Add((s.MinX + s.MaxX) / 2);
            if (s.WallLeft) xs.Add(s.MinX);
            if (s.WallRight) xs.Add(s.MaxX);
            return xs.ToList();
        }
        xs.Add(s.WallLeft ? s.MinX : s.MinX + 4);
        xs.Add(s.WallRight ? s.MaxX : s.MaxX - 4);
        int left = (int)s.Room.Left;
        int first = left + 16 + RoomGrid.Tile * (int)Math.Ceiling((s.MinX - left - 16) / (double)RoomGrid.Tile);
        for (int x = first; x <= s.MaxX; x += RoomGrid.Tile)
            if (x - s.MinX >= 8 && s.MaxX - x >= 8) xs.Add(x);
        return xs.ToList();
    }

    private bool WallPressed(Segment s, double x) => (s.WallLeft && x <= s.MinX) || (s.WallRight && x >= s.MaxX);

    // ---------- 计算一个房间的边 ----------

    private void EnsureRoom(LevelRoom room)
    {
        if (!_roomsDone.Add(room.Id)) return;
        RoomSegments segs = Segments(room);
        foreach (Segment s in segs.All)
        {
            var edges = new List<MoveEdge>();
            foreach (int form in Forms)
            {
                var candidates = new Dictionary<string, List<MoveEdge>>();
                List<int> samples = Samples(s);
                foreach (MoveAction action in Actions)
                {
                    IEnumerable<int> starts;
                    if (action.Kind == ActionKind.WalkOff)
                    {
                        if ((action.Dir > 0 && s.WallRight) || (action.Dir < 0 && s.WallLeft)) continue;
                        starts = new[] { (int)Math.Round(s.MidX) };
                    }
                    else starts = samples;
                    foreach (int x in starts)
                    {
                        MoveEdge edge = Run(s, x, form, action);
                        if (edge == null) continue;
                        string key = edge.TargetKey;
                        if (!candidates.TryGetValue(key, out var list)) candidates[key] = list = new List<MoveEdge>();
                        list.Add(edge);
                    }
                }
                foreach (var (_, list) in candidates) edges.Add(PickBest(list));
                edges.AddRange(BreakEdges(s, form, samples));
            }
            _edges[s] = edges;
        }
        if (ExtraEdges != null)
            foreach (MoveEdge e in ExtraEdges(this, room))
            {
                if (!_edges.TryGetValue(e.From, out var l)) _edges[e.From] = l = new List<MoveEdge>();
                l.Add(e);
            }
    }

    /// <summary>从 s 段的 x 处做一次动作；结果是同一段、失败或没意义时返回 null。</summary>
    public MoveEdge Run(Segment s, double x, int form, MoveAction action)
    {
        SimRun run = World.Simulate(Cfg, State, s.Room, x, s.CenterY(Cfg), form, action.Script);
        SimulatedRuns++;
        switch (run.End)
        {
            case RunEnd.Settled:
            {
                Segment to = FindSegment(run.EndRoom, run.EndPos);
                if (to == null)
                {
                    Problems.Add($"落点不在任何地面段上：{run.EndRoom.Id} {LevelWorld.Fmt(run.EndRoom, run.Body)}（{s.Describe()} 的 {action.Name}）");
                    return null;
                }
                if (to == s) return null;
                return Make(s, x, form, action, run, EdgeKind.Move, to);
            }
            case RunEnd.Exited:
                return Make(s, x, form, action, run, EdgeKind.Exit, null);
            case RunEnd.OutOfWorld:
            case RunEnd.Stuck:
                Problems.Add(run.Detail);
                return null;
            default:
                return null;
        }
    }

    private static MoveEdge Make(Segment s, double x, int form, MoveAction action, SimRun run, EdgeKind kind, Segment to) => new()
    {
        From = s, StartX = x, Form = form, Action = action, Kind = kind, To = to, Exit = run.Exit, End = run.EndPos,
        Frames = run.Frames, Climb = run.Body.EverClimbed, WallJump = run.Body.EverWallJumped, Crossings = run.Crossings,
    };

    private MoveEdge PickBest(List<MoveEdge> list)
    {
        // 优先：不攀墙 → 帧数少；逐个检查稳健性，取第一个稳健的
        var ordered = list.OrderBy(e => e.Climb ? 1 : 0).ThenBy(e => e.WallJump ? 1 : 0).ThenBy(e => e.Frames).ToList();
        foreach (MoveEdge e in ordered.Take(4))
            if (IsRobust(e))
            {
                e.Robust = true;
                return e;
            }
        return ordered[0];
    }

    private bool IsRobust(MoveEdge e)
    {
        if (WallPressed(e.From, e.StartX)) return true;
        string key = e.TargetKey;
        foreach (int dx in new[] { -3, 3 })
        {
            double x = Math.Clamp(e.StartX + dx, e.From.MinX, e.From.MaxX);
            if (Math.Abs(x - e.StartX) < 0.5) continue;
            MoveEdge other = Run(e.From, x, e.Form, e.Action);
            if (other == null || other.TargetKey != key) return false;
        }
        return true;
    }

    /// <summary>站在采样点朝左 / 右普通攻击能碰到的 B / H（攻击框和 AttackState 一致：前移 reach/2 + 前移量，宽 reach，高 AttackHeight）。</summary>
    private IEnumerable<MoveEdge> BreakEdges(Segment s, int form, List<int> samples)
    {
        FormParams p = Cfg.Form(form);
        GridWorld geo = Geometry(s.Room);
        var seen = new HashSet<Vector2I>();
        double cy = s.CenterY(Cfg);
        foreach (int x in samples)
            foreach (int d in new[] { 1, -1 })
            {
                double cx = x + d * (p.AttackReach * 0.5 + p.AttackForwardOffset);
                double bx0 = cx - p.AttackReach * 0.5, bx1 = cx + p.AttackReach * 0.5;
                double by0 = cy - p.AttackHeight * 0.5, by1 = cy + p.AttackHeight * 0.5;
                for (int c = geo.ColAt(bx0); c <= geo.ColAt(bx1); c++)
                    for (int r = geo.RowAt(by0); r <= geo.RowAt(by1); r++)
                    {
                        if (!RoomGrid.IsBreakableChar(s.Room.Grid.At(c, r)) || geo.KindAt(c, r) == TileKind.Empty) continue;
                        double tx0 = geo.ColLeft(c), ty0 = geo.RowTop(r);
                        if (bx1 <= tx0 || bx0 >= tx0 + RoomGrid.Tile || by1 <= ty0 || by0 >= ty0 + RoomGrid.Tile) continue;
                        var tile = new Vector2I(c, r);
                        if (!seen.Add(tile)) continue;
                        yield return new MoveEdge { From = s, StartX = x, Form = form, Kind = EdgeKind.Break, To = s, BreakTile = tile, BreakDir = d, Robust = true };
                    }
            }
    }

    // ---------- 搜索 ----------

    public sealed class Search
    {
        public Dictionary<Segment, MoveEdge> Prev { get; } = new();
        public HashSet<Segment> Reached { get; } = new();
        /// <summary>能到的出口（键：ExitKey）→ 用到的最后一条边。</summary>
        public Dictionary<string, MoveEdge> Exits { get; } = new();
        public List<MoveEdge> Breaks { get; } = new();

        public List<MoveEdge> RouteTo(Segment target)
        {
            var route = new List<MoveEdge>();
            if (!Reached.Contains(target)) return null;
            for (Segment s = target; Prev.TryGetValue(s, out MoveEdge e); s = e.From) route.Add(e);
            route.Reverse();
            return route;
        }

        public List<MoveEdge> RouteToExit(string exitKey)
        {
            if (!Exits.TryGetValue(exitKey, out MoveEdge last)) return null;
            List<MoveEdge> route = RouteTo(last.From);
            route.Add(last);
            return route;
        }
    }

    /// <summary>路线偏好：不稳健的边很贵；攀墙、蹬墙跳比普通跳贵（同样能到时优先走台阶）；帧数少的略便宜。</summary>
    private static double Cost(MoveEdge e) => 1 + (e.Robust ? 0 : 4) + (e.Climb ? 2 : 0) + (e.WallJump ? 2 : 0) + e.Frames / 600.0;

    /// <summary>从 starts 出发能到哪些段（Dijkstra，偏好稳健、不攀墙、帧数少的边）。filter 为 null 时用全部边。</summary>
    public Search Explore(IEnumerable<Segment> starts, Func<MoveEdge, bool> filter = null)
    {
        var result = new Search();
        var dist = new Dictionary<Segment, double>();
        var queue = new PriorityQueue<Segment, double>();
        foreach (Segment s in starts)
        {
            if (s == null || dist.ContainsKey(s)) continue;
            dist[s] = 0;
            queue.Enqueue(s, 0);
        }
        var exitCost = new Dictionary<string, double>();
        while (queue.TryDequeue(out Segment s, out double d))
        {
            if (!result.Reached.Add(s)) continue;
            foreach (MoveEdge e in EdgesFrom(s))
            {
                if (filter != null && !filter(e)) continue;
                double nd = d + Cost(e);
                switch (e.Kind)
                {
                    case EdgeKind.Break:
                        result.Breaks.Add(e);
                        break;
                    case EdgeKind.Exit:
                        string key = e.TargetKey;
                        if (!exitCost.TryGetValue(key, out double old) || nd < old)
                        {
                            exitCost[key] = nd;
                            result.Exits[key] = e;
                        }
                        break;
                    default:
                        if (dist.TryGetValue(e.To, out double cur) && cur <= nd) break;
                        dist[e.To] = nd;
                        result.Prev[e.To] = e;
                        queue.Enqueue(e.To, nd);
                        break;
                }
            }
        }
        return result;
    }

    /// <summary>在 nodes 范围内，哪些段能到达目标（goal 段、或 exitGoal 为真的出口边）。</summary>
    public HashSet<Segment> CanReach(IEnumerable<Segment> nodes, Func<Segment, bool> goal, Func<MoveEdge, bool> exitGoal = null, Func<MoveEdge, bool> filter = null)
    {
        var set = new HashSet<Segment>(nodes);
        var reverse = new Dictionary<Segment, List<Segment>>();
        var good = new HashSet<Segment>();
        var stack = new Stack<Segment>();
        foreach (Segment s in set)
        {
            if (goal(s) && good.Add(s)) stack.Push(s);
            foreach (MoveEdge e in EdgesFrom(s))
            {
                if (filter != null && !filter(e)) continue;
                if (e.Kind == EdgeKind.Exit)
                {
                    if (exitGoal != null && exitGoal(e) && good.Add(s)) stack.Push(s);
                    continue;
                }
                if (e.Kind == EdgeKind.Break || e.To == null) continue;
                if (!reverse.TryGetValue(e.To, out var l)) reverse[e.To] = l = new List<Segment>();
                l.Add(s);
            }
        }
        while (stack.Count > 0)
        {
            Segment s = stack.Pop();
            if (!reverse.TryGetValue(s, out var preds)) continue;
            foreach (Segment p in preds)
                if (set.Contains(p) && good.Add(p)) stack.Push(p);
        }
        return good;
    }
}
