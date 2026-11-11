using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>攻击收招：站定，计时结束回自由状态（看得见玩家就继续追）。</summary>
public partial class AttackRecoverState : EnemyState
{
    public float Remaining { get; private set; }

    public override string DisplayName => "攻击收招";

    public override void Enter(StateMsg msg)
    {
        E.Stop();
        Remaining = E.Data.AttackRecover;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        E.Stop();
        E.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) E.ToFreeState();
    }
}
