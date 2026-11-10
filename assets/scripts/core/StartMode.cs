namespace SonOfMud.Core;

/// <summary>进入正式游戏场景时要怎么开局（GameSession.PendingStart）。</summary>
public enum StartMode
{
    /// <summary>没有指定：直接运行正式场景（F6）时就是这个值，按新游戏处理。</summary>
    None,
    NewGame,
    Continue,
}
