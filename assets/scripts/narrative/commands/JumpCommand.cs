using System;
using System.Collections.Generic;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[跳转 标签]</summary>
public class JumpCommand : VNCommand
{
    public const string CommandName = "跳转";

    public override string Name => CommandName;
    public override bool EndsDialogueBlock => true;
    public override int MinArgs => 1;
    public override string Usage => "[跳转 标签]";
    public override IEnumerable<string> LabelArgs(string[] args) => new[] { Arg(args, 0) };

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        ctx.Runner.JumpTo(args[0]);
        done();
    }
}
