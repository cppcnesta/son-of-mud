namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>等待：站着不动，等玩家进入竞技场 → 开战（发 BossEncounterStarted）→ 逼近。</summary>
public partial class DormantState : KeeperState
{
    public override string DisplayName => "等待";
    public override string AnimName => "dormant";

    public override void PhysicsUpdate(double delta)
    {
        K.Stop();
        K.ApplyGravity((float)delta);
        if (!K.HasTarget || !K.InArena(K.Target.GlobalPosition)) return;
        K.BeginFight();
        Machine.TransitionTo<ApproachState>();
    }
}
