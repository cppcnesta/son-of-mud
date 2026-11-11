using System;
using System.Collections.Generic;
using System.Linq;
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

    // =================================================================
    // 屏幕布局（玩家实测发现：剧情界面根节点大小为 0，对话框被算到屏幕外，逻辑测试全过却看不见）
    // =================================================================

    /// <summary>屏幕矩形（容 1 像素的取整误差）。</summary>
    protected Rect2 ScreenRect => GetViewport().GetVisibleRect().Grow(1f);

    /// <summary>控件真的显示在屏幕上：在树里可见、有面积、整个矩形落在屏幕内。</summary>
    protected bool OnScreen(Control control, out string info)
    {
        if (control == null) { info = "控件不存在"; return false; }
        Rect2 r = control.GetGlobalRect();
        info = $"{control.Name} 位置 {r.Position} 大小 {r.Size}，屏幕 {GetViewport().GetVisibleRect().Size}";
        return control.IsVisibleInTree() && r.Size.X >= 1f && r.Size.Y >= 1f && ScreenRect.Encloses(r);
    }

    /// <summary>控件铺满整个屏幕（CG、淡入淡出用）。</summary>
    protected bool CoversScreen(Control control, out string info)
    {
        if (control == null) { info = "控件不存在"; return false; }
        Rect2 r = control.GetGlobalRect();
        Rect2 screen = GetViewport().GetVisibleRect();
        info = $"{control.Name} 位置 {r.Position} 大小 {r.Size}，屏幕 {screen.Size}";
        return r.Grow(1f).Encloses(screen) && ScreenRect.Encloses(r);
    }

    /// <summary>扫一遍 root 下所有可见控件：有面积的不能超出屏幕；有可见子控件的不能是 0 大小。返回问题列表。</summary>
    protected List<string> LayoutProblems(Node root)
    {
        var problems = new List<string>();
        void Walk(Node node)
        {
            if (node is CanvasItem item && !item.Visible) return;
            if (node is Control control)
            {
                Rect2 r = control.GetGlobalRect();
                bool hasArea = r.Size.X >= 1f && r.Size.Y >= 1f;
                if (hasArea && !ScreenRect.Encloses(r)) problems.Add($"{root.GetPathTo(control)} 超出屏幕（{r.Position} / {r.Size}）");
                if (!hasArea && control.GetChildren().OfType<Control>().Any(c => c.Visible)) problems.Add($"{root.GetPathTo(control)} 大小为 0 却有可见子控件");
            }
            foreach (Node child in node.GetChildren()) Walk(child);
        }
        Walk(root);
        return problems;
    }

    protected static string Join(List<string> problems) => problems.Count == 0 ? "" : "：" + string.Join(" | ", problems.Take(5));

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
