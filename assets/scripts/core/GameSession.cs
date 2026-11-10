namespace SonOfMud.Core;

/// <summary>
/// 本局游戏的共享状态入口（纯静态，不是 autoload）。
/// 新游戏、读档、回主菜单时由 Save 系统调用 Reset。
/// </summary>
public static class GameSession
{
    public static ProgressStore Progress { get; private set; } = new();

    public static void Reset(AbilityTable initialAbilities = null)
    {
        Progress = new ProgressStore();
        Progress.ResetNew(initialAbilities);
        PauseService.Reset();
        InputLock.Reset();
    }
}
