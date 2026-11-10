using System.Collections.Generic;
using Godot;

namespace SonOfMud.Player;

/// <summary>
/// 输入缓冲：记录每个动作最近一次按下距今多久。状态在「能接受这个动作」时调用 Consume，
/// 在窗口内就算数并清掉记录，过期的按键直接作废。切形态不缓冲（避免误切）。
/// </summary>
public sealed class InputBuffer
{
    private readonly Dictionary<StringName, float> _ages = new();

    public void Press(StringName action) => _ages[action] = 0f;

    public void Tick(float delta)
    {
        if (_ages.Count == 0) return;
        foreach (StringName key in new List<StringName>(_ages.Keys)) _ages[key] += delta;
    }

    public bool Has(StringName action, float window) =>
        _ages.TryGetValue(action, out float age) && age <= window + 0.000001f;

    public bool Consume(StringName action, float window)
    {
        if (!Has(action, window)) return false;
        _ages.Remove(action);
        return true;
    }

    public void Clear(StringName action) => _ages.Remove(action);
    public void Clear() => _ages.Clear();
}
