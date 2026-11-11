using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Narrative.Commands;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Parsing;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative;

/// <summary>
/// 剧本内容检查（OrpheusVN 菜单“检查所有剧本”的运行时版，由测试调用）：
/// - 每个标签段的最后一条必须是 [结束] / [跳转] / [选项] / [重开]，不会一路播进下一个标签；
/// - [字幕] 只能写在标签下第一行；字幕段里不能有选项、泥板、CG 这类只能在对话框里用的命令；
/// - 说话人都在角色表里（或者是旁白）；
/// - [CG] / [泥板] / [铭文] 用到的数据文件都存在。
/// </summary>
public static class OrphValidator
{
    private static readonly HashSet<string> SegmentEnds = new()
    {
        EndCommand.CommandName, JumpCommand.CommandName, ChoiceCommand.CommandName, RestartCommand.CommandName,
    };

    public static List<string> Check(OrphScript script, NarrativeSettings settings)
    {
        var problems = new List<string>();
        CharacterDatabase characters = settings?.Characters;
        List<OrphLabel> labels = script.Labels.OrderBy(l => l.Index).ToList();

        for (int i = 0; i < labels.Count; i++)
        {
            OrphLabel label = labels[i];
            int end = i + 1 < labels.Count ? labels[i + 1].Index : script.Entries.Count;
            if (end <= label.Index)
            {
                problems.Add($"{script.Id}「{label.Name}」（第{label.SourceLine}行）：标签下面没有指令");
                continue;
            }
            OrphEntry last = script.Entries[end - 1];
            if (!SegmentEnds.Contains(last.Command))
                problems.Add($"{script.Id}「{label.Name}」（第{label.SourceLine}行）：最后一条是 [{last.Command}]（第{last.SourceLine}行），要以 [结束] / [跳转] / [选项] / [重开] 收尾");
            bool subtitle = script.Entries[label.Index].Command == SubtitleModeCommand.CommandName;
            for (int e = label.Index; e < end; e++)
            {
                OrphEntry entry = script.Entries[e];
                if (entry.Command == SubtitleModeCommand.CommandName && e != label.Index)
                    problems.Add($"{script.Id} 第{entry.SourceLine}行：[字幕] 只能写在标签下面第一行");
                if (subtitle && VNCommands.TryGet(entry.Command, out VNCommand command) && !command.AllowedInSubtitle)
                    problems.Add($"{script.Id} 第{entry.SourceLine}行：字幕段「{label.Name}」里不能用 [{entry.Command}]");
            }
        }
        if (labels.Count > 0 && labels[0].Index > 0)
            problems.Add($"{script.Id}：第一个标签前面有 {labels[0].Index} 条指令，触发不到");

        foreach (OrphEntry entry in script.Entries)
        {
            string[] args = entry.Args ?? System.Array.Empty<string>();
            switch (entry.Command)
            {
                case SayCommand.NewBlock:
                case SayCommand.SameBlock:
                    if (characters != null && !characters.Knows(args[0]))
                        problems.Add($"{script.Id} 第{entry.SourceLine}行：说话人「{args[0]}」不在角色表里");
                    if (args[0] == "") problems.Add($"{script.Id} 第{entry.SourceLine}行：台词前面没有说话人");
                    break;
                case CgCommand.CommandName:
                    if (settings != null && args[0] != VNCommand.None && !ResourceLoader.Exists(settings.CgPath(args[0])))
                        problems.Add($"{script.Id} 第{entry.SourceLine}行：CG「{args[0]}」没有数据 {settings.CgPath(args[0])}");
                    break;
                case TabletCommand.CommandName:
                    if (settings != null && !ResourceLoader.Exists(settings.TabletPath(args[0])))
                        problems.Add($"{script.Id} 第{entry.SourceLine}行：泥板「{args[0]}」没有数据 {settings.TabletPath(args[0])}");
                    break;
                case "铭文":
                    if (settings != null && !ResourceLoader.Exists(settings.InscriptionPath(args[0])))
                        problems.Add($"{script.Id} 第{entry.SourceLine}行：铭文「{args[0]}」没有数据 {settings.InscriptionPath(args[0])}");
                    break;
            }
        }
        return problems;
    }
}
