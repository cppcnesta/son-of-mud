using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>
/// 死亡复活（D4）：没有存档时复活在 S1_01；存档后再写一个变量 → PlayerDied → 复活在存档点，这个变量还在；复活不读盘。
/// </summary>
public partial class RespawnTest : WorldTestBase
{
    protected override string SuiteName => "respawn_test";

    protected override async Task RunChecks()
    {
        await SetUpGame();
        Saves.SaveDirectory = "user://world_tests_respawn";
        string path = Saves.SlotPath(0);
        if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));

        int respawned = 0, teleported = 0;
        void OnRespawned() => respawned++;
        void OnTeleported(StringName spawnId) => teleported++;
        Events.Instance.CheckpointRespawned += OnRespawned;
        Events.Instance.PlayerTeleported += OnTeleported;

        // ---- 没有存档：复活在新游戏起点 S1_01 ----
        World.LoadRoomImmediate("S1_03", "From_S1_02");
        await Frames(10);
        teleported = 0;
        Check(Saves.CheckpointRoom == "S1_01" && Saves.CheckpointSavePoint == "", "还没存档时，最近检查点是新游戏起点 S1_01");
        Events.Instance.EmitSignal(Events.SignalName.PlayerDied);
        bool back = await WaitUntil(() => !World.IsTransitioning && World.CurrentRoomDef?.Id == "S1_01", 240);
        Check(back && World.FadeTimeouts == 0, "没有存档时死亡 → 复活在 S1_01（经淡入淡出，无超时）");
        SpawnPoint start = World.CurrentRoom.FindSpawn(SpawnPoint.DefaultId);
        Check(start != null && Mover.GlobalPosition.DistanceTo(start.GlobalPosition) < 4f, "复活在 S1_01 的 Default 出生点");
        Check(respawned == 1 && teleported == 1, $"发了 CheckpointRespawned 和 PlayerTeleported 各一次（{respawned}/{teleported}）");

        // ---- 在 S1_04 存档，之后再写一个变量 ----
        World.LoadRoomImmediate("S1_04", "Default");
        await Frames(5);
        GameSession.Progress.SetVar("before_save", true);
        Check(Saves.SaveToDisk("S1_04") && FileAccess.FileExists(path), "在 S1_04 存档");
        GameSession.Progress.SetVar("after_save", true);
        GameSession.Progress.Unlock("heavy_slam");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path)); // 复活不读盘：删掉存档也不影响

        World.LoadRoomImmediate("S1_05", "Default");
        await Frames(5);
        respawned = 0;
        teleported = 0;
        Events.Instance.EmitSignal(Events.SignalName.PlayerDied);
        bool atSave = await WaitUntil(() => !World.IsTransitioning && World.CurrentRoomDef?.Id == "S1_04", 240);
        Check(atSave && World.FadeTimeouts == 0, "存档后死亡 → 复活在存档点所在房间 S1_04");
        SpawnPoint saveSpawn = World.CurrentRoom.FindSpawn(SpawnPoint.SaveId);
        Check(saveSpawn != null && Mover.GlobalPosition.DistanceTo(saveSpawn.GlobalPosition) < 4f, "复活在存档点脚下（Save 出生点）");
        Check(GameSession.Progress.Flag("after_save") && GameSession.Progress.HasAbility("heavy_slam"), "D4：存档之后才拿到的变量和能力在复活后仍然保留");
        Check(GameSession.Progress.Flag("before_save"), "存档之前的变量也在");
        Check(!FileAccess.FileExists(path), "复活过程没有读盘也没有写盘（存档文件已删且没有重新出现）");
        Check(respawned == 1 && teleported == 1 && Mover.TeleportCount >= 2, "复活时发 CheckpointRespawned、PlayerTeleported 各一次");
        Check(!InputLock.IsLocked, "复活结束后输入已解锁");

        Events.Instance.CheckpointRespawned -= OnRespawned;
        Events.Instance.PlayerTeleported -= OnTeleported;
        await TearDownGame();
    }
}
