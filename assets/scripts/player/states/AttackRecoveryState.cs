using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 攻击后摇。泥形态（Soft）：1.5 秒可移动收势，起始 20% 移速沿曲线恢复；全程可接已冷却的闪避
/// （闪避覆盖旧收势，只留闪避自己的减速）；最后 0.5 秒可格挡（需格挡冷却就绪），更早按格挡不消耗冷却。
/// 水形态下砸落地用的是站定后摇（不能移动、不能取消）。时间到 → 自由状态。
/// </summary>
public partial class AttackRecoveryState : PlayerState
{
    private FormConfig _profile;
    private bool _soft;
    private float _duration;

    public float Remaining { get; private set; }
    public bool Soft => _soft;

    public override string AnimName => "attack_recover";
    public override string DisplayName => "攻击后摇";
    public override float AnimDuration => _duration;

    public override void Enter(StateMsg msg)
    {
        _profile = msg?.Payload as FormConfig ?? P.Forms.Current;
        _soft = _profile.SoftAttackRecovery;
        _duration = _soft ? _profile.AttackRecovery : P.Rules.HardSlamRecovery;
        Remaining = _duration;
        P.Timers.AttackInterval = _duration;
        if (_soft)
            P.Timers.Recovery.Start(MovementRecovery.Source.Attack, _duration, _profile.PenaltyInitialMultiplier, _profile.RecoveryCurve);
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        if (_soft && _profile.DodgeCancelRecovery && P.Timers.DodgeReady && P.Buffer.Has(PlayerActions.Dodge, P.Rules.ActionBuffer))
        {
            P.Timers.ResetAttackInterval();
            if (P.TryStartDodge()) return;
        }
        if (_soft && Remaining <= _profile.BlockCancelWindow + 0.000001f && P.TryStartBlock()) return;

        if (_soft) P.Walk(P.Axis);
        else P.StopHorizontal();
        P.ApplyGravity(dt);

        Remaining -= dt;
        if (Remaining <= 0.000001f)
        {
            Remaining = 0f;
            P.ToFreeState();
        }
    }
}
