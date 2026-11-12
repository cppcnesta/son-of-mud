using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 地形表面的约定（关卡几何计划 v3 第 2.1 节）。放在 Core，因为 Player 只能依赖 Core 和 Combat，读不到 World。
/// </summary>
public static class Surfaces
{
    /// <summary>瓦片集里的自定义数据层名（bool）。为 true 的瓦片不能攀爬，也不能蹬墙跳。</summary>
    public const string NoClimbData = "no_climb";

    /// <summary>节点分组：在这个分组里的碰撞体（竞技场门体、条件门等）不能攀爬，也不能蹬墙跳。</summary>
    public static readonly StringName NoClimbGroup = "no_climb";
}
