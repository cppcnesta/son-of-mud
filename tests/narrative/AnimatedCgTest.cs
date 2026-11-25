using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Narrative.Views;
using SonOfMud.World;

/// <summary>
/// 动画 CG（CG4「流星」，走真实播放时间，不用自动推进）：
/// ① 从 S2_05 碰雕像触发：放出动画场景，对话框不出现；播放中执行器一直停在 CG 指令上（ActiveSegment 是这次播放）；
///    按推进键不会跳；11 句字幕按顺序出现，文字和说话人对得上原剧本（最后两段仍是旁白）；BGM 一直在放。
/// ② 结束：总时间轴播完（不早于最后一句结束）才继续，剧情结束后剧情输入锁为 0、玩家能动、CG 实例已释放。
/// ③ 中途停止：实例马上释放、锁清零；等过原来的总时长，旧播放没有推进任何东西；再播一次从第 1 句开始、正常播完。
/// ④ 过期超时：2 秒的测试动画 CG 播完后，同一场剧情接着单独展示静态 CG 9 秒；越过第一段的超时期限（7 秒）时，
///    第二段的锁、显示的 CG 都没被动过，之后正常结束，没有“超时”报错。
/// </summary>
public partial class AnimatedCgTest : NarrativeFlowBase
{
    protected override string SuiteName => "animated_cg";

    // 原剧本 t_site.orph「流星」里的 11 句（迁到动画字幕之前的文字和说话人）
    private static readonly (string speaker, string text)[] Original =
    {
        ("旁白", "在第二个世界的某一天夜里，祂依旧在重塑他的身体，那是十年中的某一个瞬间，有一颗星从天上划过。"),
        ("穆尔图库尔", "那是什么？"),
        ("伊姆杜", "星。"),
        ("穆尔图库尔", "它为什么会掉下来？"),
        ("伊姆杜", "星是神写在天上的命运。写完了，就会落下来。"),
        ("穆尔图库尔", "那您写的是什么？"),
        ("旁白", "父亲看了那颗星很久。他把手放在我的头上，星从天这头落到那头。然后他收回手，站起来。"),
        ("伊姆杜", "穆尔图库尔，闭上眼睛吧。"),
        ("旁白", "父亲继续修复我破碎的躯体，时隔一个百年，我再次感受他的心跳和手指的纹路。我希望这一刻像永远一样久。"),
        ("旁白", "然而就如同那颗星从天这头落到那头。他的手在我头上，停了那么久。"),
        ("旁白", "只有那么久。"),
    };

    private VNContext Ctx => Narr.MainContext;
    private BgmPlayer Music => MainGame?.GetNodeOrNull<BgmPlayer>("Music");

    protected override async Task RunChecks()
    {
        Listen();
        try
        {
            bool started = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            await WaitSettled();
            Check(started && Narr != null, "正式入口开新游戏");
            if (Narr == null) return;
            await MeteorChecks();
            await CancelChecks();
            await StaleTimeoutChecks();
        }
        finally { Unlisten(); }
    }

    // ---------- ①② 真实播放 ----------

    private async Task MeteorChecks()
    {
        await Teleport("S2_05", SpawnPoint.DefaultId);
        await Frames(30);
        var trigger = World.CurrentRoom.FindChild("Story_Meteor", true, false) as Node2D;
        Check(trigger != null, "S2_05 有流星雕像");
        if (trigger == null) return;
        Player.GlobalPosition = trigger.GlobalPosition;
        await Frames(10);
        await KeyTap(Key.E);
        bool appeared = await WaitUntil(() => Narr.Cg.Animated != null, 120);
        AnimatedCg cg = Narr.Cg.Animated;
        Check(appeared && cg != null && Narr.MainActive && Narr.Cg.CurrentId == "meteor", "碰流星雕像：放出动画 CG（cg_meteor）");
        if (cg == null) return;

        var seen = new List<(string speaker, string text, float at)>();
        bool dialogShown = false, segmentHeld = true, bgmPlaying = true, skipped = false;
        var checkpoints = new List<string>();
        double duration = cg.Duration;
        int maxFrames = (int)((duration + 10) * 60);
        double lastPos = 0;
        for (int f = 0; f < maxFrames && Narr.MainActive && IsInstanceValid(cg) && cg.Playing; f++)
        {
            await Frames(1);
            if (!IsInstanceValid(cg) || !cg.Playing) break;
            double pos = cg.Position;
            dialogShown |= Narr.Dialog.IsOpen;
            segmentHeld &= Ctx.ActiveSegment is CgPlayback p && p.Instance == cg && Ctx.InputLocked;
            bgmPlaying &= Music != null && Music.CurrentStream != null && Music.IsPlaying(Music.CurrentStream);
            if (cg.ShownIndex >= 0 && (seen.Count == 0 || seen[^1].text != cg.Line.Text))
                seen.Add((cg.Who.Text == "" ? CgSubtitle.Narrator : cg.Who.Text, cg.Line.Text, (float)pos));
            foreach (double mark in new[] { 5.0, duration / 2, duration - 2 })
                if (lastPos < mark && pos >= mark) checkpoints.Add($"{mark:0}s");
            // 在第 10、20 秒连按推进键：不能跳
            if (lastPos < 10 && pos >= 10 || lastPos < 20 && pos >= 20)
            {
                double before = cg.Position;
                int index = cg.ShownIndex;
                for (int i = 0; i < 3; i++) await KeyTap(Key.E);
                skipped |= cg.Position - before > 0.3 || cg.ShownIndex > index + 1 || !cg.Playing;
            }
            lastPos = pos;
        }
        double endPos = cg.Finished ? cg.Duration : cg.Position;
        Check(checkpoints.Count == 3 && segmentHeld && !dialogShown,
            $"播放中（{string.Join("、", checkpoints)} 检查）：执行器一直停在 CG 指令上（ActiveSegment 是这次播放、推进键锁着），对话框没出现");
        Check(!skipped, "播放中按推进键：不会跳到后面");
        bool same = seen.Count == Original.Length && seen.Select((s, i) => s.speaker == Original[i].speaker && s.text == Original[i].text).All(x => x);
        Check(same, $"11 句字幕按顺序出现，文字和说话人对得上原剧本（实际 {seen.Count} 句：{string.Join(" / ", seen.Select(s => s.speaker + "：" + (s.text.Length > 6 ? s.text[..6] : s.text)))}）");
        Check(bgmPlaying, "整段播放中 BGM 一直在放");
        float lastEnd = cg.Subtitles[^1].End;
        Check(cg.Finished && endPos >= lastEnd, $"总时间轴播完（{endPos:0.0} 秒，最后一句到 {lastEnd:0.0} 秒结束）才继续");
        bool ended = await WaitUntil(() => !Narr.MainActive, 300);
        await WaitUntil(() => !PauseService.IsPaused, 120);
        await Frames(30);
        Check(ended && !Ctx.InputLocked && Ctx.ActiveSegment == null && !IsInstanceValid(cg) && Narr.Cg.Animated == null && NoPauseOrLock(),
            "剧情结束：剧情输入锁为 0，没有残留的播放，CG 实例已释放，玩家能动");
    }

    // ---------- ③ 中途停止再重播 ----------

    private async Task CancelChecks()
    {
        Request("t_site", "流星");
        await WaitUntil(() => Narr.Cg.Animated != null, 120);
        AnimatedCg first = Narr.Cg.Animated;
        await WaitUntil(() => first != null && first.Position > 8, 900);
        int endedBefore = Ended.Count;
        double duration = first.Duration;
        Narr.StopMain();
        await Frames(2);
        Check(!IsInstanceValid(first) && Narr.Cg.Animated == null && !Ctx.InputLocked && Ctx.ActiveSegment == null && !Narr.MainActive,
            "中途停止：CG 实例马上释放，剧情输入锁清零，没有残留的播放");
        await Frames((int)((duration + CgCommandMargin) * 60));
        Check(!Narr.MainActive && Narr.Cg.Animated == null && Ended.Count == endedBefore + 1,
            "等过原来的总时长和超时期限：旧播放没有推进任何东西（只有停止时的那一次结束）");

        Request("t_site", "流星");
        await WaitUntil(() => Narr.Cg.Animated != null, 120);
        AnimatedCg again = Narr.Cg.Animated;
        int firstIndex = -2;
        await WaitUntil(() => { if (again.ShownIndex >= 0 && firstIndex == -2) firstIndex = again.ShownIndex; return firstIndex != -2; }, 600);
        bool finished = await WaitUntil(() => !Narr.MainActive, (int)((duration + 10) * 60));
        Check(again != first && firstIndex == 0 && finished && !Ctx.InputLocked, "再播一次：从第 1 句开始，完整播完正常结束");
        await WaitUntil(() => !PauseService.IsPaused, 120);
    }

    private const double CgCommandMargin = SonOfMud.Narrative.Commands.CgCommand.TimeoutMargin + 1;

    // ---------- ④ 过期的超时回调不影响同一场里后面的剧情 ----------

    private async Task StaleTimeoutChecks()
    {
        LoadTestScript();
        string oldDir = Narr.Settings.CgDirectory;
        Narr.Settings.CgDirectory = "res://tests/narrative/fixtures/cg";
        int errors = NarrativeLog.ErrorCount;
        try
        {
            Request("test_story", "动画CG过期超时");
            bool shortDone = await WaitUntil(() => Narr.Cg.CurrentId == "test_static", 300);
            Check(shortDone && Ctx.InputLocked && Narr.MainActive, "2 秒的测试动画 CG 播完，同一场剧情接着单独展示静态 CG（锁着推进键）");
            await Frames((int)(6.5 * 60));   // 从开始算约 8.5 秒：已经越过第一段的超时期限（7 秒）
            Check(Narr.MainActive && Ctx.InputLocked && Narr.Cg.CurrentId == "test_static" && Narr.Cg.Visible && Ctx.ActiveSegment == null
                  && NarrativeLog.ErrorCount == errors,
                "越过第一段的超时期限：第二段的锁还在、静态 CG 还在显示，没有“超时”报错");
            bool ended = await WaitUntil(() => !Narr.MainActive, 300);
            Check(ended && !Ctx.InputLocked && NarrativeLog.ErrorCount == errors, "第二段到时间后照常继续、正常结束");
        }
        finally
        {
            Narr.Settings.CgDirectory = oldDir;
        }
    }
}
