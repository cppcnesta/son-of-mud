using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>
/// 被反制僵直：StaggerWindow.Begin()——5 秒、受到 2 倍伤害、每受一次伤剩余时间减 1 秒（Combat 已有组件计时）。
/// 僵直结束时蝎尾冷却 EndCast（这时才开始重新充 20 秒）→ 逼近。
/// </summary>
public partial class StaggeredState : KeeperState
{
    public override string DisplayName => "反制僵直";
    public override string AnimName => "staggered";

    public override void Enter(StateMsg msg)
    {
        K.Stop();
        K.Stagger.Begin();
    }

    public override void Exit() => K.Stagger.End();

    public override void PhysicsUpdate(double delta)
    {
        K.Stop();
        K.ApplyGravity((float)delta);
        if (K.Stagger.Active) return;
        K.Tail.EndCast();
        Machine.TransitionTo<ApproachState>();
    }
}
