using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.World;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>剧情推进的一步：到达 Room 之后 Flag 成立（R 出现、条件门打开）。</summary>
public sealed class StoryStep
{
    public string Flag = "";
    public string Room = "";

    public StoryStep(string flag, string room)
    {
        Flag = flag;
        Room = room;
    }
}

public sealed class WorldCheckConfig
{
    public string StartRoom = "S1_01";
    public List<StoryStep> Story = new();
    /// <summary>主线必须能到的房间；null 表示房间表里全部。</summary>
    public List<string> MainLine;
    /// <summary>最后一个阶段“继续往前”的目标房间：null 表示主线的最后一个房间；空字符串表示没有（只认存档点）。</summary>
    public string FinalGoal;
    /// <summary>两种形态各自都要能走完主线（计划 3.3“主线两种形态都要能过”）。</summary>
    public bool RequireEachForm = true;
    /// <summary>门的位置（全局脚底坐标）。默认用文字图里名为 DoorTo&lt;目标&gt; 的锚点。</summary>
    public Func<LevelRoom, string, Vector2?> DoorFeet;
    public IReadOnlyList<MoveAction> Actions;
}

public sealed class StageInfo
{
    public int Index;
    public List<string> Flags = new();
    public HashSet<string> RoomsReached = new();
    public int NodesReached;
    public List<string> DeadEnds = new();
}

public sealed class WorldReport
{
    public List<string> Errors = new();
    public List<string> Notes = new();
    public List<StageInfo> Stages = new();
    public HashSet<string> MainLineReached = new();
    public List<string> ClimbShortcuts = new();
    public List<string> Problems = new();
    public bool Ok => Errors.Count == 0;

    public string Summary() => string.Join("\n", Errors.Select(e => "[问题] " + e).Concat(Notes.Select(n => "[说明] " + n)).Concat(ClimbShortcuts.Select(c => "[攀墙捷径] " + c)));
}

/// <summary>
/// 跨房间检查：从起始房间的 P 出发，按剧情标记顺序推进（标记成立后 R 出现、条件门打开），
/// 每一阶段算出能到的所有位置；要求剧情标记所在房间能到、主线每个房间都能到、
/// 每个能到的位置都能到存档点或下一个剧情房间（没有死局）。房间之间按全局坐标连续模拟（见 LevelWorld）。
/// </summary>
public static class WorldCheck
{
    public static WorldReport Run(SimConfig cfg, LevelWorld world, WorldCheckConfig config)
    {
        var report = new WorldReport();
        LevelRoom start = world.Find(config.StartRoom);
        if (start == null || start.Grid.Find(RoomGrid.DefaultSpawn) is not { } pCell)
        {
            report.Errors.Add($"起始房间 {config.StartRoom} 不存在或没有 P");
            return report;
        }
        List<string> mainLine = config.MainLine ?? world.Rooms.Keys.ToList();

        var formsToCheck = new List<int[]> { new[] { SimConfig.Water, SimConfig.Mud } };
        if (config.RequireEachForm)
        {
            formsToCheck.Add(new[] { SimConfig.Water });
            formsToCheck.Add(new[] { SimConfig.Mud });
        }
        foreach (int[] forms in formsToCheck)
        {
            string tag = forms.Length == 2 ? "" : forms[0] == SimConfig.Water ? "［只用水］" : "［只用泥］";
            RunForms(cfg, world, config, start, pCell, mainLine, forms, tag, report, forms.Length == 2);
        }
        return report;
    }

    private static void RunForms(SimConfig cfg, LevelWorld world, WorldCheckConfig config, LevelRoom start, Vector2I pCell,
        List<string> mainLine, int[] forms, string tag, WorldReport report, bool primary)
    {
        var state = new LevelState();
        var carry = new List<(LevelRoom room, Vector2 feet)> { (start, start.FeetGlobal(pCell.X, pCell.Y)) };
        MoveGraph graph = null;
        MoveGraph.Search search = null;
        for (int k = 0; k <= config.Story.Count; k++)
        {
            if (k > 0) state.Flags.Add(config.Story[k - 1].Flag);
            while (true)
            {
                graph = new MoveGraph(cfg, world, state) { Forms = forms, ExtraEdges = (g, room) => DoorEdges(g, room, config) };
                if (config.Actions != null) graph.Actions = config.Actions;
                var starts = new List<Segment>();
                foreach (var (room, feet) in carry)
                {
                    Segment s = graph.Segments(room).Find(feet.X, feet.Y) ?? graph.SettleAt(room, feet, forms[0], out _);
                    if (s != null) starts.Add(s);
                }
                search = graph.Explore(starts);
                var fresh = search.Breaks.Where(b => !state.BrokenIn(b.From.Room.Id).Contains(b.BreakTile)).ToList();
                if (fresh.Count == 0) break;
                foreach (MoveEdge b in fresh) state.BrokenIn(b.From.Room.Id).Add(b.BreakTile);
            }

            var stage = new StageInfo { Index = k, Flags = state.Flags.ToList(), NodesReached = search.Reached.Count };
            stage.RoomsReached.UnionWith(search.Reached.Select(s => s.Room.Id));
            if (primary)
            {
                report.Stages.Add(stage);
                foreach (string p in graph.Problems) if (!report.Problems.Contains(p)) report.Problems.Add(p);
            }

            // 死局：每个能到的位置都要能到存档点或下一个剧情房间（最后一个阶段：存档点或主线最后一个房间）
            string next = k < config.Story.Count ? config.Story[k].Room : config.FinalGoal ?? mainLine.LastOrDefault();
            HashSet<Segment> good = graph.CanReach(search.Reached, s => s.Room.Id == next || IsSave(cfg, s));
            foreach (Segment s in search.Reached.Where(s => !good.Contains(s)).OrderBy(s => s.Room.Id).ThenBy(s => s.Row))
            {
                string msg = $"{tag}阶段 {k}（标记：{string.Join(",", state.Flags)}）死局：{s.Describe()} 能到，但到不了存档点" + (string.IsNullOrEmpty(next) ? "" : $"，也到不了 {next}");
                stage.DeadEnds.Add(msg);
                report.Errors.Add(msg);
            }

            if (k < config.Story.Count && !stage.RoomsReached.Contains(config.Story[k].Room))
                report.Errors.Add($"{tag}阶段 {k}：剧情标记 {config.Story[k].Flag} 所在的 {config.Story[k].Room} 到不了");

            carry = search.Reached.Select(s => (s.Room, new Vector2((float)s.MidX, (float)s.FloorY))).ToList();
        }

        foreach (string id in mainLine)
        {
            bool reached = search.Reached.Any(s => s.Room.Id == id);
            if (primary && reached) report.MainLineReached.Add(id);
            if (!reached) report.Errors.Add($"{tag}主线房间 {id} 到不了");
        }
        if (primary)
        {
            foreach (string p in graph.Problems) if (!report.Problems.Contains(p)) report.Problems.Add(p);
            foreach (string p in report.Problems) report.Errors.Add("模拟发现：" + p);
            // 靠攀墙走的捷径（最后阶段）：攀墙边的终点，不攀墙从起点到不了
            var moves = search.Reached.SelectMany(graph.EdgesFrom).Where(e => e.Kind == EdgeKind.Move && (e.Climb || e.WallJump)).ToList();
            foreach (var group in moves.GroupBy(e => (e.From, e.To)))
            {
                MoveGraph.Search noClimb = graph.Explore(new[] { group.Key.From }, m => !m.Climb && !m.WallJump);
                if (!noClimb.Reached.Contains(group.Key.To)) report.ClimbShortcuts.Add(group.OrderBy(x => x.Frames).First().Describe());
            }
        }
    }

    private static bool IsSave(SimConfig cfg, Segment s)
    {
        if (s.Room.Grid.Find(RoomGrid.SavePoint) is not { } cell) return false;
        Vector2 feet = s.Room.FeetGlobal(cell.X, cell.Y);
        return s.Contains(feet.X, feet.Y);
    }

    /// <summary>门：站在门所在的段上按交互键，到目标房间的 From_本房间 出生点（没有就用目标房间里通回来的门、再没有就用 P）。</summary>
    private static IEnumerable<MoveEdge> DoorEdges(MoveGraph graph, LevelRoom room, WorldCheckConfig config)
    {
        foreach (ExitDef exit in room.Def.Exits)
        {
            if (exit == null || exit.Kind != ExitKind.Door) continue;
            LevelRoom target = graph.World.Find(exit.Target);
            if (target == null) continue;
            if (!graph.State.FlagsMet(exit.Requirement)) continue;
            Vector2? doorFeet = config.DoorFeet?.Invoke(room, exit.Target) ?? AnchorFeet(room, "DoorTo" + exit.Target);
            if (doorFeet is not { } feet)
            {
                graph.Problems.Add($"{room.Id} 的门（→{exit.Target}）在文字图里没有锚点 DoorTo{exit.Target}，无法确定位置");
                continue;
            }
            Segment from = graph.Segments(room).All.FirstOrDefault(s => Math.Abs(s.FloorY - feet.Y) <= 2 && feet.X >= s.MinX - 32 && feet.X <= s.MaxX + 32);
            if (from == null)
            {
                graph.Problems.Add($"{room.Id} 的门（→{exit.Target}）脚下没有能站的地面");
                continue;
            }
            Vector2? arrival = SpawnFeet(target, ExitDef.ArrivalSpawn(room.Id)) ?? AnchorFeet(target, "DoorTo" + room.Id) ??
                               (target.Grid.Find(RoomGrid.DefaultSpawn) is { } p ? target.FeetGlobal(p.X, p.Y) : null);
            if (arrival is not { } arrive) continue;
            foreach (int form in graph.Forms)
            {
                Segment to = graph.SettleAt(target, arrive, form, out _);
                if (to == null)
                {
                    graph.Problems.Add($"{target.Id} 的到达点（从 {room.Id} 过门）放不下或落不到地面");
                    continue;
                }
                yield return new MoveEdge { From = from, StartX = Math.Clamp(feet.X, from.MinX, from.MaxX), Form = form, Kind = EdgeKind.Door, To = to, Exit = exit, End = arrive, Robust = true };
            }
        }
    }

    private static Vector2? AnchorFeet(LevelRoom room, string nodeName)
    {
        foreach (var (letter, node) in room.Grid.Anchors)
            if (node == nodeName && room.Grid.Find(letter) is { } c) return room.FeetGlobal(c.X, c.Y);
        return null;
    }

    private static Vector2? SpawnFeet(LevelRoom room, string spawnId)
    {
        foreach (var (digit, id) in room.Grid.Spawns)
            if (id == spawnId && room.Grid.Find(digit) is { } c) return room.FeetGlobal(c.X, c.Y);
        return null;
    }
}
