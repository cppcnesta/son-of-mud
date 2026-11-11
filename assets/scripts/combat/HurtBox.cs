using System.Collections.Generic;
using Godot;

namespace SonOfMud.Combat;

/// <summary>
/// 受击框。凡是能被打的东西都挂它（角色、可破坏物）。
/// 结算顺序：不接受 → Ignored；按实体去重 → Duplicate；无敌 → Invulnerable；
/// 格挡覆盖且可挡 → Blocked；否则按「取最大倍率、最后取整一次」扣血 → Damaged / Killed。
/// </summary>
[GlobalClass]
public partial class HurtBox : Area2D
{
    [Signal] public delegate void BlockedEventHandler(Hit hit);
    [Signal] public delegate void DamagedEventHandler(Hit hit, int dealt);
    [Signal] public delegate void KilledEventHandler(Hit hit);

    /// <summary>所属实体的根节点；同一实体的多个 HurtBox 共用一份去重记录。没填就用 Owner，再没有就用父节点。</summary>
    [Export] public Node EntityRoot { get; set; }
    /// <summary>没填就在实体根节点（再找不到就在父节点）的子节点里找第一个 Stats；第一次用到时才找。</summary>
    [Export] public Stats Stats
    {
        get => _stats ??= FindStats();
        set => _stats = value;
    }
    /// <summary>只吃重击（脆弱地面）。</summary>
    [Export] public bool HeavyOnly { get; set; }
    /// <summary>全方向格挡；关掉时只挡 GuardFacing 一侧来的攻击。</summary>
    [Export] public bool Omnidirectional { get; set; } = true;

    public GuardMode Guard { get; set; } = GuardMode.None;
    public float GuardFacing { get; set; } = 1f;
    /// <summary>为 false 时一律返回 Ignored（死亡、调试飞行、锁定等）。</summary>
    public bool Receiving { get; set; } = true;

    private readonly HashSet<StringName> _invulnerableSources = new();
    private Stats _stats;

    public bool IsInvulnerable => _invulnerableSources.Count > 0;
    public Node Entity => EntityRoot ?? Owner ?? GetParent();
    public ulong EntityId => Entity?.GetInstanceId() ?? GetInstanceId();

    public override void _Ready()
    {
        Monitoring = false;
        Monitorable = true;
    }

    private Stats FindStats()
    {
        foreach (Node node in new[] { Entity, GetParent() })
        {
            if (node == null || !IsInstanceValid(node)) continue;
            foreach (Node child in node.GetChildren())
                if (child is Stats s) return s;
        }
        return null;
    }

    /// <summary>按来源开关无敌（多个来源同时生效时，全部关闭才解除）。</summary>
    public void SetInvulnerable(StringName source, bool on)
    {
        if (on) _invulnerableSources.Add(source);
        else _invulnerableSources.Remove(source);
    }

    public bool GuardCovers(Vector2 sourcePosition)
    {
        if (Guard == GuardMode.None) return false;
        if (Omnidirectional) return true;
        return (sourcePosition.X - GlobalPosition.X) * GuardFacing >= 0f;
    }

    public HitOutcome Receive(Hit hit)
    {
        if (hit == null || !Receiving || (HeavyOnly && !hit.Heavy) || (Stats != null && Stats.IsDepleted))
            return HitOutcome.Ignored;
        if (!AttackLedger.For(Entity).Accept(hit.AttackId, EntityId))
            return HitOutcome.Duplicate;
        if (IsInvulnerable)
            return HitOutcome.Invulnerable;
        if (hit.Blockable && GuardCovers(hit.SourcePosition))
        {
            EmitSignal(SignalName.Blocked, hit);
            return HitOutcome.Blocked;
        }
        int amount = DamageMath.Final(hit.Damage, hit.Multiplier, Stats?.IncomingMultiplier ?? 1f);
        int dealt = Stats?.TakeDamage(amount) ?? amount;
        hit.Dealt = dealt;
        bool killed = Stats != null && Stats.LastHitDepleted;
        EmitSignal(SignalName.Damaged, hit, dealt);
        if (!killed) return HitOutcome.Damaged;
        EmitSignal(SignalName.Killed, hit);
        return HitOutcome.Killed;
    }
}
