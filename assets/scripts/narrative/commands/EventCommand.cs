using System;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[事件 名字 参数]：剧本通知游戏（开门、给物品……），发 Events.StoryEventRaised(名字, 参数)，立刻继续。</summary>
public class EventCommand : VNCommand
{
    public override string Name => "事件";
    public override int MinArgs => 1;
    public override string Usage => "[事件 名字 参数]";

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        Events.Instance?.EmitSignal(Events.SignalName.StoryEventRaised, args[0], Arg(args, 1));
        done();
    }
}
