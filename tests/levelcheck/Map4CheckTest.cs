using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Tools;
using SonOfMud.Tools.LevelCheck;
using SonOfMud.World;

/// <summary>
/// 地图四“七道门”的静态检查（按美术比例重做后）：S4_01 心室入口 → 塔每层一间（① S4_02、②③ S4_0304、④ S4_05、⑤ S4_06、⑥ S4_07、⑦ S4_08）
/// → ⑥ 层右边的出口进 S4_09 血池回廊 → S4_10 心室祭坛 → S4_11 终末之室。
/// 剧情按七块泥板推进（站到第 k 块泥板旁边才算读完，tablet.gate_k 成立，这一层往下的门打开）：
/// ① 每一阶段能走到下一块泥板，进不了下一层；再下一块走不到（②③ 同一间房，两块都能走到，但出口要 ② ③ 两道门都开）；七块读完之前到不了回廊；
/// ② 七块读完后能走到终末之室；
/// ③ 不给攀墙也能走完（⑦ 读完后回到 ⑥ 层出口只用跳和单向平台）；
/// ④ 每层的门块条件和泥板一一对应；回廊出口门要七块泥板；
/// ⑤ 把 S4_02、S4_0304 场景导出成文字图再检查一遍，结论不变。
/// </summary>
public partial class Map4CheckTest : LabSuiteBase
{
    protected override string ReportName => "levelcheck_map4_results";

    public static readonly string[] Rooms = { "S4_01", "S4_02", "S4_0304", "S4_05", "S4_06", "S4_07", "S4_08", "S4_09", "S4_10", "S4_11" };
    /// <summary>塔的七层（②③ 同一间）。</summary>
    private static readonly string[] Tower = { "S4_02", "S4_0304", "S4_05", "S4_06", "S4_07", "S4_08" };
    /// <summary>第 k 块泥板所在的房间（k 从 1 开始）。</summary>
    public static readonly string[] TabletRoom = { "", "S4_02", "S4_0304", "S4_0304", "S4_05", "S4_06", "S4_07", "S4_08" };
    /// <summary>每一层往下的门块：房间 → 条件。⑦ 是塔底，没有往下的门。</summary>
    private static readonly (string Room, string Flag)[] FloorGates =
    {
        ("S4_02", "tablet.gate_1"), ("S4_0304", "tablet.gate_2"), ("S4_0304", "tablet.gate_3"),
        ("S4_05", "tablet.gate_4"), ("S4_06", "tablet.gate_5"), ("S4_07", "tablet.gate_6"),
    };
    private const int Gates = 7;
    private static readonly string[] AfterTower = { "S4_09", "S4_10", "S4_11" };

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
            RoomDef def = layout.Find(id);
            string error = "房间表里没有";
            RoomGrid grid = def == null ? null : RoomGrid.Load(def, cell, out error);
            if (grid == null) loadErrors.Add($"{id}：{error}");
            else grids[id] = grid;
        }
        Check(loadErrors.Count == 0, "地图四十个房间的文字图都能读到" + (loadErrors.Count > 0 ? "：" + string.Join("；", loadErrors) : ""));
        if (loadErrors.Count > 0) return Task.CompletedTask;

        // 门块、泥板、回廊出口门
        string gateList = string.Join(" ", Rooms.SelectMany(id => grids[id].Gates.Select(g => $"{id}:{g.Key}={g.Value.Flag}")));
        bool gatesOk = FloorGates.All(f => grids[f.Room].Gates.Values.Count(g => g.Flag == f.Flag) == 1)
                       && Rooms.Sum(id => grids[id].Gates.Count) == FloorGates.Length;
        Check(gatesOk, $"每层往下的门块：① ② ③ ④ ⑤ ⑥ 各一道，条件是这一层的泥板（{gateList}）");
        bool tabletsOk = Enumerable.Range(1, Gates).All(k => grids[TabletRoom[k]].Anchors.Values.Contains($"Tablet_gate_{k}"));
        Check(tabletsOk, "七块泥板：① S4_02，②③ S4_0304，④ S4_05，⑤ S4_06，⑥ S4_07，⑦ S4_08");
        ExitDef corridor = layout.Find("S4_07").Exits.FirstOrDefault(e => e.Target == "S4_09");
        var flags = corridor?.Requirement?.Flags;
        Check(corridor != null && corridor.HasBarrier && flags.Count == Gates && Enumerable.Range(1, Gates).All(k => flags.Contains($"tablet.gate_{k}")),
            $"回廊出口：S4_07 → S4_09 要七块泥板 tablet.gate_1–7 全部读完（{(flags == null ? "没有门" : string.Join(",", flags))}）");
        Check(layout.Find("S4_01").Exits.Any(e => e.Target == "S3_08") && layout.Find("S3_08").Exits.Any(e => e.Target == "S4_01"),
            "S3_08 ↔ S4_01 的过门还在（两边都有）");

        WorldReport report = Run(cfg, layout, grids, wallClimb: true, eachForm: true);
        GD.Print("    " + report.Summary().Replace("\n", "\n    "));
        Check(report.Ok, "地图四全图检查没有问题（每块泥板都能走到、没有死局、两种形态都能走完、设计规则不违反）" +
                         (report.Ok ? "" : "：" + string.Join("；", report.Errors.Take(8))));
        StageChecks(report, "");

        WorldReport basic = Run(cfg, layout, grids, wallClimb: false, eachForm: false);
        Check(basic.Ok && basic.Stages.Count == Gates + 1 && basic.Stages[Gates].RoomsReached.Contains("S4_11"),
            "不攀墙：只用跳、落下、单向平台也能走完七层，⑦ 读完后爬回 ⑥ 层、从右边出口进回廊、落进祭坛、走到终末之室" +
            (basic.Ok ? "" : "：" + string.Join("；", basic.Errors.Take(6))));
        StageChecks(basic, "不攀墙：");

        // 导出文字图再检查：用生成工具写出来的场景重新导出，结论一样
        var builder = new GeometryBuilder(layout, cell);
        var again = new Dictionary<string, RoomGrid>(grids);
        foreach (string id in new[] { "S4_02", "S4_0304" })
        {
            Node scene = GD.Load<PackedScene>(layout.Find(id).ScenePath).Instantiate();
            string exported = builder.ExportRoom(layout.Find(id), scene);
            scene.Free();
            RoomGrid round = exported == null ? null : RoomGrid.Parse(exported, layout.Find(id), cell, out _);
            Check(round != null && Body(round.Serialize()) == Body(grids[id].Serialize()),
                $"导出：{id} 场景导出的文字图和仓库里的一样（门块、泥板、出生点、地形）" + (builder.Errors.Count > 0 ? "：" + string.Join("；", builder.Errors) : ""));
            if (round != null) again[id] = round;
        }
        WorldReport reExported = Run(cfg, layout, again, wallClimb: false, eachForm: false);
        Check(reExported.Ok && reExported.Stages.Select(s => string.Join(",", s.AnchorsReached.OrderBy(a => a)))
                  .SequenceEqual(basic.Stages.Select(s => string.Join(",", s.AnchorsReached.OrderBy(a => a)))),
            "导出再检查：用导出的文字图重跑不攀墙检查，每个阶段能走到的泥板完全一样");
        Metric("攀墙捷径条数", report.ClimbShortcuts.Count);
        Metric("耗时毫秒", Time.GetTicksMsec() - t0);
        return Task.CompletedTask;
    }

    private static string Body(string text) => string.Join("\n", text.Split('\n').Where(l => !l.StartsWith("#")));

    public static WorldReport Run(SimConfig cfg, WorldLayout layout, Dictionary<string, RoomGrid> grids, bool wallClimb, bool eachForm)
    {
        var world = new LevelWorld(LevelWorld.DefaultCellSize);
        foreach (string id in Rooms) world.Add(layout.Find(id), grids[id]);
        var config = new WorldCheckConfig
        {
            StartRoom = "S4_01",
            MainLine = Rooms.ToList(),
            FinalGoal = "S4_11",
            RequireEachForm = eachForm,
            HasWallClimb = wallClimb,
            Story = Enumerable.Range(1, Gates).Select(k => new StoryStep($"tablet.gate_{k}", TabletRoom[k], $"Tablet_gate_{k}")).ToList(),
        };
        return WorldCheck.Run(cfg, world, config);
    }

    /// <summary>
    /// 阶段 k（读完前 k 块）：第 k+1 块能走到；第 k+2 块走不到，除非两块在同一间房（②③）；
    /// 第 k+1 块所在层的下一层进不去；七块读完之前到不了回廊。
    /// </summary>
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
            string nextRoom = TabletRoom[k + 1];
            bool next = stage.AnchorsReached.Contains($"{nextRoom}/Tablet_gate_{k + 1}");
            bool sameRoom = k + 2 <= Gates && TabletRoom[k + 2] == nextRoom;
            bool beyond = k + 2 <= Gates && stage.AnchorsReached.Contains($"{TabletRoom[k + 2]}/Tablet_gate_{k + 2}");
            int floor = System.Array.IndexOf(Tower, nextRoom);
            string below = floor + 1 < Tower.Length ? Tower[floor + 1] : null;
            bool belowReached = below != null && stage.RoomsReached.Contains(below);
            bool corridor = AfterTower.Any(stage.RoomsReached.Contains);
            Check(next && (sameRoom || !beyond) && !belowReached && !corridor,
                $"{tag}读完 {k} 块：能走到第 {k + 1} 块泥板（{nextRoom}）；" +
                (k + 2 <= Gates ? (sameRoom ? $"第 {k + 2} 块在同一间房，可以先读；" : $"走不到第 {k + 2} 块；") : "") +
                (below != null ? $"进不了下一层 {below}；" : "") + "到不了回廊" +
                $"（能到的泥板：{string.Join(",", stage.AnchorsReached.Where(a => a.Contains("Tablet")).OrderBy(a => a))}；房间：{string.Join(",", stage.RoomsReached.OrderBy(r => r))}）");
        }
        StageInfo last = report.Stages[Gates];
        Check(AfterTower.All(last.RoomsReached.Contains), $"{tag}七块都读完：出口门打开，能走到回廊、祭坛、终末之室");
    }
}
