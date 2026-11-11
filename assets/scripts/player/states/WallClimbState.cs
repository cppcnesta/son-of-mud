using Godot;

namespace SonOfMud.Player.States;

/// <summary>攀墙：有 wall_climb、贴墙、按住朝墙方向和上。离墙或松键 → Fall（保留速度，爬到顶时能翻上去）；跳 → 蹬墙跳。</summary>
public partial class WallClimbState : PlayerState
{
    public override string AnimName => "wall_climb";
    public override string DisplayName => "攀墙";

    public override void PhysicsUpdate(double delta)
    {
        if (P.Buffer.Consume(PlayerActions.Jump, P.Rules.JumpBuffer))
        {
            Machine.TransitionTo<JumpWindupState>(new Core.StateMsg().Set(JumpWindupState.WallKey, true));
            return;
        }
        float axis = P.Axis;
        if (!P.Held(PlayerActions.Up) || axis == 0f || !P.HasAbility(PlayerAbilities.WallClimb))
        {
            Machine.TransitionTo<FallState>();
            return;
        }
        P.Facing = Mathf.Sign(axis);
        P.Velocity = new Vector2(axis * P.Forms.Current.MoveSpeed, -P.Forms.Current.WallClimbSpeed);
    }

    public override void AfterMove(double delta)
    {
        if (P.IsOnFloor() && P.Velocity.Y >= 0f) P.ToFreeState();
        else if (!P.IsOnWall()) Machine.TransitionTo<FallState>();
    }
}
