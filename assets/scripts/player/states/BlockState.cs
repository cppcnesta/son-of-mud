using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 格挡。按下即开始格挡冷却（3 秒）。
/// 水（ME05）：最长 0.5 秒窗口，只挡一次；成功 → 结束格挡、清格挡冷却、清攻击间隔；按住不续挡。
/// 泥（ME06）：按住最多 2 秒，可挡多段；成功 → 下一次有效命中增伤（不叠加），不清冷却和攻击间隔。
/// 时间到或松开 → 自由状态。
/// </summary>
public partial class BlockState : PlayerState
{
    private FormConfig _profile;

    public float Remaining { get; private set; }
    public FormConfig Profile => _profile;
    public int BlockedCount { get; private set; }

    public override string AnimName => "block";
    public override string DisplayName => "格挡";

    public override void Enter(StateMsg msg)
    {
        _profile = P.Forms.Current;
        P.Timers.BlockCooldown = _profile.BlockCooldown;
        Remaining = _profile.BlockDuration;
        BlockedCount = 0;
        P.HurtBox.Guard = _profile.BlockSingleHit ? GuardMode.Single : GuardMode.Hold;
        P.HurtBox.GuardFacing = P.Facing;
    }

    public override void Exit() => P.HurtBox.Guard = GuardMode.None;

    public override void PhysicsUpdate(double delta)
    {
        float dt = (float)delta;
        P.StopHorizontal();
        P.ApplyGravity(dt);
        Remaining -= dt;
        if (Remaining <= 0.000001f || !P.Held(PlayerActions.Block)) P.ToFreeState();
    }

    /// <summary>HurtBox 挡住一次攻击后由 Player 调用。</summary>
    public void OnBlocked(Hit hit)
    {
        BlockedCount++;
        if (_profile.GrantsBlockBonus) P.Timers.Bonus.Grant(_profile.BlockBonusMultiplier);
        if (_profile.ResetBlockCooldownOnSuccess) P.Timers.BlockCooldown = 0f;
        if (_profile.ResetAttackIntervalOnBlock) P.Timers.ResetAttackInterval();
        if (_profile.BlockSingleHit) P.ToFreeState();
    }
}
