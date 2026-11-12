using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 正式入口读档：先用正式入口开新游戏、到 S1_04 的存档点真的按 E 存一次档（期间写一个进度变量），
/// 存档后再改掉这个变量；然后 PendingStart = Continue 重新进入 main_game.tscn：
/// 人在 S1_04 的存档点，进度变量回到存档时的值，能力恢复，收到 GameStartFinished(true)。
/// </summary>
public partial class BootContinueTest : FlowTestBase
{
    protected override string SuiteName => "boot_continue";

    private static readonly StringName TestVar = "flow_test_offering";

    protected override async Task RunChecks()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        Check(finished && Starts.Count == 1 && Starts[0].success, "第一局：正式入口开新游戏成功");
        if (World == null || Player == null) return;
        await WaitSettled();

        // 到 S1_04 的存档点：把人放到存档点脚下的 Save 出生点，再真的按 E
        World.LoadRoomImmediate("S1_04", SpawnPoint.SaveId);
        await Frames(15);
        GameSession.Progress.SetVar(TestVar, 7);
        var interactor = Player.GetNode<PlayerInteractor>("Interactor");
        Check(interactor.CurrentPrompt == "存档", $"站在 S1_04 存档点上出现“存档”提示（实际“{interactor.CurrentPrompt}”）");

        int saves = 0;
        StringName savedId = "";
        void OnSave(StringName id) { saves++; savedId = id; }
        Events.Instance.SaveRequested += OnSave;
        await Tap(PlayerActions.Interact);
        await Frames(2);
        Events.Instance.SaveRequested -= OnSave;
        Check(saves == 1 && savedId == "S1_04", $"按 E 发出 SaveRequested(S1_04) 一次（实际 {saves} 次，{savedId}）");
        Check(FileAccess.FileExists(SavePath), "生成了存档文件 save_0.sav");
        byte[] savedBytes = ReadSaveBytes();
        string savedAbilities = AbilityList();
        ulong firstPlayer = Player.GetInstanceId();

        // 存档之后的改动不应该被读回来
        GameSession.Progress.SetVar(TestVar, 99);
        World.LoadRoomImmediate("S1_01", SpawnPoint.DefaultId);
        await Frames(5);

        int startsBefore = Starts.Count;
        string loadedRoom = null;
        void OnLoaded(string room) => loadedRoom = room;
        Events.Instance.GameLoaded += OnLoaded;
        finished = await EnterMainGame(StartMode.Continue);
        Events.Instance.GameLoaded -= OnLoaded;
        Check(finished && MainGame != null, "第二次进入正式入口（继续）收到开局结果");
        if (World == null || Player == null) return;
        await WaitSettled();
        await Frames(20);

        Check(Starts.Count == startsBefore + 1 && Starts[^1].success, $"读档：收到 GameStartFinished(true) 恰好一次（{Starts[^1].error}）");
        Check(loadedRoom == "S1_04", $"读档：GameLoaded(S1_04)（实际 {loadedRoom}）");
        Check(Player.GetInstanceId() != firstPlayer && !GodotObject.IsInstanceIdValid(firstPlayer), "读档：玩家是新实例，上一局的玩家已释放");
        Check(AllPlayers().Count == 1 && World.Player == Player && Cam.Target == Player, "读档：场景里只有一个玩家，WorldGame 和镜头都拿到它");

        SpawnPoint save = World.CurrentRoom?.FindSpawn(SpawnPoint.SaveId);
        Check(World.CurrentRoomDef?.Id == "S1_04", $"读档：人在 S1_04（实际 {World.CurrentRoomDef?.Id}）");
        Check(save != null && save.SpawnId == SpawnPoint.SaveId && Mathf.Abs(Player.GlobalPosition.X - save.GlobalPosition.X) < 1f && Mathf.Abs(Player.GlobalPosition.Y - save.GlobalPosition.Y) < 32f && Player.IsOnFloor(),
            $"读档：人在存档点的 Save 出生点（玩家 {Player.GlobalPosition}，出生点 {save?.GlobalPosition}）");
        Check(GameSession.Progress.GetVar(TestVar).AsInt32() == 7, $"读档：进度变量回到存档时的值 7（实际 {GameSession.Progress.GetVar(TestVar)}）");
        Check(AbilityList() == savedAbilities && AbilityList() == "block,switch_form,wall_climb", $"读档：能力恢复（{AbilityList()}）");
        Check(Saves.CheckpointSavePoint == "S1_04" && Saves.CheckpointRoom == "S1_04", "读档：最近存档点是 S1_04");
        Check(Player.GetNode<PlayerInteractor>("Interactor").CurrentPrompt == "存档", "读档：站在存档点上，交互提示是“存档”");
        Check(Player.Stats.Health == 70 && Player.Stats.MaxHealth == 70 && Player.Forms.Form == FormController.Water, "读档：新玩家满血（没吃生命之面包，70/70）、水形态");
        Check(GameSession.PendingStart == StartMode.None && Boot.StartedWith == StartMode.Continue, "读档：GameBoot 按 Continue 开局，PendingStart 已清零");
        Check(SameBytes(savedBytes, ReadSaveBytes()), "读档：存档文件没有被改动");
        Check(NoPauseOrLock(), "读档后没有暂停、没有输入锁");
    }
}
