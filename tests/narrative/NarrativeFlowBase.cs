using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative;
using SonOfMud.Narrative.Parsing;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Player;

/// <summary>
/// 剧情流程测试的公共部分（都从正式入口 main_game.tscn 启动，用真实玩家，不手动创建玩家、不手动解锁能力）：
/// 拿到剧情层和交互组件、记录剧情相关事件、用真实按键（InputEventKey）按 E、走路、把人放到出生点。
/// </summary>
public abstract partial class NarrativeFlowBase : FlowTestBase
{
    public const string TestScriptPath = "res://tests/narrative/fixtures/test_story.orph";

    protected NarrativeDirector Narr;
    protected PlayerInteractor Interactor;
    protected readonly List<string> Started = new();
    protected readonly List<string> Ended = new();
    protected readonly List<string> Requests = new();
    protected readonly List<string> Collected = new();
    protected int Restarts;
    protected int NewGames;
    protected ErrorCatcher Errors;

    protected void BindNarrative()
    {
        Narr = MainGame?.GetNodeOrNull<NarrativeDirector>("NarrativeLayer");
        Interactor = Player?.GetNodeOrNull<PlayerInteractor>("Interactor");
    }

    protected void Listen()
    {
        Events.Instance.NarrativeStarted += OnStarted;
        Events.Instance.NarrativeFinished += OnEnded;
        Events.Instance.StoryRequested += OnRequested;
        Events.Instance.TabletCollected += OnCollected;
        Events.Instance.RestartRunRequested += OnRestart;
        Events.Instance.NewGameRequested += OnNewGame;
        Errors = new ErrorCatcher();
        OS.AddLogger(Errors);
    }

    protected void Unlisten()
    {
        Events.Instance.NarrativeStarted -= OnStarted;
        Events.Instance.NarrativeFinished -= OnEnded;
        Events.Instance.StoryRequested -= OnRequested;
        Events.Instance.TabletCollected -= OnCollected;
        Events.Instance.RestartRunRequested -= OnRestart;
        Events.Instance.NewGameRequested -= OnNewGame;
        if (Errors != null) OS.RemoveLogger(Errors);
        // 临时的 Godot 数组 / 字典在引擎还在的时候回收，免得退出后由终结器释放时崩溃。
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void OnStarted(string script, string label) => Started.Add($"{script}:{label}");
    private void OnEnded(string script, string label) => Ended.Add($"{script}:{label}");
    private void OnRequested(string script, string label) => Requests.Add($"{script}:{label}");
    private void OnCollected(StringName id) => Collected.Add(id);
    private void OnRestart() => Restarts++;
    private void OnNewGame() => NewGames++;

    /// <summary>把测试剧本 test_story.orph 放进剧本表（每次都重新解析）。</summary>
    protected static OrphScript LoadTestScript()
    {
        OrphScript script = OrphLibrary.LoadFile(TestScriptPath);
        OrphLibrary.Shared.Add(script, TestScriptPath);
        return script;
    }

    protected static void Request(string script, string label) =>
        Events.Instance.EmitSignal(Events.SignalName.StoryRequested, script, label);

    protected bool NarrativePaused => PauseService.IsRequestedBy(PauseService.Narrative) && GetTree().Paused;

    // ---------- 真实按键 ----------

    protected static void KeyDown(Key key) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });

    protected static void KeyUp(Key key) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });

    /// <summary>按一下真实的键（一个键同时触发它绑定的所有动作，比如 E = 交互 + 推进对话）。</summary>
    protected async Task KeyTap(Key key)
    {
        KeyDown(key);
        await Frames(1);
        KeyUp(key);
        await Frames(1);
    }

    /// <summary>对话框能接受推进了（字打完，或者选项 / 泥板界面开着，或者剧情已经结束）。</summary>
    protected bool ReadyForKey() =>
        Narr == null || !Narr.MainActive || !Narr.Dialog.IsTyping || Narr.Choice.IsOpen || Narr.Tablet.IsOpen;

    /// <summary>
    /// 等字打完就按 E，直到 done 成立。选项打开时停下来交给调用方（返回 done 的结果）。
    /// observe 在每次按键前调用（记录当时的名牌和台词）。
    /// </summary>
    protected async Task<bool> AdvanceUntil(Func<bool> done, int maxTaps = 40, Action observe = null)
    {
        for (int i = 0; i < maxTaps; i++)
        {
            if (done()) return true;
            await WaitUntil(ReadyForKey, 300);
            if (done()) return true;
            if (Narr != null && Narr.Choice.IsOpen) return done();
            observe?.Invoke();
            await KeyTap(Key.E);
            await Frames(2);
        }
        return done();
    }

    // ---------- 走路 ----------

    protected async Task Teleport(string room, StringName spawn)
    {
        ReleaseInputs();
        World.LoadRoomImmediate(room, spawn);
        await Frames(15);
    }

    /// <summary>按住方向键直到条件成立（或超时），然后松开。</summary>
    protected async Task<bool> HoldUntil(StringName direction, Func<bool> condition, int maxFrames = 600)
    {
        Input.ActionPress(direction);
        bool ok = await WaitUntil(condition, maxFrames);
        Input.ActionRelease(direction);
        await Frames(2);
        return ok;
    }

    /// <summary>朝一个方向走，直到交互提示变成 prompt；停稳后如果走过了头，往回挪一点。</summary>
    protected async Task<bool> WalkToPrompt(string prompt, StringName direction, int maxFrames = 600)
    {
        await HoldUntil(direction, () => Interactor.CurrentPrompt == prompt, maxFrames);
        await WaitUntil(() => Mathf.Abs(Player.Velocity.X) < 1f, 60);
        await Frames(2);
        if (Interactor.CurrentPrompt != prompt)
        {
            StringName back = direction == PlayerActions.Right ? PlayerActions.Left : PlayerActions.Right;
            await HoldUntil(back, () => Interactor.CurrentPrompt == prompt, 120);
            await WaitUntil(() => Mathf.Abs(Player.Velocity.X) < 1f, 60);
            await Frames(2);
        }
        return Interactor.CurrentPrompt == prompt;
    }

    protected float LocalX => Player.GlobalPosition.X - World.CurrentRoom.GlobalPosition.X;
}
