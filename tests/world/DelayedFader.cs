using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.Core;

/// <summary>
/// 测试用的假黑幕：收到 FadeRequested 后故意等 DelayFrames 帧才发同一 id 的 FadeCompleted。
/// 每帧调用 OnWaitingFrame（测试用它检查“完成之前旧房间还在”）。
/// </summary>
public partial class DelayedFader : Node
{
    public int DelayFrames { get; set; } = 10;
    public Action<int, bool, int> OnWaitingFrame { get; set; }
    public int Completed { get; private set; }

    private readonly List<(int Id, bool FadeOut, int Left)> _pending = new();

    public override void _EnterTree()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (Events.Instance != null) Events.Instance.FadeRequested += OnFadeRequested;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.FadeRequested -= OnFadeRequested;
    }

    private void OnFadeRequested(int requestId, bool fadeOut) => _pending.Add((requestId, fadeOut, DelayFrames));

    public override void _Process(double delta)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            (int id, bool fadeOut, int left) = _pending[i];
            left--;
            OnWaitingFrame?.Invoke(id, fadeOut, left);
            if (left > 0)
            {
                _pending[i] = (id, fadeOut, left);
                continue;
            }
            _pending.RemoveAt(i--);
            Completed++;
            Events.Instance?.EmitSignal(Events.SignalName.FadeCompleted, id, fadeOut);
        }
    }
}
