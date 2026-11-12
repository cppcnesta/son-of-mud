using Godot;
using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>逃跑：背对玩家跑 FleeDuration 秒（遇到悬崖或墙就停在原地），之后回自由状态。</summary>
public partial class FleeState : EnemyState
{
    public float Remaining { get; private set; }
    public float Direction { get; private set; }

    public override string DisplayName => "逃跑";
    public override string AnimName => "flee";

    public override void Enter(StateMsg msg)
    {
        Remaining = E.Data.FleeDuration;
        float dx = E.DxToTarget;
        Direction = Mathf.Abs(dx) > 0.5f ? -Mathf.Sign(dx) : -E.Facing;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        E.Walk(Direction, E.Data.FleeSpeed);
        E.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) E.ToFreeState();
    }
}
