using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>横扫前摇：站着不动 1 秒，朝向在进入时锁定；横扫冷却 BeginCast。玩家离开范围也照样出手。</summary>
public partial class SweepWindupState : KeeperState
{
    public float Remaining { get; private set; }

    public override string DisplayName => "横扫前摇";
    public override string AnimName => "sweep_windup";
    public override float AnimDuration => D.SweepWindup;

    public override void Enter(StateMsg msg)
    {
        K.Stop();
        K.FaceTarget();
        K.LockedDirection = K.Facing;
        K.Sweep.BeginCast();
        Remaining = D.SweepWindup;
    }

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        K.Stop();
        K.Facing = K.LockedDirection;
        K.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f) Machine.TransitionTo<SweepStrikeState>();
    }
}
