using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>[BGM 名字 秒数] / [BGM 无 秒数]：本轮还没有音频系统，只报“未实现”警告，剧本照常继续。</summary>
public class BgmCommand : VNCommand
{
    public override string Name => "BGM";
    public override int MinArgs => 1;
    public override string Usage => "[BGM 名字 淡入秒数] 或 [BGM 无 淡出秒数]";

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        NarrativeLog.Warn($"[剧情] {ctx.Runner.Script?.Id} 第{ctx.Runner.CurrentEntry?.SourceLine ?? 0}行 [BGM {args[0]}]：BGM 还没有实现，已跳过");
        done();
    }
}
