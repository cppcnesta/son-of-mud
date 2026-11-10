using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[结束]：这一段剧情到此为止（发 Finished）。每个可触发的标签都以 [结束]、[跳转]、[选项] 或 [重开] 收尾。</summary>
public class EndCommand : VNCommand
{
    public const string CommandName = "结束";

    public override string Name => CommandName;
    public override bool EndsDialogueBlock => true;

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        ctx.Runner.EndPlayback();
        done();
    }
}
