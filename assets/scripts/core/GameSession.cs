namespace SonOfMud.Core;

/// <summary>
/// 本局游戏的共享状态入口（纯静态，不是 autoload）。
/// 新游戏、读档、回主菜单时由 Save 系统调用 Reset。
/// PendingStart 和 LastStartError 是跨场景切换保留的开局约定，Reset 不动它们。
/// </summary>
public static class GameSession
{
    public static ProgressStore Progress { get; private set; } = new();

    /// <summary>
    /// 下一次进入正式游戏场景时怎么开局。主菜单（或重开一局）先设好再切场景；
    /// 正式场景里的 GameBoot 读取后立刻设回 None。
    /// </summary>
    public static StartMode PendingStart { get; set; } = StartMode.None;

    /// <summary>最近一次开局失败的原因（存档损坏、版本不支持……）；空表示没有失败。主菜单负责显示。</summary>
    public static string LastStartError { get; set; } = "";

    public static void Reset(AbilityTable initialAbilities = null)
    {
        Progress = new ProgressStore();
        Progress.ResetNew(initialAbilities);
        PauseService.Reset();
        InputLock.Reset();
    }
}
