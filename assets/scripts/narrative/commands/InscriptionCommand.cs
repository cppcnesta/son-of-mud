using System;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[铭文 id]：用泥板界面显示一段铭文（壁画、浮雕旁的文字）。不收集、不记变量，只有一个版本。</summary>
public class InscriptionCommand : VNCommand
{
    public override string Name => "铭文";
    public override int MinArgs => 1;
    public override string Usage => "[铭文 id]";
    public override bool EndsDialogueBlock => true;
    public override bool AllowedInSubtitle => false;

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        TabletData data = ctx.LoadInscription(args[0]) ?? throw new InvalidOperationException($"找不到铭文数据 {ctx.Settings.InscriptionPath(args[0])}");
        TabletCommand.Show(ctx, data, false, done);
    }
}
