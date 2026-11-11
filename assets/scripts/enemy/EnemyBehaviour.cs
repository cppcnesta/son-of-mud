namespace SonOfMud.Enemy;

/// <summary>普通敌人的行为组合。</summary>
public enum EnemyBehaviour
{
    /// <summary>
    /// 被动：从不主动攻击（祈祷泥人）。被打中没死时按 FleeChance 逃跑，否则（配了攻击）原地起身反击一次，之后回去跪着。
    /// </summary>
    Passive,
    /// <summary>近战：巡逻、追、前摇、出手、收招（塔庙泥匠）。</summary>
    Melee,
    /// <summary>近战冲锋：前摇时锁定方向，冲刺出手（塔庙泥卫）。</summary>
    Charge,
}
