namespace SonOfMud.World;

/// <summary>出口类型：Edge = 走到边缘自动换房（MetSys 通道）；Door = 按交互键过门（不要求地图上相邻）。</summary>
public enum ExitKind
{
    Edge = 0,
    Door = 1,
}
