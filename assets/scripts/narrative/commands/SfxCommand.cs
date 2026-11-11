using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[音效 名字]：播放一次 assets/audio/sfx/sfx_narrative_&lt;名字&gt;.wav（或 .ogg）。还没交付时只报一次警告。</summary>
public class SfxCommand : VNCommand
{
    public override string Name => "音效";
    public override int MinArgs => 1;
    public override string Usage => "[音效 名字]";

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        if (!ctx.Runner.AutoAdvance) ctx.Audio?.PlaySfx(args[0]);
        done();
    }
}
