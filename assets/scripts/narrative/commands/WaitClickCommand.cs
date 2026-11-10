using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [等点击]：停下来等推进键，框里的字保留，下一句接着往下加。
/// 解析器把“同一个人的话中间的空行”变成 [等点击 空行]；设置里 PauseAtBlankLines 关掉时空行不停。
/// 字幕模式里不等。
/// </summary>
public class WaitClickCommand : VNCommand
{
    public const string CommandName = "等点击";
    public const string FromBlankLine = "空行";

    public override string Name => CommandName;
    public override string Usage => "[等点击]";

    public static bool WillWait(string[] args, VNContext ctx)
    {
        if (ctx.Mode == RunnerMode.Subtitle) return false;
        bool blankLine = args != null && args.Length > 0 && args[0] == FromBlankLine;
        return !blankLine || ctx.PauseAtBlankLines;
    }

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        if (Arg(args, 0) == FromBlankLine) ctx.Lines?.BreakParagraph();
        if (WillWait(args, ctx)) ctx.Runner.WaitForAdvance(done);
        else done();
    }
}
