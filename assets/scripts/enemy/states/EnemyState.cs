using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>敌人状态基类（普通敌人和 Boss 共用）。拥有者是状态机的父节点 EnemyBody。</summary>
public partial class EnemyState : State
{
    private EnemyBody _body;
    protected EnemyBody E => _body ??= Machine.GetParent<EnemyBody>();

    /// <summary>自己的攻击结算了（只用来分支；收招一律由状态自己的计时器决定）。</summary>
    public virtual void OnHitLanded(HurtBox target, Hit hit, HitOutcome outcome) { }
}
