using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>
/// 蝎尾瞄准：3 秒，HurtBox 无敌（受击返回 Invulnerable）、不能移动，显示瞄准预警；蝎尾冷却 BeginCast。
/// 瞄准结束时锁定玩家当时的 X 坐标 → 蝎尾出手。
/// </summary>
public partial class TailAimState : KeeperState
{
    public float Remaining { get; private set; }

    public override string DisplayName => "蝎尾瞄准";

    public override void Enter(StateMsg msg)
    {
        K.Stop();
        K.FaceTarget();
        K.Tail.BeginCast();
        K.HurtBox?.SetInvulnerable(KeeperBoss.TailAimSource, true);
        K.Aiming = true;
        K.TailLocked = false;
        K.AimX = K.HasTarget ? K.Target.GlobalPosition.X : K.GlobalPosition.X;
        Remaining = D.TailAim;
    }

    public override void Exit()
    {
        K.HurtBox?.SetInvulnerable(KeeperBoss.TailAimSource, false);
        K.Aiming = false;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        K.Stop();
        K.ApplyGravity(dt);
        if (K.HasTarget) K.AimX = K.Target.GlobalPosition.X;
        Remaining -= dt;
        if (Remaining > 0.000001f) return;
        K.LockedTailX = K.AimX;
        K.TailLocked = true;
        Machine.TransitionTo<TailStrikeState>();
    }
}
