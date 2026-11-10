using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>受伤硬直 0.18 秒（没有眩晕时），带击退；之后回自由状态。没有受伤无敌帧（候选规则：靠去重防连扣）。</summary>
public partial class HurtState : PlayerState
{
    public float Remaining { get; private set; }

    public override string AnimName => "hurt";
    public override string DisplayName => "受伤";
    public override float AnimDuration => P.Rules.HurtDuration;

    public override void Enter(StateMsg msg)
    {
        Remaining = P.Rules.HurtDuration;
        Knockback.Apply(P, msg?.Payload as Hit);
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
