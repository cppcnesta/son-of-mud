using System;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [CG id 淡入秒数]：显示一张 CG（盖在世界上面、对话框下面），剧本马上继续，CG 上叠台词。
/// [CG id 淡入秒数 单独展示秒数]：先收起对话框，让画面单独停这么久再继续。
/// [CG 无 淡出秒数]：关掉 CG。CG 数据在 assets/data/narrative/cg/cg_&lt;id&gt;.tres。
/// </summary>
public class CgCommand : VNCommand
{
    public const string CommandName = "CG";

    public override string Name => CommandName;
    public override int MinArgs => 1;
    public override string Usage => "[CG id 淡入秒数] 或 [CG id 淡入秒数 单独展示秒数] 或 [CG 无 淡出秒数]";
    public override bool AllowedInSubtitle => false;

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        float fade = FloatArg(args, 1, 0.5f);
        if (args[0] == None)
        {
            ctx.Cg?.Hide(ctx.Runner.AutoAdvance ? 0f : fade);
            done();
            return;
        }
        CgData data = ctx.LoadCg(args[0]) ?? throw new InvalidOperationException($"找不到 CG 数据 {ctx.Settings.CgPath(args[0])}");
        ctx.Cg?.Show(data, ctx.Runner.AutoAdvance ? 0f : fade);
        float alone = FloatArg(args, 2, 0f);
        if (alone <= 0f || ctx.Runner.AutoAdvance)
        {
            done();
            return;
        }
        ctx.Lines?.Hide();
        int session = ctx.Session;
        ctx.LockInput();
        ctx.Runner.After(fade + alone, () =>
        {
            if (!ctx.IsCurrent(session)) return;
            ctx.UnlockInput();
            done();
        });
    }
}
