using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using SonOfMud.Core;
using SonOfMud.Save;
using SonOfMud.World;

namespace SonOfMud.Tools;

/// <summary>
/// 关卡几何生成（关卡几何计划 v3 第 3.2 节），BuildWorld 的 --export-text / --geometry 两种模式都用它，测试也直接用它。
///
/// --export-text（<see cref="ExportAll"/>）：把现有房间场景按像素位置原样转成 32 像素文字图：
///   旧瓦片（64 像素）按像素换算；顶边没有出口的地方补一行天花板；地面以下到房间底边补满（不可达区域，保证底边封闭）；
///   Default → P、Save → S、其他出生点 → 数字（@spawn）、Objects 下的节点 → 锚点（@anchor；条件门和存档点除外）。
/// --geometry（<see cref="RunGeometry"/>）：按文字图重写 Geometry 瓦片层、条件地形、可破坏物、出生点、锚点物件、条件门位置、出口箭头，
///   其他节点和属性一律不动。先做全部校验，任一条不过就报错，一个文件都不写。
///
/// 锚点和出生点的“就近不动”：节点的脚底已经在那一格里（x 在格子左右边界内，y 在格子底边或底边上方 1 像素以内）就不动，
/// 否则挪到格子底边中点（出生点放在底边上方 1 像素，和旧骨架一致）。
/// </summary>
public sealed class GeometryBuilder
{
    public const int Tile = RoomGrid.Tile;
    public const string GeometryName = "Geometry";
    public const string MarkersName = "ExitMarkers";
    /// <summary>出生点检查用的玩家碰撞体和间隙、跨房间开口最少重叠（规则本身在 World 的 RoomRules，编辑器里的房间检查也用它）。</summary>
    public static readonly Vector2 BodySize = RoomRules.BodySize;
    public const float BodyGap = RoomRules.BodyGap;
    public const int MinOverlap = RoomRules.MinOverlap;
    public static readonly Color MarkerColor = new(1f, 1f, 1f, 0.35f);

    public WorldLayout Layout { get; }
    public Vector2I Cell { get; }

    public List<string> Errors { get; } = new();
    public List<string> Changes { get; } = new();
    public List<string> Notes { get; } = new();
    public List<string> Written { get; } = new();

    /// <summary>用这些文字代替磁盘上的文字图（测试用）。</summary>
    public Dictionary<string, string> TextOverrides { get; } = new();
    /// <summary>房间场景写到哪里（默认原路径；测试改到 user://）。</summary>
    public Func<RoomDef, string> SceneOutput { get; set; } = room => room.ScenePath;
    /// <summary>文字图写到哪里。</summary>
    public Func<RoomDef, string> TextOutput { get; set; } = room => RoomGrid.PathFor(room.Id);
    public string TileSetOutput { get; set; } = TerrainTiles.TileSetPath;

    public GeometryBuilder(WorldLayout layout, Vector2I cellSize)
    {
        Layout = layout;
        Cell = cellSize;
    }

    public static GeometryBuilder ForProject() =>
        new(GD.Load<WorldLayout>(WorldLayout.DefaultPath), new Vector2I((int)MetSysApi.CellSize.X, (int)MetSysApi.CellSize.Y));

    // =====================================================================
    // 文字图导出
    // =====================================================================

    /// <summary>导出全部房间的文字图。overwrite 为 false 时跳过已有文字图的房间。有任何错误就一个都不写。</summary>
    public bool ExportAll(bool write, bool overwrite, ISet<string> only = null)
    {
        var texts = new Dictionary<RoomDef, string>();
        foreach (RoomDef room in Layout.Rooms)
        {
            if (only != null && !only.Contains(room.Id)) continue;
            bool exists = FileAccess.FileExists(TextOutput(room));
            if (!overwrite && exists) continue;
            Node root = LoadScene(room, false);
            if (root == null) continue;
            string text = ExportRoom(room, root);
            FreeRoom(root);
            if (text == null) continue;
            texts[room] = exists ? KeepDescription(FileAccess.GetFileAsString(TextOutput(room)), text) : text;
        }
        if (Errors.Count > 0 || !write) return Errors.Count == 0;
        foreach (var (room, text) in texts)
        {
            string path = TextOutput(room);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path.GetBaseDir()));
            using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file == null) { Errors.Add($"{room.Id}：写 {path} 失败：{FileAccess.GetOpenError()}"); continue; }
            file.StoreString(text);
            Written.Add(path);
        }
        return Errors.Count == 0;
    }

    /// <summary>覆盖已有文字图时，保留原来开头写的说明注释（导出器自己生成的注释不重复保留）。</summary>
    public static string KeepDescription(string oldText, string newText)
    {
        var keep = new List<string>();
        foreach (string line in oldText.Replace("\r\n", "\n").Split('\n'))
        {
            if (!line.StartsWith("#")) break;
            if (line.Contains("BuildWorld --export-text") || line.StartsWith("# 封边") || line.Contains("写在 (")) continue;
            keep.Add(line);
        }
        if (keep.Count == 0) return newText;
        // 导出器的第一行注释是“# 房间 显示名”，已有更详细的说明时不重复
        string body = newText.StartsWith("# ") ? newText[(newText.IndexOf('\n') + 1)..] : newText;
        return string.Join("\n", keep) + "\n" + body;
    }

    /// <summary>把一个房间场景原样转成文字图；出错时返回 null，错误记进 Errors。</summary>
    public string ExportRoom(RoomDef room, Node root)
    {
        int errorsBefore = Errors.Count;
        void Fail(string message) => Errors.Add($"{room.Id}：{message}");
        RoomGrid grid = RoomGrid.CreateEmpty(room, Cell);

        var geometry = root.GetNodeOrNull<TileMapLayer>(GeometryName);
        if (geometry == null) { Fail("没有 Geometry 瓦片层"); return null; }
        int srcTile;
        using (TileSet srcTiles = geometry.TileSet) srcTile = srcTiles?.TileSize.X ?? 64;
        int srcShift = Mathf.RoundToInt(-geometry.Position.Y);
        if (srcTile % Tile != 0) { Fail($"Geometry 的瓦片尺寸 {srcTile} 不是 {Tile} 的倍数"); return null; }

        int maxRow = -1, dropped = 0;
        void Paint(int px, int py, char ch)
        {
            if (Mathf.PosMod(py + grid.Shift, Tile) != 0 || Mathf.PosMod(px, Tile) != 0) { Fail($"像素 ({px},{py}) 不在 32 像素网格上"); return; }
            int col = Mathf.FloorToInt(px / (float)Tile), row = Mathf.FloorToInt((py + grid.Shift) / (float)Tile);
            if (!grid.InBounds(col, row)) { dropped++; return; }
            grid.Set(col, row, ch);
            maxRow = Math.Max(maxRow, row);
        }
        foreach (Vector2I cell in geometry.GetUsedCells())
        {
            char ch = TerrainTiles.CharForGeometry(geometry.GetCellAtlasCoords(cell));
            for (int dy = 0; dy < srcTile; dy += Tile)
                for (int dx = 0; dx < srcTile; dx += Tile)
                    Paint(cell.X * srcTile + dx, cell.Y * srcTile - srcShift + dy, ch);
        }
        if (root.GetNodeOrNull(ConditionalTerrain.NodeName) is ConditionalTerrain conditional)
        {
            foreach (Vector2I cell in conditional.GetUsedCells()) grid.Set(cell.X, cell.Y, RoomGrid.Conditional);
            if (conditional.Requirement == null || conditional.Requirement.Flags.Count != 1 || conditional.Requirement.Abilities.Count != 0)
                Fail("ConditionalTerrain 的条件必须恰好是一个剧情标记");
            else grid.SetCondition(conditional.Requirement.Flags[0]);
        }
        if (root.GetNodeOrNull(BreakableTerrain.ContainerName) is Node breakables)
            foreach (BreakableTerrain block in breakables.GetChildren().OfType<BreakableTerrain>())
                foreach (Vector2I cell in block.Cells) grid.Set(cell.X, cell.Y, block.HiddenWall ? RoomGrid.HiddenBreakable : RoomGrid.Breakable);

        // 地面以下到房间底边：照抄最低一行（不可达区域，保证底边封闭，下方出口的开口一直通到底边）
        int filled = 0;
        if (maxRow >= 0)
            for (int r = maxRow + 1; r < grid.Rows; r++)
                for (int c = 0; c < grid.Cols; c++)
                {
                    char below = grid.At(c, maxRow);
                    grid.Set(c, r, below);
                    if (below != RoomGrid.Empty) filled++;
                }

        // 天花板：顶边不在上方出口那一格里的地方补实心
        var upCols = new HashSet<int>();
        foreach (ExitDef exit in room.Exits.Where(e => e.Kind == ExitKind.Edge && e.Side == ExitSide.Up))
        {
            var (from, to) = ExitRange(room, grid, exit);
            for (int c = from; c <= to; c++) upCols.Add(c);
        }
        int ceiling = 0;
        for (int c = 0; c < grid.Cols; c++)
            if (!upCols.Contains(c) && grid.At(c, 0) == RoomGrid.Empty)
            {
                grid.Set(c, 0, RoomGrid.Solid);
                ceiling++;
            }

        // 封边：旧骨架按 64 像素行挖出口，挖掉的行可能超出出口那一格 1 行（32 像素），落在不是出口的那一格的边界上，补成实心
        var sealedCells = new List<string>();
        foreach (var (cell, side) in BoundaryLeaks(room, grid))
        {
            grid.Set(cell.X, cell.Y, RoomGrid.Solid);
            sealedCells.Add($"{SideName(side)}边 ({cell.X},{cell.Y})");
        }

        // 标记：P、S、数字出生点、锚点
        var nudges = new List<string>();
        bool Place(char ch, Vector2 pos, string label)
        {
            Vector2I at = CellOf(grid, pos);
            if (!grid.InBounds(at.X, at.Y)) { Fail($"{label} 的位置 {pos} 在房间外"); return false; }
            char current = grid.At(at.X, at.Y);
            if (current == RoomGrid.Empty) { grid.Set(at.X, at.Y, ch); return true; }
            if (!RoomGrid.IsMarkerChar(current)) { Fail($"{label} 的位置 {pos} 落在实心格 ({at.X},{at.Y})“{current}”里"); return false; }
            bool standable = Standable(grid.At(at.X, at.Y + 1));
            // 同一格已经有别的标记：挪到同一行里离原位置最近的空格
            foreach (int c in Enumerable.Range(at.X - 6, 13).OrderBy(c => Mathf.Abs(grid.FeetAt(c, at.Y).X - pos.X)).ThenBy(c => c))
            {
                if (c == at.X || !grid.InBounds(c, at.Y) || grid.At(c, at.Y) != RoomGrid.Empty || Standable(grid.At(c, at.Y + 1)) != standable) continue;
                grid.Set(c, at.Y, ch);
                nudges.Add($"{label} 和“{current}”同一格 ({at.X},{at.Y})，写在 ({c},{at.Y})");
                return true;
            }
            Fail($"{label} 和“{current}”同一格 ({at.X},{at.Y})，附近也找不到空格");
            return false;
        }

        var spawnRoot = root.GetNodeOrNull("Spawns");
        var objects = root.GetNodeOrNull("Objects");
        if (spawnRoot == null || objects == null) { Fail("没有 Spawns 或 Objects"); return null; }
        List<SpawnPoint> spawns = spawnRoot.GetChildren().OfType<SpawnPoint>().ToList();
        SpawnPoint def = spawns.FirstOrDefault(s => s.SpawnId == SpawnPoint.DefaultId);
        if (def == null) Fail("没有 Default 出生点");
        else Place(RoomGrid.DefaultSpawn, def.Position, "Default");
        SpawnPoint save = spawns.FirstOrDefault(s => s.SpawnId == SpawnPoint.SaveId);
        SavePoint savePoint = objects.GetChildren().OfType<SavePoint>().FirstOrDefault();
        if (room.HasSavePoint)
        {
            if (save == null || savePoint == null) Fail("房间表有存档点，但场景缺 Save 出生点或 SavePoint 节点");
            else if (CellOf(grid, save.Position) != CellOf(grid, savePoint.Position)) Fail($"Save 出生点 {save.Position} 和存档点 {savePoint.Position} 不在同一格，S 只能表示一个位置");
            else Place(RoomGrid.SavePoint, save.Position, "Save");
        }
        char digit = '1';
        foreach (SpawnPoint spawn in spawns.Where(s => s.SpawnId != SpawnPoint.DefaultId && s.SpawnId != SpawnPoint.SaveId))
        {
            if (digit > '9') { Fail($"出生点超过 9 个（{spawn.SpawnId} 没有数字可用）"); break; }
            if (Place(digit, spawn.Position, spawn.SpawnId)) grid.SetSpawn(digit, spawn.SpawnId);
            digit++;
        }
        char letter = 'a';
        foreach (Node2D node in objects.GetChildren().OfType<Node2D>().Where(n => n is not GateBarrier && n is not SavePoint))
        {
            if (letter > 'z') { Fail($"锚点超过 26 个（{node.Name} 没有字母可用）"); break; }
            if (Place(letter, node.Position, node.Name)) grid.SetAnchor(letter, node.Name);
            letter++;
        }
        if (Errors.Count > errorsBefore) return null;

        grid.AddComment($"{room.Id} {room.DisplayName}");
        grid.AddComment($"由 BuildWorld --export-text 从 {srcTile} 像素瓦片原样转换：补天花板 {ceiling} 格，地面以下补满 {filled} 格，房间外丢弃 {dropped} 格");
        if (sealedCells.Count > 0) grid.AddComment($"封边 {sealedCells.Count} 格：{string.Join("、", sealedCells)}");
        foreach (string nudge in nudges) grid.AddComment(nudge);
        if (sealedCells.Count > 0) Notes.Add($"{room.Id}：封边 {sealedCells.Count} 格：{string.Join("、", sealedCells)}");
        foreach (string nudge in nudges) Notes.Add($"{room.Id}：{nudge}");
        Notes.Add($"{room.Id}：补天花板 {ceiling} 格，地面以下补满 {filled} 格，房间外丢弃 {dropped} 格");

        string text = grid.Serialize();
        if (RoomGrid.Parse(text, room, Cell, out string parseError) == null) { Fail("导出的文字图解析不通过：" + parseError); return null; }
        return text;
    }

    // =====================================================================
    // 按文字图生成
    // =====================================================================

    /// <summary>
    /// 读全部文字图 → 校验 → 算出每个房间要改的地方。write 为 false 是干跑：只把会变化的房间和节点写进 Changes。
    /// 有任何错误时一个文件都不写，返回 false。
    /// </summary>
    public bool RunGeometry(bool write)
    {
        // 用到的 Godot 资源都在用完后放掉托管包装（using），不然 C# 一直拿着它们，退出时会报 Shape2D 泄漏
        using TileSet expectedTiles = TerrainTiles.Build();
        string currentDescription;
        using (TileSet currentTiles = ResourceLoader.Exists(TerrainTiles.TileSetPath) ? ResourceLoader.Load<TileSet>(TerrainTiles.TileSetPath, null, ResourceLoader.CacheMode.Ignore) : null)
            currentDescription = TerrainTiles.Describe(currentTiles);
        bool tilesStale = currentDescription != TerrainTiles.Describe(expectedTiles);
        if (tilesStale) Changes.Add("瓦片集：world_tileset.tres 需要按 32 像素重新生成");

        var grids = new Dictionary<string, RoomGrid>();
        foreach (RoomDef room in Layout.Rooms)
        {
            string text = ReadText(room);
            if (text == null) continue;
            RoomGrid grid = RoomGrid.Parse(text, room, Cell, out string error);
            if (grid == null) { Errors.Add(error); continue; }
            grids[room.Id] = grid;
        }
        foreach (var (id, grid) in grids) ValidateRoom(Layout.Find(id), grid);
        ValidateAlignment(grids);

        // 先用普通实例算出每个房间的改动（干跑只做这一步）
        var pending = new List<(RoomDef Room, RoomGrid Grid)>();
        foreach (var (id, grid) in grids)
        {
            RoomDef room = Layout.Find(id);
            Node root = LoadScene(room, false);
            if (root == null) continue;
            List<string> changes = Plan(room, grid, root);
            FreeRoom(root);
            if (changes.Count == 0) continue;
            Changes.AddRange(changes.Select(c => $"{id}：{c}"));
            pending.Add((room, grid));
        }
        if (Errors.Count > 0 || !write) return Errors.Count == 0;

        // 写入：只对有改动的房间按“编辑态”重新实例化（保留子场景实例的覆盖关系，打包时不会把子场景的默认值写成覆盖），再改一遍、打包
        if (tilesStale) Save(expectedTiles, TileSetOutput);
        foreach (var (room, grid) in pending)
        {
            Node root = LoadScene(room, true);
            if (root == null) continue;
            Plan(room, grid, root);
            using var packed = new PackedScene();
            Error packResult = packed.Pack(root);
            FreeRoom(root);
            if (packResult != Error.Ok) { Errors.Add($"{room.Id}：打包失败 {packResult}"); continue; }
            Save(packed, SceneOutput(room));
        }
        return Errors.Count == 0;
    }

    private string ReadText(RoomDef room)
    {
        if (TextOverrides.TryGetValue(room.Id, out string text)) return text;
        string path = RoomGrid.PathFor(room.Id);
        return FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null;
    }

    private void Save(Resource resource, string path)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path.GetBaseDir()));
        // 覆盖已有文件时保留原来的 UID（MetSys 的 MapData.txt 和其他场景按 UID 引用）：
        // 新建的资源对象存盘时会丢掉原来的 UID，存完再写回去（存完后 GetResourceUid 读到的是缓存，不能用来判断，所以一律写回）。
        // 新文件用存盘时分配的 UID。
        long uid = ResourceLoader.Exists(path) ? ResourceLoader.GetResourceUid(path) : ResourceUid.InvalidId;
        Error result = ResourceSaver.Save(resource, path);
        if (result == Error.Ok && uid != ResourceUid.InvalidId)
        {
            result = ResourceSaver.SetUid(path, uid);
            if (ResourceUid.HasId(uid)) ResourceUid.SetId(uid, path);
            else ResourceUid.AddId(uid, path);
        }
        if (result != Error.Ok) { Errors.Add($"保存 {path} 失败：{result}"); return; }
        Written.Add(path);
    }

    /// <summary>
    /// 释放房间实例，并立刻让 .NET 的终结器跑完。
    /// 房间里的 C# 脚本对象（敌人、门、剧情触发器等）被释放后，它们的托管包装要靠终结器线程回收；
    /// 实测连续实例化几百个房间时，终结器线程和主线程同时操作 Godot 对象偶尔会让引擎崩溃（gchandle.is_released 断言，退出码 132），
    /// 所以每释放一个房间就在主线程上等终结器跑完，不让它和下一个房间的实例化同时进行。
    /// </summary>
    private static void FreeRoom(Node root)
    {
        root.Free();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private Node LoadScene(RoomDef room, bool forEditing)
    {
        if (!ResourceLoader.Exists(room.ScenePath)) { Errors.Add($"{room.Id}：房间场景不存在 {room.ScenePath}"); return null; }
        using var packed = ResourceLoader.Load<PackedScene>(room.ScenePath); // 用完就放掉托管包装，场景资源不会被 C# 一直拿着
        Node root = forEditing ? packed?.Instantiate(PackedScene.GenEditState.Instance) : packed?.Instantiate();
        if (root == null) Errors.Add($"{room.Id}：房间场景实例化失败");
        return root;
    }

    // ---------- 校验 ----------

    /// <summary>出口开口、非出口边界封闭（只看文字图）。出生点的检查在 Plan 里做（要用节点的最终位置）。</summary>
    public void ValidateRoom(RoomDef room, RoomGrid grid) => Errors.AddRange(RoomRules.ValidateRoom(room, grid, Cell));

    /// <summary>边界上不是 # / X、又不在出口那一格范围里的格子（角上的格子要同时在两条边的出口范围里才算出口）。</summary>
    public List<(Vector2I Cell, ExitSide Side)> BoundaryLeaks(RoomDef room, RoomGrid grid) => RoomRules.BoundaryLeaks(room, grid, Cell);

    /// <summary>跨房间对齐：边缘换房保持全局坐标，所以两边的开口要在全局坐标上对得上。</summary>
    public void ValidateAlignment(IReadOnlyDictionary<string, RoomGrid> grids)
    {
        foreach (var (id, grid) in grids)
        {
            RoomDef a = Layout.Find(id);
            foreach (ExitDef exit in a.Exits.Where(e => e.Kind == ExitKind.Edge))
            {
                RoomDef b = Layout.Find(exit.Target);
                if (b == null || string.CompareOrdinal(a.Id, b.Id) > 0 || !grids.TryGetValue(b.Id, out RoomGrid other)) continue;
                Vector2I next = a.ExitCell(exit) + RoomDef.Step(exit.Side);
                ExitDef back = b.Exits.FirstOrDefault(e => e.Kind == ExitKind.Edge && e.Target == a.Id && e.Side == RoomDef.Opposite(exit.Side) && b.ExitCell(e) == next);
                if (back == null) { Errors.Add($"{a.Id}↔{b.Id}：{b.Id} 没有对应的回程出口"); continue; }
                var (fromA, toA) = ExitRange(a, grid, exit);
                var (fromB, toB) = ExitRange(b, other, back);
                if (exit.Side is ExitSide.Left or ExitSide.Right)
                {
                    var runA = MainOpening(grid, exit.Side, fromA, toA);
                    var runB = MainOpening(other, back.Side, fromB, toB);
                    if (runA == null || runB == null) continue; // 已在单个房间的校验里报过
                    float a0 = GlobalY(a, grid, runA.Value.A), a1 = GlobalY(a, grid, runA.Value.B + 1);
                    float b0 = GlobalY(b, other, runB.Value.A), b1 = GlobalY(b, other, runB.Value.B + 1);
                    float overlap = Mathf.Min(a1, b1) - Mathf.Max(a0, b0);
                    if (overlap < MinOverlap)
                        Errors.Add($"{a.Id}↔{b.Id}：左右出口开口的竖向重叠只有 {Mathf.Max(overlap, 0)} 像素（至少 {MinOverlap}）：{a.Id} 全局 Y {a0}～{a1}，{b.Id} 全局 Y {b0}～{b1}");
                    int? floorA = FloorRow(grid, exit.Side, runA.Value), floorB = FloorRow(other, back.Side, runB.Value);
                    if (floorA == null || floorB == null) continue;
                    float ga = GlobalY(a, grid, floorA.Value), gb = GlobalY(b, other, floorB.Value);
                    if (!Mathf.IsEqualApprox(ga, gb))
                        Errors.Add($"{a.Id}↔{b.Id}：开口处的地面不在同一个全局 Y：{a.Id} 是 {ga}，{b.Id} 是 {gb}（全局 X {GlobalX(a, exit.Side == ExitSide.Right ? grid.Cols : 0)}）");
                }
                else
                {
                    var runsA = OpenRuns(grid, exit.Side, fromA, toA).Where(r => r.B - r.A + 1 >= 2).ToList();
                    var runsB = OpenRuns(other, back.Side, fromB, toB).Where(r => r.B - r.A + 1 >= 2).ToList();
                    if (runsA.Count == 0 || runsB.Count == 0) continue;
                    float best = float.MinValue;
                    string detail = "";
                    foreach (var ra in runsA)
                        foreach (var rb in runsB)
                        {
                            float a0 = GlobalX(a, ra.A), a1 = GlobalX(a, ra.B + 1), b0 = GlobalX(b, rb.A), b1 = GlobalX(b, rb.B + 1);
                            float overlap = Mathf.Min(a1, b1) - Mathf.Max(a0, b0);
                            if (overlap > best) { best = overlap; detail = $"{a.Id} 全局 X {a0}～{a1}，{b.Id} 全局 X {b0}～{b1}"; }
                        }
                    if (best < MinOverlap)
                        Errors.Add($"{a.Id}↔{b.Id}：上下出口开口的横向重叠只有 {Mathf.Max(best, 0)} 像素（至少 {MinOverlap}）：{detail}");
                }
            }
        }
    }

    private float GlobalY(RoomDef room, RoomGrid grid, int row) => room.Origin.Y * Cell.Y + row * Tile - grid.Shift;
    private float GlobalX(RoomDef room, int col) => room.Origin.X * Cell.X + col * Tile;

    /// <summary>出生点：放得下 48×48 的身体（脚在 feet，左右上和实心瓦片至少留 2 像素），并且脚下是实心或单向平台。返回问题描述，没问题返回 null。</summary>
    public static string SpawnProblem(RoomGrid grid, Vector2 feet) => RoomRules.SpawnProblem(grid, feet);

    // ---------- 生成（改节点） ----------

    /// <summary>按文字图修改房间场景（只改几何相关的节点），返回改动列表；出错记进 Errors。</summary>
    public List<string> Plan(RoomDef room, RoomGrid grid, Node root)
    {
        var changes = new List<string>();
        void Fail(string message) => Errors.Add($"{room.Id}：{message}");
        int shift = grid.Shift;
        float width = room.Size.X * Cell.X;

        // ① Geometry 瓦片层
        var geometry = root.GetNodeOrNull<TileMapLayer>(GeometryName);
        if (geometry == null) { Fail("没有 Geometry 瓦片层"); return changes; }
        var want = new Dictionary<Vector2I, Vector2I>();
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
                if (grid.At(c, r) is RoomGrid.Solid or RoomGrid.NoClimb or RoomGrid.OneWay)
                    want[new Vector2I(c, r)] = TerrainTiles.AtlasFor(grid.At(c, r)).Value;
        var have = new Dictionary<Vector2I, Vector2I>();
        foreach (Vector2I cell in geometry.GetUsedCells())
            have[cell] = geometry.GetCellSourceId(cell) == TerrainTiles.SourceId ? geometry.GetCellAtlasCoords(cell) : new Vector2I(-1, -1);
        bool sameTiles = have.Count == want.Count && want.All(kv => have.TryGetValue(kv.Key, out Vector2I v) && v == kv.Value);
        // 场景里存的瓦片数据还带着已删除的格子（不在场景树里的 TileMapLayer 删格子只做标记，存盘时会一起写出去），也算要重写
        bool stale = geometry.TileMapData.Length != EncodeTiles(have).Length;
        string layerTiles;
        using (TileSet current = geometry.TileSet) layerTiles = current?.ResourcePath;
        if (!sameTiles || stale || layerTiles != TerrainTiles.TileSetPath || geometry.Position != new Vector2(0, -shift))
        {
            using (TileSet tiles = GD.Load<TileSet>(TerrainTiles.TileSetPath)) geometry.TileSet = tiles;
            geometry.Position = new Vector2(0, -shift);
            geometry.TileMapData = EncodeTiles(want);
            geometry.UpdateInternals();
            changes.Add($"Geometry 瓦片层重写（{want.Count} 格）");
        }
        int insertAt = geometry.GetIndex() + 1;

        // ② 条件地形
        List<Vector2I> rCells = grid.FindAll(RoomGrid.Conditional).ToList();
        Node oldConditional = root.GetNodeOrNull(ConditionalTerrain.NodeName);
        string wantR = rCells.Count == 0 ? "" : ConditionalSignature(rCells, grid.Condition, shift);
        string haveR = oldConditional is ConditionalTerrain ct
            ? ConditionalSignature(ct.GetUsedCells(), ct.Requirement?.Flags.Count == 1 && ct.Requirement.Abilities.Count == 0 ? ct.Requirement.Flags[0].ToString() : "?", Mathf.RoundToInt(-ct.Position.Y))
                + (ct.GetUsedCells().All(c => ct.GetCellAtlasCoords(c) == TerrainTiles.SolidAtlas) ? "" : "!atlas")
            : oldConditional == null ? "" : "?";
        if (wantR != haveR)
        {
            Remove(oldConditional);
            if (rCells.Count > 0)
            {
                ConditionalTerrain node = ConditionalTerrain.Create(rCells, grid.Condition, shift);
                Insert(root, node, insertAt);
            }
            changes.Add(rCells.Count > 0 ? $"ConditionalTerrain 重建（{rCells.Count} 格，条件 {grid.Condition}）" : "删除 ConditionalTerrain");
        }
        if (rCells.Count > 0) insertAt++;

        // ③ 可破坏物
        List<BreakableTerrain> wantBlocks = BreakableBlocks(room, grid);
        Node oldBlocks = root.GetNodeOrNull(BreakableTerrain.ContainerName);
        string wantB = string.Join("\n", wantBlocks.Select(BreakableSignature));
        string haveB = oldBlocks == null ? "" : string.Join("\n", oldBlocks.GetChildren().Select(n => n is BreakableTerrain b ? BreakableSignature(b) : "?" + n.Name));
        if (wantB != haveB)
        {
            Remove(oldBlocks);
            if (wantBlocks.Count > 0)
            {
                var container = new Node2D { Name = BreakableTerrain.ContainerName };
                Insert(root, container, insertAt);
                foreach (BreakableTerrain block in wantBlocks) Own(root, container, block);
            }
            changes.Add(wantBlocks.Count > 0 ? $"Breakables 重建（{wantBlocks.Count} 块）" : "删除 Breakables");
        }
        else foreach (BreakableTerrain block in wantBlocks) block.Free();
        if (wantBlocks.Count > 0) insertAt++;

        // ④ 出口箭头
        Node2D wantMarkers = BuildMarkers(room, grid);
        Node oldMarkers = root.GetNodeOrNull(MarkersName);
        if (MarkersSignature(wantMarkers) != MarkersSignature(oldMarkers))
        {
            Remove(oldMarkers);
            Insert(root, wantMarkers, Math.Min(insertAt, root.GetChildCount()));
            foreach (Node child in wantMarkers.GetChildren()) child.Owner = root;
            changes.Add($"ExitMarkers 重建（{wantMarkers.GetChildCount()} 个箭头）");
        }
        else wantMarkers.Free();

        // ⑤ 出生点
        var spawnRoot = root.GetNodeOrNull<Node2D>("Spawns");
        var objects = root.GetNodeOrNull<Node2D>("Objects");
        if (spawnRoot == null || objects == null) { Fail("没有 Spawns 或 Objects"); return changes; }
        var targets = new List<(StringName Id, Vector2I Cell)>();
        void Target(StringName id, char ch)
        {
            if (grid.Find(ch) is { } at) targets.Add((id, at));
        }
        Target(SpawnPoint.DefaultId, RoomGrid.DefaultSpawn);
        Target(SpawnPoint.SaveId, RoomGrid.SavePoint);
        foreach (var (d, id) in grid.Spawns) Target(id, d);
        foreach (ExitDef exit in room.Exits)
        {
            StringName id = ExitDef.ArrivalSpawn(exit.Target);
            if (targets.Any(t => t.Id == id)) continue;
            if (exit.Kind == ExitKind.Edge && exit.Side is ExitSide.Left or ExitSide.Right)
            {
                var (from, to) = ExitRange(room, grid, exit);
                var run = MainOpening(grid, exit.Side, from, to);
                int? floor = run == null ? null : FloorRow(grid, exit.Side, run.Value);
                if (floor == null) { Fail($"推算不出到达出生点 {id}：{SideName(exit.Side)}出口没有带地面的开口"); continue; }
                targets.Add((id, new Vector2I(exit.Side == ExitSide.Left ? 3 : grid.Cols - 4, floor.Value - 1)));
            }
            else Fail($"{(exit.Kind == ExitKind.Door ? "门" : SideName(exit.Side) + "出口")}（通往 {exit.Target}）的到达出生点 {id} 必须在文字图里写数字出生点（@spawn）");
        }
        List<SpawnPoint> spawnNodes = spawnRoot.GetChildren().OfType<SpawnPoint>().ToList();
        foreach (SpawnPoint extra in spawnNodes.Where(s => targets.All(t => t.Id != s.SpawnId)))
            Fail($"场景里的出生点 {extra.SpawnId} 在文字图里没有对应（P / S / 数字）");
        foreach (var (id, cell) in targets)
        {
            SpawnPoint node = spawnNodes.FirstOrDefault(s => s.SpawnId == id);
            if (node == null)
            {
                node = new SpawnPoint { Name = id.ToString(), SpawnId = id, Position = grid.FeetAt(cell.X, cell.Y) - new Vector2(0, 1) };
                Own(root, spawnRoot, node);
                changes.Add($"新建出生点 {id} @ {node.Position}");
            }
            else if (!InPlace(grid, node.Position, cell))
            {
                Vector2 old = node.Position;
                node.Position = grid.FeetAt(cell.X, cell.Y) - new Vector2(0, 1);
                changes.Add($"出生点 {id}：{old} → {node.Position}");
            }
            string problem = SpawnProblem(grid, new Vector2(node.Position.X, grid.TileRect(cell.X, cell.Y).End.Y));
            if (problem != null) Fail($"出生点 {id}（格 {cell.X},{cell.Y}）放不下玩家：{problem}");
        }

        // ⑥ 存档点、锚点物件
        if (grid.Find(RoomGrid.SavePoint) is { } saveCell)
        {
            SavePoint savePoint = objects.GetChildren().OfType<SavePoint>().FirstOrDefault();
            if (savePoint == null) Fail("文字图有 S，但 Objects 下没有 SavePoint");
            else MoveObject(grid, savePoint, saveCell, changes);
        }
        foreach (var (letter, name) in grid.Anchors)
        {
            var node = objects.GetNodeOrNull<Node2D>(name);
            if (node == null) { Fail($"锚点 {letter} 找不到节点 Objects/{name}"); continue; }
            MoveObject(grid, node, grid.Find(letter).Value, changes);
        }

        // ⑦ 条件门挪到出口开口
        foreach (ExitDef exit in room.Exits.Where(e => e.HasBarrier))
        {
            var gate = objects.GetNodeOrNull<GateBarrier>("GateTo" + exit.Target);
            if (gate == null) { Fail($"找不到通往 {exit.Target} 的条件门 Objects/GateTo{exit.Target}"); continue; }
            if (exit.Kind != ExitKind.Edge || exit.Side is ExitSide.Up or ExitSide.Down)
            {
                Notes.Add($"{room.Id}：通往 {exit.Target} 的条件门在上下出口或门上，位置不由生成工具管");
                continue;
            }
            var (from, to) = ExitRange(room, grid, exit);
            var run = MainOpening(grid, exit.Side, from, to);
            int? floor = run == null ? null : FloorRow(grid, exit.Side, run.Value);
            if (floor == null) continue; // 已报错
            Shape2D gateShape = gate.GetNodeOrNull<CollisionShape2D>("Shape")?.Shape;
            float half = gateShape is RectangleShape2D rect ? rect.Size.X / 2f : Tile;
            gateShape?.Dispose(); // 只放掉托管包装（形状仍归场景所有），否则退出时会报 Shape2D 泄漏
            var at = new Vector2(exit.Side == ExitSide.Left ? half : width - half, floor.Value * Tile - shift);
            if (gate.Position != at)
            {
                changes.Add($"条件门 {gate.Name}：{gate.Position} → {at}");
                gate.Position = at;
            }
        }
        return changes;
    }

    /// <summary>
    /// TileMapLayer.tile_map_data 的格式：2 字节格式版本（0），然后每格 12 字节：x、y（int16），图集源、图集 x、图集 y、替代图块（uint16），小端。
    /// 按“从上到下、从左到右”的固定顺序写，结果可重复。
    /// </summary>
    public static byte[] EncodeTiles(IReadOnlyDictionary<Vector2I, Vector2I> cells)
    {
        var bytes = new List<byte> { 0, 0 };
        void Put(int value) { bytes.Add((byte)(value & 0xFF)); bytes.Add((byte)((value >> 8) & 0xFF)); }
        foreach (var (cell, atlas) in cells.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
        {
            Put(cell.X);
            Put(cell.Y);
            Put(TerrainTiles.SourceId);
            Put(atlas.X);
            Put(atlas.Y);
            Put(0);
        }
        return bytes.ToArray();
    }

    private static void MoveObject(RoomGrid grid, Node2D node, Vector2I cell, List<string> changes)
    {
        if (InPlace(grid, node.Position, cell)) return;
        Vector2 old = node.Position;
        node.Position = grid.FeetAt(cell.X, cell.Y);
        changes.Add($"物件 {node.Name}：{old} → {node.Position}");
    }

    private static void Remove(Node node)
    {
        if (node == null) return;
        node.GetParent()?.RemoveChild(node);
        node.Free();
    }

    private static void Insert(Node root, Node node, int index)
    {
        root.AddChild(node);
        root.MoveChild(node, Math.Min(index, root.GetChildCount() - 1));
        node.Owner = root;
    }

    private static void Own(Node root, Node parent, Node child)
    {
        parent.AddChild(child);
        child.Owner = root;
    }

    // ---------- 可破坏物 ----------

    /// <summary>B、H 各自按四连通分块；序号按“从上到下、从左到右第一次遇到”的顺序固定。</summary>
    public static List<BreakableTerrain> BreakableBlocks(RoomDef room, RoomGrid grid)
    {
        var blocks = new List<BreakableTerrain>();
        var seen = new HashSet<Vector2I>();
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
            {
                char ch = grid.At(c, r);
                var start = new Vector2I(c, r);
                if (!RoomGrid.IsBreakableChar(ch) || !seen.Add(start)) continue;
                var cells = new List<Vector2I>();
                var queue = new Queue<Vector2I>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Vector2I cur = queue.Dequeue();
                    cells.Add(cur);
                    foreach (Vector2I step in new[] { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down })
                    {
                        Vector2I n = cur + step;
                        if (grid.At(n.X, n.Y) == ch && grid.InBounds(n.X, n.Y) && seen.Add(n)) queue.Enqueue(n);
                    }
                }
                int index = blocks.Count;
                blocks.Add(BreakableTerrain.Create(cells, ch == RoomGrid.HiddenBreakable, BreakableTerrain.KeyFor(room.Id, index), grid.Shift, $"Breakable_{index}"));
            }
        return blocks;
    }

    private static string BreakableSignature(BreakableTerrain b) =>
        $"{b.Name}|{b.HiddenWall}|{b.BreakKey}|{b.Position}|{string.Join(";", b.Cells.OrderBy(c => c.Y).ThenBy(c => c.X))}";

    private static string ConditionalSignature(IEnumerable<Vector2I> cells, string flag, int shift) =>
        $"{flag}|{shift}|{string.Join(";", cells.OrderBy(c => c.Y).ThenBy(c => c.X))}";

    // ---------- 出口箭头 ----------

    private static readonly Vector2[] Arrow =
    {
        new(-16, -6), new(4, -6), new(4, -14), new(18, 0), new(4, 14), new(4, 6), new(-16, 6),
    };

    /// <summary>每个边缘出口一个白模箭头（Polygon2D，指向出口外），放在开口处。</summary>
    public Node2D BuildMarkers(RoomDef room, RoomGrid grid)
    {
        var markers = new Node2D { Name = MarkersName };
        float width = room.Size.X * Cell.X;
        foreach (ExitDef exit in room.Exits.Where(e => e.Kind == ExitKind.Edge))
        {
            var (from, to) = ExitRange(room, grid, exit);
            Vector2 at;
            float angle;
            if (exit.Side is ExitSide.Left or ExitSide.Right)
            {
                var run = MainOpening(grid, exit.Side, from, to);
                int? floor = run == null ? null : FloorRow(grid, exit.Side, run.Value);
                float y = floor.HasValue ? floor.Value * Tile - grid.Shift - 40 : (from + to) / 2f * Tile - grid.Shift;
                at = new Vector2(exit.Side == ExitSide.Left ? 64 : width - 64, y);
                angle = exit.Side == ExitSide.Left ? Mathf.Pi : 0f;
            }
            else
            {
                var runs = OpenRuns(grid, exit.Side, from, to);
                var widest = runs.Count > 0 ? runs.OrderByDescending(r => r.B - r.A).First() : (A: from, B: to);
                float x = (widest.A + widest.B + 1) / 2f * Tile;
                at = exit.Side == ExitSide.Up ? new Vector2(x, 2 * Tile - grid.Shift + 24) : new Vector2(x, (grid.Rows - 2) * Tile - grid.Shift - 24);
                angle = exit.Side == ExitSide.Up ? -Mathf.Pi / 2f : Mathf.Pi / 2f;
            }
            var transform = Transform2D.Identity.Rotated(angle);
            var polygon = new Polygon2D
            {
                Name = "To_" + exit.Target,
                Position = at,
                Color = MarkerColor,
                Polygon = Arrow.Select(p => (transform * p).Round() + Vector2.Zero).ToArray(),
            };
            markers.AddChild(polygon);
        }
        return markers;
    }

    private static string MarkersSignature(Node node)
    {
        if (node == null) return "";
        var sb = new StringBuilder(node.GetType().Name);
        foreach (Node child in node.GetChildren())
            sb.Append(child is Polygon2D p ? $"\n{p.Name}|{p.Position}|{p.Color.ToHtml()}|{string.Join(";", p.Polygon.Select(v => new Vector2(Mathf.Round(v.X) + 0f, Mathf.Round(v.Y) + 0f)))}" : "\n?" + child.Name);
        return sb.ToString();
    }

    // ---------- 网格工具 ----------

    /// <summary>脚底位置所在的格子：x 所在列；y 落在哪一格的底边（含底边上方 1 像素以内）就是哪一行。</summary>
    public static Vector2I CellOf(RoomGrid grid, Vector2 feet) => RoomRules.CellOf(grid, feet);

    /// <summary>就近不动：x 在格子左右边界内（含左不含右），y 在底边或底边上方 1 像素以内。</summary>
    public static bool InPlace(RoomGrid grid, Vector2 pos, Vector2I cell)
    {
        Rect2 rect = grid.TileRect(cell.X, cell.Y);
        return pos.X >= rect.Position.X && pos.X < rect.End.X && pos.Y >= rect.End.Y - 1f && pos.Y <= rect.End.Y;
    }

    /// <summary>永久的墙（边界封闭只认这两种；B / H 会被打碎，R 可能不出现）。</summary>
    public static bool IsPermanent(char ch) => RoomRules.IsPermanent(ch);

    public static bool Standable(char ch) => RoomRules.Standable(ch);

    /// <summary>出口所在那一格沿边界的范围：左右出口是行号（和这一格高度范围相交的行），上下出口是列号。</summary>
    public (int From, int To) ExitRange(RoomDef room, RoomGrid grid, ExitDef exit) => RoomRules.ExitRange(grid, exit, Cell);

    private bool InExitRange(RoomDef room, RoomGrid grid, ExitSide side, int index) => RoomRules.InExitRange(room, grid, side, index, Cell);

    /// <summary>边界上 from..to 之间能通过的连续段（不是 # / X 的格子）。</summary>
    public static List<(int A, int B)> OpenRuns(RoomGrid grid, ExitSide side, int from, int to) => RoomRules.OpenRuns(grid, side, from, to);

    /// <summary>左右出口的主开口：最低的、至少 2 格高的一段。</summary>
    public static (int A, int B)? MainOpening(RoomGrid grid, ExitSide side, int from, int to) => RoomRules.MainOpening(grid, side, from, to);

    /// <summary>左右出口开口处的地面：开口最下一格的下面一格（在边界那一列），能站才算。</summary>
    public static int? FloorRow(RoomGrid grid, ExitSide side, (int A, int B) run) => RoomRules.FloorRow(grid, side, run);

    private static string SideName(ExitSide side) => RoomRules.SideName(side);
}
