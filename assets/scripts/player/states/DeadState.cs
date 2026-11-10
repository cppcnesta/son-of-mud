using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>死亡：血量归零（PlayerDied 由 Stats.Depleted 发出）。不再受击，只等 CheckpointRespawned。</summary>
public partial class DeadState : PlayerState
{
    public override string AnimName => "death";
    public override string DisplayName => "死亡";
    public override bool AcceptsHits => false;
    public override bool Lockable => false;

    public override void Enter(StateMsg msg) => P.CloseHitBoxes();

    public override void PhysicsUpdate(double delta)
    {
        P.StopHorizontal();
        P.ApplyGravity((float)delta);
    }
}
