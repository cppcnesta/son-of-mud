using System;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[设置 变量 值]：值按 StoryValue.Parse 解析（是/否/true/false → bool，整数 → int，其余 → string）。</summary>
public class SetCommand : VNCommand
{
    public override string Name => "设置";
    public override int MinArgs => 2;
    public override string Usage => "[设置 变量 值]";

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        ctx.Vars.Set(args[0], StoryValue.Parse(args[1]));
        done();
    }
}
