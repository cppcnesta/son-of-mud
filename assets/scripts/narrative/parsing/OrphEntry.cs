using System;

namespace SonOfMud.Narrative.Parsing;

/// <summary>
/// 剧本里的一条指令：命令名 + 参数。台词也是指令，命令名是“说”（新的一段）或“接着说”（同一段的下一句），
/// 参数是说话人、表情、台词。SourceLine 是它在 .orph 文件里的行号，报错用。
/// </summary>
public sealed class OrphEntry
{
    public string Command { get; set; } = "";
    public string[] Args { get; set; } = Array.Empty<string>();
    public int SourceLine { get; set; }

    public override string ToString() => $"{SourceLine}:[{Command} {string.Join(" ", Args)}]";
}
