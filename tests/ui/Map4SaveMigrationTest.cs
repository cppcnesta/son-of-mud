using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Save;
using SonOfMud.UI;
using SonOfMud.World;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// 地图四按美术比例重做后的旧存档迁移，从正式入口走：
/// 存档槽 0 放一份旧版本游戏里真实存出来的存档（tests/fixtures/saves/old_map4_altar.sav：在旧心室祭坛 S4_04 的存档点存的，
/// 有 boss_keeper、七块泥板、count.泥肝 = 2、fixture.old_map4 = 42）→ 新主菜单按“继续” →
/// ① 人到新心室祭坛 S4_10 的存档点，检查点是 S4_10 / S4_10；进度、能力原样都在；读档不改存档文件；
/// ② 地图：S4_10 站着的格子已探索，地图四其他新房间一格都没被旧数据点亮；
/// ③ 死亡后回到 S4_10 的存档点；
/// ④ 用真实按键在存档点再存一次：存档文件里写的是 S4_10；回主菜单再“继续”，照样到 S4_10；
/// ⑤ 迁移表：旧房间号不在现在的房间表里，旧存档点编号不是现在的存档点；查表本身（换 / 不换）。
/// </summary>
public partial class Map4SaveMigrationTest : UiTestBase
{
    protected override string SuiteName => "map4_save_migration";

    private const string Fixture = "res://tests/fixtures/saves/old_map4_altar.sav";
    private static readonly string[] OtherMap4 = { "S4_01", "S4_02", "S4_0304", "S4_05", "S4_06", "S4_07", "S4_08", "S4_09", "S4_11" };

    private string RoomId => World?.CurrentRoomDef?.Id ?? "";

    protected override async Task RunChecks()
    {
        TableChecks();

        byte[] old = FileAccess.FileExists(Fixture) ? FileAccess.GetFileAsBytes(Fixture) : null;
        GDictionary oldData = old == null ? null : GD.StrToVar(System.Text.Encoding.UTF8.GetString(old)).AsGodotDictionary();
        GDictionary oldWorld = oldData?["world"].AsGodotDictionary();
        Check(oldWorld != null && oldWorld["room"].AsString() == "S4_04" && oldWorld["savePoint"].AsString() == "S4_04",
            "搭建：旧存档样本是旧版本在旧心室祭坛 S4_04 的存档点存的（检查点 S4_04 / S4_04）");
        if (oldWorld == null) return;
        string oldAbilities = string.Join(",", oldData["progress"].AsGodotDictionary()["abilities"].AsGodotArray().Select(a => a.AsString()).OrderBy(a => a));
        DeleteSave();
        WriteSaveBytes(old);

        // ① 按“继续”
        TitleMenu menu = await OpenTitleMenu();
        Check(menu != null && !menu.ContinueButton.Disabled, "新主菜单：有存档，“继续”可用");
        if (menu == null) return;
        bool started = await StartFromMenu(menu.ContinueButton);
        Check(started && Starts[^1].success && Boot.StartedWith == StartMode.Continue, $"按“继续”：旧存档读档开局成功（{Starts[^1].error}）");
        if (World == null || Player == null) return;
        await WaitSettled();
        Vector2 saveSpawn = World.CurrentRoom?.GetNodeOrNull<Node2D>("Spawns/Save")?.GlobalPosition ?? Vector2.Inf;
        Check(RoomId == "S4_10" && Saves.CheckpointRoom == "S4_10" && Saves.CheckpointSavePoint == "S4_10"
              && (Player.GlobalPosition + new Vector2(0f, PlayerBody.HalfH)).DistanceTo(saveSpawn) < 40f,
            $"旧存档到了新心室祭坛 S4_10 的存档点，检查点 S4_10 / S4_10（在 {RoomId}，检查点 {Saves.CheckpointRoom} / {Saves.CheckpointSavePoint}）");
        Variant marker = GameSession.Progress.GetVar("fixture.old_map4");
        Variant liver = GameSession.Progress.GetVar("count.泥肝");
        Check(marker.VariantType == Variant.Type.Int && marker.AsInt64() == 42 && liver.AsInt64() == 2 && GameSession.Progress.Flag("boss_keeper")
              && Enumerable.Range(1, 7).All(k => GameSession.Progress.Flag($"tablet.gate_{k}")),
            "进度原样都在：fixture.old_map4 = 42、count.泥肝 = 2、boss_keeper、七块泥板");
        string abilities = string.Join(",", AbilityList().Split(',').OrderBy(a => a));
        Check(abilities == oldAbilities, $"能力和旧存档里一样（{abilities}）");
        Check(SameBytes(old, ReadSaveBytes()), "读档没有改动存档文件（迁移只在内存里）");

        // ② 地图
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        Vector3I here = MetSysApi.CurrentCoords;
        Check(layout.Find("S4_10").ContainsCell(new Vector2I(here.X, here.Y)) && MetSysApi.IsCellDiscovered(here, false),
            $"地图：S4_10 站着的格子 {here} 已探索");
        var lit = OtherMap4.SelectMany(id => Cells(layout.Find(id)).Where(c => MetSysApi.IsCellDiscovered(c, false)).Select(c => $"{id}{c}")).ToList();
        Check(lit.Count == 0, "地图：地图四其他新房间一格都没被旧数据点亮" + (lit.Count > 0 ? "：" + string.Join(",", lit.Take(6)) : ""));

        // ③ 死亡
        int respawned = 0;
        void OnRespawn() => respawned++;
        Events.Instance.CheckpointRespawned += OnRespawn;
        World.LoadRoomImmediate("S4_11", SpawnPoint.DefaultId);
        await Frames(5);
        Events.Instance.EmitSignal(Events.SignalName.PlayerDied);
        bool back = await WaitUntil(() => respawned > 0 && !World.IsTransitioning, 400);
        Events.Instance.CheckpointRespawned -= OnRespawn;
        await WaitSettled();
        saveSpawn = World.CurrentRoom?.GetNodeOrNull<Node2D>("Spawns/Save")?.GlobalPosition ?? Vector2.Inf;
        Check(back && RoomId == "S4_10" && (Player.GlobalPosition + new Vector2(0f, PlayerBody.HalfH)).DistanceTo(saveSpawn) < 40f,
            $"在终末之室死亡：回到 S4_10 的存档点（{RoomId}）");

        // ④ 在存档点用真实按键再存一次
        int saved = 0;
        void OnSaved() => saved++;
        Events.Instance.GameSaved += OnSaved;
        RealKeys.Down(Key.E);
        await Frames(2);
        RealKeys.Up(Key.E);
        bool wrote = await WaitUntil(() => saved > 0, 120);
        Events.Instance.GameSaved -= OnSaved;
        await Frames(5);
        byte[] fresh = ReadSaveBytes();
        GDictionary freshWorld = fresh == null ? null : GD.StrToVar(System.Text.Encoding.UTF8.GetString(fresh)).AsGodotDictionary()["world"].AsGodotDictionary();
        Check(wrote && freshWorld != null && freshWorld["room"].AsString() == "S4_10" && freshWorld["savePoint"].AsString() == "S4_10",
            $"在 S4_10 的存档点按 E 再存：存档文件里写的是 S4_10 / S4_10（{freshWorld?["room"]} / {freshWorld?["savePoint"]}）");
        menu = await QuitToMenuViaPauseMenu();
        Check(menu != null && !menu.ContinueButton.Disabled, "从暂停菜单回主菜单，“继续”可用");
        if (menu == null) return;
        started = await StartFromMenu(menu.ContinueButton);
        await WaitSettled();
        Check(started && RoomId == "S4_10" && Saves.CheckpointRoom == "S4_10" && GameSession.Progress.GetVar("fixture.old_map4").AsInt64() == 42,
            $"再按“继续”读新存档：照样到 S4_10，进度还在（在 {RoomId}）");
        DeleteSave();
    }

    private static System.Collections.Generic.IEnumerable<Vector3I> Cells(RoomDef room)
    {
        for (int x = 0; x < room.Size.X; x++)
            for (int y = 0; y < room.Size.Y; y++)
                yield return new Vector3I(room.Origin.X + x, room.Origin.Y + y, 0);
    }

    /// <summary>⑤ 迁移表本身。</summary>
    private void TableChecks()
    {
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        SaveRoomMigrations table = SaveRoomMigrations.LoadDefault();
        var savePoints = layout.Rooms.Where(r => r.HasSavePoint).Select(r => r.Id).ToHashSet();
        Check(table.Rooms.Count > 0 && table.Rooms.Keys.All(k => layout.Find(k) == null) && table.Rooms.Values.All(v => layout.Find(v) != null),
            $"迁移表：旧房间号（{string.Join(",", table.Rooms.Keys)}）都不在现在的房间表里，新房间号都在");
        Check(table.SavePoints.Keys.All(k => !savePoints.Contains(k)) && table.SavePoints.Values.All(savePoints.Contains),
            $"迁移表：旧存档点编号（{string.Join(",", table.SavePoints.Keys)}）都不是现在的存档点，新编号都是");
        string room = "S4_04", point = "S4_04";
        bool changed = table.Apply(ref room, ref point);
        string room2 = "S3_03", point2 = "S3_03";
        bool unchanged = !table.Apply(ref room2, ref point2);
        Check(changed && room == "S4_10" && point == "S4_10" && unchanged && room2 == "S3_03" && point2 == "S3_03",
            "迁移表查表：S4_04 / S4_04 → S4_10 / S4_10；现在还有的 S3_03 不换");
    }
}
