using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using SonOfMud.Core;
using SonOfMud.Save;
using SonOfMud.World;

namespace SonOfMud.Tools;

/// <summary>
/// 地图生成器（无界面运行）：读 world_layout.tres →
/// 1) 缺的房间场景生成骨架（已存在的场景不覆盖）；
/// 2) 重写 MetSys 的 MapData.txt（每格四个方向：同一房间 -1、墙 0、通道 1，外加场景 UID）。
/// 运行：godot --headless --path . --scene res://tools/world/BuildWorld.tscn
/// </summary>
public partial class BuildWorld : Node
{
    public const string TileSetPath = "res://assets/data/world/world_tileset.tres";
    public const string RoomInstanceScene = "res://addons/MetroidvaniaSystem/Nodes/RoomInstance.tscn";
    public const int Tile = 64;
    private const int LedgeTiles = 4;

    private Vector2I _cell;
    private TileSet _tileSet;

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private void Run()
    {
        int code = 0;
        try
        {
            var layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
            Vector2 cellSize = MetSysApi.CellSize;
            _cell = new Vector2I((int)cellSize.X, (int)cellSize.Y);
            _tileSet = EnsureTileSet();
            int created = 0;
            foreach (RoomDef room in layout.Rooms)
            {
                if (ResourceLoader.Exists(room.ScenePath)) continue;
                BuildRoomScene(layout, room);
                created++;
            }
            string mapData = BuildMapData(layout, out string error);
            if (mapData == null) throw new System.Exception(error);
            string mapPath = ProjectSettings.GetSetting("addons/metroidvania_system/settings_file").AsString();
            string dataFile = GD.Load<Resource>(mapPath).Get("map_data_file").AsString();
            using (FileAccess file = FileAccess.Open(dataFile, FileAccess.ModeFlags.Write))
                file.StoreString(mapData);
            GD.Print($"BuildWorld：新建房间场景 {created} 个，已写 {dataFile}（{layout.Rooms.Count} 个房间）。");
        }
        catch (System.Exception exception)
        {
            GD.PushError("BuildWorld 失败：" + exception);
            code = 1;
        }
        GetTree().Quit(code);
    }

    // ---------- MapData.txt ----------

    /// <summary>按清单算出 MapData.txt 的完整内容（map_check 也用它和磁盘上的文件比对）。</summary>
    public static string BuildMapData(WorldLayout layout, out string error)
    {
        error = "";
        var sb = new StringBuilder();
        foreach (RoomDef room in layout.Rooms)
        {
            if (!ResourceLoader.Exists(room.ScenePath)) { error = $"房间场景不存在：{room.ScenePath}"; return null; }
            long uid = ResourceLoader.GetResourceUid(room.ScenePath);
            if (uid == ResourceUid.InvalidId) { error = $"房间场景没有 UID：{room.ScenePath}"; return null; }
            string uidText = ResourceUid.IdToText(uid);
            for (int y = room.Origin.Y; y < room.Origin.Y + room.Size.Y; y++)
            for (int x = room.Origin.X; x < room.Origin.X + room.Size.X; x++)
            {
                var cell = new Vector2I(x, y);
                int[] borders = new int[4];
                for (int i = 0; i < 4; i++) borders[i] = Border(room, cell, (ExitSide)i);
                sb.Append($"[{x},{y},{room.Layer}]\n");
                sb.Append($"{borders[0]},{borders[1]},{borders[2]},{borders[3]}|||{uidText}\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>同一房间内部 -1；这一格这一边是 Edge 出口 1；否则墙 0。</summary>
    public static int Border(RoomDef room, Vector2I cell, ExitSide side)
    {
        if (room.ContainsCell(cell + RoomDef.Step(side))) return -1;
        foreach (ExitDef exit in room.Exits)
            if (exit.Kind == ExitKind.Edge && exit.Side == side && room.ExitCell(exit) == cell) return 1;
        return 0;
    }

    // ---------- 占位瓦片集 ----------

    private static TileSet EnsureTileSet()
    {
        if (ResourceLoader.Exists(TileSetPath)) return GD.Load<TileSet>(TileSetPath);
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.36f, 0.31f, 0.26f));
        gradient.SetColor(1, new Color(0.30f, 0.26f, 0.22f));
        var texture = new GradientTexture2D { Gradient = gradient, Width = Tile, Height = Tile, FillTo = new Vector2(0f, 1f) };
        var tileSet = new TileSet { TileSize = new Vector2I(Tile, Tile) };
        tileSet.AddPhysicsLayer();
        tileSet.SetPhysicsLayerCollisionLayer(0, Layers.World);
        tileSet.SetPhysicsLayerCollisionMask(0, 0);
        var source = new TileSetAtlasSource { Texture = texture, TextureRegionSize = new Vector2I(Tile, Tile) };
        tileSet.AddSource(source, 0);
        source.CreateTile(Vector2I.Zero);
        TileData data = source.GetTileData(Vector2I.Zero, 0);
        data.SetCollisionPolygonsCount(0, 1);
        float h = Tile / 2f;
        data.SetCollisionPolygonPoints(0, 0, new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) });
        MakeDir(TileSetPath.GetBaseDir());
        Error result = ResourceSaver.Save(tileSet, TileSetPath);
        if (result != Error.Ok) throw new System.Exception($"保存 {TileSetPath} 失败：{result}");
        EnsureUid(TileSetPath);
        return GD.Load<TileSet>(TileSetPath);
    }

    // ---------- 房间骨架 ----------

    /// <summary>瓦片网格按全局 64 像素对齐：房间顶边全局 Y 不是 64 的倍数时，Geometry 整体上移 shift 像素，相邻房间的地面才能对齐。</summary>
    private int Shift(RoomDef room) => Mathf.PosMod(room.Origin.Y * _cell.Y, Tile);

    /// <summary>覆盖某个局部像素 Y（不含）之上的最后一行瓦片：这一行的顶边就是站立面。</summary>
    private int RowEndingAt(RoomDef room, int localBottom) => (localBottom + Shift(room)) / Tile - 1;

    private float RowTop(RoomDef room, int row) => row * Tile - Shift(room);

    private void BuildRoomScene(WorldLayout layout, RoomDef room)
    {
        int width = room.Size.X * _cell.X, height = room.Size.Y * _cell.Y;
        int cols = width / Tile;
        int floorRow = RowEndingAt(room, height);

        var root = new RoomRoot { Name = room.Id, RoomId = room.Id, DisplayName = room.DisplayName };

        var roomInstance = GD.Load<PackedScene>(RoomInstanceScene).Instantiate<Node2D>();
        roomInstance.Name = "RoomInstance";
        Add(root, root, roomInstance);

        var geometry = new TileMapLayer { Name = "Geometry", TileSet = _tileSet, Position = new Vector2(0, -Shift(room)) };
        Add(root, root, geometry);
        var spawns = new Node2D { Name = "Spawns" };
        Add(root, root, spawns);
        var objects = new Node2D { Name = "Objects" };
        Add(root, root, objects);

        var solid = new HashSet<Vector2I>();
        for (int c = 0; c < cols; c++) solid.Add(new Vector2I(c, floorRow));
        for (int r = 0; r <= floorRow; r++)
        {
            solid.Add(new Vector2I(0, r));
            solid.Add(new Vector2I(cols - 1, r));
        }

        foreach (ExitDef exit in room.Exits.Where(e => e.Kind == ExitKind.Edge))
        {
            if (exit.Side is ExitSide.Left or ExitSide.Right)
            {
                int top = exit.Offset * _cell.Y, bottom = (exit.Offset + 1) * _cell.Y;
                int ledgeRow = Mathf.Min(RowEndingAt(room, bottom), floorRow);
                int wallCol = exit.Side == ExitSide.Left ? 0 : cols - 1;
                for (int r = 0; r < ledgeRow; r++)
                    if (RowTop(room, r) < bottom && RowTop(room, r) + Tile > top) solid.Remove(new Vector2I(wallCol, r));
                for (int i = 0; i < LedgeTiles; i++)
                    solid.Add(new Vector2I(exit.Side == ExitSide.Left ? i : cols - 1 - i, ledgeRow));
            }
            else if (exit.Side == ExitSide.Down)
            {
                int first = exit.Offset * (_cell.X / Tile);
                for (int c = first + 2; c < first + _cell.X / Tile - 2; c++) solid.Remove(new Vector2I(c, floorRow));
            }
        }
        foreach (Vector2I tile in solid) geometry.SetCell(tile, 0, Vector2I.Zero);

        float floorTop = RowTop(room, floorRow);
        bool OnFloor(float x) => solid.Contains(new Vector2I((int)(x / Tile), floorRow));
        float FloorX(params float[] candidates)
        {
            foreach (float x in candidates) if (x > Tile && x < width - Tile && OnFloor(x)) return x;
            return Tile * 2.5f;
        }

        AddSpawn(root, spawns, SpawnPoint.DefaultId, new Vector2(FloorX(width / 2f, width / 2f - 256, width / 2f + 256, Tile * 2.5f), floorTop));

        foreach (ExitDef exit in room.Exits)
        {
            StringName spawnId = ExitDef.ArrivalSpawn(exit.Target);
            Vector2 at;
            if (exit.Kind == ExitKind.Door)
            {
                float doorX = FloorX(exit.Offset * _cell.X + _cell.X / 2f, width / 2f, Tile * 2.5f);
                AddDoor(root, objects, exit, room, new Vector2(doorX, floorTop));
                at = new Vector2(FloorX(doorX + 128, doorX - 128, doorX), floorTop);
            }
            else if (exit.Side is ExitSide.Left or ExitSide.Right)
            {
                int ledgeRow = Mathf.Min(RowEndingAt(room, (exit.Offset + 1) * _cell.Y), floorRow);
                at = new Vector2(exit.Side == ExitSide.Left ? 96 : width - 96, RowTop(room, ledgeRow));
                if (exit.HasBarrier) AddBarrier(root, objects, exit, new Vector2(exit.Side == ExitSide.Left ? Tile / 2f : width - Tile / 2f, RowTop(room, ledgeRow)), exit.Offset * _cell.Y);
            }
            else
            {
                float x = exit.Offset * _cell.X + _cell.X / 2f;
                at = new Vector2(exit.Side == ExitSide.Up ? FloorX(x, x - 192, x + 192) : FloorX(x - 224, x + 224, x), floorTop);
                if (exit.HasBarrier) GD.PushWarning($"{room.Id}：上下方向的条件门还没有实体门，只有逻辑条件。");
            }
            AddSpawn(root, spawns, spawnId, at);
        }

        if (room.HasSavePoint)
        {
            Vector2 at = new(FloorX(width / 2f + 192, width / 2f - 192, width / 2f), floorTop);
            var savePoint = new SavePoint { Name = "SavePoint", SavePointId = room.Id, Position = at };
            Add(root, objects, savePoint);
            AddShape(root, savePoint, new Vector2(96, 128));
            AddVisual(root, savePoint, new Vector2(48, 96), new Color(0.85f, 0.7f, 0.25f));
            AddSpawn(root, spawns, SpawnPoint.SaveId, at);
        }

        var packed = new PackedScene();
        Error packResult = packed.Pack(root);
        if (packResult != Error.Ok) throw new System.Exception($"打包 {room.Id} 失败：{packResult}");
        MakeDir(room.ScenePath.GetBaseDir());
        Error saveResult = ResourceSaver.Save(packed, room.ScenePath);
        if (saveResult != Error.Ok) throw new System.Exception($"保存 {room.ScenePath} 失败：{saveResult}");
        EnsureUid(room.ScenePath);
        root.Free();
        GD.Print($"BuildWorld：已生成 {room.ScenePath}");
    }

    private static void AddDoor(Node root, Node parent, ExitDef exit, RoomDef room, Vector2 at)
    {
        var door = new RoomDoor
        {
            Name = "DoorTo" + exit.Target,
            TargetRoom = exit.Target,
            TargetSpawn = ExitDef.ArrivalSpawn(room.Id),
            Position = at,
        };
        Add(root, parent, door);
        AddShape(root, door, new Vector2(112, 176));
        AddVisual(root, door, new Vector2(96, 160), new Color(0.25f, 0.4f, 0.8f, 0.8f));
    }

    private void AddBarrier(Node root, Node parent, ExitDef exit, Vector2 standOn, int cellTop)
    {
        float openingHeight = standOn.Y - (cellTop - Tile);
        var barrier = new GateBarrier
        {
            Name = "GateTo" + exit.Target,
            Requirement = exit.Requirement,
            Position = standOn,
        };
        Add(root, parent, barrier);
        AddShape(root, barrier, new Vector2(Tile, openingHeight));
        AddVisual(root, barrier, new Vector2(Tile, openingHeight), new Color(0.75f, 0.2f, 0.2f, 0.85f));
    }

    /// <summary>矩形碰撞形状，底边落在父节点原点（脚底）上。</summary>
    private static void AddShape(Node root, Node parent, Vector2 size)
    {
        var shape = new CollisionShape2D { Name = "Shape", Shape = new RectangleShape2D { Size = size }, Position = new Vector2(0, -size.Y / 2f) };
        Add(root, parent, shape);
    }

    private static void AddVisual(Node root, Node parent, Vector2 size, Color color)
    {
        var rect = new ColorRect { Name = "Placeholder", Color = color, Size = size, Position = new Vector2(-size.X / 2f, -size.Y), MouseFilter = Control.MouseFilterEnum.Ignore };
        Add(root, parent, rect);
    }

    private static void AddSpawn(Node root, Node parent, StringName id, Vector2 at)
    {
        var spawn = new SpawnPoint { Name = id.ToString(), SpawnId = id, Position = at - new Vector2(0, 1) };
        Add(root, parent, spawn);
    }

    private static void Add(Node root, Node parent, Node child)
    {
        parent.AddChild(child);
        child.Owner = root;
    }

    /// <summary>无界面运行时 ResourceSaver 不会自动分配 UID，而 MetSys 的 MapData.txt 用 UID 指向场景，所以手动写入。</summary>
    private static void EnsureUid(string path)
    {
        if (ResourceLoader.GetResourceUid(path) != ResourceUid.InvalidId) return;
        long uid = ResourceUid.CreateId();
        Error result = ResourceSaver.SetUid(path, uid);
        if (result != Error.Ok) throw new System.Exception($"给 {path} 写 UID 失败：{result}");
        if (ResourceUid.HasId(uid)) ResourceUid.SetId(uid, path);
        else ResourceUid.AddId(uid, path);
    }

    private static void MakeDir(string resDir) => DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(resDir));
}
