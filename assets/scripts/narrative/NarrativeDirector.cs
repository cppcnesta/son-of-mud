using System.Collections.Generic;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Commands;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Parsing;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Narrative.Views;
using SonOfMud.Narrative.World;

namespace SonOfMud.Narrative;

/// <summary>
/// 剧情层的根节点（narrative_layer.tscn，main_game.tscn 里的一个子节点）。只通过 Core 的 Events 和其他系统打交道：
/// - 收到 StoryRequested(剧本, 标签) → 标签下第一条是 [字幕] 的交给字幕执行器（不暂停、不锁输入），否则交给对话框执行器；
/// - 对话框剧情开始时 PauseService.Request(narrative, true)，结束后等推进键 / 交互键松开再释放
///   （按 E 结束对话的那一下不会被玩家拿去再触发一次交互）；开始和结束各发一次 NarrativeStarted / NarrativeFinished；
/// - 对话框剧情开始时字幕立刻收起；对话框剧情进行中又来了对话框请求就排队，来了字幕请求就丢掉；
/// - 一段剧情正常播完，写变量 seen.&lt;剧本&gt;.&lt;标签&gt; = true（StoryTrigger 的 Once 用它）；
/// - 收到 RoomEntered(开场房间) 且变量 seen.opening 还没设过 → 播开场；
/// - 推进键（dialog_advance：E、空格、鼠标左键）每帧读一次：泥板界面开着 → 关掉；选项开着 → 上下移动 / 确认；否则交给执行器。
///   打开对话的那一下按键会被吞掉（必须松开再按才算推进）。
/// 本节点和执行器的 ProcessMode 都是 Always，游戏暂停时照常运行。
/// </summary>
[GlobalClass]
public partial class NarrativeDirector : CanvasLayer
{
    public static readonly StringName AdvanceAction = "dialog_advance";
    public static readonly StringName InteractAction = "interact";
    private static readonly StringName AcceptAction = "ui_accept";
    private static readonly StringName UpAction = "move_up";
    private static readonly StringName DownAction = "move_down";
    private static readonly StringName UiUpAction = "ui_up";
    private static readonly StringName UiDownAction = "ui_down";

    /// <summary>
    /// 进入开场房间时自动播开场。只给测试用：不测剧情的流程测试把它关掉，免得开场暂停游戏、吃掉按键。
    /// </summary>
    public static bool AutoOpeningEnabled { get; set; } = true;

    [Export] public NarrativeSettings Settings { get; set; }

    public VNRunner Main { get; private set; }
    public VNRunner Bark { get; private set; }
    public VNContext MainContext { get; private set; }
    public VNContext BarkContext { get; private set; }
    public DialogBox Dialog { get; private set; }
    public SubtitleBar Subtitle { get; private set; }
    public ChoicePanel Choice { get; private set; }
    public TabletView Tablet { get; private set; }
    public CgLayer Cg { get; private set; }
    public FadeLayer Fade { get; private set; }
    public NarrativeAudio Audio { get; private set; }
    public OrphLibrary Library => OrphLibrary.Shared;

    /// <summary>对话框剧情正在进行（从开始到播完）。</summary>
    public bool MainActive { get; private set; }
    public string MainScript { get; private set; } = "";
    public string MainLabel { get; private set; } = "";
    public string BarkScript { get; private set; } = "";
    public string BarkLabel { get; private set; } = "";
    /// <summary>本节点现在持有 narrative 暂停来源。</summary>
    public bool HoldsPause { get; private set; }
    /// <summary>对话框剧情播完了，正在等按键松开再释放暂停。</summary>
    public bool ReleasePending { get; private set; }
    public int PendingCount => _pending.Count;

    private readonly Queue<(string script, string label)> _pending = new();
    private bool _advanceHeld;
    private bool _upHeld;
    private bool _downHeld;
    private bool _subscribed;

    public override void _EnterTree()
    {
        if (Events.Instance == null || _subscribed) return;
        Events.Instance.StoryRequested += OnStoryRequested;
        Events.Instance.RoomEntered += OnRoomEntered;
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (_subscribed && Events.Instance != null)
        {
            Events.Instance.StoryRequested -= OnStoryRequested;
            Events.Instance.RoomEntered -= OnRoomEntered;
        }
        _subscribed = false;
        Main?.Stop();
        Bark?.Stop();
        _pending.Clear();
        MainActive = false;
        ReleasePause();
    }

    public override void _Ready()
    {
        Settings ??= ResourceLoader.Exists(NarrativeSettings.DefaultPath) ? GD.Load<NarrativeSettings>(NarrativeSettings.DefaultPath) : new NarrativeSettings();
        ProcessMode = ProcessModeEnum.Always;
        Layer = Settings.CanvasLayer;

        Cg = Ensure<CgLayer>("Cg");
        Dialog = Ensure<DialogBox>("Dialog");
        Dialog.Settings = Settings;
        Subtitle = Ensure<SubtitleBar>("Subtitle");
        Subtitle.Settings = Settings;
        Choice = Ensure<ChoicePanel>("Choice");
        Tablet = Ensure<TabletView>("Tablet");
        Fade = Ensure<FadeLayer>("Fade");
        Audio = Ensure<NarrativeAudio>("Audio");
        Audio.Settings = Settings;
        Main = Ensure<VNRunner>("MainRunner");
        Bark = Ensure<VNRunner>("BarkRunner");

        MainContext = new VNContext
        {
            Mode = RunnerMode.Dialog, Lines = Dialog, Choice = Choice, Tablets = Tablet, Cg = Cg, Fade = Fade, Audio = Audio,
            Settings = Settings, Characters = Settings.Characters,
        };
        BarkContext = new VNContext
        {
            Mode = RunnerMode.Subtitle, Lines = Subtitle, Audio = Audio, Settings = Settings, Characters = Settings.Characters,
        };
        foreach (VNRunner runner in new[] { Main, Bark })
        {
            runner.MaxStepsPerFrame = Settings.MaxStepsPerFrame;
            runner.MaxStepsWithoutWaiting = Settings.MaxStepsWithoutWaiting;
        }
        Main.Attach(MainContext);
        Bark.Attach(BarkContext);
        Main.Finished += OnMainFinished;
        Main.Failed += OnMainFailed;
        Bark.Finished += OnBarkFinished;
        Bark.Failed += OnBarkFailed;

        Library.EnsureLoaded(Settings.StoryDirectory);
    }

    private T Ensure<T>(string name) where T : Node, new()
    {
        T node = GetNodeOrNull<T>(name);
        if (node != null) return node;
        node = new T { Name = name };
        AddChild(node);
        return node;
    }

    // =================================================================
    // 请求
    // =================================================================

    private void OnStoryRequested(string scriptId, string label) => Request(scriptId, label);

    /// <summary>播放一段剧情。返回 false 表示剧本或标签不存在、或者字幕请求被丢掉。</summary>
    public bool Request(string scriptId, string label)
    {
        if (!Library.TryGet(scriptId, out OrphScript script))
        {
            NarrativeLog.Error($"[剧情] 找不到剧本「{scriptId}」（标签「{label}」）");
            return false;
        }
        int at = string.IsNullOrEmpty(label) ? 0 : script.FindLabel(label);
        if (at < 0)
        {
            NarrativeLog.Error($"[剧情] 剧本 {scriptId} 里没有标签「{label}」");
            return false;
        }
        if (IsSubtitle(script, at))
        {
            if (MainActive) return false;
            if (Bark.IsPlaying && BarkScript == scriptId && BarkLabel == label) return true;
            BarkScript = scriptId;
            BarkLabel = label ?? "";
            Bark.Play(script, label);
            return true;
        }
        if (MainActive)
        {
            if (!(MainScript == scriptId && MainLabel == label) && !_pending.Contains((scriptId, label))) _pending.Enqueue((scriptId, label));
            return true;
        }
        StartMain(script, label ?? "");
        return true;
    }

    private static bool IsSubtitle(OrphScript script, int index) =>
        index < script.Entries.Count && script.Entries[index].Command == SubtitleModeCommand.CommandName;

    private void StartMain(OrphScript script, string label)
    {
        StopBark();
        MainActive = true;
        ReleasePending = false;
        MainScript = script.Id;
        MainLabel = label;
        if (!HoldsPause)
        {
            HoldsPause = true;
            PauseService.Request(PauseService.Narrative, true);
        }
        // 吞掉打开对话的那一下按键：从现在的按键状态开始算，必须松开再按才算推进。
        _advanceHeld = AdvancePressed();
        _upHeld = UpPressed();
        _downHeld = DownPressed();
        Events.Instance?.EmitSignal(Events.SignalName.NarrativeStarted, script.Id, label);
        Main.Play(script, label);
    }

    private void StopBark()
    {
        if (Bark.IsPlaying) Bark.Stop();
        Subtitle.Hide();
    }

    // =================================================================
    // 结束
    // =================================================================

    private void OnMainFinished() => EndMain(true);

    private void OnMainFailed(string message) => EndMain(false);

    private void EndMain(bool completed)
    {
        if (!MainActive) return;
        string script = MainScript, label = MainLabel;
        bool restart = MainContext.RestartRequested;
        MainContext.RestartRequested = false;
        if (completed) GameSession.Progress.SetVar(StoryTrigger.SeenKeyFor(script, label), true);
        Dialog.Hide();
        Choice.Close();
        Tablet.Close();
        Cg.Hide(0.3f);
        Fade.Cancel();
        MainActive = false;
        Events.Instance?.EmitSignal(Events.SignalName.NarrativeFinished, script, label);

        if (restart)
        {
            // 结局 B：不再播排队的剧情，释放暂停后请求重建整个游戏实例。
            _pending.Clear();
            ReleasePause();
            Events.Instance?.EmitSignal(Events.SignalName.RestartRunRequested);
            return;
        }
        while (_pending.Count > 0)
        {
            var (nextScript, nextLabel) = _pending.Dequeue();
            if (Library.TryGet(nextScript, out OrphScript next) && (string.IsNullOrEmpty(nextLabel) || next.FindLabel(nextLabel) >= 0))
            {
                StartMain(next, nextLabel ?? "");
                return;
            }
        }
        ReleasePending = true;
        TryReleaseAfterKeys();
    }

    private void OnBarkFinished()
    {
        if (BarkScript != "") GameSession.Progress.SetVar(StoryTrigger.SeenKeyFor(BarkScript, BarkLabel), true);
        Subtitle.Hide();
    }

    private void OnBarkFailed(string message) => Subtitle.Hide();

    private void TryReleaseAfterKeys()
    {
        if (!ReleasePending || MainActive) return;
        if (Input.IsActionPressed(AdvanceAction) || Input.IsActionPressed(InteractAction) || Input.IsActionPressed(AcceptAction)) return;
        ReleasePending = false;
        ReleasePause();
    }

    private void ReleasePause()
    {
        ReleasePending = false;
        if (!HoldsPause) return;
        HoldsPause = false;
        PauseService.Request(PauseService.Narrative, false);
    }

    // =================================================================
    // 开场
    // =================================================================

    private void OnRoomEntered(string roomId)
    {
        if (!AutoOpeningEnabled || Settings == null || roomId != Settings.OpeningRoom) return;
        if (GameSession.Progress.HasVar(Settings.OpeningSeenVar)) return;
        if (MainActive && MainScript == Settings.OpeningScript && MainLabel == Settings.OpeningLabel) return;
        Callable.From(() =>
        {
            if (IsInsideTree() && !GameSession.Progress.HasVar(Settings.OpeningSeenVar)) Request(Settings.OpeningScript, Settings.OpeningLabel);
        }).CallDeferred();
    }

    // =================================================================
    // 输入
    // =================================================================

    private static bool AdvancePressed() => Input.IsActionPressed(AdvanceAction) || Input.IsActionPressed(AcceptAction);
    private static bool UpPressed() => Input.IsActionPressed(UpAction) || Input.IsActionPressed(UiUpAction);
    private static bool DownPressed() => Input.IsActionPressed(DownAction) || Input.IsActionPressed(UiDownAction);

    private static bool Edge(ref bool held, bool now)
    {
        bool edge = now && !held;
        held = now;
        return edge;
    }

    public override void _Process(double delta)
    {
        bool advance = Edge(ref _advanceHeld, AdvancePressed());
        bool up = Edge(ref _upHeld, UpPressed());
        bool down = Edge(ref _downHeld, DownPressed());
        if (ReleasePending) TryReleaseAfterKeys();
        if (!MainActive) return;

        if (Tablet.IsOpen)
        {
            if (advance) Tablet.Close();
            return;
        }
        if (Choice.IsOpen)
        {
            if (up) Choice.Move(-1);
            if (down) Choice.Move(1);
            if (advance) Choice.ConfirmFocused();
            return;
        }
        if (advance) Main.Advance();
    }
}
