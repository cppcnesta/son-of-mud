using System;

public sealed class MudActionState
{
    public enum Kind { Free, JumpWindup, AttackWindup, AttackActive, AttackRecovery, Block, Dodge, Hurt, Slam, Dead }
    private static readonly string[] Names = { "移动/待机", "起跳前摇", "攻击前摇", "攻击判定", "攻击后摇", "格挡", "闪避", "受伤", "重压下砸", "死亡" };
    public Kind State { get; private set; } = Kind.Free;
    public float Remaining { get; private set; }
    public void Enter(Kind next, float duration = 0f) { State = next; Remaining = duration; }
    public void Tick(float delta)
    {
        Remaining = Math.Max(0f, Remaining - delta);
        if (Remaining < 0.000001f) Remaining = 0f;
    }
    public string Label() => Names[(int)State];
}
