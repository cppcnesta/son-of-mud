using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>死亡（终态）：判定已关、不再受击，原地留下红泥（占位）。</summary>
public partial class DeadState : EnemyState
{
    public override string DisplayName => "死亡";
    public override string AnimName => "dead";

    public override void Enter(StateMsg msg)
    {
        E.Stop();
        E.CloseAttacks();
    }

    public override void PhysicsUpdate(double delta)
    {
        E.Stop();
        E.ApplyGravity((float)delta);
    }
}
