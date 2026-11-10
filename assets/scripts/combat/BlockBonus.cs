namespace SonOfMud.Combat;

/// <summary>
/// 泥格挡增伤：格挡成功后就绪，下一次有效命中享受倍率。不叠加。
/// 结果是 Damaged 或 Killed 才消耗；挥空、被格挡、被判重复都不消耗（规格第 5 节）。
/// </summary>
public sealed class BlockBonus
{
    public bool Ready { get; private set; }
    public float Multiplier { get; private set; } = 1f;

    /// <summary>当前可用的倍率；没就绪时为 1。</summary>
    public float Current => Ready ? Multiplier : 1f;

    public void Grant(float multiplier)
    {
        Ready = true;
        Multiplier = multiplier;
    }

    /// <summary>根据一次命中的结果决定是否消耗，返回是否消耗了。</summary>
    public bool OnOutcome(HitOutcome outcome)
    {
        if (!Ready || (outcome != HitOutcome.Damaged && outcome != HitOutcome.Killed)) return false;
        Clear();
        return true;
    }

    public void Clear()
    {
        Ready = false;
        Multiplier = 1f;
    }
}
