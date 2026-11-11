using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>
/// 攻击判定：打开 HitBox，按自己的计时器收招（打没打中都一样，没打中不会有回调）。
/// 冲锋型沿前摇时锁定的方向冲刺，中途玩家换位置也不转向；前面是悬崖就停在边上。
/// 远程型不开 HitBox，进入时扔出一个投掷物（结算由投掷物自己负责，不回到这个状态）。
/// </summary>
public partial class AttackActiveState : EnemyState
{
    public float Remaining { get; private set; }
    /// <summary>这一次出手的结算结果（没打中就保持 null）。</summary>
    public HitOutcome? LastOutcome { get; private set; }

    public override string DisplayName => "攻击判定";

    public override void Enter(StateMsg msg)
    {
        LastOutcome = null;
        Remaining = E.Data.AttackActive;
        E.Facing = E.LockedDirection;
        E.Stop();
        if (E.Data.Behaviour == EnemyBehaviour.Ranged) E.Throw();
        else E.OpenAttack(E.AttackHitBox, E.Data.AttackSpec, E.Data.Behaviour == EnemyBehaviour.Charge ? "charge" : "strike");
    }

    public override void Exit() => E.CloseAttacks();

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        E.Facing = E.LockedDirection;
        if (E.Data.Behaviour == EnemyBehaviour.Charge) E.Walk(E.LockedDirection, E.Data.ChargeSpeed);
        else E.Stop();
        E.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) Machine.TransitionTo<AttackRecoverState>();
    }

    public override void OnHitLanded(HurtBox target, Hit hit, HitOutcome outcome) => LastOutcome = outcome;
}
