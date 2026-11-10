using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[效果 名字 参数...]：触发舞台效果。实现了 IBlockingStageEffect 的效果要等播完才继续（期间不接受推进键）。</summary>
public class EffectCommand : VNCommand
{
    public override string Name => "效果";
    public override int MinArgs => 1;
    public override string Usage => "[效果 名字 参数]";

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        if (VNRegistry.TryGetEffect(args[0], out IStageEffect effect) && effect is IBlockingStageEffect blocking && !ctx.Runner.AutoAdvance)
        {
            int session = ctx.Session;
            ctx.LockInput();
            blocking.Play(ArgsFrom(args, 1), () =>
            {
                if (!ctx.IsCurrent(session)) return;
                ctx.UnlockInput();
                done();
            });
            return;
        }
        VNEffects.Play(args[0], ArgsFrom(args, 1));
        done();
    }
}
