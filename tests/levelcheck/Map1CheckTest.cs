using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Tools.LevelCheck;
using SonOfMud.World;

/// <summary>
/// 关卡几何计划 v3 第 5.3 节：用静态模拟器检查地图 1 的真实房间（assets/data/world/rooms/S1_*.txt，加上出口那头的 S2_01）。
/// 从 S1_01 的 P 出发，剧情标记按 boss_keeper 推进：主线 S1_01→S1_07 每个房间都要能到、最后能走进 S2_01；回溯房间 S1_08 能到；
/// 能到达的每个位置都能回到存档点或继续往前（没有死局）；两种形态各自都能走完；设计规则（一跳高度、沟宽、头顶空间、通道高度）不违反；
/// 打赢守坑人之前到不了 S1_06。结果写到 tests/output/levelcheck_map1_results.json，攀墙捷径清单打印在日志里供人判断。
/// </summary>
public partial class Map1CheckTest : LabSuiteBase
{
    protected override string ReportName => "levelcheck_map1_results";

    private static readonly string[] Rooms = { "S1_01", "S1_02", "S1_03", "S1_04", "S1_05", "S1_06", "S1_07", "S1_08", "S2_01" };
    private static readonly List<string> MainLine = new() { "S1_01", "S1_02", "S1_03", "S1_04", "S1_05", "S1_06", "S1_07", "S1_08", "S2_01" };

    protected override Task RunChecks()
    {
        ulong t0 = Time.GetTicksMsec();
        SimConfig cfg = SimConfig.Load();
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        var world = new LevelWorld(LevelWorld.DefaultCellSize);
        Vector2I cell = (Vector2I)LevelWorld.DefaultCellSize;
        var loadErrors = new List<string>();
        foreach (string id in Rooms)
        {
            RoomDef def = layout.Rooms.First(r => r.Id == id);
            RoomGrid grid = RoomGrid.Load(def, cell, out string error);
            if (grid == null) loadErrors.Add($"{id}：{error}");
            else world.Add(def, grid);
        }
        Check(loadErrors.Count == 0, "地图 1 九个房间的文字图都能读到" + (loadErrors.Count > 0 ? "：" + string.Join("；", loadErrors) : ""));
        if (loadErrors.Count > 0) return Task.CompletedTask;

        var config = new WorldCheckConfig
        {
            StartRoom = "S1_01",
            MainLine = MainLine,
            FinalGoal = "S2_01",
            Story = new List<StoryStep> { new("boss_keeper", "S1_05") },
        };
        WorldReport report = WorldCheck.Run(cfg, world, config);
        GD.Print("    " + report.Summary().Replace("\n", "\n    "));
        Check(report.Ok, "地图 1 全图检查没有问题（主线都能到、没有死局、两种形态都能走完、设计规则不违反）" +
                         (report.Ok ? "" : "：" + string.Join("；", report.Errors.Take(8))));
        Check(MainLine.All(report.MainLineReached.Contains), $"主线房间全部能到（{string.Join(",", report.MainLineReached.OrderBy(r => r))}）");
        Check(report.Stages.Count == 2 && !report.Stages[0].RoomsReached.Contains("S1_06") && report.Stages[1].RoomsReached.Contains("S1_06"),
            "打赢守坑人（boss_keeper）之前到不了 S1_06，之后能到（条件台阶 R 和出口门）");
        Check(report.Stages.Count == 2 && report.Stages[0].RoomsReached.Contains("S1_08"), "回溯房间 S1_08 在打 Boss 之前就能到（攀墙开局就有）");
        Metric("攀墙捷径条数", report.ClimbShortcuts.Count);
        Metric("耗时毫秒", Time.GetTicksMsec() - t0);
        return Task.CompletedTask;
    }
}
