using System;
using SonOfMud.Narrative.Runtime;

/// <summary>测试用玩法段（对应原版测试里的 Segment）：记下回调，由测试决定什么时候、传回什么结果。</summary>
public sealed class TestSegment : IGameplaySegment
{
    public string Type { get; set; } = "__test_segment";
    public Action<string> Callback;
    public int Cancellations;
    /// <summary>为 true 时 Begin 里立刻传回 ImmediateResult。</summary>
    public bool Immediate;
    public string ImmediateResult = "pass";

    public void Begin(string arg, Action<string> done)
    {
        Callback = done;
        if (Immediate) done(ImmediateResult);
    }

    public void Cancel() => Cancellations++;
}
