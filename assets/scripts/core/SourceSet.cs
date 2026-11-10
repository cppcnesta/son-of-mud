using System.Collections.Generic;
using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 按来源登记的开关：任一来源打开即为激活，所有来源都关闭才解除。
/// 同一来源重复打开只算一次，重复关闭也不会把别的来源一起关掉。
/// </summary>
public sealed class SourceSet
{
    private readonly HashSet<StringName> _sources = new();

    public bool Active => _sources.Count > 0;
    public int Count => _sources.Count;
    public bool Contains(StringName source) => _sources.Contains(source);

    /// <summary>设置某个来源的开关，返回整体激活状态是否发生了变化。</summary>
    public bool Set(StringName source, bool on)
    {
        bool before = Active;
        if (on) _sources.Add(source);
        else _sources.Remove(source);
        return before != Active;
    }

    public void Clear() => _sources.Clear();
}
