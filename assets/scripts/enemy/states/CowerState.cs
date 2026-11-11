using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>举起双手（策划案：被打时，有的逃跑，有的举起双手）。原地不动，再被打时重新反应。</summary>
public partial class CowerState : EnemyState
{
    public override string DisplayName => "举手";

    public override void Enter(StateMsg msg) => E.Stop();

    public override void PhysicsUpdate(double delta)
    {
        E.Stop();
        E.ApplyGravity((float)delta);
    }
}
