using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>
/// RoomGrid 单元测试（关卡几何计划 v3 第 3.1 节）：每一类解析校验错误各一个用例；Serialize / Parse 往返一致；
/// 尺寸按房间表算（S1_03 是 108 列）；仓库里 27 个文字图都能解析。
/// </summary>
public partial class RoomGridTest : WorldTestBase
{
    protected override string SuiteName => "room_grid";

    private Vector2I _cell;

    protected override Task RunChecks()
    {
        _cell = new Vector2I((int)MetSysApi.CellSize.X, (int)MetSysApi.CellSize.Y);
        SizeChecks();
        ErrorCases();
        RoundTrip();
        RealFiles();
        return Task.CompletedTask;
    }

    private static RoomDef TestRoom(bool save = false) =>
        new() { Id = "T1", DisplayName = "测试", Origin = new Vector2I(0, 5), Size = new Vector2I(2, 2), HasSavePoint = save };

    // ---------- 尺寸和坐标 ----------

    private void SizeChecks()
    {
        RoomDef room = TestRoom();
        Check(RoomGrid.ExpectedSize(room, _cell) == new Vector2I(36, 21) && RoomGrid.ShiftFor(room, _cell) == 20,
            $"尺寸：2×2 格、原点 Y=5 的房间是 36 列 × 21 行，Shift=20（实际 {RoomGrid.ExpectedSize(room, _cell)}，Shift {RoomGrid.ShiftFor(room, _cell)}）");
        var layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        Vector2I s103 = RoomGrid.ExpectedSize(layout.Find("S1_03"), _cell);
        Check(s103.X == 108, $"尺寸：S1_03（6×3 格）应为 108 列（实际 {s103.X}x{s103.Y}）");
        RoomGrid grid = RoomGrid.CreateEmpty(room, _cell);
        Check(grid.TileRect(2, 3) == new Rect2(64, 76, 32, 32) && grid.FeetAt(2, 3) == new Vector2(80, 108),
            $"坐标：(2,3) 的瓦片是 Rect2(64,76,32,32)，脚底在 (80,108)（实际 {grid.TileRect(2, 3)}、{grid.FeetAt(2, 3)}）");
    }

    // ---------- 校验错误 ----------

    /// <summary>合法的基准图：P 在 (5,10)，其余全空。</summary>
    private string BaseText(RoomDef room, Action<RoomGrid> edit = null)
    {
        RoomGrid grid = RoomGrid.CreateEmpty(room, _cell);
        grid.Set(5, 10, RoomGrid.DefaultSpawn);
        edit?.Invoke(grid);
        return grid.Serialize();
    }

    private void Expect(string name, string text, RoomDef room, string expected)
    {
        RoomGrid grid = RoomGrid.Parse(text, room, _cell, out string error);
        Check(grid == null && error.Contains(room.Id) && error.Contains(expected), $"校验：{name} → 报错且指出“{expected}”（实际：{(grid == null ? error : "解析通过了")}）");
    }

    private static string ReplaceLine(string text, string from, string to)
    {
        if (!text.Contains(from)) throw new InvalidOperationException($"基准文字里没有“{from}”");
        return text.Replace(from, to);
    }

    private void ErrorCases()
    {
        RoomDef room = TestRoom();
        string ok = BaseText(room);
        Check(RoomGrid.Parse(ok, room, _cell, out string okError) != null, "校验：基准图能通过（" + okError + "）");

        Expect("@room 和房间表不一致", ReplaceLine(ok, "@room T1", "@room T2"), room, "@room 应为 T1");
        Expect("没写 @room", ReplaceLine(ok, "@room T1\n", ""), room, "@room 应为 T1");
        Expect("@size 和房间表不一致", ReplaceLine(ok, "@size 36x21", "@size 35x21"), room, "@size 应为 36x21");
        Expect("@size 格式错", ReplaceLine(ok, "@size 36x21", "@size 36*21"), room, "@size 格式应为");
        Expect("看不懂的文件头", ReplaceLine(ok, "---\n", "@foo bar\n---\n"), room, "看不懂的文件头");
        string[] lines = ok.TrimEnd('\n').Split('\n');
        Expect("少一行", string.Join("\n", lines.Take(lines.Length - 1)) + "\n", room, "行数应为 21");
        int firstRow = Array.IndexOf(lines, "---") + 1;
        string shortened = string.Join("\n", lines.Select((l, i) => i == firstRow ? l[..^1] : l)) + "\n";
        Expect("一行长度不对", shortened, room, "长度应为 36");
        Expect("非法字符", ok.Replace("@size 36x21\n---\n....", "@size 36x21\n---\n...Z"), room, "非法字符");
        Expect("没有 P", BaseText(room, g => g.Set(5, 10, RoomGrid.Empty)), room, "P（默认出生点）应恰好 1 个（实际 0）");
        Expect("两个 P", BaseText(room, g => g.Set(6, 10, RoomGrid.DefaultSpawn)), room, "P（默认出生点）应恰好 1 个（实际 2）");
        Expect("房间表没有存档点却有 S", BaseText(room, g => g.Set(7, 10, RoomGrid.SavePoint)), room, "不能有 S");
        RoomDef saveRoom = TestRoom(save: true);
        Expect("房间表有存档点却没有 S", BaseText(saveRoom), saveRoom, "S 应恰好 1 个");
        Expect("锚点声明了但图里没有", BaseText(room, g => g.SetAnchor('a', "Npc")), room, "锚点 a（Npc）应在图里恰好出现 1 次（实际 0）");
        Expect("锚点出现两次", BaseText(room, g => { g.SetAnchor('a', "Npc"); g.Set(8, 10, 'a'); g.Set(9, 10, 'a'); }), room, "锚点 a（Npc）应在图里恰好出现 1 次（实际 2）");
        Expect("图里有锚点但没声明", BaseText(room, g => g.Set(8, 10, 'b')), room, "图里有锚点 b，但文件头没有 @anchor b");
        Expect("锚点重复声明", ReplaceLine(BaseText(room, g => { g.SetAnchor('a', "Npc"); g.Set(8, 10, 'a'); }), "@anchor a Npc\n", "@anchor a Npc\n@anchor a Other\n"), room, "锚点 a 重复声明");
        Expect("数字出生点声明了但图里没有", BaseText(room, g => g.SetSpawn('1', "From_X")), room, "出生点 1（From_X）应在图里恰好出现 1 次（实际 0）");
        Expect("图里有数字出生点但没声明", BaseText(room, g => g.Set(8, 10, '2')), room, "图里有出生点 2，但文件头没有 @spawn 2");
        Expect("数字出生点重复声明", ReplaceLine(BaseText(room, g => { g.SetSpawn('1', "From_X"); g.Set(8, 10, '1'); }), "@spawn 1 From_X\n", "@spawn 1 From_X\n@spawn 1 From_Y\n"), room, "出生点 1 重复声明");
        Expect("有 R 没有 @cond", BaseText(room, g => g.Set(8, 12, RoomGrid.Conditional)), room, "图里有 R（条件地形），文件头必须写 @cond");
        Expect("有 @cond 没有 R", BaseText(room, g => g.SetCondition("boss_keeper")), room, "文件头写了 @cond，但图里没有 R");
    }

    // ---------- 往返 ----------

    private void RoundTrip()
    {
        RoomDef room = TestRoom(save: true);
        RoomGrid grid = RoomGrid.CreateEmpty(room, _cell);
        string chars = "#X.-BHR";
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
                grid.Set(c, r, chars[(c * 7 + r * 3) % chars.Length]);
        grid.Set(5, 10, RoomGrid.DefaultSpawn);
        grid.Set(6, 10, RoomGrid.SavePoint);
        grid.Set(7, 10, 'a');
        grid.Set(8, 10, 'z');
        grid.Set(9, 10, '1');
        grid.Set(10, 10, '9');
        grid.SetAnchor('a', "Npc_A");
        grid.SetAnchor('z', "Npc_Z");
        grid.SetSpawn('1', "From_A");
        grid.SetSpawn('9', "From_B");
        grid.SetCondition("boss_keeper");
        grid.AddComment("往返测试 一");
        grid.AddComment("第二行注释");
        string text = grid.Serialize();
        RoomGrid back = RoomGrid.Parse(text, room, _cell, out string error);
        Check(back != null, "往返：Serialize 的结果能被 Parse（" + error + "）");
        if (back == null) return;
        bool cells = true;
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
                cells &= back.At(c, r) == grid.At(c, r);
        Check(cells && back.Cols == grid.Cols && back.Rows == grid.Rows && back.Shift == grid.Shift, "往返：每一格、尺寸、Shift 都一致");
        Check(back.Condition == "boss_keeper" && back.Anchors.OrderBy(a => a.Key).SequenceEqual(grid.Anchors.OrderBy(a => a.Key))
              && back.Spawns.OrderBy(a => a.Key).SequenceEqual(grid.Spawns.OrderBy(a => a.Key)) && back.Comments.SequenceEqual(grid.Comments),
            "往返：@cond、@anchor、@spawn、注释都一致");
        Check(back.Serialize() == text, "往返：再 Serialize 一次，文字完全相同");
    }

    // ---------- 仓库里的文字图 ----------

    private void RealFiles()
    {
        var layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        int loaded = 0;
        var problems = new System.Collections.Generic.List<string>();
        foreach (RoomDef room in layout.Rooms)
        {
            RoomGrid grid = RoomGrid.Load(room, _cell, out string error);
            if (grid == null) { problems.Add(error.Length > 0 ? error : room.Id + " 没有文字图"); continue; }
            loaded++;
            string text = FileAccess.GetFileAsString(RoomGrid.PathFor(room.Id)).Replace("\r\n", "\n");
            if (grid.Serialize() != text) problems.Add(room.Id + " 的文件不是 Serialize 的规范格式");
        }
        Check(loaded == 27 && problems.Count == 0, $"仓库：27 个房间的文字图都能解析，且就是 Serialize 的规范格式（{loaded} 个）" + (problems.Count > 0 ? "：" + string.Join("; ", problems.Take(4)) : ""));
        RoomGrid s103 = RoomGrid.Load(layout.Find("S1_03"), _cell, out _);
        Check(s103 != null && s103.Cols == 108, $"仓库：S1_03.txt 是 108 列（实际 {s103?.Cols}）");
    }
}
