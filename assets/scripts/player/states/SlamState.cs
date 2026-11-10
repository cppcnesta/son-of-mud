using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 重压下砸：空中下+攻击，且有 heavy_slam。以固定速度直落，落地那一帧打开圆形重击 HitBox（伤害 44），
/// 然后进入后摇（泥 1.5 秒可移动；水 0.25 秒站定）。
/// 走边缘换房时下砸资格保留（规格第 4 节），所以这里不处理 RoomExiting；过门 / 复活会直接回 Idle。
/// </summary>
public partial class SlamState : PlayerState
{
    public bool Landed { get; private set; }
    public string AttackId { get; private set; } = "";

    public override string AnimName => Landed ? "slam_land" : "slam_fall";
    public override string DisplayName => "重压下砸";

    public override void Enter(StateMsg msg)
    {
        Landed = false;
        P.Velocity = new Vector2(0f, P.Rules.SlamSpeed);
    }

    public override void Exit() => P.SlamHitBox.Deactivate();

    public override void PhysicsUpdate(double delta) => P.Velocity = new Vector2(0f, P.Rules.SlamSpeed);

    public override void AfterMove(double delta)
    {
        if (Landed || !P.IsOnFloor()) return;
        Landed = true;
        AttackId = P.NextAttackId("slam");
        if (P.SlamHitBox.GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D circle)
            circle.Radius = P.Rules.SlamRadius;
        P.SlamHitBox.Activate(Hit.From(P.Rules.SlamSpec, AttackId, P.GlobalPosition, P.Timers.Bonus.Current, P));
        P.SlamHitBox.Deactivate();
        Events.Instance?.EmitSignal(Events.SignalName.CameraShakeRequested, P.Rules.SlamShakeStrength, P.Rules.SlamShakeDuration);
        Machine.TransitionTo<AttackRecoveryState>(StateMsg.With(P.Forms.Current));
    }
}
