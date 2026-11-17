using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 普通攻击（连招第 1、2 下）/ 空中下劈。由自己的计时器决定收招（规格第 5 节）：前摇 → 有效判定（开 HitBox）→
/// 水形态站定后摇后回自由状态；泥形态进入 1.5 秒可移动的 AttackRecovery。打没打中都按时收招。
/// - 连招（战斗策划表 3.2）：水第 1 下的站定后摇里（前 ComboWindow 秒）按攻击，立刻接第 2 下，剩下的后摇取消；
///   泥的第 2 下由 AttackRecovery 在收势前 ComboWindow 秒里接。第 2 下播 attack_down，打完下一次又从第 1 下开始。
///   只在地面接；空中「下 + 攻击」的下劈不算连招。
/// - 前摇里按方向键可以转身（下劈除外）；进入判定以后朝向锁住，判定框按进入判定那一刻的朝向放。
/// - 攻击中按方向键以 AttackMoveMultiplier 倍移速走动（水 0.6，泥 0 = 站定），不改朝向。
/// 换房（RoomExiting）时把这一刀标记为「判定已结束」，剩下的有效帧不再打开 HitBox。
/// </summary>
public partial class AttackState : PlayerState
{
    public static readonly StringName DownKey = "down";
    /// <summary>连招第几下（1 或 2），不写是第 1 下。</summary>
    public static readonly StringName ComboStepKey = "combo_step";

    public enum Phase { Windup, Active, Recovery }

    private FormConfig _profile;
    private float _remaining;
    private bool _down;
    private bool _judgementClosed;

    public Phase Current { get; private set; }
    public bool Down => _down;
    /// <summary>连招第几下（1 或 2）。下劈算第 1 下。</summary>
    public int ComboStep { get; private set; } = 1;
    public bool JudgementClosed => _judgementClosed;
    public FormConfig Profile => _profile;
    public string AttackId { get; private set; } = "";

    private bool Second => ComboStep == 2;
    private float Windup => Second ? _profile.Attack2Windup : _profile.AttackWindup;
    private float Active => Second ? _profile.Attack2Active : _profile.AttackActive;
    private float Recovery => Second ? _profile.Attack2Recovery : _profile.AttackRecovery;
    private HitSpec Spec => Second && _profile.Attack2Spec != null ? _profile.Attack2Spec : _profile.AttackSpec;

    public override string AnimName => _down || Second ? "attack_down" : "attack";
    public override string DisplayName => (Second ? "连招第 2 下" : "攻击") + Current switch
    {
        Phase.Windup => "前摇",
        Phase.Active => "判定",
        _ => "后摇",
    };
    public override float AnimDuration =>
        _profile == null ? 0f : Windup + Active + (_profile.SoftAttackRecovery ? 0f : Recovery);

    /// <summary>这个形态能不能接连招第 2 下（配了第 2 下的攻击）。</summary>
    public static bool HasCombo(FormConfig profile) => profile?.Attack2Spec != null;

    public override void Enter(StateMsg msg)
    {
        _profile = P.Forms.Current;
        _down = msg != null && msg.Get(DownKey, false).AsBool();
        ComboStep = !_down && msg != null && msg.Get(ComboStepKey, 1).AsInt32() == 2 && HasCombo(_profile) ? 2 : 1;
        _judgementClosed = false;
        Current = Phase.Windup;
        _remaining = Windup;
        AttackId = P.NextAttackId(_down ? "attack_down" : Second ? "attack2" : "attack");
        // 和白盒一致：攻击间隔 = 前摇 + 判定 + 后摇，泥形态进收势时再按收势重设。接第 2 下不看攻击间隔。
        P.Timers.AttackInterval = Windup + Active + Recovery;
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
        float axis = P.Axis;
        // 前摇里可以转身（下劈的判定在脚下，不用转）
        if (Current == Phase.Windup && !_down && axis != 0f) P.Facing = Mathf.Sign(axis);
        P.DriftWhileAttacking(axis, _profile.AttackMoveMultiplier);
        // 水：第 1 下站定后摇的前 ComboWindow 秒里按攻击（缓冲里有也算）→ 立刻接第 2 下
        if (Current == Phase.Recovery && ComboStep == 1 && !_down && HasCombo(_profile) && P.IsOnFloor()
            && Recovery - _remaining <= _profile.ComboWindow + 0.000001f
            && P.Buffer.Consume(PlayerActions.Attack, P.Rules.AttackBuffer))
        {
            Machine.TransitionTo<AttackState>(new StateMsg().Set(ComboStepKey, 2));
            return;
        }
        P.ApplyGravity(dt);
        _remaining -= dt;
        while (_remaining <= 0.000001f)
        {
            switch (Current)
            {
                case Phase.Windup:
                    Current = Phase.Active;
                    _remaining += Active;
                    OpenHitBox();
                    break;
                case Phase.Active:
                    P.AttackHitBox.Deactivate();
                    if (_profile.SoftAttackRecovery)
                    {
                        Machine.TransitionTo<AttackRecoveryState>(StateMsg.With(_profile).Set(AttackRecoveryState.EndedStepKey, _down ? 0 : ComboStep));
                        return;
                    }
                    Current = Phase.Recovery;
                    _remaining += Recovery;
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
                // 下劈框从脚底往下伸（原点是身体中心，所以先下移半个身高）
                shapeNode.Position = new Vector2(0f, BodyFeet.FeetOffset(P) + reach * 0.5f);
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
        Hit hit = Hit.From(Spec, AttackId, P.GlobalPosition, multiplier, P);
        if (_down && _profile.DownAttackFeel != null) hit.Feel = _profile.DownAttackFeel;
        // 挥刀音：出刀就播，打没打中都一样
        if (hit.Feel != null && hit.Feel.SwingSounds.Count > 0)
            Events.Instance?.EmitSignal(Events.SignalName.SoundRequested, (Godot.Collections.Array)hit.Feel.SwingSounds);
        P.AttackHitBox.Activate(hit);
    }
}
