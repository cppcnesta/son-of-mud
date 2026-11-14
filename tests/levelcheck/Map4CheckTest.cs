using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Tools;
using SonOfMud.Tools.LevelCheck;
using SonOfMud.World;

/// <summary>
/// 地图四“七道门”的静态检查（计划_地图四关卡按原画重做）：S4_01 → S4_02 七道门 → S4_03 回廊 → S4_04 祭坛 → S4_05 终末之室。
/// 剧情按七块泥板推进（站到第 k 块泥板旁边才算读完，tablet.gate_k 成立，第 k 道活板门打开）：
/// ① 每一阶段能走到下一块泥板、走不到再下一块（门块在模拟器里是真的碰撞，关着就挡路）；
/// ② 七块读完之前到不了回廊，读完后能走到终末之室；
/// ③ 不给攀墙也能走完（⑦ 回到 ⑥ 层出口平台只用跳和单向平台）；
/// ④ 把 S4_02 场景导出成文字图再检查一遍，结论不变。
/// </summary>
public partial class Map4CheckTest : LabSuiteBase
{
    protected override string ReportName => "levelcheck_map4_results";

    private static readonly string[] Rooms = { "S4_01", "S4_02", "S4_03", "S4_04", "S4_05" };
    private const int Gates = 7;

    protected override Task RunChecks()
    {
        ulong t0 = Time.GetTicksMsec();
        SimConfig cfg = SimConfig.Load();
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        Vector2I cell = (Vector2I)LevelWorld.DefaultCellSize;
        var grids = new Dictionary<string, RoomGrid>();
        var loadErrors = new List<string>();
        foreach (string id in Rooms)
        {
            RoomGrid grid = RoomGrid.Load(layout.Find(id), cell, out string error);
            if (grid == null) loadErrors.Add($"{id}：{error}");
            else grids[id] = grid;
        }
        Check(loadErrors.Count == 0, "地图四五个房间的文字图都能读到" + (loadErrors.Count > 0 ? "：" + string.Join("；", loadErrors) : ""));
        if (loadErrors.Count > 0) return Task.CompletedTask;

        RoomGrid s402 = grids["S4_02"];
        Check(s402.Gates.Count == 6 && Enumerable.Range(1, 6).All(k => s402.Gates.Values.Count(g => g.Flag == $"tablet.gate_{k}") == 1)
              && Enumerable.Range(1, Gates).All(k => s402.Anchors.Values.Contains($"Tablet_gate_{k}")),
            $"S4_02 有六道活板门（tablet.gate_1–6 各一道）和七块泥板的锚点（门块 {string.Join(" ", s402.Gates.Select(g => $"{g.Key}={g.Value.Flag}"))}）");

        WorldReport report = Run(cfg, layout, grids, wallClimb: true, eachForm: true);
        GD.Print("    " + report.Summary().Replace("\n", "\n    "));
        Check(report.Ok, "地图四全图检查没有问题（每块泥板都能走到、没有死局、两种形态都能走完、设计规则不违反）" +
                         (report.Ok ? "" : "：" + string.Join("；", report.Errors.Take(8))));
        StageChecks(report, "");

        WorldReport basic = Run(cfg, layout, grids, wallClimb: false, eachForm: false);
        Check(basic.Ok && basic.Stages.Count == Gates + 1 && basic.Stages[Gates].RoomsReached.Contains("S4_05"),
            "不攀墙：只用跳、落下、单向平台也能走完七层，⑦ 读完后回到 ⑥ 层右边的出口平台、进回廊、走到终末之室" +
            (basic.Ok ? "" : "：" + string.Join("；", basic.Errors.Take(6))));

        // 导出文字图再检查：用 S4_02 场景（生成工具写出来的）重新导出，结论一样
        var builder = new GeometryBuilder(layout, cell);
        Node scene = GD.Load<PackedScene>(layout.Find("S4_02").ScenePath).Instantiate();
        string exported = builder.ExportRoom(layout.Find("S4_02"), scene);
        scene.Free();
        RoomGrid round = exported == null ? null : RoomGrid.Parse(exported, layout.Find("S4_02"), cell, out _);
        Check(round != null && Body(round.Serialize()) == Body(s402.Serialize()),
            "导出：S4_02 场景导出的文字图和仓库里的一样（门块、泥板、出生点、地形）" + (builder.Errors.Count > 0 ? "：" + string.Join("；", builder.Errors) : ""));
        if (round != null)
        {
            var again = new Dictionary<string, RoomGrid>(grids) { ["S4_02"] = round };
            WorldReport reExported = Run(cfg, layout, again, wallClimb: false, eachForm: false);
            Check(reExported.Ok && reExported.Stages.Select(s => string.Join(",", s.AnchorsReached.OrderBy(a => a)))
                      .SequenceEqual(basic.Stages.Select(s => string.Join(",", s.AnchorsReached.OrderBy(a => a)))),
                "导出再检查：用导出的文字图重跑不攀墙检查，每个阶段能走到的泥板完全一样");
        }
        Metric("攀墙捷径条数", report.ClimbShortcuts.Count);
        Metric("耗时毫秒", Time.GetTicksMsec() - t0);
        return Task.CompletedTask;
    }

    private static string Body(string text) => string.Join("\n", text.Split('\n').Where(l => !l.StartsWith("#")));

    private static WorldReport Run(SimConfig cfg, WorldLayout layout, Dictionary<string, RoomGrid> grids, bool wallClimb, bool eachForm)
    {
        var world = new LevelWorld(LevelWorld.DefaultCellSize);
        foreach (string id in Rooms) world.Add(layout.Find(id), grids[id]);
        var config = new WorldCheckConfig
        {
            StartRoom = "S4_01",
            MainLine = Rooms.ToList(),
            FinalGoal = "S4_05",
            RequireEachForm = eachForm,
            HasWallClimb = wallClimb,
            Story = Enumerable.Range(1, Gates).Select(k => new StoryStep($"tablet.gate_{k}", "S4_02", $"Tablet_gate_{k}")).ToList(),
        };
        return WorldCheck.Run(cfg, world, config);
    }

    /// <summary>阶段 k（读完前 k 块）：第 k+1 块能走到，第 k+2 块走不到（被第 k+1 道活板门挡住）；七块读完之前到不了回廊。</summary>
    private void StageChecks(WorldReport report, string tag)
    {
        if (report.Stages.Count != Gates + 1)
        {
            Check(false, $"{tag}阶段数应为 {Gates + 1}（实际 {report.Stages.Count}）");
            return;
        }
        for (int k = 0; k < Gates; k++)
        {
            StageInfo stage = report.Stages[k];
            bool next = stage.AnchorsReached.Contains($"S4_02/Tablet_gate_{k + 1}");
            bool beyond = k + 2 <= Gates && stage.AnchorsReached.Contains($"S4_02/Tablet_gate_{k + 2}");
            bool corridor = stage.RoomsReached.Contains("S4_03");
            Check(next && !beyond && !corridor,
                $"{tag}读完 {k} 块：能走到第 {k + 1} 块泥板；" + (k + 2 <= Gates ? $"第 {k + 1} 道门关着，走不到第 {k + 2} 块；" : "") + "到不了回廊" +
                $"（能到的泥板：{string.Join(",", stage.AnchorsReached.Where(a => a.Contains("Tablet")).OrderBy(a => a))}）");
        }
        StageInfo last = report.Stages[Gates];
        Check(new[] { "S4_03", "S4_04", "S4_05" }.All(last.RoomsReached.Contains), $"{tag}七块都读完：出口门打开，能走到回廊、祭坛、终末之室");
    }
}
