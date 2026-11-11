namespace SonOfMud.Combat;

/// <summary>
/// 一次命中的结算结果（规格第 5 节，只有这六种）。
/// 没打中就不会产生任何结果，也不会有回调。
/// </summary>
public enum HitOutcome
{
    /// <summary>受击方当前不接受攻击（死亡、调试飞行、只吃重击的物体被轻击等）。</summary>
    Ignored,
    /// <summary>同一次攻击已经结算过这个实体。</summary>
    Duplicate,
    Invulnerable,
    Blocked,
    Damaged,
    Killed,
}
