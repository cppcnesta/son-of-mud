using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Core;

/// <summary>
/// 切换状态时附带的消息。Core 不认识其他系统的类型：
/// 需要传 Combat 的 Hit 之类时放进 Payload，由接收的状态自己转型。
/// </summary>
public sealed class StateMsg
{
    public object Payload { get; init; }
    public GDictionary Data { get; } = new();

    public static StateMsg With(object payload) => new() { Payload = payload };

    public StateMsg Set(StringName key, Variant value)
    {
        Data[key] = value;
        return this;
    }

    public Variant Get(StringName key, Variant fallback = default) =>
        Data.TryGetValue(key, out var value) ? value : fallback;
}
