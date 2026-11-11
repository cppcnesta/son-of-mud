using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>
/// 关卡几何计划 G2：进入一个区域时，地图上显示这个区域全部房间的轮廓（已知），去过的才算已探索；别的区域不显示；
/// 进入新区域后它也显示；存档读档后保留。从正式入口 main_game.tscn 开新游戏。
/// </summary>
public partial class RegionMapTest : FlowTestBase
{
    protected override string SuiteName => "region_map";

    private WorldLayout Layout => World.Layout;

    private Vector3I[] CellsOf(string region) => Layout.Rooms
        .Where(r => WorldGame.RegionOf(r.Id) == region)
        .SelectMany(r => Enumerable.Range(0, r.Size.X * r.Size.Y)
            .Select(i => new Vector3I(r.Origin.X + i % r.Size.X, r.Origin.Y + i / r.Size.X, r.Layer)))
        .ToArray();

    private Vector3I[] CellsOfRoom(string id)
    {
        RoomDef r = Layout.Rooms.First(x => x.Id == id);
        return Enumerable.Range(0, r.Size.X * r.Size.Y).Select(i => new Vector3I(r.Origin.X + i % r.Size.X, r.Origin.Y + i / r.Size.X, r.Layer)).ToArray();
    }

    private static bool AllKnown(Vector3I[] cells) => cells.All(c => MetSysApi.IsCellDiscovered(c));
    private static bool NoneKnown(Vector3I[] cells) => cells.All(c => !MetSysApi.IsCellDiscovered(c));
    private static bool AnyExplored(Vector3I[] cells) => cells.Any(c => MetSysApi.IsCellDiscovered(c, false));

    protected override async Task RunChecks()
    {
        DeleteSave();
        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        Check(started && World != null && MetSysApi.Available && MetSysApi.HasSaveData, "正式入口开新游戏，MetSys 有存档数据");
        if (World == null) return;

        Vector3I[] s1 = CellsOf("S1"), s2 = CellsOf("S2"), s3 = CellsOf("S3"), s4 = CellsOf("S4");
        Check(s1.Length > 0 && s2.Length > 0, $"区域划分：S1 {s1.Length} 格、S2 {s2.Length} 格、S3 {s3.Length} 格、S4 {s4.Length} 格");
        Check(World.CurrentRoomDef?.Id == "S1_01" && AllKnown(s1), "进入 S1_01：地图 1 所有房间的格子都已知（显示轮廓）");
        Check(!AnyExplored(CellsOfRoom("S1_03")) && !AnyExplored(CellsOfRoom("S1_08")), "没去过的 S1_03、S1_08 只是已知，不算已探索");
        Check(AnyExplored(CellsOfRoom("S1_01")), "所在的 S1_01 算已探索");
        Check(NoneKnown(s2) && NoneKnown(s3) && NoneKnown(s4), "别的区域（S2、S3、S4）一格都不显示");

        World.LoadRoomImmediate("S2_01", "Default");
        await Frames(20);
        Check(World.CurrentRoomDef?.Id == "S2_01" && AllKnown(s2) && NoneKnown(s3) && NoneKnown(s4), "进入 S2_01：地图 2 全部已知，S3、S4 仍不显示");

        // 存档读档后保留
        World.LoadRoomImmediate("S1_04", SpawnPoint.SaveId);
        await Frames(20);
        bool saved = Saves.SaveToDisk("S1_04", Slot);
        bool loaded = await EnterMainGame(StartMode.Continue);
        await WaitSettled();
        Check(saved && loaded && AllKnown(s1) && AllKnown(s2) && NoneKnown(s3) && NoneKnown(s4), "存档读档后：地图 1、2 仍然已知，3、4 仍不显示");
        Check(!AnyExplored(CellsOfRoom("S1_08")), "读档后 S1_08 仍只是已知，没有被当成已探索");
    }
}
