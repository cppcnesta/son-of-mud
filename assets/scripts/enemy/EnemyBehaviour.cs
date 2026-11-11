namespace SonOfMud.Enemy;

/// <summary>普通敌人的行为组合。</summary>
public enum EnemyBehaviour
{
    /// <summary>被动：从不攻击（朝圣者、跪拜泥人）。受击反应由 EnemyData.ReactsToHits 决定。</summary>
    Passive,
    /// <summary>近战：追、前摇、出手、收招（工人）。</summary>
    Melee,
    /// <summary>近战冲锋：前摇时锁定方向，冲刺出手（守卫）。</summary>
    Charge,
}
