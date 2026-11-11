using Godot;
using SonOfMud.Player;

namespace SonOfMud.Combat;

public sealed class MudPlayerGuard
{
    public MudPlayer Actor { get; set; }
    public MudFormConfig Profile { get; private set; }
    public float CooldownRemaining { get; set; }

    public void Tick(float delta)
    {
        CooldownRemaining = Mathf.Max(0f, CooldownRemaining - delta);
        if (CooldownRemaining < 0.000001f) CooldownRemaining = 0f;
        if (Actor.Action.State == MudActionState.Kind.Block && (Actor.Action.Remaining <= 0f || !Input.IsActionPressed("block")))
            Actor.Action.Enter(MudActionState.Kind.Free);
    }
    public bool Start()
    {
        if (!Actor.HasAbility("block") || CooldownRemaining > 0f) return false;
        var state = Actor.Action.State;
        if (state != MudActionState.Kind.Free && state != MudActionState.Kind.AttackRecovery) return false;
        if (Actor.Combat.RecoveryRemaining > 0f)
        {
            if (Actor.Combat.Profile == null || !Actor.Combat.Profile.SoftAttackRecovery ||
                Actor.Combat.RecoveryRemaining > Actor.Combat.Profile.BlockCancelWindow + 0.000001f) return false;
        }
        else if (state == MudActionState.Kind.AttackRecovery) return false;
        Profile = Actor.Config();
        CooldownRemaining = Profile.BlockCooldown;
        Actor.Action.Enter(MudActionState.Kind.Block, Profile.BlockDuration);
        return true;
    }
    public bool Covers(Vector2 sourcePosition)
    {
        if (Actor.Action.State != MudActionState.Kind.Block || Profile == null) return false;
        return Actor.Rules.OmnidirectionalBlock || (sourcePosition.X - Actor.GlobalPosition.X) * Actor.Facing >= 0f;
    }
    public void Succeed()
    {
        if (Profile.GrantsBlockBonus) Actor.Combat.BonusReady = true;
        if (Profile.ResetBlockCooldownOnSuccess) CooldownRemaining = 0f;
        if (Profile.ResetAttackIntervalOnBlock) Actor.Combat.ResetAttackInterval();
        if (Profile.BlockSingleHit) Actor.Action.Enter(MudActionState.Kind.Free);
    }
}
