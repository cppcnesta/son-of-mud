using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Feel;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 打击感的收尾（正式入口 main_game.tscn）：顿帧 / 震屏进行中遇到换房、主角死亡、重开、回主菜单、开新游戏、继续游戏时，
/// 立即恢复 TimeScale = 1、镜头偏移归零；之后马上开始一个新的顿帧，等过旧顿帧原定的结束时间，新的仍在进行（旧回调不会提前结束它）。
/// </summary>
public partial class HitFeelFlowTest : FlowTestBase
{
    protected override string SuiteName => "hit_feel_flow";

    private FeedbackService Service => FeedbackService.Instance;

    private async Task ProcessFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void RequestHitStop(float seconds) => Events.Instance.EmitSignal(Events.SignalName.HitStopRequested, seconds);
    private static void RequestShake(float strength, float seconds) => Events.Instance.EmitSignal(Events.SignalName.CameraShakeRequested, strength, seconds);

    /// <summary>
    /// 旧顿帧（old 秒）被收尾后马上开始新的（fresh 秒，比旧的长）：等过旧的原定结束时间，新的还在；之后按自己的时长结束。
    /// </summary>
    private async Task<bool> OldDoesNotEndNew(float old, float fresh)
    {
        RequestHitStop(fresh);
        bool started = Engine.TimeScale == 0.0;
        await ProcessFrames(Mathf.CeilToInt(old * 60f) + 3);
        bool stillFrozen = Engine.TimeScale == 0.0 && Service.HitStopActive;
        int guard = 0;
        while (Engine.TimeScale < 1.0 && guard++ < 120) await ProcessFrames(1);
        return started && stillFrozen && Engine.TimeScale == 1.0;
    }

    protected override async Task RunChecks()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        Check(finished && Starts.Count == 1 && Starts[0].success && Service != null, "开局：正式入口开新游戏成功，打击感服务在");
        if (World == null || Player == null || Service == null) return;
        await WaitSettled();

        // ① 换房
        RequestHitStop(0.3f);
        RequestShake(20f, 0.5f);
        await ProcessFrames(3);
        bool frozen = Engine.TimeScale == 0.0 && Cam.IsShaking;
        int cancels = Service.Cancels;
        World.LoadRoomImmediate("S1_02", SpawnPoint.DefaultId);
        bool resumed = Engine.TimeScale == 1.0 && Service.Cancels > cancels;
        await ProcessFrames(1);
        Check(frozen && resumed && Cam.Offset == Vector2.Zero && !Cam.IsShaking, "顿帧和震屏中换房：立即恢复 TimeScale = 1，镜头偏移归零");
        Check(await OldDoesNotEndNew(0.3f, 0.5f), "换房后马上开始新的顿帧：过了旧顿帧原定的结束时间新的仍在，之后按自己的时长结束");
        await WaitSettled();

        // ② 主角死亡
        RequestHitStop(0.4f);
        RequestShake(20f, 0.6f);
        await ProcessFrames(2);
        cancels = Service.Cancels;
        Player.HurtBox.Receive(new Hit { AttackId = "flow_kill", Damage = 9999f, SourcePosition = Player.GlobalPosition, Blockable = false });
        // PlayerDied 按设计延到本帧伤害结算全部结束后才发，所以下一帧检查
        await ProcessFrames(1);
        bool deathResumed = Engine.TimeScale == 1.0 && Service.Cancels > cancels;
        await ProcessFrames(1);
        Check(deathResumed && Cam.Offset == Vector2.Zero, "顿帧中主角死亡：PlayerDied 发出时恢复 TimeScale = 1，镜头偏移归零");
        await ProcessFrames(Mathf.CeilToInt(0.4f * 60f) + 5);
        Check(Engine.TimeScale == 1.0 && Cam.Offset == Vector2.Zero, "死亡后等过旧顿帧原定的结束时间：旧效果没有再改任何东西");
        await WaitUntil(() => Player.State is not SonOfMud.Player.States.DeadState && !World.IsTransitioning && !InputLock.IsLocked, 900);
        await Frames(10);

        // ③ 重开
        Node oldScene = MainGame;
        RequestHitStop(0.4f);
        RequestShake(20f, 0.6f);
        await ProcessFrames(2);
        int startsBefore = Starts.Count;
        Events.Instance.EmitSignal(Events.SignalName.RestartRunRequested);
        bool restartResumed = Engine.TimeScale == 1.0;
        await WaitUntil(() => Starts.Count > startsBefore && GetTree().CurrentScene != oldScene, 600);
        BindMainGame();
        await WaitSettled();
        Check(restartResumed && MainGame != null && MainGame != oldScene && Engine.TimeScale == 1.0 && Cam.Offset == Vector2.Zero && Service.ActiveSparks() == 0,
            "顿帧中重开：立即恢复；新一局 TimeScale = 1、镜头偏移为 0、没有旧的粒子");
        Check(await OldDoesNotEndNew(0.4f, 0.6f), "重开后马上开始新的顿帧：旧回调不会提前结束它");

        // 在 S1_04 存一次档，后面「继续游戏」要用
        World.LoadRoomImmediate("S1_04", SpawnPoint.SaveId);
        await Frames(15);
        await Tap(PlayerActions.Interact);
        await Frames(2);

        // ④ 回主菜单
        RequestHitStop(0.4f);
        await ProcessFrames(2);
        Events.Instance.EmitSignal(Events.SignalName.QuitToMenuRequested);
        bool menuResumed = Engine.TimeScale == 1.0;
        await WaitUntil(() => GetTree().CurrentScene != null && GetTree().CurrentScene.SceneFilePath != MainGameScene, 600);
        await ProcessFrames(Mathf.CeilToInt(0.4f * 60f) + 5);
        Check(menuResumed && Engine.TimeScale == 1.0 && GetTree().CurrentScene?.SceneFilePath != MainGameScene,
            "顿帧中回主菜单：立即恢复，主菜单里等过旧顿帧原定的结束时间仍是 1");

        // ⑤ 开新游戏（主菜单里正顿帧时开局）
        RequestHitStop(0.6f);
        bool menuFrozen = Engine.TimeScale == 0.0;
        finished = await EnterMainGame(StartMode.NewGame);
        Check(menuFrozen && finished && Engine.TimeScale == 1.0, "顿帧中开新游戏：开局（NewGameRequested）时恢复 TimeScale = 1");
        await WaitSettled();

        // ⑥ 继续游戏
        Events.Instance.EmitSignal(Events.SignalName.QuitToMenuRequested);
        await WaitUntil(() => GetTree().CurrentScene != null && GetTree().CurrentScene.SceneFilePath != MainGameScene, 600);
        RequestHitStop(0.6f);
        menuFrozen = Engine.TimeScale == 0.0;
        finished = await EnterMainGame(StartMode.Continue);
        Check(menuFrozen && finished && Starts[^1].success && Engine.TimeScale == 1.0, "顿帧中继续游戏：开局（ContinueRequested）时恢复 TimeScale = 1");
        await WaitSettled();
        Check(await OldDoesNotEndNew(0.6f, 0.8f), "继续游戏后马上开始新的顿帧：旧回调不会提前结束它");
    }
}
