namespace SonOfMud.Player.States;

/// <summary>Rise / Fall 共用：空中移动、重力、攀墙、空中攻击 / 下砸 / 格挡 / 闪避 / 扔泥肝、落地。</summary>
public abstract partial class AirState : PlayerState
{
    public override bool CanSwitchForm => true;
    public override bool AllowsInteract => true;

    public override void PhysicsUpdate(double delta)
    {
        if (TryAirJump()) return;
        if (P.CanStartWallClimb())
        {
            Machine.TransitionTo<WallClimbState>();
            return;
        }
        if (P.TryStartAttack() || P.TryStartBlock() || P.TryStartDodge() || ThrowState.TryEnter(Machine)) return;
        P.Walk(P.Axis);
        P.ApplyAirGravity((float)delta);
    }

    /// <summary>
    /// 空中能起跳的情况：土狼时间内（Fall）或贴着能用的墙（CanUseWall：有攀墙能力、墙面没有不可攀的）蹬墙跳。
    /// 墙不能用时不消费跳跃缓冲，落地后照常当地面跳。
    /// </summary>
    protected virtual bool TryAirJump()
    {
        if (!P.CanUseWall()) return false;
        if (!P.Buffer.Consume(PlayerActions.Jump, P.Rules.JumpBuffer)) return false;
        Machine.TransitionTo<JumpWindupState>(new Core.StateMsg().Set(JumpWindupState.WallKey, true));
        return true;
    }

    public override void AfterMove(double delta)
    {
        if (P.IsOnFloor())
        {
            P.JumpProfile = null;
            P.ToFreeState();
            return;
        }
        if (P.IsOnCeiling() && P.Velocity.Y < 0f) P.Velocity = new Godot.Vector2(P.Velocity.X, 0f);
        if (this is RiseState && P.Velocity.Y >= 0f) Machine.TransitionTo<FallState>();
    }
}
