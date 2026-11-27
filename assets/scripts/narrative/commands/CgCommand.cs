using System;
using SonOfMud.Core;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Narrative.Views;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [CG id 淡入秒数]：显示一张 CG（盖在世界上面、对话框下面），剧本马上继续，CG 上叠台词。
/// [CG id 淡入秒数 单独展示秒数]：先收起对话框，让画面单独停这么久再继续。
/// [CG 无 淡出秒数]：关掉 CG。CG 数据在 assets/data/narrative/cg/cg_&lt;id&gt;.tres。
/// 动画 CG（CG 数据里填了 AnimatedScene）：收起对话框、从头播到尾（不能跳过，台词是场景里的字幕），播完才继续往下走。
/// CG 真的显示出来时记进主页面「收集」（CollectionRecord.MarkCg）：静态 CG 交给 CG 层之后、动画 CG 开播成功之后；
/// 自动推进、没有 CG 层、找不到 CG 数据、动画开播失败都不记。
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
        if (data.AnimatedScene != null)
        {
            PlayAnimated(data, ctx.Runner.AutoAdvance ? 0f : fade, ctx, done);
            return;
        }
        ctx.Cg?.Show(data, ctx.Runner.AutoAdvance ? 0f : fade);
        if (ctx.Cg != null && !ctx.Runner.AutoAdvance) CollectionRecord.MarkCg(data.Id);
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

    /// <summary>超过总时长这么多秒还没播完，就当播放失败收尾（保险，正常不会用到）。</summary>
    public const double TimeoutMargin = 5.0;

    /// <summary>
    /// 动画 CG：收起对话框、锁住推进键，登记成 ActiveSegment，整段播完才 done()。
    /// 正常播完、播放失败、超时都走同一个只执行一次的收尾 Finish（和 GameplayCommand 一样的三重检查：
    /// 已经收尾过 / 场次变了 / ActiveSegment 已经不是这一次 → 什么都不做），所以过期的回调不会动到后来的剧情。
    /// 自动推进（测试快速跳过剧情）时直接当作播完。
    /// </summary>
    private static void PlayAnimated(CgData data, float fade, VNContext ctx, Action done)
    {
        if (ctx.Runner.AutoAdvance || ctx.Cg == null)
        {
            if (ctx.Cg == null) NarrativeLog.Warn($"[剧情] 没有 CG 层，动画 CG {data.Id} 跳过");
            done();
            return;
        }
        if (ctx.ActiveSegment != null) throw new InvalidOperationException("已有未结束的玩法或动画 CG");

        // ① 先准备好，还不开始播
        var playback = new CgPlayback(ctx.Cg, data, fade);
        int session = ctx.Session;
        bool completed = false;
        // ② 登记、上锁
        ctx.Lines?.Hide();
        ctx.ActiveSegment = playback;
        ctx.LockInput();
        ctx.SegmentHoldsInputLock = true;

        // ③ 唯一的收尾入口
        void Finish(bool ok, string reason)
        {
            if (completed || !ctx.IsCurrent(session) || ctx.ActiveSegment != playback) return;
            completed = true;
            if (!ok)
            {
                NarrativeLog.Error($"[剧情] 动画 CG {data.Id} {reason}，跳过继续");
                playback.Cancel();   // 只取消这一次的播放对象
            }
            ctx.ActiveSegment = null;
            ctx.ReleaseSegmentInputLock();
            done();
        }

        // ④ 最后才开始播
        try
        {
            playback.Begin(data.Id, _ => Finish(true, null));
        }
        catch (Exception e)
        {
            Finish(false, "播放失败：" + e.Message);
            return;
        }
        CollectionRecord.MarkCg(data.Id);   // 开播成功才算解锁
        // ⑤ 超时保险：正常播完后 completed 已经是 true，这个回调走到第一行就返回
        ctx.Runner.After(playback.Duration + fade + TimeoutMargin, () => Finish(false, "超时没有播完"));
    }
}
