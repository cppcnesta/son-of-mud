using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>
/// 横扫判定：打开横扫 HitBox，按计时收招（挥空也一样）。
/// 结果是 Blocked、Damaged 或 Killed（被格挡也算命中）→ 蝎尾剩余冷却减 5 秒（只在蝎尾回充中生效，Q5）。
/// </summary>
public partial class SweepStrikeState : KeeperState
{
    public float Remaining { get; private set; }
    public HitOutcome? LastOutcome { get; private set; }

    public override string DisplayName => "横扫";
    public override string AnimName => "sweep_strike";
    public override float AnimDuration => D.SweepActive;

    public override void Enter(StateMsg msg)
    {
        LastOutcome = null;
        Remaining = D.SweepActive;
        K.Facing = K.LockedDirection;
        K.Stop();
        K.OpenAttack(K.SweepHitBox, D.SweepSpec, "sweep");
    }

    public override void Exit() => K.SweepHitBox?.Deactivate();

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        K.Stop();
        K.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) Machine.TransitionTo<SweepRecoverState>();
    }

    public override void OnHitLanded(HurtBox target, Hit hit, HitOutcome outcome)
    {
        LastOutcome = outcome;
        if (outcome is HitOutcome.Blocked or HitOutcome.Damaged or HitOutcome.Killed)
            if (K.Tail.Reduce(D.SweepReducesTail)) K.TailReductions++;
    }
}
