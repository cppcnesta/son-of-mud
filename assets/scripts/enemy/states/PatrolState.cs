using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>巡逻：在出生点左右 PatrolDistance 之内来回走（遇到悬崖或墙就掉头），速度是移速的一半。看见玩家 → 追。</summary>
public partial class PatrolState : EnemyState
{
    public override string DisplayName => "巡逻";
    public override string AnimName => "patrol";

    public override void PhysicsUpdate(double delta)
    {
        E.ApplyGravity((float)delta);
        if (E.Data.Behaviour != EnemyBehaviour.Passive && E.CanSeeTarget())
        {
            Machine.TransitionTo<ChaseState>();
            return;
        }
        float offset = E.GlobalPosition.X - E.Home.X;
        float dir = E.Facing;
        if ((dir > 0f && offset >= E.Data.PatrolDistance) || (dir < 0f && offset <= -E.Data.PatrolDistance) || E.LedgeAhead(dir) || E.IsOnWall())
            dir = -dir;
        E.Walk(dir, E.Data.MoveSpeed * 0.5f);
    }
}
