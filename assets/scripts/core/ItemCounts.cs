using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 计数道具（泥肝……）的数量：存在进度变量 count.&lt;物品编号&gt; 里（int），存档会保存它。
/// 拾取（World）、投掷（Player）、HUD（UI）都通过这里读写，保证用的是同一个变量名。
/// 数量变化时 ProgressStore 照常发 VariableChanged(count.&lt;物品编号&gt;, 新数量)。
/// </summary>
public static class ItemCounts
{
    public const string Prefix = "count.";

    public static string Key(StringName itemId) => Prefix + itemId;

    /// <summary>当前数量；没有这个变量或类型不对时是 0。</summary>
    public static long Get(StringName itemId)
    {
        Variant value = GameSession.Progress.GetVar(Key(itemId), 0);
        return value.VariantType == Variant.Type.Int ? value.AsInt64() : 0;
    }

    /// <summary>写入数量（小于 0 按 0 算）。</summary>
    public static void Set(StringName itemId, long value) =>
        GameSession.Progress.SetVar(Key(itemId), value < 0 ? 0 : value);

    /// <summary>这个变量名是不是某个物品的计数（UI 用来过滤 VariableChanged）。</summary>
    public static bool IsKeyOf(StringName key, StringName itemId) => key == Key(itemId);
}
