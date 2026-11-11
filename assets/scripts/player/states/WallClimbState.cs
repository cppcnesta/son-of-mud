using Godot;

namespace SonOfMud.Player.States;

/// <summary>
/// 攀墙：有 wall_climb、贴着能用的墙（CanUseWall）、按住朝墙方向和上。离墙或松键 → Fall（保留速度，爬到顶时能翻上去）；
/// 跳 → 蹬墙跳（同样要求墙能用）。每一帧都检查墙面：一旦接触到不可攀的墙面（例如从可攀的一段爬进不可攀的一段），
/// 去掉向上的速度并转为下落，所以不会越过交界线。
/// </summary>
public partial class WallClimbState : PlayerState
{
    public override string AnimName => "wall_climb";
    public override string DisplayName => "攀墙";

    /// <summary>
    /// 进入的这一帧还没轮到本状态设速度（AirState 切过来就返回了），而上一帧撞墙后横向速度已被清零，
    /// 这一帧的 MoveAndSlide 不再贴墙，AfterMove 就会马上判“离墙”转 Fall。对矩形碰撞体（StaticBody2D 门体等）
    /// 这样会一直挂不上墙，所以进入时先把横向速度设成朝墙走，保证这一帧仍然贴着墙。竖直速度不动。
    /// </summary>
    public override void Enter(Core.StateMsg msg)
    {
        P.Velocity = new Vector2(P.Axis * P.Forms.Current.MoveSpeed, P.Velocity.Y);
    }

    public override void PhysicsUpdate(double delta)
    {
        if (!P.CanUseWall())
        {
            DropOff();
            return;
        }
        if (P.Buffer.Consume(PlayerActions.Jump, P.Rules.JumpBuffer))
        {
            Machine.TransitionTo<JumpWindupState>(new Core.StateMsg().Set(JumpWindupState.WallKey, true));
            return;
        }
        float axis = P.Axis;
        if (!P.Held(PlayerActions.Up) || axis == 0f)
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
        else if (P.TouchesNoClimbWall()) DropOff();
    }

    /// <summary>墙不能用了：去掉向上的速度再转为下落（离开墙顶翻上去走的是 !IsOnWall 那条路，保留速度）。</summary>
    private void DropOff()
    {
        if (P.Velocity.Y < 0f) P.Velocity = new Vector2(P.Velocity.X, 0f);
        Machine.TransitionTo<FallState>();
    }
}
