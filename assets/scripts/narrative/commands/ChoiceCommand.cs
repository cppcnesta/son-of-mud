using System;
using System.Collections.Generic;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// 选项（本作新增）：
/// <code>
/// [选项 choice.供桌]
/// - 离开 -> 供桌·离开
/// - 吃下 -> 供桌·吃下
/// </code>
/// 下面连续的「- 文本 -> 标签」行属于它，至少两项，每个标签解析时检查是否存在。
/// 选中后：变量（写了才存）= 选项文本（按 StoryValue.Parse 解析），然后跳到对应标签。
/// 和上一句台词一起出现（台词打完字就显示选项）。参数存成：变量, 文本1, 标签1, 文本2, 标签2……
/// </summary>
public class ChoiceCommand : VNCommand
{
    public const string CommandName = "选项";

    public override string Name => CommandName;
    public override bool EndsDialogueBlock => true;
    public override bool CollectsOptions => true;
    public override bool ShowsWithPreviousLine => true;
    public override bool AllowedInSubtitle => false;
    public override string Usage => "[选项 变量]，下面每行写「- 文本 -> 标签」，至少两项";

    public override string ValidateHeader(string[] args) =>
        args.Length > 1 ? "选项只能写一个变量名。用法：" + Usage : null;

    public override string Validate(string[] args)
    {
        if (args.Length < 1 || (args.Length - 1) % 2 != 0) return "选项格式不对。用法：" + Usage;
        if (args.Length < 5) return "选项至少要有两项。用法：" + Usage;
        return null;
    }

    public override IEnumerable<string> LabelArgs(string[] args)
    {
        for (int i = 2; i < args.Length; i += 2) yield return args[i];
    }

    public static string[] Texts(string[] args)
    {
        var texts = new string[(args.Length - 1) / 2];
        for (int i = 0; i < texts.Length; i++) texts[i] = args[1 + 2 * i];
        return texts;
    }

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        string[] texts = Texts(args);
        int session = ctx.Session;
        bool picked = false;
        void Pick(int index)
        {
            if (picked || !ctx.IsCurrent(session)) return;
            if (index < 0 || index >= texts.Length) return;
            picked = true;
            ctx.Choice?.Close();
            if (!string.IsNullOrEmpty(args[0])) ctx.Vars.Set(args[0], StoryValue.Parse(texts[index]));
            ctx.Runner.JumpTo(args[2 + 2 * index]);
            done();
        }

        if (ctx.Runner.AutoAdvance || ctx.Choice == null)
        {
            Pick(ctx.Runner.AutoChoose?.Invoke(texts) ?? 0);
            return;
        }
        ctx.Choice.Open(texts, Pick);
    }
}
