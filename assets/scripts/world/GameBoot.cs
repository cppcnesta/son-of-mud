using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 正式游戏的开局与每一局的生命周期，只放在 main_game.tscn 里（game.tscn 是测试容器，不会自动开局）。
/// - 开局：_Ready 后延后一帧（所有节点已进树、订阅好，玩家已发过 PlayerSpawned），先检查 WorldGame 已经拿到玩家、
///   SaveService 已经就绪，不满足按开局失败处理；然后按 GameSession.PendingStart 发 ContinueRequested
///   或 NewGameRequested（None 是直接运行本场景的情况，按新游戏），并把 PendingStart 设回 None。
///   全工程只有这里发 NewGameRequested。
/// - 开局失败（GameStartFinished(false)）：原因写进 GameSession.LastStartError，PendingStart 设回 None，回主菜单。
/// - QuitToMenuRequested：清暂停、清输入锁，切到主菜单。
/// - RestartRunRequested：PendingStart = NewGame，清暂停、清输入锁，重新载入整个正式场景（玩家、World、存档服务都重建）。
/// SaveService 属于 Save 系统，这里只把它当 Node 检查是否就绪，不引用它的类型。
/// </summary>
[GlobalClass]
public partial class GameBoot : Node
{
    [Export] public GameBootSettings Settings { get; set; }
    [Export] public WorldGame World { get; set; }
    /// <summary>存档服务节点（只检查它已进树、已就绪）。</summary>
    [Export] public Node SaveService { get; set; }

    /// <summary>本实例已经发出开局请求、正在等 GameStartFinished。</summary>
    public bool AwaitingStart { get; private set; }
    /// <summary>本实例发出的开局方式（None 表示还没开局）。</summary>
    public StartMode StartedWith { get; private set; } = StartMode.None;
    /// <summary>已经在切场景（回主菜单或重开），之后的请求一律忽略。</summary>
    public bool Leaving { get; private set; }

    public override void _EnterTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.GameStartFinished += OnGameStartFinished;
        Events.Instance.QuitToMenuRequested += OnQuitToMenuRequested;
        Events.Instance.RestartRunRequested += OnRestartRunRequested;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.GameStartFinished -= OnGameStartFinished;
        Events.Instance.QuitToMenuRequested -= OnQuitToMenuRequested;
        Events.Instance.RestartRunRequested -= OnRestartRunRequested;
    }

    public override async void _Ready()
    {
        Settings ??= ResourceLoader.Exists(GameBootSettings.DefaultPath)
            ? GD.Load<GameBootSettings>(GameBootSettings.DefaultPath)
            : new GameBootSettings();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(this) && IsInsideTree()) Begin();
    }

    private void Begin()
    {
        if (Leaving || StartedWith != StartMode.None) return;
        StartMode mode = GameSession.PendingStart == StartMode.Continue ? StartMode.Continue : StartMode.NewGame;
        GameSession.PendingStart = StartMode.None;
        StartedWith = mode;
        AwaitingStart = true;

        string problem = CheckReady();
        if (problem != null)
        {
            // 开局前提不满足：和存档读不出来一样按开局失败处理（发给所有订阅者，自己也会收到）。
            if (Events.Instance != null) Events.Instance.EmitSignal(Events.SignalName.GameStartFinished, false, problem);
            else OnGameStartFinished(false, problem);
            return;
        }
        Events.Instance.EmitSignal(mode == StartMode.Continue ? Events.SignalName.ContinueRequested : Events.SignalName.NewGameRequested);
    }

    /// <summary>开局前提：Events 在、WorldGame 已经拿到玩家、SaveService 已经就绪。返回 null 表示满足，否则是原因。</summary>
    private string CheckReady()
    {
        if (Events.Instance == null) return "开局失败：没有 Events 总线。";
        if (!IsInstanceValid(World) || !World.IsInsideTree()) return "开局失败：找不到 WorldGame。";
        if (!IsInstanceValid(World.Player) || !World.Player.IsInsideTree()) return "开局失败：WorldGame 还没有拿到玩家。";
        if (!IsInstanceValid(SaveService) || !SaveService.IsInsideTree() || !SaveService.IsNodeReady()) return "开局失败：存档服务没有就绪。";
        return null;
    }

    private void OnGameStartFinished(bool success, string error)
    {
        if (!AwaitingStart) return;
        AwaitingStart = false;
        if (success)
        {
            GameSession.LastStartError = "";
            return;
        }
        GameSession.LastStartError = string.IsNullOrEmpty(error) ? "开局失败（没有给出原因）。" : error;
        GameSession.PendingStart = StartMode.None;
        GD.PushWarning("GameBoot：" + GameSession.LastStartError + " 回到主菜单。");
        GoTo(Settings.MenuScene);
    }

    private void OnQuitToMenuRequested() => GoTo(Settings.MenuScene);

    private void OnRestartRunRequested()
    {
        if (Leaving) return;
        GameSession.PendingStart = StartMode.NewGame;
        GoTo(Settings.GameScene);
    }

    /// <summary>清掉暂停和输入锁的所有来源，再切场景（当前整个游戏实例随之释放）。</summary>
    private void GoTo(string scenePath)
    {
        if (Leaving) return;
        Leaving = true;
        AwaitingStart = false;
        PauseService.Reset();
        InputLock.Reset();
        // 不在当前调用栈里切场景：开局失败可能在开局的同一帧、同步地一路传回到这里，
        // 这时场景里还有排队中的初始化（玩家状态机进初始状态等）。等这一帧结束再离开，避免拆到一半的节点被继续调用。
        SceneTree tree = GetTree();
        Callable.From(() =>
        {
            Error result = tree.ChangeSceneToFile(scenePath);
            if (result != Error.Ok) GD.PushError($"GameBoot：切换到 {scenePath} 失败：{result}");
        }).CallDeferred();
    }
}
