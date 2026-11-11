using Godot;
using System.Collections.Generic;
using SonOfMud.Core;
using SonOfMud.Player;
using GDictionary = Godot.Collections.Dictionary;
using Kind = SonOfMud.Player.MudActionState.Kind;

namespace SonOfMud.Combat;

public partial class MudPlayerCombat : Node
{
    public MudPlayer Actor { get; set; }
    public MudFormConfig Profile { get; private set; }
    public string AttackId { get; private set; } = "";
    public int Sequence { get; private set; }
    private readonly HashSet<ulong> _victims = new();
    public bool BonusReady { get; set; }
    public bool DownAttack { get; private set; }
    public float LastDamage { get; private set; }
    public int LandedBursts { get; private set; }
    public float AttackMultiplier { get; private set; } = 1f;
    public float DownDamageMultiplier { get; private set; } = 1f;
    public float AttackIntervalRemaining { get; set; }
    public float RecoveryRemaining { get; private set; }

    public bool IsReady() => AttackIntervalRemaining <= 0f;
    public bool Start(MudFormConfig config, bool downward)
    {
        if (!IsReady() || Actor.Action.State != Kind.Free) return false;
        Profile = config;
        DownAttack = downward;
        DownDamageMultiplier = downward && Actor.Form == 1 && MudProgress.Instance.MudModule == 0 && Actor.Velocity.Y >= 0f
            ? config.DownAttackMultiplier : 1f;
        AttackId = $"player_{Actor.GetInstanceId()}_{++Sequence}";
        _victims.Clear();
        AttackMultiplier = 1f;
        AttackIntervalRemaining = config.AttackWindup + config.AttackActive + config.AttackRecovery;
        RecoveryRemaining = 0f;
        Actor.Action.Enter(Kind.AttackWindup, config.AttackWindup);
        return true;
    }
    public void Tick(float delta = 0f)
    {
        AttackIntervalRemaining = Mathf.Max(0f, AttackIntervalRemaining - delta);
        RecoveryRemaining = Mathf.Max(0f, RecoveryRemaining - delta);
        if (AttackIntervalRemaining < 0.000001f) AttackIntervalRemaining = 0f;
        if (RecoveryRemaining < 0.000001f) RecoveryRemaining = 0f;
        var action = Actor.Action;
        if (action.State == Kind.AttackActive)
        {
            var shape = new RectangleShape2D { Size = new Vector2(Profile.AttackReach, 70f) };
            var offset = new Vector2(Actor.Facing * (Profile.AttackReach * 0.5f + 16f), 0f);
            if (DownAttack) { shape.Size = new Vector2(72f, Profile.AttackReach); offset = new Vector2(0f, Profile.AttackReach * 0.5f); }
            Strike(shape, Actor.GlobalPosition + offset, Profile.Damage * DownDamageMultiplier, false);
        }
        if (action.Remaining > 0f) return;
        switch (action.State)
        {
            case Kind.AttackWindup: action.Enter(Kind.AttackActive, Profile.AttackActive); break;
            case Kind.AttackActive: BeginRecovery(Profile); break;
            case Kind.AttackRecovery: action.Enter(Kind.Free); break;
        }
    }
    public bool CanMoveInRecovery() => RecoveryRemaining > 0f && Profile != null && Profile.SoftAttackRecovery;
    public void BeginRecovery(MudFormConfig config)
    {
        Profile = config;
        RecoveryRemaining = Profile.AttackRecovery;
        AttackIntervalRemaining = Profile.AttackRecovery;
        if (Profile.SoftAttackRecovery)
            Actor.Recovery.Start(MudMovementRecovery.Source.Attack, Profile.AttackRecovery, Profile.PenaltyInitialMultiplier, Profile.RecoveryCurve);
        Actor.Action.Enter(Kind.AttackRecovery, Profile.AttackRecovery);
    }
    public bool CanDodgeCancel() => RecoveryRemaining > 0f && Profile != null && Profile.DodgeCancelRecovery;
    public void ResetAttackInterval()
    {
        AttackIntervalRemaining = 0f;
        RecoveryRemaining = 0f;
        if (Actor.Recovery.RecoverySource == MudMovementRecovery.Source.Attack) Actor.Recovery.Clear();
        if (Actor.Action.State == Kind.AttackRecovery) Actor.Action.Enter(Kind.Free);
    }
    public GDictionary Snapshot() => new()
    {
        ["interval"] = AttackIntervalRemaining, ["recovery"] = RecoveryRemaining, ["profile_path"] = Profile?.ResourcePath ?? ""
    };
    public void Restore(GDictionary data)
    {
        AttackIntervalRemaining = Mathf.Max(0f, data.Num("interval"));
        RecoveryRemaining = Mathf.Max(0f, data.Num("recovery"));
        var path = data.Str("profile_path");
        Profile = path.Length > 0 && ResourceLoader.Exists(path, "Resource") ? GD.Load<MudFormConfig>(path) : Actor.Config();
    }
    public void Burst(float damage, float radius, bool heavy)
    {
        AttackId = $"landing_{Actor.GetInstanceId()}_{++Sequence}";
        _victims.Clear();
        AttackMultiplier = 1f;
        LandedBursts++;
        Strike(new CircleShape2D { Radius = radius }, Actor.GlobalPosition, damage, heavy);
    }
    public void Strike(Shape2D shape, Vector2 at, float damage, bool heavy)
    {
        var query = new PhysicsShapeQueryParameters2D { Shape = shape, Transform = new Transform2D(0f, at), CollisionMask = 4 };
        foreach (var hit in Actor.GetWorld2D().DirectSpaceState.IntersectShape(query, 32))
        {
            var target = hit["collider"].AsGodotObject();
            if (target is not IMudDamageable damageable || !_victims.Add(target.GetInstanceId())) continue;
            var multiplier = BonusReady ? Actor.MudConfig.BlockBonusMultiplier : AttackMultiplier;
            if (!damageable.ReceiveHit(damage * multiplier, AttackId, new GDictionary { ["heavy"] = heavy, ["source"] = Actor.GlobalPosition })) continue;
            LastDamage = damage * multiplier;
            AttackMultiplier = multiplier;
            BonusReady = false;
            Actor.QueueRedraw();
        }
    }
}
