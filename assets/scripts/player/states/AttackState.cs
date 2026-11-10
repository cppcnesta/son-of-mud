using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 普通攻击 / 空中下劈。由自己的计时器决定收招（规格第 5 节）：前摇 → 有效判定（开 HitBox）→
/// 水形态站定后摇后回自由状态；泥形态进入 1.5 秒可移动的 AttackRecovery。打没打中都按时收招。
/// 换房（RoomExiting）时把这一刀标记为「判定已结束」，剩下的有效帧不再打开 HitBox。
/// </summary>
public partial class AttackState : PlayerState
{
    public static readonly StringName DownKey = "down";

    public enum Phase { Windup, Active, Recovery }

    private FormConfig _profile;
    private float _remaining;
    private bool _down;
    private bool _judgementClosed;

    public Phase Current { get; private set; }
    public bool Down => _down;
    public bool JudgementClosed => _judgementClosed;
    public FormConfig Profile => _profile;
    public string AttackId { get; private set; } = "";

    public override string AnimName => _down ? "attack_down" : "attack";
    public override string DisplayName => Current switch
    {
        Phase.Windup => "攻击前摇",
        Phase.Active => "攻击判定",
        _ => "攻击后摇",
    };
    public override float AnimDuration =>
        _profile == null ? 0f : _profile.AttackWindup + _profile.AttackActive + (_profile.SoftAttackRecovery ? 0f : _profile.AttackRecovery);

    public override void Enter(StateMsg msg)
    {
        _profile = P.Forms.Current;
        _down = msg != null && msg.Get(DownKey, false).AsBool();
        _judgementClosed = false;
        Current = Phase.Windup;
        _remaining = _profile.AttackWindup;
        AttackId = P.NextAttackId(_down ? "attack_down" : "attack");
        // 和白盒一致：攻击间隔 = 前摇 + 判定 + 后摇，泥形态进后摇时再按后摇时长重设。
        P.Timers.AttackInterval = _profile.AttackWindup + _profile.AttackActive + _profile.AttackRecovery;
    }

    public override void Exit() => P.AttackHitBox.Deactivate();

    public override void OnRoomExiting()
    {
        _judgementClosed = true;
        P.AttackHitBox.Deactivate();
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        P.StopHorizontal();
        P.ApplyGravity(dt);
        _remaining -= dt;
        while (_remaining <= 0.000001f)
        {
            switch (Current)
            {
                case Phase.Windup:
                    Current = Phase.Active;
                    _remaining += _profile.AttackActive;
                    OpenHitBox();
                    break;
                case Phase.Active:
                    P.AttackHitBox.Deactivate();
                    if (_profile.SoftAttackRecovery)
                    {
                        Machine.TransitionTo<AttackRecoveryState>(StateMsg.With(_profile));
                        return;
                    }
                    Current = Phase.Recovery;
                    _remaining += _profile.AttackRecovery;
                    break;
                default:
                    P.ToFreeState();
                    return;
            }
        }
    }

    private void OpenHitBox()
    {
        if (_judgementClosed) return;
        var shapeNode = P.AttackHitBox.GetNode<CollisionShape2D>("CollisionShape2D");
        if (shapeNode.Shape is RectangleShape2D rect)
        {
            float reach = _profile.AttackReach;
            if (_down)
            {
                rect.Size = new Vector2(_profile.DownAttackWidth, reach);
                shapeNode.Position = new Vector2(0f, reach * 0.5f);
            }
            else
            {
                rect.Size = new Vector2(reach, _profile.AttackHeight);
                shapeNode.Position = new Vector2(P.Facing * (reach * 0.5f + _profile.AttackForwardOffset), 0f);
            }
        }
        // 倍率只取最大的一个（D2）：泥格挡增伤、下落中的下劈强化。
        float downMultiplier = _down && !P.IsOnFloor() && P.Velocity.Y >= 0f ? _profile.DownAttackMultiplier : 1f;
        float multiplier = DamageMath.MaxMultiplier(P.Timers.Bonus.Current, downMultiplier);
        P.AttackHitBox.Activate(Hit.From(_profile.AttackSpec, AttackId, P.GlobalPosition, multiplier, P));
    }
}
