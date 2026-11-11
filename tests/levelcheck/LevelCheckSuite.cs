using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using SonOfMud.Core;
using SonOfMud.Tools.LevelCheck;
using SonOfMud.World;

/// <summary>
/// 静态可达性检查（关卡几何计划 v3 第 5.3 节）在 fixtures 上的测试：能走通的、有死局的、有跳不上的、有 X 墙的、
/// 有 R 的、有 B 的、出生点放不下的、违反设计规则的、两个房间开口对齐 / 不对齐的、按剧情标记推进的。
/// 每个 fixture 都断言检查结果（能不能到、问题报在哪个房间哪一格）。不需要真实玩家。
/// 结果写到 tests/output/levelcheck_results.json。
/// </summary>
public partial class LevelCheckSuite : LabSuiteBase
{
    private const string Dir = "res://tests/levelcheck/fixtures/";
    private static readonly Vector2I CellSize = (Vector2I)LevelWorld.DefaultCellSize;

    protected override string ReportName => "levelcheck_results";

    private SimConfig _cfg;

    protected override Task RunChecks()
    {
        _cfg = SimConfig.Load();
        ulong t0 = Time.GetTicksMsec();
        WalkOk();
        DeadEnd();
        TooHigh();
        Conditional();
        Breakable();
        SpawnBad();
        RulesFixture();
        TwoRooms();
        Story();
        WorldDeadEnd();
        Metric("耗时毫秒", Time.GetTicksMsec() - t0);
        return Task.CompletedTask;
    }

    // ---------- 工具 ----------

    private static ExitDef Exit(ExitSide side, int offset, string target, params string[] flags) => new()
    {
        Side = side, Offset = offset, Target = target, Kind = ExitKind.Edge,
        Requirement = flags.Length == 0 ? null : new GateRequirement { Flags = new Array<StringName>(flags.Select(f => (StringName)f)) },
    };

    private static RoomDef Def(string id, Vector2I origin, Vector2I size, bool save, params ExitDef[] exits) => new()
    {
        Id = id, Origin = origin, Size = size, HasSavePoint = save, Exits = new Array<ExitDef>(exits),
    };

    private RoomGrid Load(string file, RoomDef def)
    {
        RoomGrid grid = RoomGrid.Parse(FileAccess.GetFileAsString(Dir + file), def, CellSize, out string error);
        Check(grid != null, $"{file} 解析通过 {error}");
        return grid;
    }

    private static void Replace(RoomGrid grid, char from, char to)
    {
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
                if (grid.At(c, r) == from) grid.Set(c, r, to);
    }

    private void Dump(RoomReport report)
    {
        foreach (string line in report.Summary().Split('\n')) GD.Print("    " + line);
    }

    private const string L = "出口Left0→OUT_L";
    private const string R = "出口Right0→OUT_R";

    private static RoomDef SingleDef(string id, Vector2I size, bool save = false) =>
        Def(id, Vector2I.Zero, size, save, Exit(ExitSide.Left, 0, "OUT_L"), Exit(ExitSide.Right, 0, "OUT_R"));

    // ---------- 单房间 ----------

    private void WalkOk()
    {
        RoomDef def = SingleDef("FX_WALK", new Vector2I(2, 1));
        RoomGrid grid = Load("walk_ok.txt", def);
        if (grid == null) return;
        RoomReport report = RoomCheck.Run(_cfg, def, grid, false);
        Dump(report);
        foreach (int form in new[] { -1, SimConfig.Water, SimConfig.Mud })
        {
            string f = form switch { -1 => "可切形态", 0 => "只用水", _ => "只用泥" };
            Check(report.Reachable(L, R, form) && report.Reachable(R, L, form), $"能走通（{f}）：左出口 ↔ 右出口 双向可达");
        }
        Check(report.Reachable("P", R) && report.Reachable(L, "P"), "能走通：出生点 P 和出口互相可达");
        Check(report.DeadEnds.Count == 0 && !report.Errors.Any(), "能走通：没有死局、没有规则问题：" + string.Join("；", report.Errors));
        List<MoveEdge> route = report.Pair(L, R)?.Route ?? new List<MoveEdge>();
        Check(route.Count >= 2 && route.Last().Kind == EdgeKind.Exit && RoutePlan.From(route).All(s => s.Script != null),
            $"能走通：给出路线（{route.Count} 步）：" + string.Join(" → ", route.Select(e => e.Describe())));
        Check(report.ClimbShortcuts.Count == 0, "能走通：1～3 格台阶不需要攀墙（没有攀墙捷径）");

        // 反向：把 3 格台阶抬高 1 格（变成 4 格，墙面可攀）→ 规则报出房间和位置，主线要攀墙
        RoomGrid raised = Load("walk_ok.txt", def);
        if (raised == null) return;
        for (int c = 18; c <= 21; c++) raised.Set(c, 5, RoomGrid.Solid);
        RoomReport r2 = RoomCheck.Run(_cfg, def, raised, false);
        Finding step = r2.Findings.FirstOrDefault(f => f.Rule == "一跳高度");
        // 抬高后台阶顶在第 5 行；左边是 2 格台阶（7→5，不超），右边从地面（第 9 行）上来是 4 格，墙在第 21 列
        Check(step != null && step.Room == "FX_WALK" && step.Cell == new Vector2I(21, 5) && !step.IsError,
            $"台阶抬高 1 格：报出“一跳高度”在 FX_WALK 格(21,5)（墙可攀，记为提示）：{step}");
        Check(r2.Reachable(L, R) && r2.ClimbShortcuts.Count > 0, "台阶抬高 1 格：靠攀墙仍能过去，并列入攀墙捷径：" + string.Join("；", r2.ClimbShortcuts.Select(e => e.Describe())));
    }

    private void DeadEnd()
    {
        RoomDef def = SingleDef("FX_DEAD", new Vector2I(1, 2));
        RoomGrid grid = Load("dead_end.txt", def);
        if (grid == null) return;
        RoomReport report = RoomCheck.Run(_cfg, def, grid, false);
        Dump(report);
        Check(report.Reachable(L, R) && report.Reachable(R, L), "有死局：坑可以跳过去，左右出口仍然互通");
        Check(report.DeadEnds.Count == 1 && report.DeadEnds[0].Row == 19, $"有死局：只有坑底（第 19 行）是死局：{string.Join("；", report.DeadEnds.Select(d => d.Describe()))}");
        Finding f = report.Findings.FirstOrDefault(x => x.Rule == "死局");
        Check(f != null && f.Room == "FX_DEAD" && f.Cell.Y == 18 && f.Cell.X >= 7 && f.Cell.X <= 10, $"有死局：报出房间和格子：{f}");
        Check(report.OneWayDrops.Any(e => e.To.Row == 19), "有死局：掉进坑是单程下落");
    }

    private void TooHigh()
    {
        RoomDef def = SingleDef("FX_HIGH", new Vector2I(1, 1));
        RoomGrid grid = Load("too_high.txt", def);
        if (grid == null) return;
        RoomReport report = RoomCheck.Run(_cfg, def, grid, false);
        Dump(report);
        Check(!report.Reachable(L, R) && !report.Reachable("P", R), "跳不上：4 格高、X 面的台阶，从左边到不了右上出口");
        Finding f = report.Findings.FirstOrDefault(x => x.Rule == "一跳高度");
        Check(f != null && f.IsError && f.Room == "FX_HIGH" && f.Cell == new Vector2I(10, 5), $"跳不上：报出“一跳高度”问题在 FX_HIGH 格(10,5)：{f}");
        Check(report.Reachable(R, L), "跳不上：反方向（从高处下来）能走");

        // 对照：同样的地形，X 换成 #（可攀）→ 攀墙上得去，并列为攀墙捷径
        RoomGrid climbable = Load("too_high.txt", def);
        if (climbable == null) return;
        Replace(climbable, RoomGrid.NoClimb, RoomGrid.Solid);
        RoomReport r2 = RoomCheck.Run(_cfg, def, climbable, false);
        Check(r2.Reachable(L, R, SimConfig.Water) && r2.Reachable(L, R, SimConfig.Mud), "X 墙对照：换成 # 后水、泥都能攀墙上去");
        Check(r2.Pair(L, R)?.Route?.Any(e => e.Climb) == true, "X 墙对照：路线里用到了攀墙");
        Check(r2.ClimbShortcuts.Count > 0 && !r2.Errors.Any(e => e.Rule == "一跳高度"), "X 墙对照：列出攀墙捷径，4 格台阶只是提示：" + string.Join("；", r2.ClimbShortcuts.Select(e => e.Describe())));
    }

    private void Conditional()
    {
        RoomDef def = Def("FX_COND", Vector2I.Zero, Vector2I.One, false, Exit(ExitSide.Left, 0, "OUT_L"), Exit(ExitSide.Right, 0, "OUT_R"));
        RoomGrid grid = Load("cond_r.txt", def);
        if (grid == null) return;
        RoomReport off = RoomCheck.Run(_cfg, def, grid, false);
        RoomReport on = RoomCheck.Run(_cfg, def, grid, true);
        Dump(off);
        Dump(on);
        Check(!off.Reachable(L, R), "有 R：条件不成立时到不了右上出口");
        Check(on.Reachable(L, R, SimConfig.Water) && on.Reachable(L, R, SimConfig.Mud), "有 R：条件成立后水、泥都能到右上出口");
        Entrance left = on.Entrances.First(e => e.Name == L);
        Check(on.Graph.Explore(left.Nodes, e => !e.Climb && !e.WallJump).Exits.ContainsKey("exit:FX_COND:Right0"),
            "有 R：不攀墙、只沿台阶跳也能上去");
        Entrance leftOff = off.Entrances.First(e => e.Name == L);
        Check(!off.Graph.Explore(leftOff.Nodes).Exits.ContainsKey("exit:FX_COND:Right0"), "有 R：条件不成立时连攀墙也上不去（X 面）");
    }

    private void Breakable()
    {
        RoomDef def = SingleDef("FX_BREAK", new Vector2I(1, 1));
        RoomGrid grid = Load("breakable.txt", def);
        if (grid == null) return;
        RoomReport report = RoomCheck.Run(_cfg, def, grid, false);
        Dump(report);
        Check(report.Reachable(L, R) && report.Reachable(R, L), "有 B：打碎之后左右互通");
        Check(report.BrokenTiles.Count > 0 && report.BrokenTiles.All(t => t.X is 8 or 9 && t.Y is >= 6 and <= 8),
            "有 B：只打碎了挡路的几块（第 8、9 列，身体高度范围内）：" + string.Join(" ", report.BrokenTiles));

        RoomGrid solid = Load("breakable.txt", def);
        if (solid == null) return;
        Replace(solid, RoomGrid.Breakable, RoomGrid.NoClimb);
        RoomReport r2 = RoomCheck.Run(_cfg, def, solid, false);
        Check(!r2.Reachable(L, R) && r2.BrokenTiles.Count == 0, "B 对照：换成 X 后过不去");
    }

    private void SpawnBad()
    {
        RoomDef def = SingleDef("FX_SPAWN", new Vector2I(1, 1));
        RoomGrid grid = Load("spawn_bad.txt", def);
        if (grid == null) return;
        RoomReport report = RoomCheck.Run(_cfg, def, grid, false);
        Finding f = report.Findings.FirstOrDefault(x => x.Rule == "出生点");
        Check(f != null && f.Cell == new Vector2I(5, 8) && f.Room == "FX_SPAWN", $"出生点：P 头顶只有 1 格，报出放不下：{f}");
        RoomDef walkDef = SingleDef("FX_WALK", new Vector2I(2, 1));
        RoomGrid walk = Load("walk_ok.txt", walkDef);
        RoomReport ok = walk != null ? RoomCheck.Run(_cfg, walkDef, walk, false) : null;
        Check(ok != null && !ok.Findings.Any(x => x.Rule == "出生点"), "出生点对照：walk_ok 的 P 放得下、能离开");
    }

    private void RulesFixture()
    {
        RoomDef def = SingleDef("FX_RULES", new Vector2I(2, 1));
        RoomGrid grid = Load("rules.txt", def);
        if (grid == null) return;
        RoomReport report = RoomCheck.Run(_cfg, def, grid, false);
        Dump(report);
        Finding gap = report.Findings.FirstOrDefault(x => x.Rule == "沟宽");
        Check(gap != null && gap.Cell == new Vector2I(16, 9) && gap.Message.Contains("6 格"), $"规则：6 格宽的沟报在 FX_RULES 格(16,9)：{gap}");
        Finding head = report.Findings.FirstOrDefault(x => x.Rule == "头顶空间");
        Check(head != null && head.Cell.Y == 8 && head.Cell.X >= 1 && head.Cell.X <= 8 && head.Message.Contains("3 格"),
            $"规则：低顶下起跳，头顶空间不足报在第 8 行、低顶下面：{head}");
        Finding low = report.Findings.FirstOrDefault(x => x.Rule == "通道高度");
        Check(low != null && low.Cell == new Vector2I(30, 8), $"规则：1 格高的通道报在 FX_RULES 格(30,8)：{low}");
        Check(report.Reachable(L, "P"), "规则 fixture 本身左出口能走到 P（路线经过低顶起跳和沟）");
    }

    // ---------- 跨房间 ----------

    private LevelWorld World(params (RoomDef def, string file)[] rooms)
    {
        var world = new LevelWorld(LevelWorld.DefaultCellSize);
        foreach (var (def, file) in rooms)
        {
            RoomGrid grid = Load(file, def);
            if (grid != null) world.Add(def, grid);
        }
        return world;
    }

    private void TwoRooms()
    {
        RoomDef a = Def("FX_A", Vector2I.Zero, Vector2I.One, true, Exit(ExitSide.Right, 0, "FX_B"));
        RoomDef b = Def("FX_B", new Vector2I(1, 0), Vector2I.One, false, Exit(ExitSide.Left, 0, "FX_A"));
        var config = new WorldCheckConfig { StartRoom = "FX_A", MainLine = new List<string> { "FX_A", "FX_B" } };

        WorldReport aligned = WorldCheck.Run(_cfg, World((a, "two_a.txt"), (b, "two_b_aligned.txt")), config);
        GD.Print("    " + aligned.Summary().Replace("\n", "\n    "));
        Check(aligned.Ok && aligned.MainLineReached.Contains("FX_B"), "两房间开口对齐：按全局坐标走过边界，B 能到，没有问题：" + string.Join("；", aligned.Errors));

        WorldReport raised = WorldCheck.Run(_cfg, World((a, "two_a.txt"), (b, "two_b_raised.txt")), config);
        GD.Print("    " + raised.Summary().Replace("\n", "\n    "));
        Check(!raised.Ok && raised.Errors.Any(e => e.Contains("主线房间 FX_B 到不了")), "两房间开口不对齐（B 高 5 格）：报出 B 到不了");
        Check(raised.Errors.Any(e => e.Contains("换到 FX_B 时身体卡进墙里")), "两房间开口不对齐：报出换房时卡进 B 的墙里（含坐标）：" + raised.Errors.FirstOrDefault(e => e.Contains("卡进墙里")));
    }

    private (LevelWorld world, WorldCheckConfig config) StoryWorld()
    {
        RoomDef w1 = Def("FX_W1", Vector2I.Zero, Vector2I.One, true, Exit(ExitSide.Right, 0, "FX_W2"));
        RoomDef w2 = Def("FX_W2", new Vector2I(1, 0), Vector2I.One, false, Exit(ExitSide.Left, 0, "FX_W1"), Exit(ExitSide.Right, 0, "FX_W3", "flag_a"));
        RoomDef w3 = Def("FX_W3", new Vector2I(2, 0), Vector2I.One, false, Exit(ExitSide.Left, 0, "FX_W2", "flag_a"));
        LevelWorld world = World((w1, "story_w1.txt"), (w2, "story_w2.txt"), (w3, "story_w3.txt"));
        var config = new WorldCheckConfig
        {
            StartRoom = "FX_W1",
            MainLine = new List<string> { "FX_W1", "FX_W2", "FX_W3" },
            Story = new List<StoryStep> { new("flag_a", "FX_W2") },
        };
        return (world, config);
    }

    private void Story()
    {
        var (world, config) = StoryWorld();
        WorldReport report = WorldCheck.Run(_cfg, world, config);
        GD.Print("    " + report.Summary().Replace("\n", "\n    "));
        Check(report.Ok, "剧情推进：按标记顺序推进后主线全部能到、没有死局：" + string.Join("；", report.Errors));
        Check(report.Stages.Count == 2 && !report.Stages[0].RoomsReached.Contains("FX_W3") && report.Stages[1].RoomsReached.Contains("FX_W3"),
            "剧情推进：标记成立前到不了 W3，成立后（R 出现、条件门打开）能到");

        var (world2, config2) = StoryWorld();
        config2.Story.Clear();
        WorldReport noFlag = WorldCheck.Run(_cfg, world2, config2);
        Check(!noFlag.Ok && noFlag.Errors.Any(e => e.Contains("主线房间 FX_W3 到不了")), "剧情推进对照：不给标记时报出 W3 到不了");
    }

    private void WorldDeadEnd()
    {
        RoomDef w1 = Def("FX_W1", Vector2I.Zero, Vector2I.One, true, Exit(ExitSide.Right, 0, "FX_DEAD"));
        RoomDef dead = Def("FX_DEAD", new Vector2I(1, 0), new Vector2I(1, 2), false, Exit(ExitSide.Left, 0, "FX_W1"), Exit(ExitSide.Right, 0, "OUT_R"));
        LevelWorld world = World((w1, "story_w1.txt"), (dead, "dead_end.txt"));
        var config = new WorldCheckConfig { StartRoom = "FX_W1", MainLine = new List<string> { "FX_W1", "FX_DEAD" }, FinalGoal = "" };
        WorldReport report = WorldCheck.Run(_cfg, world, config);
        GD.Print("    " + report.Summary().Replace("\n", "\n    "));
        Check(report.MainLineReached.Contains("FX_DEAD"), "跨房间死局：从 W1 走进 FX_DEAD");
        Check(report.Errors.Any(e => e.Contains("死局") && e.Contains("FX_DEAD 第19行地面")), "跨房间死局：坑底到不了 W1 的存档点，报出死局：" + report.Errors.FirstOrDefault(e => e.Contains("死局")));
        Check(!report.Errors.Any(e => e.Contains("死局") && e.Contains("FX_W1")), "跨房间死局：W1 里的位置都能回存档点");
    }
}
