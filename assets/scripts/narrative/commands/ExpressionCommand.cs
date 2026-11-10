using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[表情 角色 表情]：不说话时单独换表情（下次这个角色说话时立绘用这个表情）。</summary>
public class ExpressionCommand : VNCommand
{
    public override string Name => "表情";
    public override int MinArgs => 2;
    public override string Usage => "[表情 角色 表情]";

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        ctx.Expressions[ctx.CanonicalName(args[0])] = args[1];
        done();
    }
}
