using SonOfMud.Enemy.States;

namespace SonOfMud.Enemy.Bosses.Keeper.States;

/// <summary>守坑人状态基类：K 是拥有者 KeeperBoss，D 是它的数值。</summary>
public partial class KeeperState : EnemyState
{
    protected KeeperBoss K => (KeeperBoss)E;
    protected KeeperData D => K.Keeper;
}
