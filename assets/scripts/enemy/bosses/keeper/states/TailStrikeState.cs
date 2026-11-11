using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>
/// 蝎尾出手：先 0.1 秒过渡，然后在锁定的 X 处打开竖带判定（宽 96、高度覆盖整个房间，只跳躲不开）。
/// Hit 带 3 秒眩晕和 CounterOnBlock。结果是 Blocked → 反制僵直；其他情况（包括没打中）按计时 → 收招。
/// </summary>
public partial class TailStrikeState : KeeperState
{
    public float Remaining { get; private set; }
    public bool Striking { get; private set; }
    public HitOutcome? LastOutcome { get; private set; }
    private bool _countered;

    public override string DisplayName => Striking ? "蝎尾判定" : "蝎尾出手";

    public override void Enter(StateMsg msg)
    {
        K.Stop();
        Striking = false;
        _countered = false;
        LastOutcome = null;
        Remaining = D.TailDelay;
    }

    public override void Exit()
    {
        K.TailHitBox?.Deactivate();
        K.TailLocked = false;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        K.Stop();
        K.ApplyGravity(dt);
        if (_countered)
        {
            Machine.TransitionTo<StaggeredState>();
            return;
        }
        Remaining -= dt;
        if (Remaining > 0.000001f) return;
        if (!Striking)
        {
            Striking = true;
            Remaining += D.TailActive;
            K.PlaceTailBand();
            K.OpenAttack(K.TailHitBox, D.TailSpec, "tail");
            if (_countered) Machine.TransitionTo<StaggeredState>();
            return;
        }
        Machine.TransitionTo<TailRecoverState>();
    }

    public override void OnHitLanded(HurtBox target, Hit hit, HitOutcome outcome)
    {
        LastOutcome = outcome;
        if (outcome == HitOutcome.Blocked && hit.CounterOnBlock) _countered = true;
    }
}
