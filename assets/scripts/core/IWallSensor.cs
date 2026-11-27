namespace SonOfMud.Core;

/// <summary>玩家实现：现在是不是贴着一面能爬的墙（新手引导用它判断「第一次碰到能爬的墙」，Core 和剧情层不认识玩家类）。</summary>
public interface IWallSensor
{
    bool TouchingClimbableWall { get; }
}
