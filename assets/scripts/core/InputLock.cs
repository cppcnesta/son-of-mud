using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 玩家操作锁：只锁玩家输入，不暂停世界。换房淡入淡出、不暂停的剧情字幕等按来源请求。
/// 和 PauseService 是两个独立的概念，互不影响。
/// </summary>
public static class InputLock
{
    public static readonly StringName RoomTransition = "room_transition";
    public static readonly StringName Narrative = "narrative";

    private static readonly SourceSet Sources = new();

    public static bool IsLocked => Sources.Active;
    public static bool IsRequestedBy(StringName source) => Sources.Contains(source);

    public static void Request(StringName source, bool on)
    {
        if (!Sources.Set(source, on)) return;
        Events.Instance?.EmitSignal(Events.SignalName.InputLockChanged, Sources.Active);
    }

    public static void Reset()
    {
        bool was = Sources.Active;
        Sources.Clear();
        if (was) Events.Instance?.EmitSignal(Events.SignalName.InputLockChanged, false);
    }
}
