using Godot;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 起跳前摇（地面、土狼时间内、贴墙三种来源；没有二段跳）。进入时就锁定这次起跳用的形态参数，
/// 前摇结束那一帧给出起跳速度并进入 Rise。
/// </summary>
public partial class JumpWindupState : PlayerState
{
    public static readonly StringName WallKey = "wall";

    private FormConfig _profile;
    private float _remaining;
    private bool _wall;

    public override string AnimName => "jump_windup";
    public override string DisplayName => "起跳前摇";
    public override float AnimDuration => _profile?.JumpWindup ?? 0f;
    public FormConfig Profile => _profile;

    public override void Enter(StateMsg msg)
    {
        _profile = P.Forms.Current;
        _remaining = _profile.JumpWindup;
        _wall = msg != null && msg.Get(WallKey, false).AsBool();
        P.Coyote.Clear();
        P.Buffer.Clear(PlayerActions.Jump);
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        P.StopHorizontal();
        if (_wall) P.Velocity = new Vector2(0f, 0f);
        else P.ApplyGravity(dt);

        _remaining -= dt;
        if (_remaining > 0.000001f) return;
        P.JumpProfile = _profile;
        P.Velocity = new Vector2(P.Velocity.X, -_profile.JumpSpeed);
        Machine.TransitionTo<RiseState>();
    }
}
