using Godot;

namespace SonOfMud.Enemy.States;

/// <summary>追击：朝玩家走，水平距离进入攻击距离就开始前摇；玩家跑出视野 1.5 倍以外就回待机。</summary>
public partial class ChaseState : EnemyState
{
    public const float LeashFactor = 1.5f;

    public override string DisplayName => "追击";

    public override void PhysicsUpdate(double delta)
    {
        E.ApplyGravity((float)delta);
        if (!E.CanSeeTarget(E.Data.VisionRange * LeashFactor))
        {
            Machine.TransitionTo<IdleState>();
            return;
        }
        float dx = E.DxToTarget;
        if (Mathf.Abs(dx) <= E.Data.AttackRange)
        {
            E.FaceTarget();
            Machine.TransitionTo<AttackWindupState>();
            return;
        }
        E.Walk(Mathf.Sign(dx), E.Data.MoveSpeed);
    }
}
