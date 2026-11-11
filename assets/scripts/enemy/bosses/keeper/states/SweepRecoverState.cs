using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>
/// 横扫收招：僵直 0.5 秒，期间受到 2 倍伤害（Stats 易伤倍率，和其他倍率取最大）；受击不缩短（Q4）。
/// 结束时横扫冷却 EndCast（5 秒从这里才开始算）→ 逼近。
/// </summary>
public partial class SweepRecoverState : KeeperState
{
    public float Remaining { get; private set; }

    public override string DisplayName => "横扫收招";

    public override void Enter(StateMsg msg)
    {
        K.Stop();
        Remaining = D.SweepRecover;
        K.Stats.SetIncomingMultiplier(KeeperBoss.SweepVulnerableSource, D.SweepRecoverMultiplier);
    }

    public override void Exit() => K.Stats.SetIncomingMultiplier(KeeperBoss.SweepVulnerableSource, 1f);

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        K.Stop();
        K.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining > 0.000001f) return;
        K.Sweep.EndCast();
        Machine.TransitionTo<ApproachState>();
    }
}
