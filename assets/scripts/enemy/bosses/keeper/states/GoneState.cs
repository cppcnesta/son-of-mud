using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>已击败（终态）：蝎形态的身体隐藏、禁用。以后再进这个房间（boss_keeper 已是 true）直接是这个状态。</summary>
public partial class GoneState : KeeperState
{
    public override string DisplayName => "已击败";

    public override void Enter(StateMsg msg)
    {
        K.ShutDownCombat();
        K.FlashRemaining = 0f;
        K.Visible = false;
        K.CollisionLayer = 0;
        K.Velocity = Godot.Vector2.Zero;
    }

    public override void PhysicsUpdate(double delta) => K.Stop();
}
