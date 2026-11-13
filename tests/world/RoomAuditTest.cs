using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.World;

/// <summary>
/// 编辑器里的关卡检查（RoomRules.AuditScene，RoomRoot 的警告）：
/// ① 仓库里每个房间场景都没有关卡警告（边缘缺口没出口、出口被堵、出生点悬空或卡砖、砖铺到房间外）；
/// ② 故意改坏 S1_01（挖缺口、堵出口、出生点挪到半空 / 砖里、在房间外铺砖），警告当场出现并说对位置；恢复后消失。
/// </summary>
public partial class RoomAuditTest : WorldTestBase
{
    protected override string SuiteName => "room_audit";

    protected override async Task RunChecks()
    {
        AuditChecks();
        ExportButtonChecks();
        await RescueChecks();
    }

    /// <summary>「导出文字图」按钮：S1_01 场景和文字图本来就同步，按一下写出来的和仓库里一字不差，开头的说明保留。</summary>
    private void ExportButtonChecks()
    {
#if TOOLS
        string path = RoomGrid.PathFor("S1_01");
        string original = FileAccess.GetFileAsString(path);
        var room = GD.Load<PackedScene>("res://assets/scenes/world/s1/S1_01.tscn").Instantiate<RoomRoot>();
        room.ExportTextInEditor();
        room.Free();
        string written = FileAccess.GetFileAsString(path);
        using (FileAccess restore = FileAccess.Open(path, FileAccess.ModeFlags.Write)) restore.StoreString(original);
        string Norm(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');
        string firstLine = Norm(original).Split('\n')[0];
        Check(Norm(written) == Norm(original) && firstLine.Length > "# S1_01 苏醒".Length,
            $"导出文字图按钮：S1_01 导出结果和仓库里的文字图一样，开头的说明“{firstLine}”保留" + (Norm(written) == Norm(original) ? "" : $"\n实际开头：{Norm(written).Split('\n')[0]}"));
#else
        Check(true, "导出文字图按钮：只在编辑器版本里有，这里跳过");
#endif
    }

    /// <summary>③ 掉出房间：从没有出口的地方离开房间 → 黑屏后送回最近站稳的地方，不扣血；从出口走仍然正常换房，不触发。</summary>
    private async Task RescueChecks()
    {
        await SetUpGame();
        World.LoadRoomImmediate("S1_01", SpawnPoint.DefaultId);
        await Frames(10);
        Vector2? safe = World.LastSafePosition;
        Check(safe != null && World.Rescues == 0, $"站稳后记下位置（{safe}），开局没有触发保险");

        Vector2 room = World.CurrentRoom.GlobalPosition;
        Mover.GlobalPosition = room + new Vector2(-200, 300);
        Mover.Velocity = Vector2.Zero;
        bool rescued = await WaitUntil(() => World.Rescues == 1 && !World.IsTransitioning, 240);
        await Frames(2);
        Check(rescued && World.CurrentRoomDef.Id == "S1_01" && safe != null && Mover.GlobalPosition.DistanceTo(safe.Value) < 4f && World.LastRescue.Contains("左边"),
            $"掉出房间：从左边离开房间 → 送回最近站稳的地方（{Mover.GlobalPosition}），说明“{World.LastRescue}”");

        // 从出口正常走：S1_01 右出口 → S1_02，不算掉出房间
        Mover.GlobalPosition = room + new Vector2(1080, 555);
        Mover.Velocity = Vector2.Zero;
        Mover.MoveDir = 1f;
        bool walked = await WaitUntil(() => World.CurrentRoomDef?.Id == "S1_02", 240);
        Mover.MoveDir = 0f;
        await Frames(60);
        Check(walked && World.Rescues == 1, $"从出口走：S1_01 → S1_02 正常换房，保险没有触发（共 {World.Rescues} 次）");
        await TearDownGame();
    }

    private void AuditChecks()
    {
        var layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        var all = new List<string>();
        foreach (RoomDef def in layout.Rooms)
        {
            var root = GD.Load<PackedScene>(def.ScenePath).Instantiate<RoomRoot>();
            all.AddRange(RoomRules.AuditScene(root, layout).Select(p => $"{def.Id}：{p}"));
            root.Free();
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }
        Check(all.Count == 0, $"全部 {layout.Rooms.Count} 个房间没有关卡警告" + (all.Count > 0 ? "：\n  " + string.Join("\n  ", all) : ""));

        BrokenChecks(layout);
    }

    private void BrokenChecks(WorldLayout layout)
    {
        var room = GD.Load<PackedScene>("res://assets/scenes/world/s1/S1_01.tscn").Instantiate<RoomRoot>();
        var geometry = room.GetNode<TileMapLayer>("Geometry");
        Vector2I cellSize = RoomRules.CellSize();
        RoomGrid grid = RoomRules.SceneGrid(layout.Find("S1_01"), room, cellSize, new List<string>(), out _);
        int tile = geometry.TileSet.TileSize.X;
        int shift = Mathf.RoundToInt(-geometry.Position.Y);
        Vector2I TileAt(int col, int row) => new(col, row);   // S1_01 的 Geometry 是 32 像素瓦片，和网格一一对应
        bool Has(string text) => RoomRules.AuditScene(room, layout).Any(p => p.Contains(text));

        // 左墙（没有出口）挖一格
        Vector2I wall = TileAt(0, 5);
        Vector2I atlas = geometry.GetCellAtlasCoords(wall);
        geometry.EraseCell(wall);
        List<string> gap = RoomRules.AuditScene(room, layout);
        bool gapReported = gap.Any(p => p.Contains("房间边缘有缺口") && p.Contains("左边 (0,5)"));
        geometry.SetCell(wall, 0, atlas);
        Check(gapReported && !Has("房间边缘有缺口"), $"挖缺口：左墙第 5 行挖掉 → 警告“房间边缘有缺口……左边 (0,5)”；补回后消失（{gap.FirstOrDefault()}）");

        // 右出口（通往 S1_02）整段堵上
        RoomDef def = layout.Find("S1_01");
        ExitDef exit = def.Exits.First(e => e.Target == "S1_02");
        var (from, to) = RoomRules.ExitRange(grid, exit, cellSize);
        var filled = new List<Vector2I>();
        for (int r = from; r <= to; r++)
        {
            var cell = TileAt(grid.Cols - 1, r);
            if (geometry.GetCellSourceId(cell) == -1) { geometry.SetCell(cell, 0, atlas); filled.Add(cell); }
        }
        bool blocked = Has("右出口（通往 S1_02）") && Has("没有至少 2 格的开口");
        foreach (Vector2I cell in filled) geometry.EraseCell(cell);
        Check(blocked && !Has("没有至少 2 格的开口"), "堵出口：把通往 S1_02 的右出口那一格整段堵上 → 警告出口没有开口；挖开后消失");

        // 出生点挪到半空 / 砖里
        var spawn = room.GetNode<SpawnPoint>("Spawns/Default");
        Vector2 original = spawn.Position;
        spawn.Position = original + new Vector2(0, -200);
        bool air = RoomRules.AuditScene(room, layout).Any(p => p.Contains("出生点 Default") && p.Contains("才有地面"));
        spawn.Position = original + new Vector2(0, 40);
        bool inside = RoomRules.AuditScene(room, layout).Any(p => p.Contains("出生点 Default") && p.Contains("砖"));
        spawn.Position = original;
        Check(air && inside && !Has("出生点 Default"), "出生点：挪到半空 → 警告“下面 N 像素才有地面”；挪进地面 → 警告卡在砖里；放回后消失");

        // 房间外铺砖
        var outside = new Vector2I(grid.Cols + 2, 3);
        geometry.SetCell(outside, 0, atlas);
        bool outReported = Has("铺到了房间外面");
        geometry.EraseCell(outside);
        Check(outReported && !Has("铺到了房间外面"), "房间外：在房间右边外面铺一格砖 → 警告“铺到了房间外面”；删掉后消失");
        // 判定框被拖离节点（挪的是判定框，不是节点本身）
        var pit = GD.Load<PackedScene>("res://assets/scenes/world/s1/S1_02.tscn").Instantiate<RoomRoot>();
        var tabletShape = pit.GetNode<CollisionShape2D>("Objects/Tablet_nameless_day/Shape");
        Vector2 shapeAt = tabletShape.Position;
        tabletShape.Position = shapeAt + new Vector2(383, 0);
        bool drift = RoomRules.AuditScene(pit, layout).Any(p => p.Contains("Tablet_nameless_day 的判定框") && p.Contains("节点本身"));
        tabletShape.Position = shapeAt;
        bool driftGone = RoomRules.AuditScene(pit, layout).Count == 0;
        pit.Free();
        Check(drift && driftGone, "判定框：把泥板的判定框拖离节点 383 像素 → 提醒“要挪节点本身”；放回后消失");
        _ = tile; _ = shift;
        room.Free();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }
}
