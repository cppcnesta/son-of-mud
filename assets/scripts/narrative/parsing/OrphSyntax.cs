using System.Text.RegularExpressions;

namespace SonOfMud.Narrative.Parsing;

/// <summary>剧本的行格式（说话人行、选项行）。</summary>
public static class OrphSyntax
{
    /// <summary>
    /// 说话人行：最多 16 个字，以冒号结尾，可以带括号写表情。
    /// 名字里不能有逗号、句号、括号等符号，这样以冒号结尾的长句不会被误认成说话人。（与 OrpheusVN 原版相同）
    /// </summary>
    public static readonly Regex SpeakerLine = new(
        @"^(?<name>[^\[\]（）()：:，,。！!…—]{1,16}?)\s*(?:[（(](?<expr>[^）)]+)[）)])?\s*[：:]$");

    /// <summary>[选项] 下面的一项：「- 文本 -> 标签」。减号可以是半角或全角，箭头可以写 -> 或 →。</summary>
    public static readonly Regex ChoiceLine = new(@"^[-－]\s*(?<text>.+?)\s*(?:->|→)\s*(?<label>.+?)\s*$");

    /// <summary>以减号开头的行都当成选项行（写错格式时报错，而不是当成台词显示出来）。</summary>
    public static bool LooksLikeOption(string trimmed) =>
        trimmed.Length > 1 && (trimmed[0] == '-' || trimmed[0] == '－') && trimmed[1] != '-' && trimmed[1] != '>';
}
