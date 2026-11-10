namespace SonOfMud.Narrative.Parsing;

/// <summary>标签：剧本里的 # 标题。Index 是标签后面第一条指令的下标。</summary>
public sealed class OrphLabel
{
    public string Name { get; set; } = "";
    public int Index { get; set; }
    public int SourceLine { get; set; }
}
