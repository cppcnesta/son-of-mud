using SonOfMud.Core;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>
/// 变身（任何状态下血量归零都进这里，优先级最高）。分两步：
/// ① 进入时立刻：关掉所有判定和 HurtBox，清掉技能计时和易伤，开始 1 秒闪白。这时还不写 boss_keeper，
///    所以老人 NPC 看不见、不能交互，出口门也还关着。
/// ② 闪白结束：写 boss_keeper = true（老人出现、出口门打开），然后发 BossDefeated（恰好一次）→ Gone。
/// </summary>
public partial class TransformState : KeeperState
{
    public override string DisplayName => "变身";
    public override string AnimName => "transform";
    public override float AnimDuration => D.TransformFlash;

    public override void Enter(StateMsg msg)
    {
        K.Transforming = true;
        K.ShutDownCombat();
        K.FlashRemaining = D.TransformFlash;
    }

    public override void PhysicsUpdate(double delta)
    {
        K.Stop();
        K.ApplyGravity((float)delta);
        K.FlashRemaining -= (float)delta;
        if (K.FlashRemaining > 0.000001f) return;
        K.FlashRemaining = 0f;
        GameSession.Progress.SetVar(K.FlagKey, true);
        K.EmitDefeated();
        K.Transforming = false;
        Machine.TransitionTo<GoneState>();
    }
}
