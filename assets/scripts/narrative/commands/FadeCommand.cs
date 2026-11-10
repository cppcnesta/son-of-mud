using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [转场 淡出 秒数] / [转场 淡入 秒数]：剧情层自己的黑幕（盖住对话框），等转场结束再继续，期间不接受推进键。
/// 和 World 换房用的黑幕是两回事。黑底白字的旁白用 [CG black 0]，不用转场。
/// </summary>
public class FadeCommand : VNCommand
{
    public override string Name => "转场";
    public override bool EndsDialogueBlock => true;
    public override int MinArgs => 1;
    public override string Usage => "[转场 淡出 秒数] 或 [转场 淡入 秒数]";
    public override bool AllowedInSubtitle => false;

    public override string Validate(string[] args)
    {
        string error = base.Validate(args);
        if (error != null) return error;
        return args[0] != "淡出" && args[0] != "淡入" ? "转场只支持「淡出」和「淡入」。用法：" + Usage : null;
    }

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        bool toBlack = args[0] == "淡出";
        if (ctx.Fade == null || ctx.Runner.AutoAdvance)
        {
            ctx.Fade?.SetBlack(toBlack);
            done();
            return;
        }
        int session = ctx.Session;
        ctx.LockInput();
        ctx.Fade.FadeTo(toBlack, FloatArg(args, 1, 0.5f), () =>
        {
            if (!ctx.IsCurrent(session)) return;  // 转场期间重新播放了：输入锁已经重置过
            ctx.UnlockInput();
            done();
        });
    }
}
