using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [重开]：结局 B 用。这一段剧情就此结束；剧情层收起界面、释放暂停后发 Events.RestartRunRequested，
/// 由 World 重建整个游戏实例、从 S1_01 重新开始。剧情系统不发 NewGameRequested。
/// </summary>
public class RestartCommand : VNCommand
{
    public const string CommandName = "重开";

    public override string Name => CommandName;
    public override bool EndsDialogueBlock => true;
    public override bool AllowedInSubtitle => false;

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        ctx.RestartRequested = true;
        ctx.Runner.EndPlayback();
        done();
    }
}
