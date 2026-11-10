using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.World;

/// <summary>
/// 交互组件（D14），从正式入口 main_game.tscn 启动：
/// ① S1_04 存档点：出现提示 → 按 E → SaveRequested、生成存档文件 → 走开后提示清空；
/// ② 锁输入时、攻击中、闪避中按 E 没有反应（回到自由状态后再按照常生效，作对照）；
/// ③ S3_08 门前按 E（连按两下）：只过门一次；
/// ④ 两个可交互物体重叠：只触发最近的那一个；最近的不可交互时改选次近的。
/// </summary>
public partial class InteractTest : FlowTestBase
{
    protected override string SuiteName => "interact_test";

    private PlayerInteractor _interactor;
    private readonly List<string> _prompts = new();
    private int _saves;

    protected override async Task RunChecks()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        Check(finished && Starts.Count == 1 && Starts[0].success, "正式入口开新游戏成功");
        if (World == null || Player == null) return;
        await WaitSettled();
        _interactor = Player.GetNodeOrNull<PlayerInteractor>("Interactor");
        Check(_interactor != null && _interactor.CollisionMask == Layers.Interact && _interactor.Monitoring,
            "玩家的 Interactor 是 Area2D 子节点，检测交互层");
        if (_interactor == null) return;
        Check(_interactor.CurrentPrompt == "" && _interactor.Current == null, "S1_01 出生点附近没有可交互的东西，提示为空");

        Events.Instance.InteractPromptChanged += OnPrompt;
        Events.Instance.SaveRequested += OnSave;
        try
        {
            await SavePointChecks();
            await BlockedChecks();
            await DoorChecks();
            await OverlapChecks();
        }
        finally
        {
            Events.Instance.InteractPromptChanged -= OnPrompt;
            Events.Instance.SaveRequested -= OnSave;
        }
    }

    private void OnPrompt(string prompt) => _prompts.Add(prompt);
    private void OnSave(StringName id) => _saves++;
    private string LastPrompt => _prompts.Count > 0 ? _prompts[^1] : "(无)";

    private async Task GoTo(string room, StringName spawn)
    {
        ReleaseInputs();
        World.LoadRoomImmediate(room, spawn);
        await Frames(15);
    }

    // ---------- ① 存档点 ----------

    private async Task SavePointChecks()
    {
        await GoTo("S1_04", SpawnPoint.SaveId);
        Check(_interactor.CurrentPrompt == "存档" && LastPrompt == "存档", $"S1_04 存档点：出现提示“存档”（事件 {LastPrompt}）");
        Check(!FileAccess.FileExists(SavePath), "按 E 之前没有存档文件");

        await Tap(PlayerActions.Interact);
        await Frames(2);
        Check(_saves == 1, $"按 E：收到 SaveRequested 一次（实际 {_saves}）");
        Check(FileAccess.FileExists(SavePath), "按 E：生成存档文件");

        Input.ActionPress(PlayerActions.Left);
        bool cleared = await WaitUntil(() => _interactor.CurrentPrompt == "", 240);
        Input.ActionRelease(PlayerActions.Left);
        await Frames(10);
        Check(cleared && LastPrompt == "" && _interactor.Current == null, $"走开后提示清空（事件 {LastPrompt}）");
        await Tap(PlayerActions.Interact);
        await Frames(2);
        Check(_saves == 1, "附近没有可交互对象时按 E 没有反应");
    }

    // ---------- ② 不该触发的状态 ----------

    private async Task BlockedChecks()
    {
        await GoTo("S1_04", SpawnPoint.SaveId);
        Check(_interactor.CurrentPrompt == "存档", "回到存档点，提示“存档”");
        int before = _saves;

        InputLock.Request(InputLock.Narrative, true);
        await Frames(3);
        await Tap(PlayerActions.Interact);
        await Frames(2);
        bool lockedState = Player.State is LockedState;
        InputLock.Request(InputLock.Narrative, false);
        await Frames(10);
        Check(_saves == before && lockedState, $"锁输入时按 E：没有反应（SaveRequested {_saves - before} 次，状态锁定 {lockedState}）");

        await Tap(PlayerActions.Attack);
        string stateAtE = await PressInteractAndReadState();
        Check(_saves == before && stateAtE is nameof(AttackState) or nameof(AttackRecoveryState),
            $"攻击中按 E：没有反应（按 E 时状态 {stateAtE}，SaveRequested {_saves - before} 次）");
        await WaitUntil(() => Player.State is IdleState, 180);

        await Tap(PlayerActions.Dodge);
        stateAtE = await PressInteractAndReadState();
        Check(_saves == before && stateAtE == nameof(DodgeState), $"闪避中按 E：没有反应（按 E 时状态 {stateAtE}）");
        await WaitUntil(() => Player.State is IdleState, 180);

        // 对照：回到自由状态后按 E 照常存档
        await GoTo("S1_04", SpawnPoint.SaveId);
        await Tap(PlayerActions.Interact);
        await Frames(2);
        Check(_saves == before + 1, "对照：回到 Idle 后在存档点按 E 照常存档");
    }

    /// <summary>按下交互键，在它被处理的那一帧记录玩家状态。</summary>
    private async Task<string> PressInteractAndReadState()
    {
        Input.ActionPress(PlayerActions.Interact);
        await Frames(1);
        string state = Player.State?.GetType().Name ?? "";
        if (_interactor.Current == null) state += "（但附近没有可交互对象）";
        Input.ActionRelease(PlayerActions.Interact);
        await Frames(2);
        return state;
    }

    // ---------- ③ 门 ----------

    private async Task DoorChecks()
    {
        await GoTo("S3_08", "From_S4_01");
        Input.ActionPress(PlayerActions.Left);
        bool atDoor = await WaitUntil(() => _interactor.CurrentPrompt == "进入", 240);
        Input.ActionRelease(PlayerActions.Left);
        await WaitUntil(() => Player.State is IdleState, 60);
        await Frames(3);
        Check(atDoor && _interactor.CurrentPrompt == "进入" && _interactor.Current is RoomDoor, "走到 S3_08 的门前，提示“进入”");

        var entered = new List<string>();
        int teleports = 0;
        void OnEnter(string id) => entered.Add(id);
        void OnTele(StringName s) => teleports++;
        Events.Instance.RoomEntered += OnEnter;
        Events.Instance.PlayerTeleported += OnTele;
        await Tap(PlayerActions.Interact);
        await Tap(PlayerActions.Interact);
        await WaitUntil(() => !World.IsTransitioning, 400);
        await Frames(10);
        Events.Instance.RoomEntered -= OnEnter;
        Events.Instance.PlayerTeleported -= OnTele;
        Check(string.Join(",", entered) == "S4_01" && teleports == 1 && World.CurrentRoomDef?.Id == "S4_01",
            $"在门前按 E（连按两下）：只过门一次到 S4_01（进入 {string.Join(",", entered)}，传送 {teleports} 次）");
        Check(!InputLock.IsLocked, "过门结束后输入锁解除");
    }

    // ---------- ④ 重叠 ----------

    private async Task OverlapChecks()
    {
        await GoTo("S1_01", SpawnPoint.DefaultId);
        Check(_interactor.Current == null, "重叠搭建：S1_01 出生点附近原本没有可交互对象");
        Vector2 at = Player.GlobalPosition - World.CurrentRoom.GlobalPosition;
        var far = new TestInteractable { Name = "Far", PromptText = "远", Position = at + new Vector2(24, 0) };
        var near = new TestInteractable { Name = "Near", PromptText = "近", Position = at + new Vector2(6, 0) };
        World.CurrentRoom.AddChild(far);
        World.CurrentRoom.AddChild(near);
        await Frames(5);
        bool bothOverlap = _interactor.GetOverlappingAreas().Contains(near) && _interactor.GetOverlappingAreas().Contains(far);
        Check(bothOverlap && _interactor.Current == near && _interactor.CurrentPrompt == "近", $"两个物体都重叠时，选中最近的（提示 {_interactor.CurrentPrompt}）");

        await Tap(PlayerActions.Interact);
        await Frames(2);
        Check(near.Count == 1 && far.Count == 0, $"按 E 只触发最近的那一个（近 {near.Count}、远 {far.Count}）");

        near.Enabled = false;
        await Frames(3);
        Check(_interactor.Current == far && _interactor.CurrentPrompt == "远" && LastPrompt == "远", "最近的那个不能交互时，改选次近的并更新提示");
        await Tap(PlayerActions.Interact);
        await Frames(2);
        Check(near.Count == 1 && far.Count == 1, $"再按 E 只触发次近的那一个（近 {near.Count}、远 {far.Count}）");

        far.QueueFree();
        near.QueueFree();
        await Frames(3);
        Check(_interactor.CurrentPrompt == "" && LastPrompt == "", "物体都移走后提示清空");
    }
}
