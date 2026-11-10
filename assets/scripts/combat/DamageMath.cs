namespace SonOfMud.Combat;

/// <summary>
/// 伤害公式（规格第 5 节、D2）：最终伤害 = floor(基础伤害 × 所有适用倍率中的最大值)，只在最后取整一次。
/// 没有任何倍率时按 1 倍算。
/// </summary>
public static class DamageMath
{
    public static float MaxMultiplier(params float[] multipliers)
    {
        if (multipliers == null || multipliers.Length == 0) return 1f;
        float best = multipliers[0];
        foreach (float m in multipliers)
            if (m > best) best = m;
        return best;
    }

    public static int Final(float baseDamage, params float[] multipliers)
    {
        // 加一个很小的余量，避免 24×1.6=38.4 这类乘积因为浮点误差落到整数以下被多舍掉。
        int result = (int)System.MathF.Floor(baseDamage * MaxMultiplier(multipliers) + 0.0001f);
        return result < 0 ? 0 : result;
    }
}
