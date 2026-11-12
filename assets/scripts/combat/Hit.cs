using Godot;

namespace SonOfMud.Combat;

/// <summary>
/// 一次具体的攻击：由 HitBox 在激活时根据 HitSpec 生成，传给受击方的 HurtBox。
/// AttackId 用来去重：同一次攻击对同一个实体只结算一次。
/// </summary>
public partial class Hit : RefCounted
{
    public string AttackId { get; set; } = "";
    public float Damage { get; set; }
    /// <summary>攻击方自己的倍率（例如泥格挡增伤 1.6、下落攻击 1.8），攻击方已经取过最大值。</summary>
    public float Multiplier { get; set; } = 1f;
    public Vector2 SourcePosition { get; set; }
    public bool Heavy { get; set; }
    public bool Blockable { get; set; } = true;
    public float StunSeconds { get; set; }
    public float KnockbackForce { get; set; }
    public bool CounterOnBlock { get; set; }
    public string[] Tags { get; set; } = System.Array.Empty<string>();
    /// <summary>发起攻击的实体（可能为 null）。</summary>
    public Node Source { get; set; }
    /// <summary>结算后实际扣掉的血量（Damaged / Killed 时有效）。</summary>
    public int Dealt { get; set; }
    /// <summary>这一刀的打击感（来自 HitSpec.Feel；攻击方可以换成别的，例如下劈）。</summary>
    public HitFeel Feel { get; set; }

    public static Hit From(HitSpec spec, string attackId, Vector2 sourcePosition, float multiplier = 1f, Node source = null) => new()
    {
        AttackId = attackId,
        Damage = spec?.Damage ?? 0f,
        Heavy = spec?.Heavy ?? false,
        Blockable = spec?.Blockable ?? true,
        StunSeconds = spec?.StunSeconds ?? 0f,
        KnockbackForce = spec?.KnockbackForce ?? 0f,
        CounterOnBlock = spec?.CounterOnBlock ?? false,
        Tags = spec?.Tags ?? System.Array.Empty<string>(),
        Multiplier = multiplier,
        SourcePosition = sourcePosition,
        Source = source,
        Feel = spec?.Feel,
    };
}
