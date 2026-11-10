using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 眩晕：没挡住 StunSeconds &gt; 0 的攻击（蝎尾 3 秒）。期间不能操作，但还能继续挨打（策划要的连环惩罚）；
/// 再吃到带眩晕的攻击时取较长的剩余时间。时间到 → 自由状态。
/// </summary>
public partial class StunnedState : PlayerState
{
    public float Remaining { get; private set; }

    public override string AnimName => "stunned";
    public override string DisplayName => "眩晕";

    public override void Enter(StateMsg msg)
    {
        var hit = msg?.Payload as Hit;
        Remaining = hit?.StunSeconds ?? 0f;
        Knockback.Apply(P, hit);
    }

    public void Extend(Hit hit)
    {
        if (hit != null) Remaining = Mathf.Max(Remaining, hit.StunSeconds);
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        P.Velocity = new Vector2(Mathf.MoveToward(P.Velocity.X, 0f, Mathf.Abs(P.Velocity.X) * 4f * dt + 1f), P.Velocity.Y);
        P.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) P.ToFreeState();
    }
}
