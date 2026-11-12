using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.World;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>设计规则的上限 / 下限（计划 3.3），单位：格。</summary>
public sealed class RuleLimits
{
    public int MaxStepTiles = 3;
    public int MaxGapMain = 4;
    public int MaxGapWater = 6;
    public int MinHeadroomTiles = 5;
    public int MinPassageTiles = 2;
}

/// <summary>一条检查结论：规则名、房间、格子、说明。IsError 为假的是提示（例如“这里要攀墙”）。</summary>
public sealed class Finding
{
    public string Rule = "";
    public string Room = "";
    public Vector2I Cell;
    public string Message = "";
    public bool IsError = true;

    public override string ToString() => $"{(IsError ? "[问题]" : "[提示]")}[{Rule}] {Room} 格({Cell.X},{Cell.Y})：{Message}";
}

/// <summary>入口：出口开口（从外面进来）或出生点 / 存档点。</summary>
public sealed class Entrance
{
    public string Name = "";
    public List<Segment> Nodes = new();
}

public sealed class PairResult
{
    public string From = "";
    public string To = "";
    /// <summary>-1 表示两种形态可随时切换；0 水；1 泥。</summary>
    public int Form = -1;
    public bool Reachable;
    public List<MoveEdge> Route;
}

public sealed class RoomReport
{
    public string RoomId = "";
    public bool ConditionMet;
    public MoveGraph Graph;
    public LevelRoom Room;
    public List<Entrance> Entrances = new();
    /// <summary>出口键（MoveEdge.ExitKey）→ 给人看的名字。</summary>
    public Dictionary<string, string> Exits = new();
    public List<PairResult> Pairs = new();
    public List<Finding> Findings = new();
    public List<MoveEdge> ClimbShortcuts = new();
    public List<MoveEdge> OneWayDrops = new();
    public List<Segment> DeadEnds = new();
    public List<Vector2I> BrokenTiles = new();
    public HashSet<Segment> Reached = new();

    public IEnumerable<Finding> Errors => Findings.Where(f => f.IsError);

    public PairResult Pair(string from, string to, int form = -1) =>
        Pairs.FirstOrDefault(p => p.From == from && p.To == to && p.Form == form);

    public bool Reachable(string from, string to, int form = -1) => Pair(from, to, form)?.Reachable ?? false;

    public string Summary()
    {
        var lines = new List<string> { $"房间 {RoomId}（条件{(ConditionMet ? "成立" : "不成立")}）：段 {Graph.Segments(Room).All.Count} 个，模拟 {Graph.SimulatedRuns} 次" };
        foreach (PairResult p in Pairs.Where(p => p.Form == -1))
            lines.Add($"  {p.From} → {p.To}：{(p.Reachable ? $"能到（{p.Route.Count} 步）" : "到不了")}");
        lines.AddRange(Findings.Select(f => "  " + f));
        lines.AddRange(DeadEnds.Select(d => "  [死局] " + d.Describe()));
        lines.AddRange(ClimbShortcuts.Select(e => "  [攀墙捷径] " + e.Describe()));
        return string.Join("\n", lines);
    }
}

/// <summary>
/// 单房间检查：每对出入口之间能否到达（含路线）、出生点放得下且能离开、设计规则（计划 3.3）、
/// 死局、单程下落、靠攀墙走的捷径。B / H 按“能打到就打碎”迭代到不动点。
/// </summary>
public static class RoomCheck
{
    public static RoomReport Run(SimConfig cfg, RoomDef def, RoomGrid grid, bool conditionMet, RuleLimits limits = null,
        IReadOnlyList<MoveAction> actions = null, Vector2? cellSize = null)
    {
        limits ??= new RuleLimits();
        var world = new LevelWorld(cellSize ?? LevelWorld.DefaultCellSize);
        LevelRoom room = world.Add(def, grid);
        var state = new LevelState { ConditionOverride = conditionMet };
        var report = new RoomReport { RoomId = def.Id, ConditionMet = conditionMet, Room = room };

        MoveGraph graph;
        while (true)
        {
            graph = new MoveGraph(cfg, world, state);
            if (actions != null) graph.Actions = actions;
            report.Entrances = FindEntrances(graph, room);
            MoveGraph.Search all = graph.Explore(report.Entrances.SelectMany(e => e.Nodes));
            var fresh = all.Breaks.Where(b => !state.BrokenIn(room.Id).Contains(b.BreakTile)).Select(b => b.BreakTile).Distinct().ToList();
            if (fresh.Count == 0)
            {
                report.Reached = all.Reached;
                break;
            }
            state.BrokenIn(room.Id).UnionWith(fresh);
            report.BrokenTiles.AddRange(fresh);
        }
        report.Graph = graph;

        foreach (ExitDef exit in def.Exits)
            if (exit != null && exit.Kind == ExitKind.Edge)
                report.Exits[MoveEdge.ExitKey(room, exit)] = ExitName(exit);

        // 每对出入口
        foreach (Entrance entrance in report.Entrances)
            foreach (int form in new[] { -1, SimConfig.Water, SimConfig.Mud })
            {
                MoveGraph.Search search = graph.Explore(entrance.Nodes, form < 0 ? null : e => e.Form == form);
                foreach (var (key, name) in report.Exits)
                {
                    if (entrance.Name == name) continue;
                    List<MoveEdge> route = search.RouteToExit(key);
                    report.Pairs.Add(new PairResult { From = entrance.Name, To = name, Form = form, Reachable = route != null, Route = route });
                }
                foreach (Entrance other in report.Entrances)
                {
                    if (other == entrance || other.Name.StartsWith("出口")) continue;
                    Segment target = other.Nodes.FirstOrDefault(n => search.Reached.Contains(n));
                    report.Pairs.Add(new PairResult { From = entrance.Name, To = other.Name, Form = form, Reachable = target != null, Route = target != null ? search.RouteTo(target) : null });
                }
            }

        CheckSpawns(cfg, graph, room, report);
        report.Findings.AddRange(Rules(cfg, graph, room, report, limits));
        foreach (string p in graph.Problems)
            report.Findings.Add(new Finding { Rule = "模拟", Room = room.Id, Message = p });

        // 死局：能到的段里，到不了任何出口也到不了存档点的
        List<Segment> saves = report.Entrances.Where(e => e.Name == "S").SelectMany(e => e.Nodes).ToList();
        HashSet<Segment> good = graph.CanReach(report.Reached, s => saves.Contains(s), e => e.Kind == EdgeKind.Exit);
        report.DeadEnds = report.Reached.Where(s => !good.Contains(s)).OrderBy(s => s.Row).ThenBy(s => s.MinX).ToList();
        foreach (Segment s in report.DeadEnds)
            report.Findings.Add(new Finding { Rule = "死局", Room = room.Id, Cell = CellOf(room, s.MidX, s.FloorY - 1), Message = $"{s.Describe()} 能到，但从这里到不了任何出口或存档点" });

        // 单程下落、靠攀墙的捷径
        List<MoveEdge> moves = report.Reached.SelectMany(graph.EdgesFrom).Where(e => e.Kind == EdgeKind.Move && e.To.Room == room).ToList();
        foreach (var group in moves.GroupBy(e => (e.From, e.To)))
        {
            MoveEdge e = group.OrderBy(x => x.Climb).ThenBy(x => x.Frames).First();
            if (!graph.Explore(new[] { e.To }).Reached.Contains(e.From) && e.To.FloorY > e.From.FloorY)
                report.OneWayDrops.Add(e);
        }
        foreach (var group in moves.Where(e => e.Climb || e.WallJump).GroupBy(e => (e.From, e.To)))
        {
            if (moves.Any(m => m.From == group.Key.From && m.To == group.Key.To && !m.Climb && !m.WallJump)) continue;
            MoveGraph.Search noClimb = graph.Explore(new[] { group.Key.From }, m => !m.Climb && !m.WallJump);
            if (!noClimb.Reached.Contains(group.Key.To)) report.ClimbShortcuts.Add(group.OrderBy(x => x.Frames).First());
        }
        return report;
    }

    public static string ExitName(ExitDef exit) => $"出口{exit.Side}{exit.Offset}→{exit.Target}";

    public static Vector2I CellOf(LevelRoom room, double x, double y)
    {
        Vector2 local = room.ToLocal(new Vector2((float)x, (float)y));
        return new Vector2I((int)Math.Floor(local.X / RoomGrid.Tile), (int)Math.Floor((local.Y + room.Grid.Shift) / RoomGrid.Tile));
    }

    // ---------- 入口 ----------

    public static List<Entrance> FindEntrances(MoveGraph graph, LevelRoom room)
    {
        var list = new List<Entrance>();
        RoomGrid grid = room.Grid;
        foreach (char marker in new[] { RoomGrid.DefaultSpawn, RoomGrid.SavePoint })
            if (grid.Find(marker) is { } cell)
                list.Add(SpawnEntrance(graph, room, marker.ToString(), cell));
        foreach (var (digit, id) in grid.Spawns)
            if (grid.Find(digit) is { } cell)
                list.Add(SpawnEntrance(graph, room, id, cell));

        SimConfig cfg = graph.Cfg;
        foreach (ExitDef exit in room.Def.Exits)
        {
            if (exit == null || exit.Kind != ExitKind.Edge) continue;
            var entrance = new Entrance { Name = ExitName(exit) };
            var feet = new List<Vector2>();
            Vector2 cs = graph.World.CellSize;
            if (exit.Side is ExitSide.Left or ExitSide.Right)
            {
                double x = exit.Side == ExitSide.Left ? room.Left + 1 + cfg.HalfW : room.Right - 1 - cfg.HalfW;
                double y0 = room.Top + exit.Offset * cs.Y, y1 = y0 + cs.Y;
                for (int r = 0; r < grid.Rows; r++)
                {
                    double top = graph.Geometry(room).RowTop(r);
                    if (top > y0 + cfg.BodySize.Y && top <= y1) feet.Add(new Vector2((float)x, (float)top));
                }
            }
            else if (exit.Side == ExitSide.Up)
            {
                double x0 = room.Left + exit.Offset * cs.X;
                for (int c = 0; c * RoomGrid.Tile < cs.X; c++)
                    feet.Add(new Vector2((float)(x0 + c * RoomGrid.Tile + RoomGrid.Tile / 2.0), (float)(room.Top + 1 + cfg.BodySize.Y)));
            }
            // 下方出口作为入口（从下面跳 / 爬上来）不在单房间检查里推算，由跨房间检查覆盖。
            GridWorld geo = graph.Geometry(room);
            foreach (Vector2 f in feet)
            {
                if (geo.Overlaps(f.X - cfg.HalfW, f.Y - cfg.BodySize.Y, f.X + cfg.HalfW, f.Y)) continue;
                foreach (int form in graph.Forms)
                {
                    Segment s = graph.SettleAt(room, f, form, out _);
                    if (s != null && !entrance.Nodes.Contains(s)) entrance.Nodes.Add(s);
                }
            }
            list.Add(entrance);
        }
        return list;
    }

    private static Entrance SpawnEntrance(MoveGraph graph, LevelRoom room, string name, Vector2I cell)
    {
        var e = new Entrance { Name = name };
        Vector2 feet = room.FeetGlobal(cell.X, cell.Y);
        Segment s = graph.Segments(room).Find(feet.X, feet.Y);
        if (s != null) e.Nodes.Add(s);
        return e;
    }

    // ---------- 出生点 ----------

    private static void CheckSpawns(SimConfig cfg, MoveGraph graph, LevelRoom room, RoomReport report)
    {
        RoomGrid grid = room.Grid;
        GridWorld geo = graph.Geometry(room);
        var markers = new List<(string name, Vector2I cell)>();
        foreach (char m in new[] { RoomGrid.DefaultSpawn, RoomGrid.SavePoint })
            if (grid.Find(m) is { } c) markers.Add((m.ToString(), c));
        foreach (var (digit, id) in grid.Spawns)
            if (grid.Find(digit) is { } c) markers.Add((id, c));
        foreach (var (name, cell) in markers)
        {
            Vector2 feet = room.FeetGlobal(cell.X, cell.Y);
            const float margin = 2f;
            bool fits = !geo.Overlaps(feet.X - cfg.HalfW - margin, feet.Y - cfg.BodySize.Y - margin, feet.X + cfg.HalfW + margin, feet.Y - 0.01);
            Segment s = graph.Segments(room).Find(feet.X, feet.Y);
            if (!fits || s == null)
            {
                report.Findings.Add(new Finding { Rule = "出生点", Room = room.Id, Cell = cell, Message = $"{name} 放不下 48×48 的身体（四周和头顶要留 {margin} 像素）或脚下没有地面" });
                continue;
            }
            bool leaves = graph.EdgesFrom(s).Any(e => e.Kind is EdgeKind.Move or EdgeKind.Exit or EdgeKind.Door) || s.MaxX - s.MinX >= RoomGrid.Tile;
            if (!leaves)
                report.Findings.Add(new Finding { Rule = "出生点", Room = room.Id, Cell = cell, Message = $"{name} 站得下，但走不开（段宽 {s.MaxX - s.MinX} 像素、没有任何能走的边）" });
        }
    }

    // ---------- 设计规则（计划 3.3） ----------

    public static List<Finding> Rules(SimConfig cfg, MoveGraph graph, LevelRoom room, RoomReport report, RuleLimits limits)
    {
        var findings = new List<Finding>();
        GridWorld geo = graph.Geometry(room);
        RoomGrid grid = room.Grid;
        List<Segment> segs = graph.Segments(room).All;

        // 1. 台阶：段的一端顶着墙，墙顶是能站的地面 → 台阶高度
        foreach (Segment s in segs)
            foreach (int side in new[] { -1, 1 })
            {
                if (side < 0 ? !s.WallLeft : !s.WallRight) continue;
                double edgeX = side < 0 ? s.MinX - cfg.HalfW - 0.5 : s.MaxX + cfg.HalfW + 0.5;
                int cw = geo.ColAt(edgeX);
                int rt = s.Row - 1;
                if (!geo.IsBlocking(cw, rt)) continue; // 挡住的是更高处的东西（低矮通道），见规则 4
                while (rt - 1 >= 0 && geo.IsBlocking(cw, rt - 1)) rt--;
                if (rt == 0) continue; // 一直到房间顶：是墙，不是台阶
                int height = s.Row - rt;
                if (height <= limits.MaxStepTiles) continue;
                Segment top = graph.Segments(room).All.FirstOrDefault(t => t.Row == rt && t.MinX - cfg.HalfW <= geo.ColLeft(cw) + RoomGrid.Tile && t.MaxX + cfg.HalfW >= geo.ColLeft(cw));
                if (top == null) continue;
                bool noClimb = Enumerable.Range(rt, height).Any(r => geo.KindAt(cw, r) == TileKind.NoClimb);
                findings.Add(new Finding
                {
                    Rule = "一跳高度", Room = room.Id, Cell = new Vector2I(cw, rt), IsError = noClimb,
                    Message = noClimb
                        ? $"台阶高 {height} 格（不攀墙最多 {limits.MaxStepTiles} 格），墙面有不可攀的 X：跳不上也爬不上"
                        : $"台阶高 {height} 格（不攀墙最多 {limits.MaxStepTiles} 格），要攀墙",
                });
            }
        foreach (MoveEdge e in segs.SelectMany(graph.EdgesFrom))
            if (e.Kind == EdgeKind.Move && !e.Climb && !e.WallJump && e.To.Room == room && e.From.FloorY - e.To.FloorY > limits.MaxStepTiles * RoomGrid.Tile)
                findings.Add(new Finding { Rule = "一跳高度", Room = room.Id, Cell = CellOf(room, e.End.X, e.To.FloorY - 1), Message = $"不攀墙跳上了 {(e.From.FloorY - e.To.FloorY) / RoomGrid.Tile:F1} 格：{e.Describe()}" });

        // 2. 同一高度的沟
        for (int r = 1; r < grid.Rows; r++)
        {
            int lastFloor = -1;
            for (int c = 0; c < grid.Cols; c++)
            {
                bool floor = geo.KindAt(c, r) != TileKind.Empty && !geo.IsBlocking(c, r - 1);
                bool open = geo.KindAt(c, r) == TileKind.Empty && !geo.IsBlocking(c, r - 1) && (r < 2 || !geo.IsBlocking(c, r - 2));
                if (floor)
                {
                    int width = c - lastFloor - 1;
                    if (lastFloor >= 0 && width > limits.MaxGapMain)
                    {
                        bool tooWide = width > limits.MaxGapWater;
                        findings.Add(new Finding
                        {
                            Rule = "沟宽", Room = room.Id, Cell = new Vector2I(lastFloor + 1, r),
                            Message = $"第 {r} 行有 {width} 格宽的沟（格 {lastFloor + 1}～{c - 1}）：超过主线 {limits.MaxGapMain} 格" + (tooWide ? $"，也超过水形态 {limits.MaxGapWater} 格" : "（仅可作为水形态的可选路线）"),
                        });
                    }
                    lastFloor = c;
                }
                else if (!open) lastFloor = -1;
            }
        }

        // 3. 起跳处头顶空间：入口→出口路线里，往上跳（含跳起攀墙）的起点
        var jumpStarts = new HashSet<(Segment, double)>();
        foreach (PairResult p in report.Pairs.Where(p => p.Reachable && p.Form == -1))
            foreach (MoveEdge e in p.Route)
                if (e.Kind == EdgeKind.Move && e.Action != null && e.Action.Kind == ActionKind.Jump && e.To.FloorY < e.From.FloorY)
                    jumpStarts.Add((e.From, e.StartX));
        foreach (var (s, x) in jumpStarts)
        {
            int c0 = geo.ColAt(x - cfg.HalfW + 0.01), c1 = geo.ColAt(x + cfg.HalfW - 0.01);
            int head = int.MaxValue;
            for (int c = c0; c <= c1; c++)
            {
                int n = 0;
                for (int r = s.Row - 1; r >= 0 && !geo.IsBlocking(c, r); r--) n++;
                head = Math.Min(head, n);
            }
            if (head < limits.MinHeadroomTiles)
                findings.Add(new Finding { Rule = "头顶空间", Room = room.Id, Cell = CellOf(room, x, s.FloorY - 1), Message = $"起跳处头顶只有 {head} 格（至少 {limits.MinHeadroomTiles} 格）：{s.Describe()} x={x - room.Left:F0}" });
        }

        // 4. 通道高度：段的一端被低矮的顶挡住
        foreach (Segment s in segs)
            foreach (int side in new[] { -1, 1 })
            {
                if (side < 0 ? !s.WallLeft : !s.WallRight) continue;
                int cw = geo.ColAt(side < 0 ? s.MinX - cfg.HalfW - 0.5 : s.MaxX + cfg.HalfW + 0.5);
                if (geo.IsBlocking(cw, s.Row - 1)) continue;
                int clear = 0;
                for (int r = s.Row - 1; r >= 0 && !geo.IsBlocking(cw, r); r--) clear++;
                if (clear < limits.MinPassageTiles && clear * RoomGrid.Tile < cfg.BodySize.Y + 1)
                    findings.Add(new Finding { Rule = "通道高度", Room = room.Id, Cell = new Vector2I(cw, s.Row - 1), Message = $"通道只有 {clear} 格高（至少 {limits.MinPassageTiles} 格），身体过不去" });
            }
        return findings;
    }
}
