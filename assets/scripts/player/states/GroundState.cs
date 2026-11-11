using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>Idle / Run 共用：落地时消费缓冲的跳；走下平台开始土狼时间；攻击 / 格挡 / 闪避 / 扔泥肝的入口。</summary>
public abstract partial class GroundState : PlayerState
{
    public override bool CanSwitchForm => true;
    public override bool AllowsInteract => true;

    public override void Enter(StateMsg msg)
    {
        P.JumpProfile = null;
        P.Coyote.Clear();
        // 落地前 JumpBuffer 秒内按过跳：落地即起跳。
        if (P.IsOnFloor() && P.Buffer.Consume(PlayerActions.Jump, P.Rules.JumpBuffer))
            Machine.TransitionTo<JumpWindupState>();
    }

    public override void PhysicsUpdate(double delta)
    {
        if (P.Buffer.Consume(PlayerActions.Jump, P.Rules.JumpBuffer))
        {
            Machine.TransitionTo<JumpWindupState>();
            return;
        }
        if (P.TryStartAttack() || P.TryStartBlock() || P.TryStartDodge() || ThrowState.TryEnter(Machine)) return;

        float axis = P.Axis;
        P.Walk(axis);
        P.StickToFloor((float)delta);
        if (axis != 0f && this is IdleState) Machine.TransitionTo<RunState>();
        else if (axis == 0f && this is RunState) Machine.TransitionTo<IdleState>();
    }

    public override void AfterMove(double delta)
    {
        if (P.IsOnFloor()) return;
        P.Coyote.Start(P.Rules.CoyoteTime);
        Machine.TransitionTo<FallState>();
    }
}
