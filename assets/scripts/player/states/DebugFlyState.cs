using Godot;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>调试飞行（Events.DebugFlyToggled，Tools 里按 F 发出）：关掉身体碰撞，四方向自由移动，不受击。</summary>
public partial class DebugFlyState : PlayerState
{
    public override string AnimName => "fall";
    public override string DisplayName => "调试飞行";
    public override bool AcceptsHits => false;
    public override bool Lockable => false;

    public override void Enter(StateMsg msg)
    {
        P.CloseHitBoxes();
        P.SetBodyCollision(false);
        P.Velocity = Vector2.Zero;
    }

    public override void Exit() => P.SetBodyCollision(true);

    public override void PhysicsUpdate(double delta)
    {
        var dir = new Vector2(P.Axis, InputLock.IsLocked ? 0f : Input.GetAxis(PlayerActions.Up, PlayerActions.Down));
        if (dir.X != 0f) P.Facing = Mathf.Sign(dir.X);
        P.Velocity = dir * P.Rules.DebugFlySpeed;
    }
}
