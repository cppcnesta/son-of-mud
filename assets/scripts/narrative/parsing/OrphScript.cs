using System.Collections.Generic;

namespace SonOfMud.Narrative.Parsing;

/// <summary>
/// 解析后的剧本：一串指令 + 一张标签表 + 解析时发现的错误和警告。
/// 由 OrphParser 在运行时从 .orph 文本生成（不是 Godot 资源）。出错的指令不会写进 Entries。
/// </summary>
public sealed class OrphScript
{
    public OrphScript(string id) => Id = id ?? "";

    /// <summary>剧本编号：文件名去掉 .orph，比如 m_pit。Events.StoryRequested 用它。</summary>
    public string Id { get; }
    public List<OrphEntry> Entries { get; } = new();
    public List<OrphLabel> Labels { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>标签对应的指令下标；找不到返回 -1。</summary>
    public int FindLabel(string name)
    {
        foreach (OrphLabel label in Labels)
            if (label.Name == name) return label.Index;
        return -1;
    }

    /// <summary>从 start 开始往后找第一条这个命令；找不到返回 -1。</summary>
    public int FindNext(string command, int start)
    {
        for (int i = System.Math.Max(0, start); i < Entries.Count; i++)
            if (Entries[i].Command == command) return i;
        return -1;
    }

    public void Add(string command, string[] args, int sourceLine) =>
        Entries.Add(new OrphEntry { Command = command, Args = args, SourceLine = sourceLine });

    /// <summary>在 index 处插入一条指令。只给解析器用：插入点后面不能已经有标签。</summary>
    public void Insert(int index, string command, string[] args, int sourceLine) =>
        Entries.Insert(index, new OrphEntry { Command = command, Args = args, SourceLine = sourceLine });

    /// <summary>在当前位置放一个标签，它指向接下来加入的那条指令。</summary>
    public void AddLabel(string name, int sourceLine) =>
        Labels.Add(new OrphLabel { Name = name, Index = Entries.Count, SourceLine = sourceLine });

    /// <summary>删掉一条指令，并把它后面的标签位置往前挪一格。</summary>
    public void RemoveEntry(int index)
    {
        Entries.RemoveAt(index);
        foreach (OrphLabel label in Labels)
            if (label.Index > index) label.Index--;
    }
}
