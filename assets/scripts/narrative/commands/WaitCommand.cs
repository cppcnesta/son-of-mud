using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[等待 秒数]：停一会儿再继续（游戏暂停时也照常计时）。</summary>
public class WaitCommand : VNCommand
{
    public override string Name => "等待";
    public override int MinArgs => 1;
    public override string Usage => "[等待 秒数]";

    public override string Validate(string[] args)
    {
        string error = base.Validate(args);
        if (error != null) return error;
        return FloatArg(args, 0, -1f) < 0f ? "秒数要写成数字。用法：" + Usage : null;
    }

    public override void Execute(string[] args, VNContext ctx, Action done) =>
        ctx.Runner.After(ctx.Runner.AutoAdvance ? 0 : FloatArg(args, 0, 1f), done);
}
