using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 重开一局（RestartRunRequested）：一律重建整个游戏实例，不在当前实例里重置。
/// 用正式入口开局，在 S1_04 存档点按 E 存一次档，然后把状态弄乱（残血、泥形态、闪避冷却、增伤就绪、进度变量、
/// 已探索的格子，外加暂停和输入锁），发 RestartRunRequested：
/// 玩家是新实例、旧实例已释放；140 血、水形态、冷却为 0、增伤未就绪；变量清空、能力回到初始；
/// 已探索的格子清空；人在 S1_01；暂停和输入锁没有残留；存档文件没被改动。
/// </summary>
public partial class RestartRunTest : FlowTestBase
{
    protected override string SuiteName => "restart_run";

    private static readonly StringName TestVar = "flow_restart_flag";

    protected override async Task RunChecks()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        Check(finished && Starts.Count == 1 && Starts[0].success, "第一局：正式入口开新游戏成功");
        if (World == null || Player == null) return;
        await WaitSettled();
        Node oldScene = MainGame;
        PlayerCharacter oldPlayer = Player;
        ulong oldId = oldPlayer.GetInstanceId();
        float freshRatio = MetSysApi.GetExploredRatio();

        // 先在 S1_04 存一次档：之后重开不能改动它
        World.LoadRoomImmediate("S1_04", SpawnPoint.SaveId);
        await Frames(15);
        await Tap(PlayerActions.Interact);
        await Frames(2);
        byte[] savedBytes = ReadSaveBytes();
        Check(savedBytes != null, "搭建：在 S1_04 存档点按 E 生成了存档文件");
        Vector3I explored = MetSysApi.CurrentCoords;
        Check(MetSysApi.IsCellDiscovered(explored, false), $"搭建：S1_04 的格子 {explored} 已探索");
        World.LoadRoomImmediate("S1_05", SpawnPoint.DefaultId);
        await Frames(10);
        Vector3I explored2 = MetSysApi.CurrentCoords;
        Check(MetSysApi.IsCellDiscovered(explored2, false) && MetSysApi.GetExploredRatio() > freshRatio,
            $"搭建：S1_05 的格子 {explored2} 也已探索，探索比例 {freshRatio:F4} → {MetSysApi.GetExploredRatio():F4}");

        // 把玩家状态弄乱：泥形态（真按键）→ 闪避（真按键，泥闪避冷却 4 秒）→ 挨一刀残血 → 增伤就绪
        await Tap(PlayerActions.SwitchForm);
        await Frames(3);
        await Tap(PlayerActions.Dodge);
        await Frames(40);
        oldPlayer.HurtBox.Receive(new Hit { AttackId = "flow_restart_hit", Damage = 60f, SourcePosition = oldPlayer.GlobalPosition, Blockable = false });
        await Frames(30);
        oldPlayer.Timers.Bonus.Grant(1.6f);
        GameSession.Progress.SetVar(TestVar, true);
        GameSession.Progress.SetVar("flow_restart_count", 3);
        PauseService.Request(PauseService.PauseMenu, true);
        InputLock.Request(InputLock.Narrative, true);
        await Frames(2);

        Check(oldPlayer.Forms.Form == FormController.Mud && oldPlayer.Stats.Health < 140 && oldPlayer.Stats.Health > 0 && oldPlayer.Timers.DodgeCooldown > 1f
              && oldPlayer.Timers.Bonus.Ready && GameSession.Progress.Flag(TestVar) && PauseService.IsPaused && InputLock.IsLocked,
            $"搭建：已弄乱（形态 {oldPlayer.Forms.Form}、血 {oldPlayer.Stats.Health}、闪避冷却 {oldPlayer.Timers.DodgeCooldown:F2}、" +
            $"增伤 {oldPlayer.Timers.Bonus.Ready}、暂停 {PauseService.IsPaused}、锁 {InputLock.IsLocked}）");

        int startsBefore = Starts.Count;
        Events.Instance.EmitSignal(Events.SignalName.RestartRunRequested);
        finished = await WaitUntil(() => Starts.Count > startsBefore && GetTree().CurrentScene != oldScene, 600);
        BindMainGame();
        Check(finished && MainGame != null && MainGame != oldScene, "重开：重新载入了正式入口 main_game.tscn（新的场景实例）");
        if (World == null || Player == null) return;
        await WaitSettled();
        await Frames(10);

        Check(Starts.Count == startsBefore + 1 && Starts[^1].success, "重开：收到 GameStartFinished(true) 恰好一次");
        Check(Player.GetInstanceId() != oldId && !GodotObject.IsInstanceIdValid(oldId) && !GodotObject.IsInstanceValid(oldScene),
            "重开：玩家是新实例（实例 ID 不同），旧玩家和旧场景都已释放");
        Check(AllPlayers().Count == 1 && World.Player == Player && Cam.Target == Player, "重开：场景里只有一个玩家，WorldGame 和镜头都拿到它");
        Check(Player.Stats.Health == 140 && Player.Stats.MaxHealth == 140, $"重开：血量 140（实际 {Player.Stats.Health}）");
        Check(Player.Forms.Form == FormController.Water, "重开：水形态");
        PlayerTimers t = Player.Timers;
        Check(t.DodgeCooldown == 0f && t.BlockCooldown == 0f && t.AttackInterval == 0f && t.SwitchCooldown == 0f && t.Recovery.Multiplier() == 1f,
            $"重开：各种冷却为 0、没有减速（闪避 {t.DodgeCooldown}、格挡 {t.BlockCooldown}、攻击 {t.AttackInterval}、切换 {t.SwitchCooldown}）");
        Check(!t.Bonus.Ready, "重开：增伤未就绪");
        Check(!GameSession.Progress.VariableKeys.Any(), "重开：进度变量清空");
        Check(AbilityList() == "block,switch_form,wall_climb", $"重开：能力恢复初始（{AbilityList()}）");
        Check(!MetSysApi.IsCellDiscovered(explored, false) && !MetSysApi.IsCellDiscovered(explored2, false),
            $"重开：上一局探索的格子 {explored}、{explored2} 已清空");
        Check(Mathf.IsEqualApprox(MetSysApi.GetExploredRatio(), freshRatio), $"重开：探索比例回到新游戏时的 {freshRatio:F4}（实际 {MetSysApi.GetExploredRatio():F4}）");
        SpawnPoint spawn = World.CurrentRoom?.FindSpawn(SpawnPoint.DefaultId);
        Check(World.CurrentRoomDef?.Id == "S1_01" && spawn != null && Mathf.Abs(Player.GlobalPosition.X - spawn.GlobalPosition.X) < 1f,
            $"重开：人在 S1_01 的 Default 出生点（{World.CurrentRoomDef?.Id} {Player.GlobalPosition}）");
        Check(Saves.CheckpointRoom == "S1_01" && Saves.CheckpointSavePoint == "", "重开：检查点回到新游戏起点");
        Check(NoPauseOrLock() && !PauseService.IsRequestedBy(PauseService.PauseMenu) && !InputLock.IsRequestedBy(InputLock.Narrative),
            "重开：暂停和输入锁都没有残留");
        Check(GameSession.PendingStart == StartMode.None && Boot.StartedWith == StartMode.NewGame, "重开：GameBoot 按 NewGame 开局，PendingStart 已清零");
        Check(SameBytes(savedBytes, ReadSaveBytes()), "重开：存档文件没被改动");
    }
}
