using System;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [玩法 类型 参数 变量]：把控制权交给一段玩法（VNRegistry 里登记的 IGameplaySegment），等它结束再继续。
/// 结果按 StoryValue.Parse 存进变量；变量不写就用类型名。期间对话框收起、不接受推进键。
/// 只认当前这一场的第一个结果：重复回调、上一场的迟到结果都忽略。
/// </summary>
public class GameplayCommand : VNCommand
{
    public const string CommandName = "玩法";

    public override string Name => CommandName;
    public override bool EndsDialogueBlock => true;
    public override int MinArgs => 1;
    public override string Usage => "[玩法 类型 参数 变量]";
    public override bool AllowedInSubtitle => false;

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        string type = args[0];
        string variable = Arg(args, 2, type);
        if (!VNRegistry.TryGetSegment(type, out IGameplaySegment segment)) throw new InvalidOperationException("找不到玩法：" + type);
        if (ctx.ActiveSegment != null) throw new InvalidOperationException("已有未结束的玩法");

        ctx.Lines?.Hide();
        ctx.ActiveSegment = segment;
        ctx.LockInput();
        ctx.SegmentHoldsInputLock = true;

        int session = ctx.Session;
        bool completed = false;
        try
        {
            segment.Begin(Arg(args, 1), result =>
            {
                if (completed || !ctx.IsCurrent(session) || ctx.ActiveSegment != segment) return;
                completed = true;
                ctx.Vars.Set(variable, StoryValue.Parse(result ?? ""));
                ctx.ActiveSegment = null;
                ctx.ReleaseSegmentInputLock();
                done();
            });
        }
        catch
        {
            completed = true;
            segment.Cancel();
            ctx.ActiveSegment = null;
            ctx.ReleaseSegmentInputLock();
            throw;
        }
    }
}
