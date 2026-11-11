using System.Globalization;
using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 进度变量只允许三种类型：bool、int（Godot 里是 64 位）、string。
/// .orph 剧本里写的字面值和门的条件都按这里的规则解析和比较，保证两边读到的是同一个值。
/// </summary>
public static class StoryValue
{
    /// <summary>
    /// 解析剧本字面值：是 / true → true；否 / false → false（true/false 不区分大小写）；
    /// 整数 → int；其余原样作为 string。前后空白会被去掉。
    /// </summary>
    public static Variant Parse(string literal)
    {
        string text = (literal ?? "").Trim();
        if (text == "是" || text.Equals("true", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (text == "否" || text.Equals("false", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number)) return number;
        return text;
    }

    /// <summary>把允许的类型规范化（StringName 转 string，int 统一成 64 位）；不允许的类型返回 false。</summary>
    public static bool TryNormalize(Variant value, out Variant normalized)
    {
        switch (value.VariantType)
        {
            case Variant.Type.Bool:
                normalized = value.AsBool();
                return true;
            case Variant.Type.Int:
                normalized = value.AsInt64();
                return true;
            case Variant.Type.String:
            case Variant.Type.StringName:
                normalized = value.AsString();
                return true;
            default:
                normalized = default;
                return false;
        }
    }

    /// <summary>类型和值都相同才相等。类型不同返回 false，并通过 typeMismatch 告诉调用方（调用方负责报警告）。</summary>
    public static bool AreEqual(Variant a, Variant b, out bool typeMismatch)
    {
        typeMismatch = false;
        if (!TryNormalize(a, out var x) || !TryNormalize(b, out var y))
        {
            typeMismatch = true;
            return false;
        }
        if (x.VariantType != y.VariantType)
        {
            typeMismatch = true;
            return false;
        }
        return x.VariantType switch
        {
            Variant.Type.Bool => x.AsBool() == y.AsBool(),
            Variant.Type.Int => x.AsInt64() == y.AsInt64(),
            _ => x.AsString() == y.AsString(),
        };
    }
}
