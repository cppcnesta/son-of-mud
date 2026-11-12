using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>蝎尾收招（没被挡住时）：0.5 秒（Q6），结束时蝎尾冷却 EndCast（从 0/20 开始充）→ 逼近。</summary>
public partial class TailRecoverState : KeeperState
{
    public float Remaining { get; private set; }

    public override string DisplayName => "蝎尾收招";
    public override string AnimName => "tail_recover";
    public override float AnimDuration => D.TailRecover;

    public override void Enter(StateMsg msg)
    {
        K.Stop();
        Remaining = D.TailRecover;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        K.Stop();
        K.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining > 0.000001f) return;
        K.Tail.EndCast();
        Machine.TransitionTo<ApproachState>();
    }
}
