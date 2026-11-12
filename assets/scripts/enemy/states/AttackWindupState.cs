using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>攻击前摇：站定，进入时朝向玩家并锁定方向（之后不再转向）。计时结束 → 出手。</summary>
public partial class AttackWindupState : EnemyState
{
    public float Remaining { get; private set; }

    public override string DisplayName => "攻击前摇";
    public override string AnimName => "attack_windup";
    public override float AnimDuration => E.Data.AttackWindup;

    public override void Enter(StateMsg msg)
    {
        E.Stop();
        E.FaceTarget();
        E.LockedDirection = E.Facing;
        Remaining = E.Data.AttackWindup;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        E.Stop();
        E.Facing = E.LockedDirection;
        E.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) Machine.TransitionTo<AttackActiveState>();
    }
}
