using System;
using System.Collections.Generic;

namespace SonOfMud.Core;

/// <summary>
/// 进程退出前（引擎还活着时）要执行的清理，例如释放静态字段持有的 Godot 资源。
/// 由 Events 离开场景树时统一执行，之后再强制一次 GC，让终结器在引擎销毁之前跑完。
/// </summary>
public static class ShutdownHooks
{
    private static readonly List<Action> Hooks = new();

    public static void Register(Action hook)
    {
        if (hook != null && !Hooks.Contains(hook)) Hooks.Add(hook);
    }

    public static void RunAll()
    {
        foreach (Action hook in Hooks.ToArray())
        {
            try { hook(); }
            catch (Exception e) { Godot.GD.PushError("ShutdownHooks：" + e.Message); }
        }
        Hooks.Clear();
    }
}
