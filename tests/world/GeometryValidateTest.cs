using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Tools;
using SonOfMud.World;

/// <summary>
/// 生成校验（关卡几何计划 v3 第 3.2 节）：把某个房间的文字图在内存里改坏，用写入模式跑 --geometry，
/// 必须报错（指出房间）且一个文件都不写。输出改到 user://geo_validate/，并核对仓库里的房间场景前后完全不变。
/// 对照组：合法的改动会真的写出文件，证明“没写文件”不是因为输出路径无效。
/// </summary>
public partial class GeometryValidateTest : WorldTestBase
{
    protected override string SuiteName => "geometry_validate";

    private const string OutDir = "user://geo_validate";
    private WorldLayout _layout;
    private Vector2I _cell;

    protected override Task RunChecks()
    {
        _layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        _cell = new Vector2I((int)MetSysApi.CellSize.X, (int)MetSysApi.CellSize.Y);
        ClearOut();
        Dictionary<string, string> before = SceneHashes();

        // ① 开口不对：S1_05 右出口（通往 S1_06）的开口全部堵上
        Case("开口不对（S1_05 右出口被堵死）", "S1_05", g =>
        {
            for (int r = 0; r < 12; r++) g.Set(g.Cols - 1, r, RoomGrid.Solid);
        }, "S1_05：右出口（通往 S1_06）", "没有至少 2 格的开口");

        // ② 边界不封闭：S1_01 左边没有出口，在左墙上开一格
        Case("边界不封闭（S1_01 左墙开一格）", "S1_01", g => g.Set(0, 5, RoomGrid.Empty), "S1_01：非出口的边界没有封闭", "左边 (0,5)");

        // ③ 出生点卡墙：在 S1_01 的 P 头顶放一格实心
        Case("出生点卡墙（S1_01 的 P 头顶有实心）", "S1_01", g =>
        {
            Vector2I p = g.Find(RoomGrid.DefaultSpawn).Value;
            g.Set(p.X, p.Y - 1, RoomGrid.Solid);
        }, "S1_01：出生点 Default", "放不下玩家");

        // ④ 跨房间对齐：S1_01 右出口开口处的地面抬高 3 格（开口仍有 2 格以上），和 S1_02 左出口的地面不在同一个全局 Y
        Case("跨房间对齐（S1_01 右出口地面抬高 3 格）", "S1_01", g =>
        {
            RoomDef room = _layout.Find("S1_01");
            ExitDef exit = room.Exits.First(e => e.Target == "S1_02");
            var builder = new GeometryBuilder(_layout, _cell);
            var (from, to) = builder.ExitRange(room, g, exit);
            var run = GeometryBuilder.MainOpening(g, exit.Side, from, to).Value;
            for (int r = run.B - 2; r <= run.B; r++) g.Set(g.Cols - 1, r, RoomGrid.Solid);
        }, "S1_01↔S1_02", "开口处的地面不在同一个全局 Y");

        // ⑤ 文字图解析失败
        CaseText("文字图解析失败（S1_01 有非法字符）", "S1_01", text => text.Replace("---\n#", "---\nZ"), "S1_01 的文字图有", "非法字符");

        // ⑥ 锚点找不到节点
        CaseText("锚点找不到节点（S1_05 的 a 指向不存在的节点）", "S1_05", text => text.Replace("@anchor a Npc_Keeper", "@anchor a Npc_Nobody"), "S1_05：锚点 a 找不到节点", "Npc_Nobody");

        // ⑦ 上下出口的到达出生点必须写数字：去掉 S1_04 的 From_S1_08（上方出口）
        Case("上方出口没写数字出生点（S1_04 去掉 From_S1_08）", "S1_04", g => { }, "S1_04：上出口（通往 S1_08）的到达出生点 From_S1_08", "必须在文字图里写数字出生点", text =>
        {
            RoomGrid g = Parse("S1_04", text);
            char digit = g.Spawns.First(s => s.Value == "From_S1_08").Key;
            int body = text.IndexOf("---\n", StringComparison.Ordinal);
            return text[..body].Replace($"@spawn {digit} From_S1_08\n", "") + text[body..].Replace(digit, '.');
        });

        Check(SceneHashes().SequenceEqual(before), "以上所有失败用例之后，仓库里 27 个房间场景和瓦片集一个字节都没变");

        // 对照组：合法改动（S1_01 半空加一格实心）→ 干跑报告 S1_01 的 Geometry 会变；写入模式真的写出 S1_01 一个文件
        string control = Edit("S1_01", g => g.Set(10, 5, RoomGrid.Solid));
        var dry = NewBuilder(("S1_01", control));
        bool dryOk = dry.RunGeometry(write: false);
        Check(dryOk && dry.Changes.Count == 1 && dry.Changes[0].StartsWith("S1_01：Geometry") && dry.Written.Count == 0 && OutFiles().Count == 0,
            $"对照组干跑：只报告 S1_01 的 Geometry 会变，不写文件（{string.Join("；", dry.Changes)}）");
        var real = NewBuilder(("S1_01", control));
        bool realOk = real.RunGeometry(write: true);
        string outScene = $"{OutDir}/S1_01.tscn";
        Check(realOk && real.Written.SequenceEqual(new[] { outScene }) && OutFiles().SequenceEqual(new[] { "S1_01.tscn" }),
            $"对照组写入：只写出 {outScene}（写了 {string.Join(",", real.Written)}；输出目录 {string.Join(",", OutFiles())}；{string.Join(";", real.Errors)}）");
        Node written = ResourceLoader.Load<PackedScene>(outScene)?.Instantiate();
        var geometry = written?.GetNodeOrNull<TileMapLayer>("Geometry");
        Check(geometry != null && geometry.GetCellAtlasCoords(new Vector2I(10, 5)) == TerrainTiles.SolidAtlas, "对照组写入：写出的场景里 (10,5) 是实心瓦片");
        written?.Free();
        Check(SceneHashes().SequenceEqual(before), "对照组之后，仓库里的房间场景仍然没变（输出确实改到了 user://）");
        ClearOut();
        return Task.CompletedTask;
    }

    // ---------- 用例 ----------

    private void Case(string name, string roomId, Action<RoomGrid> edit, string where, string what, Func<string, string> textEdit = null)
    {
        string text = Edit(roomId, edit);
        if (textEdit != null) text = textEdit(text);
        Run(name, roomId, text, where, what);
    }

    private void CaseText(string name, string roomId, Func<string, string> textEdit, string where, string what) =>
        Run(name, roomId, textEdit(Original(roomId)), where, what);

    private void Run(string name, string roomId, string text, string where, string what)
    {
        GeometryBuilder builder = NewBuilder((roomId, text));
        bool ok = builder.RunGeometry(write: true);
        bool reported = builder.Errors.Any(e => e.Contains(where) && e.Contains(what));
        Check(!ok && reported, $"{name}：报错并指出“{where}…{what}”（{string.Join("；", builder.Errors.Take(3))}）");
        Check(builder.Written.Count == 0 && OutFiles().Count == 0, $"{name}：一个文件都没写（{builder.Written.Count} 个，输出目录 {OutFiles().Count} 个）");
    }

    private GeometryBuilder NewBuilder(params (string Room, string Text)[] overrides)
    {
        var builder = new GeometryBuilder(_layout, _cell)
        {
            SceneOutput = room => $"{OutDir}/{room.Id}.tscn",
            TextOutput = room => $"{OutDir}/{room.Id}.txt",
            TileSetOutput = $"{OutDir}/world_tileset.tres",
        };
        foreach (var (room, text) in overrides) builder.TextOverrides[room] = text;
        return builder;
    }

    private string Original(string roomId) => FileAccess.GetFileAsString(RoomGrid.PathFor(roomId)).Replace("\r\n", "\n");

    private RoomGrid Parse(string roomId, string text)
    {
        RoomGrid grid = RoomGrid.Parse(text, _layout.Find(roomId), _cell, out string error);
        if (grid == null) throw new InvalidOperationException(error);
        return grid;
    }

    private string Edit(string roomId, Action<RoomGrid> edit)
    {
        RoomGrid grid = Parse(roomId, Original(roomId));
        edit(grid);
        return grid.Serialize();
    }

    // ---------- 文件 ----------

    private static List<string> OutFiles()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutDir));
        return DirAccess.GetFilesAt(OutDir).OrderBy(f => f).ToList();
    }

    private static void ClearOut()
    {
        foreach (string file in OutFiles()) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath($"{OutDir}/{file}"));
    }

    private Dictionary<string, string> SceneHashes()
    {
        var hashes = new SortedDictionary<string, string>();
        foreach (RoomDef room in _layout.Rooms) hashes[room.ScenePath] = FileAccess.GetMd5(room.ScenePath);
        hashes[TerrainTiles.TileSetPath] = FileAccess.GetMd5(TerrainTiles.TileSetPath);
        return new Dictionary<string, string>(hashes);
    }
}
