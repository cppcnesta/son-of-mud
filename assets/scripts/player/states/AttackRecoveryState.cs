using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 攻击后摇。泥形态（Soft）：1.5 秒可移动收势，起始 20% 移速沿曲线恢复；全程可接已冷却的闪避
/// （闪避覆盖旧收势，只留闪避自己的减速）；最后 0.5 秒可格挡（需格挡冷却就绪），更早按格挡不消耗冷却。
/// - 连招：第 1 下之后的收势，前 ComboWindow 秒（0.35）里按攻击接第 2 下（地面）。
/// - 收势开始 ReattackAfter 秒（1.2）后攻击间隔结束，按攻击从第 1 下重新出刀（收势被取消）。
///   按键走攻击缓冲：只有能出刀前 AttackBuffer 秒内按的才算，更早按的作废。
/// 水形态下砸落地用的是站定后摇（不能移动、不能取消）。下砸落地进来时（FromSlam），动画先播 slam_land。时间到 → 自由状态。
/// </summary>
public partial class AttackRecoveryState : PlayerState
{
    /// <summary>刚结束的是连招第几下（1 / 2）；0 表示不是普通攻击（下劈、下砸），不能接第 2 下。</summary>
    public static readonly Godot.StringName EndedStepKey = "ended_step";
    /// <summary>从下砸落地进来。</summary>
    public static readonly Godot.StringName FromSlamKey = "from_slam";

    private FormConfig _profile;
    private bool _soft;
    private float _duration;

    public float Remaining { get; private set; }
    public float Elapsed => _duration - Remaining;
    public bool Soft => _soft;
    public int EndedStep { get; private set; }
    public bool FromSlam { get; private set; }
    /// <summary>下砸落地动画播多久：PlayerRules.SlamLandAnimSeconds，不超过收势时长。</summary>
    public float SlamLandSeconds => FromSlam ? System.Math.Min(P.Rules.SlamLandAnimSeconds, _duration) : 0f;

    public override string AnimName => "attack_recover";
    public override string DisplayName => "攻击后摇";
    public override float AnimDuration => _duration;

    public override void Enter(StateMsg msg)
    {
        _profile = msg?.Payload as FormConfig ?? P.Forms.Current;
        _soft = _profile.SoftAttackRecovery;
        _duration = _soft ? _profile.AttackRecovery : P.Rules.HardSlamRecovery;
        EndedStep = msg?.Get(EndedStepKey, 0).AsInt32() ?? 0;
        FromSlam = msg?.Get(FromSlamKey, false).AsBool() ?? false;
        Remaining = _duration;
        P.Timers.AttackInterval = _soft && _profile.ReattackAfter > 0f ? System.Math.Min(_profile.ReattackAfter, _duration) : _duration;
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
        // 连招第 2 下：第 1 下之后的收势前 ComboWindow 秒里按攻击（地面）
        if (_soft && EndedStep == 1 && AttackState.HasCombo(_profile) && P.IsOnFloor()
            && Elapsed <= _profile.ComboWindow + 0.000001f
            && P.Buffer.Consume(PlayerActions.Attack, P.Rules.AttackBuffer))
        {
            Machine.TransitionTo<AttackState>(new StateMsg().Set(AttackState.ComboStepKey, 2));
            return;
        }
        // 攻击间隔（ReattackAfter）到了：从第 1 下重新出刀，收势取消
        if (_soft && P.TryStartAttack()) return;

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
