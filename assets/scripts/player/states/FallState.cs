namespace SonOfMud.Player.States;

/// <summary>下落。土狼时间内按跳仍算地面跳；没在土狼时间内的跳留在缓冲里，落地时由 Idle / Run 消费。</summary>
public partial class FallState : AirState
{
    public override string AnimName => "fall";
    public override string DisplayName => "下落";

    protected override bool TryAirJump()
    {
        if (P.Coyote.Active && P.Buffer.Consume(PlayerActions.Jump, P.Rules.JumpBuffer))
        {
            Machine.TransitionTo<JumpWindupState>();
            return true;
        }
        return base.TryAirJump();
    }
}
