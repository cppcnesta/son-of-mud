using System;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Parsing;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// 台词（解析器自动生成，不用手写）。参数：说话人、表情（可以为空）、台词。
///   “说”     开始新的一段：清空对话框，显示名牌。
///   “接着说” 加在这一段后面。
/// 对话框模式：一句打完字后，下一句还是这一段的 → 停 LineGap 秒自动出下一句；否则等推进键。
///   下一条是选项这类“和台词一起出现”的命令时，打完字就直接继续。
/// 字幕模式：打完字停留一会儿（按字数）自动继续，不等推进键。
/// </summary>
public class SayCommand : VNCommand
{
    public const string NewBlock = "说";
    public const string SameBlock = "接着说";

    public override string Name => NewBlock;
    public override int MinArgs => 3;
    public override string Usage => "（由解析器自动生成：说话人、表情、台词）";

    protected virtual bool AppendToBlock => false;

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        string speaker = args[0];
        string expression = args[1];
        string text = args[2];

        CharacterData character = ctx.Characters?.Find(speaker);
        string canonical = ctx.CanonicalName(speaker);
        string displayName = ctx.DisplayNameFor(speaker);
        if (!string.IsNullOrEmpty(expression)) ctx.Expressions[canonical] = expression;
        ctx.Expressions.TryGetValue(canonical, out string currentExpression);

        ILineSurface lines = ctx.Lines;
        if (lines == null)
        {
            done();
            return;
        }
        bool append = AppendToBlock && lines.CanAppend(displayName);
        if (append) lines.AppendLine(text);
        else lines.ShowLine(displayName, text, character, currentExpression ?? "");

        VNRunner runner = ctx.Runner;
        if (runner.AutoAdvance)
        {
            lines.CompleteTyping();
            done();
            return;
        }
        if (ctx.Mode == RunnerMode.Subtitle)
        {
            runner.WhenTrue(() => !lines.IsTyping, () => runner.After(ctx.SubtitleHold(text), done));
            return;
        }

        OrphEntry next = runner.PeekNextLine();
        if (next == null || next.Command == NewBlock)
        {
            if (next == null && runner.NextShowsWithLine()) runner.WhenTrue(() => !lines.IsTyping, done);
            else runner.WaitForAdvance(done);
        }
        else if (next.Command == WaitClickCommand.CommandName && WaitClickCommand.WillWait(next.Args, ctx))
        {
            // 后面紧跟着 [等点击]：打完字就交给它去等。
            runner.WhenTrue(() => !lines.IsTyping, done);
        }
        else
        {
            // 下一句还是这一段的：打完字停一小会儿自动继续；玩家按过推进键（Rushing）就不停。
            // 但下一句接上去框里放不下（会翻页）时，先等推进键，让玩家看完这一页。
            string nextText = next.Args.Length > 2 ? next.Args[2] : "";
            runner.WhenTrue(() => !lines.IsTyping, () =>
            {
                if (!lines.FitsAppend(nextText)) runner.WaitForAdvance(done);
                else runner.WaitAtMost(ctx.LineGap, () => lines.Rushing, done);
            });
        }
    }
}
