namespace SonOfMud.Combat;

/// <summary>投掷物为什么结束。</summary>
public enum ProjectileEnd
{
    /// <summary>还在飞。</summary>
    None,
    /// <summary>打到了受击框并结算了一次（被挡住、扣血、打死、无敌、重复都算），随即消失。</summary>
    Target,
    /// <summary>撞到世界碰撞（墙、地面、平台）就碎了。</summary>
    World,
    /// <summary>飞得太久（超过 MaxLifetime）自己消失。</summary>
    Expired,
}
