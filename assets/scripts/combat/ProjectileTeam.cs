namespace SonOfMud.Combat;

/// <summary>投掷物属于哪一方，决定它用哪套碰撞层（Core.Layers）。</summary>
public enum ProjectileTeam
{
    /// <summary>敌方扔的：在 EnemyHit 层，只打玩家的受击框（PlayerHurt）。</summary>
    Enemy,
    /// <summary>我方扔的：在 PlayerHit 层，只打敌人和可破坏物的受击框（EnemyHurt）。</summary>
    Player,
}
