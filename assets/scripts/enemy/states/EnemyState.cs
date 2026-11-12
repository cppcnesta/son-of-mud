using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>敌人状态基类（普通敌人和 Boss 共用）。拥有者是状态机的父节点 EnemyBody。</summary>
public partial class EnemyState : State
{
    private EnemyBody _body;
    protected EnemyBody E => _body ??= Machine.GetParent<EnemyBody>();

    /// <summary>这个状态播的动作名（美术图 spr_enemy_&lt;Kind&gt;_&lt;动作&gt;.png）；空字符串表示不播（例如守坑人已击败、节点隐藏）。</summary>
    public virtual string AnimName => "";
    /// <summary>不循环的动作整段要播多久（秒），动画会拉伸 / 压缩到这个长度；0 表示不拉伸（循环动作、死亡）。</summary>
    public virtual float AnimDuration => 0f;

    /// <summary>自己的攻击结算了（只用来分支；收招一律由状态自己的计时器决定）。</summary>
    public virtual void OnHitLanded(HurtBox target, Hit hit, HitOutcome outcome) { }
}
