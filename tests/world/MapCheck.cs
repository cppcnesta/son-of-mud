using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Save;
using SonOfMud.World;

/// <summary>
/// 地图检查（计划第 4 节 map_check）：
/// ① 房间编号、场景、RoomInstance；② 通道两边对得上；③ MapData.txt 与清单一致；④ 逻辑可达性（只证明逻辑，不证明物理）。
/// </summary>
public partial class MapCheck : WorldTestBase
{
    protected override string SuiteName => "map_check";

    private WorldLayout _layout;
    private readonly Dictionary<string, RoomRoot> _scenes = new();

    protected override Task RunChecks()
    {
        _layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        Check(_layout != null, "清单 world_layout.tres 能加载");
        if (_layout == null) return Task.CompletedTask;
        RoomsAndScenes();
        Passages();
        MapDataMatches();
        Reachability();
        foreach (RoomRoot root in _scenes.Values) root.Free();
        return Task.CompletedTask;
    }

    // ---------- ① ----------

    private void RoomsAndScenes()
    {
        List<RoomDef> rooms = _layout.Rooms.ToList();
        Check(rooms.Count == 27, $"①清单共 27 个房间（实际 {rooms.Count}）");
        Check(rooms.Select(r => r.Id).Distinct().Count() == rooms.Count, "①房间编号唯一");
        Check(rooms.All(r => Regex.IsMatch(r.Id, @"^S[1-4]_0[1-9]$")), "①编号格式都是 S<场景>_<两位序号>");
        int[] perScene = Enumerable.Range(1, 4).Select(s => rooms.Count(r => r.Id.StartsWith($"S{s}_"))).ToArray();
        Check(perScene.SequenceEqual(new[] { 8, 6, 8, 5 }), $"①四个场景的房间数 8/6/8/5（实际 {string.Join("/", perScene)}）");
        Check(rooms.All(r => r.Layer == 0), "①全部房间在同一个图层 0");
        Check(rooms.All(r => r.Size.X >= 2 && r.Size.Y >= 2), "①每个房间至少 2×2 格（一屏），镜头不会比房间大");

        var owner = new Dictionary<Vector2I, string>();
        var overlaps = new List<string>();
        foreach (RoomDef room in rooms)
            for (int x = 0; x < room.Size.X; x++)
            for (int y = 0; y < room.Size.Y; y++)
            {
                var cell = room.Origin + new Vector2I(x, y);
                if (owner.TryGetValue(cell, out string other)) overlaps.Add($"{room.Id}/{other}@{cell}");
                else owner[cell] = room.Id;
            }
        Check(overlaps.Count == 0, "①没有重叠的格子" + (overlaps.Count > 0 ? "：" + string.Join(", ", overlaps.Take(5)) : ""));

        var problems = new List<string>();
        Vector2 cellSize = MetSysApi.CellSize;
        foreach (RoomDef room in rooms)
        {
            if (!ResourceLoader.Exists(room.ScenePath)) { problems.Add(room.Id + " 场景不存在"); continue; }
            Node node = GD.Load<PackedScene>(room.ScenePath).Instantiate();
            if (node is not RoomRoot root) { problems.Add(room.Id + " 根节点不是 RoomRoot"); node.Free(); continue; }
            _scenes[room.Id] = root;
            if (root.RoomId != room.Id) problems.Add($"{room.Id} 的 RoomId 写成了 {root.RoomId}");
            var instance = root.GetNodeOrNull<Node2D>("RoomInstance");
            if (instance == null || instance.Position != Vector2.Zero) problems.Add(room.Id + " 缺 RoomInstance 或不在 (0,0)");
            if (root.GetNodeOrNull<TileMapLayer>("Geometry") == null || root.GetNodeOrNull("Spawns") == null || root.GetNodeOrNull("Objects") == null)
                problems.Add(room.Id + " 骨架缺 Geometry / Spawns / Objects");
            if (Search<SpawnPoint>(root).All(s => s.SpawnId != SpawnPoint.DefaultId)) problems.Add(room.Id + " 没有 Default 出生点");
        }
        Check(problems.Count == 0, "①27 个场景都存在，根是 RoomRoot、编号一致，RoomInstance 在 (0,0)，骨架齐全" + (problems.Count > 0 ? "：" + string.Join("; ", problems.Take(6)) : ""));

        // 放进场景树，让 MetSys 按 MapData.txt 认出房间：尺寸必须等于清单。
        var sizeProblems = new List<string>();
        foreach (RoomDef room in rooms)
        {
            if (!_scenes.ContainsKey(room.Id)) continue;
            Node live = GD.Load<PackedScene>(room.ScenePath).Instantiate();
            AddChild(live);
            var metSysRoom = new MetSysRoom(live.GetNode("RoomInstance"));
            Vector2 expected = new(room.Size.X * cellSize.X, room.Size.Y * cellSize.Y);
            string uid = ResourceUid.IdToText(ResourceLoader.GetResourceUid(room.ScenePath));
            if (metSysRoom.Size != expected) sizeProblems.Add($"{room.Id} 尺寸 {metSysRoom.Size}≠{expected}");
            if (metSysRoom.Id != uid) sizeProblems.Add($"{room.Id} 的 MetSys 房间 ID {metSysRoom.Id}≠{uid}");
            if (_layout.FindByMetSysId(metSysRoom.Id) != room) sizeProblems.Add($"{room.Id} 不能从 MetSys 房间 ID 反查");
            RemoveChild(live);
            live.Free();
        }
        Check(sizeProblems.Count == 0, "①MetSys 运行时认出全部 27 个房间，尺寸与清单一致，房间 ID 能反查清单" + (sizeProblems.Count > 0 ? "：" + string.Join("; ", sizeProblems.Take(6)) : ""));
    }

    // ---------- ② ----------

    private void Passages()
    {
        var problems = new List<string>();
        int edgeCount = 0, doorCount = 0;
        foreach (RoomDef room in _layout.Rooms)
        foreach (ExitDef exit in room.Exits)
        {
            RoomDef target = _layout.Find(exit.Target);
            if (target == null) { problems.Add($"{room.Id}→{exit.Target} 目标不存在"); continue; }
            if (exit.Kind == ExitKind.Edge)
            {
                edgeCount++;
                Vector2I cell = room.ExitCell(exit), next = cell + RoomDef.Step(exit.Side);
                if (!room.ContainsCell(cell)) problems.Add($"{room.Id}→{target.Id} 出口格 {cell} 不在房间里");
                if (!target.ContainsCell(next)) problems.Add($"{room.Id}→{target.Id} 出口外面 {next} 不是目标房间");
                int back = target.Exits.Count(e => e.Kind == ExitKind.Edge && e.Target == room.Id && e.Side == RoomDef.Opposite(exit.Side) && target.ExitCell(e) == next);
                if (back != 1) problems.Add($"{room.Id}→{target.Id} 没有对应的回程出口（{back}）");
            }
            else
            {
                doorCount++;
                if (target.Exits.Count(e => e.Kind == ExitKind.Door && e.Target == room.Id) != 1) problems.Add($"{room.Id}→{target.Id} 的门没有回程");
            }
            if (_scenes.TryGetValue(target.Id, out RoomRoot targetScene) && Search<SpawnPoint>(targetScene).All(s => s.SpawnId != ExitDef.ArrivalSpawn(room.Id)))
                problems.Add($"{target.Id} 缺出生点 From_{room.Id}");
        }
        Check(problems.Count == 0, $"②每条通道两边都对得上，没有单向通道，到达出生点齐全（边出口 {edgeCount} 个，门 {doorCount} 个）" + (problems.Count > 0 ? "：" + string.Join("; ", problems.Take(6)) : ""));
        Check(edgeCount == 2 * 25 && doorCount == 2, $"②通道数：25 条边通道 + 1 扇门（双向计数 {edgeCount}/{doorCount}）");

        Check(HasEdge("S1_07", "S2_01") && HasEdge("S2_06", "S3_01"), "②跨场景出口 S1_07→S2_01、S2_06→S3_01 在格子上直接相邻");
        Check(HasDoor("S3_08", "S4_01") && !Adjacent(_layout.Find("S3_08"), _layout.Find("S4_01")), "②S3_08→S4_01 是按键过门，地图上不相邻");
        Check(Adjacent(_layout.Find("S2_02"), _layout.Find("S2_04")) && !HasEdge("S2_02", "S2_04"), "②S2_02|S2_04 相邻但是墙");
        Check(Adjacent(_layout.Find("S4_03"), _layout.Find("S4_05")) && !HasEdge("S4_03", "S4_05"), "②S4_03|S4_05 相邻但是墙");
        Check(Gate("S1_04", "S1_08", ability: "wall_climb") && Gate("S2_04", "S2_05", ability: "wall_climb"), "②能力门：S1_04↑S1_08、S2_04↑S2_05 需要 wall_climb");
        Check(Gate("S1_05", "S1_06", flag: "boss_keeper") && Gate("S2_03", "S2_04", flag: "flood_done") && Gate("S2_04", "S2_06", flag: "boss_dragon")
              && Gate("S3_06", "S3_07", flag: "districts_done") && Gate("S4_02", "S4_03", flag: "seven_gates"), "②事件门：boss_keeper / flood_done / boss_dragon / districts_done / seven_gates 位置正确");

        var objectProblems = new List<string>();
        foreach (RoomDef room in _layout.Rooms)
        {
            if (!_scenes.TryGetValue(room.Id, out RoomRoot scene)) continue;
            List<GateBarrier> barriers = Search<GateBarrier>(scene);
            List<ExitDef> barrierExits = room.Exits.Where(e => e.HasBarrier).ToList();
            if (barriers.Count != barrierExits.Count) objectProblems.Add($"{room.Id} 实体门 {barriers.Count} 个，清单要求 {barrierExits.Count} 个");
            foreach (ExitDef exit in barrierExits)
                if (!barriers.Any(b => b.Requirement != null && b.Requirement.Flags.OrderBy(f => f.ToString()).SequenceEqual(exit.Requirement.Flags.OrderBy(f => f.ToString())) && b.Requirement.Abilities.Count == 0))
                    objectProblems.Add($"{room.Id}→{exit.Target} 的实体门条件和清单不一致");
            List<RoomDoor> doors = Search<RoomDoor>(scene);
            List<ExitDef> doorExits = room.Exits.Where(e => e.Kind == ExitKind.Door).ToList();
            if (doors.Count != doorExits.Count || doorExits.Any(e => !doors.Any(d => d.TargetRoom == e.Target && d.TargetSpawn == ExitDef.ArrivalSpawn(room.Id))))
                objectProblems.Add($"{room.Id} 的门和清单不一致");
            List<SavePoint> saves = Search<SavePoint>(scene);
            bool hasSaveSpawn = Search<SpawnPoint>(scene).Any(s => s.SpawnId == SpawnPoint.SaveId);
            if (room.HasSavePoint != (saves.Count == 1) || room.HasSavePoint != hasSaveSpawn) objectProblems.Add($"{room.Id} 的存档点和清单不一致");
            if (saves.Count == 1 && saves[0].SavePointId.ToString() != room.Id) objectProblems.Add($"{room.Id} 的存档点编号是 {saves[0].SavePointId}");
        }
        Check(objectProblems.Count == 0, "②场景里的实体门、过门、存档点和清单一致（能力门没有实体门，靠地形）" + (objectProblems.Count > 0 ? "：" + string.Join("; ", objectProblems.Take(6)) : ""));
        string[] saveRooms = _layout.Rooms.Where(r => r.HasSavePoint).Select(r => r.Id).OrderBy(x => x).ToArray();
        Check(saveRooms.SequenceEqual(new[] { "S1_04", "S2_02", "S3_03", "S4_04" }), $"②存档点在 S1_04 / S2_02 / S3_03 / S4_04（实际 {string.Join(",", saveRooms)}）");
    }

    private bool HasEdge(string a, string b) =>
        _layout.Find(a).Exits.Any(e => e.Kind == ExitKind.Edge && e.Target == b);

    private bool HasDoor(string a, string b) =>
        _layout.Find(a).Exits.Any(e => e.Kind == ExitKind.Door && e.Target == b);

    private bool Gate(string a, string b, string ability = null, string flag = null)
    {
        ExitDef exit = _layout.Find(a).Exits.FirstOrDefault(e => e.Target == b);
        GateRequirement req = exit?.Requirement;
        if (req == null) return false;
        return ability != null
            ? req.Abilities.Count == 1 && req.Abilities[0] == ability && req.Flags.Count == 0 && !exit.HasBarrier
            : req.Flags.Count == 1 && req.Flags[0] == flag && req.Abilities.Count == 0 && exit.HasBarrier;
    }

    /// <summary>两个房间有没有共用的边（至少一格相接）。</summary>
    private static bool Adjacent(RoomDef a, RoomDef b)
    {
        for (int x = 0; x < a.Size.X; x++)
        for (int y = 0; y < a.Size.Y; y++)
        {
            Vector2I cell = a.Origin + new Vector2I(x, y);
            for (int i = 0; i < 4; i++)
                if (b.ContainsCell(cell + RoomDef.Step((ExitSide)i))) return true;
        }
        return false;
    }

    // ---------- ③ ----------

    private void MapDataMatches()
    {
        string path = GD.Load<Resource>(ProjectSettings.GetSetting("addons/metroidvania_system/settings_file").AsString()).Get("map_data_file").AsString();
        Check(path == "res://assets/data/metsys/MapData.txt", $"③MetSys 设置指向 assets/data/metsys/MapData.txt（实际 {path}）");
        string[] lines = FileAccess.GetFileAsString(path).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var actual = new Dictionary<Vector3I, string>();
        var format = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            Match head = Regex.Match(lines[i], @"^\[(-?\d+),(-?\d+),(-?\d+)\]$");
            if (!head.Success || i + 1 >= lines.Length) { format.Add(lines[i]); continue; }
            var cell = new Vector3I(int.Parse(head.Groups[1].Value), int.Parse(head.Groups[2].Value), int.Parse(head.Groups[3].Value));
            if (actual.ContainsKey(cell)) format.Add("重复格 " + cell);
            actual[cell] = lines[++i];
        }
        Check(format.Count == 0, "③MapData.txt 只有格子数据，格式正确" + (format.Count > 0 ? "：" + string.Join(" | ", format.Take(3)) : ""));

        var expected = new Dictionary<Vector3I, string>();
        foreach (RoomDef room in _layout.Rooms)
        {
            string uid = ResourceUid.IdToText(ResourceLoader.GetResourceUid(room.ScenePath));
            for (int x = room.Origin.X; x < room.Origin.X + room.Size.X; x++)
            for (int y = room.Origin.Y; y < room.Origin.Y + room.Size.Y; y++)
            {
                var cell = new Vector2I(x, y);
                int[] b = new int[4];
                for (int d = 0; d < 4; d++)
                {
                    var side = (ExitSide)d;
                    if (room.ContainsCell(cell + RoomDef.Step(side))) b[d] = -1;
                    else b[d] = room.Exits.Any(e => e.Kind == ExitKind.Edge && e.Side == side && room.ExitCell(e) == cell) ? 1 : 0;
                }
                expected[new Vector3I(x, y, room.Layer)] = $"{b[0]},{b[1]},{b[2]},{b[3]}|||{uid}";
            }
        }
        Check(actual.Count == expected.Count, $"③格子数一致（文件 {actual.Count}，清单 {expected.Count}）");
        var diff = expected.Where(kv => !actual.TryGetValue(kv.Key, out string v) || v != kv.Value).Select(kv => kv.Key.ToString()).ToList();
        diff.AddRange(actual.Keys.Where(k => !expected.ContainsKey(k)).Select(k => "多余 " + k));
        Check(diff.Count == 0, "③每格的四个方向边界（-1 房内 / 0 墙 / 1 通道）和场景 UID 都与清单一致" + (diff.Count > 0 ? "：" + string.Join(", ", diff.Take(6)) : ""));

        int passages = actual.Values.Sum(v => v.Split('|')[0].Split(',').Count(s => s == "1"));
        Check(passages == 50, $"③通道边界共 50 个（25 条边通道 × 两侧，实际 {passages}）");
    }

    // ---------- ④ ----------

    private static readonly (string Room, string Token)[] Acquisitions =
    {
        ("S1_05", "boss_keeper"), ("S2_02", "wall_climb"), ("S2_03", "flood_done"), ("S2_04", "boss_dragon"),
        ("S3_03", "heavy_slam"), ("S4_02", "seven_gates"),
    };
    private static readonly string[] Districts = { "S3_04", "S3_05", "S3_06" };
    private const string DistrictsDone = "districts_done";

    /// <summary>从 S1_01 出发反复扩展到不动点：通道条件（两侧出口条件的并集）满足才能走；到过获取点就拿到对应标记。forbidden 永远拿不到。</summary>
    private (HashSet<string> Rooms, HashSet<string> Tokens) Reach(string forbidden)
    {
        var rooms = new HashSet<string> { "S1_01" };
        var tokens = new HashSet<string>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach ((string room, string token) in Acquisitions)
                if (rooms.Contains(room) && token != forbidden && tokens.Add(token)) changed = true;
            if (Districts.All(rooms.Contains) && DistrictsDone != forbidden && tokens.Add(DistrictsDone)) changed = true;
            foreach (string id in rooms.ToList())
            {
                RoomDef room = _layout.Find(id);
                foreach (ExitDef exit in room.Exits)
                {
                    if (rooms.Contains(exit.Target)) continue;
                    ExitDef back = _layout.Find(exit.Target).Exits.FirstOrDefault(e => e.Target == id);
                    if (Needs(exit).Concat(back == null ? Enumerable.Empty<string>() : Needs(back)).All(tokens.Contains))
                    {
                        rooms.Add(exit.Target);
                        changed = true;
                    }
                }
            }
        }
        return (rooms, tokens);
    }

    private static IEnumerable<string> Needs(ExitDef exit) =>
        exit.Requirement == null ? Enumerable.Empty<string>()
            : exit.Requirement.Abilities.Select(a => a.ToString()).Concat(exit.Requirement.Flags.Select(f => f.ToString()));

    private void Reachability()
    {
        (HashSet<string> rooms, HashSet<string> tokens) = Reach(null);
        Check(rooms.Count == 27, $"④从 S1_01 出发 27 个房间全部可达（实际 {rooms.Count}，缺 {string.Join(",", _layout.Rooms.Select(r => r.Id).Where(id => !rooms.Contains(id)))}）");
        Check(rooms.Contains("S4_05"), "④能走到终末之室 S4_05");

        var required = _layout.Rooms.SelectMany(r => r.Exits).SelectMany(Needs).Distinct().ToList();
        var missing = required.Where(t => !tokens.Contains(t)).ToList();
        Check(missing.Count == 0, $"④没有死锁：通道用到的条件（{string.Join(",", required)}）都能拿到" + (missing.Count > 0 ? "，拿不到：" + string.Join(",", missing) : ""));
        var unused = Acquisitions.Select(a => a.Token).Append(DistrictsDone).Where(t => !tokens.Contains(t)).ToList();
        Check(unused.Count == 0, "④每个能力 / 事件标记都在它的获取点拿到" + (unused.Count > 0 ? "，没拿到：" + string.Join(",", unused) : ""));

        var selfLocked = new List<string>();
        foreach ((string room, string token) in Acquisitions)
            if (!Reach(token).Rooms.Contains(room)) selfLocked.Add($"{room}/{token}");
        HashSet<string> withoutDistricts = Reach(DistrictsDone).Rooms;
        foreach (string room in Districts)
            if (!withoutDistricts.Contains(room)) selfLocked.Add($"{room}/{DistrictsDone}");
        Check(selfLocked.Count == 0, "④每个获取点不需要它自己那项能力或事件就能进入" + (selfLocked.Count > 0 ? "：" + string.Join(",", selfLocked) : ""));

        HashSet<string> noKeeper = Reach("boss_keeper").Rooms;
        Check(noKeeper.SetEquals(new[] { "S1_01", "S1_02", "S1_03", "S1_04", "S1_05" }), $"④自检：拿不到 boss_keeper 时只能到 S1_01–S1_05（实际 {string.Join(",", noKeeper.OrderBy(x => x))}），说明条件确实会挡路");

        HashSet<string> noClimb = Reach("wall_climb").Rooms;
        Check(!noClimb.Contains("S1_08") && !noClimb.Contains("S2_05"), "④拿到 wall_climb 之前进不去 S1_08 和 S2_05（逻辑上）");
        Check(noClimb.Count == 25, $"④没有 wall_climb 时只少这两个房间（可达 {noClimb.Count}）");
    }

    private static List<T> Search<T>(Node node) where T : class
    {
        var result = new List<T>();
        foreach (Node child in node.GetChildren())
        {
            if (child is T found) result.Add(found);
            result.AddRange(Search<T>(child));
        }
        return result;
    }
}
