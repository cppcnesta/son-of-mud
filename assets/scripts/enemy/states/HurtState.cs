using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>受击硬直：站定 HurtStun 秒（打断前摇和出手），之后回自由状态。</summary>
public partial class HurtState : EnemyState
{
    public float Remaining { get; private set; }

    public override string DisplayName => "受击";

    public override void Enter(StateMsg msg)
    {
        E.Stop();
        Remaining = E.Data.HurtStun;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        E.Stop();
        E.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) E.ToFreeState();
    }
}
