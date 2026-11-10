using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 全工程唯一写 SceneTree.Paused 的地方。剧情、地图、暂停菜单各自用自己的来源名请求，
/// 任一来源未释放，游戏就保持暂停。其他代码禁止直接写 Paused（测试会全局检查）。
/// </summary>
public static class PauseService
{
    public static readonly StringName Narrative = "narrative";
    public static readonly StringName Map = "map";
    public static readonly StringName PauseMenu = "pause_menu";

    private static readonly SourceSet Sources = new();

    public static bool IsPaused => Sources.Active;
    public static bool IsRequestedBy(StringName source) => Sources.Contains(source);

    public static void Request(StringName source, bool on)
    {
        if (!Sources.Set(source, on)) return;
        Apply();
    }

    /// <summary>回主菜单、开新游戏或测试开始时调用，清掉所有暂停来源。</summary>
    public static void Reset()
    {
        bool was = Sources.Active;
        Sources.Clear();
        if (was) Apply();
    }

    private static void Apply()
    {
        bool paused = Sources.Active;
        if (Engine.GetMainLoop() is SceneTree tree) tree.Paused = paused;
        Events.Instance?.EmitSignal(Events.SignalName.PauseChanged, paused);
    }
}
