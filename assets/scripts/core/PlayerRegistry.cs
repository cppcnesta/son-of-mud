using System;
using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 当前玩家的登记处（纯静态，不是 autoload）。只当 Node2D 用，Core 不认识玩家类。
/// 完全跟着玩家节点的生命周期走：玩家在 _EnterTree 时登记，_ExitTree 时注销（只注销自己）。
/// GameSession.Reset() 不碰这里：开新游戏、读档时玩家早就登记过了，不会再登记一次。
/// 敌人等后加载的节点在 _EnterTree 里先读 Current、再订阅 Changed，_ExitTree 时取消订阅。
/// （PlayerSpawned 事件保留给 World / Camera，它只在玩家初始化时发一次，晚加载的房间收不到。）
/// 静态字段里只存实例 ID，不长期持有托管包装对象：场景整个释放时不会留下指向已释放节点的静态引用。
/// </summary>
public static class PlayerRegistry
{
    private static ulong _currentId;

    /// <summary>当前玩家；没有（或已经被释放）时为 null。</summary>
    public static Node2D Current =>
        _currentId != 0 && GodotObject.IsInstanceIdValid(_currentId) ? GodotObject.InstanceFromId(_currentId) as Node2D : null;

    /// <summary>Current 变化时发出（参数是新的 Current，可能为 null）。</summary>
    public static event Action<Node2D> Changed;

    /// <summary>玩家进入场景树时调用。重复登记同一个玩家不会重复通知。</summary>
    public static void Register(Node2D player)
    {
        if (player == null) return;
        ulong id = player.GetInstanceId();
        if (id == _currentId) return;
        _currentId = id;
        Changed?.Invoke(player);
    }

    /// <summary>玩家离开场景树时调用。只有 Current 还是它自己才清空（新玩家已经登记时不受影响）。</summary>
    public static void Unregister(Node2D player)
    {
        if (player == null || player.GetInstanceId() != _currentId) return;
        _currentId = 0;
        Changed?.Invoke(null);
    }
}
