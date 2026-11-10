using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SonOfMud.Narrative.Commands;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Parsing;

/// <summary>
/// .orph 剧本解析器（OrpheusVN 导入器第 1–7 步，原样搬过来，在运行时执行）。
///
/// 语法：
///   * 开头的一行           备注，跳过
///   # 标题 / ## 标题       标签，可以跳转到这里
///   名字：/ 名字（表情）：  换说话人
///   [命令 参数 参数]        命令
///   [选项 变量] + 下面连续的「- 文本 -> 标签」行   选项（本作新增）
///   其他的每一行            一句台词
///
/// 台词怎么分段：同一个人连着说的几句是一段，第一句是“说”，后面是“接着说”；
/// 换说话人、标签、会打断台词的命令（跳转、选项、结束……）→ 新的一段；一段中间的空行 → [等点击 空行]。
/// 写错的命令（不认识、参数不对、找不到标签、本作不支持）记进 Errors 并带行号，不写进剧本。
/// </summary>
public static class OrphParser
{
    public static OrphScript Parse(string text, string id)
    {
        var script = new OrphScript(id);
        var errors = new List<(int line, string message)>();
        string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        string speaker = "";
        string expression = "";
        bool inBlock = false;   // 正处在同一个人的一段台词里（下一句是“接着说”）
        int blankAt = -1;       // 段中遇到空行时，[等点击] 将来要插在第几条指令；-1 表示没有
        int blankLine = 0;

        // 正在收集选项的 [选项] 命令
        VNCommand choiceCommand = null;
        List<string> choiceArgs = null;
        int choiceLine = 0;

        void EndBlock()
        {
            inBlock = false;
            blankAt = -1;
        }

        void FinishChoice()
        {
            if (choiceArgs == null) return;
            string[] full = choiceArgs.ToArray();
            string error = choiceCommand.Validate(full);
            if (error != null) errors.Add((choiceLine, $"第{choiceLine}行：{error}"));
            else script.Add(choiceCommand.Name, full, choiceLine);
            choiceCommand = null;
            choiceArgs = null;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            string s = lines[i].Trim();
            if (i == 0) s = s.TrimStart('﻿').Trim();

            // 1. 备注
            if (s.StartsWith("*")) continue;

            // 2. 空行：在一段话中间的话，记下位置，等确定后面还是这个人在说，再插入 [等点击]。
            if (s.Length == 0)
            {
                if (choiceArgs == null && inBlock && blankAt < 0)
                {
                    blankAt = script.Entries.Count;
                    blankLine = lineNo;
                }
                continue;
            }

            // 选项行：只能跟在 [选项] 后面
            if (OrphSyntax.LooksLikeOption(s))
            {
                if (choiceArgs == null)
                {
                    errors.Add((lineNo, $"第{lineNo}行：选项行「{s}」前面没有 [选项]"));
                    continue;
                }
                Match option = OrphSyntax.ChoiceLine.Match(s);
                if (!option.Success)
                {
                    errors.Add((lineNo, $"第{lineNo}行：选项要写成「- 文本 -> 标签」"));
                    continue;
                }
                choiceArgs.Add(option.Groups["text"].Value.Trim());
                choiceArgs.Add(CleanLabel(option.Groups["label"].Value));
                continue;
            }
            FinishChoice();

            // 3. 标签
            if (s.StartsWith("#"))
            {
                string label = CleanLabel(s);
                if (label.Length == 0) errors.Add((lineNo, $"第{lineNo}行：标签没有名字"));
                else if (script.FindLabel(label) >= 0) errors.Add((lineNo, $"第{lineNo}行：标签「{label}」重复了，跳转只会跳到第一个"));
                else script.AddLabel(label, lineNo);
                speaker = "";
                expression = "";
                EndBlock();
                continue;
            }

            // 4. 命令
            if (s.StartsWith("[") && !s.EndsWith("]"))
            {
                errors.Add((lineNo, $"第{lineNo}行：命令少了右括号 ]"));
                continue;
            }
            if (s.StartsWith("[") && s.EndsWith("]"))
            {
                string[] parts = s.Substring(1, s.Length - 2).Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    errors.Add((lineNo, $"第{lineNo}行：空的命令 []"));
                    continue;
                }
                string name = parts[0];
                string[] args = parts.Skip(1).ToArray();
                if (!VNCommands.TryGet(name, out VNCommand command))
                {
                    errors.Add((lineNo, $"第{lineNo}行：不认识的命令「{name}」"));
                    continue;
                }
                if (command.CollectsOptions)
                {
                    string headerError = command.ValidateHeader(args);
                    if (headerError != null)
                    {
                        errors.Add((lineNo, $"第{lineNo}行：{headerError}"));
                        continue;
                    }
                    choiceCommand = command;
                    choiceArgs = new List<string> { args.Length > 0 ? args[0] : "" };
                    choiceLine = lineNo;
                    if (command.EndsDialogueBlock) EndBlock();
                    continue;
                }
                string error = command.Validate(args);
                if (error != null)
                {
                    errors.Add((lineNo, $"第{lineNo}行：{error}"));
                    continue;
                }
                script.Add(name, args, lineNo);
                if (command.EndsDialogueBlock) EndBlock();
                continue;
            }

            // 5. 说话人
            Match match = OrphSyntax.SpeakerLine.Match(s);
            if (match.Success)
            {
                speaker = match.Groups["name"].Value.Trim();
                expression = match.Groups["expr"].Success ? match.Groups["expr"].Value.Trim() : "";
                EndBlock();
                continue;
            }

            // 6. 台词
            if (speaker == "") script.Warnings.Add($"第{lineNo}行：这句台词前面没有说话人");
            if (inBlock)
            {
                if (blankAt >= 0)
                    script.Insert(blankAt, WaitClickCommand.CommandName, new[] { WaitClickCommand.FromBlankLine }, blankLine);
                script.Add(SayCommand.SameBlock, new[] { speaker, expression, s }, lineNo);
            }
            else
            {
                script.Add(SayCommand.NewBlock, new[] { speaker, expression, s }, lineNo);
            }
            inBlock = true;
            blankAt = -1;
            expression = "";  // 表情只在这个人说的第一句切换
        }
        FinishChoice();

        // 7. 检查跳转目标是否存在。找不到的跳转不写进剧本。
        for (int i = script.Entries.Count - 1; i >= 0; i--)
        {
            OrphEntry entry = script.Entries[i];
            if (!VNCommands.TryGet(entry.Command, out VNCommand command)) continue;
            string missing = command.LabelArgs(entry.Args).FirstOrDefault(target => !string.IsNullOrEmpty(target) && script.FindLabel(target) < 0);
            if (missing == null) continue;
            errors.Add((entry.SourceLine, $"第{entry.SourceLine}行：找不到标签「{missing}」"));
            script.RemoveEntry(i);
        }

        foreach (var (_, message) in errors.OrderBy(e => e.line)) script.Errors.Add(message);
        return script;
    }

    /// <summary>“### 假结局：” → “假结局”。</summary>
    private static string CleanLabel(string raw) => raw.Trim().TrimStart('#').Trim().TrimEnd('：', ':').Trim();

    /// <summary>从“第12行：……”里取出行号；没有返回 -1。</summary>
    public static int LineOf(string message)
    {
        Match m = Regex.Match(message ?? "", @"第(\d+)行");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }
}
