using System;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [泥板 id]：拾取泥板。记变量 tablet.&lt;id&gt; = true，发 Events.TabletCollected，打开泥板界面，关掉后继续。
/// 变量 FullAfterVar（默认 truth）为 true、且这块泥板有完整版时显示完整版，否则显示残缺版。
/// </summary>
public class TabletCommand : VNCommand
{
    public const string CommandName = "泥板";
    public const string VarPrefix = "tablet.";

    public override string Name => CommandName;
    public override int MinArgs => 1;
    public override string Usage => "[泥板 id]";
    public override bool EndsDialogueBlock => true;
    public override bool AllowedInSubtitle => false;

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        TabletData data = ctx.LoadTablet(args[0]) ?? throw new InvalidOperationException($"找不到泥板数据 {ctx.Settings.TabletPath(args[0])}");
        ctx.Vars.Set(VarPrefix + args[0], true);
        Events.Instance?.EmitSignal(Events.SignalName.TabletCollected, (StringName)args[0]);
        bool full = data.HasFull && ctx.Vars.Flag(data.FullAfterVar);
        Show(ctx, data, full, done);
    }

    /// <summary>泥板和铭文共用：收起台词，打开阅读界面，关掉后 done。</summary>
    public static void Show(VNContext ctx, TabletData data, bool full, Action done)
    {
        ctx.Lines?.Hide();
        if (ctx.Runner.AutoAdvance || ctx.Tablets == null)
        {
            done();
            return;
        }
        int session = ctx.Session;
        ctx.Tablets.Open(data.Title, data.TextFor(full), full, () =>
        {
            if (ctx.IsCurrent(session)) done();
        });
    }
}
