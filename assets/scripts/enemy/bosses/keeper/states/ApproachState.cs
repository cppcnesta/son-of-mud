using Godot;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>
/// 逼近：以中速（Q7：120px/s）走向玩家，到 StopDistance 就站住。每帧判断（水平距离）：
/// 蝎尾就绪且玩家在触发距离内 → 瞄准；横扫就绪且距离 ≤ 128 → 横扫前摇。两个都就绪时蝎尾优先（Q3）。
/// </summary>
public partial class ApproachState : KeeperState
{
    public override string DisplayName => "逼近";
    public override string AnimName => "approach";

    public override void PhysicsUpdate(double delta)
    {
        K.ApplyGravity((float)delta);
        if (!K.HasTarget)
        {
            K.Stop();
            return;
        }
        float distance = Mathf.Abs(K.DxToTarget);
        if (K.Tail.IsReady && distance <= D.TailTriggerRange)
        {
            Machine.TransitionTo<TailAimState>();
            return;
        }
        if (K.Sweep.IsReady && distance <= D.SweepRange)
        {
            Machine.TransitionTo<SweepWindupState>();
            return;
        }
        K.FaceTarget();
        if (distance > D.StopDistance) K.Walk(Mathf.Sign(K.DxToTarget), D.MoveSpeed);
        else K.Stop();
    }
}
