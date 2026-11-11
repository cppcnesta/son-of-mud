using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.Narrative.Commands;
using SonOfMud.Narrative.Parsing;

namespace SonOfMud.Narrative.Runtime;

/// <summary>
/// 执行器（OrpheusVN VNRunner 的 Godot 版）：从剧本的某个标签开始，一条一条往下执行。
///
/// 对外入口：
///   Play(剧本, 标签)               从标签开始播放（变量一直在 Core 的 ProgressStore 里，不清空）
///   PlayUntil(剧本, 标签, 命令)     只播一段：执行完第一条指定命令就停
///   Stop()                         停下来，取消正在进行的一切（不发 Finished）
///   Advance()                      玩家按了推进键（由剧情层调用）
/// 播到 [结束] 或剧本末尾时发 Finished；出错停止时发 Failed。
///
/// 和原版的区别：协程换成 After(秒, 回调) / WhenTrue(条件, 回调)，在 _Process 里轮询，带场次检查；
/// 节点 ProcessMode = Always，游戏暂停时照常运行。原版的 [存档] / 读档 / 摆舞台不搬（剧情中途不存档）。
/// </summary>
[GlobalClass]
public partial class VNRunner : Node
{
    [Signal] public delegate void FinishedEventHandler();
    [Signal] public delegate void FailedEventHandler(string message);

    public VNContext Context { get; private set; }

    /// <summary>测试用：台词不等推进键、[等待] 不等时间、选项用 AutoChoose 选。</summary>
    public bool AutoAdvance { get; set; }
    /// <summary>没有选项面板或 AutoAdvance 时用它选（返回下标）；为空时选第一项。</summary>
    public Func<string[], int> AutoChoose { get; set; }

    public int MaxStepsPerFrame { get; set; } = 200;
    public int MaxStepsWithoutWaiting { get; set; } = 5000;

    public string LastError { get; private set; }
    public bool IsPlaying { get; private set; }
    public OrphScript Script { get; private set; }
    public string StartLabel { get; private set; } = "";

    /// <summary>每执行一条指令前触发（测试、调试用）。</summary>
    public event Action<OrphEntry> EntryExecuting;

    public OrphEntry CurrentEntry =>
        Script != null && _index >= 0 && _index < Script.Entries.Count ? Script.Entries[_index] : null;

    /// <summary>最近一次执行的指令（跳转之后 CurrentEntry 已经指向别处，报错时用它）。</summary>
    public OrphEntry LastExecuted { get; private set; }

    /// <summary>正在等推进键（台词打完了，或 [等点击]）。</summary>
    public bool WaitingForAdvance => _pendingAdvance != null;

    private int _index = -1;
    private int _stopAfterIndex = -1;
    private int _commandSerial;
    private int _activeCommand;
    private bool _stepping;
    private bool _stepRequested;
    private ulong _countedFrame = ulong.MaxValue;
    private int _stepsThisFrame;
    private int _stepsWithoutWaiting;
    private Action _pendingAdvance;

    private sealed class Waiter
    {
        public int Session;
        public double Remaining = -1;      // 小于 0 表示不计时
        public Func<bool> Condition;       // 为空表示只计时
        public Action Callback;
    }

    private readonly List<Waiter> _waiters = new();

    public void Attach(VNContext context)
    {
        Context = context;
        context.Runner = this;
    }

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    // =================================================================
    // 对外的接口
    // =================================================================

    public void Play(OrphScript script, string label = null) => Begin(script, label, null);

    public void PlayUntil(OrphScript script, string label, string stopCommand) => Begin(script, label, stopCommand);

    public void Stop()
    {
        Cancel();
        IsPlaying = false;
    }

    /// <summary>跳到某个标签，下一条执行的就是标签后面的指令。</summary>
    public bool JumpTo(string label)
    {
        int target = Script?.FindLabel(label) ?? -1;
        if (target < 0)
        {
            NarrativeLog.Error($"[剧情] {Script?.Id}：找不到标签「{label}」");
            return false;
        }
        _index = target - 1;  // Step 会先加 1
        return true;
    }

    /// <summary>[结束]：执行完当前这条就算播完（发 Finished）。</summary>
    public void EndPlayback()
    {
        if (Script != null) _index = Script.Entries.Count - 1;
    }

    /// <summary>“说”“等点击”调用：玩家下一次按推进键时执行 next。</summary>
    public void WaitForAdvance(Action next)
    {
        if (AutoAdvance)
        {
            Context.Lines?.CompleteTyping();
            next();
            return;
        }
        _pendingAdvance = next;
        Context.Lines?.StopRush();
    }

    /// <summary>
    /// 往后看下一句台词：返回后面第一条“说”“接着说”或“等点击”；
    /// 中间遇到会打断台词的命令，或者剧本结束，返回 null。
    /// </summary>
    public OrphEntry PeekNextLine()
    {
        if (Script == null) return null;
        int end = Script.Entries.Count;
        if (_stopAfterIndex >= 0) end = Math.Min(end, _stopAfterIndex + 1);
        for (int i = _index + 1; i < end; i++)
        {
            OrphEntry entry = Script.Entries[i];
            if (entry.Command == SayCommand.NewBlock || entry.Command == SayCommand.SameBlock || entry.Command == WaitClickCommand.CommandName)
                return entry;
            if (VNCommands.TryGet(entry.Command, out VNCommand command) && command.EndsDialogueBlock) return null;
        }
        return null;
    }

    /// <summary>紧接着的下一条是不是“和上一句台词一起出现”的命令（比如选项）。</summary>
    public bool NextShowsWithLine()
    {
        if (Script == null || _index + 1 >= Script.Entries.Count) return false;
        return VNCommands.TryGet(Script.Entries[_index + 1].Command, out VNCommand command) && command.ShowsWithPreviousLine;
    }

    /// <summary>过 seconds 秒（不受暂停影响）再调用 callback；重新播放或停止后作废。</summary>
    public void After(double seconds, Action callback) =>
        _waiters.Add(new Waiter { Session = Context.Session, Remaining = Math.Max(0, seconds), Callback = callback });

    /// <summary>条件成立时调用 callback（每帧检查一次）；重新播放或停止后作废。</summary>
    public void WhenTrue(Func<bool> condition, Action callback) =>
        _waiters.Add(new Waiter { Session = Context.Session, Condition = condition, Callback = callback });

    /// <summary>最多等 seconds 秒，条件提前成立就提前调用。</summary>
    public void WaitAtMost(double seconds, Func<bool> condition, Action callback) =>
        _waiters.Add(new Waiter { Session = Context.Session, Remaining = Math.Max(0, seconds), Condition = condition, Callback = callback });

    public void Fail(string message)
    {
        LastError = message;
        NarrativeLog.Error("[剧情] " + message);
        Stop();
        EmitSignal(SignalName.Failed, message);
    }

    // =================================================================
    // 开始和取消
    // =================================================================

    private void Begin(OrphScript script, string label, string stopCommand)
    {
        Cancel();
        LastError = null;
        Script = script;
        StartLabel = label ?? "";
        LastExecuted = null;
        _index = -1;
        _stopAfterIndex = -1;
        IsPlaying = false;

        if (script == null)
        {
            Fail("没有指定剧本");
            return;
        }
        if (script.Errors.Count > 0)
        {
            Fail($"剧本 {script.Id} 有 {script.Errors.Count} 处解析错误，停止播放（第一处：{script.Errors[0]}）");
            return;
        }
        if (!string.IsNullOrEmpty(label) && !JumpTo(label))
        {
            Fail($"剧本 {script.Id} 里没有标签「{label}」");
            return;
        }
        if (!string.IsNullOrEmpty(stopCommand))
        {
            _stopAfterIndex = script.FindNext(stopCommand, _index + 1);
            if (_stopAfterIndex < 0) NarrativeLog.Warn($"[剧情] 标签「{label}」后面没有 [{stopCommand}]，会一直播到剧本结尾");
        }
        IsPlaying = true;
        Step();
    }

    /// <summary>取消正在进行的一切。之后旧命令的 done、旧的计时和条件回调都会被忽略。</summary>
    private void Cancel()
    {
        IsPlaying = false;
        _stepRequested = false;
        _waiters.Clear();
        _pendingAdvance = null;
        _activeCommand = ++_commandSerial;
        _stepsWithoutWaiting = 0;
        Context?.BeginSession();
    }

    // =================================================================
    // 推进键
    // =================================================================

    /// <summary>玩家按了推进键。字没打完 → 这一段直接显示完；正在等 → 继续；两句之间的停顿 → 剩下的直接显示。</summary>
    public void Advance()
    {
        if (!IsPlaying || Context.InputLocked) return;
        ILineSurface lines = Context.Lines;
        if (lines != null && lines.IsTyping)
        {
            lines.Rush();
            return;
        }
        Action next = _pendingAdvance;
        _pendingAdvance = null;
        if (next != null)
        {
            next();
            return;
        }
        lines?.Rush();
    }

    public override void _Process(double delta)
    {
        if (_waiters.Count > 0) TickWaiters(delta);
        if (AutoAdvance && IsPlaying && _pendingAdvance != null && !Context.InputLocked)
        {
            Context.Lines?.CompleteTyping();
            Advance();
        }
    }

    private void TickWaiters(double delta)
    {
        foreach (Waiter waiter in _waiters.ToArray())
        {
            if (!_waiters.Contains(waiter)) continue;  // 前面的回调里重新播放了
            if (Context == null || !Context.IsCurrent(waiter.Session))
            {
                _waiters.Remove(waiter);
                continue;
            }
            bool fire;
            if (waiter.Condition != null && waiter.Condition()) fire = true;
            else if (waiter.Remaining >= 0)
            {
                waiter.Remaining -= delta;
                fire = waiter.Remaining <= 0;
            }
            else fire = false;
            if (!fire) continue;
            _waiters.Remove(waiter);
            waiter.Callback();
        }
    }

    // =================================================================
    // 一条一条执行
    // =================================================================

    private void Step()
    {
        // 很多命令马上调用 done，done 又调用 Step；用循环代替递归，避免调用栈越来越深。
        _stepRequested = true;
        if (_stepping) return;

        bool reachedEnd = false;
        _stepping = true;
        try
        {
            while (_stepRequested && IsPlaying)
            {
                _stepRequested = false;

                // 保护 1：一帧里做的事太多，留到下一帧。
                ulong frame = Engine.GetProcessFrames();
                if (frame != _countedFrame)
                {
                    _countedFrame = frame;
                    _stepsThisFrame = 0;
                }
                if (_stepsThisFrame >= MaxStepsPerFrame)
                {
                    WhenTrue(() => Engine.GetProcessFrames() != frame, Step);
                    return;
                }

                // 保护 2：一直没有遇到需要等待的指令，多半是跳转绕成了圈。
                if (_stepsWithoutWaiting >= MaxStepsWithoutWaiting)
                {
                    OrphEntry at = LastExecuted;
                    Fail($"疑似死循环：{Script?.Id} 连续执行了 {MaxStepsWithoutWaiting} 条指令都没有停下来等待，已停止。" +
                         $"最后执行的是第{(at != null ? at.SourceLine.ToString() : "?")}行附近，检查一下那里的跳转。");
                    return;
                }

                _stepsThisFrame++;
                _stepsWithoutWaiting++;
                _index++;

                bool pastStop = _stopAfterIndex >= 0 && _index > _stopAfterIndex;
                if (pastStop || Script == null || _index >= Script.Entries.Count)
                {
                    reachedEnd = true;
                    break;
                }

                ExecuteEntry(Script.Entries[_index]);

                // 命令没有马上完成，说明它在等（推进键、时间、玩法），剧本在正常推进。
                if (!_stepRequested) _stepsWithoutWaiting = 0;
            }
        }
        finally
        {
            _stepping = false;
        }

        // 放在循环外面发：监听者在 Finished 里再调用 Play 也没问题。
        if (reachedEnd)
        {
            IsPlaying = false;
            EmitSignal(SignalName.Finished);
        }
    }

    private void ExecuteEntry(OrphEntry entry)
    {
        LastExecuted = entry;
        EntryExecuting?.Invoke(entry);
        if (!IsPlaying) return;
        if (!VNCommands.TryGet(entry.Command, out VNCommand command))
        {
            NarrativeLog.Error($"[剧情] {Script?.Id} 第{entry.SourceLine}行：不认识的命令「{entry.Command}」，已跳过");
            _stepRequested = true;
            return;
        }
        string[] args = entry.Args ?? Array.Empty<string>();
        string error = command.Validate(args);
        if (error != null)
        {
            NarrativeLog.Error($"[剧情] {Script?.Id} 第{entry.SourceLine}行：{error}，已跳过");
            _stepRequested = true;
            return;
        }
        if (Context.Mode == RunnerMode.Subtitle && !command.AllowedInSubtitle)
        {
            Fail($"{Script?.Id} 第{entry.SourceLine}行：字幕（不暂停）的剧情里不能用 [{command.Name}]");
            return;
        }
        _activeCommand = ++_commandSerial;
        try
        {
            command.Execute(args, Context, MakeDone(_activeCommand));
        }
        catch (Exception e)
        {
            Fail($"{Script?.Id} 第{entry.SourceLine}行 [{entry.Command}]：{e.Message}");
        }
    }

    /// <summary>每条命令一个只能用一次的 done：调用两次、或者它已经不是当前命令时都会被忽略。</summary>
    private Action MakeDone(int commandId)
    {
        bool used = false;
        return () =>
        {
            if (!IsPlaying || used || commandId != _activeCommand) return;
            used = true;
            Step();
        };
    }
}
