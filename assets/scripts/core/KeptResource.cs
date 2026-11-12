using System.Collections.Generic;
using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 本项目所有 C# 资源类（.tres 数据、设置）的基类：实例一创建就登记进 <see cref="ResourceKeepAlive"/>，游戏运行期间不会被 .NET 回收。
///
/// 为什么：Godot 4.7 .NET 有一个终结器竞态。C# 脚本资源（例如 NarrativeSettings、TabletData、AbilityTable）的托管包装
/// 在没人引用时会被 GC 回收，它的终结器在终结器线程上释放本体；如果在这之前主线程又从资源缓存里取到同一个资源
/// （GD.Load、或者实例化引用它的场景），两边对同一个 GC 句柄各处理一次，引擎在 mono_object_disposed_baseref 里
/// 报 FATAL “gchandle.is_released()” 直接崩溃（退出码 132 / -1073741795）。崩不崩取决于 GC 时机，程序集越大越常见。
/// 托管包装一直有强引用就不会被回收，这条竞态就不存在。退出时由 ShutdownHooks 放掉，再由 Events 在主线程上强制回收。
/// 只对 Resource 这样做：Hit、AttackLedger 等直接 new 出来的 RefCounted 不在资源缓存里，回收后没人能再取回它们。
/// </summary>
public abstract partial class KeptResource : Resource
{
    protected KeptResource() => ResourceKeepAlive.Add(this);
}

/// <summary>运行期间持有全部 <see cref="KeptResource"/> 的强引用；退出时清空（见 <see cref="KeptResource"/>）。</summary>
public static class ResourceKeepAlive
{
    private static readonly object Gate = new();
    private static readonly HashSet<Resource> Alive = new();
    private static bool _hooked;

    public static int Count
    {
        get { lock (Gate) return Alive.Count; }
    }

    public static bool Contains(Resource resource)
    {
        lock (Gate) return resource != null && Alive.Contains(resource);
    }

    public static void Add(Resource resource)
    {
        if (resource == null) return;
        lock (Gate)
        {
            if (!_hooked)
            {
                ShutdownHooks.Register(ReleaseAll);
                _hooked = true;
            }
            Alive.Add(resource);
        }
    }

    /// <summary>放掉全部强引用（只在退出时由 ShutdownHooks 调用；之后由主线程上的 GC 回收）。</summary>
    public static void ReleaseAll()
    {
        lock (Gate)
        {
            Alive.Clear();
            _hooked = false;
        }
    }
}
