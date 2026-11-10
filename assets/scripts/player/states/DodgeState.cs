using Godot;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 闪避：进入时锁定当前形态的闪避参数并开始冷却（水 0.65 / 泥 4 秒）。没有无敌帧。
/// 结束（或被打断）时开始该形态的减速恢复（泥：1 秒，从 20% 恢复）。
/// </summary>
public partial class DodgeState : PlayerState
{
    private FormConfig _profile;

    public float Remaining { get; private set; }
    public FormConfig Profile => _profile;

    public override string AnimName => "dodge";
    public override string DisplayName => "闪避";
    public override float AnimDuration => _profile?.DodgeDuration ?? 0f;

    public override void Enter(StateMsg msg)
    {
        _profile = P.Forms.Current;
        float axis = P.Axis;
        if (axis != 0f) P.Facing = Mathf.Sign(axis);
        P.Timers.DodgeCooldown = _profile.DodgeCooldown;
        Remaining = _profile.DodgeDuration;
    }

    public override void Exit()
    {
        if (_profile != null && _profile.PenaltyDuration > 0f)
            P.Timers.Recovery.Start(MovementRecovery.Source.Dodge, _profile.PenaltyDuration,
                _profile.PenaltyInitialMultiplier, _profile.RecoveryCurve);
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        P.Velocity = new Vector2(P.Facing * _profile.DodgeSpeed, P.Velocity.Y);
        P.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) P.ToFreeState();
    }
}
