using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Tools.LevelCheck;
using SonOfMud.World;

/// <summary>
/// 用静态模拟器检查地图二、地图三的真实房间（加上入口 S1_07 和出口那头的 S4_01），按当前主角身体尺寸：
/// 从 S1_07 的 P 出发，剧情标记按 flood_done（水闸）→ boss_dragon（龙蛇）→ districts_done（三个街区）推进，
/// 主线房间都能到、最后能走进 S4_01；没有死局；两种形态各自都能走完；设计规则（一跳高度、沟宽、头顶空间、通道高度）不违反；
/// 换房时身体不卡进墙里。结果写到 tests/output/levelcheck_map23_results.json。
/// </summary>
public partial class Map23CheckTest : LabSuiteBase
{
    protected override string ReportName => "levelcheck_map23_results";

    private static readonly string[] Rooms =
    {
        "S1_07", "S2_01", "S2_02", "S2_03", "S2_04", "S2_05", "S2_06",
        "S3_01", "S3_02", "S3_03", "S3_04", "S3_05", "S3_06", "S3_07", "S3_08", "S4_01",
    };

    protected override Task RunChecks()
    {
        SimConfig cfg = SimConfig.Load();
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        var world = new LevelWorld(LevelWorld.DefaultCellSize);
        Vector2I cell = (Vector2I)LevelWorld.DefaultCellSize;
        var loadErrors = new List<string>();
        foreach (string id in Rooms)
        {
            RoomDef def = layout.Find(id);
            RoomGrid grid = def == null ? null : RoomGrid.Load(def, cell, out string error);
            if (grid == null) loadErrors.Add(id);
            else world.Add(def, grid);
        }
        Check(loadErrors.Count == 0, "地图二、三的房间文字图都能读到" + (loadErrors.Count > 0 ? "：" + string.Join("、", loadErrors) : ""));
        if (loadErrors.Count > 0) return Task.CompletedTask;

        var config = new WorldCheckConfig
        {
            StartRoom = "S1_07",
            MainLine = Rooms.ToList(),
            FinalGoal = "S4_01",
            Story = new List<StoryStep>
            {
                new("flood_done", "S2_03"),
                new("boss_dragon", "S2_04"),
                new("districts_done", "S3_06"),
            },
        };
        WorldReport report = WorldCheck.Run(cfg, world, config);
        GD.Print("    " + report.Summary().Replace("\n", "\n    "));
        Check(report.Ok, $"地图二、三全图检查没有问题（身体 {cfg.BodySize}：主线都能到、没有死局、两种形态都能走完、换房不卡墙、设计规则不违反）" +
                         (report.Ok ? "" : "：" + string.Join("；", report.Errors.Take(10))));
        Check(Rooms.All(report.MainLineReached.Contains), $"主线房间全部能到（{string.Join(",", report.MainLineReached.OrderBy(r => r))}）");
        return Task.CompletedTask;
    }
}
