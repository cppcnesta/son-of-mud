using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间地形的规则检查（按 32 像素文字图网格 RoomGrid）：出口开口、非出口边界封闭、出生点能不能站。
/// 生成工具（GeometryBuilder）和编辑器里的房间检查（RoomRoot 的警告、<see cref="AuditScene"/>）共用这一份。
/// </summary>
public static class RoomRules
{
    public const int Tile = RoomGrid.Tile;
    /// <summary>出生点检查用的玩家碰撞体（脚在出生点）和它与实心瓦片之间至少要留的间隙。</summary>
    public static readonly Vector2 BodySize = new(48, 48);
    public const float BodyGap = 2f;
    /// <summary>跨房间对齐：两边开口至少要重叠 2 格。</summary>
    public const int MinOverlap = 2 * Tile;
    /// <summary>物件的判定框横向离开节点超过这么多像素，就提醒“挪的是判定框，不是节点”。</summary>
    public const float MaxShapeDrift = 64f;

    // ---------- 网格工具 ----------

    /// <summary>脚底位置所在的格子：x 所在列；y 落在哪一格的底边（含底边上方 1 像素以内）就是哪一行。</summary>
    public static Vector2I CellOf(RoomGrid grid, Vector2 feet) =>
        new(Mathf.FloorToInt(feet.X / Tile), Mathf.CeilToInt((feet.Y + grid.Shift) / Tile) - 1);

    /// <summary>永久的墙（边界封闭只认这两种；B / H 会被打碎，R 可能不出现）。</summary>
    public static bool IsPermanent(char ch) => ch is RoomGrid.Solid or RoomGrid.NoClimb;

    public static bool Standable(char ch) => RoomGrid.IsSolidChar(ch, false) || ch == RoomGrid.OneWay;

    public static string SideName(ExitSide side) => side switch
    {
        ExitSide.Left => "左",
        ExitSide.Right => "右",
        ExitSide.Up => "上",
        _ => "下",
    };

    /// <summary>出口所在那一格沿边界的范围：左右出口是行号（和这一格高度范围相交的行），上下出口是列号。</summary>
    public static (int From, int To) ExitRange(RoomGrid grid, ExitDef exit, Vector2I cellSize)
    {
        if (exit.Side is ExitSide.Up or ExitSide.Down)
        {
            int perCell = cellSize.X / Tile;
            return (exit.Offset * perCell, exit.Offset * perCell + perCell - 1);
        }
        int top = exit.Offset * cellSize.Y, bottom = top + cellSize.Y;
        int from = -1, to = -1;
        for (int r = 0; r < grid.Rows; r++)
        {
            float y0 = r * Tile - grid.Shift;
            if (y0 < bottom && y0 + Tile > top)
            {
                if (from < 0) from = r;
                to = r;
            }
        }
        return (from, to);
    }

    public static bool InExitRange(RoomDef room, RoomGrid grid, ExitSide side, int index, Vector2I cellSize) =>
        room.Exits.Any(e => e.Kind == ExitKind.Edge && e.Side == side && ExitRange(grid, e, cellSize) is var (from, to) && index >= from && index <= to);

    public static Vector2I BoundaryCell(RoomGrid grid, ExitSide side, int index) => side switch
    {
        ExitSide.Left => new Vector2I(0, index),
        ExitSide.Right => new Vector2I(grid.Cols - 1, index),
        ExitSide.Up => new Vector2I(index, 0),
        _ => new Vector2I(index, grid.Rows - 1),
    };

    /// <summary>边界上 from..to 之间能通过的连续段（不是 # / X 的格子）。</summary>
    public static List<(int A, int B)> OpenRuns(RoomGrid grid, ExitSide side, int from, int to)
    {
        var runs = new List<(int, int)>();
        int start = -1;
        for (int i = from; i <= to + 1; i++)
        {
            bool open = i <= to && BoundaryCell(grid, side, i) is var cell && !IsPermanent(grid.At(cell.X, cell.Y));
            if (open && start < 0) start = i;
            if (!open && start >= 0) { runs.Add((start, i - 1)); start = -1; }
        }
        return runs;
    }

    /// <summary>左右出口的主开口：最低的、至少 2 格高的一段。</summary>
    public static (int A, int B)? MainOpening(RoomGrid grid, ExitSide side, int from, int to)
    {
        var runs = OpenRuns(grid, side, from, to).Where(r => r.B - r.A + 1 >= 2).ToList();
        return runs.Count == 0 ? null : runs.OrderByDescending(r => r.B).First();
    }

    /// <summary>左右出口开口处的地面：开口最下一格的下面一格（在边界那一列），能站才算。</summary>
    public static int? FloorRow(RoomGrid grid, ExitSide side, (int A, int B) run)
    {
        Vector2I cell = BoundaryCell(grid, side, run.B + 1);
        return grid.InBounds(cell.X, cell.Y) && Standable(grid.At(cell.X, cell.Y)) ? cell.Y : null;
    }

    // ---------- 检查 ----------

    /// <summary>边界上不是 # / X、又不在出口那一格范围里的格子（角上的格子要同时在两条边的出口范围里才算出口）。</summary>
    public static List<(Vector2I Cell, ExitSide Side)> BoundaryLeaks(RoomDef room, RoomGrid grid, Vector2I cellSize)
    {
        var leaks = new List<(Vector2I, ExitSide)>();
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
            {
                if (r != 0 && r != grid.Rows - 1 && c != 0 && c != grid.Cols - 1) continue;
                if (IsPermanent(grid.At(c, r))) continue;
                var sides = new List<(ExitSide Side, int Index)>();
                if (c == 0) sides.Add((ExitSide.Left, r));
                if (c == grid.Cols - 1) sides.Add((ExitSide.Right, r));
                if (r == 0) sides.Add((ExitSide.Up, c));
                if (r == grid.Rows - 1) sides.Add((ExitSide.Down, c));
                var notAllowed = sides.Where(s => !InExitRange(room, grid, s.Side, s.Index, cellSize)).ToList();
                if (notAllowed.Count > 0) leaks.Add((new Vector2I(c, r), notAllowed[0].Side));
            }
        return leaks;
    }

    /// <summary>出口开口、非出口边界封闭（只看网格）。返回问题列表。</summary>
    public static List<string> ValidateRoom(RoomDef room, RoomGrid grid, Vector2I cellSize)
    {
        var errors = new List<string>();
        foreach (ExitDef exit in room.Exits.Where(e => e.Kind == ExitKind.Edge))
        {
            var (from, to) = ExitRange(grid, exit, cellSize);
            var runs = OpenRuns(grid, exit.Side, from, to);
            if (!runs.Any(r => r.B - r.A + 1 >= 2))
            {
                string axis = exit.Side is ExitSide.Left or ExitSide.Right ? $"第 {from}～{to} 行" : $"第 {from}～{to} 列";
                errors.Add($"{room.Id}：{SideName(exit.Side)}出口（通往 {exit.Target}）所在那一格的{SideName(exit.Side)}边（{axis}）没有至少 2 格的开口");
            }
            else if (exit.Side is ExitSide.Left or ExitSide.Right && FloorRow(grid, exit.Side, MainOpening(grid, exit.Side, from, to).Value) == null)
                errors.Add($"{room.Id}：{SideName(exit.Side)}出口（通往 {exit.Target}）的开口下面没有地面");
        }
        var leaks = BoundaryLeaks(room, grid, cellSize).Select(l => $"{SideName(l.Side)}边 ({l.Cell.X},{l.Cell.Y})“{grid.At(l.Cell.X, l.Cell.Y)}”").ToList();
        if (leaks.Count > 0)
            errors.Add($"{room.Id}：非出口的边界没有封闭（必须是 # 或 X），共 {leaks.Count} 格：{string.Join("、", leaks.Take(8))}");
        return errors;
    }

    /// <summary>出生点：放得下 48×48 的身体（脚在 feet，左右上和实心瓦片至少留 2 像素），并且脚下是实心或单向平台。返回问题描述，没问题返回 null。</summary>
    public static string SpawnProblem(RoomGrid grid, Vector2 feet)
    {
        var body = new Rect2(feet.X - BodySize.X / 2f - BodyGap, feet.Y - BodySize.Y - BodyGap, BodySize.X + BodyGap * 2f, BodySize.Y + BodyGap);
        int c0 = Mathf.FloorToInt(body.Position.X / Tile), c1 = Mathf.FloorToInt(body.End.X / Tile);
        int r0 = Mathf.FloorToInt((body.Position.Y + grid.Shift) / Tile), r1 = Mathf.FloorToInt((body.End.Y + grid.Shift) / Tile);
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
                if (RoomGrid.IsSolidChar(grid.At(c, r), true) && grid.TileRect(c, r).Intersects(body))
                    return $"身体和实心格 ({c},{r})“{grid.At(c, r)}”之间不到 {BodyGap} 像素";
        Vector2I at = CellOf(grid, feet);
        char below = grid.At(Mathf.FloorToInt(feet.X / Tile), at.Y + 1);
        if (!Standable(below)) return $"脚下 ({Mathf.FloorToInt(feet.X / Tile)},{at.Y + 1})“{below}”不是实心或单向平台";
        return null;
    }

    // ---------- 直接检查房间场景（编辑器警告、测试） ----------

    /// <summary>
    /// 把房间场景的地形原样读成网格（不补天花板、不补地面以下、不封边——缺口就是缺口）。
    /// outside：铺到房间外面的瓦片数。场景不是 32 像素网格等问题记进 problems。
    /// </summary>
    public static RoomGrid SceneGrid(RoomDef room, Node root, Vector2I cellSize, List<string> problems, out int outside)
    {
        outside = 0;
        RoomGrid grid = RoomGrid.CreateEmpty(room, cellSize);
        var geometry = root.GetNodeOrNull<TileMapLayer>("Geometry");
        if (geometry == null)
        {
            problems.Add("没有 Geometry 瓦片层");
            return grid;
        }
        int srcTile = geometry.TileSet?.TileSize.X ?? Tile;
        int srcShift = Mathf.RoundToInt(-geometry.Position.Y);
        if (srcTile % Tile != 0)
        {
            problems.Add($"Geometry 的瓦片尺寸 {srcTile} 不是 {Tile} 的倍数");
            return grid;
        }
        foreach (Vector2I cell in geometry.GetUsedCells())
        {
            char ch = TerrainTiles.CharForGeometry(geometry.GetCellAtlasCoords(cell));
            for (int dy = 0; dy < srcTile; dy += Tile)
                for (int dx = 0; dx < srcTile; dx += Tile)
                {
                    int px = cell.X * srcTile + dx, py = cell.Y * srcTile - srcShift + dy;
                    int col = Mathf.FloorToInt(px / (float)Tile), row = Mathf.FloorToInt((py + grid.Shift) / (float)Tile);
                    if (grid.InBounds(col, row)) grid.Set(col, row, ch);
                    else outside++;
                }
        }
        if (root.GetNodeOrNull(ConditionalTerrain.NodeName) is ConditionalTerrain conditional)
            foreach (Vector2I cell in conditional.GetUsedCells()) grid.Set(cell.X, cell.Y, RoomGrid.Conditional);
        if (root.GetNodeOrNull(BreakableTerrain.ContainerName) is Node breakables)
            foreach (BreakableTerrain block in breakables.GetChildren().OfType<BreakableTerrain>())
                foreach (Vector2I cell in block.Cells) grid.Set(cell.X, cell.Y, block.HiddenWall ? RoomGrid.HiddenBreakable : RoomGrid.Breakable);
        return grid;
    }

    /// <summary>
    /// 检查房间场景本身（不看文字图）：边缘缺口但没有出口、出口被堵、出生点悬空或卡在砖里、砖铺到房间外。
    /// 返回给人看的问题列表（空表示没问题）。layout 为 null 时读默认房间清单。
    /// </summary>
    public static List<string> AuditScene(RoomRoot root, WorldLayout layout = null)
    {
        var problems = new List<string>();
        if (root == null || string.IsNullOrEmpty(root.RoomId)) return problems;
        layout ??= ResourceLoader.Exists(WorldLayout.DefaultPath) ? GD.Load<WorldLayout>(WorldLayout.DefaultPath) : null;
        RoomDef room = layout?.Find(root.RoomId);
        if (room == null) return problems;
        Vector2I cellSize = CellSize();
        if (cellSize.X <= 0 || cellSize.Y <= 0) return problems;

        RoomGrid grid = SceneGrid(room, root, cellSize, problems, out int outside);
        foreach (string error in ValidateRoom(room, grid, cellSize))
            problems.Add(Readable(error, room));
        if (outside > 0) problems.Add($"有 {outside} 格砖铺到了房间外面（不影响游戏，多半是误操作，可以删掉）。");
        // 物件的判定框被拖离了节点：画面上看着在那里，但节点（以及按节点位置找它的提示、测试、文字图）还在原处
        if (root.GetNodeOrNull("Objects") is Node objects)
            foreach (Node2D item in objects.GetChildren().OfType<Node2D>())
                foreach (CollisionShape2D shape in item.GetChildren().OfType<CollisionShape2D>())
                    if (Mathf.Abs(shape.Position.X) > MaxShapeDrift)
                        problems.Add($"{item.Name} 的判定框 {shape.Name} 被拖离了节点 {Mathf.Abs(shape.Position.X):0} 像素：要挪位置请选中 {item.Name} 节点本身来挪，判定框放回节点上。");
        if (root.GetNodeOrNull("Spawns") is Node spawns)
            foreach (SpawnPoint spawn in spawns.GetChildren().OfType<SpawnPoint>())
            {
                string problem = SpawnProblem(grid, spawn.Position);
                if (problem != null) problems.Add($"出生点 {spawn.SpawnId}（{spawn.Position}）：{problem}。{SpawnHint(grid, spawn.Position)}");
            }
        return problems;
    }

    /// <summary>出生点下面最近的地面在哪（帮人判断是悬空还是埋进砖里）。</summary>
    private static string SpawnHint(RoomGrid grid, Vector2 feet)
    {
        int col = Mathf.FloorToInt(feet.X / Tile);
        Vector2I at = CellOf(grid, feet);
        if (grid.InBounds(col, at.Y) && RoomGrid.IsSolidChar(grid.At(col, at.Y), true)) return "它在砖里面。";
        for (int r = at.Y + 1; r < grid.Rows; r++)
            if (Standable(grid.At(col, r)))
                return $"下面 {r * Tile - grid.Shift - feet.Y:0} 像素才有地面（y = {r * Tile - grid.Shift}）。";
        return "下面一直到房间底都没有地面。";
    }

    /// <summary>把检查结果里的“#  或 X”等写法换成更直白的说法。</summary>
    private static string Readable(string error, RoomDef room) =>
        error.Replace($"{room.Id}：", "").Replace("非出口的边界没有封闭（必须是 # 或 X）", "房间边缘有缺口，但那里没有出口（玩家会走出房间）");

    public static Vector2I CellSize()
    {
        string path = ProjectSettings.GetSetting("addons/metroidvania_system/settings_file", "").AsString();
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) return Vector2I.Zero;
        Vector2 cell = GD.Load<Resource>(path).Get("in_game_cell_size").AsVector2();
        return new Vector2I((int)cell.X, (int)cell.Y);
    }
}
