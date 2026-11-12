using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>待机。攻击型看见玩家就去追；有巡逻距离的待一会儿就开始巡逻。被动型一直待着。</summary>
public partial class IdleState : EnemyState
{
    public const float PatrolDelay = 1f;
    private float _idle;

    public override string DisplayName => "待机";
    public override string AnimName => "idle";

    public override void Enter(StateMsg msg)
    {
        _idle = 0f;
        E.Stop();
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        E.Stop();
        E.ApplyGravity(dt);
        if (E.Data.Behaviour == EnemyBehaviour.Passive) return;
        if (E.CanSeeTarget())
        {
            Machine.TransitionTo<ChaseState>();
            return;
        }
        _idle += dt;
        if (E.Data.PatrolDistance > 0f && _idle >= PatrolDelay) Machine.TransitionTo<PatrolState>();
    }
}
