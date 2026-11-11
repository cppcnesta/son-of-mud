using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Save;
using SonOfMud.World;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// 存档往返：在 S1_04 存档 → 生成 save_0.sav → 重置 → LoadFromDisk → 人在 S1_04 的存档点，已探索的格子和进度变量都在。
/// </summary>
public partial class SaveRoundtrip : WorldTestBase
{
    protected override string SuiteName => "save_roundtrip";

    protected override async Task RunChecks()
    {
        await SetUpGame();
        Saves.SaveDirectory = "user://world_tests";
        string path = Saves.SlotPath(0);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Saves.SaveDirectory));
        if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));

        World.LoadRoomImmediate("S1_04", "Default");
        await Frames(10);
        Check(Saves.CurrentRoom == "S1_04", "SaveService 从 RoomEntered 得知当前房间 S1_04");
        Vector3I visited = MetSysApi.CurrentCoords;
        Check(MetSysApi.IsCellDiscovered(visited, false), $"站过的格子 {visited} 已探索");
        var unvisited = new Vector3I(9, 21, 0); // S4_05 的一格
        Check(!MetSysApi.IsCellDiscovered(unvisited, false), "没去过的 S4_05 未探索");

        // 可储存对象（以后的可破坏墙）：标记储存后存档
        var wall = new Node2D { Name = "TestWall" };
        var persistent = new PersistentObject { Name = "Persistent", ObjectId = "test_wall_s1_04" };
        wall.AddChild(persistent);
        World.CurrentRoom.GetNode("Objects").AddChild(wall);
        await Frames(1);
        Check(!persistent.WasStored, "PersistentObject：第一次登记时没有被储存");
        persistent.MarkStored();

        GameSession.Progress.Unlock("heavy_slam");
        GameSession.Progress.SetVar("boss_keeper", true);
        GameSession.Progress.SetVar("tablets", 3);
        GameSession.Progress.SetVar("speaker", "伊吉");

        int saved = 0;
        void OnSaved() => saved++;
        Events.Instance.GameSaved += OnSaved;
        SavePoint savePoint = FindChild<SavePoint>(World.CurrentRoom);
        Check(savePoint != null && savePoint.SavePointId == "S1_04" && savePoint.CanInteract(Mover), "S1_04 有存档点 S1_04");
        savePoint?.Interact(Mover);
        Events.Instance.GameSaved -= OnSaved;
        Check(saved == 1 && FileAccess.FileExists(path), $"按存档点 → SaveRequested → 写出 {path}，发 GameSaved");
        Check(!FileAccess.FileExists(path + ".tmp"), "写盘用临时文件改名，没有留下 .tmp");
        Check(Saves.CheckpointRoom == "S1_04" && Saves.CheckpointSavePoint == "S1_04", "最近存档点更新为 S1_04");

        Variant parsed = GD.StrToVar(FileAccess.GetFileAsString(path));
        GDictionary file = parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : new GDictionary();
        bool shape = file.ContainsKey("version") && file["version"].AsInt32() == 2 && file.ContainsKey("metsys") && file.ContainsKey("progress")
                     && file.ContainsKey("world") && file.ContainsKey("meta");
        Check(shape, "存档格式 v2：{version=2, metsys, progress, world, meta}（VarToStr 文本）");
        GDictionary world = shape ? file["world"].AsGodotDictionary() : new GDictionary();
        Check(shape && world["room"].AsString() == "S1_04" && world["savePoint"].AsString() == "S1_04", "world 里记着房间 S1_04 和存档点 S1_04");

        // 再存一次：覆盖已有存档也要成功
        Check(Saves.SaveToDisk("S1_04"), "已有存档时再次存档（改名覆盖）成功");

        // ---- 重置：模拟回主菜单 ----
        GameSession.Reset();
        MetSysApi.ResetState();
        MetSysApi.SetSaveData();
        World.LoadRoomImmediate("S1_01", "Default");
        await Frames(5);
        Check(!GameSession.Progress.HasAbility("heavy_slam") && !GameSession.Progress.HasVar("boss_keeper"), "重置后内存里的进度已清空");
        Check(!MetSysApi.IsCellDiscovered(visited, false), "重置后 MetSys 的探索记录已清空");

        string loadedRoom = null;
        string roomWhenLoaded = null;
        void OnLoaded(string room) { loadedRoom = room; roomWhenLoaded = World.CurrentRoomDef?.Id; }
        Events.Instance.GameLoaded += OnLoaded;
        bool ok = Saves.LoadFromDisk(0);
        Check(ok && loadedRoom == null, "LoadFromDisk(0) 发出请求后不立刻发 GameLoaded（要等 World 回执）");
        bool arrived = await WaitUntil(() => !World.IsTransitioning && World.CurrentRoomDef?.Id == "S1_04", 240);
        Events.Instance.GameLoaded -= OnLoaded;
        Check(loadedRoom == "S1_04" && roomWhenLoaded == "S1_04", $"GameLoaded(S1_04) 在已经进入 S1_04 之后才发（发出时所在房间 {roomWhenLoaded}）");
        Check(arrived && World.FadeTimeouts == 0, "读档后经淡入淡出进入 S1_04，没有超时");

        SpawnPoint saveSpawn = World.CurrentRoom.FindSpawn(SpawnPoint.SaveId);
        SavePoint loadedSavePoint = FindChild<SavePoint>(World.CurrentRoom);
        Check(saveSpawn != null && loadedSavePoint != null && saveSpawn.GlobalPosition.DistanceTo(loadedSavePoint.GlobalPosition) < 2f
              && Mover.GlobalPosition.DistanceTo(saveSpawn.GlobalPosition) < 4f, $"人在 S1_04 的存档点（{Mover.GlobalPosition}，存档点 {loadedSavePoint?.GlobalPosition}）");
        Check(MetSysApi.IsCellDiscovered(visited, false) && !MetSysApi.IsCellDiscovered(unvisited, false), "已探索的格子恢复了，没去过的仍未探索");
        Check(GameSession.Progress.HasAbility("heavy_slam"), "能力 heavy_slam 恢复（不在初始能力里，只能来自存档）");
        Variant flag = GameSession.Progress.GetVar("boss_keeper"), count = GameSession.Progress.GetVar("tablets"), speaker = GameSession.Progress.GetVar("speaker");
        Check(flag.VariantType == Variant.Type.Bool && flag.AsBool(), "变量 boss_keeper 恢复为 bool true");
        Check(count.VariantType == Variant.Type.Int && count.AsInt64() == 3, "变量 tablets 恢复为 int 3");
        Check(speaker.VariantType == Variant.Type.String && speaker.AsString() == "伊吉", "变量 speaker 恢复为原字符串");
        Check(MetSysApi.IsStored("test_wall_s1_04"), "MetSys 储存的对象也恢复了");
        Check(Saves.CheckpointRoom == "S1_04" && Saves.CheckpointSavePoint == "S1_04", "读档后最近存档点是 S1_04");

        // ---- 坏档 ----
        using (FileAccess bad = FileAccess.Open(Saves.SlotPath(1), FileAccess.ModeFlags.Write))
            bad.StoreString(GD.VarToStr(new GDictionary { ["version"] = 99, ["world"] = new GDictionary { ["room"] = "S1_01" } }));
        Check(!Saves.LoadFromDisk(1) && GameSession.Progress.HasAbility("heavy_slam"), "版本不对的存档被拒绝，内存里的进度不受影响（会打印一条预期的错误）");
        Check(!Saves.LoadFromDisk(7), "不存在的存档槽返回 false（会打印一条预期的错误）");

        await TearDownGame();
    }
}
